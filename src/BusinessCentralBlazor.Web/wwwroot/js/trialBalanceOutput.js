export function download(filename, csv) {
    const url = URL.createObjectURL(new Blob(["\ufeff", csv], { type: "text/csv;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export async function print(paper) {
    if (!paper) throw new Error("Report preview is not available.");
    const frame = document.createElement("iframe");
    frame.title = "Trial Balance print document";
    frame.style.cssText = "position:fixed;left:-10000px;top:0;width:1100px;height:800px;border:0";
    document.body.append(frame);
    const doc = frame.contentDocument;
    if (!doc) { frame.remove(); throw new Error("Print document could not be created."); }
    doc.title = "Trial Balance";
    const style = doc.createElement("style");
    style.textContent = `
        body{font:12px "Segoe UI",sans-serif;color:#263e4d;margin:0}
        h1{font-size:27px;font-weight:400}.report-meta{border-block:1px solid #ccc;padding:12px 0;margin:18px 0;line-height:1.8}
        table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:10px 8px;border-bottom:1px solid #ddd}
        .number{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}thead{display:table-header-group}
        tfoot{display:table-row-group}tr{break-inside:avoid}.visually-hidden{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)}
        @page{size:A4 landscape;margin:18mm}
    `;
    doc.head.append(style);
    const clone = paper.cloneNode(true);
    clone.removeAttribute("id");
    doc.body.append(clone);
    try {
        await doc.fonts.ready;
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        const view = frame.contentWindow;
        if (!view) throw new Error("Browser print window is not available.");
        view.addEventListener("afterprint", () => frame.remove(), { once: true });
        view.focus();
        view.print();
        setTimeout(() => frame.remove(), 60000);
    } catch (error) {
        frame.remove();
        throw error;
    }
}
