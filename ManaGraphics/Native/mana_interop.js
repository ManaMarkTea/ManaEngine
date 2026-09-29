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

export function getDevicePixelRatio() {
    return window.devicePixelRatio || 1;
}

// Reports both sizes, the way GLFW does on macOS: CSS pixels are the window size the UI lays out
// in, device pixels are the backbuffer. Sending only the device size made the two equal, so
// ManaUI saw a scale of 1 and drew everything at half size on a Retina display.
function applyCanvasSize(cssWidth, cssHeight, deviceWidth, deviceHeight) {
    const logicalWidth = Math.max(1, Math.round(cssWidth));
    const logicalHeight = Math.max(1, Math.round(cssHeight));
    const width = Math.max(1, deviceWidth);
    const height = Math.max(1, deviceHeight);
    canvas.width = width;
    canvas.height = height;
    if (globalThis.ManaExports) {
        globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop.OnResize(logicalWidth, logicalHeight, width, height);
    }
}

let dprQuery = null;

// devicePixelRatio changes (window dragged to another display, browser zoom) resize nothing, so the
// ResizeObserver never hears about them.
function watchDevicePixelRatio() {
    if (dprQuery) {
        dprQuery.removeEventListener('change', onDevicePixelRatioChanged);
    }
    dprQuery = window.matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`);
    dprQuery.addEventListener('change', onDevicePixelRatioChanged);
}

function onDevicePixelRatioChanged() {
    watchDevicePixelRatio();
    if (canvas) {
        const dpr = window.devicePixelRatio || 1;
        applyCanvasSize(canvas.clientWidth, canvas.clientHeight,
            Math.round(canvas.clientWidth * dpr), Math.round(canvas.clientHeight * dpr));
    }
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
            const cssWidth = entry.contentRect.width;
            const cssHeight = entry.contentRect.height;
            // Exact device pixels where the browser offers them (not Safari); rounding css * dpr
            // can land a pixel off and blur the whole frame by resampling.
            const deviceBox = entry.devicePixelContentBoxSize && entry.devicePixelContentBoxSize[0];
            const width = deviceBox ? deviceBox.inlineSize : Math.round(cssWidth * dpr);
            const height = deviceBox ? deviceBox.blockSize : Math.round(cssHeight * dpr);
            applyCanvasSize(cssWidth, cssHeight, width, height);
        }
    });
    try {
        resizeObserver.observe(canvas, { box: 'device-pixel-content-box' });
    } catch {
        resizeObserver.observe(canvas);
    }
    watchDevicePixelRatio();
}

// --- Input ---------------------------------------------------------------
// Keyboard listens on `window`, not the canvas: a canvas only receives key events when it holds
// focus, which would mean the game silently stops responding any time focus drifts to the page
// body. Mouse listens on the canvas so coordinates are naturally canvas-relative.
//
// Keys the browser would otherwise act on itself (space scrolls the page, arrows scroll, Tab
// moves focus away) get preventDefault'd while the canvas is the active surface -- otherwise
// holding W to walk also scrolls the document. F-keys and browser shortcuts are deliberately
// left alone by default so devtools/refresh still work; a host that gives one of them a job of its
// own (the editor's F5 is Play) asks for it with suppressKeys -- the key still reaches the engine.
const SUPPRESSED_KEYS = new Set([
    'Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Tab',
]);

// Adds keys (KeyboardEvent.code values, comma separated) the browser must not act on. Called from C#
// as a single string because that is what a JSImport can pass without marshalling an array.
export function suppressKeys(codes) {
    for (const code of String(codes).split(',')) {
        const trimmed = code.trim();
        if (trimmed) {
            SUPPRESSED_KEYS.add(trimmed);
        }
    }
}

// A dead runtime throws on every export call, and mousemove fires constantly: without this a crashed
// boot buries the real error under thousands of "runtime already exited" lines.
let runtimeDead = false;
let guardedInput = null;

function manaInput() {
    if (runtimeDead || !globalThis.ManaExports) {
        return null;
    }

    if (guardedInput) {
        return guardedInput;
    }

    const api = globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop;
    guardedInput = new Proxy(api, {
        get(target, name) {
            const member = target[name];
            if (typeof member !== 'function') {
                return member;
            }
            return (...args) => {
                try {
                    return member.apply(target, args);
                } catch (error) {
                    runtimeDead = true;
                    console.error('[ManaBrowserInterop] input dispatch failed; input disabled:', error);
                }
            };
        }
    });
    return guardedInput;
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
        // CSS pixels, like GLFW's window coordinates: ManaUI hit-tests in them, and the camera
        // scales to framebuffer pixels itself.
        if (document.pointerLockElement === canvas) {
            // Locked: the cursor has no meaningful page position, only per-event deltas.
            api.OnMouseDelta(e.movementX, e.movementY);
        } else {
            const rect = canvas.getBoundingClientRect();
            api.OnMouseMove(e.clientX - rect.left, e.clientY - rect.top);
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
