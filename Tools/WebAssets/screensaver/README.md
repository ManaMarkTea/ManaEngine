# Code Screensaver

Pages URL: https://manamarktea.github.io/ManaRenderer/code-screensaver/

The Pages assembly exports real `.cs` and `.wgsl` files from the engine's source directories into
`sources.json`. It refreshes on each deployment. Terminals tile the viewport with outer edges flush to the
screen borders; only their contents scroll. This standalone Canvas page needs no WASM, GPU or
external libraries. Text is drawn at the device pixel ratio without CSS scaling or blur.

The right-side button toggles soft synthesized keyboard sounds (off until clicked, as browsers require).
P pauses, M toggles sound, and F toggles full screen. Bottom controls fade after inactivity. Hidden
tabs suspend audio and drawing; reduced-motion users start paused.

Local preview:

```powershell
python Tools/WebAssets/publish_screensaver.py --output Artifacts/ScreensaverSite/code-screensaver
python -m http.server 8127 --directory Artifacts/ScreensaverSite
```

The engine level is `Content/Levels/Code Screensaver.manalevel`; run with
`dotnet run --project ManaVoxel -- --play "Content/Levels/Code Screensaver.manalevel"`.
Its Inspector settings are Columns, ScrollSpeed, Seed (zero randomizes), and KeyboardVolume.
The desktop version reads the checkout at play start and falls back to embedded source files.
