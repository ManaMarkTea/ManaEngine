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

**RESOLVED — proven end to end with the real, unmodified Silk.NET.WebGPU package.**
`WebGPU` has a public `.ctor(INativeContext)`. Built a custom `INativeContext` and
confirmed `new WebGPU(context).CreateInstance(null)` — real Silk.NET.WebGPU code, the
exact NuGet package `ManaGraphics` already depends on, no changes to Silk.NET itself —
returns a valid instance handle in-browser. Full working recipe, all four pieces
required (each one silently fails/no-ops if missing, so all four matter):

1. **A one-line forwarding shim per `wgpu*` function needed**, in a
   `<NativeFileReference>`'d `.c` file (`mana_webgpu.c`):
   ```c
   #include <webgpu/webgpu.h>
   WGPUInstance mana_wgpuCreateInstance(WGPUInstanceDescriptor const* d) { return wgpuCreateInstance(d); }
   ```
   Confirmed necessary — tried skipping this and targeting the *real* `wgpuCreateInstance`
   symbol directly via `[LibraryImport("clear_native", EntryPoint = "wgpuCreateInstance")]`
   (no separate wrapper), reusing an existing `NativeFileReference`'d file as the
   "bucket." Compiled and linked with zero errors, but the table generator silently
   never created an entry for it — it appears to require the symbol be *defined* inside
   the referenced `.c` file itself, not just reachable at final link time from some
   other already-linked library (Emscripten's own `-lwebgpu`, in this case). So: yes to
   a real per-function forwarding wrapper, no shortcut around writing one.

2. **A "registration manifest"**: one `[LibraryImport("mana_webgpu")]` C# declaration
   per shim function, e.g. `static partial nint mana_wgpuCreateInstance(nint descriptor);`
   — this is what makes `PInvokeTableGenerator` emit the entry in
   `obj/.../pinvoke-table.h` at all (confirmed by testing an invented library name with
   no matching `NativeFileReference` — the declaration is silently dropped, no build
   error, just a runtime failure later). **Gotcha**: the declared method must be
   genuinely *reachable* in the compiled IL, or the trimmer strips it before the table
   generator ever sees it — confirmed by watching `mana_webgpu_imports[]` go from
   `count: 0` to `count: 1` purely by adding a call site (even one gated behind a
   condition that's always false at runtime, e.g. `if (args.Length > 1000) { ... }` —
   reachability is a static IL-call-graph check, not real runtime control flow). The
   registration method is never actually *invoked* through this declaration in the real
   design (see next point) — it exists purely so its P/Invoke metadata survives into the
   table. For the real implementation this needs `[DynamicDependency]` (or an ILLink
   root descriptor) on each registration method instead of a throwaway reachable call
   site, to preserve them from trimming without the hack.

3. **`BrowserWasmNativeContext : INativeContext`** — resolves Silk.NET's requested C
   name (e.g. `"wgpuCreateInstance"`) to the shim's exported name
   (`"mana_wgpuCreateInstance"`) via a lookup table, then dynamically to a real function
   pointer via `NativeLibrary.Load("mana_webgpu")` (once, cached) +
   `NativeLibrary.TryGetExport(handle, shimName, out addr)`. This dynamic-resolution
   path is a *different* mechanism from #2's static compile-time `[LibraryImport]` path
   — confirmed both work, but independently; `INativeContext` needs this one specifically
   since Silk.NET picks names at runtime, not compile time.

4. **Construct the API with it**: `new WebGPU(new BrowserWasmNativeContext())` instead
   of `WebGPU.GetApi()`, only on the browser build. Nothing else about
   `ManaGraphics/Code/Graphics/WebGPU/*.cs` needs to change — every existing `_api.Xxx(...)`
   call site and every Silk.NET.WebGPU struct type (`Instance*`, `Device*`,
   `SurfaceDescriptor`, etc.) keeps working completely unmodified. Only the API
   construction/bootstrap differs per target — this is the main reason the
   `INativeContext` approach (vs. hand-writing a parallel binding surface) is worth the
   extra plumbing: it avoids touching the whole existing WebGPU rendering codebase.

**Remaining mechanical work for real M2** (no longer research — just execution):
generate one forwarding-shim line + one registration declaration + one lookup-table
entry per function (fully mechanical/scriptable — same three-line pattern repeated), and
swap `[DynamicDependency]` in for the throwaway reachable-call hack. The `SType`/
struct-layout ABI match already confirmed earlier means the generated shim signatures
should be straightforward pointer-for-pointer forwards with no marshaling surprises.

**Exact scope, already enumerated** — grepped every `_api.`/`api.` call site across
`ManaGraphics/Code/Graphics/WebGPU/*.cs` (confirmed no WebGPU calls exist anywhere
outside that folder): **69 distinct `wgpu*` functions** need the shim/registration/
lookup-table triad. Naming is a mechanical 1:1 map, spot-checked via reflection
(`CreateInstance`→`wgpuCreateInstance`, `DeviceCreateBuffer`→`wgpuDeviceCreateBuffer`,
`SurfaceGetPreferredFormat`→`wgpuSurfaceGetPreferredFormat`, etc. — strip nothing, just
prepend `wgpu`). Full list:

```
AdapterEnumerateFeatures, AdapterGetLimits, AdapterGetProperties, AdapterRelease,
AdapterRequestDevice, BindGroupLayoutRelease, BindGroupRelease, BufferDestroy,
BufferMapAsync, BufferRelease, CommandBufferRelease, CommandEncoderBeginComputePass,
CommandEncoderBeginRenderPass, CommandEncoderCopyBufferToBuffer,
CommandEncoderCopyTextureToTexture, CommandEncoderFinish, CommandEncoderRelease,
ComputePassEncoderDispatchWorkgroups, ComputePassEncoderEnd, ComputePassEncoderRelease,
ComputePassEncoderSetBindGroup, ComputePassEncoderSetPipeline, ComputePipelineRelease,
CreateInstance, DeviceCreateBindGroup, DeviceCreateBindGroupLayout, DeviceCreateBuffer,
DeviceCreateCommandEncoder, DeviceCreateComputePipeline, DeviceCreatePipelineLayout,
DeviceCreateRenderPipeline, DeviceCreateSampler, DeviceCreateShaderModule,
DeviceCreateTexture, DeviceEnumerateFeatures, DeviceGetLimits, DeviceGetQueue,
DeviceRelease, DeviceSetUncapturedErrorCallback, InstanceProcessEvents, InstanceRelease,
InstanceRequestAdapter, PipelineLayoutRelease, QueueRelease, QueueSubmit,
QueueWriteBuffer, QueueWriteTexture, RenderPassEncoderDraw, RenderPassEncoderDrawIndexed,
RenderPassEncoderEnd, RenderPassEncoderRelease, RenderPassEncoderSetBindGroup,
RenderPassEncoderSetIndexBuffer, RenderPassEncoderSetPipeline,
RenderPassEncoderSetScissorRect, RenderPassEncoderSetVertexBuffer,
RenderPipelineRelease, SamplerRelease, ShaderModuleRelease, SurfaceConfigure,
SurfaceGetCurrentTexture, SurfaceGetPreferredFormat, SurfacePresent, SurfaceRelease,
SurfaceUnconfigure, TextureCreateView, TextureDestroy, TextureRelease, TextureViewRelease
```

**One excluded call site, needs its own handling, not a shim**: `TryGetDeviceExtension<T>`
(used somewhere in the 68/70 raw grep hits) is a Silk.NET-only generic helper — reflection
confirms its signature is `bool TryGetDeviceExtension<T>(Device*, out T)`, not a real
`wgpu*` C function. It's how desktop code reaches `Silk.NET.WebGPU.Extensions.WGPU`'s
device extension API, which is wgpu-native-specific tooling already excluded from the
browser build entirely — whatever call site uses it needs a browser-specific branch
(likely just skip/no-op) rather than a forwarding shim.

**One thing to verify when actually writing shims, not yet checked**: `AdapterRequestDevice`
and `InstanceRequestAdapter` are async, callback-based functions in the WebGPU API (they
take a callback function pointer as a parameter rather than returning synchronously).
Haven't yet confirmed the old Emscripten `library_webgpu.js` callback ABI matches what
Silk.NET.WebGPU's generated delegate/marshaling code expects for these two — this is
exactly the kind of subtle mismatch that could fail silently (wrong callback signature →
garbage data or a crash deep in a callback trampoline) rather than at the call site
itself, so these two deserve a dedicated small test before assuming the mechanical
pattern just works for them the same way it did for the 67 synchronous functions.

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
The core mechanism — getting Silk.NET.WebGPU's real, unmodified API to call real
Emscripten WebGPU functions in a browser-wasm build — is now fully proven (see the M0
EXTENDED section above: a working `BrowserWasmNativeContext : INativeContext` +
per-function forwarding shim + registration-manifest pattern, tested end to end with
`wgpuCreateInstance`). What's left for M2 specifically:
- Generate the shim/registration/lookup-table triad for every `wgpu*` function
  `ManaGraphics/Code/Graphics/WebGPU/*.cs` actually calls (mechanical, see the checklist
  above) instead of `Silk.NET.WebGPU.Native.WGPU` (the desktop `wgpu-native` binary,
  excluded entirely on the browser TFM/RID).
- Get `WebGPUSurface.CreateWebGPUSurface` in
  `ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.cs:201` working against the M1
  canvas window — likely needs a browser-specific surface-creation path since that
  helper currently pulls a native platform window handle (HWND/NSWindow/X11) that
  doesn't exist for a canvas; the proven `WGPUSurfaceDescriptorFromCanvasHTMLSelector`
  chain from the M0 spike is the browser equivalent to wire in here.

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
