"""Original cuboid models: editable Blockbench sources and equivalent glTF 2.0 GLB exports.

Metres, +Y up, front -Z, feet/base-centred pivots. No skinning or copied assets.
Re-run explicitly after editing this procedural source; hand-edited .bbmodel files
are retained as the artist-facing starting point under assets/source/models/.
"""
import base64
import io
import json
import math
import pathlib
import struct
import uuid
from PIL import Image, ImageDraw
from make_textures import palette

ROOT = pathlib.Path(__file__).resolve().parent.parent
COLORS = palette()


def identity(name):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "block-dungeons:" + name))


class Model:
    def __init__(self, name, folder, swatches):
        self.name, self.folder = name, folder
        self.binary = bytearray()
        self.doc = {"asset": {"version": "2.0", "generator": "Block Dungeons original cuboid pipeline"}, "scene": 0, "scenes": [{"nodes": [0]}],
                    "nodes": [{"name": name, "children": []}], "meshes": [], "accessors": [], "bufferViews": [], "animations": []}
        self.bones = {"root": 0}
        self.elements = []
        self.outliner = []
        self.animation_sources = {}
        image = Image.new("RGBA", (64, 64), COLORS[0] + (255,))
        for slot, color in enumerate(swatches):
            for y in range(8):
                for x in range(8):
                    shade = color if (x + 3 * y) % 19 else {0: 0, 7: 31, 16: 0, 24: 0, 32: 3, 38: 16, 42: 8, 44: 38, 45: 21, 46: 6, 47: 23}.get(color, color - 1)
                    image.putpixel((slot * 8 + x, y), COLORS[shade] + (255,))
        if folder == "characters":
            draw = ImageDraw.Draw(image)
            if name == "hero":
                draw.rectangle((8, 0, 15, 1), fill=COLORS[16] + (255,))
            draw.rectangle((9, 2, 10, 3), fill=COLORS[0] + (255,))
            draw.rectangle((13, 2, 14, 3), fill=COLORS[0] + (255,))
            draw.line((11, 6, 13, 6), fill=COLORS[16] + (255,))
        output = io.BytesIO()
        image.save(output, format="PNG", optimize=False)
        self.png = output.getvalue()
        self.doc.update({"images": [{"uri": "../../textures/models/" + name + ".png"}], "samplers": [{"magFilter": 9728, "minFilter": 9728, "wrapS": 33071, "wrapT": 33071}],
                         "textures": [{"source": 0, "sampler": 0}], "materials": [{"name": name + "_palette", "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0, "roughnessFactor": 1}}]})

    def view(self, data, target=None):
        while len(self.binary) % 4:
            self.binary.append(0)
        index = len(self.doc["bufferViews"])
        item = {"buffer": 0, "byteOffset": len(self.binary), "byteLength": len(data)}
        if target:
            item["target"] = target
        self.doc["bufferViews"].append(item)
        self.binary.extend(data)
        return index

    def accessor(self, values, components, kind="f", target=None):
        flattened = [number for value in values for number in (value if isinstance(value, (list, tuple)) else [value])]
        data = struct.pack("<" + kind * len(flattened), *flattened)
        item = {"bufferView": self.view(data, target), "componentType": 5126 if kind == "f" else 5123, "count": len(values), "type": {1: "SCALAR", 2: "VEC2", 3: "VEC3", 4: "VEC4"}[components]}
        item["min"] = [min(flattened[i::components]) for i in range(components)]
        item["max"] = [max(flattened[i::components]) for i in range(components)]
        index = len(self.doc["accessors"])
        self.doc["accessors"].append(item)
        return index

    def bone(self, name, pivot=(0, 0, 0), parent="root"):
        index = len(self.doc["nodes"])
        self.doc["nodes"].append({"name": name, "translation": list(pivot), "children": []})
        self.doc["nodes"][self.bones[parent]]["children"].append(index)
        self.bones[name] = index
        self.outliner.append({"name": name, "origin": [v * 16 for v in pivot], "uuid": identity(self.name + name), "children": []})
        return name

    def cube(self, name, center, size, slot=0, bone="root", face_slot=None, emissive=False):
        pivot = self.doc["nodes"][self.bones[bone]].get("translation", [0, 0, 0])
        low = [center[i] - size[i] / 2 - pivot[i] for i in range(3)]
        high = [low[i] + size[i] for i in range(3)]
        x, y, z = low
        X, Y, Z = high
        faces = [([(x, Y, z), (X, Y, z), (X, Y, Z), (x, Y, Z)], (0, 1, 0)),
                 ([(x, y, Z), (X, y, Z), (X, y, z), (x, y, z)], (0, -1, 0)),
                 ([(x, y, z), (X, y, z), (X, Y, z), (x, Y, z)], (0, 0, -1)),
                 ([(X, y, z), (X, y, Z), (X, Y, Z), (X, Y, z)], (1, 0, 0)),
                 ([(X, y, Z), (x, y, Z), (x, Y, Z), (X, Y, Z)], (0, 0, 1)),
                 ([(x, y, Z), (x, y, z), (x, Y, z), (x, Y, Z)], (-1, 0, 0))]
        positions, normals, uvs, indices = [], [], [], []
        for face, (vertices, normal) in enumerate(faces):
            positions.extend(vertices)
            normals.extend([normal] * 4)
            tile = face_slot if face == 2 and face_slot is not None else slot
            u0, u1, v0, v1 = (tile * 8 + 0.5) / 64, (tile * 8 + 7.5) / 64, 0.5 / 64, 7.5 / 64
            uvs.extend([(u0, v1), (u1, v1), (u1, v0), (u0, v0)])
            start = face * 4
            indices.extend([start, start + 2, start + 1, start, start + 3, start + 2])
        material = 0
        if emissive:
            if len(self.doc["materials"]) == 1:
                glow = json.loads(json.dumps(self.doc["materials"][0]))
                glow["name"] = self.name + "_emissive"
                glow["emissiveTexture"] = {"index": 0}
                glow["emissiveFactor"] = [1, 1, 1]
                glow["extensions"] = {"KHR_materials_emissive_strength": {"emissiveStrength": 2.5}}
                self.doc["extensionsUsed"] = ["KHR_materials_emissive_strength"]
                self.doc["materials"].append(glow)
            material = 1
        primitive = {"attributes": {"POSITION": self.accessor(positions, 3, target=34962), "NORMAL": self.accessor(normals, 3, target=34962), "TEXCOORD_0": self.accessor(uvs, 2, target=34962)}, "indices": self.accessor(indices, 1, "H", 34963), "material": material}
        mesh_index = len(self.doc["meshes"])
        self.doc["meshes"].append({"name": name, "primitives": [primitive]})
        node_index = len(self.doc["nodes"])
        self.doc["nodes"].append({"name": name, "mesh": mesh_index})
        self.doc["nodes"][self.bones[bone]]["children"].append(node_index)
        element_uuid = identity(self.name + name)
        self.elements.append({"uuid": element_uuid, "type": "cube", "name": name, "from": [(center[i] - size[i] / 2) * 16 for i in range(3)], "to": [(center[i] + size[i] / 2) * 16 for i in range(3)], "origin": [v * 16 for v in pivot],
                              "faces": {direction: {"uv": [(face_slot if direction == "north" and face_slot is not None else slot) * 8, 0, (face_slot if direction == "north" and face_slot is not None else slot) * 8 + 8, 8], "texture": 0} for direction in ("north", "east", "south", "west", "up", "down")}})
        if bone == "root":
            self.outliner.append(element_uuid)
        else:
            next(item for item in self.outliner if isinstance(item, dict) and item["name"] == bone)["children"].append(element_uuid)

    def animation(self, name, bone, times, angles, axis=0):
        rotations = []
        for angle in angles:
            q = [0, 0, 0, math.cos(math.radians(angle) / 2)]
            q[axis] = math.sin(math.radians(angle) / 2)
            rotations.append(q)
        self.doc["animations"].append({"name": name, "samplers": [{"input": self.accessor(times, 1), "output": self.accessor(rotations, 4), "interpolation": "LINEAR"}], "channels": [{"sampler": 0, "target": {"node": self.bones[bone], "path": "rotation"}}]})
        self.animation_sources[self.doc["animations"][-1]["samplers"][0]["output"]] = (times, [[angle if i == axis else 0 for i in range(3)] for angle in angles])

    def translation(self, name, bone, times, values):
        self.doc["animations"].append({"name": name, "samplers": [{"input": self.accessor(times, 1), "output": self.accessor(values, 3), "interpolation": "LINEAR"}], "channels": [{"sampler": 0, "target": {"node": self.bones[bone], "path": "translation"}}]})
        self.animation_sources[self.doc["animations"][-1]["samplers"][0]["output"]] = (times, [[v * 16 for v in value] for value in values])

    def write(self):
        self.doc["buffers"] = [{"byteLength": len(self.binary)}]
        header = json.dumps(self.doc, separators=(",", ":")).encode()
        header += b" " * (-len(header) % 4)
        binary = bytes(self.binary) + b"\0" * (-len(self.binary) % 4)
        payload = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(header) + 8 + len(binary)) + struct.pack("<II", len(header), 0x4E4F534A) + header + struct.pack("<II", len(binary), 0x004E4942) + binary
        target = ROOT / "game/models" / self.folder / (self.name + ".glb")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(payload)
        texture = ROOT / "game/textures/models" / (self.name + ".png")
        texture.parent.mkdir(parents=True, exist_ok=True)
        texture.write_bytes(self.png)
        animations = []
        bone_names = {index: name for name, index in self.bones.items()}
        for clip in self.doc["animations"]:
            animators = {}
            length = 0
            for channel in clip["channels"]:
                bone_name = bone_names[channel["target"]["node"]]
                bone_uuid = identity(self.name + bone_name)
                animator = animators.setdefault(bone_uuid, {"name": bone_name, "type": "bone", "keyframes": []})
                times, values = self.animation_sources[clip["samplers"][channel["sampler"]]["output"]]
                length = max(length, times[-1])
                for time, value in zip(times, values):
                    animator["keyframes"].append({"channel": "rotation" if channel["target"]["path"] == "rotation" else "position", "time": time,
                                                  "interpolation": "linear", "data_points": [dict(zip(("x", "y", "z"), (str(v) for v in value)))]})
            animations.append({"uuid": identity(self.name + ":clip:" + clip["name"]), "name": clip["name"], "loop": "loop" if clip["name"] in ("idle", "walk") else "once", "length": length, "animators": animators})
        editable = {"meta": {"format_version": "4.10", "model_format": "free", "box_uv": False}, "name": self.name, "resolution": {"width": 64, "height": 64}, "elements": self.elements,
                    "outliner": [{"name": "root", "uuid": identity(self.name + "root"), "origin": [0, 0, 0], "children": self.outliner}],
                    "textures": [{"name": self.name + ".png", "id": "0", "source": "data:image/png;base64," + base64.b64encode(self.png).decode()}], "animations": animations}
        source = ROOT / "assets/source/models" / (self.name + ".bbmodel")
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_text(json.dumps(editable, indent=2) + "\n", encoding="utf-8")


def characters():
    for name, swatches in {"hero": [16, 47, 5, 24, 16, 30, 45, 7], "zombie": [10, 10, 9, 24, 16, 30, 45, 7], "skeleton": [31, 31, 28, 31, 27, 20, 45, 7]}.items():
        model = Model(name, "characters", swatches)
        for bone, pivot, center, size, slot in [
            ("head", (0, 1.5, 0), (0, 1.75, 0), (0.5, 0.5, 0.5), 0),
            ("body", (0, 0.75, 0), (0, 1.125, 0), (0.5, 0.75, 0.25), 2),
            ("right_arm", (-0.375, 1.5, 0), (-0.375, 1.125, 0), (0.25, 0.75, 0.25), 2),
            ("left_arm", (0.375, 1.5, 0), (0.375, 1.125, 0), (0.25, 0.75, 0.25), 2),
            ("right_leg", (-0.125, 0.75, 0), (-0.125, 0.375, 0), (0.25, 0.75, 0.25), 3),
            ("left_leg", (0.125, 0.75, 0), (0.125, 0.375, 0), (0.25, 0.75, 0.25), 3)]:
            model.bone(bone, pivot)
            model.cube(bone + "_box", center, size, slot, bone, face_slot=1 if bone == "head" else None)
        if name == "hero":
            model.cube("sword_blade", (-0.375, 0.38, -0.08), (0.10, 0.62, 0.06), 5, "right_arm")
            model.cube("sword_guard", (-0.375, 0.72, -0.08), (0.30, 0.05, 0.09), 6, "right_arm")
            model.cube("sword_grip", (-0.375, 0.82, -0.08), (0.07, 0.16, 0.07), 4, "right_arm")
            model.cube("bow_middle", (-0.375, 0.75, -0.30), (0.07, 0.40, 0.07), 4, "right_arm")
            model.cube("bow_upper", (-0.375, 1.04, -0.20), (0.06, 0.23, 0.08), 4, "right_arm")
            model.cube("bow_lower", (-0.375, 0.46, -0.20), (0.06, 0.23, 0.08), 4, "right_arm")
            model.cube("bow_string", (-0.375, 0.75, -0.10), (0.018, 0.80, 0.018), 7, "right_arm")
        model.animation("idle", "head", [0, 0.5, 1], [0, 1.5, 0], axis=1)
        model.animation("walk", "right_arm", [0, 0.125, 0.25, 0.375, 0.5], [-25, 0, 25, 0, -25])
        # Independent rigid limb channels share the same period.
        walk = model.doc["animations"][-1]
        for bone, angles in (("left_arm", [25, 0, -25, 0, 25]), ("right_leg", [25, 0, -25, 0, 25]), ("left_leg", [-25, 0, 25, 0, -25])):
            temporary = len(model.doc["animations"])
            model.animation("limb", bone, [0, 0.125, 0.25, 0.375, 0.5], angles)
            extra = model.doc["animations"].pop(temporary)
            walk["channels"].append({**extra["channels"][0], "sampler": len(walk["samplers"])})
            walk["samplers"].extend(extra["samplers"])
        model.animation("attack", "right_arm", [0, 0.1, 0.2, 0.4] if name == "hero" else [0, 0.45, 0.55, 1.25], [0, 150, -10, 0])
        model.animation("hit", "body", [0, 0.1, 0.2], [0, -12, 0])
        model.animation("death", "root", [0, 0.5, 1], [0, 85, 85], axis=2)
        model.translation("emerge", "root", [0, 0.5], [(0, -2, 0), (0, 0, 0)])
        if name == "skeleton":
            model.animation("aim", "right_arm", [0, 0.6], [0, 90])
            model.animation("fire", "right_arm", [0, 0.1, 0.2], [90, 70, 90])
        if name == "hero":
            model.animation("roll", "root", [0, 0.1, 0.2, 0.3, 0.4], [0, 90, 180, 270, 360])
            roll = model.doc["animations"][-1]
            # Rotate around the torso, preserving the Core-owned feet transform.
            model.translation("roll_pivot", "root", [0, 0.1, 0.2, 0.3, 0.4], [(0, 1 - math.cos(math.radians(a)), -math.sin(math.radians(a))) for a in (0, 90, 180, 270, 360)])
            pivot = model.doc["animations"].pop()
            roll["channels"].append({**pivot["channels"][0], "sampler": len(roll["samplers"])})
            roll["samplers"].extend(pivot["samplers"])
            model.animation("aim", "right_arm", [0, 0.1], [0, 90])
            model.animation("fire", "right_arm", [0, 0.1, 0.2], [90, 70, 0])
        model.write()


def static_models():
    for variant in range(3):
        tree = Model("tree_oak" + (f"_{variant}" if variant else ""), "tiles", [17, 10, 9, 11, 16, 30, 45, 7])
        height = (0, -0.2, 0.3)[variant]
        tree.cube("trunk", (0, 1.1, 0), (0.4, 2.2, 0.4), 0)
        # Authored forests place a tree in every cell. Keep crowns inside their
        # cell footprint so adjacent instances never have coplanar overlapping tops.
        tree.cube("crown_low", (0, 1.95 + height, 0), (0.98 - variant * 0.03, 0.8, 0.98), 1)
        tree.cube("crown_mid", (0.025 * variant, 2.55 + height, 0), (0.80, 0.7, 0.80 - variant * 0.03), 1)
        tree.cube("crown_top", (0, 3.0 + height, -0.025 * variant), (0.60, 0.4, 0.60), 3)
        tree.write()
    definitions = {
        ("tiles", "rock"): [("base", (0, 0.3, 0), (0.8, 0.6, 0.7), 0), ("top", (-0.1, 0.65, 0), (0.5, 0.2, 0.5), 1)],
        ("tiles", "stone_wall"): [("wall", (0, 0.9, 0), (1, 1.8, 1), 0), ("cap", (0, 1.85, 0), (1, 0.1, 1), 1)],
        ("tiles", "stone_wall_1"): [("wall", (0, 0.85, 0), (1, 1.7, 1), 0), ("cap_w", (-0.3, 1.8, 0), (0.4, 0.2, 1), 1), ("cap_e", (0.3, 1.75, 0), (0.4, 0.1, 1), 1)],
        ("props", "barrel"): [("wood", (0, 0.4, 0), (0.65, 0.8, 0.65), 2), ("band_a", (0, 0.18, 0), (0.7, 0.1, 0.7), 0), ("band_b", (0, 0.62, 0), (0.7, 0.1, 0.7), 0)],
        ("props", "crate"): [("box", (0, 0.35, 0), (0.7, 0.7, 0.7), 2), ("rim", (0, 0.68, 0), (0.75, 0.08, 0.75), 3)],
        ("props", "pillar_broken"): [("base", (0, 0.1, 0), (0.8, 0.2, 0.8), 0), ("pillar", (0, 0.65, 0), (0.55, 0.9, 0.55), 1), ("break", (-0.12, 1.15, 0), (0.3, 0.1, 0.45), 0)],
        ("props", "chest"): [("box", (0, 0.25, 0), (0.8, 0.5, 0.6), 2), ("lid", (0, 0.56, 0), (0.85, 0.15, 0.65), 3), ("latch", (0, 0.4, -0.33), (0.12, 0.2, 0.07), 6)],
        ("props", "checkpoint_banner"): [("foot", (0, 0.1, 0), (0.5, 0.2, 0.5), 0), ("pole", (0, 0.9, 0), (0.1, 1.6, 0.1), 2), ("flag", (0.3, 1.35, 0), (0.6, 0.5, 0.06), 6)],
        ("props", "gate_wood"): [("post_w", (-0.95, 0.9, 0), (0.15, 1.8, 0.25), 2), ("post_e", (0.95, 0.9, 0), (0.15, 1.8, 0.25), 2), ("leaf_w", (-0.45, 0.8, 0), (0.8, 1.5, 0.12), 3), ("leaf_e", (0.45, 0.8, 0), (0.8, 1.5, 0.12), 3)],
        ("props", "arrow"): [("shaft", (0, 0, 0), (0.05, 0.05, 0.6), 2), ("tip", (0, 0, -0.32), (0.12, 0.12, 0.12), 0)],
    }
    for (folder, name), boxes in definitions.items():
        model = Model(name, folder, [27, 30, 19, 21, 16, 29, 45, 7])
        if name == "chest":
            model.bone("lid_hinge", (0, 0.5, 0.3))
        if name == "gate_wood":
            model.bone("hinge_w", (-0.9, 0, 0))
            model.bone("hinge_e", (0.9, 0, 0))
        for label, center, size, slot in boxes:
            bone = "lid_hinge" if name == "chest" and label == "lid" else "hinge_w" if name == "gate_wood" and label == "leaf_w" else "hinge_e" if name == "gate_wood" and label == "leaf_e" else "root"
            model.cube(label, center, size, slot, bone)
        if name == "chest":
            model.animation("open", "lid_hinge", [0, 0.4], [0, 110])
            model.animation("close", "lid_hinge", [0, 0.4], [110, 0])
        if name == "gate_wood":
            for clip, start, end in (("open", 0, 90), ("close", 90, 0)):
                model.animation(clip, "hinge_w", [0, 0.75], [start, end], axis=1)
                model.animation(clip + "_other", "hinge_e", [0, 0.75], [-start, -end], axis=1)
                other = model.doc["animations"].pop()
                primary = model.doc["animations"][-1]
                primary["channels"].append({**other["channels"][0], "sampler": len(primary["samplers"])})
                primary["samplers"].extend(other["samplers"])
        model.write()
    torch = Model("torch", "props", [17, 19, 40, 41, 16, 30, 45, 7])
    torch.cube("stem", (0, 0.65, 0), (0.15, 1.3, 0.15), 0)
    torch.cube("flame", (0, 1.4, 0), (0.25, 0.35, 0.25), 2, emissive=True)
    torch.write()
    beacon = Model("exit_beacon", "props", [27, 30, 43, 41, 16, 30, 45, 7])
    beacon.cube("base", (0, 0.15, 0), (2, 0.3, 2), 0)
    beacon.cube("plinth", (0, 0.4, 0), (1, 0.2, 1), 1)
    beacon.cube("column", (0, 1.6, 0), (0.35, 2.2, 0.35), 2, emissive=True)
    beacon.write()
    emerald = Model("emerald", "props", [43, 42, 14, 10, 16, 30, 45, 7])
    emerald.cube("gem", (0, 0.28, 0), (0.28, 0.38, 0.22), 0)
    emerald.cube("crown", (0, 0.49, 0), (0.16, 0.09, 0.14), 2)
    emerald.write()
    food = Model("food", "props", [44, 38, 17, 21, 16, 30, 45, 7])
    food.cube("fruit", (0, 0.20, 0), (0.32, 0.32, 0.30), 0)
    food.cube("stem", (0, 0.40, 0), (0.05, 0.10, 0.05), 2)
    food.write()
    bundle = Model("arrow_bundle", "props", [27, 30, 19, 21, 16, 30, 45, 7])
    for i in range(3):
        bundle.cube(f"shaft_{i}", ((i - 1) * 0.08, 0.12, 0), (0.04, 0.04, 0.55), 2)
        bundle.cube(f"tip_{i}", ((i - 1) * 0.08, 0.12, -0.30), (0.07, 0.07, 0.10), 0)
    bundle.cube("tie", (0, 0.12, 0), (0.26, 0.10, 0.10), 4)
    bundle.write()


if __name__ == "__main__":
    characters()
    static_models()
    print("Wrote original editable cuboid models, 64x64 sheets and GLB exports")
