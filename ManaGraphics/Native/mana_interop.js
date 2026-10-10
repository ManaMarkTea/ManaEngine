// JS side of ManaBrowserInterop.cs (M1). Deliberately minimal -- canvas size, a
// requestAnimationFrame loop, and a ResizeObserver -- see that file for why.

let canvas = null;
let resizeObserver = null;
let lastLogicalWidth = 0;
let lastLogicalHeight = 0;
const startTime = performance.now();

// A touch laptop keeps desktop quality; phones and tablets start with a bounded GPU workload.
// The query override also lets desktop browser tests exercise the mobile path.
const renderingOverride = new URLSearchParams(globalThis.location?.search || '').get('rendering');
const mobileRendering = renderingOverride === 'mobile' || (renderingOverride !== 'desktop' &&
    (globalThis.navigator?.userAgentData?.mobile === true ||
        ((globalThis.navigator?.maxTouchPoints || 0) > 0 && window.matchMedia?.('(pointer: coarse)').matches)));
let particleBudget = 20000;
let sampleMs = 0;
let sampleFrames = 0;
let fastWindows = 0;
let warmupMs = 0;

export function isMobileRendering() { return Boolean(mobileRendering); }
// The sky shader: 0 simple gradients, 1 those plus the real star catalogue, 2 the full atmosphere. Phones start
// simple; ?sky=simple|stars|full (or the page's Sky setting) picks one anywhere.
const skyModes = { simple: 0, stars: 1, full: 2 };
const skyOverride = new URLSearchParams(globalThis.location?.search || '').get('sky');
export function getSkyMode() { return skyModes[skyOverride] ?? (mobileRendering ? 0 : 2); }
export function getGpuParticleBudget() { return mobileRendering ? particleBudget : -1; }

// RAF measures delivered frames, including GPU backpressure. CPU encode time alone misses that.
// Ignore pauses/background tabs; require sustained headroom before increasing density.
function updateParticleBudget(milliseconds) {
    if (!mobileRendering || document.hidden || globalThis.ManaMenuOpen || milliseconds <= 0 || milliseconds > 5000) {
        sampleMs = sampleFrames = fastWindows = 0;
        return;
    }
    if (warmupMs < 2000) { warmupMs += milliseconds; return; }
    sampleMs += milliseconds;
    sampleFrames++;
    if (sampleMs < 1000 || sampleFrames < 3) return;
    const average = sampleMs / sampleFrames;
    if (average > 38) {
        particleBudget = Math.max(1000, Math.floor(particleBudget * 0.65));
        fastWindows = 0;
    } else if (average < 25) {
        if (++fastWindows >= 5) {
            particleBudget = Math.min(100000, Math.ceil(particleBudget * 1.1));
            fastWindows = 0;
        }
    } else {
        fastWindows = 0;
    }
    sampleMs = sampleFrames = 0;
}

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

// Gamepads, flattened for one cheap call a frame: per slot, connected (0/1), the 17 standard-mapping buttons
// (A B X Y, LB RB, LT RT as 0..1, Back Start, sticks, D-pad up down left right, Home), then 4 stick axes.
// Browsers only reveal a pad after a button is pressed on it with the page focused.
const gamepadSlots = 4;
const gamepadStride = 1 + 17 + 4;
export function pollGamepads() {
    const out = new Array(gamepadSlots * gamepadStride).fill(0);
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    for (let slot = 0, i = 0; i < pads.length && slot < gamepadSlots; i++) {
        const pad = pads[i];
        if (!pad || !pad.connected) {
            continue;
        }
        const at = slot++ * gamepadStride;
        out[at] = 1;
        for (let b = 0; b < 17 && b < pad.buttons.length; b++) {
            out[at + 1 + b] = pad.buttons[b].value || (pad.buttons[b].pressed ? 1 : 0);
        }
        for (let a = 0; a < 4 && a < pad.axes.length; a++) {
            out[at + 18 + a] = pad.axes[a];
        }
    }
    return out;
}

// The name of the pad in a slot pollGamepads filled ("Xbox Wireless Controller (STANDARD GAMEPAD ...)").
export function gamepadName(slot) {
    const pads = Array.from(navigator.getGamepads ? navigator.getGamepads() : []).filter(pad => pad && pad.connected);
    // Chrome appends "(STANDARD GAMEPAD Vendor: ...)": the name is enough for a lobby.
    return pads[slot] ? pads[slot].id.replace(/\s*\((?:XInput )?STANDARD GAMEPAD[^)]*\)\s*$/i, '').trim() : '';
}

export function getDevicePixelRatio() {
    return mobileRendering ? Math.min(1, window.devicePixelRatio || 1) : window.devicePixelRatio || 1;
}

// Reports both sizes, the way GLFW does on macOS: CSS pixels are the window size the UI lays out
// in, device pixels are the backbuffer. Sending only the device size made the two equal, so
// ManaUI saw a scale of 1 and drew everything at half size on a Retina display.
function applyCanvasSize(cssWidth, cssHeight, deviceWidth, deviceHeight) {
    const logicalWidth = Math.max(1, Math.round(cssWidth));
    const logicalHeight = Math.max(1, Math.round(cssHeight));
    const width = Math.max(1, deviceWidth);
    const height = Math.max(1, deviceHeight);
    if (canvas.width === width && canvas.height === height &&
        lastLogicalWidth === logicalWidth && lastLogicalHeight === logicalHeight) return;
    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;
    lastLogicalWidth = logicalWidth;
    lastLogicalHeight = logicalHeight;
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
        const dpr = getDevicePixelRatio();
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
            const dpr = getDevicePixelRatio();
            const cssWidth = entry.contentRect.width;
            const cssHeight = entry.contentRect.height;
            // Exact device pixels where the browser offers them (not Safari); rounding css * dpr
            // can land a pixel off and blur the whole frame by resampling.
            const deviceBox = !mobileRendering && entry.devicePixelContentBoxSize && entry.devicePixelContentBoxSize[0];
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
let pointerLockWanted = false;
let pointerLockPending = false;

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

// A page's own menu over the game (the web build's pause menu) sets this while it is open, so the keys and clicks
// meant for the menu never reach the game. Releases still go through: a key held when the menu opened must not stay down.
function inputBlocked() {
    return globalThis.ManaMenuOpen === true;
}

export function installInput() {
    if (!canvas) {
        return;
    }

    window.addEventListener('keydown', (e) => {
        if (inputBlocked()) {
            return;
        }
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
        if (!api || inputBlocked()) {
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
        if (api && !inputBlocked()) {
            api.OnMouseDown(e.button);
            // Retry a denied/deferred capture on a fresh user gesture, never on every frame.
            if (pointerLockWanted) {
                requestPointerLock();
            }
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
        if (api && !inputBlocked()) {
            // deltaMode 1 is lines, 2 is pages; normalize both to something close to pixels so a
            // notch means roughly the same amount of zoom regardless of the user's OS/browser.
            const scale = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? 100 : 1;
            api.OnMouseWheel((-e.deltaX * scale) / 100.0, (-e.deltaY * scale) / 100.0);
        }
    }, { passive: false });

    // Right-drag is a camera gesture here, not a context menu.
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());

    document.addEventListener('pointerlockchange', () => {
        pointerLockPending = false;
        // Lock is asynchronous: the drag may have ended before the browser grants it.
        if (!pointerLockWanted && document.pointerLockElement === canvas) {
            document.exitPointerLock();
        }
    });
    document.addEventListener('pointerlockerror', () => {
        pointerLockPending = false;
    });
}

export function requestPointerLock() {
    // Touch look supplies deltas directly; mobile browsers do not need pointer lock.
    if (mobileRendering) return;
    pointerLockWanted = true;
    if (canvas && !inputBlocked() && !pointerLockPending &&
        document.pointerLockElement !== canvas && typeof canvas.requestPointerLock === 'function') {
        pointerLockPending = true;
        try {
            // The camera asks on the next frame after mousedown, while its transient activation
            // is still live. Older browsers return void; newer ones may return a Promise.
            const result = canvas.requestPointerLock();
            result?.catch(() => { pointerLockPending = false; });
        } catch {
            pointerLockPending = false;
        }
    }
}

export function exitPointerLock() {
    pointerLockWanted = false;
    if (canvas && document.pointerLockElement === canvas) {
        document.exitPointerLock();
    }
}

export function isPointerLocked() {
    return canvas != null && document.pointerLockElement === canvas;
}

export function startRenderLoop() {
    let last = performance.now();
    const fpsLabel = document.getElementById?.('manaFps');
    let fpsMilliseconds = 0;
    let fpsFrames = 0;
    function frame(now) {
        const dt = (now - last) / 1000.0;
        last = now;
        updateParticleBudget(dt * 1000);
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserInterop.OnFrame(dt);
            if (fpsLabel) {
                fpsLabel.hidden = Boolean(document.hidden);
                if (document.hidden || globalThis.ManaMenuOpen || dt <= 0 || dt > 5) {
                    fpsMilliseconds = fpsFrames = 0;
                    fpsLabel.textContent = globalThis.ManaMenuOpen ? 'Paused' : 'FPS ...';
                } else {
                    fpsMilliseconds += dt * 1000;
                    fpsFrames++;
                    if (fpsMilliseconds >= 1000) {
                        fpsLabel.textContent = `${(fpsFrames * 1000 / fpsMilliseconds).toFixed(1)} FPS`;
                        fpsMilliseconds = fpsFrames = 0;
                    }
                }
            }
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

// --- Emoji -------------------------------------------------------------------
// Backs ManaBrowserEmoji: the page's own colour emoji font drawn onto a canvas and copied into the span
// C# hands over, so the web build needs no emoji font of its own. Returns the advance, or 0 when the
// browser has no colour glyph for it (what came out is grey -- a text fallback or a tofu box).
let emojiCanvas = null;

export function drawEmoji(codepoint, size, width, height, baseline, pixels) {
    if (!emojiCanvas) {
        emojiCanvas = typeof OffscreenCanvas !== 'undefined'
            ? new OffscreenCanvas(width, height)
            : document.createElement('canvas');
    }
    emojiCanvas.width = width;
    emojiCanvas.height = height;
    const context = emojiCanvas.getContext('2d', { willReadFrequently: true });
    context.clearRect(0, 0, width, height);
    context.font = `${size}px "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", "Twemoji Mozilla", sans-serif`;
    context.textBaseline = 'alphabetic';
    // A text glyph or tofu box comes out in the fill colour; an emoji font ignores it. Testing for colour
    // instead dropped the grey ones (a skull).
    context.fillStyle = '#ff00ff';
    // VS16 asks for the emoji form of a character that also has a text one (U+2764, a heart).
    const text = String.fromCodePoint(codepoint) + '️';
    context.fillText(text, Math.ceil(size / 4), baseline);
    const data = context.getImageData(0, 0, width, height).data;

    let coloured = false;
    for (let i = 0; i < data.length; i += 4) {
        if (data[i + 3] > 32 && !(data[i] > 200 && data[i + 1] < 64 && data[i + 2] > 200)) {
            coloured = true;
            break;
        }
    }
    if (!coloured) {
        return 0;
    }

    // Drawn again in the default black, which an emoji font may use for parts in the text colour.
    context.clearRect(0, 0, width, height);
    context.fillStyle = '#000000';
    context.fillText(text, Math.ceil(size / 4), baseline);
    const final = context.getImageData(0, 0, width, height).data;

    // A Uint8Array view: the span only takes the typed array matching its element type, not a clamped one.
    pixels.set(new Uint8Array(final.buffer, final.byteOffset, final.length));
    manaLoading()?.emoji?.(text);
    return context.measureText(text).width;
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
