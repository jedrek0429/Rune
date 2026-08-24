use std::collections::HashMap;

#[derive(Debug, Clone, Copy)]
enum Command<'a> {
    Help,
    Stats(&'a str),
    Count(&'a str),
    Reverse(&'a str),
    Analyse(&'a str),
    Fibonacci(u32),
    Unknown,
}

#[derive(Debug)]
struct TextStats {
    characters: usize,
    words: usize,
    unique_words: usize,
    longest_word: Option<String>,
}

fn parse_command(input: &str) -> Command<'_> {
    let input = input.trim();

    match input {
        "!rust" | "!rust help" => Command::Help,

        _ if input.starts_with("!rust stats ") =>
            Command::Stats(
                input.trim_start_matches("!rust stats ")
            ),

        _ if input.starts_with("!rust count ") =>
            Command::Count(
                input.trim_start_matches("!rust count ")
            ),

        _ if input.starts_with("!rust reverse ") =>
            Command::Reverse(
                input.trim_start_matches("!rust reverse ")
            ),

        _ if input.starts_with("!rust analyse ") =>
            Command::Analyse(
                input.trim_start_matches("!rust analyse ")
            ),

        _ if input.starts_with("!rust fib ") => {
            input
                .trim_start_matches("!rust fib ")
                .parse::<u32>()
                .ok()
                .filter(|n| *n <= 40)
                .map(Command::Fibonacci)
                .unwrap_or(Command::Unknown)
        }

        _ => Command::Unknown,
    }
}

fn normalise_word(word: &str) -> String {
    word
        .trim_matches(|c: char| !c.is_alphanumeric())
        .to_lowercase()
}

fn analyse_text(text: &str) -> TextStats {
    let words: Vec<String> = text
        .split_whitespace()
        .map(normalise_word)
        .filter(|word| !word.is_empty())
        .collect();

    let mut frequencies = HashMap::new();

    for word in &words {
        *frequencies
            .entry(word.clone())
            .or_insert(0usize) += 1;
    }

    let longest_word = words
        .iter()
        .max_by_key(|word| word.chars().count())
        .cloned();

    TextStats {
        characters: text.chars().count(),
        words: words.len(),
        unique_words: frequencies.len(),
        longest_word,
    }
}

fn count_words(text: &str) -> Vec<(String, usize)> {
    let mut frequencies =
        HashMap::<String, usize>::new();

    for word in text
        .split_whitespace()
        .map(normalise_word)
        .filter(|word| !word.is_empty())
    {
        *frequencies
            .entry(word)
            .or_insert(0) += 1;
    }

    let mut result: Vec<_> =
        frequencies.into_iter().collect();

    result.sort_by(
        |(word_a, count_a), (word_b, count_b)| {
            count_b
                .cmp(count_a)
                .then_with(|| word_a.cmp(word_b))
        },
    );

    result
}

fn fibonacci(n: u32) -> u64 {
    match n {
        0 => 0,
        1 => 1,

        _ => {
            let (mut a, mut b) = (0u64, 1u64);

            for _ in 2..=n {
                (a, b) = (b, a + b);
            }

            b
        }
    }
}

fn character_analysis(text: &str) -> String {
    let mut letters = 0usize;
    let mut digits = 0usize;
    let mut whitespace = 0usize;
    let mut symbols = 0usize;

    for character in text.chars() {
        match character {
            c if c.is_alphabetic() =>
                letters += 1,

            c if c.is_numeric() =>
                digits += 1,

            c if c.is_whitespace() =>
                whitespace += 1,

            _ =>
                symbols += 1,
        }
    }

    format!(
        "**Character analysis**\n\
         Letters: {letters}\n\
         Digits: {digits}\n\
         Whitespace: {whitespace}\n\
         Symbols: {symbols}"
    )
}

fn format_stats(text: &str) -> String {
    let stats = analyse_text(text);

    let longest = stats
        .longest_word
        .as_deref()
        .unwrap_or("none");

    format!(
        "**Text statistics**\n\
         Characters: {}\n\
         Words: {}\n\
         Unique words: {}\n\
         Longest word: `{}`",
        stats.characters,
        stats.words,
        stats.unique_words,
        longest
    )
}

fn format_word_counts(text: &str) -> String {
    let counts = count_words(text);

    if counts.is_empty() {
        return "No words found.".to_string();
    }

    let rows = counts
        .into_iter()
        .take(10)
        .enumerate()
        .map(|(index, (word, count))| {
            format!(
                "{}. `{}` × {}",
                index + 1,
                word,
                count
            )
        })
        .collect::<Vec<_>>()
        .join("\n");

    format!("**Most common words**\n{rows}")
}

fn help(message: &RuneMessage) -> String {
    format!(
        "**Rust Rune showcase**\n\
         Hello, **{}**.\n\n\
         This Rune demonstrates enums, pattern matching, \
         structs, iterators, closures, `Option`, `HashMap`, \
         sorting, ownership, borrowing and numeric algorithms.\n\n\
         `!rust stats <text>`\n\
         `!rust count <text>`\n\
         `!rust reverse <text>`\n\
         `!rust analyse <text>`\n\
         `!rust fib <0-40>`\n\n\
         Message: `{}`\n\
         Channel: `{}`",
        message.author.username,
        message.id,
        message.channel_id
    )
}

fn rune(message: RuneMessage) -> FnResult<()> {
    let response =
        match parse_command(&message.content) {
            Command::Help =>
                help(&message),

            Command::Stats(text) =>
                format_stats(text),

            Command::Count(text) =>
                format_word_counts(text),

            Command::Reverse(text) => {
                let reversed: String =
                    text.chars().rev().collect();

                format!(
                    "**Reversed**\n{}",
                    reversed
                )
            }

            Command::Analyse(text) =>
                character_analysis(text),

            Command::Fibonacci(n) => {
                let value = fibonacci(n);

                format!(
                    "**Fibonacci**\n\
                     F({n}) = `{value}`"
                )
            }

            Command::Unknown =>
                return Ok(()),
        };

    message.reply(response)?;

    Ok(())
}
