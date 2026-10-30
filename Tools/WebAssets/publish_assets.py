"""Publish immutable asset files and a browsable catalog, without a monolithic archive."""
import argparse
import hashlib
import json
import posixpath
import re
import shutil
import struct
import zipfile
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit

EXCLUDED = "Assets/Planet/ETOPO_2022_v1_60s_N90W180_surface.tif"
# Generated/ holds derived files (the baked model previews), attached to the catalog by attach_thumbnails.
EXCLUDED_PREFIXES = ("Assets/GeoData/", "Assets/Generated/")
ROOTS = ("Assets", "ManaGraphics/Assets", "ManaUI/Assets", "ManaVoxel/Assets")
SUPPORTED_EXTENSIONS = {"KHR_materials_unlit", "KHR_materials_emissive_strength",
                        "KHR_texture_transform", "KHR_materials_pbrSpecularGlossiness"}


def web_scene_dds(data, max_size=512):
    """Drop oversized top mips from imported scene textures without decoding or recompressing BC blocks."""
    if len(data) < 128 or data[:4] != b'DDS ':
        raise ValueError('Invalid scene DDS')
    height, width = struct.unpack_from('<II', data, 12)
    if max(width, height) <= max_size:
        return data
    block = {b'DXT1': 8, b'ATI1': 8, b'BC4U': 8, b'DXT3': 16, b'DXT5': 16, b'ATI2': 16, b'BC5U': 16}.get(data[84:88])
    if block is None:
        raise ValueError(f'Unsupported scene DDS format: {data[84:88]!r}')
    mips = max(1, struct.unpack_from('<I', data, 28)[0])
    offset = 128
    while max(width, height) > max_size and mips > 1:
        offset += max(1, (width + 3) // 4) * max(1, (height + 3) // 4) * block
        width, height, mips = max(1, width // 2), max(1, height // 2), mips - 1
    if max(width, height) > max_size or offset >= len(data):
        raise ValueError('Scene DDS has no usable small mip chain')
    header = bytearray(data[:128])
    struct.pack_into('<II', header, 12, height, width)
    struct.pack_into('<I', header, 20, max(1, (width + 3) // 4) * max(1, (height + 3) // 4) * block)
    struct.pack_into('<I', header, 28, mips)
    return bytes(header) + data[offset:]


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
            if not target.startswith(("Assets/", "Library/Imported/")) or "\\" in target:
                raise ValueError(f"Asset dependency escapes library: {path}: {uri}")
            dependencies.add(target)
    return sorted(dependencies)


def imported_sources(root):
    """Only converted files actually named by checked-in levels, plus their glTF sidecars."""
    paths = set()

    def walk(value):
        if isinstance(value, dict):
            for child in value.values():
                walk(child)
        elif isinstance(value, list):
            for child in value:
                walk(child)
        elif isinstance(value, str) and value.startswith("Library/Imported/"):
            if any(part in ("", ".", "..") for part in value.split("/")) or "\\" in value or ":" in value:
                raise ValueError(f"Invalid imported asset path: {value}")
            paths.add(value)

    for folder in level_folders(root):
        for level in sorted(folder.rglob("*.manalevel")):
            walk(json.loads(level.read_text(encoding="utf-8-sig")))
    sources = {}
    while paths:
        path = paths.pop()
        if path in sources:
            continue
        source = root / path
        if not source.is_file():
            raise ValueError(f"Missing imported scene asset: {path}; run dotnet run --project ManaVoxel -- --prepare-level-assets")
        sources[path] = source
        if source.suffix.lower() in (".gltf", ".glb"):
            paths.update(dependency_paths(path, model_document(source)))
    return sources


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

    # A game's own models and sounds (Games/<Name>/Content/Models, /Audio) are referenced by their repo path, as on desktop.
    for folder in ("Models", "Audio"):
        for source in sorted(root.glob(f"Games/*/Content/{folder}/**/*")):
            if source.is_file():
                sources[source.relative_to(root).as_posix()] = source

    sources.update(imported_sources(root))
    library = output / "AssetLibrary"
    library.mkdir(parents=True, exist_ok=True)
    files, models = [], []
    documents = {path: model_document(source) for path, source in sources.items()
                 if source.suffix.lower() in ('.gltf', '.glb')}
    for path, source in sources.items():
        scene_dds = None
        if path.startswith('Library/Imported/') and source.suffix.lower() == '.dds':
            scene_dds = web_scene_dds(source.read_bytes())
            digest = hashlib.sha256(scene_dds).hexdigest()
        else:
            with source.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
        filename = digest + source.suffix.lower()
        destination = library / filename
        if not destination.exists():
            temporary = destination.with_suffix(destination.suffix + ".tmp")
            if scene_dds is not None:
                temporary.write_bytes(scene_dds)
            else:
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
                          size=len(scene_dds) if scene_dds is not None else source.stat().st_size, dependencies=dependencies))
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


def level_folders(root):
    """Content/Levels, then each game's own (Games/<Name>/Content/Levels, its content mount)."""
    yield root / "Content" / "Levels"
    yield from sorted(path for path in (root / "Games").glob("*/Content/Levels") if path.is_dir())


def publish_levels(root, output):
    """levels.json: what the page's level selector lists -- each bundled level's name, file, picture and size.
    A level is listed when its own "web" block says publish (the scene's Publishing in the editor's Inspector), with
    that block's description and order. Its picture is Pictures/<name>.jpg beside it, copied beside the list."""
    levels = []
    pictures = output / "level-pictures"
    pictures.mkdir(exist_ok=True)
    for folder in level_folders(root):
        for source in sorted(folder.rglob("*.manalevel")):
            document = json.loads(source.read_text(encoding="utf-8-sig"))
            name = document.get("name") or source.stem
            web = document.get("web") or {}
            if not web.get("publish"):
                continue
            def count(nodes):
                return sum(1 + count(node.get("children") or []) for node in nodes or [])
            entry = dict(name=name, file=source.relative_to(root).as_posix(),
                         description=web.get("description") or "", nodes=count(document.get("nodes")),
                         order=web.get("order", 100))
            picture = folder / "Pictures" / f"{name}.jpg"
            if picture.exists():
                shutil.copyfile(picture, pictures / picture.name)
                entry["picture"] = "level-pictures/" + picture.name
            levels.append(entry)
    levels.sort(key=lambda level: (level["order"], level["name"]))
    temporary = output / "levels.json.tmp"
    temporary.write_text(json.dumps(dict(levels=levels), indent=1), encoding="utf-8")
    temporary.replace(output / "levels.json")
    print(f"Level selector: {len(levels)} levels.")
    publish_apps(root, output, levels)


def app_slug(name):
    """The manifest's file name for a level; index.html works out the same one from ?level=."""
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def publish_apps(root, output, levels):
    """apps/: a web app manifest per listed level, so a level's page installs as an app that opens on that level, and
    one for the selector. A level's icon is Icons/<file name>.png beside it (512 px square), otherwise the Mana cube."""
    apps = output / "apps"
    shutil.rmtree(apps, ignore_errors=True)
    apps.mkdir()
    shutil.copyfile(root / "ManaVoxel.Web" / "mana-icon.png", apps / "mana.png")

    def write(slug, name, description, start, icon):
        manifest = dict(id=f"mana-app-{slug}", name=name, short_name=name, description=description,
                        start_url=start, scope="../", display="standalone", orientation="any",
                        background_color="#1E1F22", theme_color="#1E1F22",
                        icons=[dict(src=icon, sizes="512x512", type="image/png", purpose="any")])
        (apps / f"{slug}.webmanifest").write_text(json.dumps(manifest, indent=1), encoding="utf-8")

    write("mana", "Mana Renderer", "Levels and games made with Mana.", "../index.html", "mana.png")
    for level in levels:
        source = root / level["file"]
        slug = app_slug(source.stem)
        icon = source.parent / "Icons" / f"{source.stem}.png"
        icon_name = "mana.png"
        if icon.exists():
            icon_name = f"{slug}.png"
            shutil.copyfile(icon, apps / icon_name)
        write(slug, level["name"], level["description"], f"../index.html?level={quote(level['file'])}", icon_name)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    publish(args.root.resolve(), args.output.resolve())
