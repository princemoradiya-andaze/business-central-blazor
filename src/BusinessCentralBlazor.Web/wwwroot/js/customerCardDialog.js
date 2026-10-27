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

export function cleanup(dialog) {
    const cancel = listeners.get(dialog);
    if (cancel) dialog.removeEventListener("cancel", cancel);
    listeners.delete(dialog);
    if (dialog.open) dialog.close();
    document.querySelector(".customer-actions a[href='/financial/sales/customers/new']")?.focus();
}
