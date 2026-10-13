"""Writes the endless runner's track sections (Content/Prefabs/Runner/*.manaprefab) and its level
(Content/Levels/Endless Run.manalevel). Run from anywhere: python Tools/Runner/make_sections.py

A section is 30 m of track along -Z from its origin, lanes at x = -2.2, 0, 2.2 (Games/Runner/Code/RunnerMode.cs).
The runner reads pieces by name: "Obstacle ..." is solid by its bounds (low: jump it; raised: duck under it; tall:
change lane; a top within a step can be run along), "Coin" is a coin (its Trigger and Collectible scripts collect it),
"PowerUp Magnet/Shield/Double/Jump" a power-up. Ids are made from names, so running this again keeps them; once the
sections are hand-edited in the editor, edit them there instead.
"""
import json
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PREFABS = ROOT / "Content" / "Prefabs" / "Runner"
LEVEL = ROOT / "Content" / "Levels" / "Endless Run.manalevel"
LANES = (-2.2, 0.0, 2.2)
LENGTH = 30.0

SUBWAY = "zip:kenney_train-kit.zip/Models/GLB format/train-electric-subway-a.glb"
SUBWAY_B = "zip:kenney_train-kit.zip/Models/GLB format/train-electric-subway-b.glb"
CONTAINER = "zip:kenney_train-kit.zip/Models/GLB format/train-carriage-container-red.glb"
BARRIER = "zip:kenney_city-kit-roads.zip/Models/GLB format/construction-barrier.glb"
CONE = "zip:kenney_city-kit-roads.zip/Models/GLB format/construction-cone.glb"
LIGHT = "zip:kenney_city-kit-roads.zip/Models/GLB format/light-square.glb"
CRATE = "zip:kenney_pirate-kit.zip/Models/GLB format/crate.glb"
PALM = "zip:kenney_pirate-kit.zip/Models/GLB format/palm-straight.glb"
PALM_BEND = "zip:kenney_pirate-kit.zip/Models/GLB format/palm-bend.glb"
COIN = "zip:kenney_mini-dungeon.zip/Models/GLB format/coin.glb"

ASPHALT = [0.2, 0.21, 0.25, 1]
PAVEMENT = [0.62, 0.62, 0.66, 1]
GRASS = [0.36, 0.66, 0.32, 1]
WHITE = [0.95, 0.95, 0.92, 1]
YELLOW = [1.0, 0.78, 0.15, 1]


def uid(*parts):
    return uuid.uuid5(uuid.NAMESPACE_URL, "mana-runner/" + "/".join(parts)).hex


class Section:
    def __init__(self, name):
        self.name = name
        self.children = []
        self.counts = {}

    def _id(self, label):
        n = self.counts.get(label, 0)
        self.counts[label] = n + 1
        return uid(self.name, label, str(n))

    def node(self, name, kind, position, rotation=(0, 0, 0), scale=(1, 1, 1), **extra):
        node = {"id": self._id(name), "name": name, "kind": kind, "position": list(position),
                "rotation": list(rotation), "scale": list(scale)}
        node.update(extra)
        self.children.append(node)
        return node

    def shape(self, name, center, size, color, shape="Cube", material=None):
        extra = {"shape": shape, "color": color, "size": list(size)}
        if material:
            extra["material"] = material
        return self.node(name, "Shape", center, **extra)

    def mesh(self, name, asset, position, rotation=(0, 0, 0), scale=1.0):
        return self.node(name, "Mesh", position, rotation, (scale, scale, scale), asset=asset)

    # ---- pieces ----------------------------------------------------------------------------------------------

    def road(self):
        """The 3-lane road, pavements, grass verges and lamp posts and palms along the sides."""
        mid = -LENGTH / 2
        # Tops just above y = 0, so the road never fights the play ground beneath it.
        self.shape("Road", (0, -0.08, mid), (7.6, 0.2, LENGTH), ASPHALT)
        for side in (-1, 1):
            self.shape("Pavement", (side * 4.7, 0.0, mid), (2.6, 0.25, LENGTH), PAVEMENT)
            self.shape("Verge", (side * 10.0, -0.03, mid), (8.0, 0.2, LENGTH), GRASS)
        for z in range(0, int(LENGTH), 5):
            for x in (-1.1, 1.1):
                self.shape("Lane Line", (x, 0.03, -z - 1.25), (0.1, 0.02, 2.5), WHITE)
        for i, z in enumerate((-7.0, -22.0)):
            self.mesh("Lamp", LIGHT, (4.1, 0.12, z), (0, 3.14159, 0), 3.2)
            self.mesh("Lamp", LIGHT, (-4.1, 0.12, z - 7.5), (0, 0, 0), 3.2)
        self.mesh("Palm", PALM, (7.5, 0.0, -4.0), scale=1.1)
        self.mesh("Palm", PALM_BEND, (-7.2, 0.0, -16.0), (0, 1.2, 0), 1.1)
        self.mesh("Palm", PALM, (-8.0, 0.0, -27.0), scale=0.9)
        self.mesh("Palm", PALM_BEND, (8.4, 0.0, -21.0), (0, 2.6, 0), 1.0)

    def coin(self, lane, z, y=0.55):
        self.node("Coin", "Group", (LANES[lane], y, z), scripts=[
            {"id": self._id("coin-trigger"), "type": "Trigger", "properties": {"size": [1.0, 1.2, 1.0], "offset": [0, 0.3, 0]}},
            {"id": self._id("coin-collectible"), "type": "Collectible", "properties": {"value": 1, "spinDegreesPerSecond": 180}},
        ], children=[{"id": self._id("coin-model"), "name": "Coin Model", "kind": "Mesh", "position": [0, 0.0, 0],
                      "rotation": [0, 0, 0], "scale": [1.4, 1.4, 1.4], "asset": COIN}])

    def coins(self, lane, z0, z1, step=2.0, y=0.55):
        z = z0
        while z >= z1:
            self.coin(lane, z, y)
            z -= step

    def jump_arc(self, lane, z_center, height=1.6):
        """Coins over something low: they show the jump."""
        for i, dz in enumerate((3.0, 1.5, 0.0, -1.5, -3.0)):
            lift = height * (1.0 - (dz / 3.4) ** 2)
            self.coin(lane, z_center + dz, 0.55 + lift)

    def barrier(self, lane, z):
        """Jump it: a construction barrier across the lane, 1.1 m high."""
        self.mesh("Obstacle Barrier", BARRIER, (LANES[lane], 0.0, z), (0, 1.5708, 0), 9.0)

    def cone(self, lane, z, dx=0.0):
        self.mesh("Obstacle Cone", CONE, (LANES[lane] + dx, 0.0, z), scale=8.0)

    def bar(self, lane, z):
        """Duck under it: a striped bar 1.1 m up between two posts."""
        x = LANES[lane]
        self.shape("Obstacle Bar", (x, 1.32, z), (2.0, 0.42, 0.3), YELLOW)
        for side in (-1, 1):
            self.shape("Post", (x + side * 1.08, 0.75, z), (0.12, 1.5, 0.12), WHITE)

    def train(self, lane, z_front, cars=3, model=SUBWAY):
        """Change lane: a subway train standing in the lane, front at z_front, cars behind it."""
        for i in range(cars):
            self.mesh("Obstacle Train", model if i == 0 else SUBWAY_B, (LANES[lane], 0.5, z_front - 1.82 - i * 3.64), scale=1.4)

    def container(self, lane, z):
        """Low freight: jump onto it and run along the top."""
        self.mesh("Obstacle Container", CONTAINER, (LANES[lane], 0.5, z), scale=1.4)

    def crate(self, lane, z):
        self.mesh("Obstacle Crate", CRATE, (LANES[lane], 0.0, z), scale=1.3)

    def power(self, kind, lane, z):
        color = {"Magnet": [0.95, 0.3, 0.45, 1], "Shield": [0.3, 0.6, 1.0, 1],
                 "Double": [1.0, 0.8, 0.2, 1], "Jump": [0.3, 0.9, 0.5, 1]}[kind]
        self.shape(f"PowerUp {kind}", (LANES[lane], 1.0, z), (0.8, 0.8, 0.8), color, shape="Icosphere",
                   material={"emission": color[:3], "emissionStrength": 2.5, "roughness": 0.3})

    def write(self):
        root = {"id": uid(self.name, "root"), "name": self.name, "kind": "Group", "position": [0, 0, 0],
                "rotation": [0, 0, 0], "scale": [1, 1, 1], "children": self.children}
        prefab = {"format": "mana-prefab", "version": 1, "id": uid(self.name, "asset"), "name": self.name,
                  "root": root, "properties": []}
        PREFABS.mkdir(parents=True, exist_ok=True)
        (PREFABS / f"{self.name}.manaprefab").write_text(json.dumps(prefab, indent=2) + "\n", encoding="utf-8")


def sections():
    s = Section("Runner Start")
    s.road()
    s.coins(1, -8.0, -28.0)
    yield s

    s = Section("Runner Barriers")
    s.road()
    s.barrier(0, -8.0); s.barrier(2, -8.0)
    s.coins(1, -4.0, -12.0)
    s.barrier(1, -20.0)
    s.jump_arc(1, -20.0)
    s.coins(0, -16.0, -26.0)
    yield s

    s = Section("Runner Trains")
    s.road()
    s.train(0, -4.0, cars=4)
    s.train(2, -12.0, cars=4)
    s.coins(1, -4.0, -28.0)
    s.power("Magnet", 2, -6.0)
    yield s

    s = Section("Runner Duck")
    s.road()
    s.bar(0, -8.0); s.bar(1, -8.0)
    s.coins(1, -6.0, -10.0, y=0.4)
    s.bar(1, -20.0); s.bar(2, -20.0)
    s.coins(2, -17.0, -23.0, y=0.4)
    s.cone(0, -20.0)
    yield s

    s = Section("Runner Crates")
    s.road()
    for z in (-6.0, -9.0, -12.0):
        s.container(1, z)
    s.coins(1, -5.0, -13.0, step=1.5, y=1.85)
    s.crate(0, -18.0); s.crate(2, -22.0)
    s.jump_arc(0, -18.0); s.jump_arc(2, -22.0)
    s.power("Double", 1, -24.0)
    yield s

    s = Section("Runner Mixed")
    s.road()
    s.train(1, -2.0, cars=3)
    s.barrier(0, -9.0)
    s.jump_arc(0, -9.0)
    s.bar(2, -14.0)
    s.coins(2, -12.0, -16.0, y=0.4)
    s.barrier(1, -24.0); s.barrier(2, -24.0)
    s.coins(0, -19.0, -27.0)
    s.power("Shield", 0, -15.0)
    yield s

    s = Section("Runner Cones")
    s.road()
    for i, z in enumerate((-5.0, -10.0, -15.0, -20.0, -25.0)):
        lane = (0, 2, 1, 0, 2)[i]
        s.cone(lane, z)
    for i, z in enumerate((-7.5, -12.5, -17.5, -22.5)):
        s.coin((1, 0, 2, 1)[i], z)
    s.power("Jump", 1, -27.0)
    yield s

    s = Section("Runner Train Yard")
    s.road()
    s.train(0, -2.0, cars=6, model=SUBWAY_B)
    s.train(2, -2.0, cars=6)
    s.barrier(1, -12.0)
    s.jump_arc(1, -12.0)
    s.bar(1, -22.0)
    s.coins(1, -20.0, -24.0, y=0.4)
    yield s


def level():
    start = [48.0, 0.0, 400.0]
    return {
        "format": "mana-level",
        "version": 2,
        "name": "Endless Run",
        "web": {"publish": True, "description": "Run the three lanes for as long as you can: swipe to change lane, jump and duck, grab power-ups.", "order": 0},
        "camera": {"position": [48.0, 6.0, 410.0], "rotation": [-0.3, 0.0, 0.0]},
        "sky": {"mode": "Day"},
        "gameModes": [{"id": uid("level", "mode"), "type": "Runner", "properties": {}}],
        "nodes": [],
        "playerStart": {"id": uid("level", "start"), "name": "Player Start", "kind": "PlayerStart", "position": start,
                        "rotation": [0, 0, 0], "scale": [1, 1, 1],
                        "character": {"pack": "kenney_mini-characters", "name": "character-female-b", "height": 1.8}},
    }


if __name__ == "__main__":
    for section in sections():
        section.write()
        print("wrote", section.name)
    LEVEL.write_text(json.dumps(level(), indent=2) + "\n", encoding="utf-8")
    print("wrote", LEVEL.name)
