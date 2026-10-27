const listeners = new WeakMap();

export function open(dialog, reference) {
    const cancel = event => {
        event.preventDefault();
        reference.invokeMethodAsync("RequestClose");
    };
    listeners.set(dialog, cancel);
    dialog.addEventListener("cancel", cancel);
    dialog.showModal();
}

export async function cleanup(dialog, returnFocusSelector, returnFocusFallbackSelector) {
    const cancel = listeners.get(dialog);
    if (cancel) dialog.removeEventListener("cancel", cancel);
    listeners.delete(dialog);
    if (dialog.open) dialog.close();
    await new Promise(requestAnimationFrame);
    const target = document.querySelector(returnFocusSelector);
    if (target && !target.matches(":disabled")) {
        target.focus();
    } else if (returnFocusFallbackSelector) {
        document.querySelector(returnFocusFallbackSelector)?.focus();
    }
}
