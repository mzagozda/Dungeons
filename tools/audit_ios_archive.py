"""Read-only iOS archive metadata/Mach-O prerequisite audit; never signing/device acceptance."""
import argparse
import hashlib
import json
import mmap
from pathlib import Path
import plistlib
import re
import struct
from xml.parsers.expat import ExpatError

ARM64 = 0x0100000C
IOS = 2
MINIMUM = 14 << 16
SDK = 26 << 16
MAGICS = {b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf", b"\xbe\xba\xfe\xca", b"\xbf\xba\xfe\xca"}
ALL_MAGICS = MAGICS | {b"\xfe\xed\xfa\xcf", b"\xfe\xed\xfa\xce", b"\xce\xfa\xed\xfe"}


def version(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d{1,5}(?:\.\d{1,3}){0,2}", value):
        raise ValueError("Invalid deployment/SDK version")
    parts = [int(item) for item in value.split(".")]
    parts += [0] * (3 - len(parts))
    if parts[0] > 65535 or max(parts[1:]) > 255:
        raise ValueError("Invalid deployment/SDK version range")
    return parts[0] << 16 | parts[1] << 8 | parts[2]


def version_text(value):
    return f"{value >> 16}.{(value >> 8) & 255}.{value & 255}"


def thin(data, offset, size):
    if size < 32 or offset < 0 or offset + size > len(data) or data[offset:offset + 4] != b"\xcf\xfa\xed\xfe":
        raise ValueError("Missing/truncated little-endian 64-bit Mach-O")
    _, cpu, subtype, kind, count, command_bytes, _, _ = struct.unpack_from("<8I", data, offset)
    if cpu != ARM64 or kind not in (2, 6):
        raise ValueError("Archive contains a non-arm64 executable/dylib")
    if count > 65535 or command_bytes > min(size - 32, 32 * 1024 * 1024) or count > command_bytes // 8:
        raise ValueError("Invalid Mach-O load-command bounds")
    position, end = offset + 32, offset + 32 + command_bytes
    deployment = []
    for _ in range(count):
        if position + 8 > end:
            raise ValueError("Truncated Mach-O load command")
        command, length = struct.unpack_from("<2I", data, position)
        if length < 8 or length % 8 or position + length > end:
            raise ValueError("Invalid Mach-O load-command size")
        if command == 0x32:
            if length < 24:
                raise ValueError("Truncated LC_BUILD_VERSION")
            platform, minimum, sdk, tools = struct.unpack_from("<4I", data, position + 8)
            if length != 24 + tools * 8:
                raise ValueError("Invalid LC_BUILD_VERSION tools")
            deployment.append((platform, minimum, sdk))
        elif command == 0x25:
            if length != 16:
                raise ValueError("Invalid LC_VERSION_MIN_IPHONEOS")
            minimum, sdk = struct.unpack_from("<2I", data, position + 8)
            deployment.append((IOS, minimum, sdk))
        position += length
    if position != end or len(deployment) != 1:
        raise ValueError("Missing/ambiguous deployment command or trailing command bytes")
    platform, minimum, sdk = deployment[0]
    if platform != IOS or minimum == 0 or minimum > MINIMUM:
        raise ValueError(f"Requires iOS device minimum <=14.0; platform={platform}, minimum={version_text(minimum)}")
    return {"cpu": cpu, "subtype": subtype, "fileType": kind, "minimum": version_text(minimum), "sdk": version_text(sdk)}


def macho(data):
    magic = data[:4]
    if magic == b"\xcf\xfa\xed\xfe":
        return [thin(data, 0, len(data))]
    if magic not in MAGICS or len(data) < 8:
        raise ValueError("Unsupported/truncated Mach-O container")
    endian = ">" if magic[:2] == b"\xca\xfe" else "<"
    wide = magic in (b"\xca\xfe\xba\xbf", b"\xbf\xba\xfe\xca")
    count = struct.unpack_from(endian + "I", data, 4)[0]
    stride = 32 if wide else 20
    table_end = 8 + count * stride
    if not 1 <= count <= 16 or table_end > len(data):
        raise ValueError("Invalid fat architecture table")
    records, ranges, architectures = [], [], set()
    for index in range(count):
        fields = struct.unpack_from(endian + ("IIQQII" if wide else "5I"), data, 8 + index * stride)
        cpu, subtype, offset, size, alignment = fields[:5]
        if (cpu, subtype) in architectures:
            raise ValueError("Duplicate fat architecture")
        architectures.add((cpu, subtype))
        if (wide and fields[5] != 0) or alignment > 30 or offset % (1 << alignment) or offset < table_end or offset + size > len(data):
            raise ValueError("Invalid fat slice bounds/alignment")
        if any(offset < end and start < offset + size for start, end in ranges):
            raise ValueError("Overlapping fat slices")
        record = thin(data, offset, size)
        if record["cpu"] != cpu or record["subtype"] != subtype:
            raise ValueError("Fat architecture disagrees with Mach-O header")
        records.append(record)
        ranges.append((offset, offset + size))
    return records


def read_plist(path):
    with path.open("rb") as stream:
        value = plistlib.load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"Expected plist dictionary: {path.name}")
    return value


def executable(bundle, info):
    name = info.get("CFBundleExecutable")
    if not isinstance(name, str) or not name or name in (".", "..") or "/" in name or "\\" in name:
        raise ValueError("Invalid CFBundleExecutable")
    result = bundle / name
    if not result.is_file():
        raise ValueError(f"Missing bundle executable: {name}")
    return result.resolve()


def audit(archive, bundle_id):
    root = archive.resolve(strict=True)
    archive_info = read_plist(root / "Info.plist")
    apps = list((root / "Products/Applications").glob("*.app"))
    if len(apps) != 1 or not apps[0].is_dir() or apps[0].is_symlink():
        raise ValueError("Expected one actual application in the archive")
    app = apps[0].resolve()
    if not app.is_relative_to(root):
        raise ValueError("Application path escapes its archive")
    properties = archive_info.get("ApplicationProperties", {})
    if not isinstance(properties, dict) or properties.get("ApplicationPath") != f"Applications/{app.name}" or properties.get("CFBundleIdentifier") != bundle_id:
        raise ValueError("Archive/application bundle identity differs")
    return audit_application(app, bundle_id)


def audit_application(app, bundle_id):
    if not app.is_dir() or app.is_symlink():
        raise ValueError("Expected one actual application directory")
    app = app.resolve(strict=True)
    for path in app.rglob("*"):
        if path.is_symlink() and not path.resolve(strict=True).is_relative_to(app):
            raise ValueError("Application symlink escapes its bundle")
    app_info = read_plist(app / "Info.plist")
    if app_info.get("CFBundleIdentifier") != bundle_id:
        raise ValueError("Archive/application bundle identity differs")
    if version(app_info.get("MinimumOSVersion")) != MINIMUM:
        raise ValueError("App must retain the specified iOS 14.0 deployment target")
    sdk_name = app_info.get("DTSDKName", "")
    if app_info.get("DTPlatformName") != "iphoneos" or not isinstance(sdk_name, str) or not sdk_name.startswith("iphoneos") or version(sdk_name[8:]) < SDK:
        raise ValueError("App metadata requires the iOS 26+ device SDK")
    if any(app_info.get(key) is not True for key in ("UIFileSharingEnabled", "LSSupportsOpeningDocumentsInPlace")):
        raise ValueError("Both native Documents/Files sharing keys must be true")
    families = app_info.get("UIDeviceFamily", [])
    if not isinstance(families, list) or any(type(item) is not int for item in families) or set(families) != {1, 2}:
        raise ValueError("App must support both iPhone and iPad families")
    orientations = {"UIInterfaceOrientationLandscapeLeft", "UIInterfaceOrientationLandscapeRight"}
    for key in ("UISupportedInterfaceOrientations", "UISupportedInterfaceOrientations~ipad"):
        values = app_info.get(key, app_info.get("UISupportedInterfaceOrientations"))
        if not isinstance(values, list) or set(values) != orientations or len(values) != 2:
            raise ValueError("App must retain both landscape orientations on iPhone/iPad")
    required = {executable(app, app_info): 2}
    framework_records = []
    for framework in sorted(app.rglob("*.framework")):
        info = read_plist(framework / "Info.plist")
        if not 0 < version(info.get("MinimumOSVersion")) <= MINIMUM:
            raise ValueError(f"Framework metadata has an incompatible iOS deployment minimum: {framework.name}")
        required[executable(framework, info)] = 6
        framework_records.append({"path": framework.relative_to(app).as_posix(), "minimum": info["MinimumOSVersion"]})
    binaries = []
    observed = set()
    for path in sorted(app.rglob("*")):
        if path.is_symlink() and not path.resolve(strict=True).is_relative_to(app):
            raise ValueError("Application symlink escapes its bundle")
        if not path.is_file():
            continue
        with path.open("rb") as stream:
            magic = stream.read(4)
            if magic not in ALL_MAGICS:
                if path.suffix == ".dylib":
                    raise ValueError("Embedded dylib is not a supported Mach-O")
                continue
            with mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
                records = macho(data)
            stream.seek(0)
            sha = hashlib.file_digest(stream, "sha256").hexdigest()
        actual = path.resolve()
        if actual in required and any(record["fileType"] != required[actual] for record in records):
            raise ValueError("Bundle executable Mach-O file type differs")
        if actual == executable(app, app_info) and any(version(record["sdk"]) < SDK for record in records):
            raise ValueError("Main executable was not linked with iOS SDK 26+")
        observed.add(actual)
        binaries.append({"path": path.relative_to(app).as_posix(), "sha256": sha, "slices": records})
    if not required.keys() <= observed:
        raise ValueError("A required bundle executable is not a supported Mach-O")
    return {"application": app.name, "bundleIdentifier": bundle_id, "minimumIos": "14.0", "frameworks": framework_records, "binaries": binaries}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--application", action="store_true", help="Inspect an actual extracted IPA .app instead of an xcarchive")
    parser.add_argument("--bundle-id", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists() or args.output.resolve().is_relative_to(args.archive.resolve()):
        parser.error("Output must be new and outside the input archive")
    result = {"passed": False, "scope": "archive metadata and native deployment prerequisites", "signingAcceptance": False, "physicalAcceptance": False, "storeSubmissionReady": False}
    try:
        result.update(audit_application(args.archive, args.bundle_id) if args.application else audit(args.archive, args.bundle_id))
        result["passed"] = True
    except (OSError, ValueError, struct.error, plistlib.InvalidFileException, OverflowError, ExpatError) as error:
        result["error"] = str(error)
    with args.output.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, indent=2)
        stream.write("\n")
    print("IOS_ARCHIVE_PREREQUISITES=" + ("PASS" if result["passed"] else "FAIL"))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
