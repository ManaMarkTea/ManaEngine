"""Publish immutable asset files and a browsable catalog, without a monolithic archive."""
import argparse
import hashlib
import json
import posixpath
import shutil
import struct
import zipfile
from pathlib import Path
from urllib.parse import unquote, urlsplit

EXCLUDED = "Assets/Planet/ETOPO_2022_v1_60s_N90W180_surface.tif"
# Generated/ holds derived files (the baked model previews), attached to the catalog by attach_thumbnails.
EXCLUDED_PREFIXES = ("Assets/GeoData/", "Assets/Generated/")
ROOTS = ("Assets", "Mana/Assets", "ManaGraphics/Assets", "ManaUI/Assets", "ManaVoxel/Assets")
SUPPORTED_EXTENSIONS = {"KHR_materials_unlit", "KHR_materials_emissive_strength",
                        "KHR_texture_transform", "KHR_materials_pbrSpecularGlossiness"}


def model_document(source):
    if source.suffix.lower() == '.gltf':
        return json.loads(source.read_text(encoding='utf-8-sig'))
    with source.open('rb') as stream:
        magic, version, length, chunk_length, chunk_type = struct.unpack('<5I', stream.read(20))
        if magic != 0x46546C67 or version != 2 or chunk_type != 0x4E4F534A or chunk_length > length - 20:
            raise ValueError(f'Invalid GLB header: {source}')
        return json.loads(stream.read(chunk_length))


def dependency_paths(path, document):
    dependencies = set()
    for group in ("buffers", "images"):
        for item in document.get(group, []):
            uri = item.get("uri", "")
            if not uri or uri.startswith("data:"):
                continue
            if urlsplit(uri).scheme or uri.startswith(("/", "\\")):
                raise ValueError(f"External URI in {path}: {uri}")
            target = posixpath.normpath(posixpath.join(posixpath.dirname(path), unquote(uri)))
            if not target.startswith("Assets/") or "\\" in target:
                raise ValueError(f"Asset dependency escapes library: {path}: {uri}")
            dependencies.add(target)
    return sorted(dependencies)


def publish(root, output):
    sources = {}
    for directory in ROOTS:
        for source in sorted((root / directory).rglob("*")):
            if not source.is_file():
                continue
            path = "Assets/" + source.relative_to(root / directory).as_posix()
            if path == EXCLUDED or path.startswith(EXCLUDED_PREFIXES):
                continue
            if path in sources:
                raise ValueError(f"Duplicate asset path: {path}")
            sources[path] = source

    library = output / "AssetLibrary"
    library.mkdir(parents=True, exist_ok=True)
    files, models = [], []
    documents = {path: model_document(source) for path, source in sources.items()
                 if source.suffix.lower() in ('.gltf', '.glb')}
    for path, source in sources.items():
        with source.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        filename = digest + source.suffix.lower()
        destination = library / filename
        if not destination.exists():
            temporary = destination.with_suffix(destination.suffix + ".tmp")
            shutil.copyfile(source, temporary)
            temporary.replace(destination)
        dependencies = []
        if path in documents:
            dependencies = dependency_paths(path, documents[path])
            # Mirror the loader's supported-sibling fallback. Keep the original header so the
            # loader can diagnose it, but stage the plain variant and its sidecars for import.
            required = set(documents[path].get('extensionsRequired', []))
            parent = Path(path).parent
            if required - SUPPORTED_EXTENSIONS and parent.name.lower().startswith('gltf'):
                alternatives = [str(parent.parent / variant / (source.stem + suffix)).replace('\\', '/')
                                for variant in ('glTF', 'glTF-Binary', 'glTF-Embedded')
                                for suffix in ('.gltf', '.glb')]
                alternative = next((item for item in alternatives if item != path and item in documents
                                    and not set(documents[item].get('extensionsRequired', [])) - SUPPORTED_EXTENSIONS), None)
                if alternative:
                    dependencies = [alternative]
            for dependency in dependencies:
                if dependency not in sources:
                    raise ValueError(f"Missing dependency: {path} -> {dependency}")
        files.append(dict(path=path, url="AssetLibrary/" + filename, sha256=digest,
                          size=source.stat().st_size, dependencies=dependencies))
        if source.suffix.lower() in (".glb", ".gltf"):
            models.append(dict(path=path))
        elif source.suffix.lower() == ".zip":
            with zipfile.ZipFile(source) as archive:
                for entry in sorted(archive.namelist()):
                    if Path(entry).suffix.lower() in (".glb", ".gltf"):
                        models.append(dict(path=path, entry=entry))

    attach_thumbnails(root, library, files, models)
    publish_levels(root, output)
    catalog = dict(version=1, files=files, models=models)
    temporary = output / "asset-catalog.json.tmp"
    temporary.write_text(json.dumps(catalog, separators=(",", ":")), encoding="utf-8")
    temporary.replace(output / "asset-catalog.json")
    # Remove stale external blobs only inside this generated output directory.
    keep = {Path(item["url"]).name for item in files}
    for blob in library.iterdir():
        if blob.is_file() and blob.name not in keep:
            blob.unlink()
    print(f"External library: {len(files)} files, {len(models)} models, "
          f"{sum(item['size'] for item in files) / 1024**2:.1f} MiB; not part of startup download.")
    return catalog


def attach_thumbnails(root, library, files, models):
    """Points each model at its pre-built preview in Assets/Generated/Thumbnails (bake_thumbnails.py makes them), when
    it was rendered from the model file as it is now; any other model is drawn on demand in the browser."""
    cache = root / "Assets" / "Generated" / "Thumbnails"
    index_file = cache / "index.json"
    if not index_file.exists():
        return
    thumbnails = json.loads(index_file.read_text(encoding="utf-8"))["thumbnails"]
    digests = {item["path"]: item["sha256"] for item in files}
    attached = stale = 0
    for model in models:
        label = model["path"] + ("!" + model["entry"] if "entry" in model else "")
        cached = thumbnails.get(label)
        source = cache / cached["png"] if cached else None
        if not cached or cached["source"] != digests[model["path"]] or not source.exists():
            stale += 1
            continue
        # "!" separates a zip from the entry inside it, so a preview path never collides with a real asset.
        path = label + ".thumb.png"
        digest = Path(cached["png"]).stem
        destination = library / cached["png"]
        if not destination.exists():
            shutil.copyfile(source, destination)
        files.append(dict(path=path, url="AssetLibrary/" + cached["png"], sha256=digest,
                          size=source.stat().st_size, dependencies=[]))
        model["thumbnail"] = path
        attached += 1
    print(f"Thumbnails: {attached} pre-built, {stale} without one (run Tools/WebAssets/bake_thumbnails.py).")


def publish_levels(root, output):
    """levels.json: what the page's level selector lists -- each bundled level's name, file, picture and size.
    A level is listed when its own "web" block says publish (the scene's Publishing in the editor's Inspector), with
    that block's description and order. Its picture is Content/Levels/Pictures/<name>.jpg, copied beside the list."""
    levels = []
    pictures = output / "level-pictures"
    pictures.mkdir(exist_ok=True)
    for source in sorted((root / "Content" / "Levels").rglob("*.manalevel")):
        document = json.loads(source.read_text(encoding="utf-8-sig"))
        name = document.get("name") or source.stem
        web = document.get("web") or {}
        if not web.get("publish"):
            continue
        def count(nodes):
            return sum(1 + count(node.get("children") or []) for node in nodes or [])
        entry = dict(name=name, file="Content/Levels/" + source.relative_to(root / "Content" / "Levels").as_posix(),
                     description=web.get("description") or "", nodes=count(document.get("nodes")),
                     order=web.get("order", 100))
        picture = root / "Content" / "Levels" / "Pictures" / f"{name}.jpg"
        if picture.exists():
            shutil.copyfile(picture, pictures / picture.name)
            entry["picture"] = "level-pictures/" + picture.name
        levels.append(entry)
    levels.sort(key=lambda level: (level["order"], level["name"]))
    temporary = output / "levels.json.tmp"
    temporary.write_text(json.dumps(dict(levels=levels), indent=1), encoding="utf-8")
    temporary.replace(output / "levels.json")
    print(f"Level selector: {len(levels)} levels.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    publish(args.root.resolve(), args.output.resolve())
