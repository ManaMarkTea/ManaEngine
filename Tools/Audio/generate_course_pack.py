"""Original procedural effects tailored to Levels/Sample Course.manalevel. Standard library only."""
from array import array
import hashlib
import json
import math
import random
import sys
import wave

from generate_starter_pack import RATE, ROOT, TAU, chime, impact, mix, noise, tone, write_preview, write_wav

OUTPUT = ROOT / "Mana/Assets/Audio/SampleCourse"
LEVEL = ROOT / "Levels/Sample Course.manalevel"


def periodic_noise(duration, cutoff, seed):
    """Two filter passes over the same random cycle settle the state across its wrap boundary."""
    rng = random.Random(seed)
    raw = [rng.uniform(-1, 1) for _ in range(round(duration * RATE))]
    alpha = 1 - math.exp(-TAU * cutoff / RATE)
    low = smooth = 0.0
    result = []
    for turn in range(2):
        for value in raw:
            low += alpha * (value - low)
            smooth += alpha * (low - smooth)
            if turn == 1:
                result.append(smooth)
    return result


def machinery(duration, ferry=False):
    texture = periodic_noise(duration, 650 if ferry else 1400, 211 if ferry else 212)
    result = []
    # All oscillator/modulation frequencies complete whole cycles in the four-second buffer.
    for i, value in enumerate(texture):
        t = i / RATE
        if ferry:
            vibration = (math.sin(TAU * 48 * t) + 0.35 * math.sin(TAU * 96 * t)) * (0.78 + 0.22 * math.cos(TAU * 1.5 * t))
            creak = math.sin(TAU * 220 * t + 2 * math.sin(TAU * 0.5 * t)) * (0.5 - 0.5 * math.cos(TAU * 0.25 * t)) ** 4
            result.append(0.12 * vibration + 0.08 * creak + 0.55 * value)
        else:
            motor = 0.2 * math.sin(TAU * 90 * t) + 0.09 * math.sin(TAU * 180 * t) + 0.035 * math.sin(TAU * 540 * t)
            motor *= 0.88 + 0.12 * math.cos(TAU * 6 * t)
            result.append(motor + 0.45 * value)
    return result


def harbor():
    duration = 6
    soft = periodic_noise(duration, 950, 220)
    foam = periodic_noise(duration, 3300, 221)
    return [(0.2 + 0.8 * (0.5 - 0.5 * math.cos(TAU * i / len(soft))) ** 2) * low
            + 0.20 * (0.5 - 0.5 * math.cos(TAU * i / len(soft) + 0.8)) ** 4 * high
            for i, (low, high) in enumerate(zip(soft, foam))]


def splash(seed, oily=False):
    duration = 0.3 if oily else 0.65
    layers = [(0, 1.5, noise(duration, 1350 if oily else 4200, seed, decay=3.5)),
              (0, 0.45, tone(duration * 0.4, 110, 45, warmth=0.03))]
    rng = random.Random(seed)
    for i in range(5):
        start = 0.025 + i * (0.037 if oily else 0.065)
        frequency = rng.uniform(280, 540) if oily else rng.uniform(650, 1350)
        layers.append((start, 0.18, tone(0.075, frequency, frequency * 0.44, warmth=0.04, decay=4)))
    return mix(*layers)


def definitions():
    # name, description, target peak, samples, loop flag, actual level nodes
    surfaces = {
        "asphalt": ("Road 0", "Road 5"),
        "wood": ("Pier Planks 0a", "Pier Planks 1b", "Lumber Car"),
        "metal": ("Container B1", "Flatbed", "Boxcar", "Cargo Lift"),
        "sand": ("Sand Patch 0", "Sand Patch 3"),
    }
    for material, nodes in surfaces.items():
        for variant in range(1, 4):
            seed = 100 + list(surfaces).index(material) * 10 + variant
            if material == "asphalt":
                samples = mix((0, 0.6, impact(seed, 95 + variant * 8, 0.10)),
                              (0.015, 1.1, noise(0.075, 3400, seed + 100, decay=6)))
            elif material == "wood":
                base = 190 + variant * 11
                samples = mix((0, 0.75, impact(seed, 120, 0.13)),
                              (0, 0.25, tone(0.16, base, warmth=0.02, decay=8)),
                              (0.008, 0.13, tone(0.13, base * 2.37, warmth=0.02, decay=8)))
            elif material == "metal":
                base = 340 + variant * 17
                samples = mix((0, 0.7, impact(seed, 145, 0.12, hard=True)),
                              (0.004, 0.17, tone(0.24, base, warmth=0, decay=7)),
                              (0.003, 0.09, tone(0.18, base * 1.63, warmth=0, decay=8)))
            else:
                samples = mix((0, 0.3, impact(seed, 80 + variant * 7, 0.11)),
                              (0.014, 1.2, noise(0.15, 2300, seed + 100, decay=3.5)))
            yield f"Footsteps/{material}_{variant:02d}", f"{material.title()} footstep {variant}; alternate the three variants", 0.19, samples, False, nodes

    enemies = ("Road Bunny", "Yard Hog", "Pier Crab", "Boxcar Cat", "Container Dog")
    yield "Enemies/alert", "Small startled chirrup when the Yard Hog begins chasing", 0.26, chime([440, 880], 0.045, 0.14), False, ("Yard Hog",)
    yield "Enemies/stomp", "Soft squash and upward rebound when landing on an enemy", 0.35, mix((0, 1, impact(170, 140, 0.14)), (0.022, 0.5, tone(0.18, 220, 660, decay=3))), False, enemies
    yield "Enemies/defeat", "Tiny sparkle tail for an enemy disappearing; optional after stomp", 0.23, chime([1174.66, 1567.98], 0.035, 0.15), False, enemies
    yield "Hazards/oil_squelch", "Sticky contact with the Oil Slick or Spilled Oil", 0.28, splash(180, oily=True), False, ("Oil Slick", "Spilled Oil")
    yield "Hazards/water_splash", "Broad splash on falling into the lethal Harbor Water", 0.36, splash(181), False, ("Harbor Water",)
    yield "Hazards/fall", "Short descending whistle for a long fall, before death or splash", 0.23, mix((0, 1, tone(0.55, 1100, 140, warmth=0.02, decay=0.8)), (0.12, 0.2, noise(0.4, 1800, 182, decay=1))), False, ("Island I1", "Island I2", "Island I3", "Cargo Lift")
    yield "Feedback/low_health", "Quiet double heartbeat when health first drops to one", 0.23, mix((0, 1, tone(0.16, 105, 62, decay=7)), (0.17, 0.8, tone(0.19, 90, 55, decay=7))), False, ("Oil Slick", "Track Bed Hazard", "Yard Hog")
    yield "Feedback/all_coins", "Distinct reward for collecting the last of the course's 24 coins", 0.31, chime([783.99, 1046.50, 1318.51], 0.075, 0.3), False, ("Coin 1", "Coin 24")
    yield "Platforms/ferry_motion_loop", "Quiet rolling vibration and timber creak while the ferry moves; loop", 0.13, machinery(4, ferry=True), True, ("Ferry Platform",)
    yield "Platforms/lift_motor_loop", "Steady electric lift motor while the cargo lift moves; loop", 0.15, machinery(4), True, ("Cargo Lift",)
    yield "Platforms/stop_clunk", "Damped arrival clunk when either moving platform reaches an end", 0.29, mix((0, 1, impact(190, 120, 0.20, hard=True)), (0.048, 0.3, impact(191, 90, 0.15))), False, ("Ferry Platform", "Cargo Lift")
    yield "Ambience/harbor_water_loop", "Gentle lapping water for the harbor section; seamless loop", 0.13, harbor(), True, ("Harbor Water", "Pier Planks 0a", "Buoy 1")


def write_loop(path, samples, peak):
    # Loops intentionally have no start/end silence. Fade voices at playback start/stop instead.
    mean = sum(samples) / len(samples)
    centered = [value - mean for value in samples]
    gain = peak * 32767 / max(abs(value) for value in centered)
    pcm = array("h", (round(value * gain) for value in centered))
    seam_delta = abs(pcm[0] - pcm[-1]) / 32768
    rms_step = math.sqrt(sum((pcm[i] - pcm[i - 1]) ** 2 for i in range(1, len(pcm))) / (len(pcm) - 1)) / 32768
    assert seam_delta < max(0.001, rms_step * 6), "Loop seam has an abnormal sample jump"
    assert 0 < max(abs(value) for value in pcm) < 32767
    rms = math.sqrt(sum(value * value for value in pcm) / len(pcm)) / 32768
    if sys.byteorder != "little":
        pcm.byteswap()
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(pcm.tobytes())
    with wave.open(str(path), "rb") as output:
        assert output.getnframes() == len(samples)
        assert (output.getnchannels(), output.getsampwidth(), output.getframerate()) == (1, 2, RATE)
    return {"durationSeconds": len(samples) / RATE, "peakDbfs": round(20 * math.log10(peak), 2),
            "rmsDbfs": round(20 * math.log10(rms), 2), "loopSeamDelta": round(seam_delta, 7),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def main():
    level = json.loads(LEVEL.read_text(encoding="utf-8"))
    node_names = {node["name"] for node in level["nodes"]}
    entries = []
    for name, description, peak, samples, loop, nodes in definitions():
        missing = set(nodes) - node_names
        if missing:
            raise ValueError(f"Course has changed; review sound mapping for {name}: missing {sorted(missing)}")
        path = OUTPUT / (name + ".wav")
        metrics = (write_loop if loop else write_wav)(path, samples, peak)
        entries.append({"file": name + ".wav", "description": description, "loop": loop, "courseNodes": nodes, **metrics})
    manifest = {"sampleRate": RATE, "channels": 1, "bitsPerSample": 16,
                "sourceLevel": "Levels/Sample Course.manalevel", "sourceLevelSha256": hashlib.sha256(LEVEL.read_bytes()).hexdigest(),
                "provenance": "Original procedural synthesis; no third-party samples or recordings.", "sounds": entries}
    (OUTPUT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    write_preview(entries, output=OUTPUT, title="Sample Course sounds",
                  introduction=f"{len(entries)} effects for the road, cargo yard, harbor and rail course. Loop files play one cycle in this preview.")
    total = sum((OUTPUT / entry["file"]).stat().st_size for entry in entries)
    print(f"Generated and validated {len(entries)} WAVs ({sum(e['loop'] for e in entries)} loops), {total:,} bytes, in {OUTPUT}")


if __name__ == "__main__":
    main()
