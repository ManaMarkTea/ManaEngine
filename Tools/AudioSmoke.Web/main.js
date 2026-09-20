import { dotnet } from './_framework/dotnet.js';

const status = document.querySelector('#status');
const button = document.querySelector('button');
try {
    const runtime = await dotnet.create();
    // Browser autoplay policies vary (headless Chrome may permit it). Explicit suspension makes
    // the engine's locked-device behavior deterministic while still using a real AudioContext.
    const NativeAudioContext = globalThis.AudioContext;
    let testContext;
    globalThis.AudioContext = class extends NativeAudioContext {
        constructor(options) { super(options); testContext = this; }
    };
    try { await runtime.runMain(); }
    finally { globalThis.AudioContext = NativeAudioContext; }
    await testContext.suspend();
    const exports = await runtime.getAssemblyExports('AudioSmoke.Web');
    status.textContent = exports.AudioSmoke.BeforeGesture();
    button.disabled = false;
    button.onclick = async () => {
        button.disabled = true;
        try {
            status.textContent = await exports.AudioSmoke.AfterGesture();
            document.body.dataset.result = 'passed';
        } catch (error) {
            status.textContent = `FAIL: ${error}`;
            document.body.dataset.result = 'failed';
        }
    };
} catch (error) {
    status.textContent = `FAIL: ${error}`;
    document.body.dataset.result = 'failed';
}
