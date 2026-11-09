"""Downloads NVIDIA ORCA's Amazon Lumberyard Bistro (CC-BY 4.0, ~900 MB) into Downloaded/, for benchmarks.

The editor does the rest: open Downloaded/Bistro_v5_2 in the Scenes panel (or run `ManaEditor --import
Downloaded/Bistro_v5_2.zip`) and it is converted into Library/Imported/Bistro_v5_2/ -- FBX to glTF, materials from the
pack's texture naming, pivots, its sun and HDR sky, light probes, a night version (ManaSceneImporter).

    python Tools/Benchmarks/bistro.py

The ORCA site's own renders land in Downloaded/Bistro reference/, to compare against (2017 Lumberyard ones, lit
differently from the FBX's sun and HDR sky).
"""
import os
import shutil
import sys
import urllib.request

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DOWNLOADED = os.path.join(REPO, "Downloaded")
ZIP_URL = "https://developer.nvidia.com/bistro"
REFERENCE_URL = "https://d29g4g2dyqv443.cloudfront.net/sites/default/files/"
REFERENCES = ["akamai/Bistro_Exterior_1.png", "akamai/Bistro_Exterior_2.png", "Bistro_Night.png",
              "akamai/Bistro_Interior_1.png", "akamai/Bistro_Interior_2.png", "akamai/Bistro_Interior_3.png"]


def download(url, path):
    if os.path.exists(path):
        return
    print(f"[bistro] downloading {url}", flush=True)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with urllib.request.urlopen(url) as response, open(path + ".part", "wb") as out:
        shutil.copyfileobj(response, out, 1 << 20)
    os.replace(path + ".part", path)


def main():
    download(ZIP_URL, os.path.join(DOWNLOADED, "Bistro_v5_2.zip"))
    for name in REFERENCES:
        download(REFERENCE_URL + name, os.path.join(DOWNLOADED, "Bistro reference", os.path.basename(name)))
    print("[bistro] in Downloaded/: open Bistro_v5_2 in the editor's Scenes panel to import it", flush=True)


if __name__ == "__main__":
    sys.exit(main())
