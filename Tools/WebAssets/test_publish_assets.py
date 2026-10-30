import json
import tempfile
import unittest
import zipfile
import struct
from pathlib import Path
from publish_assets import publish, EXCLUDED, web_scene_dds


class PublishAssetsTests(unittest.TestCase):
    def test_scene_dds_uses_existing_small_mips_without_changing_blocks(self):
        header = bytearray(128)
        header[:4] = b'DDS '
        struct.pack_into('<II', header, 12, 8, 8)
        struct.pack_into('<I', header, 28, 4)
        header[84:88] = b'ATI2'
        data = bytes(header) + b'a' * 64 + b'b' * 16 + b'c' * 16 + b'd' * 16
        small = web_scene_dds(data, max_size=4)
        self.assertEqual(struct.unpack_from('<II', small, 12), (4, 4))
        self.assertEqual(struct.unpack_from('<I', small, 28)[0], 3)
        self.assertEqual(small[128:], data[192:])
        self.assertEqual(web_scene_dds(small, max_size=4), small)

    def test_imported_level_assets_are_lazy_catalog_entries(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            levels = root / 'Content/Levels/Downloaded'
            levels.mkdir(parents=True)
            scene = root / 'Library/Imported/Test'
            scene.mkdir(parents=True)
            (scene / 'scene.gltf').write_text('{"buffers":[{"uri":"scene.bin"}],"images":[{"uri":"paint.png"}]}')
            for name in ('scene.bin', 'paint.png', 'sky.png', 'unused.bin'):
                (scene / name).write_bytes(b'content')
            (levels / 'Test.manalevel').write_text(json.dumps({
                'sky': {'photo': 'Library/Imported/Test/sky.png'},
                'nodes': [{'asset': 'Library/Imported/Test/scene.gltf'}]}))
            catalog = publish(root, root / 'output')
            files = {item['path']: item for item in catalog['files']}
            self.assertEqual(set(files), {f'Library/Imported/Test/{name}' for name in ('scene.gltf', 'scene.bin', 'paint.png', 'sky.png')})
            self.assertEqual(files['Library/Imported/Test/scene.gltf']['dependencies'],
                             ['Library/Imported/Test/paint.png', 'Library/Imported/Test/scene.bin'])
            (scene / 'scene.bin').unlink()
            with self.assertRaisesRegex(ValueError, 'prepare-level-assets'):
                publish(root, root / 'output')

    def test_complete_catalog_dependencies_and_immutable_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            models = root / 'Assets/Models'
            models.mkdir(parents=True)
            (models / 'test.gltf').write_text(json.dumps({
                'buffers': [{'uri': 'test.bin'}], 'images': [{'uri': 'paint%20red.png'}]}))
            (models / 'test.bin').write_bytes(b'buffer')
            (models / 'paint red.png').write_bytes(b'texture')
            excluded = root / EXCLUDED
            excluded.parent.mkdir(parents=True)
            excluded.write_bytes(b'oversized')
            assets = root / 'Assets'
            assets.mkdir(exist_ok=True)
            geodata = assets / 'GeoData'
            geodata.mkdir()
            with zipfile.ZipFile(geodata / 'ne_10m_lakes.zip', 'w') as archive:
                archive.writestr('ne_10m_lakes.shp', b'water')
            with zipfile.ZipFile(root / 'Assets/pack.zip', 'w') as archive:
                archive.writestr('Models/ship.glb', b'model')
            output = root / 'output'
            catalog = publish(root, output)
            self.assertEqual(len(catalog['models']), 2)
            files = {item['path']: item for item in catalog['files']}
            self.assertNotIn(EXCLUDED, files)
            self.assertNotIn('Assets/GeoData/ne_10m_lakes.zip', files)
            self.assertEqual(files['Assets/Models/test.gltf']['dependencies'],
                             ['Assets/Models/paint red.png', 'Assets/Models/test.bin'])
            for item in files.values():
                self.assertEqual((output / item['url']).stat().st_size, item['size'])
            self.assertEqual(catalog, publish(root, output))
            old = files['Assets/Models/test.bin']['url']
            (models / 'test.bin').write_bytes(b'changed')
            updated = publish(root, output)
            new = next(item['url'] for item in updated['files'] if item['path'].endswith('test.bin'))
            self.assertNotEqual(old, new)
            self.assertFalse((output / old).exists())

    def test_missing_sidecar_fails_the_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'Assets').mkdir()
            (root / 'Assets/model.gltf').write_text('{"buffers":[{"uri":"missing.bin"}]}')
            with self.assertRaisesRegex(ValueError, 'Missing dependency'):
                publish(root, root / 'output')

    def test_compressed_variant_stages_supported_sibling(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            model = root / 'Assets/Models/Example'
            (model / 'glTF').mkdir(parents=True)
            (model / 'glTF-Draco').mkdir()
            (model / 'glTF/Example.gltf').write_text('{"buffers":[{"uri":"Example.bin"}]}')
            (model / 'glTF/Example.bin').write_bytes(b'plain mesh')
            (model / 'glTF-Draco/Example.gltf').write_text(json.dumps({
                'extensionsRequired': ['KHR_draco_mesh_compression'],
                'buffers': [{'uri': 'Example.bin'}]}))
            catalog = publish(root, root / 'output')
            compressed = next(item for item in catalog['files'] if 'glTF-Draco' in item['path'])
            self.assertEqual(compressed['dependencies'], ['Assets/Models/Example/glTF/Example.gltf'])


if __name__ == '__main__':
    unittest.main()
