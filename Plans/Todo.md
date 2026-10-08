[] - Auto-Normal looks really bad.
    Claude (2026-10-07, not changed): looked at the Sprite Normals test scene (Tools/Automation/Checks/sprite-normals.json);
    it reads fine at night there, so I can't tell which sprite looks wrong. Tell me one (a level and a sprite) and I'll
    tune ManaSpriteNormals against it. Auto Normal Map is still off by default.

[] - can the background stars move with the dynamic stars
[] - Can we connect the constalation lines when the camera looks at them?
[] - Can I get a star preview level
[] - Can i get sample level for particles

[] - Dark mode -> it feels a little too dark and constrasty in the view port grid, can it be closer in color to the light mode grid (but a little darker) Also the contrast between alternating squares isn't visible
[] - Theme Support -> I added some ui from kenney, can we make it possible to use that theme onto the ui as well?

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

[] - UI Editor is not working and not used, needs updating - low priority
    Claude (2026-10-07): it opens and works headless here (Panels > UI Designer shows Main Menu's outline, live preview and
    properties; the tests pass), so I don't know what is broken for you. What did you hit?

[x] Shadows seem to be slightly off, they don't touch the ground at the same spot that the actor does.
    Claude (2026-10-07): could not reproduce. In play on the Sample Course at a low sun the character's shadow meets its feet,
    and the Shadow Contact scene's cubes, pillar and disk all touch their shadows (the bias is already in metres, from an
    earlier fix). If you still see it, tell me the level and the time of day -- or does it show only at a distance, where
    the shadow cascades get coarser?
