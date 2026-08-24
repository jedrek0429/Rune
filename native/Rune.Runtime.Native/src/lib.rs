use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;
use std::sync::Mutex;

use wasmtime::component::{Component, HasSelf, Linker};
use wasmtime::{Engine, Store};

const ABI_VERSION: u32 = 1;
const STATUS_OK: i32 = 0;
const STATUS_INVALID_ARGUMENT: i32 = 1;
const STATUS_RUNTIME_ERROR: i32 = 2;
const STATUS_PANIC: i32 = 3;

mod bindings {
    wasmtime::component::bindgen!({
        path: "../../wit",
        world: "message-create-rune",
    });
}

struct RuneRuntime {
    engine: Engine,
    component: Mutex<Option<Component>>,
}

#[derive(Default)]
struct InvocationState {
    replies: Vec<String>,
}

impl bindings::MessageCreateRuneImports for InvocationState {
    fn reply(&mut self, content: String) {
        self.replies.push(content);
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
}

#[no_mangle]
pub extern "C" fn rune_runtime_abi_version() -> u32 {
    ABI_VERSION
}

#[no_mangle]
pub extern "C" fn rune_runtime_create() -> *mut c_void {
    match catch_unwind(|| {
        Box::into_raw(Box::new(RuneRuntime {
            engine: Engine::default(),
            component: Mutex::new(None),
        }))
        .cast()
    }) {
        Ok(runtime) => runtime,
        Err(_) => ptr::null_mut(),
    }
}

/// # Safety
///
/// `runtime` must be a handle returned by `rune_runtime_create`, and
/// `component_data` must identify `component_len` readable bytes.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_load_component(
    runtime: *mut c_void,
    component_data: *const u8,
    component_len: usize,
) -> i32 {
    ffi_status(|| {
        let runtime = unsafe { runtime_ref(runtime) }.ok_or(STATUS_INVALID_ARGUMENT)?;
        let bytes = unsafe { bytes_from_raw(component_data, component_len) }
            .ok_or(STATUS_INVALID_ARGUMENT)?;
        let component =
            Component::new(&runtime.engine, bytes).map_err(|_| STATUS_RUNTIME_ERROR)?;
        let mut loaded = runtime
            .component
            .lock()
            .map_err(|_| STATUS_RUNTIME_ERROR)?;
        *loaded = Some(component);
        Ok(())
    })
}

/// # Safety
///
/// `runtime` must be a handle returned by `rune_runtime_create`,
/// `author_data` must identify `author_len` readable bytes, and
/// `reply` must be writable. A successful buffer must be released with
/// `rune_runtime_buffer_free`.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_invoke_message_create(
    runtime: *mut c_void,
    author_data: *const u8,
    author_len: usize,
    reply: *mut RuneBuffer,
) -> i32 {
    if reply.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    unsafe {
        reply.write(RuneBuffer::EMPTY);
    }

    ffi_status(|| {
        let runtime = unsafe { runtime_ref(runtime) }.ok_or(STATUS_INVALID_ARGUMENT)?;
        let author_bytes =
            unsafe { bytes_from_raw(author_data, author_len) }.ok_or(STATUS_INVALID_ARGUMENT)?;
        let author = std::str::from_utf8(author_bytes).map_err(|_| STATUS_INVALID_ARGUMENT)?;
        let component = runtime
            .component
            .lock()
            .map_err(|_| STATUS_RUNTIME_ERROR)?
            .clone()
            .ok_or(STATUS_RUNTIME_ERROR)?;

        let mut linker = Linker::new(&runtime.engine);
        bindings::MessageCreateRune::add_to_linker::<_, HasSelf<_>>(
            &mut linker,
            |state| state,
        )
        .map_err(|_| STATUS_RUNTIME_ERROR)?;

        let mut store = Store::new(&runtime.engine, InvocationState::default());
        let bindings =
            bindings::MessageCreateRune::instantiate(&mut store, &component, &linker)
                .map_err(|_| STATUS_RUNTIME_ERROR)?;
        bindings
            .call_handle_message_create(&mut store, author)
            .map_err(|_| STATUS_RUNTIME_ERROR)?;

        let content = store
            .data()
            .replies
            .first()
            .cloned()
            .ok_or(STATUS_RUNTIME_ERROR)?;
        let bytes = content.into_bytes().into_boxed_slice();
        let len = bytes.len();
        let data = Box::into_raw(bytes).cast::<u8>();

        unsafe {
            reply.write(RuneBuffer { data, len });
        }

        Ok(())
    })
}

/// # Safety
///
/// `buffer` must either be empty or have been returned by a successful call
/// to `rune_runtime_invoke_message_create`, and must be released only once.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_buffer_free(buffer: RuneBuffer) {
    if buffer.data.is_null() {
        return;
    }

    let slice = ptr::slice_from_raw_parts_mut(buffer.data, buffer.len);
    unsafe {
        drop(Box::from_raw(slice));
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

        let status = unsafe { rune_runtime_load_component(runtime, ptr::null(), 1) };

        assert_eq!(status, STATUS_INVALID_ARGUMENT);
        unsafe {
            rune_runtime_destroy(runtime);
        }
    }
}
