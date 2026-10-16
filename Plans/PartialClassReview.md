# Partial class review — 2026-10-16

Reviewed all 31 handwritten partial-class families (83 declarations) in the starting checkout. Extracted 21 responsibilities into separate classes; 59 partial declarations remain. Six original classes no longer need `partial`. Public entry points that moved retain forwarding methods where needed.

Partial classes are still object-oriented classes. The criterion here was whether a responsibility could own its state and dependencies independently, rather than whether its source occupied several files. Generated members and alternate platform implementations still require partial declarations.

## Extractions

| Original owner | Separate responsibility |
|---|---|
| `ManaPawnNode` | `ManaPawnAudio`: movement history, footsteps, falling and player sound gating |
| `ManaWorld` | `ManaWorldModelImporter`: glTF loading, timing and scene adoption |
| `MeshAssetLibrary` | `ZipAssetReferences`: portable archive references, encoding, decoding and entry lookup |
| `VoxelFileImportService` | `ZipModelSidecars`: resolving textures directly from an archive |
| `VoxelEditorGameMode` | `GameModePreviewController`: showing, hiding and removing level-mode previews |
| `VoxelEditorGameMode` | `EditorRoom`: room geometry, theme rebuilds and room collider state |
| `VoxelEditorGameMode` | `SelectionColliderOverlay`: collider visualization and transient-node cleanup |
| `VoxelEditorGameMode` | `PolygonEditingController`: polygon gestures, undo and handle overlays |
| `VoxelEditorGameMode` | `MeasureInputController`: measurement gestures, snapping and script input |
| `VoxelEditorGameMode` | `LiveScanCommands`: desktop live-scan automation, previously mixed into measuring |
| `VoxelEditorGameMode` | `ViewportMenuController`: right-click gestures, scene targeting and snap shortcuts |
| `VoxelEditorGameMode` | `GridMapInputController`: painting strokes, brush overlays and palette downloads |
| `VoxelEditorGameMode` | `AssetPlacementController`: preview placement, download cancellation and import-panel drops |
| `UiElement` | `UiElementBindings`: binding ownership, polling, suspension and apply-depth state; lazily allocated |
| `TrainYardWarsMode` | `TywEditorPreview`: preview scene and settings key |
| `TrainYardWarsMode` | `TywBoardCamera`: camera pose, focus, zoom, pan and pointer projection |
| `TywBoardView` | `TywTrackBatchRenderer`: batch ownership and stable render-node replacement |
| `TywGame` | `TywStorage`: storage limits, banking, surplus trading and trimming |
| `TywGame` | `TywResearch`: research eligibility, costs, purchases and landmark scoring |
| `ManaWebGpuContext` | `ManaWebGpuMemoryDiagnostics`: OS memory telemetry, DXGI interop and formatting |
| `ManaWebGpuContext` | `ManaWebGpuDemoScene`: procedural fallback content and normalization |

Removed the unused `partial` modifier from `BaseGameMode`, which had only one declaration. Removed the unused immediate mesh-picker import path after verifying the preview workflow is its replacement and it had no callers.

Controllers receive contexts, models, collections, input flags or focused callbacks; editor controllers do not receive the editor itself. State remains per instance. Existing scene locks, asynchronous cancellation generations, render-node retention and binding detach/re-attach behavior are preserved.

## Retained families

| Family | Decision |
|---|---|
| `ManaWebGpuContext` | Keep the device/frame coordinator partial. Device resources, bind groups, submission, readback and disposal share one lifetime. Rendering already delegates to particle, environment, sky, overlay, UI and post-processing objects. Extracted independent content and OS telemetry. The browser async declaration also deliberately separates async code from unsafe code. |
| `VoxelEditorGameMode` | Keep the remaining main, level and selection declarations: they implement the editor host, session transitions and selection/gizmo orchestration. Feature state has moved to controllers. |
| `TrainYardWarsMode` | Keep input, menus, lobby, couch seats, feedback and command adapters around the same live session. They share seat/menu/session state and dispatch to existing rules and view objects; camera and preview lifetimes are separate now. |
| `TywGame` | Keep track/buildings/claims/combat/train/order mutations within the match aggregate. They coordinate ownership, routes, notices and revision changes. Storage and research policy are separate objects. The small route declarations already delegate to `TywNetwork`. |
| `TywTrain` | Keep cargo docking and routing alongside the train state machine: wagon positions, reservations, path progress and loading/unloading transitions must agree. Splitting these into an object needing the train's private movement state would not improve encapsulation. |
| `UiElement` | Keep routed input with the element: virtual handlers, event senders, focus, capture and tree routing are the element's API. Binding subscription state is now separate. |
| `ManaImageCodec` | Keep platform implementations: browser decoding uses JS interop; desktop decoding uses its native image library. Partial methods select the implementation at build time. |

The other 18 families need their partial declarations for source generation:

| Generator | Families |
|---|---|
| JavaScript interop | `ManaBrowserAudioBackend`, `ManaBrowserAssets`, `ManaBrowserSettingsBridge`, `ManaBrowserEmoji`, `ManaBrowserInterop`, `ManaBrowserWebGpuCallbackBridge`, `ManaBrowserWebGpuNativeContext`, `BrowserGameControls`, `AudioSmoke` |
| Native interop | `ManaUiKeyboardSettings` (`LibraryImport`) |
| Regular expressions | `ManaWgslPreprocessor` (`GeneratedRegex`) |
| Observable properties/commands | `ManaUiShowcaseViewModel`, `ShowcaseLayerViewModel`, `TestSectionViewModel`, `TestCheckViewModel`, `TestRunnerViewModel`, `DemoViewModel`, `LayerViewModel` |

## Verification

- Desktop builds of ManaTests and ManaVoxel pass. The test build reports seven existing nullable warnings in test sources.
- All 3,455 headless checks pass, including audio, model import, archive references, bindings, grid painting, polygon helpers, research, storage and rail batching.
- Editor automation opens Sample Course with zero warnings; saved and fresh snapshots are byte-identical. Theme switches, measurement clicks, play and stop complete. Captures: `Artifacts/Automation/partial-refactor.png`.
- `Tools/Automation/Checks/train-yard-wars.json` completes with joined gamepads, bot play, score logging and an in-app capture: `Artifacts/Automation/partial-refactor-trains.png`. The existing `train-yard-wars-zoom.json` check also completes with camera/pointer logs.
- Release browser AOT publish succeeds. Headless Chrome renders Sample Course play and the editor after the loading overlay disappears, with no reported exceptions or WebGPU validation errors. Captures and console output: `Artifacts/Refactoring/BrowserReady/` and `Artifacts/Refactoring/browser-ready.log`.

No authored levels, prefabs, shaders, native WebGPU descriptors or saved formats changed. macOS and iOS runtime checks were not run on this Windows machine.
