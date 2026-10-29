"""Verify the exact audited ETC2 bytes/remaps in an actual APK; optionally expose its resources for Godot QA."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def audit(apk: Path, profile: Path):
    manifest = json.loads((profile / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("nativeAuditPassed") is not True or manifest.get("codec") != "ETC2" or not manifest.get("textures"):
        raise ValueError("Native audited mobile profile is required")
    with zipfile.ZipFile(apk) as package:
        names = package.namelist()
        if len(set(names)) != len(names):
            raise ValueError("Duplicate APK entries")
        checked = []
        for record in manifest["textures"]:
            relative = record["source"].removeprefix("res://")
            metadata = package.read("assets/" + relative + ".import")
            data = package.read("assets/" + record["mobileDataFile"])
            if hashlib.sha256(metadata).hexdigest() != record["mobileImportSha256"] or hashlib.sha256(data).hexdigest() != record["mobileDataSha256"]:
                raise ValueError("Packaged native texture/import bytes differ: " + relative)
            if ('path="' + record["mobilePath"] + '"').encode() not in metadata or b"path.s3tc=" in metadata or b"path.astc=" in metadata:
                raise ValueError("Mobile texture must resolve only to its packaged ETC2 variant")
            source_stem = Path(record["mobileDataFile"]).name.removesuffix(".etc2.ctex")
            for desktop_suffix in (".ctex", ".s3tc.ctex", ".bptc.ctex"):
                if "assets/.godot/imported/" + source_stem + desktop_suffix in names:
                    raise ValueError("Desktop texture data leaked into the mobile package: " + relative)
            checked.append(dict(source=record["source"], mobilePath=record["mobilePath"], mipmaps=record["mipmaps"], mipmapCount=record["mipmapCount"], imageFormat=record["imageFormat"], gpuBytes=record["gpuBytes"], dataSha256=record["mobileDataSha256"]))
    return dict(passed=True, codec="ETC2", textures=checked, textureCount=len(checked), gpuBytes=sum(item["gpuBytes"] for item in checked), physicalAcceptance=False)


def asset_pack(apk: Path, output: Path):
    with zipfile.ZipFile(apk) as package, output.open("xb") as stream, zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
        for item in package.infolist():
            if item.filename.startswith("assets/") and not item.is_dir():
                relative = item.filename.removeprefix("assets/")
                if relative == "project.binary" or relative.startswith(".godot/mono/"):
                    continue
                if ".." in relative or "\\" in relative or relative.startswith("/"):
                    raise ValueError("Unsafe packaged resource path")
                archive.writestr(relative, package.read(item))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("profile", type=Path)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--asset-pack", type=Path)
    args = parser.parse_args()
    try:
        if args.report.exists() or args.asset_pack and args.asset_pack.exists():
            raise ValueError("Package audit outputs must be new files")
        result = audit(args.apk, args.profile)
        if args.asset_pack:
            asset_pack(args.apk, args.asset_pack)
        with args.report.open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(result, indent=2) + "\n")
        print("MOBILE_TEXTURE_PACKAGE=PASS count=" + str(result["textureCount"]) + " gpuBytes=" + str(result["gpuBytes"]))
        return 0
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        print("MOBILE_TEXTURE_PACKAGE=FAIL " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
