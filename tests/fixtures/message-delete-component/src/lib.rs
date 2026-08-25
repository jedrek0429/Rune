mod bindings {
    wit_bindgen::generate!({
        path: "../../../wit",
        world: "message-delete-rune",
    });
}

struct Component;

impl bindings::Guest for Component {
    fn handle(args: bindings::rune::api::types::MessageDeleteEventArgs) {
        bindings::reply(&format!(
            "{}|{}|{}",
            args.channel_id,
            args.guild_id
                .map_or_else(|| "none".to_owned(), |id| id.to_string()),
            args.message_id
        ));
    }
}

bindings::export!(Component with_types_in bindings);
