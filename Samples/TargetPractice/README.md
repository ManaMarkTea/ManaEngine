# Target Practice

The engine's sample game: walk about a small level and shoot the targets with a pea shooter. The camera sits over
the character's right shoulder; click (or press F) to fire at the crosshair, right-drag to aim. R starts over, Esc quits. It is small on purpose, and a template for a game of your own.

```sh
dotnet run --project ManaEditor -- --play "Content/Levels/Target Practice.manalevel"   # the game, on its own
dotnet run --project ManaEditor -- --level "Content/Levels/Target Practice.manalevel"  # the editor; F5 plays
```

It also runs in the browser build (`ManaEditor.Web`), where the level is in the selector.

## What is where

| | |
|---|---|
| `Code/TargetPracticeMode.cs` | The **level game mode**: input, the over-the-shoulder camera, the gun, the HUD, a script command. A level lists it under `gameModes`; the editor, `--play` and the browser all run it the same way (`BeginPlay`, `Tick`, `EndPlay`). Its public properties (`TargetPrefix`, `PopSeconds`, `ShoulderOffset`, `CameraDistance`) are settings the level saves and the Inspector shows. |
| `Code/TargetRange.cs` | The **rules**, with no window or input: which nodes are targets, what a passing pea knocks down, the pop, the reset, the clock. Kept apart so `ManaTests` can play it. |
| `Code/PeaShooter.cs` | The **peas**: each a ball in the physics world, fired at 45 m/s, arcing, bouncing off the scenery, used up by a target. |
| `Code/AimingArm.cs` | A **component on the character** that turns it to the camera's heading and raises its gun arm along the aim, after the walk animation has posed it. |
| `Content/Levels/Target Practice.manalevel` | The **content**: a floor, a wall, crates, pillars and ten targets. A target is any node named `Target ...` (here a group of three discs and a post), so adding one is Add > Group in the editor and a rename. |
| `ManaTests/Code/TargetPractice/` | Checks for the rules, the mode's saved settings, and the level. |
| `Tools/Automation/Checks/target-practice.json` | An in-app check: plays the level and shoots every target by script (`target_shoot`, `target_log`). |

## How it fits the engine

- The project references only `Mana`. `ManaEditor` references it and lists the mode in `ManaLevelGameModes`, which is how
  the editor, the play window and the browser learn of it. A game in a repository of its own does the same with
  `ManaLevelGameModes.Register(...)` from its own app.
- `WantsPlayerCharacter` asks the host for the character: spawned at the level's Player Start, walked with WASD and
  followed by the camera. `ShowsPlayHud` is off because this game draws its own HUD (`BuildHud`, ManaUI).
- A shot finds what is under the crosshair with `ManaScenePicker.PickWithHit` (it tests the meshes' triangles, so it
  works on any node, with or without a collider) and fires a pea from the gun at it, aimed a little high for the drop.
  The pea is a real body (`ManaPhysics.AddDynamicSphereBody`, continuous so it cannot step through a thin wall); what it
  hits is decided by the segment it travelled each frame, so a target needs no collider either.
- The gun is the `weapon_gun` model from the Kenney space kit, placed through `ManaModelLibrary` as a child of the
  character's `arm-right` node, so it moves with the arm.
- Nothing a round does is saved: `EndPlay` puts every target back as the level had it, removes the peas and the gun,
  and gives the camera back its own distance.

## Making a game from it

1. Copy this folder to `Samples/<YourGame>` (or a project of its own), rename the namespace and the mode's `ModeType`.
2. Reference it from `ManaEditor` the same way, and register the mode in `ManaLevelGameModes`.
3. Make a level in the editor (File > New), add a Player Start, and add your mode under the scene's game modes.
4. Put the rules in a class like `TargetRange` and test them in `ManaTests/Code/<YourGame>/`.
