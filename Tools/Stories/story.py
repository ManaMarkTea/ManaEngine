"""Turns a story -- shots with sets, actors, titles and captions -- into a portrait video for Instagram/TikTok stories.

    python Tools/Stories/story.py "Content/Stories/Night Shift.json"            # check, script, render, encode
    python Tools/Stories/story.py <story> --draft                                # a quick half-size preview first
    python Tools/Stories/story.py <story> --check                                # only check the story
    python Tools/Stories/story.py <story> --script-only                          # just write the command script
    python Tools/Stories/story.py <story> --encode-only                          # re-encode frames already rendered

A story is JSON (Tools/Stories/README.md has the format; Tools/Stories/generate.py writes one from an idea): a size,
a retro look, music, and shots -- each a level, things placed on it (props, characters, prefabs), a camera move, a
timeline of actions (walk, animate, face...), an in-world title, a caption and sounds. The script it writes drives
ManaEditor (`--command-file`) in a window of the story's size, recording each shot frame by frame; ffmpeg then captions
each shot, crossfades them, mixes the music and sounds, and writes an H.264 + AAC MP4.

On a server with no screen (a VPS) the editor runs under xvfb-run, on the GPU if it has one or else Mesa's software
Vulkan (lavapipe) -- slow, but recording steps time by the frame, so the video plays at full speed regardless.
"""
import argparse
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time
import zipfile
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_FONT = "Assets/Fonts/Antonio/Antonio-Bold.ttf"
RETRO_LOOKS = ("Default", "VHS", "Console", "CRT")
ACTIONS = ("walk", "animate", "face", "remove", "place")
MAX_SHOT_SECONDS = 20.0
MAX_STORY_SECONDS = 90.0


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-") or "story"


# ----- reading and checking a story ---------------------------------------------------------------------------------

def load_story(path):
    with open(path, encoding="utf-8") as file:
        story = json.load(file)
    return normalize(story, Path(path).stem)


def normalize(story, fallback_title="story"):
    story.setdefault("title", fallback_title)
    story.setdefault("size", [1080, 1920])
    story.setdefault("fps", 30)
    story.setdefault("transition", 0.4)
    for shot in story.get("shots", []):
        shot.setdefault("seconds", 3.0)
        # Older stories kept the camera on the shot itself.
        camera = shot.get("camera")
        if camera:
            for key in ("from", "to", "fov"):
                if key in camera and key not in shot:
                    shot[key] = camera[key]
    return story


def asset_exists(asset):
    """A model (repo path or zip: pack path) or a prefab (a Content path) that the engine can place."""
    if asset.startswith("zip:"):
        pack, _, entry = asset[4:].partition("/")
        zip_path = ROOT / "Assets" / pack
        return zip_path.exists() and entry in zip_entries(str(zip_path))
    if asset.endswith(".manaprefab"):
        return (ROOT / "Content" / asset).exists()
    return (ROOT / asset).exists()


@lru_cache(maxsize=None)
def zip_entries(zip_path):
    with zipfile.ZipFile(zip_path) as archive:
        return frozenset(archive.namelist())


@lru_cache(maxsize=None)
def model_clips(asset):
    """The animation clips in a GLB (read from its JSON chunk), or an empty tuple for anything else."""
    data = None
    try:
        if asset.startswith("zip:"):
            pack, _, entry = asset[4:].partition("/")
            with zipfile.ZipFile(ROOT / "Assets" / pack) as archive:
                data = archive.read(entry)
        elif asset.endswith(".glb"):
            data = (ROOT / asset).read_bytes()
    except (OSError, KeyError, zipfile.BadZipFile):
        return ()
    if not data or data[:4] != b"glTF":
        return ()
    length = struct.unpack("<I", data[12:16])[0]
    gltf = json.loads(data[20:20 + length])
    return tuple(animation.get("name", "") for animation in gltf.get("animations", []))


def is_point(value, size=3):
    return isinstance(value, list) and len(value) == size and all(isinstance(v, (int, float)) for v in value)


def validate(story):
    """Every problem with the story, as sentences a person (or the generator's retry) can act on; empty when it is fine."""
    problems = []
    shots = story.get("shots")
    if not isinstance(shots, list) or not shots:
        return ["The story needs a non-empty \"shots\" list."]
    if story.get("retro") not in (None, "off", *RETRO_LOOKS):
        problems.append(f"retro must be one of {', '.join(RETRO_LOOKS)} or \"off\", not {story.get('retro')!r}.")
    music = story.get("music")
    if music:
        path = music.get("path") if isinstance(music, dict) else music
        if not path or not (ROOT / path).exists():
            problems.append(f"music file {path!r} does not exist.")

    total = 0.0
    placed = {}            # names on stage -> asset, carried from shot to shot on the same level
    level = None
    for index, shot in enumerate(shots):
        where = f"shot {index + 1}"
        if not shot.get("level") or not (ROOT / shot["level"]).exists():
            problems.append(f"{where}: level {shot.get('level')!r} does not exist.")
        if shot.get("level") != level or shot.get("clear"):
            placed = {}
        level = shot.get("level")
        seconds = shot.get("seconds")
        if not isinstance(seconds, (int, float)) or not 0.5 <= seconds <= MAX_SHOT_SECONDS:
            problems.append(f"{where}: seconds must be between 0.5 and {MAX_SHOT_SECONDS:g}.")
            seconds = 3.0
        total += seconds
        for pose_name in ("from", "to"):
            pose = shot.get(pose_name)
            if pose is None and pose_name == "to":
                continue
            if not isinstance(pose, dict) or not is_point(pose.get("position")) or not is_point(pose.get("target")):
                problems.append(f"{where}: \"{pose_name}\" needs a position and a target, each [x, y, z].")
        if shot.get("retro") not in (None, "off", *RETRO_LOOKS):
            problems.append(f"{where}: retro must be one of {', '.join(RETRO_LOOKS)} or \"off\".")
        for item in shot.get("set", []):
            problems += check_place(item, where, placed)
        for action in shot.get("actions", []):
            kind = action.get("type")
            at = action.get("at", 0)
            if kind not in ACTIONS:
                problems.append(f"{where}: action type {kind!r} is not one of {', '.join(ACTIONS)}.")
                continue
            if not isinstance(at, (int, float)) or at < 0 or at > seconds:
                problems.append(f"{where}: action at {at!r}s is outside the shot's {seconds}s.")
            if kind == "place":
                problems += check_place(action, where, placed)
                continue
            name = action.get("name")
            if name not in placed:
                problems.append(f"{where}: {kind} names {name!r}, which is not on stage (place it in \"set\" first).")
                continue
            if kind == "walk" and not is_point(action.get("position")):
                problems.append(f"{where}: walk {name} needs a position [x, y, z].")
            if kind == "face" and not is_point(action.get("target")):
                problems.append(f"{where}: face {name} needs a target [x, y, z].")
            if kind == "animate" or (kind == "walk" and action.get("clip")):
                clip = action.get("clip")
                clips = model_clips(placed[name])
                if kind == "animate" and not clip:
                    problems.append(f"{where}: animate {name} needs a clip.")
                elif clips and clip not in clips:
                    problems.append(f"{where}: {name} has no clip {clip!r}; it has {', '.join(clips)}.")
                elif not clips:
                    problems.append(f"{where}: {name} ({placed[name]}) has no animations to play.")
            if kind == "remove":
                placed.pop(name, None)
        title = shot.get("title")
        if title is not None and not (isinstance(title, str) or (isinstance(title, dict) and title.get("text"))):
            problems.append(f"{where}: title must be a string or {{\"text\": ...}}.")
        for sound in shot.get("sounds", []):
            if not (ROOT / str(sound.get("path", ""))).exists():
                problems.append(f"{where}: sound {sound.get('path')!r} does not exist.")
    if total > MAX_STORY_SECONDS:
        problems.append(f"The story runs {total:g}s; keep it under {MAX_STORY_SECONDS:g}s.")
    return problems


def check_place(item, where, placed):
    problems = []
    name, asset = item.get("name"), item.get("asset")
    if not name:
        problems.append(f"{where}: everything placed needs a name.")
    if not asset or not asset_exists(asset):
        problems.append(f"{where}: {name}: asset {asset!r} does not exist.")
    if not is_point(item.get("position")):
        problems.append(f"{where}: {name}: position must be [x, y, z].")
    if name and asset:
        placed[name] = asset
    return problems


# ----- the command script -------------------------------------------------------------------------------------------

def retro_command(look, lines):
    if look in (None, False, "off", "Off", "none"):
        return {"type": "retro", "visible": False}
    command = {"type": "retro", "name": look if isinstance(look, str) else "Default"}
    if lines:
        command["count"] = int(lines)
    return command


def place_command(item):
    command = {"type": "place", "name": item["name"], "path": item["asset"], "position": item["position"]}
    if "yaw" in item:
        command["yaw"] = item["yaw"]
    if "scale" in item:
        command["scale"] = item["scale"]
    return command


def action_command(action, fps):
    kind = action["type"]
    if kind == "place":
        return place_command(action)
    command = {"type": kind, "name": action["name"]}
    if kind == "walk":
        command["position"] = action["position"]
        command["background"] = True
        if "seconds" in action:
            command["frames"] = max(1, round(float(action["seconds"]) * fps))
        if action.get("clip"):
            command["clip"] = action["clip"]
    elif kind == "face":
        command["target"] = action["target"]
        command["frames"] = max(1, round(float(action.get("seconds", 0.4)) * fps))
        command["background"] = True
    elif kind == "animate":
        command["clip"] = action["clip"]
        if "loop" in action:
            command["loop"] = bool(action["loop"])
        if "speed" in action:
            command["speed"] = action["speed"]
    return command


def build_script(story, frames_dir, titles):
    """The ManaEditor command file: each shot staged, posed, then recorded while its camera and actions play out."""
    fps = int(story["fps"])
    commands = [{"type": "gizmos", "visible": False}]
    current_level = None
    for index, shot in enumerate(story["shots"]):
        folder = frames_dir / f"{index + 1:02d}"
        if shot["level"] != current_level:
            commands += [{"type": "remove", "name": "*"}, {"type": "open_level", "path": shot["level"]},
                         {"type": "wait", "frames": int(story.get("loadFrames", 120))}]
            current_level = shot["level"]
        elif shot.get("clear"):
            commands.append({"type": "remove", "name": "*"})
        commands.append(retro_command(shot.get("retro", story.get("retro", "Default")), shot.get("lines", story.get("lines"))))
        commands += shot.get("commands", [])
        commands += [place_command(item) for item in shot.get("set", [])]

        sky = shot.get("sky")
        if sky:
            start = {"type": "sky", "name": sky.get("name", "DayNightCycle")}
            if "timeOfDay" in sky:
                start["timeOfDay"] = sky["timeOfDay"]
            commands.append(start)
        if index in titles:
            title = titles[index]
            commands.append({"type": "title", "name": "Story Title", "path": title["path"], "position": title["position"],
                             "scale": title["scale"]})
        else:
            commands.append({"type": "title", "name": "Story Title", "visible": False})

        pose = {"type": "move_camera", "position": shot["from"]["position"], "target": shot["from"]["target"]}
        if "fov" in shot:
            pose["fov"] = shot["fov"]
        commands += [pose, {"type": "wait", "frames": 20}]

        # Recording held (visible false) for the clean full-window view to settle, then let go.
        frames = max(1, round(float(shot["seconds"]) * fps))
        commands += [
            {"type": "record", "path": folder.relative_to(ROOT).as_posix(), "clean": True, "frames": fps, "visible": False},
            {"type": "wait", "frames": 4},
            {"type": "record", "clean": True},
        ]
        if sky and "toTimeOfDay" in sky:
            commands.append({"type": "sky", "name": sky.get("name", "DayNightCycle"), "timeOfDay": sky["toTimeOfDay"],
                             "frames": frames, "background": True})
        if "to" in shot:
            move = {"type": "move_camera", "position": shot["to"]["position"], "target": shot["to"]["target"],
                    "frames": frames, "background": True}
            if "fov" in shot:
                move["fov"] = shot["fov"]
            commands.append(move)

        # The timeline: each action at its moment, the waits between them in frames.
        elapsed = 0
        for action in sorted(shot.get("actions", []), key=lambda action: action.get("at", 0)):
            at = min(frames, max(0, round(float(action.get("at", 0)) * fps)))
            if at > elapsed:
                commands.append({"type": "wait", "frames": at - elapsed})
                elapsed = at
            commands.append(action_command(action, fps))
        if frames > elapsed:
            commands.append({"type": "wait", "frames": frames - elapsed})
        commands += [{"type": "stop_record"}, {"type": "wait", "frames": 3}]
    commands.append({"type": "quit"})
    return commands


# ----- titles drawn for the engine to show in the scene -------------------------------------------------------------

def render_titles(story, out_dir):
    """Draws each shot's title to a PNG (Pillow) for the engine's title card; returns {shot index: title command fields}."""
    titles = {}
    wanted = [(index, shot["title"]) for index, shot in enumerate(story["shots"]) if shot.get("title")]
    if not wanted:
        return titles
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        sys.exit("In-world titles need Pillow: pip install pillow")
    out_dir.mkdir(parents=True, exist_ok=True)
    for index, title in wanted:
        if isinstance(title, str):
            title = {"text": title}
        font_path = ROOT / title.get("font", story.get("titleFont", story.get("font", DEFAULT_FONT)))
        font = ImageFont.truetype(str(font_path), 200)
        colour = tuple(title.get("color", [255, 255, 255]))
        shadow = tuple(title.get("shadow", [110, 30, 90]))
        lines = title["text"].split("\n")
        probe = ImageDraw.Draw(Image.new("RGBA", (8, 8)))
        boxes = [probe.textbbox((0, 0), line, font=font) for line in lines]
        line_height = max(box[3] - box[1] for box in boxes) + 30
        width = max(box[2] - box[0] for box in boxes) + 60
        height = line_height * len(lines) + 40
        picture = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        draw = ImageDraw.Draw(picture)
        for number, (line, box) in enumerate(zip(lines, boxes)):
            x = (width - (box[2] - box[0])) // 2 - box[0]
            y = 20 + number * line_height - box[1]
            draw.text((x + 10, y + 10), line, font=font, fill=shadow + (255,))
            draw.text((x, y), line, font=font, fill=colour + (255,))
        path = out_dir / f"{index + 1:02d}.png"
        picture.save(path)
        titles[index] = {"path": path.resolve().as_posix(), "position": title.get("position", [0.0, 0.45]),
                         "scale": title.get("scale", 0.08 * len(lines))}
    return titles


# ----- running the editor -------------------------------------------------------------------------------------------

def editor_binary(rebuild):
    output = ROOT / "Artifacts" / "AutomationBin"
    dll = output / "ManaEditor.dll"
    if rebuild or not dll.exists():
        print("Building ManaEditor into Artifacts/AutomationBin ...")
        subprocess.run(["dotnet", "build", str(ROOT / "ManaEditor"), "-o", str(output), "-v", "q", "-nologo"], check=True)
    return dll


def render(size, script_path, rebuild, timeout):
    width, height = size
    command = ["dotnet", str(editor_binary(rebuild)), "--window-size", f"{width}x{height}", "--mute",
               "--command-file", str(script_path)]
    environment = dict(os.environ)
    if sys.platform.startswith("linux") and not environment.get("DISPLAY") and not environment.get("WAYLAND_DISPLAY"):
        if not shutil.which("xvfb-run"):
            sys.exit("No display and no xvfb-run: apt-get install xvfb (see Tools/Stories/README.md).")
        command = ["xvfb-run", "-a", "-s", f"-screen 0 {width}x{height}x24"] + command
        lavapipe = Path("/usr/share/vulkan/icd.d/lvp_icd.json")
        if environment.get("MANA_STORY_SOFTWARE", "") == "1" and lavapipe.exists():
            environment["VK_ICD_FILENAMES"] = str(lavapipe)
    log_path = script_path.with_suffix(".log")
    print(f"Rendering at {width}x{height}; log in {log_path.relative_to(ROOT)}")
    started = time.time()
    with open(log_path, "w", encoding="utf-8") as log:
        result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, env=environment, timeout=timeout)
    print(f"Rendered in {time.time() - started:.0f}s (exit {result.returncode})")
    errors = [line for line in log_path.read_text(encoding="utf-8", errors="replace").splitlines()
              if "Command file" in line and ("failed" in line.lower() or "error" in line.lower())]
    for line in errors[:10]:
        print(line)
    if result.returncode != 0:
        sys.exit(f"ManaEditor failed; see {log_path}")


# ----- encoding -----------------------------------------------------------------------------------------------------

def escape(text):
    return (text.replace("\\", "\\\\").replace("'", "’").replace(":", "\\:").replace(",", "\\,")
            .replace("%", "\\%"))


def wrap(text, width):
    lines, line = [], ""
    for word in text.split():
        if line and len(line) + 1 + len(word) > width:
            lines.append(line)
            line = word
        else:
            line = f"{line} {word}".strip()
    if line:
        lines.append(line)
    return lines


def ffmpeg_font(path):
    # drawtext's own escaping: a Windows drive colon must be escaped.
    return Path(path).resolve().as_posix().replace(":", "\\:")


def encode_shot(story, index, shot, folder, clip, ffmpeg, size):
    width, height = size
    fps = int(story["fps"])
    font = ffmpeg_font(ROOT / story.get("font", DEFAULT_FONT))
    caption_size = int(story.get("captionSize", round(width * 0.062)))
    count = len(list(folder.glob("*.jpg"))) if folder.is_dir() else 0
    if count == 0:
        sys.exit(f"Shot {index + 1} has no frames in {folder}: render first.")
    seconds = count / fps
    fade = f"min(1\\,max(0\\,(t-0.25)/0.35))*min(1\\,max(0\\,({seconds:.3f}-0.2-t)/0.35))"
    filters = [f"scale={width}:{height}:flags=neighbor", "setsar=1"]
    texts = []
    if index == 0 and story.get("subtitle") and not shot.get("title"):
        texts.append((story["subtitle"], int(caption_size * 0.7), "h*0.12"))
    if shot.get("caption"):
        lines = wrap(shot["caption"], int(story.get("captionWidth", 26)))
        top = f"h*0.74-{len(lines) * caption_size * 0.6:.0f}"
        for number, line in enumerate(lines):
            texts.append((line, caption_size, f"{top}+{number * int(caption_size * 1.2)}"))
    for text, text_size, y in texts:
        filters.append(
            f"drawtext=fontfile='{font}':text='{escape(text)}':fontsize={text_size}:fontcolor=white:alpha='{fade}':"
            f"x=(w-text_w)/2:y={y}:box=1:boxcolor=black@0.45:boxborderw={max(8, text_size // 5)}:"
            f"shadowcolor=black@0.6:shadowx=3:shadowy=3")
    filters.append("format=yuv420p")
    subprocess.run([ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-framerate", str(fps), "-i",
                    str(folder / "%05d.jpg"), "-vf", ",".join(filters), "-r", str(fps), "-c:v", "libx264",
                    "-preset", "medium", "-crf", "16", str(clip)], check=True)
    return seconds


def audio_inputs(story, starts, total):
    """ffmpeg inputs and a filter graph mixing the music and every shot's sounds; None for a silent track."""
    inputs, parts, labels = [], [], []
    music = story.get("music")
    if music:
        if isinstance(music, str):
            music = {"path": music}
        inputs += ["-stream_loop", "-1", "-i", str(ROOT / music["path"])]
        start = float(music.get("start", 0))
        volume = float(music.get("volume", 0.6))
        parts.append(f"[{{n}}:a]atrim={start}:{start + total:.3f},asetpts=PTS-STARTPTS,aresample=44100,"
                     f"aformat=channel_layouts=stereo,volume={volume},afade=t=out:st={max(0, total - 1.5):.3f}:d=1.5[a{{n}}]")
    for index, shot in enumerate(story["shots"]):
        for sound in shot.get("sounds", []):
            at = starts[index] + float(sound.get("at", 0))
            delay = int(at * 1000)
            inputs += ["-i", str(ROOT / sound["path"])]
            parts.append(f"[{{n}}:a]aresample=44100,aformat=channel_layouts=stereo,volume={float(sound.get('volume', 1.0))},"
                         f"adelay={delay}|{delay}[a{{n}}]")
    if not parts:
        return None
    return inputs, parts


def encode(story, frames_dir, out_path, ffmpeg):
    size = story["size"]
    fps = int(story["fps"])
    clips_dir = frames_dir / "clips"
    clips_dir.mkdir(parents=True, exist_ok=True)
    clips = []
    for index, shot in enumerate(story["shots"]):
        clip = clips_dir / f"{index + 1:02d}.mp4"
        seconds = encode_shot(story, index, shot, frames_dir / f"{index + 1:02d}", clip, ffmpeg, size)
        clips.append((clip, seconds))
        print(f"shot {index + 1}: {seconds:.1f}s")

    fade = float(story["transition"])
    if len(clips) == 1:
        fade = 0.0
    inputs = []
    for clip, _ in clips:
        inputs += ["-i", str(clip)]
    graph, label, elapsed, starts = [], "0:v", clips[0][1], [0.0]
    for index in range(1, len(clips)):
        starts.append(elapsed - fade)
        if fade > 0:
            graph.append(f"[{label}][{index}:v]xfade=transition=fade:duration={fade}:offset={elapsed - fade:.3f}[v{index}]")
            elapsed += clips[index][1] - fade
        else:
            graph.append(f"[{label}][{index}:v]concat=n=2:v=1:a=0[v{index}]")
            elapsed += clips[index][1]
        label = f"v{index}"
    if len(clips) == 1:
        graph.append("[0:v]null[v0]")
        label = "v0"

    audio = audio_inputs(story, starts, elapsed)
    first_audio = len(clips)
    if audio is None:
        inputs += ["-f", "lavfi", "-t", f"{elapsed:.3f}", "-i", "anullsrc=channel_layout=stereo:sample_rate=44100"]
        audio_map = f"{first_audio}:a"
    else:
        extra, parts = audio
        inputs += extra
        labels = []
        for offset, part in enumerate(parts):
            n = first_audio + offset
            graph.append(part.replace("{n}", str(n)))
            labels.append(f"[a{n}]")
        graph.append(f"{''.join(labels)}amix=inputs={len(labels)}:normalize=0:duration=longest,"
                     f"atrim=0:{elapsed:.3f},alimiter=limit=0.95[mix]")
        audio_map = "[mix]"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([ffmpeg, "-y", "-hide_banner", "-loglevel", "error", *inputs,
                    "-filter_complex", ";".join(graph), "-map", f"[{label}]", "-map", audio_map,
                    "-r", str(fps), "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-profile:v", "high",
                    "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-t", f"{elapsed:.3f}",
                    "-movflags", "+faststart", str(out_path)], check=True)
    print(f"Wrote {out_path} ({elapsed:.1f}s, {size[0]}x{size[1]})")


# ----- the whole run ------------------------------------------------------------------------------------------------

def make(story, *, draft=False, preview=False, out=None, script_only=False, encode_only=False, rebuild=False,
         timeout=3 * 3600, ffmpeg=None):
    problems = validate(story)
    if problems:
        sys.exit("The story has problems:\n  " + "\n  ".join(problems))
    suffix = ""
    if draft:
        suffix = "-draft"
    if preview:
        suffix = "-preview"
    name = slug(story["title"]) + suffix
    frames_dir = ROOT / "Artifacts" / "Stories" / name
    out_path = Path(out) if out else ROOT / "Artifacts" / "Stories" / f"{name}.mp4"
    script_path = frames_dir / "script.json"
    # A draft renders at half size (a quarter of the pixels) and is scaled back up: the retro filter's blocks hide it.
    render_size = list(story["size"])
    if draft:
        render_size = [story["size"][0] // 2, story["size"][1] // 2]
    if preview:
        # The generator's look at its own shots: small and quick, frames only, no video.
        render_size = [story["size"][0] // 4, story["size"][1] // 4]

    if not encode_only:
        frames_dir.mkdir(parents=True, exist_ok=True)
        for old in frames_dir.glob("[0-9][0-9]"):
            shutil.rmtree(old)
        titles = render_titles(story, frames_dir / "titles")
        script_path.write_text(json.dumps(build_script(story, frames_dir, titles), indent=1), encoding="utf-8")
        print(f"Script: {script_path.relative_to(ROOT)}")
        if script_only:
            return None
        render(render_size, script_path, rebuild, timeout)
    if preview:
        return frames_dir
    ffmpeg = ffmpeg or os.environ.get("FFMPEG") or shutil.which("ffmpeg")
    if not ffmpeg:
        sys.exit("No ffmpeg: install it, pass --ffmpeg or set FFMPEG.")
    encode(story, frames_dir, out_path, ffmpeg)
    return out_path


def preview_frames(frames_dir, story, per_shot=3):
    """Paths of a few frames from each rendered shot -- its start, middle and end -- in shot order."""
    picked = []
    for index in range(len(story["shots"])):
        frames = sorted((frames_dir / f"{index + 1:02d}").glob("*.jpg"))
        if not frames:
            picked.append([])
            continue
        step = max(1, (len(frames) - 1) // max(1, per_shot - 1))
        picked.append([frames[min(len(frames) - 1, i * step)] for i in range(per_shot)])
    return picked


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("story")
    parser.add_argument("--out", help="the MP4 (default Artifacts/Stories/<title>.mp4)")
    parser.add_argument("--draft", action="store_true", help="render at half size for a quick look")
    parser.add_argument("--check", action="store_true", help="only check the story")
    parser.add_argument("--script-only", action="store_true", help="write the command script and stop")
    parser.add_argument("--encode-only", action="store_true", help="encode frames already rendered")
    parser.add_argument("--rebuild", action="store_true", help="rebuild ManaEditor even if a build is there")
    parser.add_argument("--timeout", type=int, default=3 * 3600, help="seconds the render may take")
    parser.add_argument("--ffmpeg", default=None)
    options = parser.parse_args()

    story = load_story(options.story)
    if options.check:
        problems = validate(story)
        print("\n".join(problems) or "The story checks out.")
        sys.exit(1 if problems else 0)
    make(story, draft=options.draft, out=options.out, script_only=options.script_only,
         encode_only=options.encode_only, rebuild=options.rebuild, timeout=options.timeout, ffmpeg=options.ffmpeg)


if __name__ == "__main__":
    main()
