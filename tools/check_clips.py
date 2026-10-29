"""Audit referenced models/variants, texture palette, editable sources and animation clips."""
import json
import pathlib
import struct
from PIL import Image
from make_textures import palette

ROOT = pathlib.Path(__file__).resolve().parent.parent
checked = 0
allowed = set(palette())
definitions = sorted((ROOT / "game/content/entities").glob("*.json")) + sorted((ROOT / "game/content/tiles").glob("*.json"))
models = []
for path in definitions:
    definition = json.loads(path.read_text(encoding="utf-8"))
    render = definition.get("render", {})
    if not render.get("model"):
        continue
    for variant in range(render.get("variants", 1)):
        model = pathlib.Path(render["model"])
        if variant:
            model = model.with_name(model.stem + f"_{variant}" + model.suffix)
        models.append((ROOT / "game" / model, render))
for model, render in models:
    if not model.exists():
        raise SystemExit(f"Missing model: {model.relative_to(ROOT)}")
    data = model.read_bytes()
    magic, version, total = struct.unpack_from("<III", data)
    size, kind = struct.unpack_from("<II", data, 12)
    if magic != 0x46546C67 or version != 2 or total != len(data) or kind != 0x4E4F534A:
        raise SystemExit(f"Invalid GLB header: {model}")
    gltf = json.loads(data[20:20 + size])
    if gltf.get("skins"):
        raise SystemExit(f"Cuboid models must use rigid animation, not skinning: {model}")
    for image in gltf["images"]:
        texture = (model.parent / image["uri"]).resolve()
        if not texture.is_relative_to(ROOT / "game/textures/models"):
            raise SystemExit(f"Unexpected model texture path: {texture}")
        with Image.open(texture) as pixels:
            if pixels.size != (64, 64) or any(color[:3] not in allowed for color in pixels.convert("RGBA").get_flattened_data()):
                raise SystemExit(f"Texture violates size/palette: {texture}")
    editable = json.loads((ROOT / "assets/source/models" / (model.stem + ".bbmodel")).read_text(encoding="utf-8"))
    source_clips = {clip["name"] for clip in editable["animations"]}
    clips = {clip["name"]: clip for clip in gltf.get("animations", [])}
    if source_clips != set(clips):
        raise SystemExit(f"Editable animation sources differ from GLB: {model}")
    for name in render.get("animations", {}).values():
        if name not in clips:
            raise SystemExit(f"Missing clip {name}: {model}")
        for sampler in clips[name]["samplers"]:
            times = gltf["accessors"][sampler["input"]]
            if times["count"] < 2 or times["max"][0] <= times["min"][0]:
                raise SystemExit(f"Zero-length clip {name}: {model}")
    checked += 1
print(f"Asset checks passed: {checked} referenced entity/tile model variants, palette textures and editable clips")
