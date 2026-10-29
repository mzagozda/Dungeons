"""Archive parser/contracts use synthetic fixtures, not a Mac/device acceptance result."""
import json
from pathlib import Path
import plistlib
import struct
import subprocess
import sys
import tempfile
import unittest

from audit_ios_archive import ARM64, audit, audit_application, macho

SCRIPT = Path(__file__).with_name("audit_ios_archive.py")
BUNDLE_ID = "org.blockdungeons.game"


def binary(minimum=14 << 16, sdk=(26 << 16) | (1 << 8), platform=2, kind=2, cpu=ARM64, legacy=False):
    command = struct.pack("<4I", 0x25, 16, minimum, sdk) if legacy else struct.pack("<6I", 0x32, 24, platform, minimum, sdk, 0)
    return struct.pack("<8I", 0xFEEDFACF, cpu, 0, kind, 1, len(command), 0, 0) + command


def fat(payload, wide=False, endian=">"):
    offset = 64
    header = struct.pack(endian + "2I", 0xCAFEBABF if wide else 0xCAFEBABE, 1)
    record = struct.pack(endian + "IIQQII", ARM64, 0, offset, len(payload), 3, 0) if wide else struct.pack(endian + "5I", ARM64, 0, offset, len(payload), 3)
    return (header + record).ljust(offset, b"\0") + payload


class IosArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.archive = self.root / "fixture.xcarchive"
        self.app = self.archive / "Products/Applications/BlockDungeons.app"
        self.framework = self.app / "Frameworks/Dungeons.Game.framework"
        self.framework.mkdir(parents=True)
        self.info = {"CFBundleIdentifier": BUNDLE_ID, "CFBundleExecutable": "BlockDungeons", "MinimumOSVersion": "14.0", "DTPlatformName": "iphoneos", "DTSDKName": "iphoneos26.1",
                     "UIFileSharingEnabled": True, "LSSupportsOpeningDocumentsInPlace": True, "UIDeviceFamily": [1, 2],
                     "UISupportedInterfaceOrientations": ["UIInterfaceOrientationLandscapeLeft", "UIInterfaceOrientationLandscapeRight"]}
        self.write_info()
        (self.archive / "Info.plist").write_bytes(plistlib.dumps({"ApplicationProperties": {"ApplicationPath": "Applications/BlockDungeons.app", "CFBundleIdentifier": BUNDLE_ID}}))
        (self.app / "BlockDungeons").write_bytes(binary())
        (self.framework / "Info.plist").write_bytes(plistlib.dumps({"CFBundleExecutable": "Dungeons.Game", "MinimumOSVersion": "14.0"}))
        (self.framework / "Dungeons.Game").write_bytes(binary(kind=6))

    def write_info(self, binary_plist=False):
        (self.app / "Info.plist").write_bytes(plistlib.dumps(self.info, fmt=plistlib.FMT_BINARY if binary_plist else plistlib.FMT_XML))

    def test_xml_and_binary_plists_record_every_actual_macho_without_claiming_device(self):
        for binary_plist in (False, True):
            with self.subTest(binary=binary_plist):
                self.write_info(binary_plist)
                result = audit(self.archive, BUNDLE_ID)
                self.assertEqual(2, len(result["binaries"]))
                self.assertEqual(1, len(result["frameworks"]))
                self.assertTrue(all(item["sha256"] for item in result["binaries"]))

    def test_native_framework_minimum_cannot_hide_behind_app_plist(self):
        (self.framework / "Dungeons.Game").write_bytes(binary(minimum=15 << 16, kind=6))
        with self.assertRaisesRegex(ValueError, "minimum=15"):
            audit(self.archive, BUNDLE_ID)

    def test_extracted_application_checks_the_same_actual_native_payload(self):
        self.assertEqual(audit(self.archive, BUNDLE_ID), audit_application(self.app, BUNDLE_ID))
        (self.framework / "Dungeons.Game").write_bytes(binary(minimum=15 << 16, kind=6))
        with self.assertRaisesRegex(ValueError, "minimum=15"):
            audit_application(self.app, BUNDLE_ID)

    def test_framework_metadata_default_15_is_rejected_even_when_machine_code_says_14(self):
        (self.framework / "Info.plist").write_bytes(plistlib.dumps({"CFBundleExecutable": "Dungeons.Game", "MinimumOSVersion": "15.0"}))
        with self.assertRaisesRegex(ValueError, "Framework metadata"):
            audit(self.archive, BUNDLE_ID)

    def test_device_architecture_platform_and_binary_type_are_required(self):
        for payload in (binary(platform=7), binary(cpu=0x01000007), binary(kind=6), b"not executable"):
            with self.subTest(payload=payload):
                (self.app / "BlockDungeons").write_bytes(payload)
                with self.assertRaises(ValueError):
                    audit(self.archive, BUNDLE_ID)

    def test_all_fat32_fat64_and_legacy_device_slices_are_checked(self):
        for wide in (False, True):
            for endian in (">", "<"):
                self.assertEqual("14.0.0", macho(fat(binary(), wide, endian))[0]["minimum"])
            with self.assertRaisesRegex(ValueError, "minimum=15"):
                macho(fat(binary(minimum=15 << 16), wide))
        self.assertEqual("18.5.0", macho(binary(kind=6, legacy=True, sdk=(18 << 16) | (5 << 8)))[0]["sdk"])

    def test_malformed_load_commands_and_container_bounds_fail_closed(self):
        original = binary()
        malformed = [original[:20], original[:-1], fat(original)[:80], b"\xfe\xed\xfa\xcf" + original[4:]]
        oversized = bytearray(original)
        struct.pack_into("<I", oversized, 36, 4096)
        malformed.append(bytes(oversized))
        duplicate = original[:16] + struct.pack("<2I", 2, 48) + original[24:] + original[32:]
        malformed.append(duplicate)
        wrong_arch = bytearray(fat(original))
        struct.pack_into(">I", wrong_arch, 8, 0x01000007)
        malformed.append(bytes(wrong_arch))
        for payload in malformed:
            with self.subTest(payload=payload):
                with self.assertRaises(ValueError):
                    macho(payload)

    def test_app_deployment_sdk_and_identity_are_independent_requirements(self):
        for key, value in (("MinimumOSVersion", "15.0"), ("DTSDKName", "iphoneos25.0"), ("DTPlatformName", "iphonesimulator"), ("CFBundleIdentifier", "another.app")):
            with self.subTest(key=key):
                previous = self.info[key]
                self.info[key] = value
                self.write_info()
                with self.assertRaises(ValueError):
                    audit(self.archive, BUNDLE_ID)
                self.info[key] = previous
        self.write_info()
        (self.app / "BlockDungeons").write_bytes(binary(sdk=25 << 16))
        with self.assertRaisesRegex(ValueError, "linked with iOS SDK"):
            audit(self.archive, BUNDLE_ID)

    def test_sharing_device_families_and_both_orientation_paths_are_checked(self):
        for key, value in (("UIFileSharingEnabled", False), ("LSSupportsOpeningDocumentsInPlace", "true"), ("UIDeviceFamily", [1]), ("UISupportedInterfaceOrientations~ipad", ["UIInterfaceOrientationPortrait"])):
            with self.subTest(key=key):
                original = dict(self.info)
                self.info[key] = value
                self.write_info()
                with self.assertRaises(ValueError):
                    audit(self.archive, BUNDLE_ID)
                self.info = original
        self.write_info()
        (self.app / "Frameworks/corrupt.dylib").write_bytes(b"invalid native library")
        with self.assertRaisesRegex(ValueError, "Embedded dylib"):
            audit(self.archive, BUNDLE_ID)

    def test_invalid_typed_metadata_and_missing_bundle_executable_are_rejected(self):
        for key, value in (("DTSDKName", 26), ("UIDeviceFamily", {}), ("MinimumOSVersion", True), ("CFBundleExecutable", "../other")):
            with self.subTest(key=key):
                original = dict(self.info)
                self.info[key] = value
                self.write_info()
                with self.assertRaises(ValueError):
                    audit(self.archive, BUNDLE_ID)
                self.info = original
        self.write_info()
        (self.framework / "Dungeons.Game").unlink()
        with self.assertRaisesRegex(ValueError, "Missing bundle executable"):
            audit(self.archive, BUNDLE_ID)

    def run_cli(self, output):
        return subprocess.run([sys.executable, str(SCRIPT), str(self.archive), "--bundle-id", BUNDLE_ID, "--output", str(output)], capture_output=True, text=True, check=False)

    def test_cli_writes_bounded_acceptance_scope_and_preserves_existing_output(self):
        output = self.root / "report.json"
        self.assertEqual(0, self.run_cli(output).returncode)
        original = output.read_bytes()
        result = json.loads(original)
        self.assertTrue(result["passed"])
        for key in ("signingAcceptance", "physicalAcceptance", "storeSubmissionReady"):
            self.assertFalse(result[key])
        self.assertEqual(2, self.run_cli(output).returncode)
        self.assertEqual(original, output.read_bytes())

    def test_cli_cannot_write_inside_input_archive(self):
        output = self.archive / "report.json"
        self.assertEqual(2, self.run_cli(output).returncode)
        self.assertFalse(output.exists())

    def test_cli_failure_retains_diagnostic_report_without_mutating_input(self):
        original = (self.app / "BlockDungeons").read_bytes()
        self.info["MinimumOSVersion"] = "15.0"
        self.write_info()
        output = self.root / "failed.json"
        self.assertEqual(1, self.run_cli(output).returncode)
        self.assertFalse(json.loads(output.read_bytes())["passed"])
        self.assertEqual(original, (self.app / "BlockDungeons").read_bytes())
        (self.app / "Info.plist").write_bytes(b'<?xml version="1.0"?><plist><dict>')
        invalid_output = self.root / "invalid-plist.json"
        self.assertEqual(1, self.run_cli(invalid_output).returncode)
        self.assertFalse(json.loads(invalid_output.read_bytes())["passed"])


if __name__ == "__main__":
    unittest.main()
