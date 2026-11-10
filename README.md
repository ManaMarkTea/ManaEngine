# ManaEngine

ManaEngine is a C# renderer and game engine with a voxel-level editor. WebGPU is the graphics backend. The projects target .NET 10, Windows, and macOS (arm64).

## Making a game

The engine has no game built in; a game is a project of its own that references `Mana`. It registers its rules as level
game modes (`ManaLevelGameModes.Register`), keeps its levels, prefabs and pictures in a `Content` folder the editor
mounts, and can run in the editor, standalone (`--play <level>`) or in the browser (`ManaEditor.Web`). `Content/Levels/`
holds sample levels to open and play, such as `Sample Course`. `AGENTS.md` describes the level, prefab and game-mode
formats.

## Projects

| Project | Purpose |
| --- | --- |
| `ManaCore` | Shared interfaces and data types |
| `ManaGraphics` | WebGPU backend and WGSL shaders |
| `Mana` | Engine, scene graph, components, and game modes |
| `ManaUI` | Retained-mode UI framework |
| `ManaTests` | Headless test suite |
| `ManaEditor` | Main runnable editor and game |

## Build and run

Install the .NET 10 SDK, then run these from the repository root:

```sh
dotnet build
dotnet run --project ManaEditor
dotnet run --project ManaTests
```

The headless tests do not require a GPU or window. See `AGENTS.md` for editor automation, browser builds, content
formats, and additional test-running instructions.

## License and third-party notices

The root [`LICENSE`](LICENSE) covers ManaEngine's original source code, not every file in the repository. Project
assets are either original work by the author or CC0, except for the separately licensed third-party material noted
below. Fonts, shaders, and dependencies may have their own terms; retain their accompanying license and attribution
notices. Do not infer permission to reuse a third-party asset from the repository's source-code license.

Notable bundled material with recorded terms:

- Kenney asset packs are CC0; their original archives are in `Assets/` and retain their included notices.
- The Flight Helmet, Lantern, and Water Bottle glTF samples include Microsoft CC0 notices beside the models.
- Antonio and Barlow Condensed fonts include their SIL Open Font Licenses in their font folders.
- The atmosphere shader includes Sebastian Lague's MIT notice. The liquid-fill shader credits Mirza Beig and records
  the author's reuse permission in the shader file.
- Poly Haven environment images are CC0; the moon texture is from NASA's public-domain CGI Moon Kit.
- Natural Earth data (`Assets/GeoData/`, and the planet's coarse start grid `Assets/Planet/baseline.tif` made from it by `Tools/Planet/make_baseline.py`) is public domain. Planet detail streams from Terrarium tiles at run time.
- Third-party libraries retain the licenses of their respective packages; see each package's distribution for its
  applicable terms.
- Desktop image decoding and encoding use StbImageSharp and StbImageWriteSharp (ports of stb_image; public domain /
  MIT).

Assets without an individual notice are original work by the author or CC0.
