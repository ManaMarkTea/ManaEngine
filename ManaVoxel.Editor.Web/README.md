# ManaVoxel.Editor.Web — the browser build of the editor

The real editor — `VoxelEditorGameMode`, not a cut-down stand-in — running through WebGPU in a
browser tab: menu bar, docked panels, viewport, asset browser, undo/redo, the works.

See `WebPort-Plan.md` at the repo root (M9) for the porting history and the reasoning behind the
browser-specific workarounds. See `ManaVoxel.Web/README.md` for the sibling project (play mode
only) this one otherwise mirrors closely.

## Build and run

```bash
# 1. Publish (AOT; takes a few minutes)
dotnet publish ManaVoxel.Editor.Web/ManaVoxel.Editor.Web.csproj -c Release

# 2. Serve
python3 ManaVoxel.Editor.Web/serve.py          # http://localhost:8002  (or pass a port)
```

Then open <http://localhost:8002>. The startup download contains the runtime, shaders, two UI
fonts, the default textures, the player character pack, and level definitions. The rest of the
library is fetched when a model is selected or a level needs it.

## Deployment and assets

Publish requires Python 3.11+ (`python` on PATH; override with `-p:ManaPython=python3`).
`WebAssets.props` defines the shared boot assets. `tools/WebAssets/publish_assets.py` generates:

- `asset-catalog.json`: file sizes, hashes, model/ZIP entries, and glTF sidecar dependencies.
- `AssetLibrary/`: individual content-hashed files, downloaded only as needed.

For local serving, upload or serve the **entire AppBundle directory** as one unit:

```text
ManaVoxel.Editor.Web/bin/Release/net10.0/browser-wasm/AppBundle/
```

itch.io is different: it caps a browser-embedded ("played in the browser") build's *extracted*
size at 500MB, and the asset library alone is already well past that. The CI workflow runs
`tools/WebAssets/split_for_itch.py` right after publish, which moves `AssetLibrary/` out to a
separate `pages-site/` tree and rewrites the itch-bound copy of `asset-catalog.json` so its file
URLs are absolute, pointing at GitHub Pages instead of the (now locally missing) `AssetLibrary/`.
`pages-site/` is deployed to GitHub Pages via `actions/deploy-pages`; the now-much-smaller
remainder of `AppBundle/` (runtime, HTML, the rewritten catalog) goes to itch.io's Butler
uploader as before. Neither `asset-downloads.js` nor `ManaAssetCatalog` needed to change for
this: a blob URL is resolved (`new URL(url, import.meta.url)`) and validated
(`ManaAssetCatalog.ValidateUrl`) the same way whether it is relative or absolute -- the catalog
just says a different thing depending on which script produced it. It no longer creates or
commits `Binaries/ManaEditor.zip` or replaces a GitHub release ZIP. Generated build artifacts are
not source files.

**One-time setup**: GitHub Pages must be enabled for this repo with "Source" set to "GitHub
Actions" (Settings → Pages) before the deploy-pages step will succeed.

The catalog includes all repository/project asset trees except the oversized
`Assets/Planet/ETOPO_2022_v1_60s_N90W180_surface.tif`. Model format/extension support remains
unchanged. A ZIP pack downloads once when any model inside it is selected; loose glTF models
fetch their referenced buffers and textures. The library can be browsed without downloading
models: unloaded entries show format placeholders, with thumbnails generated after download.

Downloads show progress, validate SHA-256 and size, and use the browser Cache API for reuse
across reloads. Cache restrictions or quota exhaustion fall back to ordinary downloads.
A failed download can be retried by selecting the asset again. Escape or choosing another
asset cancels pending placement; an already-started download may finish and remain cached.
For a drag released before download completes, click in the scene to place the ready preview.

Locally, keep `AssetLibrary/` next to the app on the same HTTPS host (this is what a plain
`dotnet publish` + `serve.py` already gives you, unmodified). Only the boot files count toward
the initial loading bar; the remaining roughly 632 MiB is available on demand. Existing tabs may
need a reload after deploying a changed catalog.

The server reads files off disk per request, so after a republish just refresh — no restart. See
`ManaVoxel.Web/README.md` for why `serve.py` sends `no-store` rather than a plain
`python3 -m http.server`.

## What's different from `ManaVoxel.Web`

That project deliberately keeps `ManaVoxel.csproj` out of its reference graph and re-links a
handful of its files by source instead — at the time it was written, `ManaVoxel.csproj` had no
browser-wasm build to reference at all. M9 changed that (see `WebPort-Plan.md`), so this project
references `ManaVoxel.csproj` directly: the real `VoxelEditorGameMode`, not a hand-picked subset.

## What works

- The full ManaUi editor shell: menu bar, toolbar, docked Import/Outliner/Debug/Inspector panels,
  viewport with the orientation gizmo, live frame-time graph.
- **F5** (play in the editor) and **Shift+F5** (play from camera) — `PlayModeController`, no
  process involved, unaffected by anything browser-specific.
- The asset browser scans all bundled asset packs and loose models, using the same shared
  asset set as `ManaVoxel.Web`; no live directory watching is needed for the bundled files.
- Placing a GLB/glTF asset from the panel into the scene — goes through `ManaWorld.Gltf.cs`
  (SharpGLTF), the only mesh-import path on any platform since M10 removed Assimp entirely.
- Docking layout and UI density preset now survive a page reload, via a small `localStorage`
  bridge (`ManaCore.Browser.ManaBrowserSettingsBridge`) standing in for the desktop settings files,
  which would otherwise write to the wasm virtual filesystem and vanish on the next load.
- **Ctrl+F5** ("Play in New Window") fails gracefully with a status message rather than crashing —
  its `Process.Start` call was already wrapped in a broad `catch (Exception)` for unrelated desktop
  reasons, and `PlatformNotSupportedException` falls into the same catch.
- **Mesh thumbnails in the asset browser**, generated on the fly from the same
  `ManaSharpGltfLoader.LoadFile`/`LoadFileFromMemory` the real import already uses.
  `ManaSharpGltfLoader` only exposes scene-node-producing loaders (no CPU-only fast path), so
  `MeshThumbnailCache.FlattenSharpGltfSceneToCpuMesh` walks the resulting node tree and
  concatenates every mesh with its node transform baked in — necessary because a rigged Kenney
  character is several separate rigid meshes, not one, and previewing only the first would show a
  single limb instead of the whole character. Same implementation on every platform now (see M10).

## What doesn't (yet)

- **Importing OBJ/FBX/DAE/STL/PLY.** `VoxelFileImportService` routes every import through
  `ManaWorld.Gltf.cs`, SharpGLTF-backed (glTF/GLB only) on every platform since M10 removed the
  Assimp-backed desktop path entirely. Deliberately not being built: real content is glTF/GLB
  only, so this gap doesn't bite in practice, and writing a parser for formats nothing actually
  imports would be speculative work with nothing to validate it against.
- **Ctrl+F5 doesn't actually open a second window** — see above; it degrades to a status message,
  which is the intended behavior here rather than a bug to fix, since there is no second-process
  concept in a browser tab. A real fix would be an in-page mode switch reusing
  `PlayModeController`, the same thing F5 already does.

## Testing without a browser

Same headless-Chrome-probe technique as `ManaVoxel.Web` — see that project's README for the full
script. Point it at `http://localhost:8002` instead of `8000`.

## If a publish fails oddly

See `ManaVoxel.Web/README.md`'s note on `obj`/`bin` corruption under a synced folder
(`~/Documents`, iCloud) — same fix applies here:

```bash
rm -rf ManaVoxel.Editor.Web/obj ManaVoxel.Editor.Web/bin
```
