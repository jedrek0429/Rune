use std::hint::black_box;
use std::time::{Duration, Instant};

use rune_runtime_javascript_experiment::{EventType, JavaScriptLimits, SharedJavaScriptRuntime};

const MESSAGE: &[u8] = br#"{
    "id": 18446744073709551615,
    "channelId": 9223372036854775808,
    "content": "!native-test",
    "author": {
        "id": 9007199254740993,
        "username": "benchmark"
    }
}"#;

const SOURCE: &str = r#"
    if (message.content === "!native-test") {
        await message.reply(`Hello, ${message.author.username}`);
    }
"#;

fn main() {
    let iterations = std::env::args().nth(1).map_or(1_000, |value| {
        value.parse().expect("iterations must be an integer")
    });
    assert!(iterations > 0, "iterations must be greater than zero");

    let started = Instant::now();
    let runtime =
        SharedJavaScriptRuntime::new(JavaScriptLimits::default()).expect("QuickJS must initialise");
    let initialisation = started.elapsed();

    let mut registrations = Vec::with_capacity(iterations);
    for index in 0..iterations {
        let started = Instant::now();
        runtime
            .register(index as u128, EventType::MessageCreate, SOURCE)
            .expect("the benchmark rune must register");
        registrations.push(started.elapsed());
    }

    let mut invocations = Vec::with_capacity(iterations);
    for index in 0..iterations {
        let started = Instant::now();
        let actions = runtime
            .invoke(index as u128, EventType::MessageCreate, MESSAGE)
            .expect("the benchmark rune must execute");
        black_box(actions);
        invocations.push(started.elapsed());
    }

    println!("shared QuickJS interpreter ({iterations} runes)");
    println!("  initialise: {}", micros(initialisation));
    print_distribution("register", &mut registrations);
    print_distribution("invoke", &mut invocations);
    println!("  live heap:  {} bytes", runtime.memory_used_bytes());
}

fn print_distribution(label: &str, samples: &mut [Duration]) {
    samples.sort_unstable();
    let total = samples.iter().sum::<Duration>();
    let mean = total / u32::try_from(samples.len()).expect("sample count must fit in u32");
    let median = samples[samples.len() / 2];
    let p95 = samples[(samples.len() * 95 / 100).min(samples.len() - 1)];

    println!(
        "  {label:<10} mean {:>10}, median {:>10}, p95 {:>10}",
        micros(mean),
        micros(median),
        micros(p95)
    );
}

fn micros(duration: Duration) -> String {
    format!("{:.1} us", duration.as_secs_f64() * 1_000_000.0)
}
