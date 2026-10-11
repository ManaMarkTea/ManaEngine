"""Store one versioned web bundle and preserve the editor/play entry URLs."""
import argparse
import json
import re
import shutil
from pathlib import Path


def assemble(bundle: Path, output: Path, build: str):
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", build):
        raise ValueError("Build must be a single directory name")
    destination = output / "app" / "v" / build
    shutil.copytree(bundle, destination)
    shutil.copyfile(bundle / "index.html", output / "app" / "index.html")
    (output / "app" / "version.json").write_text(json.dumps({"build": build}), encoding="utf-8")
    for mode in ("editor", "play"):
        entry = output / mode
        entry.mkdir(parents=True, exist_ok=True)
        (entry / "index.html").write_text(
            '<!DOCTYPE html><html lang="en"><meta charset="utf-8">'
            '<title>ManaRenderer</title><script>'
            'const target = new URL("../app/index.html", location.href);'
            'target.search = location.search; target.hash = location.hash;'
            f'target.searchParams.set("mode", "{mode}");'
            'location.replace(target.href);</script>'
            f'<a href="../app/index.html?mode={mode}">Open {mode}</a></html>',
            encoding="utf-8",
        )
    shutil.copyfile(Path(__file__).with_name("pages-index.html"), output / "index.html")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-bundle", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--build", required=True)
    parser.add_argument("--itch-editor", action="store_true", help="Make itch.io open the editor by default")
    args = parser.parse_args()
    assemble(args.app_bundle, args.output, args.build)
    if args.itch_editor:
        index = args.app_bundle / "index.html"
        index.write_text(index.read_text(encoding="utf-8").replace('<body>', '<body data-start-mode="editor">'), encoding="utf-8")
