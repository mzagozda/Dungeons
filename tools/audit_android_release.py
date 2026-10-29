"""Inspect APK release prerequisites; this does not approve an AAB or a store submission."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import struct
import zipfile

PAGE = 16384
MAX_LIBRARY_BYTES = 256 * 1024 * 1024
SIGNER_LABEL = r"(?:Signer #\d+|V\d+\.\d+ Signer:)"


def signer_fingerprints(signature: str) -> list[str]:
    # Keep every signer record: duplicate fingerprints must not bypass count checks.
    return [value.lower() for value in re.findall(
        rf"^{SIGNER_LABEL} certificate SHA-256 digest: ([0-9a-fA-F]{{64}})\s*$", signature, re.M
    )]


def elf_loads(data: bytes) -> list[dict]:
    """Read ELF64/AArch64 PT_LOAD records without loading native code."""
    if len(data) < 64 or data[:7] != b"\x7fELF\x02\x01\x01":
        raise ValueError("Expected little-endian ELF64 version 1")
    kind, machine, version = struct.unpack_from("<HHI", data, 16)
    if (kind, machine, version) != (3, 183, 1):
        raise ValueError("Expected an AArch64 shared object")
    offset = struct.unpack_from("<Q", data, 32)[0]
    header_size, entry_size, count = struct.unpack_from("<HHH", data, 52)
    if header_size != 64 or entry_size != 56 or not 0 < count < 65535 or offset < 64 or offset + count * entry_size > len(data):
        raise ValueError("Invalid or unsupported ELF program-header table")
    loads = []
    for index in range(count):
        segment, _, file_offset, address, _, file_bytes, memory_bytes, alignment = struct.unpack_from("<IIQQQQQQ", data, offset + index * entry_size)
        if segment != 1:
            continue
        if file_bytes > memory_bytes or file_offset + file_bytes > len(data):
            raise ValueError("Invalid ELF LOAD extent")
        compatible = alignment >= PAGE and alignment & (alignment - 1) == 0 and file_offset % alignment == address % alignment
        loads.append({"offset": file_offset, "virtualAddress": address, "alignment": alignment, "compatible16Kb": compatible})
    if not loads:
        raise ValueError("ELF has no LOAD segments")
    return loads


def native_libraries(apk: Path) -> list[dict]:
    libraries = []
    with zipfile.ZipFile(apk) as archive, apk.open("rb") as raw:
        names = set()
        for info in archive.infolist():
            if "\\" in info.orig_filename or "\x00" in info.orig_filename:
                raise ValueError("Unsafe ZIP member: " + info.orig_filename)
            if info.filename in names:
                raise ValueError("Duplicate ZIP member: " + info.filename)
            names.add(info.filename)
            path = PurePosixPath(info.filename)
            if path.is_absolute() or ".." in path.parts or "\\" in info.filename:
                raise ValueError("Unsafe ZIP member: " + info.filename)
            if not info.filename.endswith(".so"):
                continue
            if len(path.parts) != 3 or path.parts[:2] != ("lib", "arm64-v8a"):
                raise ValueError("Unexpected native library ABI/path: " + info.filename)
            if not 0 < info.file_size <= MAX_LIBRARY_BYTES:
                raise ValueError("Invalid native library size: " + info.filename)
            raw.seek(info.header_offset)
            local = raw.read(30)
            if len(local) != 30 or local[:4] != b"PK\x03\x04":
                raise ValueError("Invalid ZIP local header")
            name_bytes, extra_bytes = struct.unpack_from("<HH", local, 26)
            data_offset = info.header_offset + 30 + name_bytes + extra_bytes
            loads = elf_loads(archive.read(info))
            zip_compatible = info.compress_type == zipfile.ZIP_DEFLATED or info.compress_type == zipfile.ZIP_STORED and data_offset % PAGE == 0
            libraries.append({"path": info.filename, "bytes": info.file_size, "compression": info.compress_type,
                              "zipDataOffset": data_offset, "zipCompatible16Kb": zip_compatible,
                              "elfCompatible16Kb": all(load["compatible16Kb"] for load in loads), "loads": loads})
    if not libraries:
        raise ValueError("APK has no arm64 native libraries")
    return libraries


def report(apk: Path, badging: str, signature: str, required_target: int = 36, release_certificate_sha256: str | None = None) -> dict:
    with apk.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    result = {"formatVersion": 1, "apk": str(apk.resolve()), "apkSha256": digest,
              "requiredTargetSdk": required_target, "policyCheckedDate": "2026-10-27", "passed": False,
              "storeSubmissionReady": False, "aabAcceptance": False, "physical16KbAcceptance": False,
              "limitations": ["APK prerequisite audit only; signed AAB/bundletool validation, store metadata and physical 16 KB execution remain required."]}
    minimum = re.search(r"^sdkVersion:'(\d+)'\s*$", badging, re.M)
    target = re.search(r"^targetSdkVersion:'(\d+)'\s*$", badging, re.M)
    result["minSdk"] = int(minimum[1]) if minimum else None
    result["targetSdk"] = int(target[1]) if target else None
    result["debuggable"] = bool(re.search(r"^application-debuggable(?:\s|$)", badging, re.M))
    signers = re.findall(rf"^{SIGNER_LABEL} certificate DN: (.+)$", signature, re.M)
    verified = bool(re.search(r"^Verifies\s*$", signature, re.M))
    result["verifiedSignature"] = verified
    result["certificateSha256"] = signer_fingerprints(signature)
    expected = release_certificate_sha256.lower() if release_certificate_sha256 else None
    result["expectedReleaseCertificateSha256"] = expected
    result["releaseCertificate"] = verified and len(signers) == 1 and expected is not None and result["certificateSha256"] == [expected] and not re.search(r"(?:^|[,\s])CN\s*=\s*Android Debug(?:[,\s]|$)", signers[0], re.I)
    result["libraries"] = native_libraries(apk)
    result["nativePageSizeCompatible"] = all(item["elfCompatible16Kb"] and item["zipCompatible16Kb"] for item in result["libraries"])
    result["withinSizeBudget"] = apk.stat().st_size <= 150 * 1024 * 1024
    checks = {"minSdk26": result["minSdk"] == 26, "currentTargetSdk": result["targetSdk"] is not None and result["targetSdk"] >= required_target,
              "notDebuggable": not result["debuggable"], "releaseSignature": result["releaseCertificate"],
              "native16Kb": result["nativePageSizeCompatible"], "sizeBudget": result["withinSizeBudget"]}
    result["checks"] = checks
    result["passed"] = all(checks.values())
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--badging", type=Path, required=True)
    parser.add_argument("--signature", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--required-target", type=int, default=36)
    parser.add_argument("--release-cert-sha256", help="Expected publisher certificate fingerprint; absent means signature acceptance is pending")
    args = parser.parse_args()
    if args.required_target < 36:
        parser.error("required target cannot weaken the recorded API 36 policy")
    if args.release_cert_sha256 is not None and not re.fullmatch(r"[0-9a-fA-F]{64}", args.release_cert_sha256):
        parser.error("release certificate SHA256 requires exactly 64 hex characters")
    if args.output.exists():
        parser.error("report output must be new")
    try:
        value = report(args.apk, args.badging.read_text(encoding="utf-8-sig"), args.signature.read_text(encoding="utf-8-sig"), args.required_target, args.release_cert_sha256)
    except (OSError, ValueError, zipfile.BadZipFile, RuntimeError) as error:
        value = {"passed": False, "storeSubmissionReady": False, "error": str(error)}
    with args.output.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(value, output, indent=2)
        output.write("\n")
    print("ANDROID_RELEASE_PREREQUISITES=" + ("PASS" if value["passed"] else "FAIL"))
    return 0 if value["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
