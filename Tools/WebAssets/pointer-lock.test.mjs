import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const handlers = new Map();
let requests = 0;
let exits = 0;
let rejectRequest;
const canvas = {
    addEventListener() {},
    requestPointerLock() {
        requests++;
        return new Promise((resolve, reject) => { rejectRequest = reject; });
    }
};
globalThis.window = { addEventListener() {} };
globalThis.document = {
    querySelector: () => canvas,
    pointerLockElement: null,
    addEventListener: (name, callback) => handlers.set(name, callback),
    exitPointerLock() { exits++; this.pointerLockElement = null; }
};
const source = await readFile(new URL('../../ManaGraphics/Native/mana_interop.js', import.meta.url), 'utf8');
const interop = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
interop.selectCanvas('#canvas');
interop.installInput();
interop.requestPointerLock();
interop.requestPointerLock();
assert.equal(requests, 1, 'pending requests are deduplicated');
interop.exitPointerLock();
document.pointerLockElement = canvas;
handlers.get('pointerlockchange')();
assert.equal(exits, 1, 'a late lock is released after the drag ends');
interop.requestPointerLock();
rejectRequest(new Error('denied'));
await Promise.resolve();
interop.requestPointerLock();
assert.equal(requests, 3, 'rejection allows a later retry');
handlers.get('pointerlockerror')();
canvas.requestPointerLock = () => { requests++; };
interop.requestPointerLock();
assert.equal(requests, 4, 'legacy void-returning implementations work');
document.pointerLockElement = {};
interop.exitPointerLock();
assert.equal(exits, 1, 'another surface owns its own lock');
globalThis.ManaMenuOpen = true;
interop.requestPointerLock();
assert.equal(requests, 4, 'page menus block capture');
console.log('Pointer lock checks passed.');
