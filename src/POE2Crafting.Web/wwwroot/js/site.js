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

// Drag the top edge of the crafting flow lane to resize it: the drag writes --flow-lane on the page, which is the
// height of the lane's grid row — the same knob the small/medium/tall buttons use (those clear it again).
window.initLaneResize = function (handleId) {
    const handle = document.getElementById(handleId);
    if (!handle || handle.dataset.resizeBound) return;
    handle.dataset.resizeBound = '1';
    handle.addEventListener('pointerdown', function (down) {
        const page = handle.closest('.crafting-page');
        const lane = handle.parentElement;
        if (!page || !lane) return;
        const startY = down.clientY, startHeight = lane.getBoundingClientRect().height;
        const move = ev => {
            const height = Math.min(Math.max(startHeight + (startY - ev.clientY), 54), window.innerHeight - 160);
            page.style.setProperty('--flow-lane', height + 'px');
        };
        const up = () => {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', up);
            handle.removeEventListener('pointercancel', up);
        };
        handle.setPointerCapture(down.pointerId);
        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', up);
        handle.addEventListener('pointercancel', up);
        down.preventDefault();
    });
};

// Back to a preset height: drop what the drag wrote, so the size buttons take over again.
window.clearLaneHeight = function () {
    const page = document.querySelector('.crafting-page');
    if (page) page.style.removeProperty('--flow-lane');
};

// Scroll a horizontal scroll container to its right end (crafting flow lane: the newest step).
window.scrollToEnd = function (id) {
    const element = document.getElementById(id);
    if (element) element.scrollTo({ left: element.scrollWidth, behavior: 'smooth' });
};

// Scroll an element into view inside its scroll container (Market page: a table row's ⇄ opens the trade calculator).
window.scrollToElement = function (id) {
    const element = document.getElementById(id);
    if (element) element.scrollIntoView({ behavior: 'smooth', block: 'start' });
};
