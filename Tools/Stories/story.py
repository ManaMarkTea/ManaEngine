"""Turns a story -- a few shots with captions -- into a portrait video for Instagram/TikTok stories.

    python Tools/Stories/story.py "Content/Stories/Night Shift.json"            # script, render, encode
    python Tools/Stories/story.py <story> --script-only                          # just write the command script
    python Tools/Stories/story.py <story> --encode-only                          # re-encode frames already rendered

A story is JSON (see Tools/Stories/README.md): a size (1080 x 1920 by default), a retro look, and shots -- each a level,
a camera move from one pose to another over some seconds, an optional sky, and a caption. The script it writes drives
ManaEditor (`--command-file`) in a window of the story's size, recording each shot's frames at a fixed step; ffmpeg then
captions each shot, crossfades them and writes an H.264 MP4 with a silent audio track (what Instagram expects).

On a server with no screen (a VPS) the editor runs under xvfb-run, on the GPU if it has one or else Mesa's software
Vulkan (lavapipe) -- slow, but recording steps time by the frame, so the video plays at full speed regardless.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_FONT = "Assets/Fonts/Antonio/Antonio-Bold.ttf"


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-") or "story"


def load_story(path):
    with open(path, encoding="utf-8") as file:
        story = json.load(file)
    if not story.get("shots"):
        sys.exit(f"{path}: a story needs at least one shot.")
    story.setdefault("title", Path(path).stem)
    story.setdefault("size", [1080, 1920])
    story.setdefault("fps", 30)
    story.setdefault("transition", 0.4)
    for index, shot in enumerate(story["shots"]):
        if "level" not in shot or "from" not in shot:
            sys.exit(f"{path}: shot {index + 1} needs a level and a from pose.")
        shot.setdefault("seconds", 3.0)
    return story


def retro_command(look, lines):
    if look in (None, False, "off", "Off", "none"):
        return {"type": "retro", "visible": False}
    command = {"type": "retro", "name": look if isinstance(look, str) else "Default"}
    if lines:
        command["count"] = int(lines)
    return command


def build_script(story, frames_dir):
    """The ManaEditor command file: each shot opened, posed, then recorded while the camera (and sky) move."""
    fps = int(story["fps"])
    commands = [{"type": "gizmos", "visible": False}]
    current_level = None
    for index, shot in enumerate(story["shots"]):
        folder = frames_dir / f"{index + 1:02d}"
        if shot["level"] != current_level:
            commands += [{"type": "open_level", "path": shot["level"]}, {"type": "wait", "frames": int(story.get("loadFrames", 180))}]
            current_level = shot["level"]
        commands.append(retro_command(shot.get("retro", story.get("retro", "Default")), shot.get("lines", story.get("lines"))))
        commands += shot.get("commands", [])

        sky = shot.get("sky")
        if sky:
            start = {"type": "sky", "name": sky.get("name", "DayNightCycle")}
            if "timeOfDay" in sky:
                start["timeOfDay"] = sky["timeOfDay"]
            commands.append(start)

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
            move = {"type": "move_camera", "position": shot["to"]["position"], "target": shot["to"]["target"], "frames": frames}
            if "fov" in shot:
                move["fov"] = shot["fov"]
            commands.append(move)
        else:
            commands.append({"type": "wait", "frames": frames})
        commands += [{"type": "stop_record"}, {"type": "wait", "frames": 3}]
    commands.append({"type": "quit"})
    return commands


def editor_binary(rebuild):
    output = ROOT / "Artifacts" / "AutomationBin"
    dll = output / "ManaEditor.dll"
    if rebuild or not dll.exists():
        print("Building ManaEditor into Artifacts/AutomationBin ...")
        subprocess.run(["dotnet", "build", str(ROOT / "ManaEditor"), "-o", str(output), "-v", "q", "-nologo"], check=True)
    return dll


def render(story, script_path, rebuild, timeout):
    width, height = story["size"]
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
    print(f"Rendering {len(story['shots'])} shot(s) at {width}x{height}; log in {log_path.relative_to(ROOT)}")
    started = time.time()
    with open(log_path, "w", encoding="utf-8") as log:
        result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, env=environment, timeout=timeout)
    print(f"Rendered in {time.time() - started:.0f}s (exit {result.returncode})")
    if result.returncode != 0:
        sys.exit(f"ManaEditor failed; see {log_path}")


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


def encode(story, frames_dir, out_path, ffmpeg):
    width, height = story["size"]
    fps = int(story["fps"])
    font = ffmpeg_font(ROOT / story.get("font", DEFAULT_FONT))
    caption_size = int(story.get("captionSize", round(width * 0.062)))
    clips_dir = frames_dir / "clips"
    clips_dir.mkdir(parents=True, exist_ok=True)
    clips = []
    for index, shot in enumerate(story["shots"]):
        folder = frames_dir / f"{index + 1:02d}"
        count = len(list(folder.glob("*.jpg"))) if folder.is_dir() else 0
        if count == 0:
            sys.exit(f"Shot {index + 1} has no frames in {folder}: render first.")
        seconds = count / fps
        fade = f"min(1\\,max(0\\,(t-0.25)/0.35))*min(1\\,max(0\\,({seconds:.3f}-0.2-t)/0.35))"
        filters = [f"scale={width}:{height}:flags=neighbor", "setsar=1"]
        texts = []
        if index == 0 and story.get("title"):
            texts.append((story["title"], int(caption_size * 1.6), "h*0.16"))
            if story.get("subtitle"):
                texts.append((story["subtitle"], int(caption_size * 0.7), f"h*0.16+{int(caption_size * 1.9)}"))
        if shot.get("caption"):
            lines = wrap(shot["caption"], int(story.get("captionWidth", 22)))
            top = f"h*0.72-{len(lines) * caption_size * 0.6:.0f}"
            for number, line in enumerate(lines):
                texts.append((line, caption_size, f"{top}+{number * int(caption_size * 1.2)}"))
        for text, size, y in texts:
            filters.append(
                f"drawtext=fontfile='{font}':text='{escape(text)}':fontsize={size}:fontcolor=white:alpha='{fade}':"
                f"x=(w-text_w)/2:y={y}:box=1:boxcolor=black@0.45:boxborderw={max(8, size // 5)}:"
                f"shadowcolor=black@0.6:shadowx=3:shadowy=3")
        filters.append("format=yuv420p")
        clip = clips_dir / f"{index + 1:02d}.mp4"
        subprocess.run([ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-framerate", str(fps), "-i",
                        str(folder / "%05d.jpg"), "-vf", ",".join(filters), "-r", str(fps), "-c:v", "libx264",
                        "-preset", "medium", "-crf", "16", str(clip)], check=True)
        clips.append((clip, seconds))
        print(f"shot {index + 1}: {seconds:.1f}s")

    fade = float(story["transition"])
    inputs = []
    for clip, _ in clips:
        inputs += ["-i", str(clip)]
    graph, label, elapsed = [], "0:v", clips[0][1]
    for index in range(1, len(clips)):
        if fade > 0:
            graph.append(f"[{label}][{index}:v]xfade=transition=fade:duration={fade}:offset={elapsed - fade:.3f}[v{index}]")
            elapsed += clips[index][1] - fade
        else:
            graph.append(f"[{label}][{index}:v]concat=n=2:v=1:a=0[v{index}]")
            elapsed += clips[index][1]
        label = f"v{index}"
    mapping = ["-map", "0:v"] if len(clips) == 1 else ["-filter_complex", ";".join(graph), "-map", f"[{label}]"]
    audio = len(clips)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([ffmpeg, "-y", "-hide_banner", "-loglevel", "error", *inputs,
                    "-f", "lavfi", "-t", f"{elapsed:.3f}", "-i", "anullsrc=channel_layout=stereo:sample_rate=44100",
                    *mapping, "-map", f"{audio}:a", "-r", str(fps), "-c:v", "libx264", "-preset", "slow", "-crf", "18",
                    "-profile:v", "high", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-shortest",
                    "-movflags", "+faststart", str(out_path)], check=True)
    print(f"Wrote {out_path} ({elapsed:.1f}s, {width}x{height})")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("story")
    parser.add_argument("--out", help="the MP4 (default Artifacts/Stories/<title>.mp4)")
    parser.add_argument("--script-only", action="store_true", help="write the command script and stop")
    parser.add_argument("--encode-only", action="store_true", help="encode frames already rendered")
    parser.add_argument("--rebuild", action="store_true", help="rebuild ManaEditor even if a build is there")
    parser.add_argument("--timeout", type=int, default=3 * 3600, help="seconds the render may take")
    parser.add_argument("--ffmpeg", default=os.environ.get("FFMPEG") or shutil.which("ffmpeg"))
    options = parser.parse_args()

    story = load_story(options.story)
    name = slug(story["title"])
    frames_dir = ROOT / "Artifacts" / "Stories" / name
    out_path = Path(options.out) if options.out else ROOT / "Artifacts" / "Stories" / f"{name}.mp4"
    script_path = frames_dir / "script.json"

    if not options.encode_only:
        frames_dir.mkdir(parents=True, exist_ok=True)
        for old in frames_dir.glob("[0-9][0-9]"):
            shutil.rmtree(old)
        script_path.write_text(json.dumps(build_script(story, frames_dir), indent=1), encoding="utf-8")
        print(f"Script: {script_path.relative_to(ROOT)}")
        if options.script_only:
            return
        render(story, script_path, options.rebuild, options.timeout)
    if not options.ffmpeg:
        sys.exit("No ffmpeg: install it, pass --ffmpeg or set FFMPEG.")
    encode(story, frames_dir, out_path, options.ffmpeg)


if __name__ == "__main__":
    main()
