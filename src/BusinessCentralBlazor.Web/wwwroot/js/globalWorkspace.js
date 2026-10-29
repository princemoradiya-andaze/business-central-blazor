(() => {
    const dialogs = [];
    const backgroundStates = new Map();
    const shade = document.createElement("div");
    shade.className = "workspace-layer-shade";
    shade.hidden = true;
    document.body.append(shade);
    let navigating = false;
    let startup = true;
    let lastFocus = null;
    let wasLoading = false;
    const loader = document.getElementById("workspace-transition-loader");
    const visible = element => element && element.getClientRects().length > 0;
    const busy = () => startup || navigating || [...document.querySelectorAll(".app-loading:not(#workspace-transition-loader)")].some(visible);
    function syncBackground(loading) {
        if (!loading && !dialogs.length) return;
        const allowed = [loader, shade, document.getElementById("blazor-error-ui"), document.getElementById("components-reconnect-modal")];
        if (!loading) allowed.push(document.querySelector(".global-header"), dialogs.at(-1)?.element);
        const roots = allowed.filter(Boolean);
        function visit(parent) {
            for (const element of parent.children) {
                if (roots.includes(element)) continue;
                if (roots.some(root => element.contains(root))) visit(element);
                else {
                    backgroundStates.set(element, element.inert);
                    element.inert = true;
                }
            }
        }
        visit(document.body);
    }
    function sync() {
        backgroundStates.forEach((inert, element) => { element.inert = inert; });
        backgroundStates.clear();
        for (let i = dialogs.length - 1; i >= 0; i--) {
            if (!dialogs[i].element.isConnected || !dialogs[i].element.open) dialogs.splice(i, 1);
        }
        dialogs.forEach((item, index) => {
            item.element.style.zIndex = String(110 + index * 2);
            item.element.inert = index !== dialogs.length - 1;
        });
        shade.hidden = dialogs.length === 0;
        shade.style.zIndex = String(109 + (dialogs.length - 1) * 2);
        const loading = busy();
        if (loading && !wasLoading) lastFocus = document.activeElement;
        loader.hidden = !loading;
        syncBackground(loading);
        document.documentElement.classList.toggle("workspace-scroll-locked", loading || dialogs.length > 0);
        if (loading && !wasLoading) {
            loader.focus({ preventScroll: true });
        } else if (!loading && wasLoading && lastFocus?.isConnected && !lastFocus.closest("[inert]")) {
            lastFocus.focus({ preventScroll: true });
        }
        wasLoading = loading;
    }
    const focusable = root => [...root.querySelectorAll("a[href],button,input,select,textarea,[tabindex]")]
        .filter(el => !el.disabled && el.tabIndex >= 0 && !el.closest("[hidden],[inert]") && visible(el));
    document.addEventListener("keydown", event => {
        if (busy()) {
            if (event.key === "Tab") event.preventDefault();
            return;
        }
        const menuToggle = document.querySelector('.global-header button[aria-expanded="true"][aria-controls]');
        if (event.key === "Escape" && menuToggle) {
            event.preventDefault();
            event.stopImmediatePropagation();
            menuToggle.click();
            menuToggle.focus();
            return;
        }
        const top = dialogs.at(-1);
        if (!top) return;
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopImmediatePropagation();
            top.close();
        } else if (event.key === "Tab") {
            const header = document.querySelector(".global-header");
            const items = [...(header ? focusable(header) : []), ...focusable(top.element)];
            if (!items.length) { event.preventDefault(); return; }
            const index = items.indexOf(document.activeElement);
            event.preventDefault();
            items[(index + (event.shiftKey ? -1 : 1) + items.length) % items.length].focus();
        }
    }, true);
    document.addEventListener("focusin", event => {
        if (busy()) return;
        const top = dialogs.at(-1);
        if (top && !top.element.contains(event.target) && !event.target.closest(".global-header")) {
            (focusable(top.element)[0] ?? top.element).focus({ preventScroll: true });
        }
    });
    document.addEventListener("submit", event => {
        if (event.defaultPrevented || event.target.matches("[data-enhance]") ||
            event.target.getAttribute("method")?.toLowerCase() !== "post") return;
        navigating = true;
        sync();
    });
    // Native unload prompts can be canceled without a window focus event.
    window.addEventListener("beforeunload", event => {
        setTimeout(() => {
            if (event.defaultPrevented || event.returnValue) {
                navigating = false;
                sync();
            }
        }, 0);
    });
    window.addEventListener("focus", () => { if (navigating) { navigating = false; sync(); } });
    window.addEventListener("pageshow", () => { navigating = false; sync(); });
    const observer = new MutationObserver(records => {
        if (records.some(record => record.type === "childList" ||
            (record.target !== loader && record.target !== shade && record.target !== document.documentElement))) sync();
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden"] });
    window.globalWorkspace = {
        resetContentScroll() { document.querySelector(".workspace-main")?.scrollTo(0, 0); },
        ready() { if (startup) { startup = false; sync(); } },
        registerDialog(element, close) {
            dialogs.push({ element, close });
            sync();
            (element.querySelector("[autofocus]") ?? focusable(element)[0] ?? element).focus({ preventScroll: true });
        },
        unregisterDialog(element) {
            const index = dialogs.findIndex(item => item.element === element);
            if (index >= 0) dialogs.splice(index, 1);
            sync();
        },
        submitCompany(form) { form.requestSubmit(); },
        beginNavigation() { navigating = true; sync(); },
        endNavigation() { navigating = false; sync(); }
    };
    sync();
})();
