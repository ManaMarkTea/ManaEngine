"""Split a published AppBundle for two hosts: the external asset library moves out to be served
separately (GitHub Pages), and the small catalog staying with the app is repointed at it there.

itch.io caps a browser-embedded build's *extracted* size at 500MB; the external library alone
already exceeds that. Moving the library out and rewriting its URLs to an absolute host needs no
change on the reading side -- ManaCore.Assets.ManaAssetCatalog already accepts either the local
relative form or one specific caller-approved external host, and asset-downloads.js already
resolves whatever URL the catalog gives it via `new URL(url, import.meta.url)`, which passes an
absolute URL through unchanged. A locally published build's catalog is untouched by this script
and keeps working exactly as before.
"""
import argparse
import json
import shutil
from pathlib import Path

LOCAL_PREFIX = "AssetLibrary/"


def split(app_bundle, library_out, external_base_url):
    if not external_base_url.endswith("/"):
        raise ValueError(f"external_base_url must end with '/': {external_base_url}")

    source_library = app_bundle / "AssetLibrary"
    if source_library.exists():
        library_out.mkdir(parents=True, exist_ok=True)
        for item in source_library.iterdir():
            shutil.move(str(item), str(library_out / item.name))
        source_library.rmdir()

    catalog_path = app_bundle / "asset-catalog.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    for item in catalog["files"]:
        if not item["url"].startswith(LOCAL_PREFIX):
            raise ValueError(f"Expected a local AssetLibrary url, got: {item['url']}")
        item["url"] = external_base_url + item["url"][len(LOCAL_PREFIX):]
    catalog_path.write_text(json.dumps(catalog, separators=(",", ":")), encoding="utf-8")

    moved = sum(1 for _ in library_out.iterdir()) if library_out.exists() else 0
    print(f"Moved {moved} file(s) to {library_out}; catalog now points at {external_base_url}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-bundle", type=Path, required=True)
    parser.add_argument("--library-out", type=Path, required=True)
    parser.add_argument("--external-base-url", required=True,
                         help="Replaces the 'AssetLibrary/' prefix in every file url; must end with '/'.")
    args = parser.parse_args()
    split(args.app_bundle.resolve(), args.library_out.resolve(), args.external_base_url)
