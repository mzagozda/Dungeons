"""Synthetic split-set contracts; actual bundletool integration is recorded separately."""
import copy
from pathlib import Path
import tempfile
import unittest
import zipfile

from audit_android_splits import check
from test_android_release import elf

CERT = "ab" * 32
PACKAGE = "package: name='org.blockdungeons.game' versionCode='1' versionName='0.0.1'"
BASE = PACKAGE + "\nsdkVersion:'26'\ntargetSdkVersion:'36'\n"


class AndroidSplitTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.reference = self.root / "universal.apk"
        self.native_path = "lib/arm64-v8a/libsample.so"
        self.asset_path = "assets/.godot/mono/Dungeons.Game.dll"
        self.package(self.reference, {self.native_path: elf(), self.asset_path: b"synthetic managed payload"})
        self.plan = {"deviceSpec": {"supportedAbis": ["arm64-v8a"], "supportedLocales": ["en", "pl"], "screenDensity": 480, "sdkVersion": 36}, "files": []}
        for name, split, payload in (("base", None, {}), ("arm64", "config.arm64_v8a", {self.native_path: elf()}), ("assets", "assetPackInstallTime", {self.asset_path: b"synthetic managed payload"}), ("en", "config.en", {}), ("pl", "config.pl", {}), ("density", "config.xxhdpi", {})):
            apk = self.root / (name + ".apk")
            self.package(apk, payload)
            badging = self.root / (name + ".badging.txt")
            badging.write_text(BASE if split is None else PACKAGE + f" split='{split}'\n", encoding="utf-8")
            signature = self.root / (name + ".signature.txt")
            signature.write_text("Verifies\nSigner #1 certificate SHA-256 digest: " + CERT + "\n", encoding="utf-8")
            self.plan["files"].append({"apk": str(apk), "badging": str(badging), "signature": str(signature), "zipalign16Passed": True})

    def package(self, path, entries):
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            for name, data in entries.items():
                archive.writestr(name, data)

    def run_check(self):
        return check(self.plan, self.reference, BASE, CERT)

    def test_all_payload_bytes_and_roles_pass_without_claiming_devices_or_release(self):
        result = self.run_check()
        self.assertTrue(result["passed"])
        self.assertEqual(6, len(result["files"]))
        self.assertTrue(result["matchesUniversalPayloadBytes"])
        for field in ("physicalAcceptance", "releaseAcceptance", "storeSubmissionReady"):
            self.assertFalse(result[field])

    def test_missing_base_native_asset_locale_and_density_are_independent_failures(self):
        original = copy.deepcopy(self.plan)
        for index in range(6):
            self.plan = copy.deepcopy(original)
            self.plan["files"].pop(index)
            with self.subTest(index=index), self.assertRaises(ValueError):
                self.run_check()

    def test_every_apk_requires_signature_identity_and_actual_zipalign(self):
        for index, item in enumerate(self.plan["files"]):
            signature = Path(item["signature"])
            original = signature.read_text()
            signature.write_text(original.replace(CERT, "cd" * 32))
            with self.subTest(index=index), self.assertRaisesRegex(ValueError, "signer"):
                self.run_check()
            signature.write_text(original)
            item["zipalign16Passed"] = False
            with self.assertRaisesRegex(ValueError, "zipalign"):
                self.run_check()
            item["zipalign16Passed"] = True

    def test_package_and_version_mismatch_are_rejected(self):
        path = Path(self.plan["files"][1]["badging"])
        original = path.read_text()
        for changed in (original.replace("org.blockdungeons.game", "wrong.app"), original.replace("versionCode='1'", "versionCode='2'")):
            path.write_text(changed)
            with self.assertRaisesRegex(ValueError, "identity"):
                self.run_check()

    def test_tampered_asset_and_native_payloads_are_rejected(self):
        asset = self.root / "assets.apk"
        self.package(asset, {self.asset_path: b"modified"})
        with self.assertRaisesRegex(ValueError, "bytes differ"):
            self.run_check()
        self.package(asset, {self.asset_path: b"synthetic managed payload"})
        self.package(self.root / "arm64.apk", {self.native_path: elf() + b"modified"})
        with self.assertRaisesRegex(ValueError, "bytes differ"):
            self.run_check()

    def test_native_alignment_and_abi_checks_are_not_replaced_by_zipalign(self):
        self.package(self.root / "arm64.apk", {self.native_path: elf(alignment=4096)})
        with self.assertRaisesRegex(ValueError, "ELF/ZIP"):
            self.run_check()
        self.package(self.root / "arm64.apk", {"lib/x86_64/libsample.so": elf()})
        with self.assertRaisesRegex(ValueError, "ABI/path"):
            self.run_check()

    def test_duplicate_split_or_payload_is_not_silently_merged(self):
        self.plan["files"].append(copy.deepcopy(self.plan["files"][0]))
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.run_check()
        self.plan["files"].pop()
        self.package(self.root / "en.apk", {self.asset_path: b"synthetic managed payload"})
        with self.assertRaisesRegex(ValueError, "Duplicate game/native"):
            self.run_check()

    def test_universal_and_synthetic_sdk_must_match_the_supported_contract(self):
        for field, value in (("supportedAbis", ["x86_64"]), ("sdkVersion", 25), ("screenDensity", 240)):
            original = self.plan["deviceSpec"][field]
            self.plan["deviceSpec"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.run_check()
            self.plan["deviceSpec"][field] = original
        (self.root / "base.badging.txt").write_text(BASE.replace("36", "35"))
        with self.assertRaisesRegex(ValueError, "SDK"):
            self.run_check()


if __name__ == "__main__":
    unittest.main()
