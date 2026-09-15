// Info popover of a crafting item name (CraftItemLink): shown by CSS on hover, placed here (position: fixed) next to its name
// and inside the viewport, so scroll containers don't clip it. Called from the element's onmouseenter, no server round-trip.
window.positionPopover = function (wrap) {
    const anchor = wrap && wrap.querySelector('.craft-link');
    const popover = wrap && wrap.querySelector('.craft-popover');
    if (!anchor || !popover) return;
    const a = anchor.getBoundingClientRect(), p = popover.getBoundingClientRect(), margin = 8;
    // directly adjacent to the anchor, so the pointer can move into the box without leaving the hover area
    let top = a.bottom;
    if (top + p.height > window.innerHeight - margin) top = Math.max(margin, a.top - p.height);
    const left = Math.min(Math.max(margin, a.left), window.innerWidth - p.width - margin);
    popover.style.top = top + 'px';
    popover.style.left = left + 'px';
};

// Copy a text to the clipboard (item text in the game's format); falls back to a hidden textarea where the Clipboard API is unavailable.
window.copyText = async function (text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        const ok = document.execCommand('copy');
        area.remove();
        return ok;
    }
};

// Save a text as a file (exported crafting guide).
window.downloadFile = function (fileName, content, mimeType) {
    const url = URL.createObjectURL(new Blob([content], { type: mimeType }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};

// Mermaid: themed with the page's design tokens (site.css :root)
(function () {
    if (!window.mermaid) return;
    const css = getComputedStyle(document.documentElement);
    const token = name => css.getPropertyValue(name).trim();
    mermaid.initialize({
        startOnLoad: false,
        theme: 'dark',
        themeVariables: {
            darkMode: true,
            primaryColor: token('--bg-card'),
            primaryBorderColor: token('--accent'),
            primaryTextColor: token('--text'),
            lineColor: token('--accent'),
            secondaryColor: token('--bg-elevated'),
            tertiaryColor: token('--bg-panel'),
            background: token('--bg-dark'),
        },
        flowchart: { curve: 'basis', padding: 15, htmlLabels: true },
    });
})();

// Mermaid diagram renderer for Blazor interop
window.renderMermaid = async function (container, definition, id) {
    if (!container || !definition) return;
    try {
        const { svg } = await mermaid.render(id, definition);
        container.innerHTML = svg;
    } catch (e) {
        container.innerHTML = '<pre class="mermaid-error-text">' + e.message.replace(/</g, '&lt;') + '</pre>';
    }
};
