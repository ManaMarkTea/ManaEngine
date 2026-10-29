"""Writes Content/Levels/Night Sky.manalevel.
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

# Particle Garden was first written here too; it is hand-maintained now (patch it by node, never regenerate).
print("written")
