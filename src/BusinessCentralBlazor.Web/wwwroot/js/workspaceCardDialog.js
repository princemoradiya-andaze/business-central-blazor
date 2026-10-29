const listeners = new WeakMap();

export function open(dialog, reference) {
    const cancel = event => {
        event.preventDefault();
        reference.invokeMethodAsync("RequestClose");
    };
    listeners.set(dialog, cancel);
    dialog.addEventListener("cancel", cancel);
    dialog.show();
    window.globalWorkspace.registerDialog(dialog, () => reference.invokeMethodAsync("RequestClose"));
}

export async function cleanup(dialog, returnFocusSelector, returnFocusFallbackSelector) {
    const cancel = listeners.get(dialog);
    if (cancel) dialog.removeEventListener("cancel", cancel);
    listeners.delete(dialog);
    window.globalWorkspace.unregisterDialog(dialog);
    if (dialog.open) dialog.close();
    await new Promise(requestAnimationFrame);
    const target = document.querySelector(returnFocusSelector);
    if (target && !target.matches(":disabled") && target.getClientRects().length > 0 && !target.closest("[hidden],[inert]")) {
        target.focus();
    } else if (returnFocusFallbackSelector) {
        document.querySelector(returnFocusFallbackSelector)?.focus();
    }
}
