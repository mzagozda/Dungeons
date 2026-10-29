"""Release-copy boundaries using the actual draft and tracked export presets."""
import json
import re
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import store_metadata as store


class StoreMetadataTests(unittest.TestCase):
    def setUp(self):
        self.data = json.loads((store.ROOT / "platform/store/en-US.json").read_text(encoding="utf-8"))
        self.presets = (store.ROOT / "game/export_presets.cfg").read_text(encoding="utf-8")

    def test_unreviewed_draft_with_missing_privacy_cannot_claim_ready(self):
        self.data["reviewStatus"] = "pending"
        self.data["review"] = None
        self.data["publisher"]["privacyPolicyUrl"] = None
        result = store.validate(self.data, self.presets)
        self.assertTrue(result["draftValid"])
        self.assertFalse(result["metadataReady"])
        self.assertFalse(result["storeSubmissionReady"])
        self.assertIn("human copy review", result["pending"])
        self.assertIn("publisher.privacyPolicyUrl", result["pending"])

    def test_keywords_use_utf8_bytes_instead_of_character_count(self):
        self.data["ios"]["keywords"] = "é" * 51
        with self.assertRaisesRegex(ValueError, "Text limit"):
            store.validate(self.data, self.presets)
        self.data["ios"]["keywords"] = "é" * 50
        self.assertEqual(100, store.validate(self.data, self.presets)["fieldLengths"]["ios"]["keywords"])

    def test_both_android_identities_must_match(self):
        wrong = self.presets.replace('package/unique_name="org.blockdungeons.game"', 'package/unique_name="org.other.game"', 1)
        with self.assertRaisesRegex(ValueError, "identifier differs"):
            store.validate(self.data, wrong)

    def test_release_version_checks_android_low_and_ios(self):
        self.data["release"] = {"versionName": "1.2.3", "androidVersionCode": 7}
        presets = re.sub(r'(?m)^(version/name|application/short_version)="[^"]+"', r'\1="1.2.3"', self.presets)
        presets = re.sub(r'(?m)^version/code=\d+', 'version/code=7', presets)
        store.validate(self.data, presets)
        wrong = presets.replace('application/short_version="1.2.3"', 'application/short_version="2.0.0"')
        with self.assertRaisesRegex(ValueError, "version differs"):
            store.validate(self.data, wrong)
        for index in (presets.find("version/code=7"), presets.rfind("version/code=7")):
            with self.subTest(preset_offset=index):
                wrong = presets[:index] + presets[index:].replace("version/code=7", "version/code=8", 1)
                with self.assertRaisesRegex(ValueError, "version code differs"):
                    store.validate(self.data, wrong)

    def test_reserved_product_reference_and_ambiguous_keys_fail(self):
        self.data["android"]["shortDescription"] = "A Minecraft adventure"
        with self.assertRaisesRegex(ValueError, "Reserved product reference"):
            store.validate(self.data, self.presets)
        with self.assertRaisesRegex(ValueError, "Repeated metadata key"):
            json.loads('{"reviewStatus":"pending","reviewStatus":"approved"}', object_pairs_hook=store.unique_object)

    def test_approval_is_bound_to_the_exact_copy_and_publisher_fields(self):
        self.data["publisher"]["privacyPolicyUrl"] = None
        self.data["reviewStatus"] = "approved"
        self.data["review"] = {"reviewer": "Synthetic test reviewer", "approvedUtc": "2000-01-01T00:00:00Z", "reference": "In-memory contract fixture only", "copySha256": store.copy_fingerprint(self.data)}
        result = store.validate(self.data, self.presets)
        self.assertNotIn("human copy review", result["pending"])
        self.assertFalse(result["metadataReady"])  # This fixture deliberately lacks its privacy URL.
        self.assertIn("publisher.privacyPolicyUrl", result["pending"])
        previous_description = self.data["android"]["shortDescription"]
        self.data["android"]["shortDescription"] = "Changed copy"
        with self.assertRaisesRegex(ValueError, "Reviewed metadata changed"):
            store.validate(self.data, self.presets)
        self.data["android"]["shortDescription"] = previous_description
        self.data["publisher"]["name"] = "Changed test publisher"
        with self.assertRaisesRegex(ValueError, "Reviewed metadata changed"):
            store.validate(self.data, self.presets)

    def test_cli_preserves_existing_output_and_inputs(self):
        source = store.ROOT / "platform/store/en-US.json"
        before = source.read_bytes()
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "existing"
            output.mkdir()
            sentinel = output / "sentinel.txt"
            sentinel.write_text("keep", encoding="utf-8")
            process = subprocess.run([sys.executable, str(store.ROOT / "tools/store_metadata.py"), "export", "--output", str(output)], capture_output=True, text=True)
            self.assertEqual(2, process.returncode)
            self.assertEqual("keep", sentinel.read_text(encoding="utf-8"))
            self.assertEqual([sentinel], list(output.iterdir()))
        self.assertEqual(before, source.read_bytes())


if __name__ == "__main__":
    unittest.main()
