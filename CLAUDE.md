# ManaRenderer

C# .NET 10 renderer / game engine using Silk.NET. Target platforms are **macOS (arm64)** and **Windows**. **WebGPU** (wgpu via Silk.NET) is the primary and expected graphics backend going forward. **OpenGL** is still maintained and should not be broken — it exists as a fallback and for compatibility.

## Projects

| Project | Role |
|---|---|
| `ManaCore` | Shared interfaces and data types (no graphics dependency) |
| `ManaGraphics` | WebGPU + OpenGL backend implementations, WGSL shaders |
| `Mana` | Engine layer — scene graph, components, renderers, game modes |
| `ManaUI` | Retained-mode MVVM UI framework (no graphics dependency — headless-testable) |
| `ManaUITests` | Headless test suite for ManaUI |
| `ManaVoxel` | Voxel editor app (the main runnable executable) |

**Runnable entry point:** `ManaVoxel`

## Build & Run

```bash
dotnet build
dotnet run --project ManaVoxel

# ManaUI test suite — no GPU or window required; exits non-zero on failure
dotnet run --project ManaUITests
```

## Architecture

- **Scene graph:** `ManaWorld` → `ManaScene[]` → `ManaSceneNode` tree → `ManaComponent[]`
- **Render loop:** `BaseGameMode` drives update + render. `ManaGameModeGraphicsBridge` dispatches to the active backend. `ManaWorld.CreateRenderScene()` collects mesh instances each frame.
- **ImGui:** Custom WebGPU ImGui controller at `ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuImGuiController.cs`. UI views live under each project's `Code/UI/` folder.
- **ManaUI:** Retained-mode UI intended to take over editor UI from ImGui; both run side by side during migration, ManaUI drawing after ImGui in the same pass. Two-pass WPF-style measure/arrange, lambda-based bindings, docking. Everything draws as one instanced SDF primitive, so a frame is a single draw call. The engine layer owns the tree (`Mana/Code/UI/ManaUi/ManaUiHost.cs`) and hands `ManaWebGpuContext.UiRoot` a finished draw list. **Anything sharing the ImGui render pass must reset the scissor rect first** — ImGui leaves one set per draw command.
- **Shaders:** WGSL shaders at `ManaGraphics/Assets/Shaders/WebGpu/*.wgsl`, copied to output on build.
- **Physics:** BepuPhysics v2.

## Code Style

- Always use curly braces `{}` on all control flow blocks — no single-line braceless if/for/foreach/while bodies.
- Nullable reference types enabled; avoid `!` suppression where possible.
- `AllowUnsafeBlocks` is on in ManaGraphics and Mana (needed for WebGPU raw pointer interop).
