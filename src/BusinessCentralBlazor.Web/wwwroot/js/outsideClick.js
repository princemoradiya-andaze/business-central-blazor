const listeners = new Map();
let nextListenerId = 0;

export function bindOutsideClick(element, dotNetReference, callbackName) {
    const listenerId = ++nextListenerId;
    const handler = event => {
        if (!element.contains(event.target)) {
            void dotNetReference.invokeMethodAsync(callbackName);
        }
    };

    document.addEventListener("pointerdown", handler, true);
    listeners.set(listenerId, handler);
    return listenerId;
}

export function unbindOutsideClick(listenerId) {
    const handler = listeners.get(listenerId);
    if (handler) {
        document.removeEventListener("pointerdown", handler, true);
        listeners.delete(listenerId);
    }
}
