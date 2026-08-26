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

Then open <http://localhost:8002>. First load is ~54 MB and takes **10–15 seconds** on a fast
link — AOT-compiled wasm, the environment map, two bundled asset packs, and two font files. A
loading screen covers the whole wait; see `ManaVoxel.Web/README.md`'s own section on it, which
this project's `index.html`/`main.js` are identical copies of.

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
- The asset browser: 387 assets scanned from two bundled Kenney packs (same ones
  `ManaVoxel.Web.csproj` ships for play mode's character loading), no live directory watching
  needed since `Scan()` is a one-time walk.
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
