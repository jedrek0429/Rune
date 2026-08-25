use std::ffi::c_void;
use std::fmt;
use std::mem;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;
use std::sync::Mutex;

use serde::Deserialize;
use wasmtime::component::{Component, HasSelf, Linker, ResourceTable};
use wasmtime::{Config, Engine, Store, StoreLimits, StoreLimitsBuilder};
use wasmtime_wasi::{WasiCtx, WasiCtxView, WasiView};

const ABI_VERSION: u32 = 4;
const STATUS_OK: i32 = 0;
const STATUS_INVALID_ARGUMENT: i32 = 1;
const STATUS_RUNTIME_ERROR: i32 = 2;
const STATUS_PANIC: i32 = 3;
const ACTION_REPLY: u32 = 1;
const INVOCATION_FUEL: u64 = 1_000_000;
const INVOCATION_MEMORY_BYTES: usize = 16 * 1024 * 1024;
const INVOCATION_MAX_ACTIONS: usize = 16;
const INVOCATION_MAX_REPLY_BYTES: usize = 8 * 1024;
const INVOCATION_MAX_OUTPUT_BYTES: usize = 64 * 1024;
const APPROVED_WASI_IMPORTS: &[&str] = &[
    "wasi:cli/environment",
    "wasi:cli/exit",
    "wasi:cli/stderr",
    "wasi:cli/stdin",
    "wasi:cli/stdout",
    "wasi:filesystem/preopens",
    "wasi:filesystem/types",
    "wasi:io/error",
    "wasi:io/poll",
    "wasi:io/streams",
];

mod message_create_bindings {
    wasmtime::component::bindgen!({
        path: "../../wit",
        world: "message-create-rune",
    });
}

mod message_delete_bindings {
    wasmtime::component::bindgen!({
        path: "../../wit",
        world: "message-delete-rune",
    });
}

mod message_reaction_add_bindings {
    wasmtime::component::bindgen!({
        path: "../../wit",
        world: "message-reaction-add-rune",
    });
}

mod message_reaction_remove_bindings {
    wasmtime::component::bindgen!({
        path: "../../wit",
        world: "message-reaction-remove-rune",
    });
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[repr(u32)]
enum RuneEventType {
    MessageCreate = 0,
    MessageDelete = 1,
    MessageReactionAdd = 2,
    MessageReactionRemove = 3,
}

impl TryFrom<u32> for RuneEventType {
    type Error = ();

    fn try_from(value: u32) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(Self::MessageCreate),
            1 => Ok(Self::MessageDelete),
            2 => Ok(Self::MessageReactionAdd),
            3 => Ok(Self::MessageReactionRemove),
            _ => Err(()),
        }
    }
}

#[derive(Clone)]
struct LoadedComponent {
    event_type: RuneEventType,
    component: Component,
}

struct RuneRuntime {
    engine: Engine,
    component: Mutex<Option<LoadedComponent>>,
}

struct InvocationState {
    replies: Vec<String>,
    reply_bytes: usize,
    output_failure: Option<&'static str>,
    limits: StoreLimits,
    wasi: WasiCtx,
    resources: ResourceTable,
}

impl InvocationState {
    fn new() -> Self {
        Self {
            replies: Vec::new(),
            reply_bytes: 0,
            output_failure: None,
            limits: StoreLimitsBuilder::new()
                .memory_size(INVOCATION_MEMORY_BYTES)
                .trap_on_grow_failure(true)
                .build(),
            wasi: WasiCtx::builder().build(),
            resources: ResourceTable::new(),
        }
    }

    fn record_reply(&mut self, content: String) {
        if self.output_failure.is_some() {
            return;
        }

        if self.replies.len() >= INVOCATION_MAX_ACTIONS {
            self.reject_output("component exceeded the invocation action limit");
            return;
        }

        let content_bytes = content.len();
        if content_bytes > INVOCATION_MAX_REPLY_BYTES {
            self.reject_output("component reply exceeded the per-reply byte limit");
            return;
        }

        let Some(total_bytes) = self.reply_bytes.checked_add(content_bytes) else {
            self.reject_output("component output exceeded the invocation byte limit");
            return;
        };
        if total_bytes > INVOCATION_MAX_OUTPUT_BYTES {
            self.reject_output("component output exceeded the invocation byte limit");
            return;
        }

        self.reply_bytes = total_bytes;
        self.replies.push(content);
    }

    fn reject_output(&mut self, detail: &'static str) {
        self.replies.clear();
        self.reply_bytes = 0;
        self.output_failure = Some(detail);
    }
}

impl message_create_bindings::MessageCreateRuneImports for InvocationState {
    fn reply(&mut self, content: String) {
        self.record_reply(content);
    }
}

impl message_delete_bindings::MessageDeleteRuneImports for InvocationState {
    fn reply(&mut self, content: String) {
        self.record_reply(content);
    }
}

impl message_reaction_add_bindings::MessageReactionAddRuneImports for InvocationState {
    fn reply(&mut self, content: String) {
        self.record_reply(content);
    }
}

impl message_reaction_remove_bindings::MessageReactionRemoveRuneImports for InvocationState {
    fn reply(&mut self, content: String) {
        self.record_reply(content);
    }
}

impl WasiView for InvocationState {
    fn ctx(&mut self) -> WasiCtxView<'_> {
        WasiCtxView {
            ctx: &mut self.wasi,
            table: &mut self.resources,
        }
    }
}

struct NativeFailure {
    status: i32,
    detail: String,
}

impl NativeFailure {
    fn invalid(detail: impl Into<String>) -> Self {
        Self {
            status: STATUS_INVALID_ARGUMENT,
            detail: detail.into(),
        }
    }

    fn runtime(error: impl fmt::Display) -> Self {
        Self {
            status: STATUS_RUNTIME_ERROR,
            detail: format!("{error:#}"),
        }
    }
}

#[repr(C)]
pub struct RuneBuffer {
    data: *mut u8,
    len: usize,
}

impl RuneBuffer {
    const EMPTY: Self = Self {
        data: ptr::null_mut(),
        len: 0,
    };

    fn from_bytes(bytes: Vec<u8>) -> Self {
        if bytes.is_empty() {
            return Self::EMPTY;
        }

        let mut bytes = bytes.into_boxed_slice();
        let buffer = Self {
            data: bytes.as_mut_ptr(),
            len: bytes.len(),
        };
        mem::forget(bytes);
        buffer
    }
}

#[repr(C)]
pub struct RuneAction {
    kind: u32,
    content: RuneBuffer,
}

#[repr(C)]
pub struct RuneActionList {
    data: *mut RuneAction,
    len: usize,
}

impl RuneActionList {
    const EMPTY: Self = Self {
        data: ptr::null_mut(),
        len: 0,
    };

    fn from_replies(replies: Vec<String>) -> Self {
        if replies.is_empty() {
            return Self::EMPTY;
        }

        let mut actions = replies
            .into_iter()
            .map(|content| RuneAction {
                kind: ACTION_REPLY,
                content: RuneBuffer::from_bytes(content.into_bytes()),
            })
            .collect::<Vec<_>>()
            .into_boxed_slice();
        let list = Self {
            data: actions.as_mut_ptr(),
            len: actions.len(),
        };
        mem::forget(actions);
        list
    }
}

#[no_mangle]
pub extern "C" fn rune_runtime_abi_version() -> u32 {
    ABI_VERSION
}

#[no_mangle]
pub extern "C" fn rune_runtime_create() -> *mut c_void {
    match catch_unwind(|| {
        let mut config = Config::new();
        config.consume_fuel(true);
        let engine = Engine::new(&config)?;
        let runtime = Box::into_raw(Box::new(RuneRuntime {
            engine,
            component: Mutex::new(None),
        }));

        Ok::<*mut c_void, wasmtime::Error>(runtime.cast())
    }) {
        Ok(Ok(runtime)) => runtime,
        Ok(Err(_)) | Err(_) => ptr::null_mut(),
    }
}

/// # Safety
///
/// `runtime` must be a handle returned by `rune_runtime_create`, and
/// `component_data` must identify `component_len` readable bytes.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_load_component(
    runtime: *mut c_void,
    event_type: u32,
    component_data: *const u8,
    component_len: usize,
) -> i32 {
    ffi_status(|| {
        let runtime = unsafe { runtime_ref(runtime) }.ok_or(STATUS_INVALID_ARGUMENT)?;
        let event_type = RuneEventType::try_from(event_type).map_err(|()| STATUS_INVALID_ARGUMENT)?;
        let bytes = unsafe { bytes_from_raw(component_data, component_len) }
            .ok_or(STATUS_INVALID_ARGUMENT)?;
        let component = Component::new(&runtime.engine, bytes).map_err(|_| STATUS_RUNTIME_ERROR)?;
        validate_component(&runtime.engine, event_type, &component)
            .map_err(|_| STATUS_RUNTIME_ERROR)?;
        let mut loaded = runtime.component.lock().map_err(|_| STATUS_RUNTIME_ERROR)?;
        *loaded = Some(LoadedComponent {
            event_type,
            component,
        });
        Ok(())
    })
}

/// # Safety
///
/// `runtime` must be a handle returned by `rune_runtime_create`,
/// `invocation_data` must identify `invocation_len` readable bytes, and
/// `actions` and `error` must be writable. A successful action list must
/// be released with `rune_runtime_action_list_free`; an error buffer must
/// be released with `rune_runtime_buffer_free`.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_invoke(
    runtime: *mut c_void,
    invocation_data: *const u8,
    invocation_len: usize,
    actions: *mut RuneActionList,
    error: *mut RuneBuffer,
) -> i32 {
    if actions.is_null() || error.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    unsafe {
        actions.write(RuneActionList::EMPTY);
        error.write(RuneBuffer::EMPTY);
    }

    let outcome = catch_unwind(AssertUnwindSafe(|| unsafe {
        invoke(runtime, invocation_data, invocation_len)
    }));

    match outcome {
        Ok(Ok(list)) => {
            unsafe {
                actions.write(list);
            }
            STATUS_OK
        }
        Ok(Err(failure)) => {
            let status = failure.status;
            unsafe {
                error.write(RuneBuffer::from_bytes(failure.detail.into_bytes()));
            }
            status
        }
        Err(_) => {
            unsafe {
                error.write(RuneBuffer::from_bytes(
                    b"the native runtime panicked".to_vec(),
                ));
            }
            STATUS_PANIC
        }
    }
}

/// # Safety
///
/// `list` must either be empty or have been returned by a successful call
/// to `rune_runtime_invoke`, and must be released only once.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_action_list_free(list: RuneActionList) {
    if list.data.is_null() {
        return;
    }

    let slice = ptr::slice_from_raw_parts_mut(list.data, list.len);
    let actions = unsafe { Box::from_raw(slice) };
    for action in &actions {
        unsafe {
            free_buffer(&action.content);
        }
    }
}

/// # Safety
///
/// `buffer` must either be empty or have been returned by this library, and
/// must be released only once.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_buffer_free(buffer: RuneBuffer) {
    unsafe {
        free_buffer(&buffer);
    }
}

/// # Safety
///
/// `runtime` must be null or a handle returned by `rune_runtime_create`,
/// and it must be destroyed only once.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_destroy(runtime: *mut c_void) {
    if runtime.is_null() {
        return;
    }

    unsafe {
        drop(Box::from_raw(runtime.cast::<RuneRuntime>()));
    }
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

unsafe fn invoke(
    runtime: *mut c_void,
    invocation_data: *const u8,
    invocation_len: usize,
) -> Result<RuneActionList, NativeFailure> {
    let runtime = unsafe { runtime_ref(runtime) }
        .ok_or_else(|| NativeFailure::invalid("the runtime handle is null"))?;
    let invocation = unsafe { bytes_from_raw(invocation_data, invocation_len) }
        .ok_or_else(|| NativeFailure::invalid("the invocation buffer is null"))?;
    let loaded = runtime
        .component
        .lock()
        .map_err(|_| NativeFailure::runtime("the component lock is poisoned"))?
        .clone()
        .ok_or_else(|| NativeFailure::runtime("no component is loaded"))?;
    let linker = component_linker(&runtime.engine, loaded.event_type)
        .map_err(NativeFailure::runtime)?;
    let mut store = Store::new(&runtime.engine, InvocationState::new());
    store.limiter(|state| &mut state.limits);
    store
        .set_fuel(INVOCATION_FUEL)
        .map_err(NativeFailure::runtime)?;

    match loaded.event_type {
        RuneEventType::MessageCreate => {
            invoke_message_create(&mut store, &loaded.component, &linker, invocation)?;
        }
        RuneEventType::MessageDelete => {
            invoke_message_delete(&mut store, &loaded.component, &linker, invocation)?;
        }
        RuneEventType::MessageReactionAdd => {
            invoke_message_reaction_add(&mut store, &loaded.component, &linker, invocation)?;
        }
        RuneEventType::MessageReactionRemove => {
            invoke_message_reaction_remove(&mut store, &loaded.component, &linker, invocation)?;
        }
    }

    if let Some(detail) = store.data().output_failure {
        return Err(NativeFailure::runtime(detail));
    }

    let replies = mem::take(&mut store.data_mut().replies);
    Ok(RuneActionList::from_replies(replies))
}

fn invoke_message_create(
    store: &mut Store<InvocationState>,
    component: &Component,
    linker: &Linker<InvocationState>,
    invocation: &[u8],
) -> Result<(), NativeFailure> {
    let input: MessageInput =
        serde_json::from_slice(invocation).map_err(NativeFailure::runtime)?;
    let message = message_create_bindings::rune::api::types::Message {
        id: input.id,
        channel_id: input.channel_id,
        content: input.content,
        author: message_create_bindings::rune::api::types::User {
            id: input.author.id,
            username: input.author.username,
        },
    };
    let bindings = message_create_bindings::MessageCreateRune::instantiate(
        &mut *store,
        component,
        linker,
    )
    .map_err(NativeFailure::runtime)?;
    bindings
        .call_handle(&mut *store, &message)
        .map_err(NativeFailure::runtime)
}

fn invoke_message_delete(
    store: &mut Store<InvocationState>,
    component: &Component,
    linker: &Linker<InvocationState>,
    invocation: &[u8],
) -> Result<(), NativeFailure> {
    let input: MessageDeleteInput =
        serde_json::from_slice(invocation).map_err(NativeFailure::runtime)?;
    let args = message_delete_bindings::rune::api::types::MessageDeleteEventArgs {
        channel_id: input.channel_id,
        guild_id: input.guild_id,
        message_id: input.message_id,
    };
    let bindings = message_delete_bindings::MessageDeleteRune::instantiate(
        &mut *store,
        component,
        linker,
    )
    .map_err(NativeFailure::runtime)?;
    bindings
        .call_handle(&mut *store, &args)
        .map_err(NativeFailure::runtime)
}

fn invoke_message_reaction_add(
    store: &mut Store<InvocationState>,
    component: &Component,
    linker: &Linker<InvocationState>,
    invocation: &[u8],
) -> Result<(), NativeFailure> {
    let input: MessageReactionAddInput =
        serde_json::from_slice(invocation).map_err(NativeFailure::runtime)?;
    let reaction_type = match input.type_ {
        0 => message_reaction_add_bindings::rune::api::types::ReactionType::Normal,
        1 => message_reaction_add_bindings::rune::api::types::ReactionType::Burst,
        value => {
            return Err(NativeFailure::invalid(format!(
                "unsupported reaction type {value}"
            )))
        }
    };
    let args = message_reaction_add_bindings::rune::api::types::MessageReactionAddEventArgs {
        burst: input.burst,
        channel_id: input.channel_id,
        emoji: message_reaction_add_bindings::rune::api::types::MessageReactionEmoji {
            animated: input.emoji.animated,
            id: input.emoji.id,
            name: input.emoji.name,
        },
        guild_id: input.guild_id,
        message_author_id: input.message_author_id,
        message_id: input.message_id,
        type_: reaction_type,
        user_id: input.user_id,
    };
    let bindings = message_reaction_add_bindings::MessageReactionAddRune::instantiate(
        &mut *store,
        component,
        linker,
    )
    .map_err(NativeFailure::runtime)?;
    bindings
        .call_handle(&mut *store, &args)
        .map_err(NativeFailure::runtime)
}

fn invoke_message_reaction_remove(
    store: &mut Store<InvocationState>,
    component: &Component,
    linker: &Linker<InvocationState>,
    invocation: &[u8],
) -> Result<(), NativeFailure> {
    let input: MessageReactionRemoveInput =
        serde_json::from_slice(invocation).map_err(NativeFailure::runtime)?;
    let reaction_type = match input.type_ {
        0 => message_reaction_remove_bindings::rune::api::types::ReactionType::Normal,
        1 => message_reaction_remove_bindings::rune::api::types::ReactionType::Burst,
        value => {
            return Err(NativeFailure::invalid(format!(
                "unsupported reaction type {value}"
            )))
        }
    };
    let args =
        message_reaction_remove_bindings::rune::api::types::MessageReactionRemoveEventArgs {
            burst: input.burst,
            channel_id: input.channel_id,
            emoji: message_reaction_remove_bindings::rune::api::types::MessageReactionEmoji {
                animated: input.emoji.animated,
                id: input.emoji.id,
                name: input.emoji.name,
            },
            guild_id: input.guild_id,
            message_id: input.message_id,
            type_: reaction_type,
            user_id: input.user_id,
        };
    let bindings = message_reaction_remove_bindings::MessageReactionRemoveRune::instantiate(
        &mut *store,
        component,
        linker,
    )
    .map_err(NativeFailure::runtime)?;
    bindings
        .call_handle(&mut *store, &args)
        .map_err(NativeFailure::runtime)
}

fn validate_component(
    engine: &Engine,
    event_type: RuneEventType,
    component: &Component,
) -> Result<(), wasmtime::Error> {
    let mut approved = true;
    for (name, _) in component.component_type().imports(engine) {
        approved &= is_approved_import(name);
    }

    if !approved {
        return Err(wasmtime::Error::msg(
            "the component declares an unapproved import",
        ));
    }

    component_linker(engine, event_type)?.instantiate_pre(component)?;
    Ok(())
}

fn is_approved_import(name: &str) -> bool {
    if name == "reply" {
        return true;
    }

    let Some((interface, version)) = name.rsplit_once('@') else {
        return false;
    };

    APPROVED_WASI_IMPORTS.contains(&interface) && is_wasi_preview_two_version(version)
}

fn is_wasi_preview_two_version(version: &str) -> bool {
    let mut parts = version.split('.');
    if parts.next() != Some("0") || parts.next() != Some("2") {
        return false;
    }

    let Some(patch) = parts.next() else {
        return false;
    };

    parts.next().is_none()
        && patch
            .split_once('-')
            .map_or(patch, |(number, _)| number)
            .parse::<u64>()
            .is_ok()
}

fn component_linker(
    engine: &Engine,
    event_type: RuneEventType,
) -> Result<Linker<InvocationState>, wasmtime::Error> {
    let mut linker = Linker::new(engine);
    match event_type {
        RuneEventType::MessageCreate => {
            message_create_bindings::MessageCreateRune::add_to_linker::<_, HasSelf<_>>(
                &mut linker,
                |state| state,
            )?;
        }
        RuneEventType::MessageDelete => {
            message_delete_bindings::MessageDeleteRune::add_to_linker::<_, HasSelf<_>>(
                &mut linker,
                |state| state,
            )?;
        }
        RuneEventType::MessageReactionAdd => {
            message_reaction_add_bindings::MessageReactionAddRune::add_to_linker::<_, HasSelf<_>>(
                &mut linker,
                |state| state,
            )?;
        }
        RuneEventType::MessageReactionRemove => {
            message_reaction_remove_bindings::MessageReactionRemoveRune::add_to_linker::<
                _,
                HasSelf<_>,
            >(&mut linker, |state| state)?;
        }
    }
    wasmtime_wasi::p2::add_to_linker_sync(&mut linker)?;
    Ok(linker)
}

fn ffi_status(operation: impl FnOnce() -> Result<(), i32>) -> i32 {
    match catch_unwind(AssertUnwindSafe(operation)) {
        Ok(Ok(())) => STATUS_OK,
        Ok(Err(status)) => status,
        Err(_) => STATUS_PANIC,
    }
}

unsafe fn runtime_ref<'a>(runtime: *mut c_void) -> Option<&'a RuneRuntime> {
    unsafe { runtime.cast::<RuneRuntime>().as_ref() }
}

unsafe fn bytes_from_raw<'a>(data: *const u8, len: usize) -> Option<&'a [u8]> {
    if data.is_null() {
        return None;
    }

    Some(unsafe { slice::from_raw_parts(data, len) })
}

unsafe fn free_buffer(buffer: &RuneBuffer) {
    if buffer.data.is_null() {
        return;
    }

    let slice = ptr::slice_from_raw_parts_mut(buffer.data, buffer.len);
    unsafe {
        drop(Box::from_raw(slice));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reports_current_abi_version() {
        assert_eq!(rune_runtime_abi_version(), ABI_VERSION);
    }

    #[test]
    fn rejects_null_component_data() {
        let runtime = rune_runtime_create();

        let status = unsafe {
            rune_runtime_load_component(
                runtime,
                RuneEventType::MessageCreate as u32,
                ptr::null(),
                1,
            )
        };

        assert_eq!(status, STATUS_INVALID_ARGUMENT);
        unsafe {
            rune_runtime_destroy(runtime);
        }
    }

    #[test]
    fn creates_ordered_reply_actions() {
        let list = RuneActionList::from_replies(vec!["first".to_owned(), "second".to_owned()]);

        let actions = unsafe { slice::from_raw_parts(list.data, list.len) };
        assert_eq!(actions.len(), 2);
        assert_eq!(actions[0].kind, ACTION_REPLY);
        assert_eq!(actions[1].kind, ACTION_REPLY);

        unsafe {
            rune_runtime_action_list_free(list);
        }
    }
}
