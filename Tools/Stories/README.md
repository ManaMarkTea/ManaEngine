# Stories: portrait videos from a few lines of JSON

`story.py` turns a story -- shots with captions -- into a 1080 x 1920 MP4 for Instagram or TikTok stories, shot
through the retro filter. It writes a ManaEditor command script, runs the editor in a portrait window
(`--window-size 1080x1920`), records each shot frame by frame, then captions, crossfades and encodes with ffmpeg.

```bash
python Tools/Stories/story.py "Content/Stories/Night Shift.json"      # -> Artifacts/Stories/night-shift.mp4
python Tools/Stories/story.py <story> --script-only                    # only the command script (Artifacts/Stories/<title>/script.json)
python Tools/Stories/story.py <story> --encode-only                    # re-caption / re-encode frames already rendered
```

## A story

```json
{
  "title": "NIGHT SHIFT",                 // over the first shot (leave out for none); "subtitle" below it
  "size": [1080, 1920], "fps": 30,        // the defaults
  "retro": "VHS",                         // Default, VHS, Console, CRT, or "off"; "lines" sets its pixel height
  "transition": 0.4,                      // crossfade seconds between shots, 0 for cuts
  "font": "Assets/Fonts/Antonio/Antonio-Bold.ttf",
  "shots": [
    {
      "level": "Content/Levels/Light Show.manalevel",
      "from": { "position": [64, 6.5, 80], "target": [64, 2.5, 48] },
      "to":   { "position": [64, 4, 66],   "target": [64, 2.5, 48] },   // leave out to hold still
      "fov": 70,                          // vertical degrees: portrait wants it wider than the editor's 55
      "seconds": 4,
      "caption": "11:58 PM. The lights are still on.",
      "sky": { "name": "DayNightCycle", "timeOfDay": 0.8, "toTimeOfDay": 0.9 },   // optional; runs the day on during the shot
      "retro": "CRT",                     // optional, this shot only
      "commands": []                      // optional extra script commands before recording (AGENTS.md lists them)
    }
  ]
}
```

Camera positions are world coordinates; a level's own `camera` block and the editor's camera (read off while flying
round) are good places to start. Any script command can go in `commands` -- `wind`, `emission`, `lighting`...

## Running it on a Linux server (a VPS)

No screen is needed: with no `DISPLAY` the editor runs under `xvfb-run`. Without a GPU, Vulkan falls back to Mesa's
software rasterizer (lavapipe) -- set `MANA_STORY_SOFTWARE=1` to insist on it. Recording steps the game clock a frame
at a time, so a slow machine only takes longer; the video still plays at full speed.

```bash
# Ubuntu 24.04: .NET 10, a virtual screen, software Vulkan, ffmpeg
sudo apt-get install -y dotnet-sdk-10.0 xvfb mesa-vulkan-drivers libvulkan1 ffmpeg python3
git clone https://github.com/ManaMarkTea/ManaEngine && cd ManaEngine
MANA_STORY_SOFTWARE=1 python3 Tools/Stories/story.py "Content/Stories/Night Shift.json"
```

On lavapipe a 12-second, three-shot story takes a few minutes per shot of rendering on a few cores. Copy
`Artifacts/Stories/<title>.mp4` off the server (`scp`) and post it.
