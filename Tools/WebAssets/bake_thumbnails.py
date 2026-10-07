"""Renders model previews into Content/Thumbnails for the web catalog: only models whose file changed since the
last bake, dropping those of models that are gone. Commit what it changes; the web publish picks them up.

    python Tools/WebAssets/bake_thumbnails.py
"""
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import publish_assets  # noqa: E402

root = Path(__file__).resolve().parents[2]
# Kept between runs: publish only copies files it does not already have there.
staging = root / "Artifacts" / "ThumbnailBake"
publish_assets.publish(root, staging)
sys.exit(subprocess.call(["dotnet", "run", "--project", str(root / "Tools" / "ThumbnailBaker"), "-c", "Release", "--",
                          "--app-bundle", str(staging), "--cache", str(root / "Content" / "Thumbnails")]))
