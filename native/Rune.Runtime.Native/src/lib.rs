const ABI_VERSION: u32 = 1;

#[no_mangle]
pub extern "C" fn rune_runtime_abi_version() -> u32 {
    ABI_VERSION
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reports_current_abi_version() {
        assert_eq!(rune_runtime_abi_version(), ABI_VERSION);
    }
}
