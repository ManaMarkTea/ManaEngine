# Compiling ManaRenderer to run in the browser (WebAssembly)

## Status (resume-from-here notes)

No engine code has been written yet — this session got as far as toolchain setup.

- **.NET `wasm-tools` workload**: not installed. `dotnet workload install wasm-tools`
  needs `sudo` on this machine because the SDK at `/usr/local/share/dotnet` is
  root-owned. Run manually: `sudo dotnet workload install wasm-tools`.
- **Emscripten SDK**: install in progress via `brew install emscripten` on the machine
  this was started on. On a fresh machine, either `brew install emscripten` or the
  official `emsdk` clone-and-install flow works — just need `emcc` on `PATH`.
- Next actual coding step once both are installed: **M0** below (the WebGPU-in-browser
  spike). Nothing else should start before M0 passes or explicitly falls back to OpenGL.

## Context

The user wants ManaRenderer's play experience running directly in a browser. Today the
engine is a native desktop app end to end: GLFW windowing (`Silk.NET.Windowing.Glfw`),
a native `wgpu-native` binary for WebGPU (`Silk.NET.WebGPU.Native.WGPU`), native Assimp
for mesh import (`Silk.NET.Assimp`), unrestricted synchronous `System.IO` disk access
everywhere (shaders, textures, levels, a `FileSystemWatcher` for shader hot-reload), and
background work via real OS threads (`Task.Run`, `Parallel.For`). None of that exists in
a browser sandbox as-is. This plan scopes a v1 browser build and the concrete porting
work to get there, based on codebase research (see the native-desktop-assumption map
gathered during investigation) plus current-2026 ecosystem research on WebGPU wasm
support, Silk.NET's own browser tracking issue, and how JS engines (Babylon.js/Three.js)
and tools like `assimpjs` handle mesh import for the web.

**Decisions locked in with the user:**
- **v1 target = play mode only** (`VoxelPlayGameMode`): one bundled level, load, render,
  play. No editor UI, no ImGui docking chrome, no asset library disk scanning, no
  thumbnail cache, no level authoring/saving. This is what makes v1 tractable — it
  eliminates most of the file-write/background-scan/editor-only surface area entirely.
- **Graphics backend = WebGPU in-browser** (not the OpenGL/WebGL2 fallback). Matches the
  project's stated direction, at the cost of real technical risk: Silk.NET's own
  browser/Emscripten WebGPU support is an open tracking issue
  ([dotnet/Silk.NET#740](https://github.com/dotnet/Silk.NET/issues/740)), not a shipped
  package. This is the single highest-uncertainty item in the plan and gets a dedicated
  spike (M0) before any engine porting work starts.
- **Mesh import**: the user pushed back on "voxel-only" and asked what Babylon.js/
  Three.js/`assimpjs` actually do, then asked whether mesh loading could be abstracted to
  work with either backend. Answer folded into the plan (see M4): those JS engines don't
  embed Assimp at runtime — they parse glTF natively in-language because glTF is designed
  to be a simple, web-friendly runtime format, and use per-format JS loaders (or a
  client-side one-time conversion step) for anything else. `assimpjs` exists but is used
  as an offline/import-time *converter to glTF*, not as an engine's runtime mesh-import
  path. We do the same, behind an explicit `IMeshImportBackend` seam: an
  `AssimpMeshImportBackend` (today's `ManaGltfLoader.cs`, unchanged, covers
  OBJ/FBX/DAE/STL/PLY too) and a new `SharpGltfMeshImportBackend` (pure managed,
  glTF/GLB only) both implement it, and the host (desktop vs. `ManaVoxel.Web`) picks
  which one `ManaWorld` uses at composition time — not a runtime OS check buried in
  loader internals. v1 browser support covers glTF/GLB; legacy formats stay
  desktop/Assimp-only for now, with client-side `assimpjs`-to-glTF conversion as a
  possible later milestone.

## Architecture for the browser build

- New project `ManaVoxel.Web`, `net10.0` targeting the `browser-wasm` RID via the
  `wasm-tools` workload — **not Blazor**. The engine already owns its render loop and
  input handling; we want the plain "wasm console app" shape (the same shape as the
  community `dotnet-webgl-sample` precedent), with a small hand-written `index.html` +
  JS glue module, not Razor/Blazor's component model.
- Everything native-desktop-only gets swapped for a browser-appropriate implementation
  behind the *same* seams the code already has (`IWindow`/`IInputContext` where Silk.NET
  provides them; new small abstractions where it doesn't). Desktop builds (`ManaVoxel`,
  `Mana`) are untouched — all new code is additive and browser-only.

## Milestones

**M0 — WebGPU-in-browser spike (de-risk first, before touching engine code)**
Build the smallest possible throwaway program in `ManaVoxel.Web`: clear a canvas to a
solid color via WebGPU. The mechanism: Mono's wasm workload supports statically linking
custom native code into the final `.wasm` via `<NativeFileReference>` + `[DllImport("__Internal")]`.
Write (or vendor) a thin C shim that implements the subset of the `webgpu.h` C ABI that
`Silk.NET.WebGPU`'s C# bindings already call, compiled with the Emscripten SDK, backed by
Emscripten's own `library_webgpu.js` (which forwards Dawn/wgpu C-ABI calls to the
browser's real `navigator.gpu`). This validates the entire technical premise of "WebGPU
in browser via Silk.NET's existing bindings" before any engine code is ported. **If this
spike fails or proves impractical, fall back to WebGL2/OpenGL for v1** — flag that
decision point back to the user rather than sinking further time into WebGPU.

**M1 — Browser windowing/input**
Silk.NET has no shipped browser windowing backend, so `Window.Create(...)` (used at
`ManaGraphics/Code/Graphics/WebGPU/GameSilkWebGpu.cs:58`) can't just work. Write a
minimal custom `IWindow`/`IInputContext` implementation backed by an HTML `<canvas>`:
size/resize via `ResizeObserver`, frame pump via `requestAnimationFrame`, keyboard/mouse
via browser DOM events — all bridged through `JSImport`/`JSExport`
(`System.Runtime.InteropServices.JavaScript`). Scope is bounded because
`ManaWebGpuImGuiController` and the input system already consume Silk.NET's abstract
`IInputContext`/`IKeyboard`/`IMouse`/`ICursor` interfaces rather than calling GLFW
directly — confirmed during investigation. (Play-mode-only means ImGui itself may not
even be needed at runtime — verify whether `VoxelPlayGameMode` constructs an ImGui
controller at all; if not, this milestone shrinks further.)

**M2 — WebGPU surface binding**
Swap `Silk.NET.WebGPU.Native.WGPU` (desktop `wgpu-native` binary) for the M0 shim on the
browser TFM/RID (conditional `NativeFileReference`/package reference in
`ManaVoxel.Web.csproj`). Get `WebGPUSurface.CreateWebGPUSurface` in
`ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.cs:201` working against the M1
canvas window — likely needs a browser-specific surface-creation path since that helper
currently pulls a native platform window handle (HWND/NSWindow/X11) that doesn't exist
for a canvas.

**M3 — Asset pipeline**
Every shader/texture/level loader in this codebase calls `System.IO` directly with
desktop semantics (working-directory probing in `ManaShader.cs`/`ManaKhronosShader.cs`/
`ManaTexture.cs`, `FileSystemWatcher` hot-reload in `ManaShader.cs`, `ZipFile.OpenRead`
in `ManaLevelLoader.cs`). For v1 (play mode, read-only — no level saving needed):
- Bundle the one sample level, its shaders, and its textures as static wasm assets
  (browser-wasm's native file-preload mechanism), so `File.ReadAllText`/`Image.Load`
  keep working unmodified against the wasm virtual filesystem — no need to rewrite every
  call site for a read-only v1.
- Guard `FileSystemWatcher` construction (`ManaShader.cs`) behind a runtime check
  (`OperatingSystem.IsBrowser()`) since it hot-reload is a dev-time desktop feature with
  no browser equivalent and would likely throw or no-op badly.
- No IndexedDB/writable-storage work needed for v1 since play mode never writes levels,
  thumbnails, or terrain cache (those are editor-only, per `MeshThumbnailCache.cs`/
  `MeshAssetLibrary.cs`/`VoxelEditorGameMode.cs` — all out of scope for play-mode-only).

**M4 — Abstract mesh import behind a common interface, with two interchangeable backends**
Rather than special-casing the browser inline, extract an explicit seam so either
backend can be selected the same way on any platform. Concretely:
- Define a small interface, e.g. `IMeshImportBackend` (`Mana/Code/Shapes/Loaders/`),
  covering what `ManaWorld.LoadFromGltf`/`LoadFromGltfMemory`
  (`Mana/Code/Scenes/ManaWorld.cs:158,177`) actually need from a loader today — import a
  file/byte buffer, produce the engine's existing internal mesh/shape data. This is
  extracted from `ManaGltfLoader.cs`'s current public surface, not redesigned.
- `AssimpMeshImportBackend` wraps the existing `ManaGltfLoader.cs` (`Silk.NET.Assimp`)
  unchanged internally — still the only backend that covers OBJ/FBX/DAE/STL/PLY, and
  still what desktop uses by default.
- `SharpGltfMeshImportBackend` (new) implements the same interface using `SharpGLTF`
  (pure managed, no native/P-Invoke dependency — already referenced as a dead stub in
  `Mana/Code/ImageLoaders/ImageLoader.cs`), covering glTF/GLB only.
- `ManaWorld` takes an `IMeshImportBackend` (constructor/property, defaulting to the
  Assimp backend) instead of hardcoding `ManaGltfLoader` — a composition-root choice, not
  a runtime `OperatingSystem.IsBrowser()` branch buried in loader code.
  `ManaVoxel.Web`'s bootstrap wires `SharpGltfMeshImportBackend`; desktop keeps
  `AssimpMeshImportBackend`. Desktop can also opt into the SharpGLTF backend for testing
  or to sidestep the native Assimp dependency — the abstraction doesn't care which host
  picks which.
- Voxel content (`MagicaVoxLoader.cs`/`ManaRiff.cs`) doesn't go through this interface at
  all today and needs no change.
- If a level references a format the active backend can't handle (e.g. an `.fbx` on the
  SharpGLTF backend), fail with a clear "backend X doesn't support format Y" error rather
  than silently doing nothing — surfaces authoring mistakes instead of a blank mesh.

**M5 — Threading**
Play-mode-only trims this list to just what `VoxelPlayGameMode` actually touches:
- `VoxelPlayGameMode.cs:92` `Task.Run(...)` (background level/environment load on
  startup) — convert to a single-threaded incremental load sequence (a few steps spread
  across frames) since browser wasm has no thread pool by default.
- `ManaWebGpuEnvironmentReflectionMap.cs:490,542` `Parallel.For` (BRDF LUT / prefiltered
  cubemap precompute) — either run single-threaded (accept a longer one-time load stall)
  or spread across frames; not worth pulling in multithreaded wasm (pthreads need
  `SharedArrayBuffer` + COOP/COEP response headers from the host, plus Safari
  restrictions) for v1.
- `ManaVoxelsNode.cs:594` and `ManaGltfLoader.cs:203,998` `Parallel.For` only matter if
  the sample level has >256 disassembled voxel bodies or goes through the new glTF path
  with large meshes — fine to leave as `Parallel.For` (wasm just runs it single-threaded
  when `Environment.ProcessorCount == 1`), no code change needed, just note it as a perf
  cliff to watch, not a correctness issue.
- `MeshThumbnailCache.cs`/`MeshAssetLibrary.cs` background workers are editor-only — not
  in scope for play-mode-only v1.

**M6 — Stray native calls**
`Mana/Code/UI/ManaDpiScale.cs:62-75` calls `Glfw.GetApi()`/`GetMonitorContentScale`
directly, bypassing `IWindow` entirely — needs a browser branch (read
`window.devicePixelRatio` via JS interop, or just default to `1.0` for v1 since play
mode doesn't need precise multi-monitor DPI handling).

**M7 — BepuPhysics wasm verification (spike, do early)**
Could not confirm from research whether BepuPhysics v2.5.0-beta.29 hits any hard
AVX/SSE `System.Runtime.Intrinsics` calls that wasm SIMD can't cover (vs. its
width-agnostic `Vector<T>` "bundle" design, which should be fine). Write a tiny
standalone console test that runs a few physics steps, publish it with
`dotnet publish -r browser-wasm`, and run it in a browser console early — before
integrating physics into `ManaVoxel.Web` — so a hard blocker here is discovered cheaply.

**M8 — Project wiring & hosting**
- New `ManaVoxel.Web.csproj`: `net10.0`, `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>`,
  references `ManaCore`/`Mana`/`ManaGraphics`, but with `Silk.NET.Windowing.Glfw`,
  `Silk.NET.GLFW`, `Silk.NET.WebGPU.Native.WGPU`, and `Silk.NET.Assimp` excluded/replaced
  per the milestones above (likely needs the shared projects' native refs made
  conditional on TFM/RID rather than unconditionally pulled in).
- `dotnet publish -r browser-wasm` produces the deployable `wwwroot`; host it with any
  static file server. No special COOP/COEP headers needed for v1 (single-threaded, no
  `SharedArrayBuffer`) — only becomes a requirement if/when M5's threading gap is
  revisited later.

## Verification

- **M0**: open the published page in Chrome/Edge (stable WebGPU support) and Firefox;
  confirm the canvas clears to the chosen color with no console errors. This is the
  go/no-go gate for the whole WebGPU approach.
- **M1–M2**: extend the M0 spike to draw a static triangle using the real
  `ManaWebGpuContext` pipeline, confirm resize/input events reach `IInputContext`
  correctly (log keydown/mouse move to the JS console).
- **M3–M4**: load the actual bundled sample level end to end in-browser; confirm both a
  voxel grid and at least one glTF-imported mesh render correctly.
- **M5**: confirm the level loads without hitching the browser tab (no `Task.Run`/
  thread-pool exceptions in the console), and that first-frame-to-interactive time is
  acceptable.
- **M7**: standalone Bepu wasm test passes with correct physics results compared to the
  same test run natively (desktop `dotnet run`).
- **Full pass**: `dotnet run --project ManaVoxel -- --play "Levels/Sample Course.manalevel"`
  vs. the browser build of the same level — compare visually and confirm play mode
  (movement, camera) works equivalently.
