[x] Press the END key to drop the item onto the floor or other objects. - Writen by copilot, verify its good.
    Claude (overnight): verified and improved. It now casts five rays (centre + footprint corners) and lands on the
    highest hit, as Unreal's End does, so a box half over a ledge rests on the ledge; rays start just above the
    object's bottom so something sunk a hair into the floor rises out. Tested: a cube overhanging a 6 m pillar
    lands on top of it (6.5). `drop_to_floor` command added for scripted checks.

[x] - Moved various data out of Root/ and into /Content/. Anything user authored should go there.  Done by copilot make sure everything still works.
    Claude (overnight): verified. All 8 levels in Content/Levels open with 0 warnings; tests pass (2,799).
    Found and fixed along the way: every level read as "Save *" (changed) the moment it opened. Colliders set to
    "fit the model" were captured by guessing, and the guess flipped once the model finished loading (or the
    moving platform's ghost joined its bounds). Colliders now remember whether they follow the model, refit when
    the model grows, and ignore editor-only children; all 8 levels now open clean. See ColliderIntentChecks.
    Still to do: the planet code hard-codes C:\Users\night\...\GeoData paths (will break on the Mac) -- fixing that
    in the VoxelEditorGameMode split below.

[~] Add Support for 2d gameplay (Sprites, platforms, sprite sheet animations, sprite lighting and normals, polygon colliders, etc)
    Claude (overnight), milestone 1 done:
    - Sprite script: a PNG on a quad facing +Z, sized by pixels-per-unit, pivot, sheet frames (columns x rows),
      flipX, tint, alpha cutout; optional normal map, so lamps/glows shade it with depth (PBR pass).
    - SpriteAnimation script: fps, frame range, loop -- plays in the editor too.
    - PolygonCollider script: any outline (concave OK -- ear-clipped, extruded to a solid), rebuilt when edited.
    - Sample: Content/Levels/2D Sample.manalevel (platforms, walking hero, a torch Glow); art in Content/Sprites
      (drawn procedurally, with normal maps). Photo: DevPhotos/<date> 2d-sprites-torchlit.png. Tests: SpriteChecks.
    - Engine fix on the way: a component can now add another to its node while it ticks (the update loop walks
      components by index), and the level format reads Vector2 ([x, y]).
    Milestone 2 done: a playable 2D character. The Platformer2D script (on the 2D Sample's Player Start) makes
    F5 / Ctrl+F5 play side-on: A/D run, Space jumps (double jump too), W/S do nothing; the character is held to
    its plane, drawn by the hero sheet (idle / run / jump frames, flipped by direction) instead of its 3D model,
    and the camera sits side-on following it. Stop puts everything back. Photo: DevPhotos/2026-09-29
    2d-player-side-on.png. Tests: Platformer2DChecks. (Add Component names with numbers now read "Platformer 2D".)
    Also done: sprites are pixel art by default (Sprite > Pixel Art: nearest sampling, crisp square pixels; off for
    painted art) -- DevPhotos/2026-09-29 2d-pixel-art-crisp.png. And an animating sprite no longer marks its level
    changed (the playing frame is kept apart from the saved one); the 2D Sample opens clean.
    Next for 2D: an orthographic camera option, tiling sprites (one platform picture repeated rather than stretched), and editing
    a PolygonCollider's points in the viewport.

[x] Road generator - There is a city pack with road pieces, it would be nice to be able to drag out a road and have it auto-generate the correct nodes under it dynamically.
    Claude: done earlier -- Grid Map window, Road preset (auto-tile): drag to paint a road and the right straight /
    corner / T / crossing pieces are picked from the neighbours.

[x] Similar to our grid map floating tool, lets move all the voxel arrows and stuff into a tool panel that shows up when selecting a voxel node.
    Claude: verified -- selecting Captain Voxels in Voxel Showcase brings up the Voxel Tools window (and the room walls).

[x] Wave function collapse to populate a dungeon from the kenney dungeon assets, but have it be configurable with some JSON assets and a Window Editor for it.

[x] Add support for UI from JSON or xaml-ish.  And an editor for it. (Like UMG in unreal)
    Claude: done earlier as JSON -- UiMarkup (.manaui files in UI/) and Panels > UI Designer. No XAML flavour yet.

[x] Glow component (next up): levels and prefabs don't save material settings, so emission set in the Inspector is lost on save. Add a "Glow" script (colour, strength, optional light range) that sets emission on the meshes beneath it and adds a small point light, so a "Glowing Coin" is a Coin variant with a Glow added. Strength above 1 blooms.
    Claude (overnight): done. "Glow" script (Add component > Glow): colour, strength (above 1 blooms), light range,
    light intensity, light offset, pulse speed. It makes everything beneath its node glow without touching the shared
    materials (so other coins stay plain) and brings its own small point light. New prefab:
    Content/Prefabs/Glowing Coin.manaprefab -- a variant of Coin whose only change is the added Glow, gently pulsing.
    Photo: DevPhotos/<date> glowing-coin-prefab.png. Tests: GlowChecks.
    Still open: materials themselves (emission set in the Mesh Renderer inspector) still aren't saved with a level.


[x] Review Changes for prefab variants: the review window works on placed copies; a variant open in the prefab stage could use it too.

[x] ManaVoxel\Code\GameModes\VoxelEditorGameMode.cs has A LOT of stuff it shouldn't have, planet stuff mostly, that all needs to be split out into game mode components, or some other class.
    Claude (overnight): VoxelEditorGameMode.cs 3,205 -> 1,273 lines.
    - Planet, true-scale regions, KML airspace, streaming and planet stats -> EditorPlanetScenery (its own class;
      the game mode owns one and only reads its Planet to pick/up-res patches).
    - Dead code deleted: the editor's own water mask and country/state border loaders (unused since planets load
      through ManaLevelLoader), taking every hard-coded C:\Users\night\...\GeoData path with them. The optional
      KML file is now MANA_KML_AIRSPACE or ../ManaAirSpace/AirSpaceModels/UnitedStates.xml.
    - The rest split into partial files by job: .Room (the grid room), .Placement (placing from the Import panel),
      .ColliderDebug (selection collider wireframes), .Selection (picking + F / End / Escape).
    Checked: Planet Showcase streams, End still lands on a ledge, all tests pass.
