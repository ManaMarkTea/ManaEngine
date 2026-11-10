# Target Practice

The engine's sample game: walk about a small level and shoot the targets. Click (or press F) to fire at the pointer;
R starts over, Esc quits. It is small on purpose, and a template for a game of your own.

```sh
dotnet run --project ManaEditor -- --play "Content/Levels/Target Practice.manalevel"   # the game, on its own
dotnet run --project ManaEditor -- --level "Content/Levels/Target Practice.manalevel"  # the editor; F5 plays
```

It also runs in the browser build (`ManaEditor.Web`), where the level is in the selector.

## What is where

| | |
|---|---|
| `Code/TargetPracticeMode.cs` | The **level game mode**: input, the camera ray, the HUD, a script command. A level lists it under `gameModes`; the editor, `--play` and the browser all run it the same way (`BeginPlay`, `Tick`, `EndPlay`). Its public properties (`TargetPrefix`, `PopSeconds`) are settings the level saves and the Inspector shows. |
| `Code/TargetRange.cs` | The **rules**, with no window or input: which nodes are targets, what a shot knocks down, the pop, the reset, the clock. Kept apart so `ManaTests` can play it with a ray. |
| `Content/Levels/Target Practice.manalevel` | The **content**: a floor, a wall, crates, pillars and ten targets. A target is any node named `Target ...` (here a group of three discs and a post), so adding one is Add > Group in the editor and a rename. |
| `ManaTests/Code/TargetPractice/` | Checks for the rules, the mode's saved settings, and the level. |
| `Tools/Automation/Checks/target-practice.json` | An in-app check: plays the level and shoots every target by script (`target_shoot`, `target_log`). |

## How it fits the engine

- The project references only `Mana`. `ManaEditor` references it and lists the mode in `ManaLevelGameModes`, which is how
  the editor, the play window and the browser learn of it. A game in a repository of its own does the same with
  `ManaLevelGameModes.Register(...)` from its own app.
- `WantsPlayerCharacter` asks the host for the character: spawned at the level's Player Start, walked with WASD and
  followed by the camera. `ShowsPlayHud` is off because this game draws its own HUD (`BuildHud`, ManaUI).
- A shot is `ManaScenePicker.PickWithHit` from the camera through the pointer. It tests the meshes' triangles, so it works
  on any node, with or without a collider; the player's own body is left out because the camera shoots through it.
- Nothing a round does is saved: `EndPlay` puts every target back as the level had it.

## Making a game from it

1. Copy this folder to `Samples/<YourGame>` (or a project of its own), rename the namespace and the mode's `ModeType`.
2. Reference it from `ManaEditor` the same way, and register the mode in `ManaLevelGameModes`.
3. Make a level in the editor (File > New), add a Player Start, and add your mode under the scene's game modes.
4. Put the rules in a class like `TargetRange` and test them in `ManaTests/Code/<YourGame>/`.
