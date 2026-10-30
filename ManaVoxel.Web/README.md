# ManaVoxel.Web — the browser build

One browser host provides standalone play and the full editor, sharing the engine, WebGPU,
audio, physics and one AOT-compiled runtime bundle. Open `?mode=editor` for the editor;
the default page is the play level selector.

## Levels and the menu

The page opens on a level selector: every level in `Content/Levels` and each game's `Games/<Name>/Content/Levels` whose own `web` block says
`"publish": true` (the scene's Publishing section in the editor's Inspector), with its `description`, in its
`order`, and a picture from `Pictures/<name>.jpg` beside it. `Tools/WebAssets/publish_assets.py` writes the
list (`levels.json`) at publish time. A level's game modes run here as on desktop; one that builds its world in
code names what to fetch ahead in `ManaLevelGameMode.Assets`, and one that handles Escape keeps the page's menu shut. Picking one reloads the page with
`?level=<its path>` (`Content/Levels/<file>`, or `Games/<Name>/Content/Levels/<file>`; an older link to a game level under `Content/Levels/` is found by name); only then does the engine download, and the level is the program's argument.

Escape while playing opens the pause menu (Resume, Restart level, Choose another level); the game stops
while it is open and the page keeps its keys and clicks. The menu talks to the game through
`BrowserGameControls` (JS exports from this assembly).

## Deployment

CI publishes this project once and builds one versioned `app/v/<commit>/` directory.
The existing `/editor/` and `/play/` links redirect there with the appropriate mode,
keeping level and graphics parameters. Build and deployment share a runner, so there
are no staging artifacts. The Pages upload is deleted after successful deployment.
Both modes download the editor-capable runtime; assets remain on demand.

## Build and run

```bash
# 1. Publish (AOT; takes a few minutes)
dotnet publish ManaVoxel.Web/ManaVoxel.Web.csproj -c Release

# 2. Serve
python3 ManaVoxel.Web/serve.py          # http://localhost:8000  (or pass a port)
```

Then open <http://localhost:8000>. Only the runtime and boot essentials load initially;
models and their dependencies are downloaded from `AssetLibrary/` as needed. The initial
level's dependencies are prepared before play starts. A loading screen covers startup.

Publish requires Python 3.11+ (`python` on PATH; override with `-p:ManaPython=python3`).
Deploy the entire `AppBundle/` directory, including `asset-catalog.json` and `AssetLibrary/`.
There is no monolithic asset archive to commit or upload. See the editor README's
"Deployment and assets" section for caching, retries, and deployment details.

The server reads files off disk per request, so after a republish just refresh — no restart.

### Why `serve.py` and not `python3 -m http.server`

It sends `no-store` on everything. `mana_interop.js` is pulled in by `JSHost.ImportAsync` as a
dynamic import, which Chrome will serve from cache even through a hard reload — so a fresh
`.wasm` ends up calling into stale JS, surfacing as bewildering errors like
`installInput must be a Function but was undefined`. The import URL also carries a
per-load cache-buster for the same reason; belt and braces, because this bites hard when it
happens.

## The loading screen

The wait here is much longer than desktop's and starts much earlier, so the screen is HTML
(`index.html`) rather than the engine's own ManaUI one. That is not a shortcut — it is the
only thing that can cover the first and longest phase, because no C# is running during it:

| Phase | Roughly | Driven by |
|---|---|---|
| Downloading `_framework` and boot assets | Depends on connection speed | `main.js`, counting bytes off the wire |
| `BrowserPlayGameMode.Load` — env map decode, level, meshes, character | 2–4s | C# → `mana_interop.js` |
| Renderer init and GPU uploads (`ManaLoadingCoordinator`) | <1s | same |

The bar gives the download 70% and the engine load the other 30%.

Two things worth knowing before changing any of it:

- **Every animated property is `opacity` or `transform`, deliberately.** Browser wasm is
  single-threaded, so `Load` holds the main thread for seconds at a stretch. Those two properties
  animate on the compositor and keep moving through it; anything else (a `width` transition, a
  JS-driven spinner) freezes solid at exactly the moment the screen most needs to look alive.
- **Download progress counts bytes, not files.** `dotnet.js` offers
  `onDownloadResourceProgress(loaded, total)`, but that counts resources, and `dotnet.native.wasm`
  alone is 23 MB of ~60 files — a file count sits near zero for most of the wait and then leaps.
  `main.js` wraps `globalThis.fetch` instead (the loader looks it up per call, so replacing it
  before `dotnet.create()` is enough). The wrapper copies response headers wholesale because
  `dotnet.native.wasm` goes through `WebAssembly.instantiateStreaming`, which drops to buffering
  the whole 23 MB if the `application/wasm` content type goes missing.

The colours and metrics in `index.html` are copied from ManaUI's dark `UiTheme` and
`ManaLoadingScreenManaView`, so this and the in-engine screen are the same picture. If the theme
moves, move them with it.

A boot failure keeps the screen up and prints the reason on it, rather than leaving a black canvas
and a stack trace in a console nobody had open.

## Controls

Phones show a movement stick, a drag-to-look pad, and Jump, Run, Camera, and Menu buttons.
Movement, looking, and actions support separate fingers at the same time. Pausing, rotating,
or backgrounding the page releases held controls. FPS appears at the upper left during play.

| Input | Action |
|---|---|
| `W` `A` `S` `D` / arrows | Move |
| `Shift` | Run |
| `Space` | Jump |
| `C` | Cycle camera (Free → Follow → Orbit) |
| Right-drag | Orbit / look |
| Scroll | Zoom |
| `+` / `-` (keypad) | Camera distance |

## Changing the level

Change the path passed to `BrowserPlayGameMode` in `Program.cs` to a level under `Content/Levels/`.

Both browser projects import `WebAssets.props`, which embeds shaders, two Roboto fonts,
default textures, the mini-character pack, and all `.manalevel` files under `Content/Levels/` and the games' `Games/*/Content/`.
`Tools/WebAssets/publish_assets.py` publishes every file under `Assets/`,
`ManaGraphics/Assets/`, `ManaUI/Assets/`, and `ManaVoxel/Assets/` as separate hashed files,
plus a small catalog. The only exclusion is the oversized desktop planet source
`Assets/Planet/ETOPO_2022_v1_60s_N90W180_surface.tif`.

The initial level's models/packs and glTF sidecars download before its synchronous loader
runs. Subsequent uses reuse staged files or the persistent browser cache. New assets are
picked up automatically; no per-level packaging allowlist needs updating.

## Things that differ from desktop, and why

Downloaded scene levels (including `Content/Levels/Downloaded/Bistro/`) also work in the browser. Before a local web
publish, run `dotnet run --project ManaVoxel -- --prepare-level-assets` to fetch and import their declared source packs.
The web publisher includes only referenced `Library/Imported/` files and glTF sidecars in the external asset catalog;
they download when the level opens, with the usual persistent browser cache. The daily workflow prepares these packs
automatically. Imported DDS textures start at their existing 512 px mip to fit browser memory and hosting limits;
desktop keeps full resolution. Neither the ZIP nor the desktop FBX converter is sent to the browser. See `Tools/README.md`.

- **Phones and tablets use mobile rendering automatically** (touch input with a coarse primary pointer).
  Their canvas is capped at one render pixel per CSS pixel. A lightweight sky shader draws day/night
  color gradients, a simple sun and moon, and sparse procedural stars. Both the background and lighting
  cubes use it, avoiding atmospheric scattering and clouds on mobile. `?sky=simple|stars|full` picks the
  sky anywhere: `stars` is the lightweight sky plus the real star catalogue and constellation lines (read
  from a storage buffer, so they cost only on night-sky pixels), `full` the desktop atmosphere. The level
  selector and the pause menu set it, and `?rendering=`, from their Sky and Graphics settings.
  Environment/reflection cubes are capped at 128 px and irradiance at 16 px. Desktop quality is unchanged.
  GPU particles share a 20,000-particle starting budget across all emitters, proportional to their requested
  counts. Sustained frames slower than 38 ms lower it; five seconds of frames faster than 25 ms raise it
  gradually, within 1,000–100,000 particles. Paused/background frames are ignored. Saved emitter counts
  stay unchanged, and reduced streams still cover the full lifetime. Renderer diagnostics report requested
  and drawn counts. `?rendering=mobile` or `?rendering=desktop` overrides detection for testing (combine
  with `&level=...`); `node Tools/WebAssets/mobile-rendering.test.mjs` checks the policy.

- **BepuPhysics is interpreted, not AOT'd.** It crashes Mono's LLVM AOT compiler outright
  (internal assertion in `mini-llvm.c` on its SIMD generics). `_AOT_InternalForceInterpretAssemblies`
  scopes that to Bepu; everything else is still AOT.
- **The canvas is configured with an sRGB `viewFormats` entry.** WebGPU requires
  `configure()` be given `getPreferredCanvasFormat()`, always a non-sRGB format, and Dawn rejects
  the `-srgb` variant outright. Passing `viewFormats: ['bgra8unorm-srgb']` is the sanctioned way
  back: the canvas stays `bgra8unorm`, but `getCurrentTexture()` will hand out an sRGB view, and
  `ManaWebGpuContext` promotes `_surfaceFormat` to the `Srgb` variant so every pipeline targets it.
  That gets the automatic linear→sRGB transfer on write, exactly as desktop's `Srgb` surface does.
  The legacy Emscripten swapchain can't express `viewFormats`, so `mana_webgpu.c` calls
  `context.configure()` directly through `EM_JS` instead (`mana_wgpuSurfaceConfigure`).

  Encoding sRGB in `hdr_tonemap.wgsl` instead — the first attempt — is the wrong layer, and the
  editor build is what proves it: the tonemap pass is only one of three things that write to the
  surface, with ManaUI drawing into the same pass afterwards. Those came out a gamma
  dark, and encoding per-shader would still have blended the UI in the wrong space.
- **Reflection-based JSON and trimmer roots are on.** Level DTOs are only ever constructed by
  `System.Text.Json`'s reflection, which the trimmer can't see.
- **Shared editor and play host.** `ManaVoxel.csproj` is referenced with its browser-wasm target;
  both startup modes use its level loaders and the same engine assemblies. ManaUI supplies the UI.

## Testing without a browser

A headless Chrome probe via Playwright is how most of this was verified — it catches real bugs
and needs no human in the loop:

```bash
mkdir -p /tmp/pw && cd /tmp/pw && npm install playwright
```

```js
import { chromium } from 'playwright';
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
page.on('console', m => console.log(m.text()));
page.on('pageerror', e => console.log('[pageerror] ' + e.message));
await page.goto('http://localhost:8000/index.html', { waitUntil: 'load' });
await page.waitForTimeout(17000);        // AOT boot + level + environment map
await page.screenshot({ path: 'shot.png' });
await browser.close();
```

`channel: 'chrome'` reuses the installed Chrome, so there's no separate browser download. A fresh
profile also has no cache, which is why this probe never reproduced the stale-JS problem the real
browser hit.

## If a publish fails oddly

`"'X 2.dll' doesn't match original file name"` during AOT means duplicate files in `obj/`. Clean
and retry:

```bash
rm -rf ManaVoxel.Web/obj ManaVoxel.Web/bin
```

This used to happen constantly when the repo lived under `~/Documents` (iCloud created `X 2.dll`
conflict copies mid-build, and once corrupted `.git`). Outside a synced folder it should be rare.
