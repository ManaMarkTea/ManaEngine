# ManaVoxel.Web — the browser build

Play mode running in a browser: WebGPU rendering, real keyboard/mouse input, BepuPhysics, a
bundled level, and a controllable character. No editor.

See `WebPort-Plan.md` at the repo root for the porting history and the reasoning behind the
browser-specific workarounds.

## Build and run

```bash
# 1. Publish (AOT; takes a few minutes)
dotnet publish ManaVoxel.Web/ManaVoxel.Web.csproj -c Release

# 2. Serve
python3 ManaVoxel.Web/serve.py          # http://localhost:8000  (or pass a port)
```

Then open <http://localhost:8000>. First load takes **10–15 seconds** — it's AOT-compiled wasm
plus a 5 MB environment map and two asset packs. A loading screen covers the whole wait; see
below.

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
(`index.html`) rather than the engine's own ManaUI/ImGui one. That is not a shortcut — it is the
only thing that can cover the first and longest phase, because no C# is running during it:

| Phase | Roughly | Driven by |
|---|---|---|
| Downloading ~45 MB of `_framework` + 13 MB of bundled assets | 10s on a fast link | `main.js`, counting bytes off the wire |
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

Two places, both in this project:

1. `Program.cs` — the path passed to `BrowserPlayGameMode`.
2. `ManaVoxel.Web.csproj` — a `WasmFilesToIncludeInFileSystem` entry so the file is actually in
   the wasm virtual filesystem, plus any asset packs the level references.

A level's mesh nodes reference packs as base64 `zipasset-rel:` strings. To find which ones a
level needs:

```bash
python3 - <<'EOF'
import json, base64
d = json.load(open("Levels/Little Train Yard.manalevel"))
def walk(n):
    a = n.get("asset")
    if a and a.startswith("zipasset-rel:"):
        print(base64.b64decode(a[len("zipasset-rel:"):].split(":")[0]).decode())
    for c in n.get("children") or []: walk(c)
for n in d["nodes"]: walk(n)
EOF
```

Currently bundled: `Little Train Yard` and `Sample Course`, with `kenney_train-kit.zip` (5.3 MB)
and `kenney_mini-characters.zip` (2.4 MB). Only bundle what a level actually needs — everything
here is download weight.

## Things that differ from desktop, and why

- **BepuPhysics is interpreted, not AOT'd.** It crashes Mono's LLVM AOT compiler outright
  (internal assertion in `mini-llvm.c` on its SIMD generics). `_AOT_InternalForceInterpretAssemblies`
  scopes that to Bepu; everything else is still AOT.
- **The tonemap shader encodes sRGB itself.** WebGPU requires the canvas use
  `getPreferredCanvasFormat()`, always a non-sRGB format, so the browser can't get the automatic
  linear→sRGB transfer desktop's `Srgb` surface provides. `ManaWgslPreprocessor` understands
  `#if MANA_BROWSER` for this.
- **Reflection-based JSON and trimmer roots are on.** Level DTOs are only ever constructed by
  `System.Text.Json`'s reflection, which the trimmer can't see.
- **No editor, no ImGui.** `ManaVoxel.csproj` is never referenced; the handful of level-loading
  and character-spawning sources it owns are linked in by source instead (see the `Compile
  Include` block in the csproj).

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
