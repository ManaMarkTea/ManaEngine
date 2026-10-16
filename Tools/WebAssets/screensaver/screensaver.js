const canvas = document.querySelector('canvas');
const ctx = canvas.getContext('2d', { alpha: false });
const status = document.querySelector('#status');
const pauseButton = document.querySelector('#pause');
const soundButton = document.querySelector('#sound');
const stopButton = document.querySelector('#stop');
const runStatus = document.querySelector('#run-status');
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
    render(0);
    controls();
}

function sourceTokens(file, line) {
    let cache = tokens.get(file);
    if (!cache) { cache = new Map(); tokens.set(file, cache); }
    if (cache.has(line)) return cache.get(line);
    const text = file.lines[line];
    const parts = [];
    const pattern = /(\/\/.*$|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\b(?:public|private|protected|internal|static|sealed|class|struct|void|return|if|else|for|foreach|new|using|namespace|var|let|fn|float|bool|int|override|readonly|const|async|await)\b|\b\d+(?:\.\d+)?[fu]?\b|[{}()[\];])/g;
    let start = 0;
    for (const match of text.matchAll(pattern)) {
        if (match.index > start) parts.push([text.slice(start, match.index), colors.ink]);
        const value = match[0];
        parts.push([value, value.startsWith('//') ? colors.muted : /^["']/.test(value) ? colors.green
            : /^\d/.test(value) ? colors.red : /^[{}()[\];]$/.test(value) ? colors.blue : colors.red]);
        start = match.index + value.length;
    }
    if (start < text.length) parts.push([text.slice(start), colors.ink]);
    cache.set(line, parts);
    return parts;
}

function label(text, x, y, color) {
    ctx.fillStyle = color;
    ctx.fillText(text, snap(x), snap(y));
}

function render(delta) {
    ctx.fillStyle = '#050a0e'; ctx.fillRect(0, 0, width, height);
    for (const pane of panes) {
        if (delta) {
            const burst = Math.sin(time * .65 + pane.phase);
            if (pane.typed < pane.typingLength)
                pane.typed += delta * pane.rate * 24 * (burst > .65 ? 3 : burst < -.7 ? .06 : 1);
            else {
                const next = Math.floor(pane.scroll) + 1;
                pane.scroll += delta * 8;
                if (pane.scroll >= next) { pane.scroll = next; pane.typed = 0; }
            }
            if (pane.scroll >= pane.file.lines.length) { pane.file = pick(); pane.scroll = 0; pane.typed = 0; }
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
    if (!stopped) { time = 0; resize(); }
    controls();
    if (audio) await (stopped || !sound ? audio.suspend() : audio.resume());
};
document.querySelector('#fullscreen').onclick = async () => {
    try { await (document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen()); }
    catch { /* Fullscreen can be unavailable in an embedded browser. */ }
};
addEventListener('keydown', event => {
    if (event.repeat || event.ctrlKey || event.metaKey || event.altKey || event.target.closest('button,a')) return;
    const button = { p: pauseButton, s: stopButton, m: soundButton, f: document.querySelector('#fullscreen') }[event.key.toLowerCase()];
    if (button) { event.preventDefault(); button.click(); }
});
document.addEventListener('visibilitychange', async () => {
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
try {
    const response = await fetch('sources.json');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    sources = await response.json();
    if (!sources.length) throw new Error('No source files');
    status.hidden = true; resize(); controls(); requestAnimationFrame(frame);
    console.log(`[CodeWall] Ready: ${sources.length} real source files`);
} catch (error) { status.textContent = `Could not open source files. Reload to retry. (${error.message})`; }
