"""Build native ETC2 import variants in a new asset-only Godot project."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import shutil

from PIL import Image
from texture_imports import ROOT, expected, mipmaps, rewrite, sha, textures, validate


def prepare(project: Path):
    if project.resolve().is_relative_to((ROOT / "game").resolve()):
        raise ValueError("Mobile import project must be outside the desktop game")
    records = []
    for source in textures(ROOT):
        relative = source.relative_to(ROOT / "game").as_posix()
        sidecar = source.with_suffix(".png.import")
        settings = sidecar.read_text(encoding="utf-8")
        validate(settings, relative)
        with Image.open(source) as pixels:
            width, height = pixels.size
        records.append(dict(source="res://" + relative, sourceSha256=sha(source), desktopImportSha256=sha(sidecar),
                            width=width, height=height, mipmaps=mipmaps(relative)))
    if not records:
        raise ValueError("No game textures found")
    project.mkdir(parents=True, exist_ok=False)
    (project / "project.godot").write_text('config_version=5\n\n[application]\nconfig/name="Block Dungeons isolated mobile textures"\n\n[rendering]\nrenderer/rendering_method="mobile"\ntextures/vram_compression/import_s3tc_bptc=false\ntextures/vram_compression/import_etc2_astc=true\n', encoding="utf-8")
    for record in records:
        relative = record["source"].removeprefix("res://")
        source, destination = ROOT / "game" / relative, project / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        if sha(destination) != record["sourceSha256"]:
            raise ValueError("Texture changed while copying into the isolated import project")
        original = source.with_suffix(".png.import").read_text(encoding="utf-8")
        destination.with_suffix(".png.import").write_text(rewrite(original, expected(relative, mobile=True)), encoding="utf-8")
    manifest = dict(formatVersion=1, codec="ETC2", sourceSettingsPreserved=True, textures=records)
    (project / "source-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def finish(project: Path):
    output = project / "manifest.json"
    if output.exists():
        raise FileExistsError("Native variant manifest already exists")
    manifest = json.loads((project / "source-manifest.json").read_text(encoding="utf-8"))
    audit = json.loads((project / "native-audit.json").read_text(encoding="utf-8"))
    if audit.get("passed") is not True or len(audit.get("textures", [])) != len(manifest["textures"]):
        raise ValueError("Native ETC2 audit did not pass every texture")
    actual = {record["source"]: record for record in audit["textures"]}
    if len(actual) != len(manifest["textures"]) or set(actual) != {record["source"] for record in manifest["textures"]}:
        raise ValueError("Native texture coverage differs")
    for record in manifest["textures"]:
        relative = record["source"].removeprefix("res://")
        source = ROOT / "game" / relative
        if sha(source) != record["sourceSha256"] or sha(source.with_suffix(".png.import")) != record["desktopImportSha256"]:
            raise ValueError("Desktop texture inputs changed during native variant creation")
        native = actual[record["source"]]
        if any(native[key] != record[key] for key in ("width", "height", "mipmaps")) or native.get("family") != "ETC2":
            raise ValueError("Native dimensions/mipmaps/format differ: " + relative)
        target = native["mobilePath"]
        if not target.startswith("res://.godot/imported/") or not target.endswith(".etc2.ctex") or ".." in target or "\\" in target:
            raise ValueError("Unsafe native mobile path")
        data_file = project / target.removeprefix("res://")
        uid = re.findall(r'^uid="(uid://[a-z0-9]+)"$', source.with_suffix(".png.import").read_text(encoding="utf-8"), re.MULTILINE)
        if len(uid) != 1:
            raise ValueError("Texture UID is unavailable: " + relative)
        # The host also imports S3TC for its own editor. Export one unconditional
        # ETC2 remap so a mobile driver never selects an unpackaged desktop variant.
        exported_import = "export_imports/" + relative + ".import"
        exported_path = project / exported_import
        exported_path.parent.mkdir(parents=True, exist_ok=True)
        exported_path.write_text('[remap]\n\nimporter="texture"\ntype="CompressedTexture2D"\nuid="' + uid[0] + '"\npath="' + target + '"\nmetadata={"vram_texture": true}\n', encoding="utf-8")
        record.update(native)
        record.update(mobileImportFile=exported_import, mobileImportSha256=sha(exported_path),
                      mobileDataFile=target.removeprefix("res://"), mobileDataSha256=sha(data_file))
    manifest.update(godot=audit["godot"], nativeAuditPassed=True, physicalAcceptance=False)
    with output.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(manifest, indent=2) + "\n")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("prepare", "finish"))
    parser.add_argument("project", type=Path)
    args = parser.parse_args()
    try:
        result = prepare(args.project) if args.command == "prepare" else finish(args.project)
        print(json.dumps(dict(passed=True, stage=args.command, textures=len(result["textures"]), codec="ETC2", physicalAcceptance=False)))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("MOBILE_TEXTURE_VARIANTS=FAIL " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
