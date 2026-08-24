use std::ffi::c_void;
use std::mem;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;
use std::sync::Mutex;

use wasmtime::component::{Component, HasSelf, Linker, ResourceTable};
use wasmtime::{Engine, Store};
use wasmtime_wasi::{WasiCtx, WasiCtxView, WasiView};

const ABI_VERSION: u32 = 2;
const STATUS_OK: i32 = 0;
const STATUS_INVALID_ARGUMENT: i32 = 1;
const STATUS_RUNTIME_ERROR: i32 = 2;
const STATUS_PANIC: i32 = 3;
const ACTION_REPLY: u32 = 1;

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
/// `actions` must be writable. A successful action list must be released
/// with `rune_runtime_action_list_free`.
#[no_mangle]
pub unsafe extern "C" fn rune_runtime_invoke_message_create(
    runtime: *mut c_void,
    author_data: *const u8,
    author_len: usize,
    actions: *mut RuneActionList,
) -> i32 {
    if actions.is_null() {
        return STATUS_INVALID_ARGUMENT;
    }

    unsafe {
        actions.write(RuneActionList::EMPTY);
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
        bindings::MessageCreateRune::add_to_linker::<_, HasSelf<_>>(&mut linker, |state| state)
            .map_err(|_| STATUS_RUNTIME_ERROR)?;
        wasmtime_wasi::p2::add_to_linker_sync(&mut linker).map_err(|_| STATUS_RUNTIME_ERROR)?;

        let mut store = Store::new(&runtime.engine, InvocationState::new());
        let bindings = bindings::MessageCreateRune::instantiate(&mut store, &component, &linker)
            .map_err(|_| STATUS_RUNTIME_ERROR)?;
        bindings
            .call_handle_message_create(&mut store, author)
            .map_err(|_| STATUS_RUNTIME_ERROR)?;

        let replies = mem::take(&mut store.data_mut().replies);
        unsafe {
            actions.write(RuneActionList::from_replies(replies));
        }

        Ok(())
    })
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
    let mut actions = unsafe { Box::from_raw(slice) };
    for action in &mut actions {
        let content = mem::replace(&mut action.content, RuneBuffer::EMPTY);
        unsafe {
            free_buffer(content);
        }
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

unsafe fn free_buffer(buffer: RuneBuffer) {
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
