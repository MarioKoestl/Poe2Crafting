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
