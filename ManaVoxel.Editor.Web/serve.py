#!/usr/bin/env python3
"""Compatibility server for the combined host; open /?mode=editor."""
import runpy
import sys
from pathlib import Path

if len(sys.argv) == 1:
    sys.argv.append("8002")
print(f"Editor: http://localhost:{sys.argv[1]}/?mode=editor", flush=True)
runpy.run_path(str(Path(__file__).resolve().parent.parent / "ManaVoxel.Web" / "serve.py"), run_name="__main__")
