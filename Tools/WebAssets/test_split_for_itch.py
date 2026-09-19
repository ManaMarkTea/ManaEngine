import json
import tempfile
import unittest
from pathlib import Path
from split_for_itch import split


class SplitForItchTests(unittest.TestCase):
    def _app_bundle(self, root):
        app_bundle = root / 'AppBundle'
        library = app_bundle / 'AssetLibrary'
        library.mkdir(parents=True)
        (library / 'abc123.glb').write_bytes(b'model')
        catalog = {'version': 1, 'files': [
            {'path': 'Assets/Models/duck.glb', 'url': 'AssetLibrary/abc123.glb',
             'sha256': 'abc123', 'size': 5, 'dependencies': []}], 'models': []}
        (app_bundle / 'asset-catalog.json').write_text(json.dumps(catalog), encoding='utf-8')
        return app_bundle, library

    def test_library_moves_out_and_urls_become_absolute(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app_bundle, library = self._app_bundle(root)
            library_out = root / 'pages/web-assets/AssetLibrary'

            split(app_bundle, library_out, 'https://example.github.io/repo/web-assets/AssetLibrary/')

            self.assertFalse(library.exists())
            self.assertEqual((library_out / 'abc123.glb').read_bytes(), b'model')
            updated = json.loads((app_bundle / 'asset-catalog.json').read_text(encoding='utf-8'))
            self.assertEqual(updated['files'][0]['url'],
                              'https://example.github.io/repo/web-assets/AssetLibrary/abc123.glb')
            # The itch-bound bundle keeps only the small catalog now, not the blobs it used to carry.
            self.assertFalse((app_bundle / 'AssetLibrary').exists())

    def test_base_url_must_end_with_slash(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app_bundle, _ = self._app_bundle(root)
            with self.assertRaisesRegex(ValueError, "must end with"):
                split(app_bundle, root / 'pages', 'https://example.github.io/repo/web-assets/AssetLibrary')

    def test_a_url_not_already_local_fails_loudly_rather_than_double_rewriting(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app_bundle, _ = self._app_bundle(root)
            catalog_path = app_bundle / 'asset-catalog.json'
            catalog = json.loads(catalog_path.read_text(encoding='utf-8'))
            catalog['files'][0]['url'] = 'https://already-external.example/abc123.glb'
            catalog_path.write_text(json.dumps(catalog), encoding='utf-8')

            with self.assertRaisesRegex(ValueError, 'Expected a local AssetLibrary url'):
                split(app_bundle, root / 'pages', 'https://example.github.io/repo/web-assets/AssetLibrary/')


if __name__ == "__main__":
    unittest.main()
