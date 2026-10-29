"""Build a labelled review index without cropping the original scenario captures."""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
metadata = sorted(args.directory.glob("*-1.capture.json"))
if not metadata:
    raise SystemExit("No scenario captures found")
columns, cell_width, cell_height = 3, 336, 212
sheet = Image.new("RGB", (columns * cell_width, ((len(metadata) + columns - 1) // columns) * cell_height), "#242833")
draw = ImageDraw.Draw(sheet)
for index, path in enumerate(metadata):
    record = json.loads(path.read_text(encoding="utf-8"))
    image_path = path.with_name(path.name.removesuffix(".capture.json") + ".png")
    x, y = (index % columns) * cell_width, (index // columns) * cell_height
    with Image.open(image_path) as image:
        thumb = ImageOps.contain(image.convert("RGB"), (320, 180), Image.Resampling.NEAREST)
        sheet.paste(thumb, (x + (cell_width - thumb.width) // 2, y + 8 + (180 - thumb.height) // 2))
    draw.text((x + 8, y + 190), record["scenario"], fill="white")
args.output.parent.mkdir(parents=True, exist_ok=True)
sheet.save(args.output)
print(f"Review contact sheet: {len(metadata)} captures, {sheet.width}x{sheet.height}")
