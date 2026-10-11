"""Downsample licensed PBR ZIPs to compact, reproducible 512px PNG packs.
Usage: python Tools/Materials/compact.py input.zip output.zip --source https://... --size 512
Requires Pillow and numpy. Only OpenGL normal maps and supported PBR channels are kept.
"""
import argparse
import io
import zipfile
from pathlib import Path
import numpy as np
from PIL import Image


def channel(name):
    if Path(name).suffix.lower() not in ('.png', '.jpg', '.jpeg', '.tga', '.bmp'):
        return None
    stem = Path(name).stem.lower().replace('-', '_')
    tokens = stem.split('_')
    if 'dx' in tokens or 'normaldx' in tokens:
        return None
    aliases = {'albedo': ['albedo', 'basecolor', 'diffuse', 'diff', 'color'],
               'occlusion': ['ao', 'occlusion', 'ambientocclusion', 'ambient'], 'roughness': ['roughness', 'rough'],
               'normal': ['normal', 'normalgl', 'nor', 'norgl'], 'metallic': ['metallic', 'metalness'],
               'emission': ['emissive', 'emission'], 'opacity': ['opacity', 'alpha']}
    return next((key for key, values in aliases.items() if set(tokens) & set(values)), None)


def compact(source, destination, url, size, creator='Share Textures', metallic=None, roughness=None):
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source) as archive, zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as output:
        written = set()
        for name in sorted(archive.namelist()):
            key = channel(name)
            if key is None or key in written:
                continue
            image = Image.open(io.BytesIO(archive.read(name))).convert('RGB')
            image.thumbnail((size, size), Image.Resampling.LANCZOS)
            if key == 'normal':
                normals = np.asarray(image, dtype=np.float32) / 127.5 - 1
                normals /= np.maximum(np.linalg.norm(normals, axis=2, keepdims=True), 1e-6)
                image = Image.fromarray(np.rint(np.clip((normals + 1) * 127.5, 0, 255)).astype(np.uint8))
            elif key not in ('albedo', 'emission'):
                image = image.convert('L')
            buffer = io.BytesIO()
            image.save(buffer, format='PNG', optimize=True)
            entry = zipfile.ZipInfo(f'material_{key}.png', date_time=(2026, 10, 10, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, buffer.getvalue())
            written.add(key)
        if 'albedo' not in written:
            raise ValueError('No albedo map in source ZIP')
        fallbacks = []
        for key, value in (('metallic', metallic), ('roughness', roughness)):
            if value is None or key in written:
                continue
            if not 0 <= value <= 1:
                raise ValueError(f'{key} must be between 0 and 1')
            buffer = io.BytesIO()
            Image.new('L', (1, 1), round(value * 255)).save(buffer, format='PNG')
            entry = zipfile.ZipInfo(f'material_{key}.png', date_time=(2026, 10, 10, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, buffer.getvalue())
            written.add(key)
            fallbacks.append(f'{key}={value}')
        entry = zipfile.ZipInfo('LICENSE.txt', date_time=(2026, 10, 10, 0, 0, 0))
        output.writestr(entry, f'Source: {url}\nCreator: {creator}\nLicense: CC0 / public domain, as stated on the source page.\nhttps://creativecommons.org/publicdomain/zero/1.0/\nModified: resized to at most {size}px; normal vectors renormalized.\nHeight/displacement and specular maps omitted (not supported PBR channels).\nFallback constant maps where absent: {", ".join(fallbacks) or "none"}.\n'.encode())
    print(f'{destination}: {destination.stat().st_size / 1024:.0f} KiB; {", ".join(sorted(written))}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--source', required=True)
    parser.add_argument('--size', type=int, default=512)
    parser.add_argument('--creator', default='Share Textures')
    parser.add_argument('--metallic', type=float, help='Fallback factor when the source has no metallic map')
    parser.add_argument('--roughness', type=float, help='Fallback factor when the source has no roughness map')
    args = parser.parse_args()
    compact(args.input, args.output, args.source, args.size, args.creator, args.metallic, args.roughness)
