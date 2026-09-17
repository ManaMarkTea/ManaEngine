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
            if path == EXCLUDED:
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


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    publish(args.root.resolve(), args.output.resolve())
