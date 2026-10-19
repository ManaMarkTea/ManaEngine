import { AgentLedger, renderLedger } from './agent-ledger.js';
import { highlightSource } from './code-syntax.js';
import { loadRepository, parseRepository, readHistory, useRepository } from './github-sources.js';

const canvas = document.querySelector('canvas');
const ctx = canvas.getContext('2d', { alpha: false });
const status = document.querySelector('#status');
const pauseButton = document.querySelector('#pause');
const soundButton = document.querySelector('#sound');
const stopButton = document.querySelector('#stop');
const runStatus = document.querySelector('#run-status');
const clock = document.querySelector('#clock');
const repoForm = document.querySelector('#repo-form');
const repoInput = document.querySelector('#repo');
const repoHistory = document.querySelector('#repo-history');
const sourceLabel = document.querySelector('#source');
const sourceLink = document.querySelector('.source-link');
const clockFormat = new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' });
function updateClock() {
    const now = new Date();
    clock.textContent = clockFormat.format(now);
    clock.dateTime = now.toISOString();
}
updateClock();
setInterval(updateClock, 1000);
const colors = { ink: '#b9cbd1', green: '#80dfa0', red: '#f47980', muted: '#52727e', blue: '#78afc2' };
const family = 'ui-monospace, "Cascadia Mono", Consolas, "Liberation Mono", monospace';
let sources = [], panes = [], width = 0, height = 0, dpr = 1, time = 0, last = 0;
let paused = matchMedia('(prefers-reduced-motion: reduce)').matches;
let stopped = false;
let sound = false, audio, keys = [], nextKey = 0, phrase = 0;
const random = (low, high) => low + Math.random() * (high - low);
const pick = () => sources[Math.floor(Math.random() * sources.length)];
const snap = value => Math.round(value * dpr) / dpr;
const tokens = new WeakMap();
let ledger = new AgentLedger();
const sourceCharacters = file => file.lines.reduce((sum, line) => sum + line.length, 0);

function resize() {
    width = canvas.clientWidth; height = canvas.clientHeight; dpr = devicePixelRatio || 1;
    canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.textBaseline = 'top';
    if (!sources.length) return;
    panes = [];
    const columns = Math.max(1, Math.min(8, Math.round(width / 390)));
    const rows = Math.max(1, Math.round(height / 360));
    for (let col = 0; col < columns; col++) {
        const x = snap(width * col / columns), right = snap(width * (col + 1) / columns);
        const weights = Array.from({ length: rows }, () => random(.8, 1.2));
        const total = weights.reduce((sum, value) => sum + value, 0);
        let used = 0, y = 0;
        for (let row = 0; row < rows; row++) {
            used += weights[row];
            const bottom = row === rows - 1 ? height : snap(height * used / total);
            const file = pick();
            panes.push({ file, x, y, w: right - x, h: bottom - y,
                scroll: Math.floor(random(0, file.lines.length)), typed: 0, typingLength: 0,
                rate: random(.8, 3.6), phase: random(0, 30),
                id: panes.length + 1, accent: Math.random() < .22 ? colors.red : colors.green });
            y = bottom;
        }
    }
    const meterIndex = panes.findIndex(p => p.x === snap(width * (columns - 1) / columns) && p.y === 0);
    const meter = panes[meterIndex];
    meter.isLedger = true;
    if (columns > 1 && rows > 1) {
        meter.h += panes[meterIndex + 1].h;
        panes.splice(meterIndex + 1, 1);
    }
    for (const pane of panes.filter(p => !p.isLedger)) ledger.register(pane.id, sourceCharacters(pane.file));
    render(0);
    controls();
}

function sourceTokens(file, line) {
    let cache = tokens.get(file);
    if (!cache) { cache = highlightSource(file.lines, file.path); tokens.set(file, cache); }
    return cache[line];
}

function label(text, x, y, color) {
    ctx.fillStyle = color;
    ctx.fillText(text, snap(x), snap(y));
}

function render(delta) {
    ctx.fillStyle = '#050a0e'; ctx.fillRect(0, 0, width, height);
    for (const pane of panes) {
        if (pane.isLedger) { renderLedger(ctx, pane, ledger, time, family, dpr); continue; }
        if (delta) {
            const burst = Math.sin(time * .65 + pane.phase);
            if (pane.typed < pane.typingLength) {
                const before = pane.typed;
                pane.typed += delta * pane.rate * 24 * (burst > .65 ? 3 : burst < -.7 ? .06 : 1);
                ledger.type(pane.id, Math.min(pane.typed, pane.typingLength) - before);
            }
            else {
                const next = Math.floor(pane.scroll) + 1;
                pane.scroll += delta * 8;
                if (pane.scroll >= next) { pane.scroll = next; pane.typed = 0; ledger.read(pane.id, 64); }
            }
            if (pane.scroll >= pane.file.lines.length) {
                pane.file = pick(); pane.scroll = 0; pane.typed = 0;
                ledger.read(pane.id, sourceCharacters(pane.file));
            }
        }
        const x = pane.x, y = pane.y, w = pane.w, h = pane.h;
        if (x + w < 0 || x > width || y + h < 0 || y > height) continue;
        ctx.fillStyle = '#091117'; ctx.fillRect(x, y, w, h);
        ctx.fillStyle = '#101e25'; ctx.fillRect(x + 1, y + 1, w - 2, 25);
        ctx.fillStyle = '#315143'; ctx.fillRect(x + 2, y + 26, w - 4, 1);
        ctx.save(); ctx.beginPath(); ctx.rect(x + 8, y + 2, w - 16, h - 4); ctx.clip();
        ctx.font = `11px ${family}`;
        label(`> agent/${String(pane.id).padStart(2, '0')}  ~/${pane.file.path}`, x + 9, y + 8, pane.accent);
        const size = 12, lineHeight = size + 3;
        ctx.font = `${size}px ${family}`;
        const advance = ctx.measureText('M').width;
        const rows = Math.max(1, Math.floor((h - 64) / lineHeight));
        const first = Math.floor(pane.scroll), fraction = pane.scroll - first;
        const capacity = Math.max(1, Math.floor((w - 58) / advance));
        pane.typingLength = Math.min(capacity, pane.file.lines[(first + rows - 1) % pane.file.lines.length].length);
        ctx.save(); ctx.beginPath(); ctx.rect(x + 8, y + 33, w - 16, h - 62); ctx.clip();
        for (let row = 0; row < rows; row++) {
            const line = (first + row) % pane.file.lines.length;
            const top = y + 35 + row * lineHeight - fraction * lineHeight;
            label(String(line + 1).padStart(4, '0'), x + 10, top, colors.muted);
            let pen = x + 48;
            let remaining = row === rows - 1 ? Math.floor(pane.typed) : capacity;
            for (const [text, color] of sourceTokens(pane.file, line)) {
                const available = Math.min(remaining, Math.max(0, Math.floor((x + w - 10 - pen) / advance)));
                if (!available) break;
                const visible = text.slice(0, available);
                label(visible, pen, top, color);
                pen += visible.length * advance;
                remaining -= visible.length;
            }
            if (row === rows - 1 && Math.floor(time * 2 + pane.phase) % 2 === 0) {
                ctx.fillStyle = pane.accent;
                ctx.fillRect(snap(pen), snap(top), advance * .8, size + 1);
            }
        }
        ctx.restore();
        ctx.fillStyle = '#0d1c22'; ctx.fillRect(x, y + h - 24, w, 23);
        ctx.font = `10px ${family}`;
        label(`> typing source   ${String(first + 1).padStart(4, '0')}/${pane.file.lines.length}`, x + 10, y + h - 17, pane.accent);
        ctx.restore();
        ctx.fillStyle = '#3d6552';
        ctx.fillRect(x, y, w, 2); ctx.fillRect(x, y + h - 2, w, 2);
        ctx.fillRect(x, y, 2, h); ctx.fillRect(x + w - 2, y, 2, h);
        ctx.fillStyle = pane.accent; ctx.fillRect(x + 2, y + 2, 2, 22);
    }
    ctx.globalAlpha = 1;
    if (delta) ledger.advance(delta);
}

function makeAudio() {
    audio = new AudioContext();
    for (let key = 0; key < 6; key++) {
        const buffer = audio.createBuffer(1, Math.round(audio.sampleRate * .05), audio.sampleRate);
        const samples = buffer.getChannelData(0);
        let low = 0;
        for (let i = 0; i < samples.length; i++) {
            const t = i / audio.sampleRate, noise = random(-1, 1);
            low += .22 * (noise - low);
            const body = Math.sin(2 * Math.PI * (360 + key * 71) * t) * Math.exp(-t * 240);
            samples[i] = (.18 * body + .35 * (noise - low) * Math.exp(-t * 650)
                + (t < .016 ? 0 : .3 * low * Math.exp(-(t - .016) * 470))) * Math.min(1, t / .0008);
        }
        keys.push(buffer);
    }
}

function typing(delta) {
    if (!sound || audio?.state !== 'running') return;
    nextKey -= delta;
    if (nextKey > 0) return;
    if (phrase-- <= 0) { phrase = Math.floor(random(4, 18)); nextKey = random(.35, 2.1); return; }
    nextKey = random(.055, .205);
    const tap = audio.createBufferSource(), gain = audio.createGain(), pan = audio.createStereoPanner();
    tap.buffer = keys[Math.floor(random(0, keys.length))];
    tap.playbackRate.value = random(.9, 1.1);
    gain.gain.value = random(.025, .05); pan.pan.value = random(-.4, .4);
    tap.connect(gain).connect(pan).connect(audio.destination);
    tap.onended = () => { tap.disconnect(); gain.disconnect(); pan.disconnect(); };
    tap.start();
}

function controls() {
    pauseButton.textContent = paused ? 'Resume' : 'Pause';
    pauseButton.setAttribute('aria-pressed', String(paused));
    pauseButton.disabled = stopped;
    stopButton.textContent = stopped ? 'Start' : 'Stop';
    runStatus.textContent = `${stopped ? 'Stopped' : paused ? 'Paused' : 'Running'} · ${panes.length} sessions`;
    soundButton.textContent = sound ? 'Sound on' : 'Sound off';
    soundButton.setAttribute('aria-pressed', String(sound));
}
pauseButton.onclick = async () => {
    paused = !paused; controls(); last = 0;
    if (audio) await (paused ? audio.suspend() : sound ? audio.resume() : audio.suspend());
};
soundButton.onclick = async () => {
    try {
        if (!audio) makeAudio();
        sound = !sound;
        await (sound && !paused && !stopped ? audio.resume() : audio.suspend());
        controls();
    } catch (error) { sound = false; controls(); status.hidden = false; status.textContent = `Sound unavailable: ${error.message}`; }
};
stopButton.onclick = async () => {
    stopped = !stopped;
    paused = false;
    last = 0;
    if (!stopped) { time = 0; ledger = new AgentLedger(); resize(); }
    controls();
    if (audio) await (stopped || !sound ? audio.suspend() : audio.resume());
};
document.querySelector('#fullscreen').onclick = async () => {
    try { await (document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen()); }
    catch { /* Fullscreen can be unavailable in an embedded browser. */ }
};
addEventListener('keydown', event => {
    if (event.repeat || event.ctrlKey || event.metaKey || event.altKey || event.target.closest('button,a,input')) return;
    const button = { p: pauseButton, s: stopButton, m: soundButton, f: document.querySelector('#fullscreen') }[event.key.toLowerCase()];
    if (button) { event.preventDefault(); button.click(); }
});
document.addEventListener('visibilitychange', async () => {
    updateClock();
    last = 0;
    if (audio) await (document.hidden || paused || stopped || !sound ? audio.suspend() : audio.resume());
});
addEventListener('resize', resize);
function frame(now) {
    requestAnimationFrame(frame);
    if (document.hidden) { last = 0; return; }
    if (last && now - last < 1000 / 30) return;
    const delta = paused || stopped || !last ? 0 : Math.min(.1, (now - last) / 1000);
    last = now;
    time += delta;
    if (!paused && !stopped) render(delta);
    if (!paused && !stopped) typing(delta);
}
let localSources = [], loading = null, started = false;

function showSources(files, label, href) {
    sources = files;
    time = 0; ledger = new AgentLedger();
    sourceLabel.textContent = label;
    sourceLink.href = href;
    status.hidden = true;
    resize(); controls();
    if (!started) { started = true; requestAnimationFrame(frame); }
}

function listHistory(recent) {
    repoHistory.replaceChildren(...recent.map(key => Object.assign(document.createElement('option'), { value: key })));
}

// A public GitHub repository instead of ManaRenderer's own files; blank goes back to them.
async function showRepository(text) {
    loading?.abort();
    loading = null;
    const url = new URL(location.href);
    if (!text.trim()) {
        if (localSources.length) showSources(localSources, 'ManaRenderer', 'https://github.com/ManaMarkTea/ManaRenderer');
        listHistory(useRepository('').recent);
        url.searchParams.delete('repo'); history.replaceState(null, '', url);
        return;
    }
    const repository = parseRepository(text);
    if (!repository) { sourceLabel.textContent = `Not a GitHub repository: ${text.trim()}`; return; }
    const controller = loading = new AbortController();
    sourceLabel.textContent = `Loading ${repository.label}…`;
    try {
        const files = await loadRepository(repository, controller.signal);
        if (loading !== controller) return;
        loading = null;
        showSources(files, `${repository.label} · ${files.length} files`, `https://github.com/${repository.owner}/${repository.name}`);
        listHistory(useRepository(repository.key).recent);
        url.searchParams.set('repo', repository.key); history.replaceState(null, '', url);
        console.log(`[CodeWall] Ready: ${files.length} files from ${repository.key}`);
    } catch (error) {
        if (error.name === 'AbortError' || loading !== controller) return;
        loading = null;
        sourceLabel.textContent = `Could not open ${repository.label}: ${error.message}`;
        console.log(`[CodeWall] ${sourceLabel.textContent}`);
    }
}

repoForm.onsubmit = event => { event.preventDefault(); repoInput.blur(); showRepository(repoInput.value); };
try {
    const response = await fetch('sources.json');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    localSources = await response.json();
    if (!localSources.length) throw new Error('No source files');
    showSources(localSources, 'ManaRenderer', sourceLink.href);
    console.log(`[CodeWall] Ready: ${localSources.length} real source files`);
} catch (error) { status.textContent = `Could not open source files. Reload to retry. (${error.message})`; }
// ?repo= (a shared link) wins over the repository this browser showed last.
const saved = readHistory();
listHistory(saved.recent);
const first = new URL(location.href).searchParams.get('repo') ?? saved.last;
repoInput.value = first;
if (first) showRepository(first);
