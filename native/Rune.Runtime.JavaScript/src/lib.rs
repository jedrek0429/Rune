use std::collections::HashMap;
use std::error::Error;
use std::fmt;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use rquickjs::function::Func;
use rquickjs::{BigInt, CatchResultExt, Context, Ctx, Function, Null, Object, Promise, Runtime};
use serde::Deserialize;

const DEFAULT_MEMORY_BYTES: usize = 16 * 1024 * 1024;
const DEFAULT_STACK_BYTES: usize = 512 * 1024;
const DEFAULT_DEADLINE: Duration = Duration::from_millis(100);
const DEFAULT_MAX_ACTIONS: usize = 16;
const DEFAULT_MAX_REPLY_BYTES: usize = 8 * 1024;
const DEFAULT_MAX_OUTPUT_BYTES: usize = 64 * 1024;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum EventType {
    MessageCreate,
    MessageDelete,
    MessageReactionAdd,
    MessageReactionRemove,
}

impl EventType {
    const fn parameter(self) -> &'static str {
        match self {
            Self::MessageCreate => "message",
            Self::MessageDelete | Self::MessageReactionAdd | Self::MessageReactionRemove => "args",
        }
    }
}

#[derive(Clone, Debug)]
pub struct JavaScriptLimits {
    pub memory_bytes: usize,
    pub stack_bytes: usize,
    pub deadline: Duration,
    pub max_actions: usize,
    pub max_reply_bytes: usize,
    pub max_output_bytes: usize,
}

impl Default for JavaScriptLimits {
    fn default() -> Self {
        Self {
            memory_bytes: DEFAULT_MEMORY_BYTES,
            stack_bytes: DEFAULT_STACK_BYTES,
            deadline: DEFAULT_DEADLINE,
            max_actions: DEFAULT_MAX_ACTIONS,
            max_reply_bytes: DEFAULT_MAX_REPLY_BYTES,
            max_output_bytes: DEFAULT_MAX_OUTPUT_BYTES,
        }
    }
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct JavaScriptAction {
    pub content: String,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum JavaScriptFailureKind {
    InvalidSource,
    NotFound,
    WrongEvent,
    Deadline,
    OutputLimit,
    Runtime,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct JavaScriptFailure {
    pub kind: JavaScriptFailureKind,
    pub detail: String,
}

impl JavaScriptFailure {
    fn new(kind: JavaScriptFailureKind, detail: impl Into<String>) -> Self {
        Self {
            kind,
            detail: detail.into(),
        }
    }

    fn runtime(detail: impl Into<String>) -> Self {
        Self::new(JavaScriptFailureKind::Runtime, detail)
    }
}

impl fmt::Display for JavaScriptFailure {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        self.detail.fmt(formatter)
    }
}

impl Error for JavaScriptFailure {}

impl From<rquickjs::Error> for JavaScriptFailure {
    fn from(error: rquickjs::Error) -> Self {
        Self::runtime(error.to_string())
    }
}

#[derive(Clone)]
struct RegisteredScript {
    event_type: EventType,
    source: Arc<str>,
}

pub struct SharedJavaScriptRuntime {
    runtime: Runtime,
    interpreter: Mutex<()>,
    scripts: Mutex<HashMap<u128, RegisteredScript>>,
    limits: JavaScriptLimits,
}

impl SharedJavaScriptRuntime {
    /// Creates one shared `QuickJS` runtime with the supplied resource limits.
    ///
    /// # Errors
    ///
    /// Returns an error when `QuickJS` cannot initialise.
    pub fn new(limits: JavaScriptLimits) -> Result<Self, JavaScriptFailure> {
        let runtime = Runtime::new().map_err(|error| {
            JavaScriptFailure::runtime(format!("QuickJS could not be created: {error}"))
        })?;
        runtime.set_memory_limit(limits.memory_bytes);
        runtime.set_max_stack_size(limits.stack_bytes);

        Ok(Self {
            runtime,
            interpreter: Mutex::new(()),
            scripts: Mutex::new(HashMap::new()),
            limits,
        })
    }

    /// Parses and atomically stores a rune's source without executing it.
    ///
    /// # Errors
    ///
    /// Returns an error when the source is invalid or the registry is unavailable.
    pub fn register(
        &self,
        rune_id: u128,
        event_type: EventType,
        source: impl Into<Arc<str>>,
    ) -> Result<(), JavaScriptFailure> {
        let source = source.into();
        self.validate(event_type, &source)?;

        self.scripts
            .lock()
            .map_err(|_| JavaScriptFailure::runtime("the JavaScript registry lock is poisoned"))?
            .insert(rune_id, RegisteredScript { event_type, source });
        Ok(())
    }

    /// Removes one registered rune and reports whether it existed.
    ///
    /// # Errors
    ///
    /// Returns an error when the registry is unavailable.
    pub fn remove(&self, rune_id: u128) -> Result<bool, JavaScriptFailure> {
        Ok(self
            .scripts
            .lock()
            .map_err(|_| JavaScriptFailure::runtime("the JavaScript registry lock is poisoned"))?
            .remove(&rune_id)
            .is_some())
    }

    /// Executes a rune in a fresh context belonging to the shared interpreter.
    ///
    /// # Errors
    ///
    /// Returns a typed failure for invalid routing, resource exhaustion, output
    /// limits, or JavaScript exceptions.
    pub fn invoke(
        &self,
        rune_id: u128,
        event_type: EventType,
        invocation_json: &[u8],
    ) -> Result<Vec<JavaScriptAction>, JavaScriptFailure> {
        let script = self
            .scripts
            .lock()
            .map_err(|_| JavaScriptFailure::runtime("the JavaScript registry lock is poisoned"))?
            .get(&rune_id)
            .cloned()
            .ok_or_else(|| {
                JavaScriptFailure::new(
                    JavaScriptFailureKind::NotFound,
                    "the JavaScript rune is not registered",
                )
            })?;

        if script.event_type != event_type {
            return Err(JavaScriptFailure::new(
                JavaScriptFailureKind::WrongEvent,
                "the invocation event does not match the JavaScript rune",
            ));
        }

        let _interpreter = self.interpreter.lock().map_err(|_| {
            JavaScriptFailure::runtime("the JavaScript interpreter lock is poisoned")
        })?;
        let output = Arc::new(Mutex::new(ReplyCollector::new(&self.limits)));
        let timed_out = Arc::new(AtomicBool::new(false));
        let deadline = Instant::now() + self.limits.deadline;
        let timeout_flag = Arc::clone(&timed_out);
        self.runtime.set_interrupt_handler(Some(Box::new(move || {
            let interrupted = Instant::now() >= deadline;
            if interrupted {
                timeout_flag.store(true, Ordering::Relaxed);
            }
            interrupted
        })));
        let interrupt = InterruptGuard(self.runtime.clone());

        let context = Context::full(&self.runtime).map_err(|error| {
            JavaScriptFailure::runtime(format!(
                "a JavaScript context could not be created: {error}"
            ))
        })?;
        let execution = context.with(|context| {
            execute(
                &context,
                script.event_type,
                &script.source,
                invocation_json,
                &output,
            )
        });

        drop(context);
        drop(interrupt);
        self.runtime.run_gc();

        if timed_out.load(Ordering::Relaxed) {
            return Err(JavaScriptFailure::new(
                JavaScriptFailureKind::Deadline,
                "the JavaScript rune exceeded its execution deadline",
            ));
        }
        {
            let mut output = output.lock().map_err(|_| {
                JavaScriptFailure::runtime("the JavaScript output lock is poisoned")
            })?;
            if let Some(detail) = output.failure.take() {
                return Err(JavaScriptFailure::new(
                    JavaScriptFailureKind::OutputLimit,
                    detail,
                ));
            }
        }
        execution?;

        let mut output = output
            .lock()
            .map_err(|_| JavaScriptFailure::runtime("the JavaScript output lock is poisoned"))?;
        Ok(output
            .replies
            .drain(..)
            .map(|content| JavaScriptAction { content })
            .collect())
    }

    #[must_use]
    pub fn memory_used_bytes(&self) -> u64 {
        u64::try_from(self.runtime.memory_usage().memory_used_size).unwrap_or(0)
    }

    fn validate(&self, event_type: EventType, source: &str) -> Result<(), JavaScriptFailure> {
        let _interpreter = self.interpreter.lock().map_err(|_| {
            JavaScriptFailure::runtime("the JavaScript interpreter lock is poisoned")
        })?;
        let timed_out = Arc::new(AtomicBool::new(false));
        let deadline = Instant::now() + self.limits.deadline;
        let timeout_flag = Arc::clone(&timed_out);
        self.runtime.set_interrupt_handler(Some(Box::new(move || {
            let interrupted = Instant::now() >= deadline;
            if interrupted {
                timeout_flag.store(true, Ordering::Relaxed);
            }
            interrupted
        })));
        let interrupt = InterruptGuard(self.runtime.clone());
        let context = Context::full(&self.runtime).map_err(|error| {
            JavaScriptFailure::runtime(format!(
                "a JavaScript context could not be created: {error}"
            ))
        })?;
        let wrapped = wrap(event_type, source);
        let result = context.with(|context| {
            context
                .eval::<Function<'_>, _>(wrapped.as_bytes())
                .catch(&context)
                .map(|_| ())
                .map_err(|error| error.to_string())
        });
        drop(context);
        drop(interrupt);
        self.runtime.run_gc();

        if timed_out.load(Ordering::Relaxed) {
            return Err(JavaScriptFailure::new(
                JavaScriptFailureKind::Deadline,
                "the JavaScript rune exceeded its registration deadline",
            ));
        }

        result
            .map_err(|detail| JavaScriptFailure::new(JavaScriptFailureKind::InvalidSource, detail))
    }
}

impl Default for SharedJavaScriptRuntime {
    fn default() -> Self {
        Self::new(JavaScriptLimits::default()).expect("the default QuickJS runtime must initialise")
    }
}

struct InterruptGuard(Runtime);

impl Drop for InterruptGuard {
    fn drop(&mut self) {
        self.0.set_interrupt_handler(None);
    }
}

struct ReplyCollector {
    replies: Vec<String>,
    bytes: usize,
    failure: Option<String>,
    max_actions: usize,
    max_reply_bytes: usize,
    max_output_bytes: usize,
}

impl ReplyCollector {
    fn new(limits: &JavaScriptLimits) -> Self {
        Self {
            replies: Vec::new(),
            bytes: 0,
            failure: None,
            max_actions: limits.max_actions,
            max_reply_bytes: limits.max_reply_bytes,
            max_output_bytes: limits.max_output_bytes,
        }
    }

    fn reply(&mut self, content: String) -> Result<(), &'static str> {
        if self.failure.is_some() {
            return Err("the JavaScript rune already exceeded an output limit");
        }
        if self.replies.len() >= self.max_actions {
            return self.reject("the JavaScript rune exceeded the invocation action limit");
        }
        if content.len() > self.max_reply_bytes {
            return self.reject("the JavaScript reply exceeded the per-reply byte limit");
        }
        let Some(bytes) = self.bytes.checked_add(content.len()) else {
            return self.reject("the JavaScript rune exceeded the invocation output limit");
        };
        if bytes > self.max_output_bytes {
            return self.reject("the JavaScript rune exceeded the invocation output limit");
        }

        self.bytes = bytes;
        self.replies.push(content);
        Ok(())
    }

    fn reject(&mut self, detail: &'static str) -> Result<(), &'static str> {
        self.replies.clear();
        self.bytes = 0;
        self.failure = Some(detail.to_owned());
        Err(detail)
    }
}

fn execute(
    context: &Ctx<'_>,
    event_type: EventType,
    source: &str,
    invocation_json: &[u8],
    output: &Arc<Mutex<ReplyCollector>>,
) -> Result<(), JavaScriptFailure> {
    let payload = project_payload(context, event_type, invocation_json)?;
    if event_type == EventType::MessageCreate {
        let reply_output = Arc::clone(output);
        payload
            .set(
                "reply",
                Func::from(move |context: Ctx<'_>, content: String| {
                    reply_output
                        .lock()
                        .map_err(|_| {
                            rquickjs::Exception::throw_internal(
                                &context,
                                "the JavaScript output lock is poisoned",
                            )
                        })?
                        .reply(content)
                        .map_err(|detail| rquickjs::Exception::throw_range(&context, detail))
                }),
            )
            .catch(context)
            .map_err(|error| JavaScriptFailure::runtime(error.to_string()))?;
    }
    freeze_object(context, &payload)?;

    let function = context
        .eval::<Function<'_>, _>(wrap(event_type, source).as_bytes())
        .catch(context)
        .map_err(|error| JavaScriptFailure::runtime(error.to_string()))?;
    let promise = function
        .call::<_, Promise<'_>>((payload,))
        .catch(context)
        .map_err(|error| JavaScriptFailure::runtime(error.to_string()))?;
    promise
        .finish::<()>()
        .catch(context)
        .map_err(|error| JavaScriptFailure::runtime(error.to_string()))
}

fn wrap(event_type: EventType, source: &str) -> String {
    format!(
        "(async function({}) {{\n{}\n}})",
        event_type.parameter(),
        source
    )
}

fn project_payload<'js>(
    context: &Ctx<'js>,
    event_type: EventType,
    invocation_json: &[u8],
) -> Result<Object<'js>, JavaScriptFailure> {
    match event_type {
        EventType::MessageCreate => {
            let input: MessageInput = serde_json::from_slice(invocation_json).map_err(|error| {
                JavaScriptFailure::runtime(format!("invalid invocation: {error}"))
            })?;
            let author = Object::new(context.clone()).map_err(JavaScriptFailure::from)?;
            set_snowflake(context, &author, "id", input.author.id)?;
            author
                .set("username", input.author.username)
                .map_err(JavaScriptFailure::from)?;
            freeze_object(context, &author)?;

            let message = Object::new(context.clone()).map_err(JavaScriptFailure::from)?;
            set_snowflake(context, &message, "id", input.id)?;
            set_snowflake(context, &message, "channelId", input.channel_id)?;
            message
                .set("content", input.content)
                .map_err(JavaScriptFailure::from)?;
            message
                .set("author", author)
                .map_err(JavaScriptFailure::from)?;
            Ok(message)
        }
        EventType::MessageDelete => {
            let input: MessageDeleteInput =
                serde_json::from_slice(invocation_json).map_err(|error| {
                    JavaScriptFailure::runtime(format!("invalid invocation: {error}"))
                })?;
            let args = Object::new(context.clone()).map_err(JavaScriptFailure::from)?;
            set_snowflake(context, &args, "channelId", input.channel_id)?;
            set_optional_snowflake(context, &args, "guildId", input.guild_id)?;
            set_snowflake(context, &args, "messageId", input.message_id)?;
            Ok(args)
        }
        EventType::MessageReactionAdd => {
            let input: MessageReactionAddInput =
                serde_json::from_slice(invocation_json).map_err(|error| {
                    JavaScriptFailure::runtime(format!("invalid invocation: {error}"))
                })?;
            let args = reaction_payload(
                context,
                input.burst,
                input.channel_id,
                input.emoji,
                input.guild_id,
                input.message_id,
                input.type_,
                input.user_id,
            )?;
            set_optional_snowflake(context, &args, "messageAuthorId", input.message_author_id)?;
            Ok(args)
        }
        EventType::MessageReactionRemove => {
            let input: MessageReactionRemoveInput = serde_json::from_slice(invocation_json)
                .map_err(|error| {
                    JavaScriptFailure::runtime(format!("invalid invocation: {error}"))
                })?;
            reaction_payload(
                context,
                input.burst,
                input.channel_id,
                input.emoji,
                input.guild_id,
                input.message_id,
                input.type_,
                input.user_id,
            )
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn reaction_payload<'js>(
    context: &Ctx<'js>,
    burst: bool,
    channel_id: u64,
    emoji: MessageReactionEmojiInput,
    guild_id: Option<u64>,
    message_id: u64,
    reaction_type: u8,
    user_id: u64,
) -> Result<Object<'js>, JavaScriptFailure> {
    if reaction_type > 1 {
        return Err(JavaScriptFailure::runtime(format!(
            "unsupported reaction type {reaction_type}"
        )));
    }

    let emoji_object = Object::new(context.clone()).map_err(JavaScriptFailure::from)?;
    emoji_object
        .set("animated", emoji.animated)
        .map_err(JavaScriptFailure::from)?;
    set_optional_snowflake(context, &emoji_object, "id", emoji.id)?;
    set_optional_string(&emoji_object, "name", emoji.name)?;
    freeze_object(context, &emoji_object)?;

    let args = Object::new(context.clone()).map_err(JavaScriptFailure::from)?;
    args.set("burst", burst).map_err(JavaScriptFailure::from)?;
    set_snowflake(context, &args, "channelId", channel_id)?;
    args.set("emoji", emoji_object)
        .map_err(JavaScriptFailure::from)?;
    set_optional_snowflake(context, &args, "guildId", guild_id)?;
    set_snowflake(context, &args, "messageId", message_id)?;
    args.set("type", reaction_type)
        .map_err(JavaScriptFailure::from)?;
    set_snowflake(context, &args, "userId", user_id)?;
    Ok(args)
}

fn set_snowflake<'js>(
    context: &Ctx<'js>,
    object: &Object<'js>,
    name: &str,
    value: u64,
) -> Result<(), JavaScriptFailure> {
    object
        .set(
            name,
            BigInt::from_u64(context.clone(), value).map_err(JavaScriptFailure::from)?,
        )
        .map_err(JavaScriptFailure::from)
}

fn set_optional_snowflake<'js>(
    context: &Ctx<'js>,
    object: &Object<'js>,
    name: &str,
    value: Option<u64>,
) -> Result<(), JavaScriptFailure> {
    match value {
        Some(value) => set_snowflake(context, object, name, value),
        None => object.set(name, Null).map_err(JavaScriptFailure::from),
    }
}

fn set_optional_string(
    object: &Object<'_>,
    name: &str,
    value: Option<String>,
) -> Result<(), JavaScriptFailure> {
    match value {
        Some(value) => object.set(name, value).map_err(JavaScriptFailure::from),
        None => object.set(name, Null).map_err(JavaScriptFailure::from),
    }
}

fn freeze_object<'js>(context: &Ctx<'js>, object: &Object<'js>) -> Result<(), JavaScriptFailure> {
    let constructor: Object<'js> = context
        .globals()
        .get("Object")
        .map_err(JavaScriptFailure::from)?;
    let freeze: Function<'js> = constructor.get("freeze").map_err(JavaScriptFailure::from)?;
    freeze
        .call::<_, Object<'js>>((object.clone(),))
        .map(|_| ())
        .map_err(JavaScriptFailure::from)
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct UserInput {
    id: u64,
    username: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MessageInput {
    id: u64,
    channel_id: u64,
    content: String,
    author: UserInput,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
#[allow(clippy::struct_field_names)]
struct MessageDeleteInput {
    channel_id: u64,
    guild_id: Option<u64>,
    message_id: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MessageReactionEmojiInput {
    animated: bool,
    id: Option<u64>,
    name: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MessageReactionAddInput {
    burst: bool,
    channel_id: u64,
    emoji: MessageReactionEmojiInput,
    guild_id: Option<u64>,
    message_author_id: Option<u64>,
    message_id: u64,
    #[serde(rename = "type")]
    type_: u8,
    user_id: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MessageReactionRemoveInput {
    burst: bool,
    channel_id: u64,
    emoji: MessageReactionEmojiInput,
    guild_id: Option<u64>,
    message_id: u64,
    #[serde(rename = "type")]
    type_: u8,
    user_id: u64,
}

#[cfg(test)]
mod tests {
    use std::time::Instant;

    use super::*;

    const MESSAGE: &[u8] = br#"{
        "id": 18446744073709551615,
        "channelId": 9223372036854775808,
        "content": "!native-test",
        "author": {
            "id": 9007199254740993,
            "username": "Yendreck"
        }
    }"#;

    const DELETE: &[u8] = br#"{
        "channelId": 9223372036854775808,
        "guildId": null,
        "messageId": 18446744073709551615
    }"#;

    const REACTION_ADD: &[u8] = br#"{
        "burst": true,
        "channelId": 9223372036854775808,
        "emoji": { "animated": false, "id": null, "name": "\ud83d\udd25" },
        "guildId": 9007199254740993,
        "messageAuthorId": null,
        "messageId": 18446744073709551615,
        "type": 1,
        "userId": 9007199254740994
    }"#;

    const REACTION_REMOVE: &[u8] = br#"{
        "burst": false,
        "channelId": 9223372036854775808,
        "emoji": { "animated": true, "id": 9007199254740995, "name": null },
        "guildId": null,
        "messageId": 18446744073709551615,
        "type": 0,
        "userId": 9007199254740994
    }"#;

    #[test]
    fn registration_only_parses_and_stores_tiny_source() {
        let runtime = SharedJavaScriptRuntime::default();
        let started = Instant::now();

        runtime
            .register(1, EventType::MessageCreate, "await message.reply('hello');")
            .unwrap();

        assert!(started.elapsed() < Duration::from_secs(1));
    }

    #[test]
    fn invalid_source_is_rejected_without_replacing_a_working_rune() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(1, EventType::MessageCreate, "await message.reply('old');")
            .unwrap();

        let failure = runtime
            .register(1, EventType::MessageCreate, "const = ;")
            .unwrap_err();

        assert_eq!(failure.kind, JavaScriptFailureKind::InvalidSource);
        assert_eq!(
            runtime
                .invoke(1, EventType::MessageCreate, MESSAGE)
                .unwrap()[0]
                .content,
            "old"
        );
    }

    #[test]
    fn message_projection_preserves_snowflakes_and_supports_reply() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(
                1,
                EventType::MessageCreate,
                r#"
                    if (message.id !== 18446744073709551615n ||
                        message.channelId !== 9223372036854775808n ||
                        message.author.id !== 9007199254740993n) {
                        throw new Error("snowflake precision was lost");
                    }
                    await message.reply(`${message.author.username}: ${message.content}`);
                "#,
            )
            .unwrap();

        let actions = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(
            actions,
            [JavaScriptAction {
                content: "Yendreck: !native-test".into()
            }]
        );
    }

    #[test]
    fn every_gateway_event_projection_matches_the_javascript_api() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(
                1,
                EventType::MessageDelete,
                r#"
                    if (args.channelId !== 9223372036854775808n ||
                        args.guildId !== null ||
                        args.messageId !== 18446744073709551615n) {
                        throw new Error("message delete projection failed");
                    }
                "#,
            )
            .unwrap();
        runtime
            .register(
                2,
                EventType::MessageReactionAdd,
                r#"
                    if (!args.burst || args.emoji.name !== "🔥" ||
                        args.guildId !== 9007199254740993n ||
                        args.messageAuthorId !== null || args.type !== 1 ||
                        args.userId !== 9007199254740994n) {
                        throw new Error("reaction add projection failed");
                    }
                "#,
            )
            .unwrap();
        runtime
            .register(
                3,
                EventType::MessageReactionRemove,
                r#"
                    if (args.burst) throw new Error("burst");
                    if (!args.emoji.animated) throw new Error("animated");
                    if (args.emoji.id !== 9007199254740995n) throw new Error("emoji id");
                    if (args.emoji.name !== null) throw new Error("emoji name");
                    if (args.guildId !== null) throw new Error("guild id");
                    if (args.type !== 0) throw new Error("type");
                "#,
            )
            .unwrap();

        assert!(runtime
            .invoke(1, EventType::MessageDelete, DELETE)
            .unwrap()
            .is_empty());
        assert!(runtime
            .invoke(2, EventType::MessageReactionAdd, REACTION_ADD)
            .unwrap()
            .is_empty());
        assert!(runtime
            .invoke(3, EventType::MessageReactionRemove, REACTION_REMOVE)
            .unwrap()
            .is_empty());
    }

    #[test]
    fn every_invocation_receives_fresh_global_state() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(
                1,
                EventType::MessageCreate,
                "globalThis.count = (globalThis.count ?? 0) + 1; await message.reply(String(globalThis.count));",
            )
            .unwrap();

        let first = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap();
        let second = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(first[0].content, "1");
        assert_eq!(second[0].content, "1");
    }

    #[test]
    fn projected_event_values_reject_root_and_nested_mutation() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(1, EventType::MessageCreate, "message.content = 'changed';")
            .unwrap();
        runtime
            .register(
                2,
                EventType::MessageCreate,
                "message.author.username = 'changed';",
            )
            .unwrap();

        let root = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap_err();
        let nested = runtime
            .invoke(2, EventType::MessageCreate, MESSAGE)
            .unwrap_err();

        assert!(root.detail.contains("read-only"));
        assert!(nested.detail.contains("read-only"));
    }

    #[test]
    fn runes_cannot_observe_each_others_globals() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(1, EventType::MessageCreate, "globalThis.secret = 'first';")
            .unwrap();
        runtime
            .register(
                2,
                EventType::MessageCreate,
                "await message.reply(typeof globalThis.secret);",
            )
            .unwrap();

        runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap();
        let actions = runtime
            .invoke(2, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(actions[0].content, "undefined");
    }

    #[test]
    fn runes_cannot_modify_each_others_intrinsics() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(
                1,
                EventType::MessageCreate,
                "Array.prototype.leaked = 'first';",
            )
            .unwrap();
        runtime
            .register(
                2,
                EventType::MessageCreate,
                "await message.reply(typeof [].leaked);",
            )
            .unwrap();

        runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap();
        let actions = runtime
            .invoke(2, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(actions[0].content, "undefined");
    }

    #[test]
    fn network_process_and_module_loading_are_absent() {
        let runtime = SharedJavaScriptRuntime::default();
        runtime
            .register(
                1,
                EventType::MessageCreate,
                "await message.reply([typeof fetch, typeof process, typeof require].join(','));",
            )
            .unwrap();

        let actions = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(actions[0].content, "undefined,undefined,undefined");
    }

    #[test]
    fn infinite_loop_is_interrupted_and_next_rune_recovers() {
        let runtime = SharedJavaScriptRuntime::new(JavaScriptLimits {
            deadline: Duration::from_millis(25),
            ..JavaScriptLimits::default()
        })
        .unwrap();
        runtime
            .register(1, EventType::MessageCreate, "while (true) {}")
            .unwrap();
        runtime
            .register(
                2,
                EventType::MessageCreate,
                "await message.reply('healthy');",
            )
            .unwrap();

        let failure = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap_err();
        let recovery = runtime
            .invoke(2, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(failure.kind, JavaScriptFailureKind::Deadline);
        assert_eq!(recovery[0].content, "healthy");
    }

    #[test]
    fn memory_exhaustion_is_contained_and_next_rune_recovers() {
        let runtime = SharedJavaScriptRuntime::new(JavaScriptLimits {
            memory_bytes: 4 * 1024 * 1024,
            deadline: Duration::from_secs(1),
            ..JavaScriptLimits::default()
        })
        .unwrap();
        runtime
            .register(
                1,
                EventType::MessageCreate,
                "const chunks = []; while (true) chunks.push(new Uint8Array(1024 * 1024));",
            )
            .unwrap();
        runtime
            .register(
                2,
                EventType::MessageCreate,
                "await message.reply('healthy');",
            )
            .unwrap();

        let failure = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap_err();
        let recovery = runtime
            .invoke(2, EventType::MessageCreate, MESSAGE)
            .unwrap();

        assert_eq!(failure.kind, JavaScriptFailureKind::Runtime);
        assert!(failure.detail.to_lowercase().contains("memory"));
        assert_eq!(recovery[0].content, "healthy");
    }

    #[test]
    fn output_limits_discard_partial_actions() {
        let runtime = SharedJavaScriptRuntime::new(JavaScriptLimits {
            max_actions: 2,
            ..JavaScriptLimits::default()
        })
        .unwrap();
        runtime
            .register(
                1,
                EventType::MessageCreate,
                "await message.reply('one'); await message.reply('two'); await message.reply('three');",
            )
            .unwrap();

        let failure = runtime
            .invoke(1, EventType::MessageCreate, MESSAGE)
            .unwrap_err();

        assert_eq!(failure.kind, JavaScriptFailureKind::OutputLimit);
    }
}
