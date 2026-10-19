# Code Screensaver

Pages URL: https://manamarktea.github.io/ManaRenderer/code-screensaver/

The Pages assembly exports real `.cs` and `.wgsl` files from the engine's source directories into
`sources.json`. It refreshes on each deployment. Terminals tile the viewport with outer edges flush to the
screen borders; only their contents scroll. The newest line types character by character before the pane
scrolls, with independent pauses and bursts. This standalone Canvas page needs no WASM, GPU or external
libraries. Text is drawn at the device pixel ratio without CSS scaling or blur.
Cached C#/WGSL syntax highlighting colors keywords, types, methods, strings, numbers, comments and
directives, retaining multiline comment and string state when a pane starts partway through a file.

One pane is the “finance department”: a live spend tally, input/output tokens, throughput chart, per-agent
costs and rotating commentary. Usage is simulated from source reading and characters typed (four characters
per token), with fictional $3/$15 input/output tariffs per million tokens. It makes no API calls. Pause
freezes the tally; Stop/Start resets it. The engine uses the same rules in `AgentLedger.cs`.

The full-width top bar holds Pause, Stop/Start, Sound, the current session status and a local clock
that updates every second, including while paused. Full screen is
centered at the top in HTML. Sound starts off until clicked, as browsers require. P pauses, S stops or
starts fresh sessions, M toggles sound, and F toggles full screen. Hidden tabs suspend audio and drawing;
reduced-motion users start paused. The engine's Stop button leaves play mode.

Any public GitHub repository can stand in for ManaRenderer: type `owner/name` or its github.com URL (a
`/tree/<branch>` URL picks the branch) into the bar and press Enter; blank goes back to ManaRenderer. Only
code files are shown -- C#, Python, Rust, Go, JS/TS, C/C++, shaders and the like, not docs, data or markup --
skipping vendored and build folders, minified files and anything under 600 bytes or over 160 KB; 48 are
sampled at random through GitHub's API (unauthenticated: 60 calls an hour, two per repository). `?repo=`
opens the page on one, and the last repository and the ten most recent are kept in the browser
(`localStorage`). The engine mode does the same with its `Repository` setting or the bar's box and Recent
list, keeping them in `%LOCALAPPDATA%/ManaRenderer/code-screensaver.json` (`GITHUB_TOKEN` raises the limit).
Highlighting knows `#` comments (Python, Ruby, shell...) and `--` comments (Lua, SQL, Haskell).

Local preview:

```powershell
python Tools/WebAssets/publish_screensaver.py --output Artifacts/ScreensaverSite/code-screensaver
python -m http.server 8127 --directory Artifacts/ScreensaverSite
```

The engine level is `Content/Levels/Code Screensaver.manalevel`; run with
`dotnet run --project ManaVoxel -- --play "Content/Levels/Code Screensaver.manalevel"`.
Its Inspector settings are Columns, ScrollSpeed, Seed (zero randomizes), KeyboardVolume and Repository
(empty: the last one picked, else this checkout).
The desktop version reads the checkout at play start and falls back to embedded source files.
It lives in `Games/CodeScreensaver`, with separate mode, rendering, source loading, terminal state and audio files;
`ManaVoxel/Code/Levels/ManaGames.cs` registers it with the editor.
