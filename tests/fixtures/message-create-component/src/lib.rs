mod bindings {
    wit_bindgen::generate!({
        path: "../../../wit",
        world: "message-create-rune",
    });
}

struct Component;

impl bindings::Guest for Component {
    fn handle_message_create(author_username: String) {
        bindings::reply(&format!("Hello, {author_username}!"));
    }
}

bindings::export!(Component with_types_in bindings);
