// webdrive.mjs for Firefox, over WebDriver BiDi: its shader compiler and Web Audio differ from Chrome's, so a build that
// passes there can still fail here. Serve the build first (python ManaEditor.Web/serve.py 8123).
//
//   node Tools/WebAssets/webdrive-firefox.mjs Artifacts/WebDrive/firefox '[{"go":"http://127.0.0.1:8123/"},{"shot":"selector"}]'
//
// Steps: {go: url}, {wait: ms}, {waitFor: "console text", timeout: ms}, {shot: name}, {press: "Enter", hold: ms},
// {down: key}, {up: key}, {eval: "javascript"}. Keys are DOM key values ("Enter", "w").
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

const out = process.argv[2];
const steps = JSON.parse(process.argv[3]);
const profile = resolve(out, 'firefox-profile');
mkdirSync(profile, { recursive: true });
writeFileSync(`${profile}/user.js`, [
    'user_pref("media.autoplay.default", 0);',
    'user_pref("browser.shell.checkDefaultBrowser", false);',
    'user_pref("datareporting.policy.dataSubmissionEnabled", false);'
].join('\n'));

const log = [];
const note = line => { log.push(line); console.log(line); };
const sleep = ms => new Promise(done => setTimeout(done, ms));

const port = process.env.BIDI_PORT ?? '9444';
const firefoxPath = process.env.FIREFOX_PATH ?? (process.platform === 'darwin'
    ? '/Applications/Firefox.app/Contents/MacOS/firefox'
    : 'C:/Program Files/Mozilla Firefox/firefox.exe');
// A real window: headless Firefox has no WebGPU adapter.
const firefox = spawn(firefoxPath, ['--no-remote', '--profile', profile, `--remote-debugging-port=${port}`,
    '--width=1280', '--height=720', 'about:blank'], { stdio: 'ignore' });

let socket;
for (let i = 0; i < 100 && !socket; i++) {
    await sleep(300);
    socket = await new Promise(done => {
        const attempt = new WebSocket(`ws://127.0.0.1:${port}/session`);
        attempt.addEventListener('open', () => done(attempt));
        attempt.addEventListener('error', () => done(null));
    });
}
if (!socket) {
    firefox.kill();
    throw new Error('Firefox did not open its debugging port');
}

let id = 0;
const waiting = new Map();
socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id && waiting.has(message.id)) {
        waiting.get(message.id)(message);
        waiting.delete(message.id);
    } else if (message.method === 'log.entryAdded') {
        note(`${message.params.level}: ${message.params.text}`);
    }
});
const send = (method, params = {}) => new Promise(done => {
    const messageId = ++id;
    waiting.set(messageId, done);
    socket.send(JSON.stringify({ id: messageId, method, params }));
});

await send('session.new', { capabilities: {} });
await send('session.subscribe', { events: ['log.entryAdded'] });
const context = (await send('browsingContext.getTree')).result.contexts[0].context;

const key = (value, type) => send('input.performActions', {
    context, actions: [{ type: 'key', id: 'keyboard', actions: [{ type, value }] }]
});

for (const step of steps) {
    if (step.go) {
        note('go: ' + step.go);
        await send('browsingContext.navigate', { context, url: step.go, wait: 'none' });
    } else if (step.wait) {
        await sleep(step.wait);
    } else if (step.waitFor) {
        const deadline = Date.now() + (step.timeout ?? 120000);
        while (Date.now() < deadline && !log.some(line => line.includes(step.waitFor))) {
            await sleep(250);
        }
        note(log.some(line => line.includes(step.waitFor)) ? `saw: ${step.waitFor}` : `TIMED OUT waiting for: ${step.waitFor}`);
    } else if (step.shot) {
        const result = await send('browsingContext.captureScreenshot', { context });
        if (result.result) {
            writeFileSync(`${out}/${step.shot}.png`, Buffer.from(result.result.data, 'base64'));
        }
        note('shot: ' + step.shot);
    } else if (step.press) {
        await key(step.press, 'keyDown');
        await sleep(step.hold ?? 60);
        await key(step.press, 'keyUp');
    } else if (step.down) {
        await key(step.down, 'keyDown');
    } else if (step.up) {
        await key(step.up, 'keyUp');
    } else if (step.eval) {
        const result = await send('script.evaluate', { expression: step.eval, target: { context }, awaitPromise: true });
        note('eval: ' + JSON.stringify(result.result?.result?.value ?? result.result ?? result.error));
    }
}

writeFileSync(`${out}/log.txt`, log.join('\n'));
await send('browser.close').catch(() => {});
firefox.kill();
process.exit(0);
