# Terrain

Goal: rolling hills, dirt paths, sculpting -- and later caves and cliffs. Written 2026-10-08; nothing is built yet.

## Two layers, built in this order

### 1. Heightfield terrain (hills, paths, sculpting)

A `Terrain` node kind, like `GridMap` and `Voxels`:

- **Data:** a grid of heights (e.g. 129 x 129 samples, a configurable metres-per-sample) and a splat map -- up to four
  layer weights per sample (grass, dirt, rock, sand). Saved in the level as a size plus one base64 blob each, the way voxel
  grids and grid-map cells are, so a level file stays small and diffs stay quiet.
- **Mesh:** one mesh per 32 x 32 patch, so editing rebuilds a patch, not the whole terrain; patches share the node's
  material. Normals from the height gradient. A skirt is not needed.
- **Material:** blends the layers by the splat weights. First version: vertex colours per layer (no shader change, works
  today). Later: a terrain shader with tiling albedo/normal per layer and triplanar on steep slopes.
- **Collision:** a heightfield collider in BepuPhysics v2 (a `Mesh` shape built from the patches is the fallback). The
  player must stand on it -- `teleport_player` + `log_player` checks, as for any level.
- **Tool window:** floats while a terrain node is selected, like the Grid Map and Voxel Tools windows (`ManaUiDockDemo.SyncToolWindow`).
  Brushes: raise, lower, smooth, flatten-to-height, noise; paint layer; brush size, strength, falloff. Undo per stroke.
- **Paths:** a spline tool that flattens and paints along a polyline (reuse the polygon-collider point handles in
  `VoxelEditorGameMode.Polygon.cs`), so a dirt road is a curve you can drag, not a stroke you must repeat.
- **Scatter:** later, paint grass/trees/rocks by density from the Import panel's models.

Files to start from: `ManaVoxel/Code/GridMaps/` (node, tool window, level capture/load), `Mana/Code/Shapes/ManaSpriteMesh.cs`
and `ManaParticleMesh.cs` (hand-built meshes), `ManaVoxel/Code/Levels/` (capture/load).

### 2. Marching-cubes volume (caves and cliffs)

A second node kind over a density grid (signed distance), meshed per chunk with marching cubes or dual contouring:

- Reuse the voxel node's chunking and its edit tools' shape (sphere add/subtract brushes).
- Normals from the density gradient; materials by a per-voxel id, as the voxel node does.
- Collision from the chunk meshes.
- It can sit beside a heightfield terrain: carve a cave mouth into a hillside by subtracting from the volume that covers it.

## Decisions to make

- Heightfield resolution and size limits (memory on the web build: 129 x 129 floats is only 66 KB; 1025 x 1025 is 4 MB).
- Whether the web build gets sculpting or only plays terrain (the editor on the web exists, so probably both).
- Splat layers: four per node (one RGBA map) is simplest; more means more maps.

## Checks to add

- A `Terrain` reference level in `Content/Levels/Tests/` and a script in `Tools/Automation/Checks/`.
- Tests: height round-trips through the level format; a brush changes only the patches it touches; the player is grounded on a slope.
