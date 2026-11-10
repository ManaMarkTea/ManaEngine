# Target Practice

The engine's sample game: walk about a small level and shoot the targets with a pea shooter. The camera sits over
the character's right shoulder; the mouse aims, as in an action game (the camera's `ActionLook`), and a click (or F)
fires at the crosshair. R starts over, Esc quits. It is small on purpose, and a template for a game of your own.

```sh
dotnet run --project ManaEditor -- --play "Content/Levels/Target Practice.manalevel"   # the game, on its own
dotnet run --project ManaEditor -- --level "Content/Levels/Target Practice.manalevel"  # the editor; F5 plays
```

It also runs in the browser build (`ManaEditor.Web`), where the level is in the selector.

## Data first

As much of the game as can be is content, edited in the editor rather than in code:

| File | What it is |
|---|---|
| `Content/Levels/Target Practice.manalevel` | The level: floor, walls, pillars, trees, and prefab instances -- ten targets, one moving target, three crates. A `HUD` node carries the UiCanvas script, a `Sounds` node the music and the win fanfare. |
| `Content/Prefabs/Target Practice/Target.manaprefab` | A target: three discs and an `AudioSource` on the `EnemyDefeat` cue (its hit sound). Ring and bullseye colours are exposed properties, so each instance can be recoloured. |
| `.../Target On Post.manaprefab` | The same on a post, for the crates and pillars. |
| `.../Moving Target.manaprefab` | A **variant** of Target: it adds a `Motion` script (gliding side to side) and follows every later change to Target. |
| `.../Crate.manaprefab` | Kenney's pirate crate, sized and given a collider -- peas bounce off it, the character stands on it. |
| `.../Pea Shooter.manaprefab` | The gun: its grip at the origin, barrel along -Z, a `Muzzle` node where peas leave, an `AudioSource` on the `Shoot` cue. |
| `.../Pea.manaprefab` | What a pea looks like (a glowing green ball); its physics is the game's. |
| `Content/UI/Target Practice HUD.manaui` | The HUD layout -- score card, progress bar, crosshair, win banner, controls -- bound to `TargetPracticeHud` (MVVM). Open it in Panels > UI Designer. |

Sounds are cues, not code: the game says what happened (`ManaAudioCueRouter.EmitAt(node, ManaSoundCue.Shoot)`, or
`world.AudioCues.Emit(Goal)`) and whatever `AudioSource` in the content listens for that cue plays. Swap a clip, or add
one, in the prefab or the level.

## What is where in code

| File | What it does |
|---|---|
| `Code/TargetPracticeMode.cs` | The **level game mode**: input, the over-the-shoulder camera, putting the gun prefab in the hand, firing, the cues and effects of a hit and a win, script commands. A level lists it under `gameModes`; the editor, `--play` and the browser all run it the same way (`BeginPlay`, `Tick`, `EndPlay`). Its public properties -- `TargetPrefix`, `PopSeconds`, `ShoulderOffset`, `CameraDistance`, `Gravity`, `JumpHeight`, and under Content `GunPrefab`, `PeaPrefab`, `HitEffect`, `WinEffect` (pixelfx ids) -- are settings the level saves and the Inspector shows; gravity and jump height are set for play and put back after. |
| `Code/TargetPracticeHud.cs` | The **view model** the HUD layout binds to: numbers only, raising `PropertyChanged` when they change. |
| `Code/TargetRange.cs` | The **rules**, with no window or input: which nodes are targets, what a passing pea knocks down, how fast each target moves, the pop, the reset, the clock. Kept apart so `ManaTests` can play it. |
| `Code/PeaShooter.cs` | The **peas**: each a ball in the physics world, fired at 60 m/s, arcing under its own light gravity, bouncing off the scenery, used up by a target (the `Hit` event). |
| `Code/AimingArm.cs` | A **component on the character** that turns it to the camera's heading and raises its gun arm along the aim, after the walk animation has posed it. |
| `ManaTests/Code/TargetPractice/` | Checks for the rules, the peas, the mode's saved settings, the level, the HUD layout's bindings, and placing the prefabs from code. |
| `Tools/Automation/Checks/target-practice.json` | An in-app check: plays the level and shoots every target by script (`target_shoot` leads a moving one; `target_log`; `target_look` [yaw, pitch] points the camera, gliding over `frames` if given). |

## How it fits the engine

- The project references only `Mana`. `ManaEditor` references it and lists the mode in `ManaLevelGameModes`, which is how
  the editor, the play window and the browser learn of it. A game in a repository of its own does the same with
  `ManaLevelGameModes.Register(...)` from its own app.
- `WantsPlayerCharacter` asks the host for the character: spawned at the level's Player Start, walked with WASD and
  followed by the camera. `ShowsPlayHud` is off because this game has its own HUD: a `UiCanvas` script in the level
  whose `bindingSource` names this mode, which hands over `HudViewModel`.
- Prefabs from code: `ManaPrefabs.Spawn` places one with its scripts, colliders and sounds (the gun, put under the
  character's `arm-right` node so it moves with the arm; `ManaPrefabs.Find` gets its `Muzzle`), and `ManaModelLibrary`
  reads one for its look alone (each pea a copy sharing the prefab's mesh). `Assets` lists both, so the browser fetches them before play.
- A shot finds what is under the crosshair with `ManaScenePicker.PickWithHit` and fires a pea from the muzzle at it,
  aimed a little high for the drop. The pea is a real body (`ManaPhysics.AddDynamicSphereBody`, continuous so it cannot
  step through a thin wall); what it hits is decided by the segment it travelled each frame, so a target needs no
  collider. A hit plays the target's cue and a pixel effect (`ManaPixelFxSpawner`); the last one sets off the Goal cue
  and fireworks.
- Nothing a round does is saved: `EndPlay` puts every target back as the level had it, removes the peas, effects and
  gun, and gives back the camera, gravity and jump it borrowed.

## Making a game from it

1. Copy this folder to `Samples/<YourGame>` (or a project of its own), rename the namespace and the mode's `ModeType`.
2. Reference it from `ManaEditor` the same way, and register the mode in `ManaLevelGameModes`.
3. Make a level in the editor (File > New), add a Player Start, and add your mode under the scene's game modes.
4. Make what things look and sound like prefabs, and the HUD a layout (Panels > UI Designer) bound to your mode's view model.
5. Put the rules in a class like `TargetRange` and test them in `ManaTests/Code/<YourGame>/`.
