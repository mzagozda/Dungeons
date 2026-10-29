"""Package a self-contained Godot Linux export as a Debian/Ubuntu amd64 installer."""
import argparse
import json
from pathlib import Path
import shutil
import struct
import subprocess

from release_assets import read_identity, sha256

DEPENDENCIES = (
    "libc6 (>= 2.34), libstdc++6, libgcc-s1, zlib1g, ca-certificates, tzdata, "
    "libgssapi-krb5-2, libbrotli1, libicu78 | libicu76 | libicu74 | libicu72, "
    "libssl3t64 | libssl3, libfontconfig1, libx11-6, libxcursor1, libxinerama1, "
    "libxrandr2, libxi6, libasound2t64 | libasound2, libgl1, libvulkan1"
)


def stage_package(root, identity, export, stage):
    identity = read_identity(identity)
    runtime = export / "data_Dungeons.Game_linuxbsd_x86_64"
    executable = export / "BlockDungeons.x86_64"
    required = [executable, runtime / "Dungeons.Game.dll", runtime / "libcoreclr.so"]
    if any(not path.is_file() or path.is_symlink() for path in required):
        raise ValueError("Incomplete self-contained Linux export")
    with executable.open("rb") as source:
        header = source.read(20)
    if len(header) != 20 or header[:7] != b"\x7fELF\x02\x01\x01" or struct.unpack_from("<H", header, 18)[0] != 62:
        raise ValueError("Expected an ELF64 x86-64 Linux executable")
    entries = [executable, runtime] + list(runtime.rglob("*"))
    if any(path.is_symlink() for path in entries):
        raise ValueError("Linked Linux export inputs are forbidden")
    if stage.exists():
        raise ValueError("Package staging directory must be new")
    metadata = json.loads((root / "platform/store/en-US.json").read_text(encoding="utf-8-sig"))
    publisher = metadata["publisher"]
    if metadata["release"]["versionName"] != identity["versionName"]:
        raise ValueError("Linux package version differs from publisher metadata")
    maintainer = f"{publisher['name']} <{publisher['supportEmail']}>"
    if any(character in maintainer + publisher["supportUrl"] for character in "\r\n"):
        raise ValueError("Invalid Linux package metadata")
    payload = stage / "opt/block-dungeons"
    payload.mkdir(parents=True)
    shutil.copyfile(executable, payload / executable.name)
    shutil.copytree(runtime, payload / runtime.name, ignore=shutil.ignore_patterns("*.pdb", "*.log", "*.trx", "*.editor-audit.json"))
    (payload / executable.name).chmod(0o755)
    (payload / "build-info.json").write_text(json.dumps(identity, indent=2) + "\n", encoding="utf-8")
    launcher = stage / "usr/bin/block-dungeons"
    launcher.parent.mkdir(parents=True)
    launcher.write_text('#!/bin/sh\nexec /opt/block-dungeons/BlockDungeons.x86_64 "$@"\n', encoding="utf-8", newline="\n")
    launcher.chmod(0o755)
    desktop = stage / "usr/share/applications/block-dungeons.desktop"
    desktop.parent.mkdir(parents=True)
    desktop.write_text("[Desktop Entry]\nType=Application\nName=Block Dungeons\nExec=block-dungeons\n"
                       "Icon=block-dungeons\nTerminal=false\nCategories=Game;RolePlaying;\n", encoding="utf-8")
    icon = stage / "usr/share/icons/hicolor/scalable/apps/block-dungeons.svg"
    icon.parent.mkdir(parents=True)
    shutil.copyfile(root / "game/assets/ui/probe_icon.svg", icon)
    copyright_file = stage / "usr/share/doc/block-dungeons/copyright"
    copyright_file.parent.mkdir(parents=True)
    shutil.copyfile(root / "THIRD_PARTY_NOTICES.md", copyright_file)
    version = f"{identity['versionName']}-{identity['runNumber']}.{identity['runAttempt']}"
    control = stage / "DEBIAN/control"
    control.parent.mkdir()
    size = sum(path.stat().st_size for path in stage.rglob("*") if path.is_file())
    control.write_text(f"Package: block-dungeons\nVersion: {version}\nArchitecture: amd64\n"
                       f"Maintainer: {maintainer}\nSection: games\nPriority: optional\n"
                       f"Depends: {DEPENDENCIES}\nInstalled-Size: {(size + 1023) // 1024}\n"
                       f"Homepage: {publisher['supportUrl']}\nDescription: Block Dungeons action adventure\n"
                       " Explore Mossy Hollow, battle monsters and find your way to the beacon.\n"
                       " Includes the self-contained .NET runtime and desktop level editor.\n", encoding="utf-8")
    for directory in [stage] + [path for path in stage.rglob("*") if path.is_dir()]:
        directory.chmod(0o755)
    for path in stage.rglob("*"):
        if path.is_file() and path not in (launcher, payload / executable.name):
            path.chmod(0o644)
    return version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--identity", type=Path, required=True)
    parser.add_argument("--export", type=Path, required=True)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    identity = read_identity(args.identity)
    if args.output.exists():
        parser.error("Linux output must be new")
    version = stage_package(Path(__file__).resolve().parents[1], identity, args.export, args.stage)
    args.output.mkdir(parents=True)
    package = args.output / (identity["stem"] + "-linux-amd64.deb")
    subprocess.run(["dpkg-deb", "--root-owner-group", "--build", str(args.stage), str(package)], check=True)
    subprocess.run(["dpkg-deb", "--info", str(package)], check=True)
    info = {"passed": True, "identity": identity, "debianVersion": version, "architecture": "amd64", "physicalAcceptance": False}
    (args.output / "build-info.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8")
    (args.output / "SHA256SUMS.txt").write_text("".join(f"{sha256(path)}  {path.name}\n" for path in sorted(args.output.iterdir())), encoding="utf-8")


if __name__ == "__main__":
    main()
