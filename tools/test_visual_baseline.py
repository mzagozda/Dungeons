"""Baseline contracts use synthetic 4x4 fixtures, never gameplay approvals."""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from PIL import Image
from visual_baseline import capture_set, check, digest, PendingReview, prepare, safe_file


class VisualBaselineTests(unittest.TestCase):
    def setUp(self):
        root = Path(__file__).resolve().parents[1] / "artifacts/m8"
        root.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="baseline-contract-", dir=root)
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name).resolve()
        self.assertTrue(self.folder.is_relative_to(root.resolve()))
        self.captures = self.folder / "captures"
        self.captures.mkdir()
        self.baseline = self.folder / "baseline"
        self.profile = "synthetic-unit-fixture"
        self.names = ("fixture-first", "fixture-second")
        self.records = []
        for name in self.names:
            image = self.captures / (name + "-1.png")
            Image.new("RGBA", (4, 4), (80, 80, 80, 255)).save(image)
            record = dict(formatVersion=1, passed=True, scenario=name, width=4, height=4,
                          godot="synthetic fixture only", renderer="fixture", adapter="unit test",
                          quality="fixture", shaderTime=0, visualStepSeconds=1 / 60,
                          levelHash="a" * 64, contentHash="b" * 64, seed=1, tick=0,
                          finalHash="c" * 16, baselineApproved=False, physicalAcceptance=False,
                          imageSha256=digest(image))
            self.records.append(record)
        self.write_run()
        (self.captures / "catalog.log").write_text("SCREENSHOT_SCENARIOS=" + json.dumps([{"id": name} for name in self.names]) + "\n")
        # The runner's deliberate preservation sentinel is not JSON and must be ignored.
        (self.captures / "reserved.capture.json").write_text("preserved companion")

    def write_run(self):
        for record in self.records:
            (self.captures / (record["scenario"] + "-1.capture.json")).write_text(json.dumps(record))
        (self.captures / "result.json").write_text(json.dumps(dict(passed=True, renderer="fixture", scenarios=len(self.names), captures=self.records)))

    def approve_synthetic_fixture(self):
        manifest = prepare(self.captures, self.baseline, self.profile)
        manifest["reviewStatus"] = "approved"
        manifest["review"] = dict(reviewer="automated unit fixture", approvedUtc="2026-01-01T00:00:00Z", reference="synthetic contract test only")
        (self.baseline / "manifest.json").write_text(json.dumps(manifest))
        return manifest

    def compare(self):
        output = self.folder / "comparison"
        output.mkdir()
        return check(self.baseline, self.captures, self.profile, output)

    def cli(self, *arguments):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name("visual_baseline.py")), *map(str, arguments)], capture_output=True, text=True, check=False)
        self.assertFalse(result.stderr, result.stderr)
        return result.returncode, json.loads(result.stdout)

    def test_candidate_is_always_pending_and_missing_profiles_fail_closed(self):
        with self.assertRaises(PendingReview):
            self.compare()
        manifest = prepare(self.captures, self.baseline, self.profile)
        self.assertEqual("pending", manifest["reviewStatus"])
        self.assertIsNone(manifest["review"])
        before = (self.baseline / "manifest.json").read_bytes()
        output = self.folder / "pending"
        code, result = self.cli("check", self.baseline, self.captures, "--profile", self.profile, "--output", output)
        self.assertEqual(3, code)
        self.assertEqual("pending-review", result["status"])
        self.assertEqual(result, json.loads((output / "result.json").read_text()))
        self.assertEqual(before, (self.baseline / "manifest.json").read_bytes())

    def test_cli_prepare_and_approved_synthetic_exact_pass(self):
        code, result = self.cli("prepare", self.captures, self.baseline, "--profile", self.profile)
        self.assertEqual((0, "pending", 2), (code, result["reviewStatus"], result["cases"]))
        manifest = json.loads((self.baseline / "manifest.json").read_text())
        manifest.update(reviewStatus="approved", review=dict(reviewer="automated unit fixture", approvedUtc="2026-01-01T00:00:00Z", reference="synthetic contract test only"))
        (self.baseline / "manifest.json").write_text(json.dumps(manifest))
        code, result = self.cli("check", self.baseline, self.captures, "--profile", self.profile, "--output", self.folder / "passed")
        self.assertEqual(0, code)
        self.assertTrue(result["passed"])
        self.assertFalse(result["physicalAcceptance"])
        self.assertEqual(2, len(result["cases"]))

    def test_cli_visual_regression_is_one_and_emits_diff(self):
        self.approve_synthetic_fixture()
        image = self.captures / "fixture-first-1.png"
        with Image.open(image) as original:
            changed = original.copy()
        changed.putpixel((0, 0), (86, 80, 80, 255))
        changed.save(image)
        self.records[0]["imageSha256"] = digest(image)
        self.write_run()
        output = self.folder / "regression"
        code, result = self.cli("check", self.baseline, self.captures, "--profile", self.profile, "--output", output)
        self.assertEqual(1, code)
        self.assertFalse(result["passed"])
        self.assertEqual(1, result["cases"][0]["pixels"]["mismatchedPixels"])
        self.assertTrue((output / "fixture-first.diff.png").is_file())

    def test_identical_pixels_cannot_hide_adapter_source_or_state_changes(self):
        self.approve_synthetic_fixture()
        for field, changed in (("adapter", "other GPU"), ("contentHash", "d" * 64), ("levelHash", "d" * 64), ("seed", 2), ("tick", 1), ("finalHash", "d" * 16), ("godot", "other version"), ("quality", "other")):
            with self.subTest(field=field):
                original = self.records[0][field]
                self.records[0][field] = changed
                self.write_run()
                output = self.folder / field
                output.mkdir()
                result = check(self.baseline, self.captures, self.profile, output)
                self.assertFalse(result["passed"])
                self.assertEqual([field], result["cases"][0]["changedIdentityFields"])
                self.assertTrue(result["cases"][0]["pixels"]["passed"])
                self.records[0][field] = original

    def test_review_requires_explicit_complete_human_record(self):
        manifest = self.approve_synthetic_fixture()
        for review in (None, {}, dict(reviewer="person", approvedUtc="2026-01-01T00:00:00Z", reference=" ")):
            with self.subTest(review=review):
                manifest["review"] = review
                (self.baseline / "manifest.json").write_text(json.dumps(manifest))
                with self.assertRaises(PendingReview):
                    check(self.baseline, self.captures, self.profile, self.folder)

    def test_invalid_review_time_and_wrong_profile_are_rejected(self):
        manifest = self.approve_synthetic_fixture()
        with self.assertRaises(ValueError):
            check(self.baseline, self.captures, "wrong-profile", self.folder)
        for timestamp in ("invalid", "2026-01-01T00:00:00", "2026-01-01T00:00:00+01:00"):
            manifest["review"]["approvedUtc"] = timestamp
            (self.baseline / "manifest.json").write_text(json.dumps(manifest))
            with self.assertRaises(ValueError):
                check(self.baseline, self.captures, self.profile, self.folder)

    def test_baseline_tampering_and_unsafe_paths_are_rejected(self):
        manifest = self.approve_synthetic_fixture()
        sidecar = self.baseline / manifest["cases"][0]["capture"]
        sidecar.write_text(sidecar.read_text() + " ")
        with self.assertRaisesRegex(ValueError, "files changed"):
            self.compare()
        for path in ("../outside.png", "/root.png", "C:/file.png", "cases\\file.png", "cases//file.png", "./file.png"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                safe_file(self.baseline, path)

    def test_incomplete_duplicate_and_extra_baseline_cases_are_rejected(self):
        manifest = self.approve_synthetic_fixture()
        for cases in (manifest["cases"][:1], manifest["cases"] + [manifest["cases"][0]], manifest["cases"] + [dict(scenario="extra")]):
            changed = dict(manifest, cases=cases)
            (self.baseline / "manifest.json").write_text(json.dumps(changed))
            with self.assertRaisesRegex(ValueError, "exact executable"):
                check(self.baseline, self.captures, self.profile, self.folder)

    def test_catalogue_requires_all_scenarios_and_no_duplicates(self):
        result = json.loads((self.captures / "result.json").read_text())
        result["captures"] = result["captures"][:1]
        (self.captures / "result.json").write_text(json.dumps(result))
        with self.assertRaisesRegex(ValueError, "every executable"):
            capture_set(self.captures)
        self.write_run()
        (self.captures / "catalog.log").write_text("SCREENSHOT_SCENARIOS=" + json.dumps([{"id": self.names[0]}] * 2))
        with self.assertRaises(ValueError):
            capture_set(self.captures)

    def test_sidecar_run_image_and_backend_provenance_must_agree(self):
        original = copy.deepcopy(self.records)
        for field, value in (("imageSha256", "0" * 64), ("renderer", "other-backend"), ("width", 5), ("baselineApproved", True), ("physicalAcceptance", True), ("seed", True), ("shaderTime", 1), ("visualStepSeconds", float("nan"))):
            with self.subTest(field=field):
                self.records = copy.deepcopy(original)
                self.records[0][field] = value
                self.write_run()
                with self.assertRaises(ValueError):
                    capture_set(self.captures)
        self.records = original
        self.write_run()
        self.records[0]["tick"] = 2
        (self.captures / "result.json").write_text(json.dumps(dict(passed=True, renderer="fixture", scenarios=2, captures=self.records)))
        with self.assertRaisesRegex(ValueError, "provenance differs"):
            capture_set(self.captures)

    def test_cli_invalid_input_and_existing_output_preserve_sources(self):
        self.approve_synthetic_fixture()
        before = (self.captures / "result.json").read_bytes()
        for destination in (self.captures, self.captures / "inside", self.baseline / "inside"):
            code, result = self.cli("check", self.baseline, self.captures, "--profile", self.profile, "--output", destination)
            self.assertEqual((2, "invalid-input"), (code, result["status"]))
        output = self.folder / "existing"
        output.mkdir()
        sentinel = output / "result.json"
        sentinel.write_text("preserved")
        code, _ = self.cli("check", self.baseline, self.captures, "--profile", self.profile, "--output", output)
        self.assertEqual(2, code)
        self.assertEqual("preserved", sentinel.read_text())
        self.assertEqual(before, (self.captures / "result.json").read_bytes())
        with self.assertRaises(ValueError):
            prepare(self.captures, self.captures / "candidate", self.profile)
        with self.assertRaises(FileExistsError):
            prepare(self.captures, self.baseline, self.profile)


if __name__ == "__main__":
    unittest.main()
