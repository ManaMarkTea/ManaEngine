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

Add coverage by dropping a suite in `ManaTests/Code/<System>/` and registering it in the `suites` array
in `ManaTests/Code/Program.cs`. Suite files end in `Tests.cs`, and live under the system they exercise --
asset and model logic under its own project, not under ManaUI because a panel happens to call it.

## Architecture

- **Scene graph:** `ManaWorld` → `ManaScene[]` → `ManaSceneNode` tree → `ManaComponent[]`
- **Render loop:** `BaseGameMode` drives update + render. `ManaGameModeGraphicsBridge` dispatches to the active backend. `ManaWorld.CreateRenderScene()` collects mesh instances each frame.
- **ManaUI is the only UI framework.** ImGui was removed entirely (package references, controllers, the `Draw*` hooks and every `*PanelView.cs`); a mode with `EnableManaUi` off now has no UI at all rather than an ImGui fallback. Retained-mode, two-pass WPF-style measure/arrange, lambda-based bindings, docking. Everything draws as one instanced SDF primitive, so a frame is a single draw call. The engine layer owns the tree (`Mana/Code/UI/ManaUi/ManaUiHost.cs`) and hands `ManaWebGpuContext.UiRoot` a finished draw list, which the backend submits in the tone-map pass. Views live under each project's `Code/UI/ManaUi/` folder.
- **Levels:** `.manalevel` JSON in `Content/Levels/`. A level holds only what somebody placed -- a node is saved when it carries a `ManaLevelSource` (`Mana/Code/Levels/`), which `ManaShapeFactory` and the mesh import paths set as they build. Demo scenery (grid planes, planet, borders) has none and is never captured, because it comes back from the code that generates it. Voxel grids go in as a palette plus one base64 blob. Capture/load live in `ManaVoxel/Code/Levels/`.
- **Grid maps:** `ManaGridMapNode` (`ManaVoxel/Code/GridMaps/`) is Godot's GridMap -- cells of (palette index, quarter turns) painted from a palette of mesh assets. Each palette asset loads once and its tiles share its meshes; tiles are editor-transient children, so a level saves the palette and one cell blob, never the tiles. Painting lives in `VoxelEditorGameMode.GridMap.cs`, switched on from the Grid Map tool window (`GridMapToolManaView`), which floats while a grid map is selected and exists only then (`GridMapEditing` holds the brush; its Blank brush clears). Each palette item has its own `GridMapItemSettings` -- cells covered, fit-or-fixed scale, turn, align (Model, as authored, by default). Maps have layers; the cell API works on `ActiveLayer`, and a hidden layer builds nothing. `ManaEditorGizmos.Visible` (the camera widget's gizmo button) hides editor-only scene helpers. The editor room (256 units, centred on the origin, with the world axes drawn on its floor by `grid.wgsl`) shows only its floor; its walls, ceiling and coves come up while drawing voxels (`SyncGridRoomWalls`) or with `ManaEditorGizmos.ShowRoom` (the camera widget's room button). Auto-tile sets (`GridMapAutoTile.cs`) are roads and the like: palette items tagged with the sides they join on, re-picked with their neighbours as cells change; `GridMapAutoTilePresets` holds the Kenney defaults, all editable in the window.
- **Generator (WFC):** `GridMapWfc` is wave function collapse over grid map cells: tiles with per-cell socket labels on each side, multi-cell tiles cut into parts that only join each other. Rules are `.manawfc` JSON in `Content/Generators/` (`Dungeon.manawfc` is Kenney's modular dungeon kit), edited in the Generator window (`GridMapWfcManaView`, opened by Generate... in the Grid Map window) and run into an area of the selected map by `GridMapWfcGenerator`. Big rooms of any size are built from pieces (`GridMapWfcBigRooms`) and laid first. Props rules (`Kind: props`, e.g. `Dungeon Props.manawfc`, named by the dungeon rules' `Then`) furnish the result on a props layer, reading each cell's tag and walls off the map (`ReadDungeon`); `Pairs` lets labels join others, and a `>` label never joins itself.
- **UI from JSON:** `UiMarkup` (`ManaUI/Code/Markup/`) builds a ManaUI tree from a `.manaui` document -- properties by name, `children`/`child`, `grid`/`dock`/`canvas` slots, `bind` (polled values) and `on` (events) against a `UiMarkupData`, colours as hex or `@ThemeToken`. Layouts live in `UI/`; Panels > UI Designer (`UiDesignerManaView`) edits them with an outline, a live preview (click to select) and a properties panel, all through the JSON.
- **Prefabs:** reusable authored trees in `Content/Prefabs/*.manaprefab` (browse them in the Project panel; see `Content/README.md` for the editor workflow). Model: `ManaVoxel/Code/Assets/ManaPrefab.cs`; loading/instances: `ManaLevelLoader` (`Prefab` kind) and `ManaPrefabRuntime`. Grid map presets are `*.managridpreset` beside them (`ManaGridMapPreset`). Edit Prefab is a prefab stage: the Outliner shows only the prefab (`ProjectAssetViewModel.EditingScenes`) under a Save / Save & Back bar. `PrefabOverrideMarks` marks each Inspector row an instance -- or the variant being edited -- changes from its source, with a revert arrow; a widget opts a row in with `PrefabOverrideMarks.Mark(context, row, node, component, "savedFieldName")`.

- **Voxel tools:** the tools, their settings and the work plane float in the Voxel Tools window (`VoxelToolPaletteManaView.BuildWindow`) while a voxel node is selected or a voxel tool is on; the toolbar keeps one toggle for it. Selecting a voxel node makes it the one the tools draw into (`VoxelEditorViewModel.ActivateVoxelNode`). The tool windows come and go in `ManaUiDockDemo.SyncToolWindow`.
- **Play mode:** F5 flips the editor into `PlayModeController` -- character spawned at the `ManaPlayerStartNode`, possessed, follow camera, editor UI collapsed by `ManaUiPlayOverlay`. Ctrl+F5 saves the level and launches `--play` in a second process, which runs `VoxelPlayGameMode`: ground, level, character, no editor.
- **Sky:** `ManaIrradianceSettings.Sky` picks Day, Day and Night (`TimeOfDay`, `TimeSpeed` in sky seconds a second, or `RealTime`; the menu bar's sky clock, `SkyClockManaView`, sets them), Space (the LCARS theme's default) or Photo, from Appearance > Sky. `ManaSky.Evaluate` works out the sun and the daylight once a frame; `skybox.wgsl` draws from it (a scattering atmosphere after Sebastian Lague's MIT-licensed Solar System, notice in the file), and the fill lights, image lighting and editor grid dim with the daylight, so placed lamps carry a night scene. A scene with no directional light of its own is lit by the sky's sun, then the moon (`ManaSky.Light`), which casts the cascaded shadows (`ManaWebGpuContext.Shadows.cs`; the editor grid receives but never casts). Placed point lights get 16 slots of their own (`ManaRenderScene.LocalLight*`, falloff in `include/point_lights.wgsl`, lighting meshes and the grid); the four `LightPositions` slots are the far fill lights only.
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

- **Root and nodes** are `ManaLevelNode`s, exactly as in a `.manalevel` (camelCase JSON, enums as strings, rotation in radians). Kinds: `Group` (empty parent), `Shape` (`shape`: Cube/Sphere/Pyramid/Wedge/Disk/Torus/Icosahedron/Icosphere/Quad, `color`, `size`), `Mesh` (`asset`: a repo path like `Assets/Models/Kenny/candle.glb` or `zip:<pack>.zip/Models/GLB format/<model>.glb`), plus `Water`, `GridMap`, `Voxels`. One root; put everything else under it. No nested prefabs (`kind: Prefab` inside a prefab is rejected).
- **Every node and every script needs an `id`** -- 32 hex chars (`uuid4().hex`), unique within the prefab. Overrides and exposed properties key on these, never on names, so never reuse or regenerate them once instances exist.
- **Components** go in `scripts` by type name: `Trigger`, `Collectible`, `Hazard`, `Checkpoint`, `Goal`, `SpringPad`, `BoostPad`, `MovingPlatform`, `Enemy`, `AudioSource`, `TrainCar`, `Light` (`color` `[r, g, b, a]`, `intensity`, `range` in metres, `lightType` Point). Their `properties` are the component's own serialized fields -- copy a working example from `Content/Levels/Sample Course.manalevel`.
- **Exposed properties** (`properties`) let each instance change one value: a component field is `nodeId` + `componentId` + the field name; a node field is `nodeId` + e.g. `position` (no `componentId`). `minimum`/`maximum` are optional.
- **Placing one in a level** is a node `{ "id": "<32 hex>", "name": "...", "kind": "Prefab", "position": [...], "prefab": { "assetId": "<prefab id>", "assetPath": "Prefabs/Coin.manaprefab", "overrides": [] } }` -- `assetPath` is relative to `Content/`; the id is what is trusted if the file moves. Levels are format version 2.
- **Per-instance changes** are `overrides`: `{ "path": [<nodeId>, "scripts", <scriptId>, "properties", "color"], "value": [1, 0.15, 0.1, 1] }` for a script field, `[<nodeId>, "position"]` for a node field. `Content/Levels/Prefab Lamps.manalevel` recolours one `Lamp` this way.
- **Variants** hold `base` instead of `root` -- `"base": { "assetId": "<Lamp id>", "assetPath": "Prefabs/Lamp.manaprefab", "overrides": [ ...only what changes... ] }` with `"properties": []` to show the base's -- and follow every later edit to their base. See `Content/Prefabs/Blue Lamp.manaprefab`; the Project panel's Create Variant makes one, and saving it writes only the diff. Variants of variants work (8 deep); a loop is refused.

## Code Style

- Always use curly braces `{}` on all control flow blocks — no single-line braceless if/for/foreach/while bodies.
- Nullable reference types enabled; avoid `!` suppression where possible.
- `AllowUnsafeBlocks` is on in ManaGraphics and Mana (needed for WebGPU raw pointer interop).
