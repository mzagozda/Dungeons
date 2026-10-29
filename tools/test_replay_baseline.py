"""Review/immutability contracts use synthetic recordings, not actual golden approvals."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from replay_baseline import FIELDS, PendingReview, REQUIRED, check

SCRIPT = Path(__file__).with_name("replay_baseline.py")


class ReplayBaselineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.baseline = self.root / "baseline"
        self.baseline.mkdir()
        self.manifest = {"formatVersion": 1, "reviewStatus": "approved", "reviewedGolden": True, "review": {"reviewer": "Synthetic test fixture", "approvedUtc": "2026-10-27T20:00:00Z", "reference": "unit-test-only", "scope": "synthetic metadata validation"}, "candidates": []}
        for name in sorted(REQUIRED):
            replay = {"levelId": "core.mossy_hollow", "seed": 1, "tickCount": 2, "initialHash": "0" * 16, "finalHash": "b" * 16, "levelHash": "c" * 64, "contentHash": "d" * 64, "tickHashes": ["a" * 16, "b" * 16]}
            path = self.baseline / name
            path.write_text(json.dumps(replay), encoding="utf-8")
            self.manifest["candidates"].append({"name": name, "fileSha256": hashlib.sha256(path.read_bytes()).hexdigest(), **{key: replay[key] for key in FIELDS}})
        self.save()

    def save(self):
        (self.baseline / "manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def test_reviewed_bytes_are_checked_without_claiming_simulation_or_devices(self):
        result = check(self.baseline)
        self.assertTrue(result["passed"])
        self.assertEqual(3, len(result["cases"]))
        self.assertFalse(result["simulationVerified"])
        self.assertFalse(result["physicalAcceptance"])

    def test_missing_or_pending_review_is_not_acceptance(self):
        (self.baseline / "manifest.json").unlink()
        with self.assertRaises(PendingReview):
            check(self.baseline)
        self.manifest["reviewStatus"] = "pending"
        self.save()
        with self.assertRaises(PendingReview):
            check(self.baseline)

    def test_review_requires_attribution_reference_and_zoned_time(self):
        original = copy.deepcopy(self.manifest)
        for key, value in (("reviewer", ""), ("reference", ""), ("scope", ""), ("approvedUtc", "2026-10-27T20:00:00")):
            self.manifest = copy.deepcopy(original)
            self.manifest["review"][key] = value
            self.save()
            with self.assertRaises(ValueError):
                check(self.baseline)

    def test_missing_extra_duplicate_and_unsafe_case_names_are_rejected(self):
        original = copy.deepcopy(self.manifest)
        for name in ("../escape.dreplay", original["candidates"][1]["name"], "other.dreplay"):
            self.manifest = copy.deepcopy(original)
            self.manifest["candidates"][0]["name"] = name
            self.save()
            with self.assertRaises(ValueError):
                check(self.baseline)
        self.manifest = original
        self.save()
        (self.baseline / "extra.dreplay").write_text("{}", encoding="utf-8")
        with self.assertRaises(ValueError):
            check(self.baseline)

    def test_byte_tampering_is_rejected_even_when_json_identity_still_matches(self):
        path = self.baseline / self.manifest["candidates"][0]["name"]
        path.write_bytes(path.read_bytes() + b"\n")
        with self.assertRaisesRegex(ValueError, "bytes changed"):
            check(self.baseline)

    def test_every_identity_field_is_bound_to_the_review(self):
        original = copy.deepcopy(self.manifest)
        for key in FIELDS:
            self.manifest = copy.deepcopy(original)
            self.manifest["candidates"][0][key] = "changed"
            self.save()
            with self.assertRaisesRegex(ValueError, "identity"):
                check(self.baseline)

    def test_complete_tick_hashes_are_required_even_if_new_bytes_are_supplied(self):
        case = self.manifest["candidates"][0]
        path = self.baseline / case["name"]
        replay = json.loads(path.read_bytes())
        replay["tickHashes"].pop()
        path.write_text(json.dumps(replay), encoding="utf-8")
        case["fileSha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        self.save()
        with self.assertRaisesRegex(ValueError, "per-tick"):
            check(self.baseline)

    def test_duplicate_json_metadata_keys_are_rejected(self):
        path = self.baseline / "manifest.json"
        path.write_text(path.read_text().replace('"formatVersion": 1', '"formatVersion": 1, "formatVersion": 1'), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            check(self.baseline)

    def run_cli(self, output):
        return subprocess.run([sys.executable, str(SCRIPT), str(self.baseline), "--output", str(output)], capture_output=True, text=True, check=False)

    def test_cli_preserves_existing_report_and_baseline(self):
        output = self.root / "report.json"
        original = (self.baseline / "manifest.json").read_bytes()
        self.assertEqual(0, self.run_cli(output).returncode)
        report = output.read_bytes()
        self.assertEqual(2, self.run_cli(output).returncode)
        self.assertEqual(report, output.read_bytes())
        self.assertEqual(original, (self.baseline / "manifest.json").read_bytes())
        self.assertEqual(2, self.run_cli(self.baseline / "forbidden.json").returncode)
        self.assertFalse((self.baseline / "forbidden.json").exists())

    def test_cli_records_pending_status_with_distinct_exit_code(self):
        self.manifest["reviewStatus"] = "pending"
        self.save()
        output = self.root / "pending.json"
        self.assertEqual(3, self.run_cli(output).returncode)
        self.assertEqual("pending-review", json.loads(output.read_bytes())["status"])


if __name__ == "__main__":
    unittest.main()
