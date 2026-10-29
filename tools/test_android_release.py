import struct
import tempfile
import unittest
from pathlib import Path
import zipfile

from audit_android_release import elf_loads, native_libraries, report


def elf(alignment=16384, address=0, machine=183):
    header = struct.pack("<16sHHIQQQIHHHHHH", b"\x7fELF\x02\x01\x01" + bytes(9), 3, machine, 1, 0, 64, 0, 0, 64, 56, 1, 0, 0, 0)
    return header + struct.pack("<IIQQQQQQ", 1, 5, 0, address, 0, 120, 120, alignment)


class AndroidReleaseTests(unittest.TestCase):
    badging = "package: name='org.blockdungeons.game'\nsdkVersion:'26'\ntargetSdkVersion:'36'\n"
    signed = "Verifies\nSigner #1 certificate DN: CN=Release\nSigner #1 certificate SHA-256 digest: " + "ab" * 32 + "\n"
    versioned_signed = (
        "Verifies\n"
        "Verified using v2 scheme (APK Signature Scheme v2): true\n"
        "Verified using v3 scheme (APK Signature Scheme v3): true\n"
        "Number of signers: 1\n"
        "V3.0 Signer: certificate DN: CN=Release\n"
        "V3.0 Signer: certificate SHA-256 digest: " + "AB" * 32 + "\n"
    )

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.apk = Path(self.temp.name) / "level build.apk"

    def package(self, data=None, stored=False, aligned=False, name="lib/arm64-v8a/libgame.so"):
        info = zipfile.ZipInfo(name)
        info.filename = name  # Preserve deliberately malformed wire names on Windows.
        info.compress_type = zipfile.ZIP_STORED if stored else zipfile.ZIP_DEFLATED
        if aligned:
            pad = 16384 - (30 + len(name.encode("utf-8"))) % 16384
            info.extra = struct.pack("<HH", 0xD935, pad - 4) + bytes(pad - 4)
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr(info, elf() if data is None else data)

    def test_valid_native_apk_never_claims_aab_or_device_acceptance(self):
        self.package(stored=True, aligned=True)
        result = report(self.apk, self.badging, self.signed, release_certificate_sha256="ab" * 32)
        self.assertTrue(result["passed"])
        self.assertFalse(result["storeSubmissionReady"])
        self.assertFalse(result["aabAcceptance"])
        self.assertFalse(result["physical16KbAcceptance"])

    def test_compressed_libraries_do_not_require_zip_alignment(self):
        self.package()
        self.assertTrue(native_libraries(self.apk)[0]["zipCompatible16Kb"])

    def test_versioned_signer_output_preserves_qa_identity_without_release_acceptance(self):
        self.package()
        signature = self.versioned_signed.replace("CN=Release", "CN=Android Debug, O=Block Dungeons QA, C=PL")
        result = report(self.apk, self.badging + "application-debuggable\n", signature)
        self.assertTrue(result["verifiedSignature"])
        self.assertEqual(["ab" * 32], result["certificateSha256"])
        self.assertFalse(result["releaseCertificate"])
        self.assertFalse(result["passed"])

    def test_versioned_release_signer_requires_one_verified_expected_certificate(self):
        self.package()
        result = report(self.apk, self.badging, self.versioned_signed, release_certificate_sha256="ab" * 32)
        self.assertTrue(result["passed"])
        for signature, expected in (
            (self.versioned_signed.replace("Verifies\n", "DOES NOT VERIFY\n"), "ab" * 32),
            (self.versioned_signed, None),
            (self.versioned_signed, "cd" * 32),
            (self.versioned_signed.replace("CN=Release", "CN=Android Debug"), "ab" * 32),
            (self.versioned_signed.replace("AB" * 32, "AB" * 31), "ab" * 32),
            (self.versioned_signed + self.versioned_signed.split("Number of signers: 1\n")[1], "ab" * 32),
            (self.versioned_signed + self.signed.split("Verifies\n")[1], "ab" * 32),
        ):
            with self.subTest(signature=signature, expected=expected):
                result = report(self.apk, self.badging, signature, release_certificate_sha256=expected)
                self.assertFalse(result["releaseCertificate"])
                self.assertFalse(result["passed"])

    def test_stored_libraries_require_the_actual_local_data_offset(self):
        self.package(stored=True)
        result = report(self.apk, self.badging, self.signed)
        self.assertFalse(result["passed"])
        self.assertFalse(result["libraries"][0]["zipCompatible16Kb"])

    def test_each_elf_load_must_support_16kb_mapping(self):
        for data in (elf(4096), elf(24576), elf(address=4096), elf(65536, address=16384)):
            with self.subTest(data=data):
                self.assertFalse(elf_loads(data)[0]["compatible16Kb"])

    def test_truncation_wrong_abi_and_invalid_extents_are_rejected(self):
        extent = bytearray(elf())
        struct.pack_into("<Q", extent, 64 + 32, 121)
        for data in (b"", elf()[:100], elf(machine=62), extent):
            with self.subTest(data=data):
                with self.assertRaises(ValueError):
                    elf_loads(data)

    def test_no_load_segments_cannot_pass(self):
        data = bytearray(elf())
        struct.pack_into("<I", data, 64, 4)
        with self.assertRaisesRegex(ValueError, "no LOAD"):
            elf_loads(data)

    def test_debug_signing_debuggable_and_old_target_fail_independently(self):
        self.package()
        for badging, signature, failed in (
            (self.badging.replace("36", "34"), self.signed, "currentTargetSdk"),
            (self.badging + "application-debuggable\n", self.signed, "notDebuggable"),
            (self.badging, self.signed.replace("CN=Release", "C=US, O=Android, CN=Android Debug"), "releaseSignature"),
            (self.badging, self.signed.replace("Verifies", "DOES NOT VERIFY"), "releaseSignature"),
            ("", self.signed, "minSdk26"),
        ):
            with self.subTest(failed=failed):
                result = report(self.apk, badging, signature, release_certificate_sha256="ab" * 32)
                self.assertFalse(result["passed"])
                self.assertFalse(result["checks"][failed])

    def test_unknown_and_unexpected_publisher_certificates_cannot_pass(self):
        self.package()
        for expected in (None, "cd" * 32):
            with self.subTest(expected=expected):
                result = report(self.apk, self.badging, self.signed, release_certificate_sha256=expected)
                self.assertTrue(result["verifiedSignature"])
                self.assertFalse(result["checks"]["releaseSignature"])

    def test_unsupported_abi_and_unsafe_member_paths_fail(self):
        for name in ("lib/x86_64/libgame.so", "../lib/arm64-v8a/libgame.so", "lib\\arm64-v8a\\libgame.so"):
            with self.subTest(name=name):
                self.package(name=name)
                with self.assertRaises(ValueError):
                    native_libraries(self.apk)

    def test_empty_apk_cannot_pass(self):
        with zipfile.ZipFile(self.apk, "w"):
            pass
        with self.assertRaisesRegex(ValueError, "no arm64"):
            native_libraries(self.apk)


if __name__ == "__main__":
    unittest.main()
