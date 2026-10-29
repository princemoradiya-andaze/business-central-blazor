document.addEventListener("submit", event => {
    if (!event.defaultPrevented) document.getElementById("account-loader").hidden = false;
});
window.addEventListener("pageshow", () => { document.getElementById("account-loader").hidden = true; });
