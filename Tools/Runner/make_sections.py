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
  Exit                its height is where the next section begins (a dungeon's floor is 5 m down, the sky 30 m up)
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
DUNGEON_DEPTH = 5.0
# The modular dungeon kit at 1.25x: 10 m corridor tiles. Its corridors run along X; a quarter turn runs them down the track.
TILE = 1.25
QUARTER = math.pi / 2
# Its wide stairs rise one 4.05 m storey toward their +Z end: scaled to the dungeon's depth, their top step ends 1.2 m
# (model) toward +Z of their origin and they meet the floor 3.9 m toward -Z.
STAIR_SCALE = DUNGEON_DEPTH / 4.05
STAIR_TOP_Z = 1.2
STAIR_RUN = (1.2 + 3.9) * STAIR_SCALE
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
CHEST = zip_model("kenney_mini-dungeon", "chest")
POT = zip_model("kenney_mini-dungeon", "pot")
POTION = zip_model("kenney_mini-dungeon", "potion")
ROCKS_DUNGEON = zip_model("kenney_mini-dungeon", "rocks")
STATUE = zip_model("kenney_mini-arena", "statue")
ZOMBIE = zip_model("kenney_blocky-characters_20", "character-l")
ZOMBIE_B = zip_model("kenney_blocky-characters_20", "character-o")
CART = zip_model("kenney_train-kit", "train-carriage-coal")
RAIL = zip_model("kenney_train-kit", "railroad-rail-straight")
PICKAXE = zip_model("kenney_survival-kit", "tool-pickaxe")
SHOVEL = zip_model("kenney_survival-kit", "tool-shovel")
BUCKET = zip_model("kenney_survival-kit", "bucket")
ORE = zip_model("kenney_survival-kit", "resource-stone-large")
BOX_LARGE = zip_model("kenney_survival-kit", "box-large")
SCAFFOLD = zip_model("kenney_survival-kit", "structure")
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
STONE = [0.5, 0.47, 0.5, 1]
STONE_DARK = [0.44, 0.41, 0.39, 1]
GLOW = [1.0, 0.6, 0.18, 1]
WOOD = [0.45, 0.3, 0.18, 1]
CLOUD = [0.97, 0.98, 1.0, 1]
TORCH = [1.0, 0.62, 0.25, 1]
CRYPT_STONE = [0.5, 0.5, 0.66, 1]
CRYPT_LIGHT = [0.62, 0.62, 0.78, 1]
CEILING = [0.1, 0.09, 0.13, 1]
BONE = [0.93, 0.9, 0.82, 1]
IRON = [0.36, 0.36, 0.42, 1]
IRON_MATERIAL = {"metallic": 0.5, "roughness": 0.5}
VOID = [0.0, 0.0, 0.0, 1]
GLOW_RED = [1.0, 0.25, 0.12, 1]
POWER_COLORS = {"Magnet": [0.95, 0.3, 0.45, 1], "Shield": [0.3, 0.6, 1.0, 1], "Double": [1.0, 0.8, 0.2, 1], "Jump": [0.3, 0.9, 0.5, 1]}


def kit(name):
    return zip_model("kenney_modular-dungeon-kit_1.0", name)


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

    def arc_coin(self, lane, z, y):
        """A coin the runner moves onto its own flight path (RunnerMode.PlaceArcCoins); here, roughly where it goes."""
        self.node("Coin Arc", "Group", (LANES[lane], y, z), scripts=[
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
                       "color": [1, 0.62, 0.3, 1], "intensity": 4.5, "range": 14, "lightType": "Point",
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
                # The model's arm points along its -Z: a quarter turn hangs it out over the lanes.
                self.mesh("Lamp", LIGHT, (4.1, 0.12, z), (0, 1.5708, 0), 3.2)
                self.mesh("Lamp", LIGHT, (-4.1, 0.12, z - 7.5), (0, -1.5708, 0), 3.2)
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
    # The crypt is Kenney's modular dungeon kit at 1.25x: 10 m corridor tiles, three to a section, turned a quarter to
    # run down the track (the kit's run along X). Junction tiles open into side passages that lead off into the dark.

    def part(self, name, center, size, color, shape="Cube", material=None, rotation=(0, 0, 0)):
        """A shape inside a group (a skeleton's bones, a portcullis's bars), placed relative to the group."""
        node = {"id": self._id(name), "name": name, "kind": "Shape", "position": [round(v, 4) for v in center],
                "rotation": [round(v, 5) for v in rotation], "scale": [1, 1, 1], "shape": shape, "color": color,
                "size": [round(v, 4) for v in size]}
        if material:
            node["material"] = material
        return node

    def group(self, name, position, children, rotation=(0, 0, 0)):
        return self.node(name, "Group", position, rotation, children=children)

    def crypt(self, tiles=("straight",) * 3, base=0.0, z0=0.0, mine=False, gaps=()):
        """
        Corridor tiles from z0 (a junction opens left or right, a cross both ways, each into a side passage), the floor
        under them except over gaps (crypt pits, (near, far) pairs), a dark ceiling over all, torches on the walls.
        """
        z1 = z0 - 10.0 * len(tiles)
        for i, kind in enumerate(tiles):
            zc = z0 - 5.0 - 10.0 * i
            model, yaw = {"straight": ("corridor-wide", QUARTER), "left": ("corridor-wide-junction", QUARTER),
                          "right": ("corridor-wide-junction", -QUARTER), "cross": ("corridor-wide-intersection", QUARTER)}[kind]
            self.mesh("Crypt", kit(model), (0, base, zc), (0, yaw, 0), TILE)
            for side in {"left": (-1,), "right": (1,), "cross": (-1, 1)}.get(kind, ()):
                self.passage(side, zc, base, mine)
            for side in {"straight": (-1, 1), "left": (1,), "right": (-1,), "cross": ()}[kind]:
                self.torch(side * 3.45, zc + 2.5 * side, base + 3.0)
            if mine:
                for dz in (2.5, -2.5):
                    self.timber(zc + dz, base)
        # The floor is a slab just under the kit's own (whose stones stand a little proud), broken where there is a pit.
        z = z0
        for near, far in sorted(gaps, reverse=True) + [(z1, z1)]:
            if z > near:
                self.ground("Floor", z, near, y=base - 0.05, color=STONE_DARK)
            if near != far:
                self.pit(near, far, base)
            z = far
        self.shape("Ceiling", (0, base + 6.0, (z0 + z1) / 2), (64.0, 0.6, abs(z1 - z0)), CEILING)

    def pit(self, near, far, base):
        """A crypt pit across the lanes: black, its lips lit -- the kit's floor runs on under it, so the hole is drawn."""
        mid, length = (near + far) / 2, abs(near - far)
        self.shape("Pit", (0, base + 0.14, mid), (7.0, 0.02, length), VOID, material={"unlit": True, "roughness": 1.0})
        for z in (near, far):
            self.shape("Pit Lip", (0, base + 0.16, z), (7.0, 0.05, 0.12), GLOW_RED,
                       material={"emission": GLOW_RED[:3], "emissionStrength": 2.5, "unlit": True})

    def passage(self, side, zc, base, mine):
        """A side passage off a junction: a corridor leading away, closed and lit at its far end, a mine's or a crypt's things in it."""
        self.mesh("Passage", kit("corridor-wide"), (side * 10.0, base, zc), scale=TILE)
        self.mesh("Passage End", kit("corridor-wide-end"), (side * 20.0, base, zc), (0, 0 if side < 0 else math.pi, 0), TILE)
        self.torch(side * 23.4, zc, base + 3.0)
        if mine:
            # Rails off into the dark, a cart left on them, the tools of whoever was digging.
            for i in range(5):
                x = side * (4.5 + 4.0 * i) - (4.0 if side < 0 else 0.0)
                self.mesh("Rail", RAIL, (x, base, zc), (0, QUARTER, 0), 1.0)
            self.mesh("Cart", CART, (side * 15.0, base, zc), (0, QUARTER, 0), 0.8)
            self.mesh("Scaffold", SCAFFOLD, (side * 9.0, base, zc), scale=9.4)
            self.mesh("Ore", ORE, (side * 19.5, base, zc + 2.6), (0, 0.6, 0), 4.5)
            self.mesh("Pickaxe", PICKAXE, (side * 20.5, base, zc - 2.8), (0, side * 0.5, 0.35), 4.0)
            self.mesh("Shovel", SHOVEL, (side * 18.0, base, zc - 3.0), (0, 0, -0.3), 4.0)
            self.mesh("Bucket", BUCKET, (side * 12.0, base, zc + 3.0), scale=4.0)
            self.mesh("Crates", BOX_LARGE, (side * 22.0, base, zc - 1.5), (0, 0.3, 0), 4.0)
        else:
            # A crypt's: a statue at the end of the passage, coffins along it, pots and a chest.
            self.mesh("Statue", STATUE, (side * 22.4, base, zc), (0, -side * QUARTER, 0), 2.6)
            for x in (side * 10.0, side * 15.5):
                self.coffin_decor(x, base, zc + 3.0)
            self.mesh("Chest", CHEST, (side * 19.0, base, zc - 2.8), (0, side * 0.4, 0), 2.4)
            self.mesh("Pot", POT, (side * 12.5, base, zc - 3.1), scale=2.2)
            self.mesh("Banner", BANNER, (side * 13.0, base + 1.6, zc + 3.7), (0, math.pi, 0), 2.6)

    def coffin_decor(self, x, base, z):
        """A stone coffin along a passage wall (scenery: not in a lane)."""
        self.shape("Coffin", (x, base + 0.3, z), (1.2, 0.6, 1.8), CRYPT_STONE)
        self.shape("Coffin Lid", (x, base + 0.68, z), (1.3, 0.16, 1.9), CRYPT_LIGHT)

    def timber(self, z, base):
        """A mine's timber frame across the corridor: posts against the walls, a beam up under the ceiling."""
        for side in (-1, 1):
            self.shape("Timber Post", (side * 3.2, base + 2.6, z), (0.32, 5.2, 0.32), WOOD)
            self.shape("Timber Brace", (side * 2.75, base + 4.55, z), (0.22, 1.2, 0.22), WOOD, rotation=(0, 0, side * 0.75))
        self.shape("Timber Beam", (0, base + 5.15, z), (6.8, 0.36, 0.36), WOOD)

    def rails(self, lane, z0, z1, base=0.0):
        """Mine rails down a lane, from z0 to z1 (4 m pieces)."""
        z = z1
        while z < z0 - 0.1:
            self.mesh("Rail", RAIL, (LANES[lane], base, z), scale=1.0)
            z += 4.0

    def cart(self, lane, z, base=0.0):
        """Jump onto it: a mine cart full of coal on the rails, 1.3 m high -- or change lane."""
        self.mesh("Obstacle Cart", CART, (LANES[lane], base, z), scale=0.8)

    def portcullis(self, lane, z, base=0.0):
        """
        Duck under it: a spiked iron gate let down on chains, its spikes stopping 1.15 m up -- over a ducking runner's head,
        under a standing one's -- and lit along its bottom. It stops at 2.8 m, well under the camera that follows the runner
        through it, and too high to jump without a Super Jump.
        """
        bottom, top = 1.15, 2.8
        parts = []
        for i in range(7):
            x = -0.9 + 0.3 * i
            parts.append(self.part("Bar", (x, (bottom + 0.2 + top) / 2, 0), (0.09, top - bottom - 0.2, 0.09), IRON, material=IRON_MATERIAL))
            parts.append(self.part("Spike", (x, bottom + 0.12, 0), (0.17, 0.24, 0.17), IRON, shape="Pyramid", material=IRON_MATERIAL,
                                   rotation=(math.pi, 0, 0)))
        for y in (bottom + 0.3, top - 0.08):
            parts.append(self.part("Band", (0, y, 0), (2.1, 0.16, 0.5), IRON, material=IRON_MATERIAL))
        for dz in (0.27, -0.27):
            parts.append(self.part("Glow Edge", (0, bottom + 0.3, dz), (2.12, 0.12, 0.04), GLOW_RED,
                                   material={"emission": GLOW_RED[:3], "emissionStrength": 6.0, "unlit": True}))
        self.group("Obstacle Portcullis", (LANES[lane], base, z), parts)
        for x in (-0.75, 0.75):
            self.shape("Chain", (LANES[lane] + x, base + (top + 6.0) / 2, z), (0.06, 6.0 - top, 0.06), IRON, material=IRON_MATERIAL)

    def tomb(self, lane, z, base=0.0):
        """Jump it (or onto it): a stone sarcophagus across the lane, a bone-white cross on its lid, its front edge lit."""
        parts = [self.part("Tomb", (0, 0.3, 0), (1.8, 0.6, 2.4), CRYPT_STONE),
                 self.part("Lid", (0, 0.71, 0), (1.95, 0.22, 2.55), CRYPT_LIGHT),
                 self.part("Cross", (0, 0.85, 0.05), (0.16, 0.06, 1.4), BONE),
                 self.part("Cross", (0, 0.85, 0.4), (0.85, 0.06, 0.16), BONE),
                 self.part("Glow Edge", (0, 0.83, 1.29), (1.95, 0.06, 0.06), GLOW,
                           material={"emission": GLOW[:3], "emissionStrength": 4.0, "unlit": True})]
        self.group("Obstacle Tomb", (LANES[lane], base, z), parts)

    def skeleton(self, kind, lane, z, base=0.0):
        """A skeleton, arms out, eyes lit red, walking at you (Charge) or across (Patrol): stomp it or step round."""
        bone = {"roughness": 0.8, "emission": BONE[:3], "emissionStrength": 0.12}
        eye = {"emission": [1.0, 0.15, 0.1], "emissionStrength": 5.0, "unlit": True}
        parts = [self.part("Skull", (0, 1.22, 0), (0.42, 0.4, 0.4), BONE, material=bone),
                 self.part("Jaw", (0, 0.99, 0.03), (0.32, 0.08, 0.3), BONE, material=bone),
                 self.part("Eye", (-0.09, 1.25, 0.2), (0.1, 0.08, 0.03), [1, 0.15, 0.1, 1], material=eye),
                 self.part("Eye", (0.09, 1.25, 0.2), (0.1, 0.08, 0.03), [1, 0.15, 0.1, 1], material=eye),
                 self.part("Nose", (0, 1.15, 0.2), (0.05, 0.07, 0.02), VOID),
                 self.part("Spine", (0, 0.7, -0.05), (0.07, 0.55, 0.07), BONE, material=bone),
                 self.part("Pelvis", (0, 0.45, 0), (0.34, 0.1, 0.16), BONE, material=bone)]
        for k, y in enumerate((0.88, 0.78, 0.68)):
            parts.append(self.part("Rib", (0, y, 0.02), (0.4 - 0.05 * k, 0.05, 0.22), BONE, material=bone))
        for side in (-1, 1):
            parts.append(self.part("Leg", (side * 0.1, 0.22, 0), (0.08, 0.45, 0.08), BONE, material=bone))
            parts.append(self.part("Foot", (side * 0.1, 0.025, 0.05), (0.12, 0.05, 0.2), BONE, material=bone))
            parts.append(self.part("Arm", (side * 0.25, 0.86, 0.18), (0.07, 0.07, 0.44), BONE, material=bone))
            parts.append(self.part("Hand", (side * 0.25, 0.84, 0.42), (0.1, 0.1, 0.08), BONE, material=bone))
        self.group(f"Enemy {kind}", (LANES[lane], base, z), parts)

    def skull(self, lane, z, base=0.0):
        """A floating skull, eyes lit green, bobbing a lane's height off the floor: stomp it or step round."""
        eye = {"emission": [0.3, 1.0, 0.45], "emissionStrength": 5.0, "unlit": True}
        bone = {"roughness": 0.7, "emission": BONE[:3], "emissionStrength": 0.25}
        parts = [self.part("Skull", (0, 0.3, 0), (0.62, 0.56, 0.58), BONE, material=bone),
                 self.part("Jaw", (0, -0.02, 0.05), (0.46, 0.12, 0.44), BONE, material=bone),
                 self.part("Eye", (-0.14, 0.34, 0.29), (0.15, 0.12, 0.03), [0.3, 1, 0.45, 1], material=eye),
                 self.part("Eye", (0.14, 0.34, 0.29), (0.15, 0.12, 0.03), [0.3, 1, 0.45, 1], material=eye),
                 self.part("Nose", (0, 0.2, 0.29), (0.07, 0.1, 0.02), VOID)]
        self.group("Enemy Fly", (LANES[lane], base + 0.55, z), parts)

    def zombie(self, kind, lane, z, model, base=0.0):
        """A zombie (a blocky character with a green face): sprints at you (Charge) or shambles across (Patrol)."""
        self.mesh(f"Enemy {kind}", model, (LANES[lane], base, z), scale=0.55)

    def potion(self, kind, lane, z, base=0.0):
        """A power-up in the dungeon: a potion bottle glowing its power's colour."""
        color = POWER_COLORS[kind]
        self.node(f"PowerUp {kind}", "Mesh", (LANES[lane], base + 0.45, z), scale=(2.4, 2.4, 2.4), asset=POTION,
                  scripts=[{"id": self._id("potion-glow"), "type": "Glow", "properties": {
                      "color": color, "strength": 1.8, "lightRange": 4.0, "lightIntensity": 1.5, "lightOffset": [0, 0.4, 0],
                      "pulseSpeed": 1.2, "enabled": True}}])

    def stairs(self, z_top, top_y, down=True):
        """
        The kit's stairs, one storey (DUNGEON_DEPTH): down along -Z from z_top at top_y, or up along -Z to top_y at
        z_top. The runner goes on a hidden ramp along the steps. Returns where the steps meet the lower floor.
        """
        foot = z_top - STAIR_RUN if down else z_top + STAIR_RUN
        # The model rises toward its +Z end: as it is going down along -Z, turned round going up.
        origin = z_top - STAIR_TOP_Z * STAIR_SCALE if down else z_top + STAIR_TOP_Z * STAIR_SCALE
        self.mesh("Stairs", kit("stairs-wide"), (0, top_y - DUNGEON_DEPTH, origin), (0, 0 if down else math.pi, 0), STAIR_SCALE)
        near, far = (z_top, foot) if down else (foot, z_top)
        y_near, y_far = (top_y, top_y - DUNGEON_DEPTH) if down else (top_y - DUNGEON_DEPTH, top_y)
        run = abs(far - near)
        angle = math.atan2(y_far - y_near, run)
        self.shape("Ramp Down" if down else "Ramp Up", (0, (y_near + y_far) / 2 - 0.18, (near + far) / 2),
                   (7.6, 0.36, math.hypot(run, y_far - y_near) + 1.2), STONE_DARK, rotation=(angle, 0, 0),
                   material={"opacity": 0.0, "opacityMode": "Blend"})
        return foot

    def column(self, lane, z, base=0.0):
        """Change lane: a stone column."""
        self.mesh("Obstacle Column", COLUMN, (LANES[lane], base, z), scale=2.6)

    def spikes(self, lane, z, base=0.0):
        """Jump it: a spike trap."""
        self.mesh("Obstacle Spikes", TRAP, (LANES[lane], base, z), scale=2.6)

    def rocks(self, lane, z, base=0.0):
        """Jump it: a heap of fallen rock across the lane."""
        self.mesh("Obstacle Rocks", ROCKS_DUNGEON, (LANES[lane], base, z), (0, 0.5 * lane, 0), 1.9)

    def gate(self, z, floor, top=8.0, faces=1):
        """
        The way into (or out of) the hill: a stone face across the track at z with an opening the track runs through,
        from below its floor up to the ground above. faces=1 looks back toward the city (+Z), -1 ahead.
        """
        height = top - floor + 1.0
        centre = (top + floor - 1.0) / 2
        for side in (-1, 1):
            self.shape("Gate Wall", (side * 17.4, centre, z), (27.2, height, 2.0), STONE)
        self.shape("Gate Lintel", (0, (floor + 5.6 + top) / 2, z), (7.8, top - floor - 5.6, 2.0), STONE)
        self.shape("Gate Arch", (0, floor + 5.75, z + faces * 1.05), (8.2, 0.5, 0.25), STONE_DARK)
        for side in (-1, 1):
            # On the face beside the opening.
            self.torch(side * 5.4, z + faces * 1.2, floor + 4.2)
            self.mesh("Banner", BANNER, (side * 7.8, floor + 3.0, z + faces * 1.1), (0, 0 if faces > 0 else 3.14159, 0), 3.0)
        # A grassy mound over the way down, so the face reads as a hillside rather than a wall.
        self.shape("Hill", (0, top + 0.6, z - faces * 9.0), (56.0, 1.2, 18.0), GRASS)
        for side in (-1, 1):
            self.shape("Hill Shoulder", (side * 16.0, top + 1.6, z - faces * 9.0), (14.0, 1.4, 14.0), GRASS, shape="Icosphere")
            self.mesh("Palm", PALM, (side * 12.0, top + 1.2, z - faces * 6.0), scale=1.1)

    def ramp_coins(self, lane, z0, z1, y0, y1, step=2.0):
        """Coins down (or up) a slope, the same height above it all the way."""
        z = z0
        while z >= z1:
            t = (z0 - z) / (z0 - z1)
            self.coin(lane, z, y0 + t * (y1 - y0) + 0.55)
            z -= step

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
    # The road runs up to a doorway in a hillside; inside, the kit's stairs go down one storey into the crypt.
    s.road(0.0, -8.5, decor=False)
    for side in (-1, 1):
        s.mesh("Palm", PALM, (side * 9.0, 0.0, -4.0), scale=1.0)
    s.gate(-9.5, 0.0, top=7.0, faces=1)
    s.ground("Threshold", -8.5, -11.0, y=-0.05, color=STONE)
    foot = s.stairs(-11.0, 0.0, down=True)
    s.shape("Ceiling", (0, 5.8, (-9.5 + foot) / 2), (12.0, 0.6, abs(foot + 9.5)), CEILING)
    s.ramp_coins(1, -11.5, foot + 0.5, 0.0, d)
    s.ground("Landing", foot, -20.0, y=d - 0.05, color=STONE_DARK)
    s.shape("Header", (0, (d + 5.3 + 6.1) / 2, -20.0), (12.0, 6.1 - (d + 5.3), 0.6), CRYPT_STONE)
    s.torch(-4.0, -19.0, d + 3.0)
    s.torch(4.0, -19.0, d + 3.0)
    s.crypt(("straight",), base=d, z0=-20.0)
    s.coins(1, -21.0, -29.0, y=d + 0.55)
    s.exit(d)
    yield s

    s = Section("Dungeon", "Crypt Tombs")
    s.crypt(("straight", "left", "straight"))
    s.tomb(0, -6.0); s.tomb(2, -6.0)
    s.coins(1, -2.0, -10.0)
    s.tomb(1, -14.0)
    s.jump_arc(1, -14.0, height=1.3)
    s.skeleton("Charge", 0, -22.0)
    s.potion("Shield", 2, -17.0)
    for lane in range(3):
        s.tomb(lane, -26.5)
    s.jump_arc(1, -26.5, height=1.3)
    yield s

    s = Section("Dungeon", "Crypt Gates")
    s.crypt(("straight", "right", "straight"))
    s.portcullis(0, -6.0); s.portcullis(1, -6.0)
    s.coins(2, -2.0, -10.0)
    s.tomb(2, -14.0)
    s.portcullis(1, -14.0)
    s.coins(0, -11.0, -17.0)
    for lane in range(3):
        s.portcullis(lane, -22.0)
    s.coins(1, -20.5, -23.5, step=1.5, y=0.35)
    s.skeleton("Patrol", 1, -27.5)
    yield s

    s = Section("Dungeon", "Crypt Pits")
    pits = ((-6.0, -9.0), (-16.5, -19.5), (-25.5, -28.5))
    s.crypt(("straight", "cross", "straight"), gaps=pits)
    for near, far in pits:
        s.jump_arc(1, (near + far) / 2, height=1.4)
    s.skull(0, -13.0)
    s.coins(2, -10.5, -15.0)
    s.potion("Jump", 2, -22.5)
    yield s

    s = Section("Dungeon", "Mine Carts")
    s.crypt(("right", "straight", "left"), mine=True)
    for lane in range(3):
        s.rails(lane, 0.0, -30.0)
    for z in (-8.0, -10.2, -12.4):
        s.cart(0, z)
    s.coins(0, -7.0, -13.0, step=1.5, y=1.32 + 0.55)
    s.zombie("Charge", 1, -16.0, ZOMBIE)
    for z in (-19.0, -21.2):
        s.cart(2, z)
    s.cart(1, -27.0)
    s.coins(2, -2.0, -14.0)
    yield s

    s = Section("Dungeon", "Mine Junction")
    s.crypt(("cross", "straight", "cross"), mine=True)
    s.rails(1, 0.0, -30.0)
    s.rocks(0, -7.0); s.rocks(2, -7.0)
    s.jump_arc(1, -7.0, height=1.3)
    s.zombie("Patrol", 1, -15.0, ZOMBIE_B)
    s.cart(1, -23.0); s.cart(1, -25.2)
    s.rocks(0, -24.0)
    s.coins(2, -18.0, -28.0)
    s.potion("Magnet", 0, -16.0)
    yield s

    s = Section("Dungeon", "Crypt Undead")
    s.crypt(("straight", "straight", "right"))
    s.skull(1, -7.0)
    s.skeleton("Charge", 0, -12.0)
    s.skeleton("Charge", 2, -17.0)
    s.zombie("Charge", 1, -23.0, ZOMBIE)
    s.enemy("Charge", ORC, 0, -28.0, 2.0)
    s.coins(1, -2.0, -6.0); s.coins(2, -9.0, -14.0); s.coins(0, -18.0, -24.0)
    s.potion("Double", 2, -26.0)
    yield s

    s = Section("Dungeon", "Crypt Spikes")
    s.crypt(("straight", "left", "straight"))
    for z in (-6.0, -14.0):
        for lane in range(3):
            s.spikes(lane, z)
        s.jump_arc(1, z, height=1.2)
    s.portcullis(0, -22.0); s.portcullis(2, -22.0)
    s.coins(1, -19.0, -25.0)
    s.column(1, -28.0)
    yield s

    s = Section("Dungeon", "Dungeon Leave")
    # Out up the kit's stairs, through a doorway in the hillside, onto the road one storey up.
    s.crypt(("straight",), z0=0.0)
    s.coins(1, -1.0, -9.0)
    top = -10.0 - STAIR_RUN
    s.stairs(top, DUNGEON_DEPTH, down=False)
    s.shape("Header", (0, (5.3 + DUNGEON_DEPTH + 6.1) / 2, -10.0), (12.0, DUNGEON_DEPTH + 6.1 - 5.3, 0.6), CRYPT_STONE)
    s.shape("Ceiling", (0, DUNGEON_DEPTH + 5.8, (-10.0 + top - 2.0) / 2), (12.0, 0.6, abs(top - 12.0)), CEILING)
    s.ramp_coins(1, -10.5, top + 0.5, 0.0, DUNGEON_DEPTH)
    s.ground("Threshold", top, -24.0, y=DUNGEON_DEPTH - 0.05, color=STONE)
    s.gate(top - 2.0, DUNGEON_DEPTH, top=DUNGEON_DEPTH + 7.0, faces=-1)
    s.road(-24.0, -LENGTH, decor=False)
    for node in s.children:
        # The road outside, up where the city is.
        if node["name"] in ("Ground Road", "Pavement", "Verge", "Lane Line") and node["position"][2] < -23.9:
            node["position"][1] += DUNGEON_DEPTH
    for side in (-1, 1):
        s.shape("Verge", (side * 26.0, DUNGEON_DEPTH - 0.03, top - 4.0), (40.0, 0.2, 8.0), GRASS)
    s.coins(1, -25.0, -29.0, step=2.0, y=DUNGEON_DEPTH + 0.55)
    s.exit(DUNGEON_DEPTH)
    yield s


def sky():
    h = SKY_HEIGHT
    s = Section("Sky", "Sky Enter")
    # Thrown well above the clouds, and the cloud starts far enough on that even at top speed the runner is over its
    # level before reaching its edge (near the top of a throw the climb is slow; 2 m over came in under the edge).
    s.road(0.0, -6.0, decor=False)
    s.launch(-3.0, h + 6.0)
    for lane in range(3):
        for i, y in enumerate((4.0, 10.0, 16.0, 21.0, 25.0, 28.0)):
            s.arc_coin(lane, -6.0 - i * 2.5, y)
    s.cloud(0, -24.0, -LENGTH, base=h, wide=True)
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
        for i, y in enumerate((-2.0, -5.0, -9.0, -14.0, -20.0, -26.0)):
            s.arc_coin(lane, -10.0 - i * 2.0, y)
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
        # The music and sounds written out, so the phone app (which carries what levels name) brings them along.
        "gameModes": [{"id": uid("level", "mode"), "type": "Runner", "properties": {
            "music": "Assets/Audio/Music/Pixel Peeker Polka - faster.wav",
            "beachMusic": "Assets/Audio/Music/Monkeys Spinning Monkeys.wav",
            "coinSound": "Assets/Audio/Starter/Gameplay/pickup.wav",
            "jumpSound": "Assets/Audio/Starter/Gameplay/jump.wav",
            "landSound": "Assets/Audio/Starter/Gameplay/land.wav",
            "laneSound": "Assets/Audio/Starter/Gameplay/whoosh.wav",
            "crashSound": "Assets/Audio/Starter/Gameplay/impact_hard.wav",
            "fallSound": "Assets/Audio/SampleCourse/Hazards/fall.wav",
            "powerUpSound": "Assets/Audio/Starter/Gameplay/checkpoint.wav",
            "stompSound": "Assets/Audio/SampleCourse/Enemies/stomp.wav",
            "launchSound": "Assets/Audio/Starter/Gameplay/respawn.wav"}}],
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
