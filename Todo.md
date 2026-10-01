[x] - Version number in the app (bottom left, and in the About box); View > About did nothing; an emptied side column
    could only be got back by resetting the panels.
    Claude: the version is <Version> in Directory.Build.props (0.1.0; bump it for a release) plus the git commit, read by
    ManaCore's ManaVersion. View > About opens an About box (AboutBoxManaView) with build details and credits -- fonts,
    the emoji font in use, Kenney's packs, the sky photos, the libraries; sources not on record (the lighthouse, the zoo
    animals, the course audio) are left off until we know them. The old diagnostics panel is now Graphics Diagnostics.
    An emptied column keeps a 40 px band along its window edge to drop a panel into (DockHost.EmptyDropZone).

[x] - Web Level play:  ManaUi is not showing up at all. the player also moves much slower than the regular player.
    Claude: the browser play mode had ManaUI switched off; it now shows desktop play's HUD, results and death screens
    (Back to levels returns to the selector). The slowness was the character: it spawned the first one found at the
    default 1 m height, and speeds and jump scale with height; it now spawns the Player Start's character, as desktop.
    Also: Pages deploys go to <app>/v/<build>/, named by version.json, so a cached old file can no longer break a new
    deploy (the editor's 'Failed to fetch ManaAudio.wasm'). The first deploy after this can still trip a browser holding
    the old index.html for up to 10 minutes; a reload after that fixes it for good.



[x] - Can the moon match up to the real phase of the moon (New moon, half moon, full moon)
    `ManaLunarPhase` calculates the phase and illumination; the simulated date/time now drives the moon, its
    light, and reflections.

[x] - Add date/time picking and sidereal day steps to the sky clock.
    The clock accepts a local date/time and can use either a solar day or an 86,164-second sidereal day; negative
    speeds advance the calendar backwards too.

[x] - Run the day-night cycle backwards.
    Sky Clock speed presets include reverse rates, and clouds drift backwards with the cycle.


[] - Can the mana command runner be in a higher up in the project dependencies? with more commands registered by the project that supports it. - like things only mana voxel can do etc.

[x] - It can be hard to tell when something is html, or mana ui.  Is there a way to mark html as such
    Browser loading screens, level selector, pause menu and Escape hint now carry an HTML badge.
    Labels live inside each HTML surface and disappear with it; checked the page layouts in headless Chrome.

[] - Do we support nested prefabs, and is it possible?

[] - Dynamic normal maps on sprites?
[] - Auto-Normal looks really bad.


[] - Can we organize the panels menu? Some things are more important than others, some things are for debugging, etc.  Also a search bar that finds a given menu (like unreal) would be cool

[] - Can we also support exporting video in addition to the PNG snapshots we take now. Prolly using FFMPEG to compress pngs or something.

[x] - The search location panel is gone, i think it moved somewhere but i can't find it anymore. where is it?
    Location search is a dialog under View > Search Location... (formerly Search Area...), renamed for discoverability.

[x] - There is a blue ball and yellow donut in every scene for some reason - i forgot why, remove them.
    Removed the startup cursor sphere and demo pickup donut from VoxelEditorGameMode; voxel hover feedback remains.

[x] - Finish the Little Pond Level - Add tents and picnic stuff
    Added Kenney Mini Forest tents and woodland, a fire circle, picnic table, blanket and supplies.
    Reusable camping prefabs and refreshed level preview; verified loading and player grounding.

[] - Terrain support?  Would be nice to have rolling hills, or dirt paths, sculpting, etc. Can also be marching cube style voxel terrain to support caves and cliffs?

[x] - JSON files seem to be adding lots of new lines again, i thought it was fixed.
    Sprite assets were bypassing ManaJsonFormat. Their shared save path now uses it too, keeping short arrays
    like pivots on one line. Checked value preservation and stable load/save formatting; existing files update when saved.

[] - Some kind of particle system. - Points, trails, ribbons, etc.

[] - Is there some kind of couch co-op 4 - 8 player game we could make using this stuff

[] - UI Editor is not working and not used, needs updating - low priority

[x] - Perf: Split up the world perf ManaUI by panel to help know which panel is slow
    World Perf now separates panel CPU timings into Bindings, Measure, Arrange and Record, including floated panels.
    Scope names sit above aligned Peak/Now/Avg milliseconds and peak allocation columns; frame totals stay separate.
    Timings also appear in profile_dump. Verified in-app (DevPhotos/2026-10-01 panel-timings.png); all 2,920 checks pass.

Claude, overnight summary (2026-09-29): every item below is done and pushed; the 2D work went furthest. Open
the 2D Sample and press F5 -- a side-on pixel-art platformer: A/D run, Space (double) jumps, three glowing coins on
the long ledge, parallax mountains and hills, orthographic view. Photos for each step are in DevPhotos/.
One question for you (in the Glow item): how material edits should be saved.
2026-09-30 (night): the web build no longer downloads the 10 MB emoji font or the 5 MB sky photo (play startup 57 -> 41 MB).
Emoji come from the browser's own font, drawn on a canvas (ManaBrowserEmoji); the web loading screen pops each one in
as it is drawn (DevPhotos/2026-09-30 web-loading-emoji.png). On WebGPU the shader sky is drawn into the reflection
cubes, so reflections match the sky on screen (sunsets, night, space); the photo is decoded only for the Photo sky,
fetched from the catalog on the web. Follow-up: Space scenes are darker now (a dark sky lights them less) -- say if
you want an ambient floor there.
2026-09-30 (evening): the whole pack built into assets (5 characters, 18 enemies, items, terrain slices for six grounds)
and the 2D Sample grown into a five-biome run: grass -> desert (cacti, a spike pit crossed on a moving platform, snails)
-> snow (clouds over a gap, a spring up to a coin cache) -> mushroom (bees, flies, ladybugs) -> stone castle (torches,
lava, a saw, the exit door). 28 coins and 4 gems, checkpoints between biomes. DevPhotos/2026-09-30 2d-biome-*.png.
2026-09-30 (later): sprites reworked into three components -- Sprite, Sprite Sheet, Sprite 9-Slice -- on reusable
assets (.manasheet, .manaslice in Content/Sprites) set up once in the Sprite Asset window; every user-made asset kind
now comes from one registry (ManaContentKinds) the Project panel lists, with New. The Import panel lists 2D pictures
(PNG format) to drag in, or turn a selection into a sheet or a 9-slice. DevPhotos/2026-09-30 sprite-sheet-editor.png,
nine-slice-editor.png.
2026-09-30: the 2D Sample now uses Kenney's New Platformer Pack (Assets/kenney_new-platformer-pack-1.1.zip): the green
character, grass terrain, gold coins (Content/Prefabs/Coin 2D), water, a slime, a goal flag and the painted backdrop.
Sprites read straight from the zip; the pack's .ogg sounds need Ogg support in ManaAudio first, so the sample uses
the existing .wav jump/pickup/stomp. DevPhotos/2026-09-30 2d-kenney-platformer-pack.png. Tests: 2,841 checks, all passing.

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
    Still to do: the planet code had machine-specific GeoData paths -- fixed in the VoxelEditorGameMode split below.

[x] Add Support for 2d gameplay (Sprites, platforms, sprite sheet animations, sprite lighting and normals, polygon colliders, etc)
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
    Tiling done: Sprite > Tiled Size (metres) repeats the picture across it instead of stretching -- the 2D Sample
    gained a Long Ledge (8 m of brick from the 4 m tile) with three Glowing Coins to collect on it.
    Photo: DevPhotos/2026-09-29 2d-tiled-ledge-coins.png.
    Orthographic done: Platformer2D > Orthographic (on by default) plays with a flat view, View Height metres tall,
    looking level; stopping puts the editor's perspective back. The sky keeps its gradient and clouds under an
    orthographic camera now (it used to go one flat colour). Photo: DevPhotos/2026-09-29 2d-orthographic-play.png.
    Parallax done: a Parallax script (factor x/y: 0 moves with the level, 1 stays with the camera) on backdrop
    sprites; the 2D Sample has far mountains and nearer hills (Content/Sprites/mountains.png, hills.png) sliding
    at 0.8 and 0.5. Photo: DevPhotos/2026-09-29 2d-parallax-backdrops.png. Tests: ParallaxChecks.
    Polygon editing done: select a node with a PolygonCollider and its points get handles in the viewport -- drag
    one to move it (the editor's move snap applies when on), Ctrl+click an edge to add a point there, Alt+click a
    point to remove it; each is one undo step. Photo: DevPhotos/2026-09-29 2d-polygon-point-handles.png.
    Tests: PolygonEditChecks. NOTE: I could only check the handles draw -- automation has no mouse drag -- so
    please give dragging a try.
    Still open for 2D: the sprite and its collider are separate (moving points does not reshape the picture), and
    OpenGL does not draw sprite textures (it never reads mesh texture paths).

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
    QUESTION for you before I build it: materials are shared -- every copy of a model uses the same one, so an edit in
    the inspector changes them all. Two ways to save that:
      a) per-copy overrides (Unity's "material override" / Unreal's material instance on the actor): the level saves
         just the changed values on that node, and only that copy changes. Needs the edit to clone the material first.
      b) the shared material is the asset: the edit is saved beside the model and every copy, in every level, changes.
    I'd suggest (a) -- it matches how the Glow already works and cannot surprise another level -- with (b) later as
    "Apply to all copies". Say which and I'll do it.


[x] Review Changes for prefab variants: the review window works on placed copies; a variant open in the prefab stage could use it too.

[x] ManaVoxel\Code\GameModes\VoxelEditorGameMode.cs has A LOT of stuff it shouldn't have, planet stuff mostly, that all needs to be split out into game mode components, or some other class.
    Claude (overnight): VoxelEditorGameMode.cs 3,205 -> 1,273 lines.
    - Planet, true-scale regions, KML airspace, streaming and planet stats -> EditorPlanetScenery (its own class;
      the game mode owns one and only reads its Planet to pick/up-res patches).
    - Dead code deleted: the editor's own water mask and country/state border loaders (unused since planets load
      through ManaLevelLoader), taking the old machine-specific GeoData paths with them. The optional
      KML file is now MANA_KML_AIRSPACE or ../ManaAirSpace/AirSpaceModels/UnitedStates.xml.
    - The rest split into partial files by job: .Room (the grid room), .Placement (placing from the Import panel),
      .ColliderDebug (selection collider wireframes), .Selection (picking + F / End / Escape).
    Checked: Planet Showcase streams, End still lands on a ledge, all tests pass.

[] Shadows seem to be slightly off, they don't touch the ground at the same spot that the actor does.
