"""Check an offline selected split set against a validated universal APK, without installing it."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import zipfile

from audit_android_release import native_libraries


def inventory(apk):
    resources, libraries = {}, {}
    with zipfile.ZipFile(apk) as archive:
        seen = set()
        for info in archive.infolist():
            name = info.filename
            path = PurePosixPath(name)
            if name in seen or path.is_absolute() or ".." in path.parts or "\\" in info.orig_filename or "\0" in info.orig_filename:
                raise ValueError("Duplicate or unsafe APK member")
            seen.add(name)
            if info.is_dir() or not (name.startswith("assets/") or name.endswith(".so")):
                continue
            if not 0 <= info.file_size <= 256 * 1024 * 1024:
                raise ValueError("Oversized APK payload member")
            with archive.open(info) as stream:
                sha = hashlib.file_digest(stream, "sha256").hexdigest()
            (resources if name.startswith("assets/") else libraries)[name] = sha
    return resources, libraries


def identity(text):
    header = re.search(r"^package: name='([^']+)' versionCode='([0-9]+)' versionName='([^']*)'([^\r\n]*)$", text, re.M)
    if not header:
        raise ValueError("Missing aapt package identity")
    split = re.search(r"\bsplit='([^']+)'", header[4])
    return header[1], int(header[2]), header[3], split[1] if split else None


def check(plan, reference, reference_badging, expected_certificate):
    if not isinstance(plan, dict):
        raise ValueError("Expected a split audit plan object")
    package, code, version, split = identity(reference_badging)
    if split is not None or not re.fullmatch(r"[0-9a-f]{64}", expected_certificate):
        raise ValueError("Invalid universal reference/signing identity")
    reference_assets, reference_libraries = inventory(reference)
    if not reference_assets or not reference_libraries:
        raise ValueError("Universal reference lacks game assets/native libraries")
    files, spec = plan.get("files"), plan.get("deviceSpec")
    if not isinstance(files, list) or not 3 <= len(files) <= 64 or not isinstance(spec, dict) or spec.get("supportedAbis") != ["arm64-v8a"] or type(spec.get("sdkVersion")) is not int or spec["sdkVersion"] < 26:
        raise ValueError("Expected a bounded arm64 split set and supported synthetic SDK")
    assets, libraries, names, paths, records = {}, {}, set(), set(), []
    base, asset_holder = None, None
    total_bytes = 0
    for item in files:
        if not isinstance(item, dict) or item.get("zipalign16Passed") is not True:
            raise ValueError("Each APK needs actual successful independent zipalign")
        apk = Path(item["apk"])
        if not apk.is_file() or apk.is_symlink() or apk.resolve() in paths:
            raise ValueError("Missing/duplicate/linked split APK")
        paths.add(apk.resolve())
        badging = Path(item["badging"]).read_text(encoding="utf-8-sig")
        signature = Path(item["signature"]).read_text(encoding="utf-8-sig")
        apk_package, apk_code, apk_version, apk_split = identity(badging)
        if apk_package != package or apk_code != code or apk_version not in ("", version) or apk_split in names:
            raise ValueError("Split package/version identity differs or split is duplicated")
        names.add(apk_split)
        fingerprints = re.findall(r"^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})\s*$", signature, re.M)
        if not re.search(r"^Verifies\s*$", signature, re.M) or [value.lower() for value in fingerprints] != [expected_certificate]:
            raise ValueError("Every selected split must verify under the expected APK signer")
        if apk_split is None:
            base = str(apk.resolve())
            minimum = re.search(r"^sdkVersion:'([0-9]+)'\s*$", badging, re.M)
            target = re.search(r"^targetSdkVersion:'([0-9]+)'\s*$", badging, re.M)
            if not minimum or int(minimum[1]) != 26 or not target or int(target[1]) < 36:
                raise ValueError("Base split fails minimum/target SDK requirements")
        apk_assets, apk_libraries = inventory(apk)
        if set(assets).intersection(apk_assets) or set(libraries).intersection(apk_libraries):
            raise ValueError("Duplicate game/native payload across selected splits")
        assets.update(apk_assets)
        libraries.update(apk_libraries)
        if apk_assets:
            if asset_holder is not None or apk_split != "assetPackInstallTime":
                raise ValueError("Expected one install-time game asset pack")
            asset_holder = str(apk.resolve())
        native = native_libraries(apk) if apk_libraries else []
        if any(not entry["elfCompatible16Kb"] or not entry["zipCompatible16Kb"] for entry in native):
            raise ValueError("A selected native library fails ELF/ZIP 16 KB prerequisites")
        total_bytes += apk.stat().st_size
        with apk.open("rb") as stream:
            sha = hashlib.file_digest(stream, "sha256").hexdigest()
        records.append({"apk": str(apk.resolve()), "split": apk_split, "sha256": sha, "nativeLibraries": native})
    if base is None or asset_holder is None or "config.arm64_v8a" not in names:
        raise ValueError("Selected set lacks base, arm64 or install-time assets")
    for language in spec.get("supportedLocales", []):
        if language not in ("en", "pl") or "config." + language not in names:
            raise ValueError("Requested language split is absent")
    density = {160: "mdpi", 240: "hdpi", 320: "xhdpi", 480: "xxhdpi", 640: "xxxhdpi"}.get(spec.get("screenDensity"))
    if density is None or "config." + density not in names:
        raise ValueError("Requested density split is absent")
    if assets != reference_assets or libraries != reference_libraries:
        raise ValueError("Selected split assets/native bytes differ from the validated universal APK")
    if total_bytes > 150 * 1024 * 1024:
        raise ValueError("Aggregate selected APK size exceeds the recorded budget")
    return {"passed": True, "offlineSelectionOnly": True, "physicalAcceptance": False, "releaseAcceptance": False, "storeSubmissionReady": False,
            "package": package, "versionCode": code, "versionName": version, "baseApk": base, "assetApk": asset_holder,
            "assetEntries": len(assets), "nativeLibraryCount": len(libraries), "aggregateBytes": total_bytes, "apkSignaturesMatched": True,
            "matchesUniversalPayloadBytes": True, "deviceSpec": spec, "files": records}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plan", type=Path)
    parser.add_argument("--reference-apk", type=Path, required=True)
    parser.add_argument("--reference-badging", type=Path, required=True)
    parser.add_argument("--certificate", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Split report output must be new")
    try:
        result = check(json.loads(args.plan.read_text(encoding="utf-8-sig")), args.reference_apk, args.reference_badging.read_text(encoding="utf-8-sig"), args.certificate.lower())
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        result = {"passed": False, "physicalAcceptance": False, "releaseAcceptance": False, "storeSubmissionReady": False, "error": str(error)}
    with args.output.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(result, output, indent=2)
        output.write("\n")
    print("ANDROID_SPLIT_SET=" + ("PASS" if result["passed"] else "FAIL"))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
