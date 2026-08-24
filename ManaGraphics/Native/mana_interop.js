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
