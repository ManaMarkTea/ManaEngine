"""Joins the frames `Content/Scripts/Sizzle Reel.json` records into the sizzle reel MP4.

    ./Tools/Automation/run-commands.ps1 -Name sizzle -File "Content/Scripts/Sizzle Reel.json" -TimeoutSeconds 1500
    python Tools/Automation/sizzle.py --out Artifacts/Sizzle/ManaRenderer-sizzle.mp4

Each folder under Artifacts/Sizzle becomes a captioned clip; the clips get title and end cards and crossfade into one
another. ffmpeg comes from --ffmpeg, the FFMPEG environment variable or the PATH.
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

FPS = 30
FADE = 0.5
FONT = "C\\:/Windows/Fonts/segoeui.ttf"
FONT_BOLD = "C\\:/Windows/Fonts/segoeuib.ttf"

# folder, caption, one-line detail[, speed-up]
TAKES = [
    ("01-helmet", "Physically based rendering", "glTF models, image-based lighting and a scattering sky"),
    ("02-gallery", "Materials", "Metallic and roughness, lit by the sky around them"),
    ("03-pond", "Day and night", "A moving sun and moon, placed lamps, fireflies, and water that catches the light"),
    ("04-reflections", "Reflections", "The sky drawn into a live reflection map"),
    ("05-planet", "Planets", "A world seen from space"),
    ("06-voxels", "Voxel art", "Painted and edited in the editor"),
    ("07-lamps", "Prefabs", "One lamp, recoloured per instance; placed lights carry the night"),
    ("08-trainyard", "Physics", "Bepu rigid bodies and a moving rail yard"),
    ("09-course", "3D platforming", "Moving platforms, ferries, lifts and boost pads - in the editor and the browser", 1.3),
    ("10-platformer", "2D platforming", "Sprite sheets, five biomes, parallax backdrops and a side-on camera"),
    ("11-editor", "The editor", "Retained-mode ManaUI: docking panels, an inspector, asset and UI designers", 1.6),
]


def run(args):
    subprocess.run(args, check=True)


def escape(text):
    return text.replace("\\", "\\\\").replace("'", "’").replace(":", "\\:").replace(",", "\\,").replace("%", "\\%")


def frame_count(folder):
    return len([name for name in os.listdir(folder) if name.endswith(".jpg")])


def encode_take(ffmpeg, folder, title, detail, out, speed=1.0):
    seconds = frame_count(folder) / FPS / speed
    fade = (
        "min(1\\,max(0\\,(t-0.4)/0.5))*min(1\\,max(0\\,({d:.3f}-0.4-t)/0.5))".format(d=seconds)
    )
    common = f"fontcolor=white:alpha='{fade}':shadowcolor=black@0.7:shadowx=2:shadowy=2"
    filters = (
        f"setpts=PTS/{speed},scale=1920:1080,format=yuv420p,"
        f"drawtext=fontfile='{FONT_BOLD}':text='{escape(title)}':fontsize=64:x=80:y=h-190:{common},"
        f"drawtext=fontfile='{FONT}':text='{escape(detail)}':fontsize=32:x=84:y=h-110:{common}"
    )
    run([ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-framerate", str(FPS), "-i", str(folder / "%05d.jpg"),
         "-vf", filters, "-r", str(FPS), "-c:v", "libx264", "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p", str(out)])
    return seconds


def card(ffmpeg, title, detail, seconds, out):
    fade = "min(1\\,max(0\\,t/0.5))*min(1\\,max(0\\,({d}-t)/0.5))".format(d=seconds)
    common = f"fontcolor=white:alpha='{fade}'"
    filters = (
        f"drawtext=fontfile='{FONT_BOLD}':text='{escape(title)}':fontsize=110:x=(w-text_w)/2:y=(h-text_h)/2-40:{common},"
        f"drawtext=fontfile='{FONT}':text='{escape(detail)}':fontsize=38:x=(w-text_w)/2:y=(h/2)+60:{common},format=yuv420p"
    )
    run([ffmpeg, "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
         f"color=c=0x0b0e14:s=1920x1080:r={FPS}:d={seconds}", "-vf", filters,
         "-c:v", "libx264", "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p", str(out)])
    return float(seconds)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--frames", default="Artifacts/Sizzle")
    parser.add_argument("--out", default="Artifacts/Sizzle/ManaRenderer-sizzle.mp4")
    parser.add_argument("--ffmpeg", default=os.environ.get("FFMPEG") or shutil.which("ffmpeg"))
    options = parser.parse_args()
    if not options.ffmpeg:
        sys.exit("No ffmpeg: pass --ffmpeg or set FFMPEG.")

    frames = Path(options.frames)
    clips_dir = frames / "clips"
    clips_dir.mkdir(exist_ok=True)

    clips = []
    clips.append((clips_dir / "title.mp4", card(options.ffmpeg, "ManaRenderer",
                                                "A C# engine and editor - WebGPU, Silk.NET, Bepu physics, ManaUI", 3.0,
                                                clips_dir / "title.mp4")))
    for folder, title, detail, *speed in TAKES:
        source = frames / folder
        if not source.is_dir():
            print(f"skipping {folder}: no frames")
            continue
        out = clips_dir / f"{folder}.mp4"
        clips.append((out, encode_take(options.ffmpeg, source, title, detail, out, *speed)))
        print(f"{folder}: {clips[-1][1]:.1f}s")
    clips.append((clips_dir / "end.mp4", card(options.ffmpeg, "ManaRenderer", "macOS, Windows and the web", 3.0,
                                              clips_dir / "end.mp4")))

    # Each crossfade starts FADE before the running clip ends.
    inputs = []
    for path, _ in clips:
        inputs += ["-i", str(path)]
    graph = []
    label = "0:v"
    elapsed = clips[0][1]
    for index in range(1, len(clips)):
        offset = elapsed - FADE
        out = f"v{index}"
        graph.append(f"[{label}][{index}:v]xfade=transition=fade:duration={FADE}:offset={offset:.3f}[{out}]")
        label = out
        elapsed += clips[index][1] - FADE
    run([options.ffmpeg, "-y", "-hide_banner", "-loglevel", "error", *inputs, "-filter_complex", ";".join(graph),
         "-map", f"[{label}]", "-r", str(FPS), "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
         "-movflags", "+faststart", options.out])
    print(f"wrote {options.out}: {elapsed:.1f}s")


if __name__ == "__main__":
    main()
