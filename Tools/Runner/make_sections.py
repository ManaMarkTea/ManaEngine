"""Writes the endless runner's track sections (Content/Prefabs/Runner/<Biome>/*.manaprefab) and its level
(Content/Levels/Endless Run.manalevel). Run from anywhere: python Tools/Runner/make_sections.py

A section is 30 m of track along -Z from its origin, lanes at x = -2.2, 0, 2.2 (Games/Runner/Code/RunnerMode.cs). The
runner reads pieces by name:
  Ground...           stood on, by the top of its bounds (a road, a pier, a cloud) -- where there is none, a gap
  Ramp Up / Ramp Down a slope, rising (or falling) from its near end to its far end
  Obstacle...         solid by its bounds: low ones are jumped, raised ones ducked under, tall ones block the lane
  Launch <metres>     a pad that throws the runner up
  Enemy Charge/Patrol/Fly  runs at you down its lane / sweeps across the lanes / hovers; land on it to stomp it
  Coin, PowerUp Magnet/Shield/Double/Jump
  Exit                its height is where the next section begins (a dungeon's floor is 10 m down, the sky 30 m up)
The folder is the biome. The run goes City -> another biome (through its Enter section) -> its own sections -> out
through its Leave section -> City again. Ids are made from names, so running this again keeps them; once the sections
are hand-edited in the editor, edit them there instead.
"""
import json
import math
import shutil
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PREFABS = ROOT / "Content" / "Prefabs" / "Runner"
LEVEL = ROOT / "Content" / "Levels" / "Endless Run.manalevel"
LANES = (-2.2, 0.0, 2.2)
LENGTH = 30.0
DUNGEON_DEPTH = 10.0
SKY_HEIGHT = 30.0


def zip_model(pack, name):
    return f"zip:{pack}.zip/Models/GLB format/{name}.glb"


SUBWAY = zip_model("kenney_train-kit", "train-electric-subway-a")
SUBWAY_B = zip_model("kenney_train-kit", "train-electric-subway-b")
CONTAINER = zip_model("kenney_train-kit", "train-carriage-container-red")
BARRIER = zip_model("kenney_city-kit-roads", "construction-barrier")
CONE = zip_model("kenney_city-kit-roads", "construction-cone")
LIGHT = zip_model("kenney_city-kit-roads", "light-square")
CRATE = zip_model("kenney_pirate-kit", "crate")
BARREL = zip_model("kenney_pirate-kit", "barrel")
PALM = zip_model("kenney_pirate-kit", "palm-straight")
PALM_BEND = zip_model("kenney_pirate-kit", "palm-bend")
ROCKS = zip_model("kenney_pirate-kit", "rocks-sand-a")
COIN = zip_model("kenney_mini-dungeon", "coin")
ORC = zip_model("kenney_mini-dungeon", "character-orc")
COLUMN = zip_model("kenney_mini-dungeon", "column")
TRAP = zip_model("kenney_mini-dungeon", "trap")
BANNER = zip_model("kenney_mini-dungeon", "banner")
CRAB = zip_model("kenney_cube-pets_1.0", "animal-crab")
BEE = zip_model("kenney_cube-pets_1.0", "animal-bee")
SAILBOAT = zip_model("kenney_watercraft-pack", "boat-sail-a")
SPEEDBOAT = zip_model("kenney_watercraft-pack", "boat-speed-a")
BUOY = zip_model("kenney_watercraft-pack", "buoy")

ASPHALT = [0.2, 0.21, 0.25, 1]
PAVEMENT = [0.62, 0.62, 0.66, 1]
GRASS = [0.36, 0.66, 0.32, 1]
WHITE = [0.95, 0.95, 0.92, 1]
YELLOW = [1.0, 0.78, 0.15, 1]
SAND = [0.93, 0.83, 0.6, 1]
PLANKS = [0.58, 0.4, 0.24, 1]
STONE = [0.36, 0.34, 0.38, 1]
STONE_DARK = [0.34, 0.31, 0.3, 1]
WOOD = [0.45, 0.3, 0.18, 1]
CLOUD = [0.97, 0.98, 1.0, 1]
TORCH = [1.0, 0.62, 0.25, 1]


def uid(*parts):
    return uuid.uuid5(uuid.NAMESPACE_URL, "mana-runner/" + "/".join(parts)).hex


class Section:
    def __init__(self, biome, name):
        self.biome = biome
        self.name = name
        self.children = []
        self.counts = {}

    def _id(self, label):
        n = self.counts.get(label, 0)
        self.counts[label] = n + 1
        return uid(self.name, label, str(n))

    def node(self, name, kind, position, rotation=(0, 0, 0), scale=(1, 1, 1), **extra):
        node = {"id": self._id(name), "name": name, "kind": kind, "position": [round(v, 4) for v in position],
                "rotation": [round(v, 5) for v in rotation], "scale": list(scale)}
        node.update(extra)
        self.children.append(node)
        return node

    def shape(self, name, center, size, color, shape="Cube", material=None, rotation=(0, 0, 0), scripts=None):
        extra = {"shape": shape, "color": color, "size": [round(v, 4) for v in size]}
        if material:
            extra["material"] = material
        if scripts:
            extra["scripts"] = scripts
        return self.node(name, "Shape", center, rotation, **extra)

    def mesh(self, name, asset, position, rotation=(0, 0, 0), scale=1.0):
        return self.node(name, "Mesh", position, rotation, (scale, scale, scale), asset=asset)

    def exit(self, height):
        """Where the next section begins: this far up (or down) from this one's start."""
        self.node("Exit", "Group", (0, height, -LENGTH))

    # ---- ground ---------------------------------------------------------------------------------------------

    def ground(self, name, z0, z1, y=0.0, color=ASPHALT, width=7.6, x=0.0, depth=0.4):
        """A walkable slab from z0 to z1 (z0 nearer), its top at y."""
        self.shape(f"Ground {name}", (x, y - depth / 2 + 0.02, (z0 + z1) / 2), (width, depth, abs(z1 - z0)), color)

    def ramp(self, up, z0, z1, y0, y1, color=STONE, width=7.6):
        """A slope from height y0 at z0 to y1 at z1, as a tilted slab."""
        run = abs(z1 - z0)
        rise = y1 - y0
        length = math.hypot(run, rise)
        # Tilted about x: a positive angle lifts the far (-Z) end.
        angle = math.atan2(rise, run)
        self.shape("Ramp Up" if up else "Ramp Down", (0, (y0 + y1) / 2 - 0.18, (z0 + z1) / 2), (width, 0.36, length), color,
                   rotation=(angle, 0, 0))

    # ---- pieces ---------------------------------------------------------------------------------------------

    def coin(self, lane, z, y=0.55, x=None):
        self.node("Coin", "Group", (LANES[lane] if x is None else x, y, z), scripts=[
            {"id": self._id("coin-trigger"), "type": "Trigger", "properties": {"size": [1.0, 1.2, 1.0], "offset": [0, 0.3, 0]}},
            {"id": self._id("coin-collectible"), "type": "Collectible", "properties": {"value": 1, "spinDegreesPerSecond": 180}},
        ], children=[{"id": self._id("coin-model"), "name": "Coin Model", "kind": "Mesh", "position": [0, 0.0, 0],
                      "rotation": [0, 0, 0], "scale": [1.4, 1.4, 1.4], "asset": COIN}])

    def coins(self, lane, z0, z1, step=2.0, y=0.55):
        z = z0
        while z >= z1:
            self.coin(lane, z, y)
            z -= step

    def jump_arc(self, lane, z_center, height=1.6, base=0.0):
        """Coins over something low (or a gap): they show the jump."""
        for dz in (3.0, 1.5, 0.0, -1.5, -3.0):
            lift = height * (1.0 - (dz / 3.4) ** 2)
            self.coin(lane, z_center + dz, base + 0.55 + lift)

    def power(self, kind, lane, z, y=1.0):
        color = {"Magnet": [0.95, 0.3, 0.45, 1], "Shield": [0.3, 0.6, 1.0, 1],
                 "Double": [1.0, 0.8, 0.2, 1], "Jump": [0.3, 0.9, 0.5, 1]}[kind]
        self.shape(f"PowerUp {kind}", (LANES[lane], y, z), (0.8, 0.8, 0.8), color, shape="Icosphere",
                   material={"emission": color[:3], "emissionStrength": 2.5, "roughness": 0.3})

    def enemy(self, kind, model, lane, z, scale, y=0.0, yaw=0.0):
        self.mesh(f"Enemy {kind}", model, (LANES[lane], y, z), (0, yaw, 0), scale)

    def torch(self, x, z, y=3.0):
        self.shape("Torch", (x, y, z), (0.25, 0.4, 0.25), TORCH, material={"emission": TORCH[:3], "emissionStrength": 4.0},
                   scripts=[{"id": self._id("torch-light"), "type": "Light", "properties": {
                       "color": [1, 0.6, 0.25, 1], "intensity": 3.0, "range": 12, "lightType": "Point",
                       "flickerAmount": 0.35, "flickerSpeed": 8}}])

    # ---- city -----------------------------------------------------------------------------------------------

    def road(self, z0=0.0, z1=-LENGTH, decor=True):
        """The 3-lane road, pavements, wide grass verges, lamp posts and palms along the sides."""
        mid = (z0 + z1) / 2
        length = abs(z1 - z0)
        self.ground("Road", z0, z1)
        for side in (-1, 1):
            self.shape("Pavement", (side * 4.7, 0.0, mid), (2.6, 0.25, length), PAVEMENT)
            self.shape("Verge", (side * 26.0, -0.03, mid), (40.0, 0.2, length), GRASS)
        z = z0
        while z > z1 + 0.1:
            for x in (-1.1, 1.1):
                self.shape("Lane Line", (x, 0.03, z - 1.25), (0.1, 0.02, 2.5), WHITE)
            z -= 5.0
        if decor and length >= LENGTH - 0.1:
            for z in (-7.0, -22.0):
                self.mesh("Lamp", LIGHT, (4.1, 0.12, z), (0, 3.14159, 0), 3.2)
                self.mesh("Lamp", LIGHT, (-4.1, 0.12, z - 7.5), (0, 0, 0), 3.2)
            self.mesh("Palm", PALM, (7.5, 0.0, -4.0), scale=1.1)
            self.mesh("Palm", PALM_BEND, (-7.2, 0.0, -16.0), (0, 1.2, 0), 1.1)
            self.mesh("Palm", PALM, (-8.0, 0.0, -27.0), scale=0.9)
            self.mesh("Palm", PALM_BEND, (8.4, 0.0, -21.0), (0, 2.6, 0), 1.0)

    def barrier(self, lane, z):
        """Jump it: a construction barrier across the lane, 1.1 m high."""
        self.mesh("Obstacle Barrier", BARRIER, (LANES[lane], 0.0, z), (0, 1.5708, 0), 9.0)

    def cone(self, lane, z, dx=0.0):
        self.mesh("Obstacle Cone", CONE, (LANES[lane] + dx, 0.0, z), scale=8.0)

    def bar(self, lane, z, base=0.0, color=YELLOW):
        """Duck under it: a bar 1.1 m up between two posts."""
        x = LANES[lane]
        self.shape("Obstacle Bar", (x, base + 1.32, z), (2.0, 0.42, 0.3), color)
        for side in (-1, 1):
            self.shape("Post", (x + side * 1.08, base + 0.75, z), (0.12, 1.5, 0.12), WHITE)

    def train(self, lane, z_front, cars=3, model=SUBWAY):
        """Change lane: a subway train standing in the lane, front at z_front, cars behind it."""
        for i in range(cars):
            self.mesh("Obstacle Train", model if i == 0 else SUBWAY_B, (LANES[lane], 0.5, z_front - 1.82 - i * 3.64), scale=1.4)

    def container(self, lane, z):
        """Low freight: jump onto it and run along the top."""
        self.mesh("Obstacle Container", CONTAINER, (LANES[lane], 0.5, z), scale=1.4)

    def crate(self, lane, z, base=0.0):
        self.mesh("Obstacle Crate", CRATE, (LANES[lane], base, z), scale=1.3)

    # ---- beach ----------------------------------------------------------------------------------------------

    def shore(self, z0=0.0, z1=-LENGTH):
        """Water either side and under the pier, sand beyond, boats and buoys out on it."""
        mid = (z0 + z1) / 2
        length = abs(z1 - z0)
        self.node("Sea", "Water", (0, -0.7, mid), water={"size": [44.0, length], "resolution": 24})
        for side in (-1, 1):
            self.shape("Beach", (side * 34.0, -0.4, mid), (24.0, 0.6, length), SAND)
        self.mesh("Boat", SAILBOAT, (-12.0, -0.7, z0 - 9.0), (0, 0.4, 0), 1.3)
        self.mesh("Boat", SPEEDBOAT, (13.0, -0.7, z0 - 21.0), (0, -0.7, 0), 1.2)
        self.mesh("Buoy", BUOY, (7.5, -0.7, z0 - 4.0), scale=0.9)
        self.mesh("Buoy", BUOY, (-7.0, -0.7, z0 - 26.0), scale=0.9)
        self.mesh("Palm", PALM, (25.0, -0.1, z0 - 12.0), scale=1.3)
        self.mesh("Palm", PALM_BEND, (-24.0, -0.1, z0 - 20.0), (0, 1.0, 0), 1.3)

    def pier(self, z0, z1):
        """A stretch of pier across all three lanes, posts down into the water."""
        self.ground("Pier", z0, z1, color=PLANKS, width=7.4, depth=0.3)
        z = z0 - 1.0
        while z > z1:
            for x in (-3.6, 3.6):
                self.shape("Pier Post", (x, -0.9, z), (0.3, 1.8, 0.3), WOOD)
            z -= 6.0

    def barrel(self, lane, z):
        self.mesh("Obstacle Barrel", BARREL, (LANES[lane], 0.0, z), scale=0.95)

    # ---- dungeon --------------------------------------------------------------------------------------------

    def tunnel(self, z0=0.0, z1=-LENGTH, base=0.0, floor=True, torches=True):
        mid = (z0 + z1) / 2
        length = abs(z1 - z0)
        if floor:
            self.ground("Floor", z0, z1, y=base, color=STONE_DARK)
        for side in (-1, 1):
            self.shape("Wall", (side * 4.3, base + 3.0, mid), (1.0, 6.4, length), STONE)
        self.shape("Ceiling", (0, base + 6.0, mid), (9.6, 0.6, length), STONE_DARK)
        if torches and length >= 20:
            for i, z in enumerate((z0 - 6.0, z0 - 21.0)):
                side = -1 if i == 0 else 1
                self.torch(side * 3.65, z, base + 3.2)
                self.mesh("Banner", BANNER, (side * 3.8, base + 1.6, z - 6.0), (0, -side * 1.5708, 0), 2.4)

    def rubble(self, lane, z, base=0.0):
        """Jump it: a low heap of stone."""
        self.shape("Obstacle Rubble", (LANES[lane], base + 0.45, z), (2.0, 0.9, 1.2), STONE)

    def beam(self, lane, z, base=0.0):
        """Duck under it: a wooden beam across the lane."""
        self.shape("Obstacle Beam", (LANES[lane], base + 1.35, z), (2.1, 0.45, 0.45), WOOD)

    def column(self, lane, z, base=0.0):
        """Change lane: a stone column."""
        self.mesh("Obstacle Column", COLUMN, (LANES[lane], base, z), scale=2.6)

    def spikes(self, lane, z, base=0.0):
        """Jump it: a spike trap."""
        self.mesh("Obstacle Spikes", TRAP, (LANES[lane], base, z), scale=2.6)

    # ---- sky ------------------------------------------------------------------------------------------------

    def cloud(self, lane, z0, z1, base=0.0, wide=False):
        """A cloud to run on: a lane wide (or all three), puffs along its sides."""
        width = 7.4 if wide else 2.3
        x = 0.0 if wide else LANES[lane]
        self.ground("Cloud", z0, z1, y=base, color=CLOUD, width=width, x=x, depth=0.8)
        z = z0 - 1.0
        while z > z1 + 0.5:
            for side in (-1, 1):
                self.shape("Puff", (x + side * (width / 2 + 0.1), base - 0.3, z), (1.1, 1.0, 1.4), CLOUD, shape="Icosphere")
            z -= 2.5

    def sky_decor(self, base=0.0):
        for i, (x, y, z, s) in enumerate(((-14, -6, -6, 5.0), (16, -9, -14, 6.0), (-18, -3, -24, 4.0), (12, 4, -28, 3.5))):
            self.shape("Far Cloud", (x, base + y, z), (s * 2.2, s, s * 1.6), CLOUD, shape="Icosphere")

    def launch(self, z, metres):
        for lane in range(3):
            self.shape(f"Launch {metres:g}", (LANES[lane], 0.08, z), (2.0, 0.16, 2.2), [0.3, 0.95, 1.0, 1],
                       material={"emission": [0.3, 0.95, 1.0], "emissionStrength": 3.0})

    def write(self):
        root = {"id": uid(self.name, "root"), "name": self.name, "kind": "Group", "position": [0, 0, 0],
                "rotation": [0, 0, 0], "scale": [1, 1, 1], "children": self.children}
        prefab = {"format": "mana-prefab", "version": 1, "id": uid(self.name, "asset"), "name": self.name,
                  "root": root, "properties": []}
        folder = PREFABS / self.biome
        folder.mkdir(parents=True, exist_ok=True)
        (folder / f"{self.name}.manaprefab").write_text(json.dumps(prefab, indent=2) + "\n", encoding="utf-8")


def city():
    s = Section("City", "Runner Start")
    s.road()
    s.coins(1, -8.0, -28.0)
    yield s

    s = Section("City", "Runner Barriers")
    s.road()
    s.barrier(0, -8.0); s.barrier(2, -8.0)
    s.coins(1, -4.0, -12.0)
    s.barrier(1, -20.0)
    s.jump_arc(1, -20.0)
    s.coins(0, -16.0, -26.0)
    yield s

    s = Section("City", "Runner Trains")
    s.road()
    s.train(0, -4.0, cars=4)
    s.train(2, -12.0, cars=4)
    s.coins(1, -4.0, -28.0)
    s.power("Magnet", 2, -6.0)
    yield s

    s = Section("City", "Runner Duck")
    s.road()
    s.bar(0, -8.0); s.bar(1, -8.0)
    s.coins(1, -6.0, -10.0, y=0.4)
    s.bar(1, -20.0); s.bar(2, -20.0)
    s.coins(2, -17.0, -23.0, y=0.4)
    s.cone(0, -20.0)
    yield s

    s = Section("City", "Runner Crates")
    s.road()
    for z in (-6.0, -9.0, -12.0):
        s.container(1, z)
    s.coins(1, -5.0, -13.0, step=1.5, y=1.85)
    s.crate(0, -18.0); s.crate(2, -22.0)
    s.jump_arc(0, -18.0); s.jump_arc(2, -22.0)
    s.power("Double", 1, -24.0)
    yield s

    s = Section("City", "Runner Mixed")
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

    s = Section("City", "Runner Cones")
    s.road()
    for i, z in enumerate((-5.0, -10.0, -15.0, -20.0, -25.0)):
        s.cone((0, 2, 1, 0, 2)[i], z)
    for i, z in enumerate((-7.5, -12.5, -17.5, -22.5)):
        s.coin((1, 0, 2, 1)[i], z)
    s.power("Jump", 1, -27.0)
    yield s

    s = Section("City", "Runner Train Yard")
    s.road()
    s.train(0, -2.0, cars=6, model=SUBWAY_B)
    s.train(2, -2.0, cars=6)
    s.barrier(1, -12.0)
    s.jump_arc(1, -12.0)
    s.bar(1, -22.0)
    s.coins(1, -20.0, -24.0, y=0.4)
    yield s

    s = Section("City", "Runner Orcs")
    s.road()
    s.enemy("Charge", ORC, 1, -16.0, 2.0)
    s.enemy("Charge", ORC, 0, -27.0, 2.0)
    s.coins(2, -6.0, -26.0)
    s.barrier(2, -20.0)
    s.jump_arc(2, -20.0)
    yield s


def beach():
    s = Section("Beach", "Beach Enter")
    s.road(0.0, -10.0, decor=False)
    s.ground("Sand", -10.0, -LENGTH, color=SAND, width=12.0)
    s.shore(-10.0, -LENGTH)
    s.coins(1, -12.0, -28.0)
    s.exit(0.0)
    yield s

    s = Section("Beach", "Beach Pier")
    s.shore()
    s.pier(0.0, -10.0)
    s.pier(-14.0, -LENGTH)
    for lane in range(3):
        s.jump_arc(lane, -12.0, height=1.4)
    s.barrel(0, -22.0); s.barrel(2, -22.0)
    s.coins(1, -18.0, -28.0)
    yield s

    s = Section("Beach", "Beach Crabs")
    s.shore()
    s.pier(0.0, -LENGTH)
    s.enemy("Patrol", CRAB, 1, -10.0, 0.6)
    s.enemy("Patrol", CRAB, 1, -22.0, 0.6)
    s.coins(0, -4.0, -28.0, step=3.0)
    s.power("Shield", 2, -16.0)
    yield s

    s = Section("Beach", "Beach Shallows")
    s.ground("Sand", 0.0, -LENGTH, y=-0.3, color=SAND, width=12.0)
    s.node("Shallows", "Water", (0, -0.15, -15.0), water={"size": [44.0, LENGTH], "resolution": 24})
    for side in (-1, 1):
        s.shape("Beach", (side * 34.0, -0.4, -15.0), (24.0, 0.6, LENGTH), SAND)
    s.mesh("Rocks", ROCKS, (-2.2, -0.3, -9.0), scale=1.6)
    s.mesh("Rocks", ROCKS, (6.5, -0.3, -18.0), scale=2.0)
    s.barrel(2, -9.0)
    s.crate(0, -20.0, base=-0.3)
    s.coins(1, -4.0, -28.0, y=0.1)
    s.power("Magnet", 1, -24.0, y=0.55)
    s.exit(0.0)
    yield s

    s = Section("Beach", "Beach Gaps")
    s.shore()
    s.pier(0.0, -6.0)
    s.pier(-10.0, -16.0)
    s.pier(-20.0, -LENGTH)
    for lane in range(3):
        s.jump_arc(lane, -8.0, height=1.3)
    s.jump_arc(1, -18.0, height=1.3)
    s.barrel(0, -25.0)
    yield s

    s = Section("Beach", "Beach Leave")
    s.ground("Sand", 0.0, -20.0, color=SAND, width=12.0)
    s.shore(0.0, -20.0)
    s.road(-20.0, -LENGTH, decor=False)
    s.coins(1, -4.0, -26.0)
    s.exit(0.0)
    yield s


def dungeon():
    d = -DUNGEON_DEPTH
    s = Section("Dungeon", "Dungeon Enter")
    s.road(0.0, -4.0, decor=False)
    s.ramp(False, -4.0, -24.0, 0.0, d, color=STONE_DARK)
    s.ground("Floor", -24.0, -LENGTH, y=d, color=STONE_DARK)
    s.tunnel(-10.0, -LENGTH, base=d, floor=False, torches=False)
    s.torch(3.65, -26.0, d + 3.2)
    for side in (-1, 1):
        s.shape("Wall", (side * 4.3, d / 2 + 1.0, -16.0), (1.0, abs(d) + 2.0, 24.0), STONE)
    s.coins(1, -6.0, -28.0, step=2.0, y=0.55)
    s.exit(d)
    yield s

    s = Section("Dungeon", "Dungeon Hall")
    s.tunnel()
    s.rubble(0, -8.0); s.rubble(2, -8.0)
    s.jump_arc(1, -8.0)
    s.beam(1, -18.0); s.beam(2, -18.0)
    s.coins(2, -16.0, -20.0, y=0.4)
    s.column(0, -24.0)
    s.coins(1, -22.0, -28.0)
    yield s

    s = Section("Dungeon", "Dungeon Orcs")
    s.tunnel()
    s.enemy("Charge", ORC, 0, -14.0, 2.0)
    s.enemy("Charge", ORC, 2, -20.0, 2.0)
    s.column(1, -26.0)
    s.coins(1, -4.0, -22.0)
    s.power("Shield", 1, -8.0)
    yield s

    s = Section("Dungeon", "Dungeon Spikes")
    s.tunnel()
    for z in (-6.0, -14.0, -22.0):
        for lane in range(3):
            s.spikes(lane, z)
    for z in (-6.0, -14.0, -22.0):
        s.jump_arc(1, z, height=1.2)
    s.power("Double", 0, -27.0)
    yield s

    s = Section("Dungeon", "Dungeon Columns")
    s.tunnel()
    s.column(0, -6.0); s.column(1, -6.0)
    s.column(1, -15.0); s.column(2, -15.0)
    s.column(0, -24.0); s.column(2, -24.0)
    s.coins(2, -2.0, -10.0); s.coins(0, -11.0, -19.0); s.coins(1, -20.0, -28.0)
    s.beam(1, -28.5)
    yield s

    s = Section("Dungeon", "Dungeon Leave")
    s.ground("Floor", 0.0, -6.0, color=STONE_DARK)
    s.tunnel(0.0, -14.0, floor=False, torches=False)
    s.torch(-3.65, -4.0, 3.2)
    s.ramp(True, -6.0, -26.0, 0.0, DUNGEON_DEPTH, color=STONE_DARK)
    for side in (-1, 1):
        s.shape("Wall", (side * 4.3, DUNGEON_DEPTH / 2 - 1.0, -16.0), (1.0, DUNGEON_DEPTH + 2.0, 20.0), STONE)
    s.road(-26.0, -LENGTH, decor=False)
    for node in s.children:
        # The road at the top of the ramp, up where the city is.
        if node["name"] in ("Ground Road", "Pavement", "Verge", "Lane Line") and node["position"][2] < -25.9:
            node["position"][1] += DUNGEON_DEPTH
    s.coins(1, -2.0, -28.0, step=2.0)
    s.exit(DUNGEON_DEPTH)
    yield s


def sky():
    h = SKY_HEIGHT
    s = Section("Sky", "Sky Enter")
    s.road(0.0, -12.0, decor=False)
    s.launch(-9.0, h + 2.0)
    for lane in range(3):
        for i, y in enumerate((4.0, 10.0, 18.0, 26.0)):
            s.coin(lane, -12.0 - i * 2.5, y)
    s.cloud(0, -18.0, -LENGTH, base=h, wide=True)
    s.sky_decor(h)
    s.exit(h)
    yield s

    s = Section("Sky", "Sky Clouds")
    s.sky_decor()
    s.cloud(0, 0.0, -LENGTH, wide=True)
    s.enemy("Fly", BEE, 1, -14.0, 0.55, y=0.4)
    s.coins(0, -4.0, -28.0); s.coins(2, -4.0, -28.0)
    s.power("Jump", 1, -24.0)
    yield s

    s = Section("Sky", "Sky Hops")
    s.sky_decor()
    s.cloud(0, 0.0, -9.0, wide=True)
    s.cloud(0, -13.0, -20.0)
    s.cloud(1, -13.0, -20.0)
    s.cloud(2, -13.0, -20.0)
    s.cloud(0, -24.0, -LENGTH, wide=True)
    for lane in range(3):
        s.jump_arc(lane, -11.0, height=1.4)
    s.jump_arc(1, -22.0, height=1.4)
    yield s

    s = Section("Sky", "Sky Bees")
    s.sky_decor()
    s.cloud(0, 0.0, -LENGTH, wide=True)
    s.enemy("Fly", BEE, 0, -10.0, 0.55, y=0.4)
    s.enemy("Fly", BEE, 2, -16.0, 0.55, y=0.4)
    s.enemy("Fly", BEE, 1, -24.0, 0.55, y=0.4)
    s.coins(1, -2.0, -12.0); s.coins(0, -18.0, -28.0)
    s.power("Magnet", 2, -26.0)
    yield s

    s = Section("Sky", "Sky Islands")
    s.sky_decor()
    s.cloud(1, 0.0, -12.0)
    s.cloud(0, -6.0, -18.0)
    s.cloud(2, -6.0, -18.0)
    s.cloud(1, -16.0, -LENGTH)
    s.cloud(0, -22.0, -LENGTH)
    s.coins(1, -2.0, -10.0); s.coins(0, -8.0, -16.0); s.coins(2, -8.0, -16.0); s.coins(1, -18.0, -28.0)
    s.power("Double", 0, -26.0)
    yield s

    s = Section("Sky", "Sky Leave")
    s.sky_decor()
    s.cloud(0, 0.0, -8.0, wide=True)
    for lane in range(3):
        for i, y in enumerate((-4.0, -12.0, -20.0)):
            s.coin(lane, -10.0 - i * 3.0, y)
    s.road(-8.0, -LENGTH, decor=False)
    for node in s.children:
        # The city, 30 m below the clouds: dropped onto at the end of the sky.
        if node["name"] in ("Ground Road", "Pavement", "Verge", "Lane Line"):
            node["position"][1] -= h
    s.exit(-h)
    yield s


def level():
    start = [48.0, 0.0, 400.0]
    return {
        "format": "mana-level",
        "version": 2,
        "name": "Endless Run",
        "web": {"publish": True, "description": "Run the three lanes for as long as you can: swipe to change lane, jump and duck, "
                "stomp enemies, grab power-ups -- through the city, along the shore, down into dungeons and up into the clouds.",
                "order": 0},
        "camera": {"position": [48.0, 6.0, 410.0], "rotation": [-0.3, 0.0, 0.0]},
        "sky": {"mode": "Day"},
        "gameModes": [{"id": uid("level", "mode"), "type": "Runner", "properties": {}}],
        # The Player Start is a node like any other: a top-level "playerStart" is only an echo the loader ignores, and
        # without the node the character spawned at the origin as whichever came first in the catalog.
        "nodes": [{"id": uid("level", "start"), "name": "Player Start", "kind": "PlayerStart", "position": start,
                   "rotation": [0, 0, 0], "scale": [1, 1, 1],
                   "character": {"pack": "kenney_mini-characters", "name": "character-female-b", "height": 1.1}}],
    }


if __name__ == "__main__":
    # Laid out afresh: sections dropped from here go from the folder too.
    if PREFABS.exists():
        shutil.rmtree(PREFABS)
    for biome in (city, beach, dungeon, sky):
        for section in biome():
            section.write()
            print("wrote", section.biome, "/", section.name)
    LEVEL.write_text(json.dumps(level(), indent=2) + "\n", encoding="utf-8")
    print("wrote", LEVEL.name)
