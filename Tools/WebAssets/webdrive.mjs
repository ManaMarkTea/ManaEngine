// Drives the published web build in headless Chrome over the DevTools protocol -- the selector, a level, the pause
// menu, some play -- writing screenshots and the page's console to <out dir>. Needs Node 22+ (built-in WebSocket)
// and Chrome; serve the build first (python ManaVoxel.Web/serve.py 8123). See AGENTS.md, "Testing the web build".
//
//   node Tools/WebAssets/webdrive.mjs Artifacts/WebDrive '[{"go":"http://127.0.0.1:8123/"},{"shot":"selector"}]'
//
// Steps: {go: url}, {wait: ms}, {waitFor: "console text", timeout: ms}, {shot: name}, {press: "KeyW", key: "w", hold: ms},
// {down: code}, {up: code}, {eval: "javascript"}, {viewport: {width, height, deviceScaleFactor, mobile}},
// {touch: {type: "touchStart" | "touchMove" | "touchEnd", touchPoints: [{x, y, id}]}}.
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

const out = process.argv[2];
const steps = JSON.parse(process.argv[3]);
mkdirSync(out, { recursive: true });
const log = [];
const note = line => { log.push(line); console.log(line); };

// CDP_PORT picks another port when one Chrome is already driving something else.
const port = process.env.CDP_PORT ?? '9333';
const running = await fetch(`http://127.0.0.1:${port}/json/version`).then(() => true, () => false);
const chromePath = process.env.CHROME_PATH ?? (process.platform === 'darwin'
    ? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
    : 'C:/Program Files/Google/Chrome/Application/chrome.exe');
const chrome = running ? null : spawn(chromePath, [
    '--headless=new', `--remote-debugging-port=${port}`, '--enable-unsafe-webgpu', '--enable-features=Vulkan',
    '--window-size=1280,720', `--user-data-dir=${resolve(out, 'chrome-profile')}`, '--no-first-run', '--autoplay-policy=no-user-gesture-required', '--mute-audio', 'about:blank'
], { stdio: 'ignore' });

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let target;
for (let i = 0; i < 150 && !target; i++) {
    await sleep(200);
    try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch { }
}
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(resolve => socket.addEventListener('open', resolve));
let id = 0;
const waiting = new Map();
socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id && waiting.has(message.id)) {
        waiting.get(message.id)(message);
        waiting.delete(message.id);
    } else if (message.method === 'Runtime.consoleAPICalled') {
        note('console: ' + message.params.args.map(a => a.value ?? a.description ?? '').join(' '));
    } else if (message.method === 'Runtime.exceptionThrown') {
        note('exception: ' + (message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text));
    }
});
const send = (method, params = {}) => new Promise(resolve => {
    const messageId = ++id;
    waiting.set(messageId, resolve);
    socket.send(JSON.stringify({ id: messageId, method, params }));
});

await send('Runtime.enable');
await send('Page.enable');

const key = async (code, keyName, type) => send('Input.dispatchKeyEvent', {
    type, code, key: keyName, windowsVirtualKeyCode: { Escape: 27, KeyD: 68, KeyA: 65, Space: 32, KeyR: 82 }[code] ?? 0
});

for (const step of steps) {
    if (step.viewport) {
        await send('Emulation.setDeviceMetricsOverride', step.viewport);
        await send('Emulation.setTouchEmulationEnabled', { enabled: Boolean(step.viewport.mobile), maxTouchPoints: 5 });
    } else if (step.touch) {
        await send('Input.dispatchTouchEvent', step.touch);
    } else if (step.go) {
        note('go: ' + step.go);
        await send('Page.navigate', { url: step.go });
    } else if (step.wait) {
        await sleep(step.wait);
    } else if (step.waitFor) {
        const deadline = Date.now() + (step.timeout ?? 120000);
        while (Date.now() < deadline && !log.some(line => line.includes(step.waitFor))) {
            await sleep(250);
        }
        note(log.some(line => line.includes(step.waitFor)) ? `saw: ${step.waitFor}` : `TIMED OUT waiting for: ${step.waitFor}`);
    } else if (step.shot) {
        const result = await send('Page.captureScreenshot', { format: 'png' });
        writeFileSync(`${out}/${step.shot}.png`, Buffer.from(result.result.data, 'base64'));
        note('shot: ' + step.shot);
    } else if (step.press) {
        await key(step.press, step.key ?? step.press, 'keyDown');
        await sleep(step.hold ?? 60);
        await key(step.press, step.key ?? step.press, 'keyUp');
    } else if (step.down) {
        await key(step.down, step.key ?? step.down, 'keyDown');
    } else if (step.up) {
        await key(step.up, step.key ?? step.up, 'keyUp');
    } else if (step.eval) {
        const result = await send('Runtime.evaluate', { expression: step.eval, returnByValue: true, awaitPromise: true });
        note('eval: ' + JSON.stringify(result.result?.result?.value));
    }
}

writeFileSync(`${out}/log.txt`, log.join('\n'));
socket.close();
chrome.kill();
process.exit(0);
