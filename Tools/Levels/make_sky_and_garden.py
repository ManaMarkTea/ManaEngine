"""Writes Content/Levels/Night Sky.manalevel and Content/Levels/Particle Garden.manalevel.
They are authored here (random pine rings, fire presets) and then hand-edited as levels like any other; run from the
repo root. Node ids are fresh each run, so only rerun it to start over -- edits made in the editor keep their ids.
"""
import json
import math
import random
import uuid


def hid():
    return uuid.uuid4().hex


def shape(name, shp, pos, size, color, rot=(0, 0, 0)):
    # Spheres and pyramids are a unit mesh stretched by the node's scale (the loader overwrites it from "scale"),
    # while cubes and disks carry their size in the mesh.
    stretched = shp in ("Sphere", "Pyramid")
    return {"id": hid(), "name": name, "kind": "Shape", "position": list(pos), "rotation": list(rot),
            "scale": list(size) if stretched else [1, 1, 1], "shape": shp, "color": color,
            "size": [1, 1, 1] if stretched else list(size)}


def group(name, pos, scripts, rot=(0, 0, 0)):
    return {"id": hid(), "name": name, "kind": "Group", "position": list(pos), "rotation": list(rot),
            "scale": [1, 1, 1], "scripts": scripts}


def script(kind, props):
    return {"id": hid(), "type": kind, "properties": props}


def emitter(name, pos, props, rot=(0, 0, 0), extra=None):
    scripts = [script("ParticleEmitter", props)]
    if extra:
        scripts.append(extra)
    return group(name, pos, scripts, rot)


def light(color, intensity, rng, flicker=0.0, speed=8.0):
    props = {"color": color, "intensity": intensity, "range": rng, "lightType": "Point"}
    if flicker:
        props["flickerAmount"] = flicker
        props["flickerSpeed"] = speed
    return script("Light", props)


def write(path, level):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(level, handle, indent=2)


# ---------------------------------------------------------------- Night Sky
random.seed(11)
CX, CZ = 90.0, 90.0
nodes = [shape("Ground", "Disk", [CX, -0.18, CZ], [150, 0.4, 150], [0.22, 0.30, 0.20, 1])]
for i in range(110):
    a = random.uniform(0, math.tau)
    r = math.sqrt(random.uniform(12 * 12, 44 * 44))
    height = random.uniform(5.0, 12.0)
    width = height * random.uniform(0.28, 0.4)
    shade = random.uniform(0.07, 0.14)
    nodes.append(shape(f"Pine {i + 1}", "Pyramid", [CX + math.cos(a) * r, 0, CZ + math.sin(a) * r],
                       [width, height, width], [shade, shade * 1.8, shade * 1.1, 1]))

nodes.append(shape("Observer's Rock", "Cube", [CX, 0, CZ + 4], [3, 0.8, 2], [0.12, 0.12, 0.14, 1], (0, 0.4, 0)))

# A small camp in front: a flickering fire, and glowing lanterns on posts round the clearing.
nodes.append(emitter("Camp Flames", [CX - 3.5, 0.2, CZ + 1.5], {
    "blendMode": "Additive", "shape": "Flames", "rate": 70, "speed": 1.1, "spreadDegrees": 12, "radius": 0.18,
    "lifetime": 0.8, "gravity": [0, 2.4, 0], "drag": 0.8, "turbulence": 2.4, "turbulenceScale": 1.5,
    "startSize": 0.26, "endSize": 0.04, "sizeBulge": 1.4, "useMidColor": True,
    "startColor": [1, 0.95, 0.7, 0.9], "midColor": [1, 0.55, 0.12, 0.8], "endColor": [0.6, 0.1, 0.02, 0],
    "maxParticles": 180}, extra=light([1, 0.6, 0.25, 1], 2.6, 11, 0.4, 9)))
nodes.append(emitter("Camp Embers", [CX - 3.5, 0.5, CZ + 1.5], {
    "blendMode": "Additive", "rate": 10, "speed": 2.4, "spreadDegrees": 35, "lifetime": 2.6, "gravity": [0, 0.9, 0],
    "drag": 0.3, "turbulence": 2.2, "startSize": 0.05, "endSize": 0.01,
    "startColor": [1, 0.7, 0.2, 1], "endColor": [1, 0.2, 0.02, 0], "brightness": 2.0, "maxParticles": 80}))
for k in range(8):
    a = k * math.tau / 8
    nodes.append(shape(f"Camp Stone {k + 1}", "Cube", [CX - 3.5 + math.cos(a) * 0.8, 0, CZ + 1.5 + math.sin(a) * 0.8],
                       [0.45, 0.3, 0.45], [0.3, 0.29, 0.28, 1], (0, a, 0)))

for k, (dx, dz) in enumerate([(-9, -2), (8, -4), (-6, 8), (10, 6), (0, -10), (-12, 4)]):
    nodes.append(shape(f"Lantern Post {k + 1}", "Cube", [CX + dx, 0, CZ + dz], [0.12, 2.0, 0.12], [0.08, 0.06, 0.05, 1]))
    bulb = shape(f"Lantern {k + 1}", "Sphere", [CX + dx, 2.1, CZ + dz], [0.2, 0.2, 0.2], [1, 0.8, 0.45, 1])
    bulb["scripts"] = [script("Glow", {"color": [1, 0.72, 0.35, 1], "strength": 1.0, "lightRange": 20,
                                       "lightIntensity": 2.4, "lightOffset": [0, 0, 0], "pulseSpeed": 0.4 + 0.1 * k})]
    nodes.append(bulb)

# A pale moon-blue key light, so the trees and the clearing read without the sky's own (dim) moon doing it.
nodes.append(group("Moonlight", [CX, 30, CZ], [script("Light", {
    "color": [0.55, 0.65, 1, 1], "intensity": 0.55, "lightType": "Directional", "castsShadows": True,
    "direction": [-0.45, -0.75, -0.5]})]))

# Fireflies drifting among the trees.
for k in range(9):
    a = random.uniform(0, math.tau)
    r = random.uniform(4, 22)
    nodes.append(emitter(f"Fireflies {k + 1}", [CX + math.cos(a) * r, random.uniform(0.8, 2.2), CZ + math.sin(a) * r], {
        "blendMode": "Additive", "rate": 3.5, "speed": 0.25, "spreadDegrees": 180, "radius": random.uniform(3, 6),
        "lifetime": 5, "gravity": [0, 0.04, 0], "turbulence": 0.6, "turbulenceScale": 0.5,
        "startSize": 0.10, "endSize": 0.10, "startColor": [0.75, 1, 0.35, 1], "endColor": [0.75, 1, 0.35, 0],
        "brightness": 1.4, "maxParticles": 30}))

write("Content/Levels/Night Sky.manalevel", {
    "format": "mana-level", "version": 2, "name": "Night Sky",
    "camera": {"position": [CX, 1.7, CZ + 8], "rotation": [0.2, 0, 0]},
    "sky": {"mode": "DayNightCycle", "dateTime": "2026-12-20T22:30:00-08:00", "timeSpeed": 15, "constellationLines": True},
    "nodes": nodes})

# ---------------------------------------------------------------- Particle Garden
nodes = [shape("Lawn", "Disk", [50, -0.18, 40], [70, 0.4, 70], [0.16, 0.30, 0.14, 1])]


def ring(x, label):
    for k in range(8):
        a = k * math.tau / 8
        nodes.append(shape(f"{label} Stone {k + 1}", "Cube", [x + math.cos(a) * 0.9, 0, 40 + math.sin(a) * 0.9],
                           [0.5, 0.35, 0.5], [0.35, 0.34, 0.33, 1], (0, a, 0)))


# Realistic fire: additive teardrops that go white-hot, orange, red and dark, pushed about by turbulence, with
# glowing embers, a slow dark smoke, and a light that wavers.
ring(41, "Realistic Fire")
nodes.append(emitter("Realistic Fire Flames", [41, 0.25, 40], {
    "blendMode": "Additive", "shape": "Flames", "rate": 95, "speed": 1.3, "spreadDegrees": 13, "radius": 0.22,
    "lifetime": 0.85, "gravity": [0, 2.6, 0], "drag": 0.8, "turbulence": 3.0, "turbulenceScale": 1.5,
    "startSize": 0.34, "endSize": 0.05, "sizeBulge": 1.45, "flameStretch": 2.3, "useMidColor": True, "midPosition": 0.4,
    "startColor": [1, 0.95, 0.7, 0.9], "midColor": [1, 0.55, 0.12, 0.8], "endColor": [0.6, 0.1, 0.02, 0],
    "maxParticles": 240}, extra=light([1, 0.6, 0.25, 1], 2.6, 10, 0.4, 9)))
nodes.append(emitter("Realistic Fire Embers", [41, 0.5, 40], {
    "blendMode": "Additive", "rate": 14, "speed": 2.5, "spreadDegrees": 35, "lifetime": 2.6, "gravity": [0, 1.0, 0],
    "drag": 0.3, "turbulence": 2.5, "startSize": 0.05, "endSize": 0.01,
    "startColor": [1, 0.7, 0.2, 1], "endColor": [1, 0.2, 0.02, 0], "brightness": 2.0, "maxParticles": 100}))
nodes.append(emitter("Realistic Fire Smoke", [41, 1.3, 40], {
    "rate": 11, "speed": 0.5, "spreadDegrees": 14, "lifetime": 5.5, "gravity": [0.2, 0.6, 0], "drag": 0.2,
    "turbulence": 1.4, "turbulenceScale": 0.8, "startSize": 0.5, "endSize": 2.3,
    "startColor": [0.22, 0.22, 0.24, 0.35], "endColor": [0.4, 0.4, 0.42, 0], "maxParticles": 100}))

# Stylized fire: flat cel-shaded tongues in four hard colour bands, chunky square-ish sparks, round smoke puffs.
ring(46, "Stylized Fire")
nodes.append(emitter("Stylized Fire Flames", [46, 0.25, 40], {
    "shape": "Flames", "hardEdge": True, "colorSteps": 4, "rate": 22, "speed": 1.0, "spreadDegrees": 9, "radius": 0.15,
    "lifetime": 0.9, "gravity": [0, 1.8, 0], "turbulence": 1.0, "turbulenceScale": 0.9,
    "startSize": 0.5, "endSize": 0.0, "sizeBulge": 1.2, "flameStretch": 2.0, "useMidColor": True, "midPosition": 0.5,
    "startColor": [1, 0.92, 0.35, 1], "midColor": [1, 0.55, 0.1, 1], "endColor": [0.85, 0.15, 0.1, 1],
    "maxParticles": 60}, extra=light([1, 0.62, 0.3, 1], 2.2, 9, 0.18, 5)))
nodes.append(emitter("Stylized Fire Sparks", [46, 0.6, 40], {
    "hardEdge": True, "colorSteps": 2, "rate": 7, "speed": 2.4, "spreadDegrees": 28, "lifetime": 1.6,
    "gravity": [0, -1.5, 0], "startSize": 0.1, "endSize": 0.0,
    "startColor": [1, 0.9, 0.4, 1], "endColor": [1, 0.45, 0.1, 1], "maxParticles": 40}))
nodes.append(emitter("Stylized Fire Smoke", [46, 1.2, 40], {
    "hardEdge": True, "colorSteps": 2, "rate": 3.5, "speed": 0.5, "spreadDegrees": 10, "lifetime": 3.5,
    "gravity": [0.15, 0.5, 0], "startSize": 0.25, "endSize": 0.7,
    "startColor": [0.3, 0.28, 0.38, 0.8], "endColor": [0.45, 0.43, 0.55, 0.0], "maxParticles": 30}))

nodes.append(shape("Fountain Basin", "Disk", [54, 0, 40], [3.2, 0.5, 3.2], [0.5, 0.5, 0.52, 1]))
nodes.append(emitter("Fountain Jet", [54, 0.5, 40], {
    "rate": 110, "speed": 5.8, "spreadDegrees": 9, "lifetime": 1.9, "gravity": [0, -9.8, 0], "startSize": 0.12,
    "endSize": 0.08, "startColor": [0.65, 0.85, 1, 0.9], "endColor": [0.85, 0.95, 1, 0.1], "brightness": 0.9,
    "maxParticles": 300}))
nodes.append(emitter("Fountain Spray", [54, 0.5, 40], {
    "rate": 40, "speed": 3.0, "spreadDegrees": 70, "lifetime": 0.8, "gravity": [0, -9.8, 0], "startSize": 0.05,
    "endSize": 0.03, "startColor": [0.8, 0.92, 1, 0.7], "endColor": [0.9, 0.97, 1, 0], "maxParticles": 150}))
nodes.append(emitter("Rain Cloud", [60, 7, 40], {
    "rate": 170, "speed": 9, "spreadDegrees": 3, "radius": 2.5, "lifetime": 0.75, "gravity": [0, 0, 0],
    "startSize": 0.04, "endSize": 0.04, "startColor": [0.75, 0.85, 1, 0.75], "endColor": [0.75, 0.85, 1, 0.25],
    "shape": "Streaks", "streakLength": 0.12, "maxParticles": 300}, rot=(3.14159, 0, 0)))
nodes.append(emitter("Fireflies", [49, 1.4, 36], {
    "blendMode": "Additive", "rate": 8, "speed": 0.3, "spreadDegrees": 180, "radius": 3.5, "lifetime": 4.0,
    "gravity": [0, 0.05, 0], "turbulence": 0.5, "startSize": 0.09, "endSize": 0.09,
    "startColor": [0.8, 1, 0.4, 1], "endColor": [0.8, 1, 0.4, 0], "brightness": 1.4, "maxParticles": 60}))

write("Content/Levels/Particle Garden.manalevel", {
    "format": "mana-level", "version": 2, "name": "Particle Garden",
    "camera": {"position": [47, 1.8, 47], "rotation": [-0.1, 0, 0]},
    "sky": {"mode": "DayNightCycle", "dateTime": "2026-06-21T21:15:00-07:00", "timeSpeed": 0, "constellationLines": False},
    "nodes": nodes})
print("written")
