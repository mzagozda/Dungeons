"""Validate build identity and package checksums before preparing public downloads."""
import hashlib
import json
from pathlib import Path
import re

PACKAGES = {
    "windows-x64": ("-windows-x64-setup.exe", "-windows-x64-portable.zip"),
    "linux-amd64": ("-linux-amd64.deb",),
    "android-arm64": ("-android-arm64.apk", "-android-arm64.aab"),
}


def read_identity(value):
    if isinstance(value, Path):
        value = json.loads(value.read_text(encoding="utf-8-sig"))
    if (value.get("formatVersion") != 1 or not re.fullmatch(r"\d+\.\d+\.\d+", value.get("versionName", ""))
            or not re.fullmatch(r"[0-9a-f]{40}", value.get("commit", ""))
            or any(type(value.get(key)) is not int or value[key] < 1 for key in ("runNumber", "runAttempt", "androidVersionCode"))
            or value["androidVersionCode"] > 2_100_000_000):
        raise ValueError("Invalid release identity")
    stem = f"BlockDungeons-{value['versionName']}-build{value['runNumber']}.{value['runAttempt']}-{value['commit'][:12]}"
    if value.get("stem") != stem:
        raise ValueError("Release filename identity differs")
    return value


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_group(directory, platform, commit, run_number):
    if directory.is_symlink() or not directory.is_dir():
        raise ValueError("Expected a regular package directory")
    files = list(directory.iterdir())
    if any(path.is_symlink() or not path.is_file() for path in files):
        raise ValueError("Package artifacts must contain regular files only")
    info = json.loads((directory / "build-info.json").read_text(encoding="utf-8-sig"))
    identity = read_identity(info["identity"])
    if identity["commit"] != commit or identity["runNumber"] != run_number or info.get("passed") is not True:
        raise ValueError("Package did not pass for this exact commit/run")
    if directory.name != identity["stem"] + "-" + platform:
        raise ValueError("Artifact group name differs from its identity")
    expected = {identity["stem"] + suffix for suffix in PACKAGES[platform]} | {"build-info.json"}
    if {path.name for path in files} != expected | {"SHA256SUMS.txt"}:
        raise ValueError("Unexpected or missing distribution files")
    checksums = {}
    for line in (directory / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9._-]+)", line)
        if not match or match[2] in checksums:
            raise ValueError("Invalid or duplicate package checksum")
        checksums[match[2]] = match[1]
    if set(checksums) != expected or any(sha256(directory / name) != digest for name, digest in checksums.items()):
        raise ValueError("Package checksum mismatch")
    if platform == "android-arm64" and info.get("privateFilesRemoved") is not True:
        raise ValueError("Android private signing cleanup was not verified")
    return identity, info
