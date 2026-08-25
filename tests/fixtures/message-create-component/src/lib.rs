mod bindings {
    wit_bindgen::generate!({
        path: "../../../wit",
        world: "message-create-rune",
    });
}

struct Component;

impl bindings::Guest for Component {
    fn handle(message: bindings::rune::api::types::Message) {
        let author_username = message.author.username;

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

        if author_username == "output-boundary" {
            bindings::reply(&"x".repeat(8 * 1024));
            for _ in 0..14 {
                bindings::reply(&"x".repeat(4 * 1024));
            }
            bindings::reply("");
            return;
        }

        if author_username == "action-limit" {
            bindings::reply("discard me");
            for _ in 0..16 {
                bindings::reply("");
            }
            return;
        }

        if author_username == "reply-size-limit" {
            bindings::reply("discard me");
            bindings::reply(&"x".repeat(8 * 1024 + 1));
            return;
        }

        if author_username == "total-output-limit" {
            bindings::reply("discard me");
            for _ in 0..8 {
                bindings::reply(&"x".repeat(8 * 1024));
            }
            return;
        }

        bindings::reply(&format!(
            "{}|{}|{}|{}|{}",
            message.id, message.channel_id, message.content, message.author.id, author_username
        ));
        bindings::reply("Welcome to Rune.");
    }
}

bindings::export!(Component with_types_in bindings);
