# WebGPU renderer resource ownership

Audit: 2026-10-18, following CodeCleanup section 7. This records the current implementation and
proposes the order of later changes. It does not certify shutdown safety. The readback/thumbnail repair below updates those audited boundaries.
Paths below are relative to this file. Symbols are used instead of line numbers so the map survives extraction.

## Ownership map

`GameSilkWebGpu` owns the live `ManaWebGpuContext` and input context. The application owns the scene,
CPU mesh data, window lifecycle and `UiRoot`; the renderer borrows them. `ManaWebGpuGraphicsDevice`
forwards height-sampling requests to the context and does not own or dispose it.

| Resource group | Owner and creation | Release / replacement boundary |
|---|---|---|
| API binding, native extension | Context constructs `_api`; obtains `_wgpuNativeExtension` during `FinishInitialization` | Neither wrapper is explicitly disposed. Their binding/library lifetime needs an explicit decision after all callbacks have stopped. |
| Instance, main surface, adapter, device, queue | Context constructor, `Device` partial, then `FinishInitialization`; browser adapter/device acquisition is asynchronous | Context `Dispose`: queue, device, adapter, surface, instance. Main surface is unconfigured first. Constructor failure has no encompassing rollback. |
| Main depth and handle depth textures/views | Context `Resources`: `ConfigureSurface` / `CreateDepthResources` | Recreated on resize; `ReleaseDepthResources` at shutdown. Views released before textures. |
| Frame uniform buffer/group, object/custom/capture layouts, three samplers | Context `Pipelines` and `Uniforms`; shared by scene meshes and passes | `ReleaseDemoResources`, also called on pipeline-initialization failure. This is renderer-wide cleanup despite its name. |
| Shader modules, material pipelines, outline/capture/overlay pipelines | Context dictionaries and fields in `Pipelines`, `Demo`, `Shadows` | `ReleaseDemoResources`; shader dictionary is redundantly cleared twice. Custom-uniform schemas are managed metadata, not GPU handles. |
| Scene mesh allocations | Nested `ManaWebGpuSceneMesh` owns geometry, optional wireframe/normal-line geometry, object/custom uniform buffers/groups, per-cube-face capture buffers/groups | `Scene`: reconciliation replaces or removes entries; normal retirement is deferred six frame serials. Shutdown and initialization rollback force disposal. This delay is a frame heuristic, not an explicit GPU completion fence. |
| Mesh albedo and six material-map textures/views | `ManaWebGpuMeshTextureCache` owns allocations; scene meshes and thumbnail draws hold acquired references | Last `ReleaseMeshTexture` releases view/texture. No cache-wide disposal; correctness depends on every successful acquire being returned, including failed construction. |
| Environment cube, irradiance, prefiltered cube, BRDF LUT | `ManaWebGpuEnvironmentReflectionMap` owns its texture resources; context owns active and pending map references | Active map disposed on swap and `ReleaseEnvironmentReflectionMap`. Pending/background-created maps have no shutdown collection path. Scene bind groups borrow views; map swap retires scene meshes for rebuilding. |
| Environment prefilter | Context lazily owns `ManaWebGpuEnvironmentPrefilterCompute` | `ReleaseDemoResources` disposes it. Environment background work currently reaches this same instance. |
| Scene-capture depth texture/view | Context `Resources` | Size change and `ReleaseDemoResources`. Capture render passes borrow the map's face views and mesh capture bind groups. |
| Shadow array texture, array/cascade views, sampler, cascade buffers/groups, layout/pipeline | Context `Shadows` | `ReleaseShadowResources`, called by `ReleaseDemoResources`. Mesh object groups also bind shadow resources. |
| HDR scene, bloom and outline targets plus postprocessing pipelines | Context owns `ManaWebGpuHdrPostProcessor`; that object owns its targets and GPU state | Resize replaces targets; `ReleaseDemoResources` disposes processor. Main passes borrow its target views. |
| Main and environment sky renderers | Context owns two `ManaWebGpuSkybox` instances | Context `Dispose` after `ReleaseDemoResources`; internal GPU state belongs to each skybox. |
| Procedural particle renderer | Context lazily owns `ManaWebGpuParticles` | Context `Dispose`; emitter descriptions in `_activeRenderScene` remain application-owned. |
| UI renderer and backdrop | Context owns main `ManaWebGpuUiRenderer`; it owns glyph/image GPU atlases, buffers, pipeline and backdrop resources | Main renderer disposed before scene resources. `UiRoot` and its CPU atlases are borrowed and simply detached. |
| Native panel surfaces | Context tracks `UiSurface`; each owns a surface/configuration and its own UI renderer, borrows shared API/device/queue and panel window | Individual panel close or context `Dispose` before `_disposed` is set. Surface removes itself from context list; its frame temporaries use `finally`. Not compiled for browser. |
| Thumbnail targets and pipeline | Context `Thumbnail`, shared across requests | Recreated for size changes; `ReleaseThumbnailResources` during renderer cleanup. API assumes one caller/request at a time, rather than enforcing serialization. |
| Thumbnail draw allocations | Per-request list owns mesh, one acquired albedo texture, uniform buffer/group | Construction rollback releases partial allocations. After submit, ownership transfers to readback retirement, which disposes draw resources before completing/canceling the task. Continuations only convert CPU pixels. |
| Height-sampling pipeline/module/layouts | Context lazily owns `ManaWebGpuHeightSampleCompute` | Helper implements `Dispose`, but context never invokes it. Per-dispatch input/output buffers and groups are released after submit; staging ownership transfers to readback helper. |
| Readback staging buffers and callback state | Context's `ManaWebGpuReadbackRegistry`, with never-reused integer userdata IDs | Callback or shutdown removes routing, releases staging and associated request resources, then settles the task. Admission and retirement share a per-context gate; late/duplicate IDs are ignored. |
| Screenshot/environment-dump requests | Context `Screenshot` / `EnvironmentDump` hold pending metadata; encoded requests own a staging buffer until mapping | After submit, readback registry owns completion. CPU image conversion/writes need no live GPU objects. A screenshot not yet encoded is outside the registry. |
| Frame-local encoders, passes, command buffers, acquired surface texture/view | `ClearFrame`, demo/environment passes, compute dispatches and panel render methods | Explicit release at end of successful path; coverage of exceptional/early-return paths varies. They are not context fields and cannot be recovered by ordinary context disposal. |

## Partial-file responsibilities

All 16 `ManaWebGpuContext` files participate in this map:

| File | Responsibility |
|---|---|
| `ManaWebGpuContext.cs` | Root handles, construction, frame orchestration, render-thread queue, height-sampling entry point and shutdown |
| `Device.cs`, `BrowserAsync.cs` | Native callbacks versus browser Promise/JSExport initialization |
| `Resources.cs` | Shared cleanup, surface configuration, main/capture depth target lifetimes |
| `Pipelines.cs`, `Uniforms.cs` | Pipeline/module/layout caches, bindings, shared uniforms and managed uniform schemas |
| `Scene.cs` | Mesh reconciliation, construction/rollback, deferred disposal and nested mesh owner |
| `Demo.cs` | Scene draw encoding, outline/overlay draws and pipeline use |
| `Environment.cs` | Active/pending reflection maps, asynchronous photo work, captures and prefilter dispatch |
| `Shadows.cs` | Cascaded-shadow allocations, updates and pass encoding |
| `Particles.cs` | Lazy particle owner and per-frame budget |
| `Thumbnail.cs` | Offscreen targets, temporary draw allocations and asynchronous readback |
| `Screenshot.cs`, `EnvironmentDump.cs` | Frame-copy requests, mapping handoff and CPU image writes |
| `UiSurface.cs` | Native panel presentation on the shared device |
| `Diagnostics.cs` | Diagnostics derived from renderer state; no persistent GPU resource owner |

## Current shutdown sequence

1. `GameSilkWebGpu.Closing` records a close request. If update/render is active, cleanup waits until
   that callback unwinds. Its application closing callback runs first, retiring application work.
2. Context disposes native panel surfaces, then sets `_disposed`.
3. Context drains queued render work. Height/thumbnail entry work checks `_disposed` to cancel.
4. Registry `FaultAllPending` closes admission, removes callback routing, unmaps/releases staging and retires thumbnail draw allocations, then cancels registered tasks. It waits for callback cleanup already holding the registry gate.
5. Deferred scene meshes are force-disposed; `UiRoot` is detached and main UI renderer disposed.
6. `ReleaseDemoResources` releases pipelines, HDR/capture/thumbnail state, prefilter, shared layouts,
   samplers, frame bindings, shadows, resident scene meshes and active environment map.
7. Main skybox, particles and environment skybox are disposed.
8. Main surface is unconfigured; main depth resources released.
9. Queue, device, adapter, surface and instance are released and nulled.
10. Host disposes input. Context does not own input or the borrowed window.

This is the observed order, not a proposed dependency order. Thumbnail continuations now use only CPU
bytes; background environment work is still not joined by this sequence. `ReleaseDemoResources` also serves partial
initialization rollback, so extracting it as if it were exclusively a shutdown function would be incorrect.

## Callback and thread contracts to preserve

- Native adapter/device callbacks are static unmanaged entry points. The code assumes wgpu-native
  answers before the request call returns, then frees the temporary context GCHandle in `finally`.
  An asynchronous native request implementation would require a different ownership contract.
- Browser acquisition uses `ManaBrowserWebGpuCallbackBridge.PendingContext`, one process-wide slot,
  with JSExport results completing adapter/device tasks. Legacy Emscripten request callbacks are bypassed.
  The slot is not cleared on completion/shutdown; there are no disposed-result guards.
- `ClearFrame` pumps maps before surface early returns, using native `DevicePoll(false)` or
  `InstanceProcessEvents`. Background height/thumbnail entry work is queued before submit/present to
  avoid concurrent native submits. Readback task continuations are scheduled asynchronously.
- Native map callbacks receive never-reused IDs as userdata, looked up in a shared request table.
  Removal happens before unmap/release can deliver a late callback. Registry shutdown closes admission
  and serializes with dispatch and callback cleanup, before the context releases its device.
- Screenshot/dump/thumbnail continuations use copied CPU bytes. Thumbnail GPU resources retire inside
  the mapped callback pump or synchronous shutdown, before its task settles. Those native callbacks
  must continue to be pumped on the render thread; task continuations need no live GPU objects.
- Environment map/photo continuations currently create/write resources and submit prefilter work on
  `TaskScheduler.Default`. They do not follow the render-thread admission used by height/thumbnail work.

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
3. **Retire environment work before device teardown.** The map/photo continuations are untracked,
   have no disposed/generation gate and can publish `_pendingEnvironmentMap` after the final frame.
   They can also race replacement of an active map. Separate CPU loading from render-thread GPU
   creation/upload/prefilter; discard stale generations and explicitly dispose pending/abandoned maps.
   Keep activation at the frame boundary before reconciliation, never inside per-mesh creation.
4. **Balance local ownership and pending requests.** Dispose `_heightSampleCompute` before device release.
   Cancel `_pendingScreenshot` even when no frame ever encodes it, reject capture after disposal, and
   define shutdown handling for pending dump metadata. Fix scene-mesh failure branches after
   `AcquireMaterialMaps`: they return albedo but omit the six acquired material-map references.
5. **Specify initialization failure ownership.** Constructor failures can leave root handles allocated;
   browser host assigns `_webGpu` only after asynchronous initialization succeeds. Define cleanup for
   partial construction, request failure and close-during-initialization, including clearing the browser
   bridge slot. Decide ownership of API/native-extension wrappers after native callbacks are retired.
6. **Review transient failure cleanup and shared-resource dependencies.** Several frame/thumbnail/compute
   paths release locals only on success. Use narrowly scoped ownership/`finally` after the callback
   boundary is settled. Keep borrowed environment/shadow views valid for retained mesh bind groups;
   do not replace the six-frame retirement rule with immediate destruction during a structural split.

## Extraction sequence and validation

Readback and thumbnail retirement are repaired. Next land environment admission/retirement, then the
remaining request/construction cleanup. Each needs lifetime tests and live shutdown-during-work checks.
Only then extract services, keeping frame orchestration and the final device lifetime in the context:

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
- [Shared cleanup](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.Resources.cs)
- [Scene ownership](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.Scene.cs)
- [Texture references](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuMeshTextureCache.cs)
- [Readback callback](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuBufferReadback.cs)
- [Readback cancellation](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuReadbackRegistry.cs)
- [Height compute](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuHeightSampleCompute.cs)
- [Thumbnail completion](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.Thumbnail.cs)
- [Environment work](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.Environment.cs)
- [Screenshot requests](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.Screenshot.cs)
- [Browser initialization](../ManaGraphics/Code/Graphics/WebGPU/ManaWebGpuContext.BrowserAsync.cs)
- [Browser callback bridge](../ManaGraphics/Code/Graphics/WebGPU/ManaBrowserWebGpuCallbackBridge.cs)

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
