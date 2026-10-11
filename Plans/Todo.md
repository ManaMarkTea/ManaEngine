[] - Auto-Normal looks really bad.
    Claude (2026-10-07, not changed): looked at the Sprite Normals test scene (Tools/Automation/Checks/sprite-normals.json);
    it reads fine at night there, so I can't tell which sprite looks wrong. Tell me one (a level and a sprite) and I'll
    tune ManaSpriteNormals against it. Auto Normal Map is still off by default.

[] - Do we have cloth simulation in bepu? If so can we set it up to work with ManaWind on some of the cloths (tents, flags,

[] - Terrain support?  Would be nice to have rolling hills, or dirt paths, sculpting, etc. Can also be marching cube style voxel terrain to support caves and cliffs?
    Plan: Plans/Terrain.md. Claude (2026-10-07, not started -- a design choice for you): I'd build it in two layers. (1) A heightfield terrain node:
    a grid of heights drawn as one mesh, with sculpt brushes (raise, lower, smooth, flatten), a splat map for grass / dirt /
    path painted the same way, and a heightfield collider -- covers hills and paths cheaply, and the existing Grid Map and
    Voxel tools show how a floating tool window plus a brush works. (2) Caves and cliffs: a marching-cubes mesh over a density
    grid, reusing the voxel node's chunking, as a second node kind. Say if you want (1) first.

[] - Is there some kind of couch co-op 4 - 8 player game we could make using this stuff
    Plan: Plans/Couch-Coop.md. Claude (2026-10-07, ideas only): what exists is a side-on platformer pawn, bows and arrows, enemies, coins, checkpoints and
    moving platforms. A good fit is a "Coin Scramble" arena: 2-8 players on one screen, a fixed camera framing everyone, coins
    spawn, arrows stun, most coins when the clock ends wins; or a co-op "race the flood" run through the 2D Sample. What it
    needs first: more than one pawn (the world has a single GameState.Player), one gamepad per player (Silk.NET.Input
    handles several; nothing reads them yet apart from the flying mount), a camera that frames all players, and a join screen.
    Each game should be its own project to keep things seperate where posssible.  The editor prolly needs to depend on them all though to function?
    Claude (2026-10-08): yes -- Train Yard Wars is now its own project, `Games/TrainYardWars/` (engine only, no editor
    code). ManaVoxel references each game and registers its level game mode in `ManaVoxel/Code/Levels/ManaGames.cs`,
    lending it what only the app has (mesh import), so the editor depends on the games and never the other way round.
    Its tests are their own system: `dotnet run --project ManaTests -- TrainYardWars`. Yacht Jousting
    (`ManaVoxel/Code/Gameplay/YachtJousting/`) could move to `Games/` the same way.
    Codex (2026-10-10): Train Yard Wars performance and bot fixes are complete; review: Plans/TrainYardWarsReview.md.
    Follow-ups: reduce overlapping world gauges in eight-player matches; improve bot spending when one scarce
    resource blocks expansion; human playtest recovery after losing territory and full-edition pacing.

[x] - Add support for GPU particles
    Codex (2026-10-08): GPU Particle Emitter on WebGPU -- additive procedural discs, shader-side ballistic motion/drag,
    up to 10 million per emitter, without per-particle CPU work. Test scene: Content/Levels/GPU Particles.manalevel.
    Run Tools/Automation/benchmark-gpu-particles.ps1 for a count sweep with a zero-particle baseline. Details: Plans/GpuParticles.md.
    Moving/rotating emitters now preserve each particle's birth pose, with world-space gravity; no per-particle CPU state.
    Optional bounce against two world-space infinite planes, with restitution and drag. Demo: GPU Particle Bounce.


[] - UI Editor is not working and not used, needs updating - low priority
    Claude (2026-10-07): it opens and works headless here (Panels > UI Designer shows Main Menu's outline, live preview and
    properties; the tests pass), so I don't know what is broken for you. What did you hit?

[x] Shadows seem to be slightly off, they don't touch the ground at the same spot that the actor does.
    Claude (2026-10-07): could not reproduce. In play on the Sample Course at a low sun the character's shadow meets its feet,
    and the Shadow Contact scene's cubes, pillar and disk all touch their shadows (the bias is already in metres, from an
    earlier fix). If you still see it, tell me the level and the time of day -- or does it show only at a distance, where
    the shadow cascades get coarser?

[] - Big step!!  Rename ManaVoxel, its no longer just a voxel editor


[] - Lights:  I dont like the limits, games like minecraft seem to have unlimited light sources. Unreal has MegaLights, etc.  How can we increase the light amounts?  Forward+ rendering?  Fake lights? etc

[] - Do we have cloth simulation in bepu? If so can we set it up to work with ManaWind on some of the cloths (tents, flags, etc)
