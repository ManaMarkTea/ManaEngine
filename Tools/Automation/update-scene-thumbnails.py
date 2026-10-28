"""Capture level thumbnails with desktop WebGPU, including missing previews.

Build ManaVoxel into Artifacts/AutomationBin first. Outputs go to
Artifacts/Automation/SceneThumbnails for review; --apply replaces the bundled JPGs.
Requires Pillow, also used by the other asset baking tools.
"""
import argparse
import json
import subprocess
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--only', help='Capture one scene by name')
    parser.add_argument('--missing', action='store_true', help='Capture only levels without a preview')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = root / 'Artifacts/Automation/SceneThumbnails'
    output.mkdir(parents=True, exist_ok=True)
    cameras = {
        'Little Pond': ([55, 25, 60], [32, 1.5, 32]),
        'PBR Gallery': ([49, 16, 82], [49, 1, 58]),
        'Prefab Lamps': ([40, 3.5, 53], [40, 1.3, 44]),
        'Voxel Showcase': ([58, 38, 165], [44, 24, 40]),
        'Planet Showcase': ([32, 85, -85], [32, 40, 32]),
        'Anti Aliasing': ([48, 5, 57], [48, 2, 43]),
        'Reflections': ([66, 4, 49], [59, 0, 49]),
        'Shadow Contact': ([50, 5, 58], [50, 0.5, 40]),
        'Sprite Normals': ([45, 4, 55], [45, 3.6, 40]),
        'Particles': ([50, 4.5, 54], [50, 1.7, 40]),
    }
    level_files = sorted((root / 'Content/Levels').rglob('*.manalevel'))
    level_files += sorted(root.glob('Games/*/Content/Levels/*.manalevel'))
    for level_file in level_files:
        name = level_file.stem
        if args.only and name != args.only:
            continue
        pictures = level_file.parent / 'Pictures'
        picture = pictures / (name + '.jpg')
        if args.missing and any((pictures / (name + suffix)).exists() for suffix in ('.png', '.jpg', '.jpeg')):
            continue
        level_path = level_file.relative_to(root)
        level = json.loads((root / level_path).read_text(encoding='utf-8-sig'))
        commands = [
            {'type': 'open_level', 'path': level_path.as_posix()},
            {'type': 'wait', 'frames': 180},
            {'type': 'gizmos', 'visible': False},
            {'type': 'select_node', 'name': level['name']},
        ]
        if not level.get('sky'):
            commands.append({'type': 'sky', 'name': 'Day'})
        if name == 'Planet Showcase':
            commands += [{'type': 'sky', 'name': 'Day'}, {'type': 'wait', 'frames': 720}]
        if name in cameras:
            position, target = cameras[name]
            commands.append({'type': 'move_camera', 'position': position, 'target': target})
        if name == 'Voxel Showcase':
            commands.append({'type': 'select_node', 'name': 'Player Start'})
        if name in ('Sample Course', '2D Sample', 'Yacht Jousting', 'Foundry', 'Code Screensaver') or name.startswith('Train Yard Wars'):
            commands += [{'type': 'play'}, {'type': 'wait', 'frames': 120}]
        if name.startswith('Train Yard Wars'):
            commands += [{'type': 'tyw_new_map', 'seed': 42}, {'type': 'wait', 'frames': 120}]
        commands += [
            {'type': 'wait', 'frames': 60},
            # The screensaver is all HUD, which a clean shot leaves out.
            {'type': 'screenshot', 'path': '@@OUT@@', 'clean': name != 'Code Screensaver'},
            {'type': 'quit'},
        ]
        capture_path = output / (name + '.png')
        capture_path.unlink(missing_ok=True)
        subprocess.run([
            'pwsh', '-NoProfile', '-File', str(root / 'Tools/Automation/run-commands.ps1'),
            '-Name', name, '-Out', str(output), '-Commands', json.dumps(commands),
        ], cwd=root, check=True)
        with Image.open(capture_path) as capture:
            capture.convert('RGB').resize((800, 450), Image.Resampling.LANCZOS).save(
                output / picture.name, quality=92, subsampling=0)
        if args.apply:
            pictures.mkdir(parents=True, exist_ok=True)
            picture.write_bytes((output / picture.name).read_bytes())


if __name__ == '__main__':
    main()
