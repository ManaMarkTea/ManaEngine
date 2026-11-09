"""Store one versioned web bundle and preserve the editor/play entry URLs."""
import argparse
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path
from publish_screensaver import publish as publish_screensaver


def assemble(bundle: Path, output: Path, build: str):
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", build):
        raise ValueError("Build must be a single directory name")
    destination = output / "app" / "v" / build
    shutil.copytree(bundle, destination)
    shutil.copyfile(bundle / "index.html", output / "app" / "index.html")
    # Beside index.html, not in v/<build>/: an installed app's manifest address must not change with each deploy.
    shutil.copytree(bundle / "apps", output / "app" / "apps")
    (output / "app" / "version.json").write_text(json.dumps({"build": build}), encoding="utf-8")
    for mode in ("editor", "play"):
        entry = output / mode
        entry.mkdir(parents=True, exist_ok=True)
        (entry / "index.html").write_text(
            '<!DOCTYPE html><html lang="en"><meta charset="utf-8">'
            '<title>ManaEngine</title><script>'
            'const target = new URL("../app/index.html", location.href);'
            'target.search = location.search; target.hash = location.hash;'
            f'target.searchParams.set("mode", "{mode}");'
            'location.replace(target.href);</script>'
            f'<a href="../app/index.html?mode={mode}">Open {mode}</a></html>',
            encoding="utf-8",
        )
    built_at = datetime.now(timezone.utc)
    landing = Path(__file__).with_name("pages-index.html").read_text(encoding="utf-8")
    landing = landing.replace("{{BUILD_TIME}}", built_at.isoformat(timespec="seconds"))
    landing = landing.replace("{{BUILD_DATE}}", built_at.strftime("%Y-%m-%d %H:%M UTC"))
    (output / "index.html").write_text(landing, encoding="utf-8")
    publish_screensaver(Path(__file__).resolve().parents[2], output / "code-screensaver")


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
