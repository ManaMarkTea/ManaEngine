# Open work

What is still to do in the engine. Finished work, with notes on how it was built, is in `Todo-Finished.md`.

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

[ ] - Planet (Planet Showcase): the planet in the browser (compiled out with `MANA_BROWSER`; the flight relay on hera
    is ready, the GeoTIFF baseline, Natural Earth and imagery caching are not); aircraft models by type (a CC0
    set of ~8 silhouettes -- narrow/wide-body, regional, turboprop, business jet, light, helicopter -- picked by the
    adsbdb type, in place of the one marker shape); sharper imagery below ~5 km than Blue Marble's 500 m (Sentinel-2
    cloudless is 10 m, stricter licence); the two ~100 ms stalls in the first seconds after loading.
