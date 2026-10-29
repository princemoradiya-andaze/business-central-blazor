document.getElementById("company-search")?.addEventListener("input", event => {
    const buttons = [...document.querySelectorAll("#company-choices button")];
    buttons.forEach(button => { button.hidden = !button.textContent.toLowerCase().includes(event.target.value.toLowerCase()); });
    document.getElementById("no-company-matches").hidden = buttons.some(button => !button.hidden);
});
