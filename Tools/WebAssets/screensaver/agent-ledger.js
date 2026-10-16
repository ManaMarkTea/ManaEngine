export class AgentLedger {
    accounts = new Map();
    history = [];
    tokensPerSecond = 0;
    sampleTime = 0;
    previousTokens = 0;
    register(agent, characters) {
        if (!this.accounts.has(agent)) {
            this.accounts.set(agent, { agent, input: 0, output: 0 });
            this.read(agent, characters);
            this.previousTokens += characters / 4;
        }
    }
    read(agent, characters) { this.accounts.get(agent).input += characters / 4; }
    type(agent, characters) { this.accounts.get(agent).output += Math.max(0, characters) / 4; }
    get input() { return [...this.accounts.values()].reduce((sum, a) => sum + a.input, 0); }
    get output() { return [...this.accounts.values()].reduce((sum, a) => sum + a.output, 0); }
    get cost() { return (this.input * 3 + this.output * 15) / 1e6; }
    advance(delta) {
        this.sampleTime += delta;
        if (this.sampleTime < 1) return;
        const tokens = this.input + this.output;
        this.tokensPerSecond = (tokens - this.previousTokens) / this.sampleTime;
        this.previousTokens = tokens;
        this.sampleTime = 0;
        this.history.push(this.tokensPerSecond);
        if (this.history.length > 32) this.history.shift();
    }
}

const notes = ['wallet: gently weeping', 'one more tiny refactor...', 'cache hit. morale restored.',
    'the agents have requested a raise', 'optimizing the optimization', 'finance has left the chat'];
const number = value => Math.floor(value).toLocaleString();

export function renderLedger(ctx, pane, ledger, time, family, dpr) {
    const { x, y, w, h } = pane;
    const green = '#80dfa0', red = '#f47980', muted = '#668277', amber = '#f0bd66';
    ctx.save();
    ctx.fillStyle = '#0b0a0f'; ctx.fillRect(x, y, w, h);
    ctx.fillStyle = '#251316'; ctx.fillRect(x, y, w, 25);
    ctx.beginPath(); ctx.rect(x + 8, y + 3, w - 16, h - 6); ctx.clip();
    function text(value, top, color, size = 12, left = 14) {
        ctx.font = `${size}px ${family}`; ctx.fillStyle = color;
        ctx.fillText(value, Math.round((x + left) * dpr) / dpr, Math.round((y + top) * dpr) / dpr);
    }
    text('> swarm / finance department', 8, amber, 11);
    text('SESSION SPEND', 39, muted, 11);
    text(`$${ledger.cost.toFixed(6)}`, 57, red, 30);
    text(`${number(ledger.input + ledger.output)} TOKENS  /  ${ledger.accounts.size} AGENTS`, 99, '#b9cbd1');
    text(`IN ${number(ledger.input)}    OUT ${number(ledger.output)}`, 120, green, 11);
    text(`${ledger.tokensPerSecond.toFixed(1)} tok/s | $${(ledger.cost / Math.max(1, time) * 60).toFixed(4)}/min avg`, 140, amber, 11);
    const chartWidth = w - 28;
    ctx.fillStyle = '#0c1a15'; ctx.fillRect(x + 14, y + 163, chartWidth, 37);
    const peak = Math.max(1, ...ledger.history);
    ctx.fillStyle = green;
    ledger.history.forEach((value, i) => {
        const bar = value / peak * 33;
        ctx.fillRect(x + 14 + (32 - ledger.history.length + i) * chartWidth / 32,
            y + 198 - bar, Math.max(1, chartWidth / 32 - 2), bar);
    });
    text('AGENT', 214, muted, 11);
    ctx.textAlign = 'right';
    text('TOKENS', 214, muted, 11, w * .61);
    text('USD', 214, muted, 11, w - 14);
    ctx.textAlign = 'left';
    let top = 236;
    for (const a of ledger.accounts.values()) {
        if (top > h - 90) break;
        text(`/${String(a.agent).padStart(2, '0')}`, top, green, 11);
        ctx.textAlign = 'right';
        text(number(a.input + a.output), top, '#b9cbd1', 11, w * .61);
        text(((a.input * 3 + a.output * 15) / 1e6).toFixed(4), top, red, 11, w - 14);
        ctx.textAlign = 'left'; top += 19;
    }
    text(notes[Math.floor(time / 8) % notes.length], h - 66, amber, 11);
    text('SIMULATED USAGE / FICTIONAL RATES', h - 41, muted, 10);
    text('$3 input / $15 output per 1M tokens', h - 25, muted, 10);
    ctx.restore();
    ctx.fillStyle = '#804047';
    ctx.fillRect(x, y, w, 2); ctx.fillRect(x, y + h - 2, w, 2);
    ctx.fillRect(x, y, 2, h); ctx.fillRect(x + w - 2, y, 2, h);
}
