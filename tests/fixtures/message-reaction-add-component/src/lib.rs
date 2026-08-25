mod bindings {
    wit_bindgen::generate!({
        path: "../../../wit",
        world: "message-reaction-add-rune",
    });
}

struct Component;

impl bindings::Guest for Component {
    fn handle(args: bindings::rune::api::types::MessageReactionAddEventArgs) {
        bindings::reply(&format!(
            "{}|{}|{}|{}|{}|{}|{}|{}|{}|{}",
            args.burst,
            args.channel_id,
            args.emoji.animated,
            optional_id(args.emoji.id),
            args.emoji.name.as_deref().unwrap_or("none"),
            optional_id(args.guild_id),
            optional_id(args.message_author_id),
            args.message_id,
            reaction_type(args.type_),
            args.user_id
        ));
    }
}

fn optional_id(value: Option<u64>) -> String {
    value.map_or_else(|| "none".to_owned(), |id| id.to_string())
}

fn reaction_type(value: bindings::rune::api::types::ReactionType) -> &'static str {
    match value {
        bindings::rune::api::types::ReactionType::Normal => "normal",
        bindings::rune::api::types::ReactionType::Burst => "burst",
    }
}

bindings::export!(Component with_types_in bindings);
