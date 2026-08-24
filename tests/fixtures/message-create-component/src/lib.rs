mod bindings {
    wit_bindgen::generate!({
        path: "../../../wit",
        world: "message-create-rune",
    });
}

struct Component;

impl bindings::Guest for Component {
    fn handle_message_create(author_username: String) {
        if author_username == "trap" {
            bindings::reply("discard me");
            panic!("intentional integration-test trap");
        }

        bindings::reply(&format!("Hello, {author_username}!"));
        bindings::reply("Welcome to Rune.");
    }
}

bindings::export!(Component with_types_in bindings);
