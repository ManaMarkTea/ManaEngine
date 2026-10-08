import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';

const source = (await readFile(new URL('../../ManaGraphics/Native/mana_interop.js', import.meta.url), 'utf8'))
    .replaceAll('export function ', 'function ');

function browser({ touch = 1, coarse = true, override = '' } = {}) {
    let nextFrame, resize, now = 0;
    const sizes = [];
    const canvas = { clientWidth: 402, clientHeight: 874 };
    const context = {
        URLSearchParams, performance: { now: () => now },
        navigator: { maxTouchPoints: touch }, location: { search: override },
        window: { devicePixelRatio: 3, matchMedia: () => ({ matches: coarse, addEventListener() {} }) },
        document: { hidden: false, querySelector: () => canvas },
        ResizeObserver: class { constructor(callback) { resize = callback; } observe() {} },
        requestAnimationFrame: callback => { nextFrame = callback; },
        ManaExports: { ManaGraphics: { Code: { Graphics: { Browser: { ManaBrowserInterop: {
            OnFrame() {}, OnResize: (...args) => sizes.push(args)
        } } } } } }
    };
    runInNewContext(source + '\nglobalThis.api = { selectCanvas, observeResize, startRenderLoop, isMobileRendering, getDevicePixelRatio, getGpuParticleBudget };', context);
    const api = context.api;
    api.selectCanvas('#canvas'); api.observeResize(); api.startRenderLoop();
    resize([{ contentRect: { width: 402, height: 874 }, devicePixelContentBoxSize: [{ inlineSize: 1206, blockSize: 2622 }] }]);
    return { api, context, sizes, canvas, frames(count, milliseconds) {
        for (let i = 0; i < count; i++) { now += milliseconds; nextFrame(now); }
    } };
}

const mobile = browser();
assert.equal(mobile.api.isMobileRendering(), true);
assert.equal(mobile.api.getDevicePixelRatio(), 1);
assert.deepEqual(mobile.sizes[0], [402, 874, 402, 874], 'exact physical size cannot bypass the mobile cap');
assert.equal(mobile.api.getGpuParticleBudget(), 20000);
mobile.frames(100, 50);
const reduced = mobile.api.getGpuParticleBudget();
assert.ok(reduced < 20000, 'sustained slow frames reduce the budget');
mobile.context.document.hidden = true;
mobile.frames(100, 100);
assert.equal(mobile.api.getGpuParticleBudget(), reduced, 'background tabs do not lower quality');
mobile.context.document.hidden = false;
mobile.context.ManaMenuOpen = true;
mobile.frames(100, 16);
assert.equal(mobile.api.getGpuParticleBudget(), reduced, 'pause menus cannot inflate quality');
mobile.context.ManaMenuOpen = false;
mobile.frames(400, 16);
assert.ok(mobile.api.getGpuParticleBudget() > reduced, 'sustained headroom recovers quality slowly');
mobile.frames(3000, 100);
assert.equal(mobile.api.getGpuParticleBudget(), 1000, 'slow device has a bounded minimum');
mobile.frames(50000, 16);
assert.equal(mobile.api.getGpuParticleBudget(), 100000, 'recovery respects the mobile ceiling');
for (const options of [{ touch: 0, coarse: false }, { touch: 10, coarse: false }, { override: '?rendering=desktop' }]) {
    const desktop = browser(options);
    assert.equal(desktop.api.isMobileRendering(), false);
    assert.equal(desktop.api.getDevicePixelRatio(), 3);
    assert.deepEqual(desktop.sizes[0], [402, 874, 1206, 2622]);
    desktop.frames(100, 100);
    assert.equal(desktop.api.getGpuParticleBudget(), -1);
}
assert.equal(browser({ touch: 0, coarse: false, override: '?rendering=mobile' }).api.isMobileRendering(), true);
console.log('Mobile rendering checks passed.');
