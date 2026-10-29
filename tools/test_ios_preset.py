"""Check the real preset transformation without claiming a Mac export."""
import json
from pathlib import Path
import re
import unittest

from prepare_ios_preset import prepare

ROOT = Path(__file__).resolve().parent.parent


class IosPresetTests(unittest.TestCase):
    def setUp(self):
        self.source = (ROOT / "game/export_presets.cfg").read_bytes().decode("utf-8")

    def test_real_preset_uses_native_file_sharing_without_duplicate_plist_keys(self):
        result = prepare(self.source, "ABCDE12345")
        self.assertIn('application/app_store_team_id="ABCDE12345"', result)
        for key in ("application/export_project_only", "user_data/accessible_from_files_app", "user_data/accessible_from_itunes_sharing"):
            self.assertEqual(1, result.count(key + "=true"))
        self.assertNotIn("<key>UIFileSharingEnabled</key>", result)
        self.assertNotIn("<key>LSSupportsOpeningDocumentsInPlace</key>", result)
        self.assertIn('application/min_ios_version="14.0"', result)
        before = re.split(r"(?m)(?=^\[preset\.\d+\]\r?$)", self.source)
        after = re.split(r"(?m)(?=^\[preset\.\d+\]\r?$)", result)
        for left, right in zip(before, after, strict=True):
            if 'name="iOS"' not in left:
                self.assertEqual(left, right)

    def test_existing_unrelated_plist_values_are_retained(self):
        source = self.source.replace('<key>UIFileSharingEnabled</key>', '<key>CustomFlag</key><string>retained</string><key>UIFileSharingEnabled</key>')
        result = prepare(source, "ABCDE12345")
        value = re.search(r"^application/additional_plist_content=(.*)$", result, re.M)[1]
        decoded = json.loads(value)
        self.assertIn("CustomFlag", decoded)
        self.assertIn("retained", decoded)

    def test_transform_is_idempotent_and_target_cannot_silently_change(self):
        once = prepare(self.source, "ABCDE12345")
        self.assertEqual(once, prepare(once, "ABCDE12345"))
        with self.assertRaisesRegex(ValueError, "iOS 14/arm64"):
            prepare(self.source.replace('application/min_ios_version="14.0"', 'application/min_ios_version="15.0"'), "ABCDE12345")
        with self.assertRaisesRegex(ValueError, "Apple team"):
            prepare(self.source, "Invented identity")


if __name__ == "__main__":
    unittest.main()
