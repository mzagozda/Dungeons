"""Original 16px, hard-alpha, palette-only nine-slice UI assets and shared theme.

Silkscreen is an unmodified, pinned OFL dependency; its provenance is separate.
Run normally to regenerate, or --check to compare without changing files.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent


def outputs():
    colors = []
    for line in (ROOT / "tools/palette.gpl").read_text().splitlines():
        fields = line.split()
        if len(fields) >= 3 and all(part.isdecimal() for part in fields[:3]):
            colors.append(tuple(map(int, fields[:3])))
    assert len(colors) == 48
    result = {}

    def save(name, image):
        memory = io.BytesIO(); image.save(memory, format="PNG", optimize=False)
        result[f"game/assets/ui/{name}.png"] = memory.getvalue()

    for name, fill, border in (("panel", 1, 2), ("button_hover", 24, 45), ("button_pressed", 0, 45), ("button_disabled", 1, 24), ("focus", None, 45)):
        image = Image.new("RGBA", (16, 16)); draw = ImageDraw.Draw(image)
        draw.polygon([(2, 0), (13, 0), (15, 2), (15, 13), (13, 15), (2, 15), (0, 13), (0, 2)], fill=colors[border] + (255,))
        draw.rectangle((2, 2, 13, 13), fill=colors[fill] + (255,) if fill is not None else (0, 0, 0, 0))
        save(name, image)
    for name, checked in (("check_off", False), ("check_on", True)):
        image = Image.new("RGBA", (16, 16)); draw = ImageDraw.Draw(image)
        draw.rectangle((1, 1, 14, 14), fill=colors[1] + (255,), outline=colors[45 if checked else 2] + (255,), width=2)
        if checked:
            for x, y in ((4, 7), (5, 8), (6, 9), (7, 8), (8, 7), (9, 6), (10, 5), (11, 4)):
                draw.rectangle((x, y, x + 1, y + 1), fill=colors[45] + (255,))
        save(name, image)
    image = Image.new("RGBA", (16, 16)); draw = ImageDraw.Draw(image)
    for y, width in ((6, 8), (7, 6), (8, 4), (9, 2)):
        draw.rectangle((8 - width // 2, y, 7 + width // 2, y), fill=colors[45] + (255,))
    for name, angle in (("arrow_down", 0), ("arrow_up", 180), ("arrow_left", 270), ("arrow_right", 90)):
        save(name, image.rotate(angle, resample=Image.Resampling.NEAREST))
    image = Image.new("RGBA", (16, 16)); draw = ImageDraw.Draw(image)
    draw.rectangle((3, 3, 12, 12), fill=colors[45] + (255,)); draw.rectangle((5, 5, 10, 10), fill=colors[1] + (255,)); save("slider_grab", image)

    # Original 8px fallback glyphs keep controller directions/HUD diamonds crisp.
    glyphs = {8592: ["........", "...#....", "..#.....", ".######.", "..#.....", "...#....", "........", "........"],
              8593: ["........", "...#....", "..###...", ".#.#.#..", "...#....", "...#....", "...#....", "........"],
              8594: ["........", "....#...", ".....#..", ".######.", ".....#..", "....#...", "........", "........"],
              8595: ["........", "...#....", "...#....", "...#....", ".#.#.#..", "..###...", "...#....", "........"],
              9671: ["...#....", "..#.#...", ".#...#..", "#.....#.", ".#...#..", "..#.#...", "...#....", "........"],
              9888: ["...#....", "..#.#...", "..###...", ".##.##..", ".##.##..", "#######.", "........", "........"]}
    image = Image.new("RGBA", (64, 8))
    chars = []
    for index, (code, rows) in enumerate(glyphs.items()):
        for y, row in enumerate(rows):
            for x, pixel in enumerate(row):
                if pixel == "#": image.putpixel((index * 8 + x, y), colors[7] + (255,))
        chars.append(f"char id={code} x={index * 8} y=0 width=8 height=8 xoffset=0 yoffset=0 xadvance=8 page=0 chnl=15")
    memory = io.BytesIO(); image.save(memory, format="PNG", optimize=False)
    result["game/assets/fonts/block_symbols.png"] = memory.getvalue()
    result["game/assets/fonts/block_symbols.fnt"] = ('info face="Block Symbols" size=8 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=0 aa=1 padding=0,0,0,0 spacing=0,0\n'
        'common lineHeight=8 base=7 scaleW=64 scaleH=8 pages=1 packed=0\npage id=0 file="block_symbols.png"\nchars count=6\n' + '\n'.join(chars) + '\n').encode()

    textures = ["panel", "button_hover", "button_pressed", "button_disabled", "focus", "check_off", "check_on", "arrow_down", "arrow_up", "arrow_left", "arrow_right", "slider_grab"]
    lines = ['[gd_resource type="Theme" load_steps=20 format=3]', '', '[ext_resource type="FontFile" path="res://assets/fonts/Silkscreen-Regular.ttf" id="font"]']
    lines += [f'[ext_resource type="Texture2D" path="res://assets/ui/{name}.png" id="{name}"]' for name in textures]
    for name in textures[:5]:
        lines += ['', f'[sub_resource type="StyleBoxTexture" id="{name}"]']
        lines += [f'content_margin_{side} = {8 if side in ("left", "right") else 6}.0' for side in ("left", "right", "top", "bottom")]
        lines += [f'texture = ExtResource("{name}")']
        lines += [f'texture_margin_{side} = 4.0' for side in ("left", "right", "top", "bottom")]
        if name == "focus": lines += ['draw_center = false']
    lines += ['', '[sub_resource type="StyleBoxFlat" id="slider"]', 'bg_color = Color(0.235294, 0.235294, 0.27451, 1)', 'content_margin_top = 3.0', 'content_margin_bottom = 3.0']
    lines += ['', '[resource]', 'default_font = ExtResource("font")', 'default_font_size = 16']
    for control in ("Panel", "PanelContainer", "PopupPanel", "TabContainer", "TooltipPanel"):
        lines += [f'{control}/styles/panel = SubResource("panel")']
    for control in ("Button", "OptionButton", "MenuButton", "LineEdit"):
        for state, style in (("normal", "panel"), ("hover", "button_hover"), ("pressed", "button_pressed"), ("disabled", "button_disabled"), ("focus", "focus")):
            lines += [f'{control}/styles/{state} = SubResource("{style}")']
        lines += [f'{control}/colors/font_color = Color(1, 1, 1, 1)', f'{control}/colors/font_hover_color = Color(0.886275, 0.713726, 0.266667, 1)']
    for state, style in (("tab_selected", "button_pressed"), ("tab_unselected", "panel"), ("tab_hovered", "button_hover"), ("tab_disabled", "button_disabled"), ("tab_focus", "focus")):
        lines += [f'{control}/styles/{state} = SubResource("{style}")' for control in ("TabBar", "TabContainer")]
    lines += [f'{control}/colors/font_selected_color = Color(0.886275, 0.713726, 0.266667, 1)' for control in ("TabBar", "TabContainer")]
    lines += ['OptionButton/icons/arrow = ExtResource("arrow_down")']
    for name, direction in (("increment", "right"), ("increment_highlight", "right"), ("decrement", "left"), ("decrement_highlight", "left")):
        lines += [f'{control}/icons/{name} = ExtResource("arrow_{direction}")' for control in ("TabBar", "TabContainer")]
    for control in ("CheckBox", "CheckButton"):
        for state in ("checked", "checked_disabled", "radio_checked", "radio_checked_disabled", "on", "on_disabled"):
            lines += [f'{control}/icons/{state} = ExtResource("check_on")']
        for state in ("unchecked", "unchecked_disabled", "radio_unchecked", "radio_unchecked_disabled", "off", "off_disabled"):
            lines += [f'{control}/icons/{state} = ExtResource("check_off")']
        lines += [f'{control}/styles/focus = SubResource("focus")']
    for control, lower, upper in (("HScrollBar", "left", "right"), ("VScrollBar", "up", "down")):
        for state, style in (("scroll", "panel"), ("scroll_focus", "focus"), ("grabber", "button_hover"), ("grabber_highlight", "button_pressed"), ("grabber_pressed", "button_pressed")):
            lines += [f'{control}/styles/{state} = SubResource("{style}")']
        for state in ("decrement", "decrement_highlight", "decrement_pressed"):
            lines += [f'{control}/icons/{state} = ExtResource("arrow_{lower}")']
        for state in ("increment", "increment_highlight", "increment_pressed"):
            lines += [f'{control}/icons/{state} = ExtResource("arrow_{upper}")']
    for control in ("HSlider", "VSlider"):
        for state in ("slider", "grabber_area", "grabber_area_highlight"):
            lines += [f'{control}/styles/{state} = SubResource("slider")']
        for state in ("grabber", "grabber_highlight", "grabber_disabled"):
            lines += [f'{control}/icons/{state} = ExtResource("slider_grab")']
    result["game/assets/ui/block_dungeons.tres"] = ('\n'.join(lines) + '\n').encode()
    return result


def main():
    check = argparse.ArgumentParser(); check.add_argument("--check", action="store_true"); args = check.parse_args()
    files = outputs()
    for path, data in files.items():
        target = ROOT / path
        if args.check:
            assert target.is_file() and target.read_bytes() == data, f"Stale UI asset: {path}"
        else:
            target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(data)
    manifest_path = ROOT / "assets/source/fonts/manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    for entry in manifest["files"]:
        assert hashlib.sha256((ROOT / entry["path"]).read_bytes()).hexdigest() == entry["sha256"], f"Font provenance mismatch: {entry['path']}"
    font_import = ROOT / "game/assets/fonts/Silkscreen-Regular.ttf.import"
    if args.check:
        settings = font_import.read_text()
        for required in ("antialiasing=0", "subpixel_positioning=0", "multichannel_signed_distance_field=false", "generate_mipmaps=false", "oversampling=1.0"):
            assert required in settings, f"Pixel font import must preserve {required}"
        assert "scaling_mode=1" in (ROOT / "game/assets/fonts/block_symbols.fnt.import").read_text(), "Pixel symbol font must scale by whole pixels"
        for path in files:
            if path.endswith(".png"):
                settings = (ROOT / (path + ".import")).read_text()
                if path.endswith("fonts/block_symbols.png"):
                    # The BMFont importer embeds its page losslessly in fontdata,
                    # and deliberately marks the standalone source image skipped.
                    assert 'importer="skip"' in settings, path
                else:
                    assert "compress/mode=0" in settings and "mipmaps/generate=false" in settings, path
    print("UI assets: 12 original 16px palette icons/panels, six original pixel symbol glyphs, shared nine-slice theme, pinned OFL font hashes")


if __name__ == "__main__":
    main()
