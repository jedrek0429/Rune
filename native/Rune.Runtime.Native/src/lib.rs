use std::ffi::c_void;
use std::fmt;
use std::mem;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;
use std::sync::Mutex;

use wasmtime::component::{Component, HasSelf, Linker, ResourceTable};
use wasmtime::{Config, Engine, Store};
use wasmtime_wasi::{WasiCtx, WasiCtxView, WasiView};

const ABI_VERSION: u32 = 3;
const STATUS_OK: i32 = 0;
const STATUS_INVALID_ARGUMENT: i32 = 1;
const STATUS_RUNTIME_ERROR: i32 = 2;
const STATUS_PANIC: i32 = 3;
const ACTION_REPLY: u32 = 1;
const INVOCATION_FUEL: u64 = 1_000_000;

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

struct InvocationState {
    replies: Vec<String>,
    wasi: WasiCtx,
    resources: ResourceTable,
}

impl InvocationState {
    fn new() -> Self {
        Self {
            replies: Vec::new(),
            wasi: WasiCtx::builder().build(),
            resources: ResourceTable::new(),
        }
    }
}

impl bindings::MessageCreateRuneImports for InvocationState {
    fn reply(&mut self, content: String) {
        self.replies.push(content);
    }
}

impl WasiView for InvocationState {
    fn ctx(&mut self) -> WasiCtxView<'_> {
        WasiCtxView {
            ctx: &mut self.wasi,
            table: &mut self.resources,
        }
    }
}

struct NativeFailure {
    status: i32,
    detail: String,
}

impl NativeFailure {
    fn invalid(detail: impl Into<String>) -> Self {
        Self {
            status: STATUS_INVALID_ARGUMENT,
            detail: detail.into(),
        }
    }

    fn runtime(error: impl fmt::Display) -> Self {
        Self {
            status: STATUS_RUNTIME_ERROR,
            detail: format!("{error:#}"),
        }
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

    fn from_bytes(bytes: Vec<u8>) -> Self {
        if bytes.is_empty() {
            return Self::EMPTY;
        }

        let mut bytes = bytes.into_boxed_slice();
        let buffer = Self {
            data: bytes.as_mut_ptr(),
            len: bytes.len(),
        };
        mem::forget(bytes);
        buffer
    }
}

#[repr(C)]
pub struct RuneAction {
    kind: u32,
    content: RuneBuffer,
}

#[repr(C)]
pub struct RuneActionList {
    data: *mut RuneAction,
    len: usize,
}

impl RuneActionList {
    const EMPTY: Self = Self {
        data: ptr::null_mut(),
        len: 0,
    };

    fn from_replies(replies: Vec<String>) -> Self {
        if replies.is_empty() {
            return Self::EMPTY;
        }

        let mut actions = replies
            .into_iter()
            .map(|content| RuneAction {
                kind: ACTION_REPLY,
                content: RuneBuffer::from_bytes(content.into_bytes()),
            })
            .collect::<Vec<_>>()
            .into_boxed_slice();
        let list = Self {
            data: actions.as_mut_ptr(),
            len: actions.len(),
        };
        mem::forget(actions);
        list
    }
}

#[no_mangle]
pub extern "C" fn rune_runtime_abi_version() -> u32 {
    ABI_VERSION
}

#[no_mangle]
pub extern "C" fn rune_runtime_create() -> *mut c_void {
    match catch_unwind(|| {
        let mut config = Config::new();
        config.consume_fuel(true);
        let engine = Engine::new(&config)?;
        let runtime = Box::into_raw(Box::new(RuneRuntime {
            engine,
            component: Mutex::new(None),
        }));

        Ok::<*mut c_void, wasmtime::Error>(runtime.cast())
    }) {
        Ok(Ok(runtime)) => runtime,
        Ok(Err(_)) | Err(_) => ptr::null_mut(),
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
        let component = Component::new(&runtime.engine, bytes).map_err(|_| STATUS_RUNTIME_ERROR)?;
        let mut loaded = runtime.component.lock().map_err(|_| STATUS_RUNTIME_ERROR)?;
        *loaded = Some(component);
        Ok(())
    })
}

/// # Safety
///
/// `runtime` must be a handle returned by `rune_runtime_create`,
/// `author_data` must identify `author_len` readable bytes, and
/// `actions` and `error` must be writable. A successful action list must
/// be released with `rune_runtime_action_list_free`; an error buffer must
/// be released with `rune_runtime_buffer_free`.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_invoke_message_create(
    runtime: *mut c_void,
    author_data: *const u8,
    author_len: usize,
    actions: *mut RuneActionList,
    error: *mut RuneBuffer,
) -> i32 {
    if actions.is_null() || error.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    unsafe {
        actions.write(RuneActionList::EMPTY);
        error.write(RuneBuffer::EMPTY);
    }

    let outcome = catch_unwind(AssertUnwindSafe(|| unsafe {
        invoke_message_create(runtime, author_data, author_len)
    }));

    match outcome {
        Ok(Ok(list)) => {
            unsafe {
                actions.write(list);
            }
            STATUS_OK
        }
        Ok(Err(failure)) => {
            let status = failure.status;
            unsafe {
                error.write(RuneBuffer::from_bytes(failure.detail.into_bytes()));
            }
            status
        }
        Err(_) => {
            unsafe {
                error.write(RuneBuffer::from_bytes(
                    b"the native runtime panicked".to_vec(),
                ));
            }
            STATUS_PANIC
        }
    }
}

/// # Safety
///
/// `list` must either be empty or have been returned by a successful call
/// to `rune_runtime_invoke_message_create`, and must be released only once.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_action_list_free(list: RuneActionList) {
    if list.data.is_null() {
        return;
    }

    let slice = ptr::slice_from_raw_parts_mut(list.data, list.len);
    let actions = unsafe { Box::from_raw(slice) };
    for action in &actions {
        unsafe {
            free_buffer(&action.content);
        }
    }
}

/// # Safety
///
/// `buffer` must either be empty or have been returned by this library, and
/// must be released only once.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_buffer_free(buffer: RuneBuffer) {
    unsafe {
        free_buffer(&buffer);
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

unsafe fn invoke_message_create(
    runtime: *mut c_void,
    author_data: *const u8,
    author_len: usize,
) -> Result<RuneActionList, NativeFailure> {
    let runtime = unsafe { runtime_ref(runtime) }
        .ok_or_else(|| NativeFailure::invalid("the runtime handle is null"))?;
    let author_bytes = unsafe { bytes_from_raw(author_data, author_len) }
        .ok_or_else(|| NativeFailure::invalid("the author buffer is null"))?;
    let author = std::str::from_utf8(author_bytes)
        .map_err(|error| NativeFailure::invalid(error.to_string()))?;
    let component = runtime
        .component
        .lock()
        .map_err(|_| NativeFailure::runtime("the component lock is poisoned"))?
        .clone()
        .ok_or_else(|| NativeFailure::runtime("no component is loaded"))?;

    let mut linker = Linker::new(&runtime.engine);
    bindings::MessageCreateRune::add_to_linker::<_, HasSelf<_>>(&mut linker, |state| state)
        .map_err(NativeFailure::runtime)?;
    wasmtime_wasi::p2::add_to_linker_sync(&mut linker).map_err(NativeFailure::runtime)?;

    let mut store = Store::new(&runtime.engine, InvocationState::new());
    store
        .set_fuel(INVOCATION_FUEL)
        .map_err(NativeFailure::runtime)?;
    let bindings = bindings::MessageCreateRune::instantiate(&mut store, &component, &linker)
        .map_err(NativeFailure::runtime)?;
    bindings
        .call_handle_message_create(&mut store, author)
        .map_err(NativeFailure::runtime)?;

    let replies = mem::take(&mut store.data_mut().replies);
    Ok(RuneActionList::from_replies(replies))
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

unsafe fn free_buffer(buffer: &RuneBuffer) {
    if buffer.data.is_null() {
        return;
    }

    let slice = ptr::slice_from_raw_parts_mut(buffer.data, buffer.len);
    unsafe {
        drop(Box::from_raw(slice));
    }
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

    #[test]
    fn creates_ordered_reply_actions() {
        let list = RuneActionList::from_replies(vec!["first".to_owned(), "second".to_owned()]);

        let actions = unsafe { slice::from_raw_parts(list.data, list.len) };
        assert_eq!(actions.len(), 2);
        assert_eq!(actions[0].kind, ACTION_REPLY);
        assert_eq!(actions[1].kind, ACTION_REPLY);

        unsafe {
            rune_runtime_action_list_free(list);
        }
    }
}
