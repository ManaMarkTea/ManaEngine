# Open work

What is still to do in the engine. Finished work, with notes on how it was built, is in `Todo-Finished.md`.

[ ] - Sample game: a small game project in the engine (its own level game mode, HUD, content folder and test) that also
    works as a template for making one. Today the engine ships sample levels, not a game.

[ ] - Auto-Normal looks really bad. Auto Normal Map is off by default; tune `ManaSpriteNormals` against a level and sprite
    that look wrong (Tools/Automation/Checks/sprite-normals.json reads fine).

[ ] - Terrain (Plans/Terrain.md): the path spline tool (flatten + paint along a draggable curve); caves and cliffs (a
    marching-cubes node over a density grid, reusing the voxel chunking). Scatter should hand the renderer its transforms
    directly (one instanced batch per model, not a node per copy), with a distance fade for grass and colliders for trees.

[ ] - Project code cleanup (Plans/CodeCleanup.md): specify binding-wrapper lifetime and extract reviewed renderer resource
    owners (section 7); other mutable test hooks; register wasm-tools for SDK 10.0.402 (needs administrator rights).

[ ] - Cloth: tents and awnings (collision with a box or the ground, more pin shapes); sleep cloths out of view; pin a cape to
    the chest bone; turn capes on in a bundled level.

[ ] - Lights: no depth pre-pass for the main scene (overdraw pays full lighting); a cell lists at most 256 lights.

[ ] - UI: show a `UiCanvas` in the editor's viewport before pressing play; rebuild canvas layouts when the theme changes.

[ ] - Imported scenes (Bistro): persist reparenting, duplication and authored nodes nested inside imported parts; improve
    the lighting against the ORCA references.

[ ] - Other platforms: the iPhone/iPad host and a macOS check of the web build.
