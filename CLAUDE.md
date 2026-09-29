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
dotnet run --project ManaVoxel -- --play "Levels/Sample Course.manalevel"

# The editor, opened on a level
dotnet run --project ManaVoxel -- --level "Levels/Sample Course.manalevel"

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
- **Levels:** `.manalevel` JSON in `Levels/`. A level holds only what somebody placed -- a node is saved when it carries a `ManaLevelSource` (`Mana/Code/Levels/`), which `ManaShapeFactory` and the mesh import paths set as they build. Demo scenery (grid planes, planet, borders) has none and is never captured, because it comes back from the code that generates it. Voxel grids go in as a palette plus one base64 blob. Capture/load live in `ManaVoxel/Code/Levels/`.
- **Grid maps:** `ManaGridMapNode` (`ManaVoxel/Code/GridMaps/`) is Godot's GridMap -- cells of (palette index, quarter turns) painted from a palette of mesh assets. Each palette asset loads once and its tiles share its meshes; tiles are editor-transient children, so a level saves the palette and one cell blob, never the tiles. Painting lives in `VoxelEditorGameMode.GridMap.cs`, switched on from the Grid Map tool window (`GridMapToolManaView`), which floats while a grid map is selected and exists only then (`GridMapEditing` holds the brush; its Blank brush clears). Each palette item has its own `GridMapItemSettings` -- cells covered, fit-or-fixed scale, turn, align (Model, as authored, by default). Maps have layers; the cell API works on `ActiveLayer`, and a hidden layer builds nothing. `ManaEditorGizmos.Visible` (the camera widget's gizmo button) hides editor-only scene helpers. Auto-tile sets (`GridMapAutoTile.cs`) are roads and the like: palette items tagged with the sides they join on, re-picked with their neighbours as cells change; `GridMapAutoTilePresets` holds the Kenney defaults, all editable in the window.
- **Play mode:** F5 flips the editor into `PlayModeController` -- character spawned at the `ManaPlayerStartNode`, possessed, follow camera, editor UI collapsed by `ManaUiPlayOverlay`. Ctrl+F5 saves the level and launches `--play` in a second process, which runs `VoxelPlayGameMode`: ground, level, character, no editor.
- **Shaders:** WGSL shaders at `ManaGraphics/Assets/Shaders/WebGpu/*.wgsl`, copied to output on build.
- **Physics:** BepuPhysics v2.

## Code Style

- Always use curly braces `{}` on all control flow blocks — no single-line braceless if/for/foreach/while bodies.
- Nullable reference types enabled; avoid `!` suppression where possible.
- `AllowUnsafeBlocks` is on in ManaGraphics and Mana (needed for WebGPU raw pointer interop).
