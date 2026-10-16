import json
import tempfile
import unittest
from pathlib import Path

from publish_screensaver import publish


class ScreensaverExportTests(unittest.TestCase):
    def test_exports_real_source_with_paths_but_no_unrelated_files(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            source = root / "Mana/Code"
            source.mkdir(parents=True)
            code = "\treturn 42;\n" * 25
            (source / "Example.cs").write_text(code, encoding="utf-8")
            (source / "settings.json").write_text('{"private":true}', encoding="utf-8")
            (root / "secrets.cs").write_text(code, encoding="utf-8")
            output = root / "site"
            self.assertEqual(publish(root, output), 1)
            files = json.loads((output / "sources.json").read_text())
            self.assertEqual(files, [{"path": "Mana/Code/Example.cs", "lines": ["    return 42;"] * 25}])
            self.assertTrue((output / "index.html").is_file())
            self.assertTrue((output / "screensaver.js").is_file())
            self.assertTrue((output / "agent-ledger.js").is_file())
            self.assertTrue((output / "code-syntax.js").is_file())

    def test_missing_sources_fails_the_publish(self):
        with tempfile.TemporaryDirectory() as scratch:
            with self.assertRaises(ValueError):
                publish(Path(scratch), Path(scratch) / "site")


if __name__ == "__main__":
    unittest.main()
