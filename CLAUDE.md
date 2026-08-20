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
- **ImGui:** Custom WebGPU ImGui controller at `ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuImGuiController.cs`. UI views live under each project's `Code/UI/` folder.
- **ManaUI:** Retained-mode UI intended to take over editor UI from ImGui; both run side by side during migration, ManaUI drawing after ImGui in the same pass. Two-pass WPF-style measure/arrange, lambda-based bindings, docking. Everything draws as one instanced SDF primitive, so a frame is a single draw call. The engine layer owns the tree (`Mana/Code/UI/ManaUi/ManaUiHost.cs`) and hands `ManaWebGpuContext.UiRoot` a finished draw list. **Anything sharing the ImGui render pass must reset the scissor rect first** — ImGui leaves one set per draw command.
- **Levels:** `.manalevel` JSON in `Levels/`. A level holds only what somebody placed -- a node is saved when it carries a `ManaLevelSource` (`Mana/Code/Levels/`), which `ManaShapeFactory` and the mesh import paths set as they build. Demo scenery (grid planes, planet, borders) has none and is never captured, because it comes back from the code that generates it. Voxel grids go in as a palette plus one base64 blob. Capture/load live in `ManaVoxel/Code/Levels/`.
- **Play mode:** F5 flips the editor into `PlayModeController` -- character spawned at the `ManaPlayerStartNode`, possessed, follow camera, editor UI collapsed by `ManaUiPlayOverlay`. Ctrl+F5 saves the level and launches `--play` in a second process, which runs `VoxelPlayGameMode`: ground, level, character, no editor.
- **Shaders:** WGSL shaders at `ManaGraphics/Assets/Shaders/WebGpu/*.wgsl`, copied to output on build.
- **Physics:** BepuPhysics v2.

## Code Style

- Always use curly braces `{}` on all control flow blocks — no single-line braceless if/for/foreach/while bodies.
- Nullable reference types enabled; avoid `!` suppression where possible.
- `AllowUnsafeBlocks` is on in ManaGraphics and Mana (needed for WebGPU raw pointer interop).
