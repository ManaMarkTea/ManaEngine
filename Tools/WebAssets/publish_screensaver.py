"""Export the terminal screensaver and real, source-only repository excerpts for Pages."""
import argparse
import json
import shutil
from pathlib import Path


def publish(root: Path, output: Path):
    output.mkdir(parents=True, exist_ok=True)
    files = []
    for folder in ("Mana/Code", "ManaCore", "ManaUI/Code", "ManaGraphics/Code",
                   "ManaGraphics/Assets/Shaders/WebGpu", "ManaEditor/Code", "Games"):
        for path in sorted((root / folder).rglob("*")):
            if path.suffix not in (".cs", ".wgsl") or any(p in ("obj", "bin") for p in path.parts):
                continue
            lines = path.read_text(encoding="utf-8-sig").replace("\t", "    ").splitlines()
            if len(lines) >= 24:
                files.append({"path": path.relative_to(root).as_posix(), "lines": lines})
    if not files:
        raise ValueError("No repository source files found for the screensaver")
    (output / "sources.json").write_text(json.dumps(files, separators=(",", ":")), encoding="utf-8")
    for name in ("index.html", "screensaver.js", "agent-ledger.js", "code-syntax.js", "github-sources.js"):
        shutil.copyfile(Path(__file__).with_name("screensaver") / name, output / name)
    return len(files)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(f"Screensaver: exported {publish(args.root, args.output)} source files")
