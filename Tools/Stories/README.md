# Stories: portrait videos from an idea

Three tools turn an idea into a 1080 x 1920 MP4 for Instagram or TikTok stories, filmed by ManaEngine through the retro
filter:

- `generate.py "<idea>"` asks Claude for a story (JSON, the format below), checks it against what the engine has and
  sends any problems back to be fixed, then saves it in `Content/Stories/`. Needs an Anthropic API key.
- `story.py <story.json>` checks a story, writes the ManaEditor command script, renders it in a portrait window
  (`--window-size 1080x1920`), and captions, crossfades, mixes music and sound, and encodes it with ffmpeg.
- `catalog.py` prints what a story can use: levels, every placeable model with its size, characters and their
  animation clips, prefabs, music, sounds and fonts. It is what Claude reads; read it yourself to write one by hand.

```bash
python Tools/Stories/generate.py "Sam Altman took my RAM. Getting it back took a heist." --draft
python Tools/Stories/story.py "Content/Stories/<Title>.json" --draft     # half size, ~4x quicker: check it
python Tools/Stories/story.py "Content/Stories/<Title>.json"             # -> Artifacts/Stories/<title>.mp4
python Tools/Stories/story.py <story> --check                            # only the checker
python Tools/Stories/story.py <story> --encode-only                      # re-caption / re-mix frames already rendered
```

## The story format

```json
{
  "title": "NIGHT SHIFT",                       // names the files; also the subtitle's story
  "subtitle": "a ManaEngine story",            // optional small line over the first shot (when it has no title)
  "size": [1080, 1920], "fps": 30,              // the defaults
  "retro": "VHS",                               // Default, VHS, Console, CRT, or "off"; "lines": 240 sets its pixel height
  "transition": 0.4,                            // crossfade seconds between shots; 0 cuts
  "font": "Assets/Fonts/Antonio/Antonio-Bold.ttf",        // captions and titles ("titleFont" for titles alone)
  "music": { "path": "Assets/Audio/Music/Monkeys Spinning Monkeys.wav", "volume": 0.5, "start": 0 },
  "shots": [
    {
      "level": "Content/Levels/Stages/Empty Stage.manalevel",
      "clear": false,                           // true removes everything placed earlier (a level change always does)
      "set": [                                  // placed before the shot starts; stays for later shots on this level
        { "name": "Mio",  "asset": "zip:kenney_blocky-characters_20.zip/Models/GLB format/character-a.glb",
          "position": [46, 0, 48], "yaw": 90, "scale": 0.65 },
        { "name": "Desk", "asset": "zip:kenney_furniture-kit.zip/Models/GLTF format/desk.glb",
          "position": [49, 0, 47], "yaw": 0, "scale": 2.5 },
        { "name": "Lamp", "asset": "Prefabs/Lamp.manaprefab", "position": [51, 0, 46] }
      ],
      "from": { "position": [48, 1.6, 53], "target": [48, 1.1, 47] },    // the camera at the start
      "to":   { "position": [48, 1.4, 51], "target": [48, 1.1, 47] },    // and the end (leave out to hold still)
      "fov": 60,                                // vertical degrees
      "seconds": 4,
      "sky": { "name": "DayNightCycle", "timeOfDay": 0.8, "toTimeOfDay": 0.9 },   // optional
      "actions": [                              // the timeline, "at" seconds into the shot
        { "at": 0.2, "type": "walk", "name": "Mio", "position": [48, 0, 48], "seconds": 1.5 },
        { "at": 1.8, "type": "face", "name": "Mio", "target": [48, 1, 53] },
        { "at": 2.0, "type": "animate", "name": "Mio", "clip": "emote-yes", "loop": false },
        { "at": 3.0, "type": "place", "name": "Ball", "asset": "Prefabs/Coin.manaprefab", "position": [48, 1, 49] },
        { "at": 3.5, "type": "remove", "name": "Ball" }
      ],
      "title": { "text": "NUCLEI", "position": [0, 0.5], "scale": 0.09 },  // or just "NUCLEI"
      "caption": "Sam Altman took my RAM.",     // the subtitle under the picture
      "sounds": [ { "path": "Assets/Audio/Starter/UI/notification.wav", "at": 0.5, "volume": 0.8 } ],
      "retro": "CRT",                           // optional, this shot only
      "commands": []                            // optional raw ManaEditor script commands before the shot (AGENTS.md)
    }
  ]
}
```

- **Coordinates** are world metres, +Y up. Levels sit at x and z of 32 and above. `Content/Levels/Stages/Empty Stage.manalevel`
  is a bare 24 m floor centred on [48, 0, 48] to build a set on.
- **Yaw** is degrees round +Y. Kenney models face +Z: yaw 0 faces a camera at larger z, 90 faces +X, 180 faces -Z.
- **Assets** are a model -- `zip:<pack>.zip/<path>.glb`, as `catalog.py` lists them -- a loose `.glb` by repo path, or a
  prefab by its path under `Content/` (`Prefabs/Lamp.manaprefab`).
- **Characters** (`kenney_blocky-characters_20`) animate: `idle` when placed, `walk` while walking, then `idle` again; any
  clip by `animate` (`emote-yes`, `emote-no`, `pick-up`, `sit`, `interact-right`, `attack-kick-right`, `die`...). They are
  2.7 m tall as authored, so scale 0.65 makes a 1.75 m person.
- **Actions**: `walk` (`position`, `seconds` -- about 1.4 m a second by default; `clip` other than walk, e.g. `sprint`),
  `face` (`target`, `seconds` to turn), `animate` (`clip`, `loop` -- true by default, `speed`), `place` (as in `set`),
  `remove`. Walks and turns run alongside whatever comes next.
- **Titles** are drawn in the scene in front of the camera (so the retro filter pixelates them, like an old game's title):
  `position` is the centre on screen, -1..1 across and up; `scale` the height as a share of the screen.
- **Captions** are clean subtitles over the finished picture, lower third, fading in and out.

The checker (`story.py --check`) reports, in sentences, every level, asset, clip, sound or music file that does not
exist, an action naming something not on stage, and shots that are too long (20 s each, 90 s in all).

## Running it on a Linux server (a VPS)

No screen is needed: with no `DISPLAY` the editor runs under `xvfb-run`. Without a GPU, Vulkan falls back to Mesa's
software rasterizer (lavapipe) -- set `MANA_STORY_SOFTWARE=1` to insist on it. Recording steps the game clock a frame
at a time, so a slow machine only takes longer; the video still plays at full speed. On lavapipe a full-size frame
takes about a second (a 15 s story: ~10 minutes); `--draft` renders a quarter of the pixels.

```bash
# Ubuntu 24.04: .NET 10, a virtual screen, software Vulkan, ffmpeg, Python with Pillow and the Anthropic SDK
sudo apt-get install -y dotnet-sdk-10.0 xvfb mesa-vulkan-drivers libvulkan1 ffmpeg python3 python3-pip
pip install anthropic pillow
git clone https://github.com/ManaMarkTea/ManaEngine && cd ManaEngine
export ANTHROPIC_API_KEY=...            # for generate.py
MANA_STORY_SOFTWARE=1 python3 Tools/Stories/generate.py "<idea>" --render
```

Copy `Artifacts/Stories/<title>.mp4` off the server (`scp`) and post it. The music is Kevin MacLeod's, CC BY 4.0: credit
him in the post (`Assets/Audio/Music/README.md`).
