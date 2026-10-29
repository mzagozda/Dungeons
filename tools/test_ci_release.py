"""Release version allocation must preserve identity and reject reuse before writing."""
import json
from pathlib import Path
import tempfile
import unittest

from prepare_ci_release import prepare


class CiReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for directory in ("platform/store", "platform/android", "game"):
            (self.root / directory).mkdir(parents=True)
        self.metadata = self.root / "platform/store/en-US.json"
        self.metadata.write_text(json.dumps({"release": {"versionName": "1.2.3", "androidVersionCode": 7}, "reviewStatus": "approved", "review": {"owner": "fixture"}}))
        (self.root / "platform/android/play-console.json").write_text(json.dumps({"uploadedVersionCode": 8}))
        self.presets = self.root / "game/export_presets.cfg"
        self.presets.write_text('[preset.0]\nname="Windows Desktop"\n\n[preset.0.options]\napplication/product_version="1.2.3"\n\n[preset.1]\nname="Android"\n\n[preset.1.options]\nversion/code=7\nversion/name="1.2.3"\n\n[preset.3]\nname="Android Low"\n\n[preset.3.options]\nversion/code=7\nversion/name="1.2.3"\n')
        self.sha = "a123456789abcdef0123456789abcdef01234567"

    def test_stamp_both_presets_clear_review_and_keep_display_version(self):
        identity = prepare(self.root, 42, 1, self.sha)
        self.assertEqual(1042, identity["androidVersionCode"])
        self.assertEqual("BlockDungeons-1.2.3-build42.1-a123456789ab", identity["stem"])
        self.assertEqual(2, self.presets.read_text().count("version/code=1042\n"))
        self.assertIn('application/product_version="1.2.3"', self.presets.read_text())
        metadata = json.loads(self.metadata.read_text())
        self.assertEqual("1.2.3", metadata["release"]["versionName"])
        self.assertEqual("pending", metadata["reviewStatus"])
        self.assertIsNone(metadata["review"])

    def test_attempt_has_distinct_filename_but_same_code(self):
        result = prepare(self.root, 42, 2, self.sha)
        self.assertEqual(1042, result["androidVersionCode"])
        self.assertIn("build42.2-", result["stem"])

    def reject_without_writes(self, **arguments):
        before = (self.metadata.read_bytes(), self.presets.read_bytes())
        with self.assertRaises(ValueError):
            prepare(self.root, arguments.get("run_number", 42), arguments.get("run_attempt", 1), arguments.get("commit", self.sha), arguments.get("offset", 1000))
        self.assertEqual(before, (self.metadata.read_bytes(), self.presets.read_bytes()))

    def test_uploaded_version_cannot_be_reused(self):
        self.reject_without_writes(run_number=1, offset=7)

    def test_play_maximum_is_enforced(self):
        self.reject_without_writes(run_number=2_100_000_000)

    def test_invalid_run_numbers_offsets_and_sha_rejected(self):
        for arguments in ({"run_number": 0}, {"run_attempt": 0}, {"offset": -1}, {"commit": "main; command"}):
            with self.subTest(arguments=arguments):
                self.reject_without_writes(**arguments)

    def test_missing_low_preset_rejected_without_partial_stamp(self):
        self.presets.write_text(self.presets.read_text().split("[preset.3]")[0])
        self.reject_without_writes()

    def test_version_mismatch_rejected_without_partial_stamp(self):
        self.presets.write_text(self.presets.read_text().replace('version/name="1.2.3"', 'version/name="1.2.4"', 1))
        self.reject_without_writes()

    def test_custom_offset_can_follow_previous_workflow_counter(self):
        self.assertEqual(5001, prepare(self.root, 1, 1, self.sha, 5000)["androidVersionCode"])


if __name__ == "__main__":
    unittest.main()
