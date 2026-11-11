# Repository tools

Audited 2026-10-29 against project files, CI, documentation, generated outputs and automation command handlers.
These are development tools, not runtime dependencies unless a build explicitly invokes them.

| Folder | Purpose / why kept |
|---|---|
| `Assets` | Generates the model/picture catalog used to author content and checked by tests. |
| `Audio` | Reproduces the original starter and Sample Course sound packs. |
| `AudioSmoke.Web` | Isolated browser audio harness; instructions in `ManaAudio/README.md`. |
| `Automation` | In-app capture/profiling runner, particle benchmark, sizzle reel and scene thumbnails. `Checks/` contains regression scenarios, including focused Train Yard Wars cases. All source level/prefab/asset paths were checked; the old Bistro path was repaired. |
| `Benchmarks` | Bistro performance measurements and optional NVIDIA reference-image downloads. Opening a checked-in Bistro level now handles the source pack itself. |
| `Fonts` | Reproduces the adjusted Kenney font metrics from the original licensed pack. |
| `ManaLidar` | Windows-to-Mac deployment, device installation and signing setup. |
| `Materials` | Downloads/rebuilds compact licensed material packs using the source manifest. |
| `Mobile` | iOS wgpu-native build, mobile asset selection and app-icon generation. |
| `PixelFx` | `parity.mjs` writes pixelfx's own per-frame hashes of each ported effect (a checkout at `../pixelfx`) for `PixelFxParityChecks`. |
| `Profiling` | Collects desktop .NET performance traces. |
| `Sky` | Generates the constellation data shared by shaders and C#. |
| `Sprites` | Reproduces the bow/arrow sprite assets. |
| `Terrain` | Reproduces the terrain layer atlases from the licensed material packs. |
| `ThumbnailBaker` | Headless thumbnail executable called by web asset publishing. |
| `WebAssets` | Immutable asset catalogs, Pages/itch packaging, browser driver and their tests. |

Removed obsolete initial-authoring tools:

- `Levels/make_sky_and_garden.py`: rewrote the authored Night Sky level with fresh node IDs; Particle Garden had already been removed from its output.
- `Runner/make_sections.py`: deleted and rebuilt the authored Runner prefabs and rewrote its level. Those are maintained in the editor now.

Keep regression fixtures even when no production script calls them: they are intentionally run by developers.
Keep asset generators that explain how to reproduce bundled binary assets. One-off investigations belong in `Artifacts/`.

## Preparing downloadable levels for the web

From the repository root:

```powershell
dotnet run --project ManaEditor -- --prepare-level-assets
dotnet publish ManaEditor.Web/ManaEditor.Web.csproj -c Release -m:1
python ManaEditor.Web/serve.py 8123
```

The preparation command downloads/checks/imports the source packs declared by checked-in levels. It opens no window.
The web publisher follows each level's `Library/Imported/` references and glTF sidecars, publishing only those files as
immutable catalog entries. Source ZIPs and unused importer files stay out of the web output. Only web-published levels count,
so Bistro (desktop-only) is never imported on the build machine.

Imported DDS textures use their existing mip chain from 512 px down in web output; desktop files stay full resolution.
This keeps the site within Pages hosting limits and reduces browser memory use without decoding/recompressing blocks.
