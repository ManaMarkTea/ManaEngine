# ManaRenderer

ManaRenderer is a C# renderer and game engine with a voxel-level editor. WebGPU is the primary graphics backend;
OpenGL remains available as a fallback. The projects target .NET 10, Windows, and macOS (arm64).

## Projects

| Project | Purpose |
| --- | --- |
| `ManaCore` | Shared interfaces and data types |
| `ManaGraphics` | WebGPU and OpenGL backends and shaders |
| `Mana` | Engine, scene graph, components, and game modes |
| `ManaUI` | Retained-mode UI framework |
| `ManaTests` | Headless test suite |
| `ManaVoxel` | Main runnable editor and game |

## Build and run

Install the .NET 10 SDK, then run these from the repository root:

```sh
dotnet build Mana/Mana.sln
dotnet run --project ManaVoxel
dotnet run --project ManaTests
```

The headless tests do not require a GPU or window. See `CLAUDE.md` for editor automation, browser builds, content
formats, and additional test-running instructions.

## License and third-party notices

The root [`LICENSE`](LICENSE) covers ManaRenderer's original source code, not every file in the repository. Project
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
- Third-party libraries retain the licenses of their respective packages; see each package's distribution for its
  applicable terms.
- SixLabors.ImageSharp (3.x, used by the desktop builds for image decoding and encoding) is under the Six Labors
  Split License: free for open-source use and for companies under US$1M annual gross revenue, otherwise a
  commercial license is required. If that ever applies, buy a license or switch to an MIT-licensed library such as
  SkiaSharp.

Assets without an individual notice are original work by the author or CC0. The Duck and Sci-Fi Helmet samples were
removed because their redistribution terms were not established.
