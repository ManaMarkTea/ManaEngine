# Sample Course sound additions

24 original procedural sounds to complement the Starter pack. Open **preview.html** to audition them.
Mono 48 kHz PCM16 WAV throughout; spatial playback is supported. The preview plays loops once so
**Play all** can continue to the next sound.

Designed against the current `Content/Levels/Sample Course.manalevel`: 24 coins, two checkpoints, five enemies,
two moving platforms, damaging oil/track hazards and lethal harbor water. The finish does **not**
require every coin. The scene's cannons, item boxes, bananas and parked trains have no corresponding
gameplay scripts, so this pack does not invent firing, power-up or driving events for them.

| Files | Course use |
|---|---|
| `Footsteps/asphalt_01..03` | Road section: firm shoe taps with a little grit |
| `Footsteps/wood_01..03` | Pier planks and lumber car: hollow, woody footfalls |
| `Footsteps/metal_01..03` | Containers, rail cars and lift: muted metallic footfalls |
| `Footsteps/sand_01..03` | Harbor sand patches, if used as walkable surfaces: soft scrunches |
| `Enemies/alert` | Yard Hog's transition into chasing; the other four enemies currently only patrol |
| `Enemies/stomp` | Successful stomp and bounce on any of the five enemies |
| `Enemies/defeat` | Optional quiet sparkle after the enemy vanishes |
| `Hazards/oil_squelch` | First contact with Oil Slick or Spilled Oil |
| `Hazards/water_splash` | Entering lethal Harbor Water, before the respawn cue |
| `Hazards/fall` | Optional cue for a sustained downward fall off the islands or elevated course |
| `Feedback/low_health` | Transition down to one health; don't retrigger each frame |
| `Feedback/all_coins` | Optional reward when collected coins first equal the level total; distinct from finishing |
| `Platforms/ferry_motion_loop` | Ferry Platform's movement: subdued vibration and timber creak |
| `Platforms/lift_motor_loop` | Cargo Lift's movement: restrained electrical motor hum |
| `Platforms/stop_clunk` | Arrival at either end of the ferry/lift path |
| `Ambience/harbor_water_loop` | Quiet localized water texture around the harbor |

## Playback notes

The Sample Course now saves Audio Source components for these events, alongside the Starter pickup,
checkpoint, jump, landing, hurt, death, respawn and goal cues. Open the level and press F5 to play.
Select a node's Audio Source in the inspector to preview or adjust it. Shared player cues live on
Player Start; object cues live on the relevant coin, enemy, hazard or platform.

Surface assignments follow the scene's assets. Ground ray hits select the surface's Footstep cue
(including parent nodes), and distance traveled triggers rotating variants. Unassigned surfaces use
the player's generic fallback. Standing still, airborne movement and teleports do not trigger steps.

Only the three `_loop.wav` files are loops. They are 4, 4 and 6 seconds long, with continuous boundaries
and no artificial silence. Start machinery when a platform moves; stop it during the 1.5-second pause
at each endpoint. The platform component triggers the clunk once on arrival. Audio Source provides
built-in start/stop fades: the saved motors use 150 ms and harbor ambience uses 800 ms.

Oil currently causes damage, not slippery movement: its squelch should indicate contact, not imply a
new sliding mechanic. Water currently kills immediately, so a short splash is sufficient; there is
no swimming loop. Low-health/fall/all-coins sounds are optional feedback for existing state, not new rules.

```csharp
var stomp = await Context.Audio.LoadClipAsync("Assets/Audio/SampleCourse/Enemies/stomp.wav");
Context.Audio.Play(stomp, new() { Position = enemyPosition });
```

Preload during scene setup. Existing desktop copying and browser asset publishing include this folder.
Keep ambience low enough that footsteps, enemy feedback and pickups are intelligible.

## Provenance and regeneration

All samples are synthesized from oscillators, envelopes and seeded noise; no recordings or third-party
samples are used. These are stylized sounds matching the Starter pack, not field-recorded foley.

Run `python Tools/Audio/generate_course_pack.py`. It shares synthesis helpers with the Starter generator
but leaves all Starter WAVs unchanged. The manifest lists intended nodes, durations, levels, loop flags,
hashes and the inspected level's hash. It validates named node mappings, WAV encoding, headroom and loop
seams. If the course is rearranged, review the mappings before regeneration.
