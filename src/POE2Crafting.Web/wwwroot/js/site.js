// File download helper for save/load
window.downloadFile = function (fileName, base64) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Place an info popover (position: fixed) next to its anchor, inside the viewport, so scroll containers don't clip it
window.positionPopover = function (anchor, popover) {
    if (!anchor || !popover) return;
    const a = anchor.getBoundingClientRect(), p = popover.getBoundingClientRect(), margin = 8;
    // directly adjacent to the anchor, so the pointer can move into the box without leaving the hover area
    let top = a.bottom;
    if (top + p.height > window.innerHeight - margin) top = Math.max(margin, a.top - p.height);
    const left = Math.min(Math.max(margin, a.left), window.innerWidth - p.width - margin);
    popover.style.top = top + 'px';
    popover.style.left = left + 'px';
    popover.style.visibility = 'visible';
};

// Mermaid diagram renderer for Blazor interop
window.renderMermaid = async function (container, definition, id) {
    if (!container || !definition) return;
    try {
        const { svg } = await mermaid.render(id, definition);
        container.innerHTML = svg;
    } catch (e) {
        container.innerHTML = '<pre class="mermaid-error-text">' +
            e.message.replace(/</g, '&lt;') + '</pre>';
    }
};
