"""Validate authored palette hints, original icon provenance and 32px palette PNGs."""
import argparse
import hashlib
import json
import pathlib
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--contact-sheet', type=pathlib.Path)
parser.add_argument('--catalog', type=pathlib.Path)
args = parser.parse_args()
manifest = json.loads((ROOT / 'game/editor/icons/manifest.json').read_text(encoding='utf-8'))
palette = []
for line in (ROOT / 'tools/palette.gpl').read_text().splitlines():
    fields = line.split()
    if len(fields) >= 3 and all(value.isdecimal() for value in fields[:3]):
        palette.append(tuple(map(int, fields[:3])))
assert len(palette) == 48
allowed = set(palette)
assert (manifest['width'], manifest['height'], manifest['paletteColors']) == (32, 32, 48)
records = {record['id']: record for record in manifest['icons']}
assert len(records) == len(manifest['icons'])
expected, images = {}, []
for group in ('tiles', 'entities'):
    for source in sorted((ROOT / 'game/content' / group).glob('*.json')):
        definition = json.loads(source.read_text(encoding='utf-8'))
        hint = definition.get('editor', {})
        assert definition.get('displayName') and definition.get('category'), source
        assert isinstance(hint.get('char'), str) and len(hint['char']) == 1, source
        path = hint.get('icon', '')
        assert path.startswith('editor/icons/') and path.endswith('.png') and '\\' not in path, source
        file = (ROOT / 'game' / path).resolve()
        assert file.is_relative_to((ROOT / 'game/editor/icons').resolve()) and file.is_file(), source
        record = records[definition['id']]
        assert record['path'] == path and hashlib.sha256(file.read_bytes()).hexdigest() == record['sha256'], path
        imported = dict(line.split('=', 1) for line in file.with_suffix('.png.import').read_text(encoding='utf-8').splitlines() if '=' in line)
        assert imported.get('compress/mode') == '0' and imported.get('mipmaps/generate') == 'false', f'UI icon must stay lossless without mipmaps: {path}'
        with Image.open(file) as image:
            assert image.size == (32, 32) and image.mode == 'RGBA', path
            pixels = list(image.get_flattened_data())
            assert all(pixel[3] in (0, 255) and (pixel[3] == 0 or pixel[:3] in allowed) for pixel in pixels), path
            opaque = sum(pixel[3] == 255 for pixel in pixels)
            bounds = image.getchannel('A').getbbox()
            assert 16 <= opaque <= 900 and bounds is not None, (path, opaque)
            assert bounds[0] >= 1 and bounds[1] >= 1 and bounds[2] <= 31 and bounds[3] <= 31, (path, bounds)
            images.append((group + '/' + source.stem, image.copy()))
        editor_only = definition.get('render', {}).get('editorOnly')
        assert editor_only is None or editor_only == path, source
        expected[definition['id']] = path
assert set(records) == set(expected)
if args.catalog:
    catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    assert catalog['contentHash'] == manifest['contentHash'], 'Icon manifest and actual registry identity differ.'
    entries = catalog['tiles'] + catalog['entities']
    assert {entry['id']: entry['icon'] for entry in entries} == expected
    assert all(isinstance(entry['char'], str) and len(entry['char']) == 1 for entry in entries)
assert {str(path.relative_to(ROOT / 'game')).replace('\\', '/') for path in (ROOT / 'game/editor/icons').rglob('*.png')} == set(expected.values())
for relative, digest in manifest['inputs'].items():
    path = (ROOT / relative).resolve()
    assert path.is_relative_to(ROOT) and path.is_file(), relative
    assert hashlib.sha256(path.read_bytes()).hexdigest() == digest, f'Stale editor icon source: {relative}; regenerate in the pinned native host.'
if args.contact_sheet:
    columns, width, height = 6, 132, 98
    sheet = Image.new('RGB', (columns * width, ((len(images) + columns - 1) // columns) * height), '#242833')
    draw = ImageDraw.Draw(sheet)
    for index, (name, icon) in enumerate(images):
        x, y = index % columns * width, index // columns * height
        sheet.paste(icon.resize((64, 64), Image.Resampling.NEAREST), (x + 34, y + 3), icon.resize((64, 64), Image.Resampling.NEAREST))
        label = name.split('/')[-1]
        draw.text((x + 4, y + 73), label, fill='white')
    args.contact_sheet.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.contact_sheet)
print(f'Editor palette: {len(expected)} authored icons/chars, 32px original-palette RGBA assets, current input/output hashes')
