// JS side of ManaBrowserInterop.cs (M1). Deliberately minimal -- canvas size, a
// requestAnimationFrame loop, and a ResizeObserver -- see that file for why.

let canvas = null;
let resizeObserver = null;
const startTime = performance.now();

export function selectCanvas(selector) {
    canvas = document.querySelector(selector);
    return canvas != null;
}

export function getCanvasWidth() {
    return canvas ? canvas.clientWidth : 0;
}

export function getCanvasHeight() {
    return canvas ? canvas.clientHeight : 0;
}

export function getTimeMs() {
    return performance.now() - startTime;
}

export function observeResize() {
    if (!canvas) {
        return;
    }
    if (resizeObserver) {
        resizeObserver.disconnect();
    }
    resizeObserver = new ResizeObserver((entries) => {
        for (const entry of entries) {
            const dpr = window.devicePixelRatio || 1;
            const width = Math.max(1, Math.round(entry.contentRect.width * dpr));
            const height = Math.max(1, Math.round(entry.contentRect.height * dpr));
            canvas.width = width;
            canvas.height = height;
            if (globalThis.ManaExports) {
                globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop.OnResize(width, height);
            }
        }
    });
    resizeObserver.observe(canvas);
}

// --- Input ---------------------------------------------------------------
// Keyboard listens on `window`, not the canvas: a canvas only receives key events when it holds
// focus, which would mean the game silently stops responding any time focus drifts to the page
// body. Mouse listens on the canvas so coordinates are naturally canvas-relative.
//
// Keys the browser would otherwise act on itself (space scrolls the page, arrows scroll, Tab
// moves focus away) get preventDefault'd while the canvas is the active surface -- otherwise
// holding W to walk also scrolls the document. F-keys and browser shortcuts are deliberately
// left alone so devtools/refresh still work.
const SUPPRESSED_KEYS = new Set([
    'Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Tab',
]);

function manaInput() {
    return globalThis.ManaExports
        ? globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop
        : null;
}

export function installInput() {
    if (!canvas) {
        return;
    }

    window.addEventListener('keydown', (e) => {
        if (SUPPRESSED_KEYS.has(e.code)) {
            e.preventDefault();
        }
        const api = manaInput();
        if (api) {
            api.OnKeyDown(e.code, e.repeat);
            // Printable characters only: a text field wants 'a', not the fact that KeyA moved.
            if (e.key && e.key.length === 1) {
                api.OnKeyChar(e.key);
            }
        }
    });

    window.addEventListener('keyup', (e) => {
        const api = manaInput();
        if (api) {
            api.OnKeyUp(e.code);
        }
    });

    // Losing focus (alt-tab, devtools, another window) never delivers the matching keyup, so
    // without this the engine keeps believing whatever was held is still held -- the classic
    // "walked away and came back still running forward" bug.
    window.addEventListener('blur', () => {
        const api = manaInput();
        if (api) {
            api.OnFocusLost();
        }
    });

    canvas.addEventListener('mousemove', (e) => {
        const api = manaInput();
        if (!api) {
            return;
        }
        const dpr = window.devicePixelRatio || 1;
        if (document.pointerLockElement === canvas) {
            // Locked: the cursor has no meaningful page position, only per-event deltas.
            api.OnMouseDelta(e.movementX * dpr, e.movementY * dpr);
        } else {
            const rect = canvas.getBoundingClientRect();
            api.OnMouseMove((e.clientX - rect.left) * dpr, (e.clientY - rect.top) * dpr);
        }
    });

    canvas.addEventListener('mousedown', (e) => {
        const api = manaInput();
        if (api) {
            api.OnMouseDown(e.button);
        }
    });

    window.addEventListener('mouseup', (e) => {
        // On window, not canvas: a drag that starts on the canvas and releases outside it would
        // otherwise leave the button stuck down forever.
        const api = manaInput();
        if (api) {
            api.OnMouseUp(e.button);
        }
    });

    canvas.addEventListener('wheel', (e) => {
        e.preventDefault();
        const api = manaInput();
        if (api) {
            // deltaMode 1 is lines, 2 is pages; normalize both to something close to pixels so a
            // notch means roughly the same amount of zoom regardless of the user's OS/browser.
            const scale = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? 100 : 1;
            api.OnMouseWheel((-e.deltaX * scale) / 100.0, (-e.deltaY * scale) / 100.0);
        }
    }, { passive: false });

    // Right-drag is a camera gesture here, not a context menu.
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
}

export function requestPointerLock() {
    if (canvas && document.pointerLockElement !== canvas) {
        canvas.requestPointerLock();
    }
}

export function exitPointerLock() {
    if (document.pointerLockElement) {
        document.exitPointerLock();
    }
}

export function isPointerLocked() {
    return canvas != null && document.pointerLockElement === canvas;
}

export function startRenderLoop() {
    let last = performance.now();
    function frame(now) {
        const dt = (now - last) / 1000.0;
        last = now;
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop.OnFrame(dt);
        }
        requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
}

// --- Loading screen ------------------------------------------------------
// The overlay itself lives in index.html and is already on screen before any of this exists --
// it has to be, since the wasm download is the longest part of the wait and nothing here is
// running during it. These are just the C# side's handle on it, so the same screen carries
// straight through from "downloading" to "loading the level" to gone.
//
// Guarded rather than assumed: mana_interop.js is also loadable from a host page that has no
// such overlay, and a missing loading screen must never take the engine down with it.
function manaLoading() {
    return globalThis.ManaLoading || null;
}

export function setLoadingProgress(status, fraction) {
    const loading = manaLoading();
    if (loading) {
        loading.engine(status, fraction);
    }
}

export function failLoading(message) {
    const loading = manaLoading();
    if (loading) {
        loading.fail(message);
    }
}

export function hideLoadingOverlay() {
    const loading = manaLoading();
    if (loading) {
        loading.hide();
    }
}

// --- Settings storage --------------------------------------------------------
// Backs ManaCore.Browser.ManaBrowserSettingsBridge: a plain localStorage key/value store for the
// handful of desktop settings files (docking layout, UI density) that would otherwise write to
// the wasm virtual filesystem and vanish on the next page load. A single "mana:" prefix keeps
// these out of the way of anything else that might use localStorage on the same origin (an
// embedding page, browser extensions) rather than namespacing every call site individually.
const SETTINGS_PREFIX = 'mana:';

export function manaSettingsGet(key) {
    try {
        return globalThis.localStorage.getItem(SETTINGS_PREFIX + key);
    } catch (error) {
        // A private-browsing tab or a denied storage permission throws on access, not just on
        // quota -- same "never worth taking the app down over" reasoning as the rest of this file.
        console.warn('[ManaBrowserSettingsBridge] getItem failed: ' + error);
        return null;
    }
}

export function manaSettingsSet(key, value) {
    try {
        globalThis.localStorage.setItem(SETTINGS_PREFIX + key, value);
    } catch (error) {
        console.warn('[ManaBrowserSettingsBridge] setItem failed: ' + error);
    }
}

export function manaSettingsRemove(key) {
    try {
        globalThis.localStorage.removeItem(SETTINGS_PREFIX + key);
    } catch (error) {
        console.warn('[ManaBrowserSettingsBridge] removeItem failed: ' + error);
    }
}
