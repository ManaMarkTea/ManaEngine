# ManaRenderer

C# .NET 10 renderer / game engine using Silk.NET. Target platforms are **macOS (arm64)** and **Windows**. **WebGPU** (wgpu via Silk.NET) is the primary and expected graphics backend going forward. **OpenGL** is still maintained and should not be broken — it exists as a fallback and for compatibility.

## Projects

| Project | Role |
|---|---|
| `ManaCore` | Shared interfaces and data types (no graphics dependency) |
| `ManaGraphics` | WebGPU + OpenGL backend implementations, WGSL shaders |
| `Mana` | Engine layer — scene graph, components, renderers, game modes |
| `ManaUI` | Retained-mode MVVM UI framework (no graphics dependency — headless-testable) |
| `ManaTests` | Headless test suite for all systems; suites live in `Code/<System>/` |
| `ManaVoxel` | Voxel editor app (the main runnable executable) |

**Runnable entry point:** `ManaVoxel`

## Build & Run

```bash
dotnet build
dotnet run --project ManaVoxel

# The game, standalone: one level, no editor UI
dotnet run --project ManaVoxel -- --play "Content/Levels/Sample Course.manalevel"

# The editor, opened on a level
dotnet run --project ManaVoxel -- --level "Content/Levels/Sample Course.manalevel"

# Test suite — no GPU or window required; exits non-zero on failure
dotnet run --project ManaTests

# One system's suites only
dotnet run --project ManaTests -- ManaUI

# Results in a window, drawn with ManaUI itself (filter, failures-only, re-run)
dotnet run --project ManaTests -- -gui
```

Add coverage by dropping a suite in `ManaTests/Code/<System>/` and registering it in the suite list in
`ManaTests/Code/TestRun.cs`. Suite files end in `Tests.cs`, and live under the system they exercise --
asset and model logic under its own project, not under ManaUI because a panel happens to call it.

- **Suites run in parallel.** Never change process-wide state in one (`ManaContentAssets.Root`, the working directory,
  a static setting) -- it leaks into whatever else is running. Build a fresh `ManaWorld`/`ManaContext` per suite.
- **When the normal output is locked** (a debugger holding `Artifacts/Bin/ManaVoxel`), build the tests elsewhere and
  run the dll: `dotnet build ManaTests -o Artifacts/testcheck` then `dotnet Artifacts/testcheck/ManaTests.dll`.
- **Content checks are strict:** `ContentAssetChecks` loads every prefab and every bundled level and fails on any load
  warning; bundled level roots must sit at x and z >= 32 (the scene offset) with cameras clear of the zero-axis walls;
  every picture a `.manasheet`/`.manaslice` names must exist in its pack.

## Testing levels with scripts

The editor can be driven by a JSON script instead of a person: `ManaVoxel --command-file <script.json>` runs the
steps one per frame (`ManaVoxel/Code/Automation/ManaCommandRunner.cs`, format in `ManaCommandFile.cs`) and is how
anything visual or interactive gets checked -- in-app screenshots, not desktop captures (those can show the user's
other windows). `Tools/Automation/run-commands.ps1` does the plumbing: writes the script, runs the editor from the
repo root, waits for it to quit and prints what it reported. `@@OUT@@` in a path becomes `Artifacts/Automation/<Name>.png`;
`@@PHOTO@@` becomes `DevPhotos/<today>` (`-PhotoDir` sends those somewhere else for a look first).

```powershell
dotnet build ManaVoxel -o Artifacts/AutomationBin      # a separate output, so a running editor or debugger is no problem
./Tools/Automation/run-commands.ps1 -Name course -Commands '[
  {"type":"open_level","path":"Content/Levels/2D Sample.manalevel"}, {"type":"wait","frames":240},
  {"type":"play"}, {"type":"wait","frames":90},
  {"type":"teleport_player","position":[100,1.3,40]}, {"type":"wait","frames":45},
  {"type":"log_player","name":"desert"}, {"type":"screenshot","path":"@@OUT@@"},
  {"type":"quit"}]'
```

| Command | Fields | Does |
|---|---|---|
| `open_level` | `path` | Opens a level in the editor, as File > Open does |
| `wait` | `frames` | Holds that many rendered frames (loading, physics settling, an animation) |
| `move_camera` | `position`, `target`, `frames`, `background` | Puts the editor camera somewhere, facing a point; with `frames`, glides there eased (`background` lets the script go on meanwhile) |
| `select_node` | `name` | Selects a node by name (brings up its Inspector and gizmo) |
| `screenshot` | `path`, `clean` | Writes the next frame to a PNG (WebGPU only); `clean` leaves out every panel and the HUD -- the scene across the whole window |
| `gizmos` | `visible` | Editor helpers (axes, markers) on or off -- off for pictures |
| `play` / `stop_play` | | Enters and leaves play mode (F5) |
| `teleport_player` | `position` | While playing, moves the character -- to look at a stretch of a level without walking there |
| `log_player` | `name` | While playing, logs `Command file: player <name> at <feet>, grounded <bool>` |
| `sky` | `name`, `timeOfDay`, `frames`, `background` | Sky mode by name (`Day`, `DayNightCycle`, `Space`, `Photo`); a time of day holds the cycle still, and with `frames` the day runs forward to it |
| `dirty_report` | `path` | Writes each open level's saved snapshot and a fresh capture (`<path>.saved.json`, `.now.json`) -- identical means it opens unchanged |
| `open_prefab` / `open_asset` / `review_changes` | `path` / `path` / `name` | Edit Prefab; a sheet or slice in the Sprite Asset window (or rules in the Generator); Review Changes on an instance |
| `open_panel` / `open_tools` | `name` / | Brings up a panel by id; opens the viewport tools drawer |
| `emission` | `name`, `position` (rgb), `frames` (strength x100) | Sets emission on every mesh under a node |
| `dump_environment` | `path` | Saves the reflection cube's six faces as `<path>.face0..5.png` (+X -X +Y -Y +Z -Z) |
| `material` | `name`, `position` (metallic, roughness) | Sets metallic and roughness on every mesh under a node -- `[1, 0]` is a mirror |
| `drop_to_floor` | | Drops the selection onto whatever is below it (the End key) |
| `grid_autotile` / `grid_generate` | see `ManaCommandFile` | Paints a road preset through cells; runs the WFC generator |
| `profile_reset` / `profile_dump` | `path` | Frame profiler: start fresh; write the per-scope report |
| `quit` | | Closes the editor (always end with it, or the runner times out) |

Reference scenes for rendering checks live in `Content/Levels/Tests/` (not in the web selector), with the scripts that
drive them in `Tools/Automation/Checks/` -- `./Tools/Automation/run-commands.ps1 -Name shadows -File Tools/Automation/Checks/shadows.json`.
`Shadow Contact` (shadows meeting their casters, objects in shadow, low sun) and `Reflections` (a mirror slab and ball --
their material is in the level; compare each shot with the matching `-sky` one), `Sprite Normals` (lit Kenney sprites
under a raking lamp: made normal maps on the left, flat on the right; `sprite-normals.json`). Add a scene and a
script there whenever a rendering bug is fixed.

Patterns that work:
- **Does a level open clean?** `open_level`, `wait`, `dirty_report`, then compare the two files -- the same means
  nothing about it changes just by being opened (a level that reads as modified on open is a bug).
- **Is the footing right?** In play, `teleport_player` above each platform, `wait`, `log_player`; a respawn back at
  the start or `grounded False` is a fall-through. Restart play (`stop_play`, `play`) before each moving platform --
  they keep moving along their path.
- **A picture of play:** `play`, `wait` ~120 frames, `screenshot`. Editor screenshots include the panels: crop to
  the viewport, whose rectangle depends on the layout (open the PNG once and read it off).
- Scripts worth keeping -- tours, highlight reels, demos -- live in `Content/Scripts/` (`Highlights/Showcase Levels.json`,
  `Highlights/Sample Course Tour.json` shoot DevPhotos with `@@PHOTO@@`); one-off scripts and every output belong in
  `Artifacts/` or the session scratchpad.

## Testing the web build

Publish (`dotnet publish ManaVoxel.Web/ManaVoxel.Web.csproj -c Release`, AOT, a few minutes), serve it
(`python ManaVoxel.Web/serve.py 8123`), then drive headless Chrome with `node Tools/WebAssets/webdrive.mjs <out dir>
'<steps>'` -- `go`, `wait`, `waitFor` (console text), `shot`, `press`/`down`/`up` (key codes), `eval` (page
JavaScript). The page console is the game's log: `[BrowserPlay] Spawned` means a level is up, and the browser
logs the score (`[BrowserPlay] Coins x/y`) for scripts; the HUD itself is ManaUI, as on desktop. Things that only break there: the trimmed build
strips reflection metadata (types made by JSON need `DynamicDependency` and parameterless constructors), assets come
from the catalog rather than a checkout, and files can be locked by Defender mid-build (just build again).

## Working conventions

- **DevPhotos:** when something visual works for the first time, save a screenshot of it to
  `DevPhotos/<yyyy-mm-dd> <what-it-is>.png` (e.g. `2026-09-30 2d-biome-castle.png`) -- an in-app capture, cropped
  to the viewport, of the feature at its best. It is the project's visual record; one per milestone, not per tweak.
- **Todo.md** is the user's list. Mark items done (`[x]`) with a short note of what was built and where, and add any
  open follow-ups under the item, so the state of things can be read there between sessions.
- **The Sample Course is the source of truth:** patch `Content/Levels/Sample Course.manalevel` by hand or by script,
  never regenerate it -- it carries authored audio and tuning that a rebuild would lose. Other levels too: edit by
  node id, keep every id.
- **Other agents edit this repo at the same time.** Files can change under you: edit by anchor (the surrounding
  text), not by line number, and stop any running `ManaVoxel.exe` before building it. Commit only your own files
  (`git add <paths>`, never `git add -A`) -- the working tree can hold someone else's unfinished change.
- **Code comments:** one or two lines, saying why, not what; no essays.
- **Commit and push** working steps as they land, with a message saying what changed and why.

## Architecture

- **Scene graph:** `ManaWorld` → `ManaScene[]` → `ManaSceneNode` tree → `ManaComponent[]`
- **Render loop:** `BaseGameMode` drives update + render. `ManaGameModeGraphicsBridge` dispatches to the active backend. `ManaWorld.CreateRenderScene()` collects mesh instances each frame.
- **ManaUI is the only UI framework.** ImGui was removed entirely (package references, controllers, the `Draw*` hooks and every `*PanelView.cs`); a mode with `EnableManaUi` off now has no UI at all rather than an ImGui fallback. Retained-mode, two-pass WPF-style measure/arrange, lambda-based bindings, docking. Everything draws as one instanced SDF primitive, so a frame is a single draw call. The engine layer owns the tree (`Mana/Code/UI/ManaUi/ManaUiHost.cs`) and hands `ManaWebGpuContext.UiRoot` a finished draw list, which the backend submits in the tone-map pass. Views live under each project's `Code/UI/ManaUi/` folder.
- **Levels:** `.manalevel` JSON in `Content/Levels/`. A level holds only what somebody placed -- a node is saved when it carries a `ManaLevelSource` (`Mana/Code/Levels/`), which `ManaShapeFactory` and the mesh import paths set as they build. Demo scenery (grid planes, planet, borders) has none and is never captured, because it comes back from the code that generates it. Voxel grids go in as a palette plus one base64 blob. Capture/load live in `ManaVoxel/Code/Levels/`.
- **Grid maps:** `ManaGridMapNode` (`ManaVoxel/Code/GridMaps/`) is Godot's GridMap -- cells of (palette index, quarter turns) painted from a palette of mesh assets. Each palette asset loads once and its tiles share its meshes; tiles are editor-transient children, so a level saves the palette and one cell blob, never the tiles. Painting lives in `VoxelEditorGameMode.GridMap.cs`, switched on from the Grid Map tool window (`GridMapToolManaView`), which floats while a grid map is selected and exists only then (`GridMapEditing` holds the brush; its Blank brush clears). Each palette item has its own `GridMapItemSettings` -- cells covered, fit-or-fixed scale, turn, align (Model, as authored, by default). Maps have layers; the cell API works on `ActiveLayer`, and a hidden layer builds nothing. `ManaEditorGizmos.Visible` (the camera widget's gizmo button) hides editor-only scene helpers. The editor room (256 units, centred on the origin, with the world axes drawn on its floor by `grid.wgsl`) shows only its floor; its walls, ceiling and coves come up while drawing voxels (`SyncGridRoomWalls`) or with `ManaEditorGizmos.ShowRoom` (the camera widget's room button). Auto-tile sets (`GridMapAutoTile.cs`) are roads and the like: palette items tagged with the sides they join on, re-picked with their neighbours as cells change; `GridMapAutoTilePresets` holds the Kenney defaults, all editable in the window.
- **Generator (WFC):** `GridMapWfc` is wave function collapse over grid map cells: tiles with per-cell socket labels on each side, multi-cell tiles cut into parts that only join each other. Rules are `.manawfc` JSON in `Content/Generators/` (`Dungeon.manawfc` is Kenney's modular dungeon kit), edited in the Generator window (`GridMapWfcManaView`, opened by Generate... in the Grid Map window) and run into an area of the selected map by `GridMapWfcGenerator`. Big rooms of any size are built from pieces (`GridMapWfcBigRooms`) and laid first. Props rules (`Kind: props`, e.g. `Dungeon Props.manawfc`, named by the dungeon rules' `Then`) furnish the result on a props layer, reading each cell's tag and walls off the map (`ReadDungeon`); `Pairs` lets labels join others, and a `>` label never joins itself.
- **Emoji:** ManaUI draws colour emoji from an `IManaColorGlyphSource` -- Noto's CBDT tables (`ManaColorBitmapTable`) on desktop; on the web the browser's own emoji font, drawn on a canvas (`ManaBrowserEmoji`, `drawEmoji` in `mana_interop.js`), so the 10 MB font is not downloaded. The web loading screen shows each one as it is drawn.
- **UI from JSON:** `UiMarkup` (`ManaUI/Code/Markup/`) builds a ManaUI tree from a `.manaui` document -- properties by name, `children`/`child`, `grid`/`dock`/`canvas` slots, `bind` (polled values) and `on` (events) against a `UiMarkupData`, colours as hex or `@ThemeToken`. Layouts live in `UI/`; Panels > UI Designer (`UiDesignerManaView`) edits them with an outline, a live preview (click to select) and a properties panel, all through the JSON.
- **Prefabs:** reusable authored trees in `Content/Prefabs/*.manaprefab` (browse them in the Project panel; see `Content/README.md` for the editor workflow). Model: `ManaVoxel/Code/Assets/ManaPrefab.cs`; loading/instances: `ManaLevelLoader` (`Prefab` kind) and `ManaPrefabRuntime`. Grid map presets are `*.managridpreset` beside them (`ManaGridMapPreset`). Edit Prefab is a prefab stage: the Outliner shows only the prefab (`ProjectAssetViewModel.EditingScenes`) under a Save / Save & Back bar. `PrefabOverrideMarks` marks each Inspector row an instance -- or the variant being edited -- changes from its source, with a revert arrow; a widget opts a row in with `PrefabOverrideMarks.Mark(context, row, node, component, "savedFieldName")`.

- **Voxel tools:** the tools, their settings and the work plane float in the Voxel Tools window (`VoxelToolPaletteManaView.BuildWindow`) while a voxel node is selected or a voxel tool is on; the toolbar keeps one toggle for it. Selecting a voxel node makes it the one the tools draw into (`VoxelEditorViewModel.ActivateVoxelNode`). The tool windows come and go in `ManaUiDockDemo.SyncToolWindow`.
- **Play mode:** F5 flips the editor into `PlayModeController` -- character spawned at the `ManaPlayerStartNode`, possessed, follow camera, editor UI collapsed by `ManaUiPlayOverlay`. Ctrl+F5 saves the level and launches `--play` in a second process, which runs `VoxelPlayGameMode`: ground, level, character, no editor.
- **Sky:** `ManaIrradianceSettings.Sky` picks Day, Day and Night (`TimeOfDay`, `TimeSpeed` in sky seconds a second, or `RealTime`; the menu bar's sky clock, `SkyClockManaView`, sets them), Space (the LCARS theme's default) or Photo, from Appearance > Sky. `ManaSky.Evaluate` works out the sun and the daylight once a frame; `skybox.wgsl` draws from it (a scattering atmosphere after Sebastian Lague's MIT-licensed Solar System, notice in the file; the moon is NASA's CGI Moon Kit map, `Mana/Assets/Textures/Sky/moon_lroc_1k.jpg`, wrapped onto its disc as a sphere), and the fill lights and editor grid dim with the daylight, so placed lamps carry a night scene. On WebGPU the same shader is drawn into the reflection and irradiance cubes whenever the sky moves (`DrawSkyIntoEnvironmentIfNeeded` in `ManaWebGpuContext.Environment.cs`), so reflections show the sky on screen; the 4K photo is decoded into them only for Photo (and fetched from the asset catalog on the web). A scene with no directional light of its own is lit by the sky's sun, then the moon (`ManaSky.Light`), which casts the cascaded shadows (`ManaWebGpuContext.Shadows.cs`; the editor grid receives but never casts). Placed point lights get 16 slots of their own (`ManaRenderScene.LocalLight*`, falloff in `include/point_lights.wgsl`, lighting meshes and the grid); the four `LightPositions` slots are the far fill lights only.
- **User-made assets:** everything a person sets up once and reuses is a JSON file in `Content/` with `format`, `version`, `id` and `name`, referenced by `{ assetId, assetPath }` (`ManaAssetRef`; the id wins when a file moves). `ManaContentAssets` (`Mana/Code/Content/`) finds, reads (cached until the file changes) and saves them; a `[AssetKind(".ext")]` `ManaAssetRef` property gets an asset picker in the Inspector. Every kind is one entry in `ManaContentKinds` (`ManaVoxel/Code/Assets/`): the Project panel lists, filters, badges and offers New for them from it. Kinds today: prefabs (own instance/override code), sprite sheets, 9-slices, grid map presets, generator rules. Sheets and slices open in the Sprite Asset window (`SpriteAssetManaView`, state in `SpriteAssetEditing`; each change saves at once so the scene updates live), and drag into the scene like prefabs.
- **Sprites in the Import panel:** PNGs are a format there (`MeshAssetFormats.Png`) -- a pack's pictures when it has no models (not its previews, `Spritesheets/` or Kenney's `Double/` copies), loose ones in a `Sprites` folder. Dragging one in makes a Sprite; right-click a selection for Make Sprite Sheet (frames in order) or Make 9-Slice (pieces placed by their names, `SpriteAssetFactory`).
- **2D:** three sprite scripts on a shared base (`ManaSpriteBase`, Look: flip, tint, pixel art, unlit): `Sprite` (one picture, optional `repeatSize`), `SpriteSheet` (a `.manasheet` asset -- frames as separate pictures or a grid, pixels per unit, pivot, named animations -- played by `animation` name) and `Sprite9Slice` (a `.manaslice` asset of nine pieces, laid out across `size` in whole tiles), plus `PolygonCollider`. A sprite is a quad facing +Z drawn by a mesh renderer on its own node, lit (and normal-mapped) by the PBR pass. A `Platformer2D` script (on the Player Start) makes play side-on: the pawn is held to its plane (`ManaPawnNode.PlaneZ`), drawn by a sprite sheet instead of its model, with the boom fixed looking down -Z. Sprite pictures can live in a pack (`zip:kenney_new-platformer-pack-1.1.zip/Sprites/...`, copied out once by `ManaSpritePictures`). `unlit` shows flat art as drawn (the PBR shader inverts the tone map). A `[PicturePath]` string or string[] property gets the Inspector's picture picker (`PicturePicker`: Content, pack pictures, material textures). Sample: `Content/Levels/2D Sample.manalevel`, a five-biome run (grass, desert, snow, mushroom, stone castle) built from Kenney's New Platformer Pack. The pack's pieces are assets in `Content/Sprites/` -- `Characters/` (five aliens, every pose), `Enemies/` (18), `Items/` (coins, flags, torch, spring), `Terrain/` (block, platform and cloud slices for six grounds, water, lava) -- and the things placed again and again are 2D prefabs (`Coin 2D`, `Gem 2D`, `Spring 2D`, `Spikes 2D`, `Checkpoint Flag 2D`).
- **Web build:** `ManaVoxel.Web` opens on a level selector (`levels.json`, written by `Tools/WebAssets/publish_assets.py` with pictures from `Content/Levels/Pictures/`) and plays the chosen level; Escape is a pause menu. The Pages deploy (`.github/workflows/build-web.yml`) puts each build in `<app>/v/<commit>/` and names it in `version.json`, which `index.html` reads uncached -- file names carry no hash and Pages caches for 10 minutes, so deploying over the same paths mixed builds. Assets come from the web asset catalog on demand (`ManaAssetCatalog`; `ResolveAssetPath` and sprite packs look there first). Testing it: see "Testing the web build" below.
- **Shaders:** WGSL shaders at `ManaGraphics/Assets/Shaders/WebGpu/*.wgsl`, copied to output on build.
- **Physics:** BepuPhysics v2.

## Authoring prefabs (by hand or from code)

A prefab is plain JSON, so one can be authored without the editor -- write the file and run the suite: `ContentAssetChecks` loads and places every prefab in `Content/` and fails on any warning.

```json
{
  "format": "mana-prefab", "version": 1,
  "id": "<32 hex chars, unique per asset>",
  "name": "Coin",
  "root": {
    "id": "<32 hex>", "name": "Coin", "kind": "Group",
    "position": [0, 0, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1],
    "scripts": [
      { "id": "<32 hex>", "type": "Trigger", "properties": { "size": [0.8, 0.8, 0.8], "offset": [0, 0.4, 0] } },
      { "id": "<32 hex>", "type": "Collectible", "properties": { "value": 1, "spinDegreesPerSecond": 120 } }
    ],
    "children": [
      { "id": "<32 hex>", "name": "Coin Model", "kind": "Mesh", "position": [0, 0.15, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1],
        "asset": "zip:kenney_mini-dungeon.zip/Models/GLB format/coin.glb" }
    ]
  },
  "properties": [
    { "id": "<32 hex>", "name": "Value", "nodeId": "<root id>", "componentId": "<Collectible id>", "property": "value", "minimum": 1, "maximum": 100 }
  ]
}
```

- **Root and nodes** are `ManaLevelNode`s, exactly as in a `.manalevel` (camelCase JSON, enums as strings, rotation in radians). Kinds: `Group` (empty parent), `Shape` (`shape`: Cube/Sphere/Pyramid/Wedge/Disk/Torus/Icosahedron/Icosphere/Quad, `color`, `size`, and an optional `material` holding only what differs from the default -- `metallic`, `roughness`, `albedo`, `emission`, `emissionStrength`, `occlusion`, `normalStrength`, `opacity`, `opacityMode`, `alphaCutoff`, `uvScale`, `uvOffset`, `flatShaded`, `unlit`; no texture maps yet), `Mesh` (`asset`: a repo path like `Assets/Models/Kenny/candle.glb` or `zip:<pack>.zip/Models/GLB format/<model>.glb`), plus `Water`, `GridMap`, `Voxels`. One root; put everything else under it. No nested prefabs (`kind: Prefab` inside a prefab is rejected).
- **Every node and every script needs an `id`** -- 32 hex chars (`uuid4().hex`), unique within the prefab. Overrides and exposed properties key on these, never on names, so never reuse or regenerate them once instances exist.
- **Components** go in `scripts` by type name: `Trigger`, `Collectible`, `Hazard`, `Checkpoint`, `Goal`, `SpringPad`, `BoostPad`, `MovingPlatform`, `Enemy`, `AudioSource`, `TrainCar`, `Sprite` (`texture`/`normalMap` PNG or `zip:` pack paths, `pixelsPerUnit`, `pivot` [x, y], `repeatSize` [w, h] metres), `SpriteSheet` (`sheet` asset ref, `animation` name, `playing`, `frame`), `Sprite9Slice` (`slice` asset ref, `size` [w, h] metres, `pivot`) -- all three also take `flipX`, `tint`, `pixelArt`, `unlit` --, `PolygonCollider` (`points` as [x0, y0, x1, y1, ...] in the XY plane, `depth`; concave is fine; edited in the viewport while selected -- `VoxelEditorGameMode.Polygon.cs`), `Parallax` (`factor` [x, y]: share of the camera's travel a backdrop follows while playing), `Platformer2D` (`sheet` asset ref, `idleAnimation`/`runAnimation`/`jumpAnimation` names, `jumpHeight` (held jump while playing side-on), `cameraDistance`, `orthographic`, `viewHeight`), `Glow` (`color`, `strength` -- above 1 blooms, `lightRange`, `lightIntensity`, `lightOffset`, `pulseSpeed`; makes everything beneath glow and adds a small light -- see `Content/Prefabs/Glowing Coin.manaprefab`, a Coin variant that adds one), `Light` (`color` `[r, g, b, a]`, `intensity`, `range` in metres, `lightType` Point). Their `properties` are the component's own serialized fields -- copy a working example from `Content/Levels/Sample Course.manalevel`.
- **Exposed properties** (`properties`) let each instance change one value: a component field is `nodeId` + `componentId` + the field name; a node field is `nodeId` + e.g. `position` (no `componentId`). `minimum`/`maximum` are optional.
- **Placing one in a level** is a node `{ "id": "<32 hex>", "name": "...", "kind": "Prefab", "position": [...], "prefab": { "assetId": "<prefab id>", "assetPath": "Prefabs/Coin.manaprefab", "overrides": [] } }` -- `assetPath` is relative to `Content/`; the id is what is trusted if the file moves. Levels are format version 2.
- **Per-instance changes** are `overrides`: `{ "path": [<nodeId>, "scripts", <scriptId>, "properties", "color"], "value": [1, 0.15, 0.1, 1] }` for a script field, `[<nodeId>, "position"]` for a node field. `Content/Levels/Prefab Lamps.manalevel` recolours one `Lamp` this way.
- **Variants** hold `base` instead of `root` -- `"base": { "assetId": "<Lamp id>", "assetPath": "Prefabs/Lamp.manaprefab", "overrides": [ ...only what changes... ] }` with `"properties": []` to show the base's -- and follow every later edit to their base. See `Content/Prefabs/Blue Lamp.manaprefab`; the Project panel's Create Variant makes one, and saving it writes only the diff. Variants of variants work (8 deep); a loop is refused. A variant can add a script: an override `{ "path": [<nodeId>, "scripts", <new script id>], "value": { "id", "type", "properties" }, "add": true }`.

## Code Style

- Always use curly braces `{}` on all control flow blocks — no single-line braceless if/for/foreach/while bodies.
- Nullable reference types enabled; avoid `!` suppression where possible.
- `AllowUnsafeBlocks` is on in ManaGraphics and Mana (needed for WebGPU raw pointer interop).
