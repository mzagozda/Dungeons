"""1024x1024 atlas, 16px tiles and 2px bleed gutters; --check is read-only."""
import argparse
import hashlib
import io
import json
import pathlib
from PIL import Image
from make_textures import palette

ROOT = pathlib.Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    colors = set(palette())
    atlas = Image.new("RGBA", (1024, 1024), (*palette()[0], 0))
    rects = {}
    identity = hashlib.sha256()
    for index, path in enumerate(sorted((ROOT / "game/textures/terrain").glob("*.png"))):
        image = Image.open(path).convert("RGBA")
        if image.size != (16, 16) or any(pixel[:3] not in colors for pixel in image.get_flattened_data()):
            raise SystemExit(f"Texture size/palette violation: {path}")
        x, y = index % 51 * 20 + 2, index // 51 * 20 + 2
        if y + 18 > 1024:
            raise SystemExit("Atlas is full")
        for oy in range(-2, 18):
            for ox in range(-2, 18):
                atlas.putpixel((x + ox, y + oy), image.getpixel((min(15, max(0, ox)), min(15, max(0, oy)))))
        rects[path.stem] = [x, y, 16, 16]
        identity.update(path.name.encode() + b"\0" + path.read_bytes())
    if not rects:
        raise SystemExit("No terrain texture sources")
    metadata = {"formatVersion": 1, "width": 1024, "height": 1024, "tileSize": 16, "gutter": 2, "sourceHash": identity.hexdigest(), "tiles": rects}
    encoded = io.BytesIO()
    atlas.save(encoded, format="PNG", optimize=False)
    outputs = {ROOT / "game/textures/terrain_atlas.png": encoded.getvalue(), ROOT / "game/content/atlas.json": (json.dumps(metadata, indent=2) + "\n").encode()}
    for path, data in outputs.items():
        if args.check:
            if not path.exists() or path.read_bytes() != data:
                raise SystemExit(f"Atlas is stale: {path.relative_to(ROOT)}")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    print(f"Atlas {'checked' if args.check else 'packed'}: {len(rects)} tiles")


if __name__ == "__main__":
    main()
