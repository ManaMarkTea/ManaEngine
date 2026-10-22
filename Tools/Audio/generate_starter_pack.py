"""Generate original, deterministic UI/gameplay effects using only Python's standard library.

Run from any directory: python Tools/Audio/generate_starter_pack.py
No recordings, third-party samples, sound models, or network downloads are used.
"""
from array import array
import hashlib
import html
import json
import math
from pathlib import Path
import random
import sys
import wave

RATE = 48000
ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/Audio/Starter"
TAU = math.tau


def envelope(t, duration, attack=0.003, decay=5):
    ramp = math.sin(min(1, t / attack) * math.pi / 2) ** 2
    release = math.sin(min(1, max(0, duration - t) / 0.015) * math.pi / 2) ** 2
    return ramp * release * math.exp(-decay * t / duration)


def tone(duration, start, end=None, warmth=0.12, decay=5):
    end = start if end is None else end
    slope = math.log(end / start) / duration
    result = []
    for i in range(round(duration * RATE)):
        t = i / RATE
        phase = TAU * start * (math.expm1(slope * t) / slope if abs(slope) > 1e-8 else t)
        value = math.sin(phase) + warmth * math.sin(2 * phase) + warmth * 0.35 * math.sin(3 * phase)
        result.append(value * envelope(t, duration, decay=decay))
    return result


def noise(duration, cutoff, seed, decay=5):
    rng = random.Random(seed)
    alpha = 1 - math.exp(-TAU * cutoff / RATE)
    low = smooth = 0.0
    result = []
    for i in range(round(duration * RATE)):
        low += alpha * (rng.uniform(-1, 1) - low)
        smooth += alpha * (low - smooth)
        result.append(smooth * envelope(i / RATE, duration, attack=0.002, decay=decay))
    return result


def mix(*layers):
    """Layers are (start time, gain, samples)."""
    result = [0.0] * max(round(offset * RATE) + len(samples) for offset, _, samples in layers)
    for offset, gain, samples in layers:
        first = round(offset * RATE)
        for i, value in enumerate(samples):
            result[first + i] += gain * value
    return result


def chime(notes, spacing=0.07, duration=0.24):
    layers = []
    for i, frequency in enumerate(notes):
        layers.append((i * spacing, 1, tone(duration, frequency, warmth=0.025)))
        layers.append((i * spacing, 0.16, tone(duration * 0.6, frequency * 2.76, warmth=0)))
    return mix(*layers)


def impact(seed, frequency=135, duration=0.16, hard=False):
    return mix(
        (0, 0.8, tone(duration, frequency, frequency * 0.42, warmth=0.05, decay=7)),
        (0, 1.2 if hard else 0.65, noise(duration * 0.7, 3400 if hard else 1400, seed, decay=9)),
        (0.005, 0.15, tone(duration * 0.5, frequency * 3.2, warmth=0, decay=9)),
    )


def definitions():
    # Peak targets are intentionally quieter for frequently repeated UI and footstep sounds.
    yield "UI/hover", "Soft focus tick; use sparingly on keyboard navigation", 0.10, tone(0.040, 1500, 1250, decay=7)
    yield "UI/click", "Short, rounded button press", 0.22, mix((0, 1, tone(0.070, 1050, 620)), (0, 0.3, noise(0.022, 4000, 1)))
    yield "UI/confirm", "Positive two-note confirmation", 0.28, chime([880, 1320], 0.055, 0.18)
    yield "UI/cancel", "Gentle descending dismissal", 0.23, chime([740, 494], 0.05, 0.15)
    yield "UI/error", "Low double pulse for a rejected action", 0.27, mix((0, 1, tone(0.115, 220, 170, warmth=0.32)), (0.105, 0.9, tone(0.14, 196, 155, warmth=0.32)))
    yield "UI/toggle_on", "Upward toggle", 0.20, tone(0.095, 540, 1020)
    yield "UI/toggle_off", "Downward toggle", 0.18, tone(0.095, 820, 420)
    yield "UI/notification", "Small, clear notification chime", 0.25, chime([660, 990], 0.09, 0.33)
    yield "UI/open", "Soft rising panel motion", 0.16, mix((0, 0.7, tone(0.13, 370, 700, decay=3)), (0, 0.4, noise(0.12, 1900, 2, decay=3)))
    yield "UI/close", "Soft falling panel motion", 0.15, mix((0, 0.7, tone(0.11, 650, 330, decay=3)), (0, 0.4, noise(0.10, 1500, 3, decay=3)))
    yield "Gameplay/jump", "Playful upward hop", 0.34, tone(0.22, 180, 720, warmth=0.15, decay=2.6)
    yield "Gameplay/land", "Small landing thump", 0.36, impact(10, 115, 0.20)
    yield "Gameplay/pickup", "Bright collectible ping", 0.30, chime([1174.66, 1760], 0.055, 0.22)
    yield "Gameplay/checkpoint", "Warm checkpoint arpeggio", 0.34, chime([523.25, 659.25, 783.99], 0.09, 0.36)
    yield "Gameplay/hurt", "Brief downward damage pulse", 0.36, mix((0, 1, tone(0.22, 280, 75, warmth=0.28)), (0, 0.8, noise(0.12, 2400, 11, decay=7)))
    yield "Gameplay/death", "Descending failure cue", 0.32, chime([392, 311.13, 196], 0.14, 0.33)
    yield "Gameplay/respawn", "Gentle rising return cue", 0.30, mix((0, 0.65, tone(0.36, 190, 760, decay=2)), (0.18, 0.65, chime([523.25, 1046.50], 0.07, 0.28)))
    yield "Gameplay/goal", "Short major-chord celebration", 0.38, mix((0, 1, chime([523.25, 659.25, 783.99, 1046.50], 0.11, 0.40)), (0.34, 0.30, chime([523.25, 659.25, 783.99], 0, 0.55)))
    yield "Gameplay/impact_soft", "Muted collision or placement", 0.27, impact(12, 190, 0.12)
    yield "Gameplay/impact_hard", "Crisp collision or break", 0.40, impact(13, 170, 0.21, hard=True)
    yield "Gameplay/whoosh", "Short movement or swing", 0.28, mix((0, 1.4, noise(0.24, 3200, 14, decay=1.5)), (0, 0.15, tone(0.24, 900, 180, decay=2)))
    for i in range(1, 5):
        yield f"Gameplay/footstep_{i:02d}", "Stylized dry footstep; alternate the four variants", 0.19, impact(30 + i, 105 + i * 13, 0.09 + i * 0.009)


def write_wav(path, samples, peak):
    # Remove DC and fade the outer edges after summing layers. Preserve generous digital headroom.
    mean = sum(samples) / len(samples)
    edge = round(RATE * 0.002)
    samples = [(value - mean) * min(1, i / edge, (len(samples) - 1 - i) / edge)
               for i, value in enumerate(samples)]
    gain = peak / max(abs(value) for value in samples)
    pcm = array("h", (round(value * gain * 32767) for value in samples))
    if sys.byteorder != "little":
        pcm.byteswap()
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(pcm.tobytes())
    # Inspect the actual written file, not just the floating-point source.
    with wave.open(str(path), "rb") as output:
        assert (output.getnchannels(), output.getsampwidth(), output.getframerate()) == (1, 2, RATE)
        decoded = array("h", output.readframes(output.getnframes()))
    if sys.byteorder != "little":
        decoded.byteswap()
    assert decoded[0] == decoded[-1] == 0
    assert 0 < max(abs(value) for value in decoded) < 32767
    rms = math.sqrt(sum(value * value for value in decoded) / len(decoded)) / 32768
    return {
        "durationSeconds": round(len(samples) / RATE, 4),
        "peakDbfs": round(20 * math.log10(max(abs(value) for value in decoded) / 32768), 2),
        "rmsDbfs": round(20 * math.log10(rms), 2),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }


def write_preview(entries, *, output=None, title="Mana starter sounds", introduction=None):
    rows = []
    for entry in entries:
        name = html.escape(entry["file"])
        rows.append(f'<article><div><strong>{name[:-4]}</strong><p>{html.escape(entry["description"])}</p></div>'
                    f'<audio controls preload="none" src="{name}"></audio></article>')
    page = '''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Mana starter sounds</title>
<style>
body { font:16px system-ui,sans-serif; max-width:960px; margin:40px auto; padding:0 24px; background:#121820; color:#edf2f8; }
p { color:#acbdd0; line-height:1.5; } article { display:flex; align-items:center; justify-content:space-between;
gap:20px; padding:18px 0; border-bottom:1px solid #2b3542; } article p { margin:6px 0 0; font-size:14px; }
audio { width:290px; flex-shrink:0; } button { background:#aacdff; color:#152234; border:0; border-radius:6px;
padding:12px 20px; font:inherit; cursor:pointer; margin-right:10px; } #status { min-height:24px; }
@media(max-width:650px) { article { align-items:stretch; flex-direction:column; } audio { width:100%; } }
</style>
<h1>Mana starter sounds</h1><p>25 original synthesized effects. Soft UI feedback and playful gameplay sounds.
Mono · 48 kHz · 16-bit PCM WAV. Start with a comfortable speaker volume.</p>
<button id="play">Play all</button><button id="stop">Stop</button><p id="status" role="status"></p>
''' + "\n".join(rows) + '''
<script>
const clips = [...document.querySelectorAll('audio')];
const status = document.querySelector('#status');
let generation = 0;
function stop() { generation++; for (const clip of clips) { clip.pause(); clip.currentTime = 0; } status.textContent = ''; }
document.querySelector('#stop').onclick = stop;
document.querySelector('#play').onclick = async () => {
    stop(); const current = generation;
    for (const clip of clips) {
        if (current !== generation) { break; }
        status.textContent = clip.closest('article').querySelector('strong').textContent;
        try {
            await new Promise((resolve, reject) => {
                clip.addEventListener('ended', resolve, { once:true });
                clip.addEventListener('pause', resolve, { once:true });
                clip.addEventListener('error', () => reject(new Error('Unable to load sound')), { once:true });
                clip.play().catch(reject);
            });
        } catch (error) { status.textContent = error.message; return; }
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    if (current === generation) { status.textContent = 'Finished'; }
};
for (const clip of clips) { clip.addEventListener('play', () => { for (const other of clips) { if (other !== clip) { other.pause(); } } }); }
</script></html>
'''
    page = page.replace("Mana starter sounds", html.escape(title))
    if introduction is not None:
        page = page.replace("25 original synthesized effects. Soft UI feedback and playful gameplay sounds.", html.escape(introduction))
    ((output or OUTPUT) / "preview.html").write_text(page, encoding="utf-8")


def main():
    entries = []
    for name, description, peak, samples in definitions():
        filename = name + ".wav"
        metrics = write_wav(OUTPUT / filename, samples, peak)
        entries.append({"file": filename, "description": description, **metrics})
    manifest = {"sampleRate": RATE, "channels": 1, "bitsPerSample": 16,
                "provenance": "Original procedural synthesis; no third-party samples or recordings.", "sounds": entries}
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    write_preview(entries)
    total = sum((OUTPUT / entry["file"]).stat().st_size for entry in entries)
    print(f"Generated and validated {len(entries)} WAVs, {total:,} bytes, in {OUTPUT}")


if __name__ == "__main__":
    main()
