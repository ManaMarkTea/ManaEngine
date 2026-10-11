"""Build the categorized starter packs; original downloads stay in Artifacts/.

python Tools/Materials/build_starters.py --download
Requires curl, Pillow and numpy. Sources and their licenses are recorded in starter-packs.json.
"""
import argparse
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

from compact import compact


def build(download=False):
    root = Path(__file__).resolve().parents[2]
    scratch = root / 'Artifacts' / 'MaterialSources'
    scratch.mkdir(parents=True, exist_ok=True)
    packs = json.loads(Path(__file__).with_name('starter-packs.json').read_text())
    for pack in packs:
        if pack['license'] != 'CC0-1.0':
            raise ValueError(f"Review license for {pack['name']} before redistributing")
        source = scratch / (pack['name'] + '-original.zip')
        if not zipfile.is_zipfile(source):
            if not download:
                raise FileNotFoundError(f'{source}: run with --download to fetch missing sources')
            curl = shutil.which('curl')
            if curl is None:
                raise RuntimeError('curl is required to download starter sources')
            partial = source.with_suffix('.zip.part')
            subprocess.run([curl, '--fail', '--silent', '--show-error', '--location', '--retry', '2',
                            '--user-agent', 'Mozilla/5.0', '--output', str(partial), pack['download']],
                           check=True, timeout=300)
            if not zipfile.is_zipfile(partial):
                raise ValueError(f"Invalid source ZIP for {pack['name']}")
            partial.replace(source)
        output = root / 'Mana' / 'Assets' / 'Materials' / pack['category'] / (pack['name'] + '.zip')
        compact(source, output, pack['source'], pack['size'], pack['creator'],
                pack.get('metallic'), pack.get('roughness'))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download', action='store_true', help='Fetch missing source archives')
    build(parser.parse_args().download)
