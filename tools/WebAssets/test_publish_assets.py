import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from publish_assets import publish, EXCLUDED


class PublishAssetsTests(unittest.TestCase):
    def test_complete_catalog_dependencies_and_immutable_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            models = root / 'Mana/Assets/Models'
            models.mkdir(parents=True)
            (models / 'test.gltf').write_text(json.dumps({
                'buffers': [{'uri': 'test.bin'}], 'images': [{'uri': 'paint%20red.png'}]}))
            (models / 'test.bin').write_bytes(b'buffer')
            (models / 'paint red.png').write_bytes(b'texture')
            excluded = root / 'Mana' / EXCLUDED
            excluded.parent.mkdir(parents=True)
            excluded.write_bytes(b'oversized')
            (root / 'Assets').mkdir()
            with zipfile.ZipFile(root / 'Assets/pack.zip', 'w') as archive:
                archive.writestr('Models/ship.glb', b'model')
            output = root / 'output'
            catalog = publish(root, output)
            self.assertEqual(len(catalog['models']), 2)
            files = {item['path']: item for item in catalog['files']}
            self.assertNotIn(EXCLUDED, files)
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
            model = root / 'Assets/Models/Duck'
            (model / 'glTF').mkdir(parents=True)
            (model / 'glTF-Draco').mkdir()
            (model / 'glTF/Duck.gltf').write_text('{"buffers":[{"uri":"Duck.bin"}]}')
            (model / 'glTF/Duck.bin').write_bytes(b'plain mesh')
            (model / 'glTF-Draco/Duck.gltf').write_text(json.dumps({
                'extensionsRequired': ['KHR_draco_mesh_compression'],
                'buffers': [{'uri': 'Duck.bin'}]}))
            catalog = publish(root, root / 'output')
            compressed = next(item for item in catalog['files'] if 'glTF-Draco' in item['path'])
            self.assertEqual(compressed['dependencies'], ['Assets/Models/Duck/glTF/Duck.gltf'])


if __name__ == '__main__':
    unittest.main()
