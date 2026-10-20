# WebGPU renderer resource ownership

Audit: 2026-10-18, following CodeCleanup section 7. This records the current implementation and
proposes the order of later changes. It does not certify shutdown safety. The readback/thumbnail and environment repairs below update those audited boundaries.
Paths below are relative to this file. Symbols are used instead of line numbers so the map survives extraction.

## Ownership map

`GameSilkWebGpu` owns the live `ManaWebGpuContext` and input context. The application owns the scene,
CPU mesh data, window lifecycle and `UiRoot`; the renderer borrows them. `ManaWebGpuGraphicsDevice`
forwards height-sampling requests to the context and does not own or dispose it.

| Resource group | Owner and creation | Release / replacement boundary |
|---|---|---|
| API binding, native extension | Context constructs `_api`; obtains `_wgpuNativeExtension` during `FinishInitialization` | Process-lifetime by contract, never disposed: disposing the Silk wrapper frees wgpu_native, which the static `DllImport` log hooks, other contexts/panel surfaces and the embedded iOS framework still use. The context owns only the GPU objects made through it; shutdown drops the extension reference with the device. |
| Instance, main surface, adapter, device, queue | Context constructor, `Device` partial, then `FinishInitialization`; browser adapter/device acquisition is asynchronous | Context `Dispose`: queue, device, adapter, surface, instance. Main surface is unconfigured first. Constructor failures now dispose partial root handles. The browser host takes ownership before awaiting adapter/device initialization, whose failure/cancellation also disposes the partial context. |
| Main depth and handle depth textures/views | Context `Resources`: `ConfigureSurface` / `CreateDepthResources` | Recreated on resize; `ReleaseDepthResources` at shutdown. Views released before textures. |
| Frame uniform buffer/group, object/custom/capture layouts, three samplers | Context `Pipelines` and `Uniforms`; shared by scene meshes and passes | `ReleaseDemoResources`, also called on pipeline-initialization failure. This is renderer-wide cleanup despite its name. |
| Shader modules, material pipelines, outline/capture/overlay pipelines | Context dictionaries and fields in `Pipelines`, `Demo`, `Shadows` | `ReleaseDemoResources`; shader dictionary is redundantly cleared twice. Custom-uniform schemas are managed metadata, not GPU handles. |
| Scene mesh allocations | Nested `ManaWebGpuSceneMesh` owns geometry, optional wireframe/normal-line geometry, object/custom uniform buffers/groups, per-cube-face capture buffers/groups | `Scene`: reconciliation replaces or removes entries; normal retirement is deferred six frame serials. Shutdown and initialization rollback force disposal. This delay is a frame heuristic, not an explicit GPU completion fence. |
| Mesh albedo and six material-map textures/views | `ManaWebGpuMeshTextureCache` owns allocations; scene meshes and thumbnail draws hold acquired references | Last `ReleaseMeshTexture` releases view/texture. No cache-wide disposal; material-map batches roll back partial acquisition and scene construction returns every acquired map on failure. Successful owners still return each reference on disposal. |
| Environment cube, irradiance, prefiltered cube, BRDF LUT | `ManaWebGpuEnvironmentReflectionMap` owns its texture resources; context owns active and pending map references | GPU maps are created only at the render boundary from prepared CPU pixels. Active maps release on swap; pending maps release on cleanup/shutdown. Replacement preserves the visible map until activation, then retires mesh bindings for rebuilding. |
| Environment prefilter | Context lazily owns `ManaWebGpuEnvironmentPrefilterCompute` | `ReleaseDemoResources` disposes it. Creation and all dispatch/submission now stay on the render thread. |
| Scene-capture depth texture/view | Context `Resources` | Size change and `ReleaseDemoResources`. Capture render passes borrow the map's face views and mesh capture bind groups. |
| Shadow array texture, array/cascade views, sampler, cascade buffers/groups, layout/pipeline | Context `Shadows` | `ReleaseShadowResources`, called by `ReleaseDemoResources`. Mesh object groups also bind shadow resources. |
| HDR scene, bloom and outline targets plus postprocessing pipelines | Context owns `ManaWebGpuHdrPostProcessor`; that object owns its targets and GPU state | Resize replaces targets; `ReleaseDemoResources` disposes processor. Main passes borrow its target views. |
| Main and environment sky renderers | Context owns two `ManaWebGpuSkybox` instances | Context `Dispose` after `ReleaseDemoResources`; internal GPU state belongs to each skybox. |
| Procedural particle renderer | Context lazily owns `ManaWebGpuParticles` | Context `Dispose`; emitter descriptions in `_activeRenderScene` remain application-owned. |
| UI renderer and backdrop | Context owns main `ManaWebGpuUiRenderer`; it owns glyph/image GPU atlases, buffers, pipeline and backdrop resources | Main renderer disposed before scene resources. `UiRoot` and its CPU atlases are borrowed and simply detached. |
| Native panel surfaces | Context tracks `UiSurface`; each owns a surface/configuration and its own UI renderer, borrows shared API/device/queue and panel window | Individual panel close or context `Dispose` before `_disposed` is set. Surface removes itself from context list; its frame temporaries use `finally`. Not compiled for browser. |
| Thumbnail targets and pipeline | Context `Thumbnail`, shared across requests | Recreated for size changes; `ReleaseThumbnailResources` during renderer cleanup. API assumes one caller/request at a time, rather than enforcing serialization. |
| Thumbnail draw allocations | Per-request list owns mesh, one acquired albedo texture, uniform buffer/group | Construction rollback releases partial allocations. After submit, ownership transfers to readback retirement, which disposes draw resources before completing/canceling the task. Continuations only convert CPU pixels. |
| Environment CPU jobs | `ManaWebGpuEnvironmentLoad<T>` owns cancellation, generation-tagged result/progress publication and CPU tasks; prepared photo/LUT arrays carry no native handles | Source replacement invalidates old results; retirement cancels preparation/download waits and clears published pixels/progress. Shared downloads finish independently. |
| Height-sampling pipeline/module/layouts | Context lazily owns `ManaWebGpuHeightSampleCompute` | Context disposes the helper after readback retirement and before device release; repeated helper disposal is harmless. Constructor failure and per-dispatch locals unwind through cleanup. Staging ownership transfers to readback helper. |
| Readback staging buffers and callback state | Context's `ManaWebGpuReadbackRegistry`, with never-reused integer userdata IDs | Callback or shutdown removes routing, releases staging and associated request resources, then settles the task. Admission and retirement share a per-context gate; late/duplicate IDs are ignored. |
| Screenshot/environment-dump requests | Context `Screenshot` / `EnvironmentDump` hold pending metadata; encoded requests own a staging buffer until mapping | After submit, readback registry owns completion. CPU image conversion/writes need no live GPU objects. Unencoded screenshot tasks are canceled and pending dump metadata cleared at shutdown. Disposed contexts reject new requests; encoded readbacks remain registry-owned. |
| Frame-local encoders, passes, command buffers, acquired surface texture/view | `ClearFrame`, demo/environment passes, compute dispatches and panel render methods | Released in `finally` wherever managed code runs inside them: `ClearFrame` owns its encoder, passes, command buffer and surface texture/view, and a frame abandoned before submit releases its screenshot/dump staging and faults the screenshot. Passes that only record native calls release inline. `FramePassOwnershipChecks` requires a release for every begun pass. |

## Owners after extraction (2026-10-19)

The context's partial files were split into owners; the context keeps the device, the frame orchestration
(`ClearFrame`), the render-thread queue, the public request APIs and shutdown ordering.

| Owner | Responsibility |
|---|---|
| `ManaWebGpuContext.cs` | Root handles, construction, the frame skeleton (acquire, scene, tone map and UI, capture, submit, present) and shutdown order |
| `ManaWebGpuWindowSurface` | Surface configuration, scene and handle depth targets, acquire and present |
| `ManaWebGpuSceneRenderer` | Shadow map, scene bindings, HDR post and the registered passes; frame uniform and the scene half of a frame |
| `ManaWebGpuNativeDevice` | Instance, window surface, adapter, device and queue; desktop native request callbacks, the browser's promise requests and late-result rejection, the callback pump, release in reverse order |
| `ManaWebGpuContext.Diagnostics.cs`, `.ResourceReport.cs` | Diagnostics derived from renderer state; no GPU resource owner |
| `ManaWebGpuDevice` | Borrowed API/device/queue/formats, shader loading, asset paths |
| `ManaWebGpuSceneBindings` | Samplers, group layouts, frame uniform buffer/group, pipeline layouts |
| `ManaWebGpuRenderPass` subclasses | Environment capture, shadows, sky, materials, particles, wireframe/normal overlays, outline: each owns its pipelines and frame-local passes |
| `ManaWebGpuShadowMap`, `ManaWebGpuEnvironment` | Shadow texture/views/cascades; reflection cubes, loading, activation and prefiltering |
| `ManaWebGpuSceneMeshes`, `ManaWebGpuSceneMesh` | Resident set, upload budget, six-frame retirement; per-mesh geometry and bindings |
| `ManaWebGpuThumbnailRenderer`, `ManaWebGpuFrameCapture` | Offscreen thumbnails; screenshot/environment-dump copies and their readback handoff |
| `ManaWebGpuUiCompositor`, `ManaWebGpuPanelSurface` | ManaUI renderer and blur backdrop; native panel presentation on the shared device |

Release order in `ReleaseScenePipelines`: thumbnails and HDR post, resident meshes (they bind the views below),
passes (reverse registration order), shared bindings, shadow map, environment. Context `Dispose` keeps the
sequence below, with these owners standing in for the former partial-file members.

## Current shutdown sequence

1. `GameSilkWebGpu.Closing` records a close request. If update/render is active, cleanup waits until
   that callback unwinds. Its application closing callback runs first, retiring application work.
2. Context disposes native panel surfaces, sets `_disposed`, cancels unencoded screenshots/browser initialization and clears dump metadata, then retires environment CPU admission/results.
3. Context drains queued render work. Height/thumbnail entry work checks `_disposed` to cancel.
4. Registry `FaultAllPending` closes admission, removes callback routing, unmaps/releases staging and retires thumbnail draw allocations, then cancels registered tasks. It waits for callback cleanup already holding the registry gate. The height-sampling helper is then disposed while the device is still live.
5. Deferred scene meshes are force-disposed; `UiRoot` is detached and main UI renderer disposed.
6. `ReleaseDemoResources` releases pipelines, HDR/capture/thumbnail state, prefilter, shared layouts,
   samplers, frame bindings, shadows, resident scene meshes, pending environment work/maps and the active environment map.
7. Main skybox, particles and environment skybox are disposed.
8. Main surface is unconfigured; main depth resources released.
9. Queue, device, adapter, surface and instance are released and nulled.
10. Host disposes input. Context does not own input or the borrowed window.

This is the observed order, not a proposed dependency order. Thumbnail continuations and environment jobs now use only CPU
data. Environment jobs are canceled/invalidated rather than joined; they cannot create native resources
or publish results/progress after retirement. `ReleaseDemoResources` also serves partial
initialization rollback, so extracting it as if it were exclusively a shutdown function would be incorrect.

## Callback and thread contracts to preserve

- Native adapter/device callbacks are static unmanaged entry points. The code assumes wgpu-native
  answers before the request call returns, then frees the temporary context GCHandle in `finally`.
  An asynchronous native request implementation would require a different ownership contract.
- Browser acquisition uses never-reused request IDs, with JSExport results completing only the matching
  adapter/device task. Completion/cancellation removes routing; disposed owners reject results. JS releases
  rejected adapter IDs and destroys/releases rejected devices and their queue wrappers. The C device-release
  shim also balances the SDK manager's default-queue reference on the last device release. Legacy Emscripten
  native request callbacks remain bypassed. Browser initialization/close run on the JS/render thread.
- `ClearFrame` pumps maps before surface early returns, using native `DevicePoll(false)` or
  `InstanceProcessEvents`. Background height/thumbnail entry work is queued before submit/present to
  avoid concurrent native submits. Readback task continuations are scheduled asynchronously.
- Native map callbacks receive never-reused IDs as userdata, looked up in a shared request table.
  Removal happens before unmap/release can deliver a late callback. Registry shutdown closes admission
  and serializes with dispatch and callback cleanup, before the context releases its device.
- Screenshot/dump/thumbnail continuations use copied CPU bytes. Thumbnail GPU resources retire inside
  the mapped callback pump or synchronous shutdown, before its task settles. Those native callbacks
  must continue to be pumped on the render thread; task continuations need no live GPU objects.
- Environment preparation decodes photos, converts cube faces and generates LUT rows on CPU workers.
  Cancellation is checked before decoding, between cube faces and by parallel row loops. The context
  consumes generation-tagged results/progress in `CheckPendingEnvironmentMap`, before reconciliation;
  GPU texture creation, upload, prefilter submission and map activation all run there on the render thread.

## Findings and ordered follow-ups

These are source findings. No native failure was reproduced during this documentation-only audit.

1. **Completed: safe readback callback userdata and staging retirement (2026-10-18).**
   Replaced GCHandles with never-reused IDs (bounded to positive 32-bit values for native/browser
   compatibility; exhaustion rejects new work). Per-context admission/retirement gates serialize
   dispatch, callback cleanup and shutdown. Retirement removes routing before releasing staging;
   callbacks carrying retired IDs do nothing. Staging is explicitly unmapped/released before device
   teardown, with no assumption of device-cascaded release of caller references.
2. **Completed: thumbnail request cleanup (2026-10-18).** Submitted draw allocations retire with
   their readback, before task completion/cancellation. CPU-only continuations cannot enqueue GPU
   cleanup after shutdown. Partial construction and transient encoders/passes/buffers use `finally`;
   mesh/texture/uniform/group ownership is recorded as each allocation succeeds.
3. **Completed: environment admission and retirement (2026-10-18).**
   Split CPU photo/LUT preparation from GPU allocation/upload. Per-context generation ownership drops
   superseded results, late errors and progress; cancellation stops local preparation/download waits
   without canceling shared catalog downloads. The render-boundary drain alone creates native maps
   and submits prefilter work. Source changes keep the previous visible map until activation. Photo
   updates use the active texture size and can run again after computed sky replaces the photo.
   Shutdown retires CPU admission before teardown and explicitly disposes pending/active GPU maps;
   failed full-map construction or prefilter publication rolls back candidate allocations. A CPU job
   that is still unwinding holds only managed preparation state, with no renderer/device callback.
4. **Completed: local ownership and pending requests (2026-10-18).** Context shutdown cancels
   unencoded screenshots, clears dump metadata and disposes height sampling after its readbacks retire.
   Captures after disposal return cancellation; zero-sized/failed encoding settles the request. Scene
   construction transfers ownership only on success; its single `finally` returns albedo, all six
   material-map references, uniform/capture bindings and geometry on failure. Batch acquisition returns
   each earlier reference even when several bindings share a neutral texture. Dummy environment and
   height-compute construction now roll back partial allocations; height-dispatch locals use `finally`.
5. **Completed for native handles: initialization failure ownership (2026-10-18).** Constructor
   failure releases partial root handles. The browser host stores the context before awaiting startup;
   failed/canceled initialization disposes it. Stable request IDs replace the shared current-context
   slot, and disposed-result guards plus JS rejection cleanup stop late handles from reaching a fresh
   renderer. Native adapter/device callback lifetime assumptions are unchanged. The API/native-extension
   binding wrappers are deliberately never disposed (finding 6): releasing GPU objects does not make
   unloading their native library safe.
6. **Completed: frame-local passes and binding wrappers (2026-10-19).** Three passes were ended but never
   released on the ordinary path: every shadow cascade pass, every backdrop reduce level and the first
   tone-map pass when blur splits it -- native handles leaked each frame. `ClearFrame`, shadow, sky/capture
   face, outline and prefilter dispatch locals now unwind through `finally`; prefilter parameter buffers and
   bind groups share one owner. The API/native-extension wrappers are process-lifetime (row above).
   Borrowed environment/shadow views stay valid for retained mesh bind groups; the six-frame retirement rule
   is unchanged.

## Native binding-wrapper ownership contract

The Silk.NET wrappers are not GPU objects: they hold the loaded native library and its entry points, so
releasing every GPU handle says nothing about whether the library may be unloaded. Decisions (2026-10-18; the callback-after-release rationale below is the intent, not something
a test here proves):

- **`WebGPU _api`** is created once per `ManaWebGpuContext` (`WebGPU.GetApi()`, the host's `CreateApi()` on iOS/Android, or the
  browser's `ManaBrowserWebGpuNativeContext`) and is **never disposed by the context**. The native library therefore lives for the
  process, on purpose: wgpu-native can still deliver a map/device callback after the device is released, and unloading the library
  under it would turn that into a crash. A context that is rebuilt (fresh-context reopen, injected-failure rollback) makes a new wrapper
  over the same already-loaded library, which is what the native smoke harnesses exercise.
- **`Wgpu? _wgpuNativeExtension`** (desktop/mobile poll extension from `TryGetDeviceExtension`) is a borrowed view of the same library:
  no release. It is left set after `Dispose`; `ClearFrame` has no `_disposed` guard and still polls through it first thing, so the
  safety of a frame after disposal rests on `GameSilkWebGpu` dropping its context reference and letting any active frame unwind before
  `Dispose` (not verified by a test). A `_disposed` guard at the top of `ClearFrame` is the cheap follow-up if that ever changes.
- **`IManaWebGpuNativeHost.CreateApi()`** hands the context a wrapper the host does not keep: the context is the only holder, and
  a host that wants an unload contract (none does today) must own that decision itself, with its callbacks quiesced first.
- **Revisit** only with a native host that needs the library unloaded (hot restart, plugin reload). The prerequisites are no registered
  readbacks (`_readbackRegistry` empty after `FaultAllPending`), no pending adapter/device request (browser: request IDs retired), and
  a device that has finished its last `DevicePoll`/`InstanceProcessEvents`.

### Frame-local exception paths

Settled by finding 6 above: the main frame and every sub-pass (shadow cascades, sky/capture faces, outline mask, prefilter dispatch)
release in a `finally`, and a source check requires a release for each begun pass. Pop-out surfaces (`UiSurface`) already did. The
earlier decision here to leave sub-passes releasing at their own end was superseded the next day, once the same leak turned up in the
shadow and backdrop passes.

## Detecting leaks on purpose

`gpu_report` (automation command; `ManaGpuResourceReport`, `ManaWebGpuContext.ResourceReport.cs`) reads wgpu-native's own registry
through `GenerateReport`: per kind, what the app still holds and what the app released but the driver keeps alive. Run it twice around a
still scene (`Tools/Automation/Checks/gpu-leak.json`); any kind that climbs is a leak. Baseline for the Sample Course with the Blur Test
panel open: 3,255 buffers, 2,613 bind groups, 119 texture views, 27 textures, flat over 1,200 frames.

Limits, found by test: with the old backdrop-blur pass leak put back, the report read "unchanged". It counts wgpu-core objects (buffers,
textures, views, bind groups, pipelines, command buffers), not the thin wrapper handles around passes and encoders, so that class of leak
is still found by review only. The browser's WebGPU cannot list what it holds, so there is no equivalent there.

## Extraction sequence and validation

Readback, thumbnail, environment, local native-handle and frame-local pass retirement are repaired, and
binding-wrapper ownership is specified. Each extraction still needs lifetime tests and live shutdown-during-work checks.
Extract services, keeping frame orchestration and the final device lifetime in the context:

- Surface/depth targets, with main/panel surface differences explicit.
- Pipeline/shared-binding owner, preserving initialization rollback and format-specific cache keys.
- Scene residency owner, including deferred mesh disposal and texture reference accounting.
- Environment controller, once it owns pending generations and render-thread handoffs.
- Capture/thumbnail service, after its per-request ownership can outlive task cancellation safely.

For runtime changes, run relevant headless suites plus desktop in-app rendering checks (shadows,
reflections, thumbnail previews, screenshots, panel pop-outs, resize/minimize, and quit during active
readback/environment loading). Use existing fixtures in `Tools/Automation/Checks/` where applicable.
Publish browser Release/AOT and drive Chrome for loading, shadows/reflections, UI and capture paths
that are exposed there; inspect browser validation output. Browser-only initialization cancellation
needs a browser fixture. Planet loading remains a desktop fixture because Planet Showcase is excluded
from the browser selector. iOS callback entry points must remain compatible with ahead-of-time compilation.

Audit validation: traced the creation/release sites and caller/continuation paths listed above; checked
all partial files are represented and documentation links resolve. No production edits, build, GPU run
or new tests were needed for this written map. Desktop/browser checks remain required for later fixes.

## Source entry points

- [Context and shutdown](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.cs)
- [Host lifetime](../ManaGraphics/Code/Graphics/WebGPU/GameSilkWebGpu.cs)
- [Device object base](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuDeviceObject.cs)
- [Render pass base](../ManaGraphics/Code/Graphics/WebGPU/Pipelines/ManaWebGpuRenderPass.cs)
- [Scene meshes](../ManaGraphics/Code/Graphics/WebGPU/Resources/ManaWebGpuSceneMeshes.cs)
- [Texture references](../ManaGraphics/Code/Graphics/WebGPU/Resources/ManaWebGpuMeshTextureCache.cs)
- [Readback callback](../ManaGraphics/Code/Graphics/WebGPU/Resources/ManaWebGpuBufferReadback.cs)
- [Readback cancellation](../ManaGraphics/Code/Graphics/WebGPU/Resources/ManaWebGpuReadbackRegistry.cs)
- [Height compute](../ManaGraphics/Code/Graphics/WebGPU/Renderers/ManaWebGpuHeightSampleCompute.cs)
- [Thumbnails](../ManaGraphics/Code/Graphics/WebGPU/Renderers/ManaWebGpuThumbnailRenderer.cs)
- [Environment](../ManaGraphics/Code/Graphics/WebGPU/Resources/ManaWebGpuEnvironment.cs)
- [Frame captures](../ManaGraphics/Code/Graphics/WebGPU/Renderers/ManaWebGpuFrameCapture.cs)
- [Native device and browser initialization](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuNativeDevice.cs)
- [Browser callback bridge](../ManaGraphics/Code/Graphics/Browser/ManaBrowserWebGpuCallbackBridge.cs)
- [Initialization request routing](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuInitializationRequests.cs)
- [Partial acquisition rollback](../ManaGraphics/Code/Graphics/WebGPU/Resources/ManaWebGpuAcquisition.cs)
- [Browser native shim](../ManaGraphics/Native/mana_webgpu.c)

Readback/thumbnail repair validation (2026-10-18): all 3,523 headless checks passed (11 new callback
lifetime checks, focused Graphics run 57 checks). Desktop build had zero warnings. A native Metal
smoke harness under `Artifacts/Scratch/ReadbackSmoke` verified padded-row thumbnail pixels, injected
failure after the first draw allocation, successful cleanup, a submitted map pending at shutdown,
queued-work cancellation and rejection after disposal; registry/texture counts returned to zero.
Desktop reflection captures and six-face dumps completed; images/logs use `Artifacts/Automation/readback-*`.
Browser Release/AOT publish with SDK 10.0.300 and Chrome editor/resize and 2D Sample play/input checks
passed without logged exceptions or WebGPU validation errors (`Artifacts/WebDrive/readback-*`). Browser
checks verify rendering compatibility; the deterministic pending-readback cancellation harness ran on
native Metal. User workspace settings were restored and byte-compared.
iOS Compile passed with zero warnings using SDK 10.0.300; no phone deployment was performed.

Environment lifetime repair validation (2026-10-18): all 3,538 headless checks passed in 28.2s,
including 15 new generation/cancellation and CPU-pixel checks (focused Graphics: 72 checks in 0.2s).
The native Metal harness in `Artifacts/Scratch/EnvironmentSmoke` verified CPU-only preparation,
render-boundary activation, visible-map retention, source-size replacement, repeat photo uploads,
disposal with both a pending GPU map and CPU work, and fresh-context initialization. Desktop build
and iOS Compile passed with zero warnings; no phone deployment. In-app Photo/Day/Photo captures and
six-face dumps were inspected using `Tools/Automation/Checks/environment-lifetime.json`; artifacts
use `Artifacts/Automation/environment-*`. Browser Release/AOT (SDK 10.0.300) and Chrome editor/resize
and 2D Sample play/input checks passed without logged exceptions or validation errors; captures/logs
are in `Artifacts/WebDrive/environment-*`. User workspace settings were restored and byte-compared.

Local ownership repair validation (2026-10-18): 3,550 headless checks passed (12 new rollback/request
routing checks; focused Graphics 84). Native Metal smoke in `Artifacts/Scratch/LocalOwnershipSmoke`
counted one instance release after an injected surface-creation failure, verified zero residual texture
references after a late material-data failure, rebuilt/disposed a valid mesh, faulted a zero-sized
capture, canceled an unencoded screenshot and retired a height readback/pipeline. Desktop captures
and cube-face dumps use `Artifacts/Automation/local-ownership*`. Browser AOT publish (SDK 10.0.300),
editor/resize and play/input checks passed; injected adapter/device failures rejected late deliveries
and reached the error overlay without unhandled runtime exceptions (`Artifacts/WebDrive/local-*`).
Desktop build and iOS Compile passed with zero warnings; no phone deployment. Workspace settings
were restored and byte-compared. Native binding-wrapper unload/lifetime and remaining main-frame/pass
exception paths are still open; this step does not claim those boundaries are fully settled.
