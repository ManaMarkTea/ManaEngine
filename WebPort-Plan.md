# Compiling ManaRenderer to run in the browser (WebAssembly)

## Status (resume-from-here notes)

**Toolchain: done.** `wasm-tools` workload installed (`sudo dotnet workload install
wasm-tools`) and Emscripten installed via `brew install emscripten` (retry needed once
after a wifi drop mid-download — Homebrew resumed from cache fine).

**M0: PASSED.** Both halves of the spike confirmed live in a real browser (Chrome),
tested interactively:
1. A bare `dotnet new console` app with `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>`
   published via `dotnet publish -c Release` and ran in-browser, printing to the devtools
   console. Confirms the .NET wasm-tools pipeline itself works on this machine.
   Gotcha: the boot API is `await dotnet.run()`, not `dotnet.create().run()` — `.run()`
   lives on the `dotnet` builder object itself. Also: after editing `main.js`,
   `dotnet publish` did NOT recopy it into `AppBundle/` (stale incremental-build output) —
   needed `rm -rf bin obj` before republishing to pick up the change. Watch for this
   again during real development.
2. **The critical result**: a hand-written C program using the *old* Emscripten WebGPU
   API (`-sUSE_WEBGPU=1`, `webgpu.h`/`library_webgpu.js`, NOT the newer `emdawnwebgpu`
   port) compiled against the **exact Emscripten build the .NET SDK bundles internally**
   (3.1.56, found at
   `/usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.osx-arm64/`)
   requested a WebGPU device, created a swapchain from a canvas, and cleared it to a
   solid color — rendered correctly in Chrome, no console errors, full success log
   (`device ready` → `swapchain ready` → `frame submitted` → `done`). This is the load-
   bearing finding for the whole "WebGPU in browser" bet: Emscripten 3.1.56's WebGPU
   support genuinely works against a real browser's `navigator.gpu` today.

   To reproduce/extend this spike, from a shell:
   ```
   export DOTNET_EMSCRIPTEN_LLVM_ROOT=/usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.osx-arm64/10.0.11/tools/bin
   export DOTNET_EMSCRIPTEN_NODE_JS=/usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Node.osx-arm64/10.0.11/tools/bin/node
   export DOTNET_EMSCRIPTEN_BINARYEN_ROOT=/usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.osx-arm64/10.0.11/tools
   export EM_CACHE=/usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.osx-arm64/10.0.11/tools/emscripten/cache
   /usr/local/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.osx-arm64/10.0.11/tools/emscripten/emcc \
     clear.c -o clear.html -sUSE_WEBGPU=1 --shell-file shell.html -O0
   ```
   (`(version numbers like 10.0.11/9.0.3 are whatever SDK version pulled the pack in —
   check `/usr/local/share/dotnet/packs/` if these exact paths don't exist anymore.)

   Three bugs hit and fixed along the way (all now fixed in the scratch `clear.c`/
   `shell.html`, which were NOT committed to the repo — they lived in the session's
   scratchpad and should be recreated if needed, see "how to reproduce" above):
   - `em++` (not `emcc`) required for anything using `emdawnwebgpu`/C++ — irrelevant to
     the old API path but relevant if we ever revisit the newer port.
   - The `{{{ SCRIPT }}}` shell-file macro expands to a full `<script src=...>` tag, not
     inline JS — nesting it inside a hand-written `<script>` block produces invalid
     markup (`Unexpected token '<'`). Correct pattern: do the async
     `navigator.gpu.requestAdapter()`/`requestDevice()` dance inside
     `Module.preRun`, gated with `addRunDependency`/`removeRunDependency` so the wasm
     runtime waits for it, then let `{{{ SCRIPT }}}` sit on its own line.
   - `wgpuInstanceCreateSurface(0, ...)` fails an assertion
     (`WGPUInstance must be created by wgpuCreateInstance`) — must call
     `wgpuCreateInstance(NULL)` first and pass its actual return value, not a null/zero
     placeholder.
   - Zero-initializing `WGPURenderPassColorAttachment` leaves `depthSlice = 0`, which a
     newer WebGPU spec revision interprets as "slice 0 of a 3D texture" — triggers a
     validation error against a 2D swapchain texture view. Must explicitly set
     `depthSlice = WGPU_DEPTH_SLICE_UNDEFINED` (`UINT32_MAX`).
   - Also: the SDK's bundled `emcc` isn't runnable standalone without setting
     `DOTNET_EMSCRIPTEN_LLVM_ROOT`/`_NODE_JS`/`_BINARYEN_ROOT` and `EM_CACHE` env vars
     (normally set internally by the MSBuild task) — `BINARYEN_ROOT` should point at the
     `tools` dir, NOT `tools/bin` (emscripten appends `bin/` itself). `EM_CACHE` should
     point at the separate prebuilt `*.Cache.*` pack's `cache` dir since `FROZEN_CACHE`
     is on by default and the SDK pack's own `cache/` dir is empty and root-owned.

**M0 EXTENDED — fully closed the loop, C# calling into native WebGPU works.** Went one
step further than the plan required: got actual C# code (via `[LibraryImport]`) calling
into the native `clear_native.c` shim through a real `dotnet publish -r browser-wasm`
build (not a standalone Emscripten spike), and it rendered into the canvas — full
success log through `native_webgpu_init returned 1` →
`Called native_webgpu_render from C# via LibraryImport`, canvas visibly orange. To
reproduce: a `dotnet new console` app with `<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>`,
`<EmccExtraLDFlags>-sUSE_WEBGPU=1</EmccExtraLDFlags>`, a `<NativeFileReference Include="clear_native.c" />`,
and `<EmccExportedFunction Include="_funcName" />` per function (leading underscore
required — it's the `EXPORTED_FUNCTIONS` convention, stripped again by the time it hits
`wasm-ld`'s `--export=`), calling into it via `[LibraryImport("clear_native")]` (a
partial method on a partial class) from C#, with the WebGPU device requested JS-side via
`dotnet.withModuleConfig({ preRun: [...] })` using `addRunDependency`/
`removeRunDependency` to gate startup on the async `navigator.gpu` calls, exactly as in
the pure-Emscripten spike.

**The one load-bearing gotcha, found the hard way**: the P/Invoke **library name must
match the native source file's basename** (`"clear_native"` for `clear_native.c`), **not**
`"__Internal"`. Using `"__Internal"` compiles fine, the generated
`obj/.../pinvoke-table.h` correctly lists the function under a `{"__Internal", __Internal_imports, 2}`
entry (verified: present, correctly named, correctly pointing at the real C functions),
and yet at runtime it throws `DllNotFoundException` with message literally `"__Internal"`
— no entry-point detail, just the bare library name — every time, with both classic
`[DllImport]` and source-generated `[LibraryImport]`, in both Debug and Release. Root
cause not fully nailed down (didn't chase further once the fix was found: the .NET wasm
docs' own worked example uses the source-file's name as the DllImport target, e.g.
`[DllImport("Test")]` for `Test.c` — `"__Internal"` is apparently reserved for the
runtime's own corebindings, not for user `<NativeFileReference>` code, despite the
generator dutifully building a well-formed table entry for it anyway). **Takeaway for
M1/M2: always `[LibraryImport("<sourceFileBasename>")]`, never `"__Internal"`, for our
own native shim code.**

**New open question for M2, found while chasing the above**: reflecting over the actual
`Silk.NET.WebGPU.dll` (see the `SType`/struct-layout checks above) also revealed that
`Silk.NET.WebGPU.WebGPU.GetApi()` does NOT use compile-time `[DllImport]`/`[LibraryImport]`
at all — its base type is `Silk.NET.Core.Native.NativeAPI`, constructed from an
`INativeContext` built via `WebGPULibraryNameContainer` (confirmed present in the
assembly), which is Silk.NET's own **runtime dynamic-loading** abstraction: it calls
`NativeLibrary.Load`/`TryLoad` against a list of OS-specific candidate filenames (e.g.
`wgpu_native.dll` / `libwgpu_native.dylib` / `libwgpu_native.so`) and resolves each
function via `NativeLibrary.GetExport` against whatever loads. That mechanism has no
separate shared-library file to find in a statically-linked browser-wasm binary — there
is no `libwgpu_native.so` sitting anywhere; the WebGPU functions are exports of the
*same* wasm module the whole app is compiled into. So **Silk.NET.WebGPU's own default
loader almost certainly won't resolve anything on browser-wasm out of the box**, separate
from and in addition to the ABI-numbering question already flagged below (which still
stands — the SType/struct-layout match is good news, but only matters once something
can actually call these functions at all).

The fix is architecturally clear even though unbuilt: `WebGPU` has a public
`.ctor(INativeContext)` — construct it directly with a small hand-written
`INativeContext` for the browser build that resolves function pointers against our own
statically-linked exports (the same `EmccExportedFunction` + per-file `[LibraryImport]`
mechanism just proven above, generalized across the ~dozens of `wgpu*` functions
`ManaWebGpuContext.cs` actually calls — not all ~100+ in the full API surface, just the
subset in use) instead of going through `WebGPULibraryNameContainer`'s file-loading path.
This is real, scoped, buildable work — not a new unknown — and is the concrete next
step before M1/M2's real integration begins: build that custom `INativeContext`, wire
one real `ManaWebGpuContext.cs` call through it (e.g. `wgpuCreateInstance`), and confirm
end to end before porting the rest of the WebGPU backend.

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
