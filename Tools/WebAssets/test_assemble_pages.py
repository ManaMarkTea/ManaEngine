import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from assemble_pages import assemble


class PagesAssemblyTests(unittest.TestCase):
    def test_both_entry_urls_share_one_versioned_runtime(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            bundle = root / "bundle"
            (bundle / "_framework").mkdir(parents=True)
            (bundle / "_framework" / "dotnet.native.wasm").write_bytes(b"runtime")
            (bundle / "index.html").write_text("<body>selector</body>", encoding="utf-8")
            output = root / "pages"
            library = output / "web-assets" / "AssetLibrary"
            library.mkdir(parents=True)
            (library / "model.glb").write_bytes(b"model")

            assemble(bundle, output, "abc123")

            runtimes = list(output.rglob("*.wasm"))
            self.assertEqual(runtimes, [output / "app/v/abc123/_framework/dotnet.native.wasm"])
            self.assertEqual(json.loads((output / "app/version.json").read_text()), {"build": "abc123"})
            self.assertEqual((library / "model.glb").read_bytes(), b"model")
            for mode in ("editor", "play"):
                redirect = (output / mode / "index.html").read_text()
                self.assertIn('target.search = location.search', redirect)
                self.assertIn('target.hash = location.hash', redirect)
                self.assertIn(f'target.searchParams.set("mode", "{mode}")', redirect)
                self.assertIn('../app/index.html', redirect)

    def test_build_cannot_escape_version_directory(self):
        with self.assertRaises(ValueError):
            assemble(Path("unused"), Path("unused"), "../escape")

    def test_itch_defaults_to_editor_without_changing_pages_default(self):
        with tempfile.TemporaryDirectory() as scratch:
            bundle = Path(scratch) / "bundle"
            bundle.mkdir()
            (bundle / "index.html").write_text("<body>selector</body>", encoding="utf-8")
            output = Path(scratch) / "pages"
            subprocess.run([
                sys.executable, str(Path(__file__).with_name("assemble_pages.py")),
                "--app-bundle", str(bundle), "--output", str(output),
                "--build", "abc123", "--itch-editor",
            ], check=True)
            self.assertIn('data-start-mode="editor"', (bundle / "index.html").read_text())
            self.assertEqual((output / "app/index.html").read_text(), "<body>selector</body>")
            self.assertEqual((output / "app/v/abc123/index.html").read_text(), "<body>selector</body>")


if __name__ == "__main__":
    unittest.main()
