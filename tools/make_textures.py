"""Original deterministic pixel textures, exclusively from palette.gpl.

These code-native source textures are editable PNGs. No reference-game assets.
Tinted terrain tops use neutral palette values; colour comes from tile metadata.
"""
import hashlib
import pathlib
import random
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent


def palette():
    colors = []
    for line in (ROOT / "tools/palette.gpl").read_text().splitlines():
        fields = line.split()
        if len(fields) >= 3 and all(c.isdecimal() for c in fields[:3]):
            colors.append(tuple(map(int, fields[:3])))
    assert len(colors) == 48
    return colors


def main():
    colors = palette()
    destination = ROOT / "game/textures/terrain"
    destination.mkdir(parents=True, exist_ok=True)
    textures = {
        **{f"grass_top_{i}": [7, 7, 7, 31, 30] for i in range(4)},
        "dirt_side": [18, 19, 19, 20, 17], "path": [7, 7, 31, 30],
        "stone_top": [7, 7, 31, 30], "stone_side": [28, 29, 29, 30],
        "stone_cracked": [7, 31, 31, 30], "planks": [19, 20, 20, 21],
        "sand_dark": [16, 17, 18, 19], "water": [32, 33, 34, 34, 35],
        "bark": [16, 17, 18], "leaves": [8, 9, 10, 10, 11],
    }
    for name, swatches in textures.items():
        rng = random.Random(int.from_bytes(hashlib.sha256(name.encode()).digest()[:8], "little"))
        image = Image.new("RGBA", (16, 16))
        for z in range(16):
            for x in range(16):
                index = rng.choice(swatches)
                if name == "planks" and (z % 4 == 0 or x == (z // 4 * 7) % 16):
                    index = 16
                if name == "bark" and x % 5 == 0:
                    index = 16
                if name == "stone_side" and (z % 8 == 0 or x == (0 if z < 8 else 8)):
                    index = 26
                if name == "water" and (x + z * 3) % 13 == 0:
                    index = 36
                image.putpixel((x, z), colors[index] + (255,))
        if name == "stone_cracked":
            draw = ImageDraw.Draw(image)
            draw.line([(2, 0), (5, 4), (4, 7), (9, 10), (8, 15)], fill=colors[26], width=1)
        image.save(destination / f"{name}.png", optimize=False)
    for name in ("tall_grass", "flowers", "foam"):
        image = Image.new("RGBA", (16, 16), colors[0] + (0,))
        draw = ImageDraw.Draw(image)
        if name == "foam":
            for x in range(16):
                for z in range(2 + (x % 3 == 0)):
                    image.putpixel((x, z), colors[37] + (255,))
        else:
            for x, top in ((3, 8), (6, 3), (9, 6), (12, 5)):
                draw.line([(x, 15), (x, top)], fill=colors[10] + (255,), width=1)
                draw.line([(x, 13), (x - 2, 10)], fill=colors[11] + (255,), width=1)
                if name == "flowers":
                    draw.rectangle((x - 1, top, x + 1, top + 2), fill=colors[46 if x % 2 else 7] + (255,))
                    image.putpixel((x, top + 1), colors[45] + (255,))
        image.save(destination / f"{name}.png", optimize=False)
    print(f"Wrote {len(textures) + 3} original 16x16 palette textures")


if __name__ == "__main__":
    main()
