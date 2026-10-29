import json
from pathlib import Path
import struct
import tempfile
import unittest

from build_linux_deb import stage_package


class LinuxDebTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.export = self.root / "export"
        runtime = self.export / "data_Dungeons.Game_linuxbsd_x86_64"
        runtime.mkdir(parents=True)
        (runtime / "Dungeons.Game.dll").write_bytes(b"managed runtime fixture")
        (runtime / "libcoreclr.so").write_bytes(b"self-contained runtime fixture")
        (runtime / "unwanted.pdb").write_bytes(b"symbols")
        self.binary = self.export / "BlockDungeons.x86_64"
        header = bytearray(20)
        header[:7] = b"\x7fELF\x02\x01\x01"
        struct.pack_into("<H", header, 18, 62)
        self.binary.write_bytes(header)
        self.identity = {"formatVersion": 1, "versionName": "1.0.0", "runNumber": 7, "runAttempt": 2,
                         "commit": "a" * 40, "androidVersionCode": 1007, "stem": "BlockDungeons-1.0.0-build7.2-" + "a" * 12}
        for directory in ("platform/store", "game/assets/ui"):
            (self.root / directory).mkdir(parents=True)
        (self.root / "platform/store/en-US.json").write_text(json.dumps({"release": {"versionName": "1.0.0"},
            "publisher": {"name": "Fixture", "supportEmail": "fixture@example.com", "supportUrl": "https://example.com"}}))
        (self.root / "game/assets/ui/probe_icon.svg").write_text("<svg/>")
        (self.root / "THIRD_PARTY_NOTICES.md").write_text("Fixture notices")
        self.stage = self.root / "stage"

    def test_stage_contains_self_contained_runtime_launcher_metadata_and_notices(self):
        version = stage_package(self.root, self.identity, self.export, self.stage)
        self.assertEqual("1.0.0-7.2", version)
        control = (self.stage / "DEBIAN/control").read_text()
        self.assertIn("Architecture: amd64\n", control)
        self.assertIn("Version: 1.0.0-7.2\n", control)
        self.assertIn("libicu74 | libicu72", control)
        launcher = (self.stage / "usr/bin/block-dungeons").read_text()
        self.assertIn('exec /opt/block-dungeons/BlockDungeons.x86_64 "$@"', launcher)
        payload = self.stage / "opt/block-dungeons/data_Dungeons.Game_linuxbsd_x86_64"
        self.assertEqual(b"self-contained runtime fixture", (payload / "libcoreclr.so").read_bytes())
        self.assertFalse((payload / "unwanted.pdb").exists())
        self.assertEqual("Fixture notices", (self.stage / "usr/share/doc/block-dungeons/copyright").read_text())
        self.assertTrue((self.stage / "usr/share/applications/block-dungeons.desktop").is_file())

    def test_missing_runtime_and_wrong_native_architecture_fail_before_staging(self):
        runtime = self.export / "data_Dungeons.Game_linuxbsd_x86_64/libcoreclr.so"
        runtime.unlink()
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            stage_package(self.root, self.identity, self.export, self.stage)
        self.assertFalse(self.stage.exists())
        runtime.write_bytes(b"runtime")
        self.binary.write_bytes(b"MZ" + bytes(18))
        with self.assertRaisesRegex(ValueError, "ELF64"):
            stage_package(self.root, self.identity, self.export, self.stage)
        self.assertFalse(self.stage.exists())

    def test_existing_staging_and_mismatched_version_are_preserved(self):
        self.stage.mkdir()
        marker = self.stage / "keep"
        marker.write_text("original")
        with self.assertRaisesRegex(ValueError, "new"):
            stage_package(self.root, self.identity, self.export, self.stage)
        self.assertEqual("original", marker.read_text())
        marker.unlink()
        self.stage.rmdir()
        self.identity["versionName"] = "2.0.0"
        self.identity["stem"] = self.identity["stem"].replace("1.0.0", "2.0.0")
        with self.assertRaisesRegex(ValueError, "version"):
            stage_package(self.root, self.identity, self.export, self.stage)
        self.assertFalse(self.stage.exists())


if __name__ == "__main__":
    unittest.main()
