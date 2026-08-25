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

        if author_username == "fuel" {
            let mut value = 0_u64;
            for index in 0..50_000_000_u64 {
                value = std::hint::black_box(value.wrapping_add(index));
            }
            bindings::reply(&value.to_string());
            return;
        }

        if author_username == "memory" {
            bindings::reply("discard me");
            let allocation = vec![0_u8; 32 * 1024 * 1024];
            std::hint::black_box(&allocation);
            bindings::reply("memory allocation succeeded");
            return;
        }

        bindings::reply(&format!("Hello, {author_username}!"));
        bindings::reply("Welcome to Rune.");
    }
}

bindings::export!(Component with_types_in bindings);
