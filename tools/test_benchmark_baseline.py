"""Regression-gate boundary tests use synthetic statistics, never performance acceptance."""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from benchmark_baseline import compare, HOST_FIELDS, load_run, METHODS, read_json, record, REPORT


class BenchmarkBaselineTests(unittest.TestCase):
    def setUp(self):
        root = Path(__file__).resolve().parents[1] / "artifacts/nightly"
        root.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="benchmark-contract-", dir=root)
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name).resolve()
        self.assertTrue(self.folder.is_relative_to(root.resolve()))
        self.reports = self.folder / "reports"
        self.reports.mkdir()
        self.contracts = self.folder / "contracts.json"
        self.contract = dict(formatVersion=1, fixtureVersion=1, fixtureSha256="a" * 64,
                             requiredMethods=list(METHODS), correctnessPassed=True,
                             runnerIdentity=dict(cpuIdentity="synthetic CPU", logicalProcessors=4))
        host = {key: "synthetic context" for key in HOST_FIELDS}
        host.update(Configuration="RELEASE", HasAttachedDebugger=False, HasRyuJit=True)
        self.report = dict(HostEnvironmentInfo=host, Benchmarks=[dict(Method=name, Namespace="Dungeons.Benchmarks", Type="NightlyBenchmarks", FullName="Dungeons.Benchmarks.NightlyBenchmarks." + name, Parameters="", DisplayInfo="NightlyBenchmarks." + name + ": Job-AAAAAA(IterationCount=8, LaunchCount=3, OutlierMode=DontRemove, WarmupCount=12)", Statistics=dict(N=24, Mean=100), Memory=dict(BytesAllocatedPerOperation=0), HardwareIntrinsics="synthetic SIMD",
            Measurements=[dict(IterationMode="Workload", IterationStage="Result", LaunchIndex=launch, IterationIndex=iteration, Operations=1, Nanoseconds=100) for launch in range(1, 4) for iteration in range(1, 9)]) for name in METHODS])
        self.write()

    def write(self):
        self.contracts.write_text(json.dumps(self.contract))
        (self.reports / REPORT).write_text(json.dumps(self.report))

    def cli(self, *arguments):
        process = subprocess.run([sys.executable, str(Path(__file__).with_name("benchmark_baseline.py")), *map(str, arguments)], text=True, capture_output=True, check=False)
        self.assertFalse(process.stderr, process.stderr)
        return process.returncode, json.loads(process.stdout)

    def run_data(self):
        return load_run(self.reports, self.contracts)

    def test_twenty_percent_is_inclusive_and_higher_is_rejected(self):
        before = self.run_data()
        baseline = record(before, "synthetic")
        for mean, passed in ((100, True), (120, True), (120.0001, False), (121, False), (80, True)):
            with self.subTest(mean=mean):
                after = copy.deepcopy(before)
                after["cases"][METHODS[0]]["meanNs"] = mean
                self.assertEqual(passed, compare(baseline, after, "synthetic")["passed"])

    def test_all_six_means_must_pass_and_absolute_tick_budget_cannot_be_hidden(self):
        before = self.run_data()
        for method in METHODS:
            after = copy.deepcopy(before)
            after["cases"][method]["meanNs"] = 121
            self.assertFalse(compare(record(before, "synthetic"), after, "synthetic")["passed"])
        before["cases"][METHODS[0]]["meanNs"] = 2_000_000
        baseline = record(before, "synthetic")
        after = copy.deepcopy(before)
        after["cases"][METHODS[0]]["meanNs"] += 1
        self.assertFalse(compare(baseline, after, "synthetic")["passed"])
        with self.assertRaises(ValueError):
            record(after, "synthetic")

    def test_generated_job_labels_are_ignored_but_actual_settings_are_preserved(self):
        baseline = record(self.run_data(), "synthetic")
        for row in self.report["Benchmarks"]:
            row["DisplayInfo"] = row["DisplayInfo"].replace("AAAAAA", "BBBBBB")
        self.write()
        self.assertTrue(compare(baseline, self.run_data(), "synthetic")["passed"])
        self.report["Benchmarks"][0]["DisplayInfo"] = self.report["Benchmarks"][0]["DisplayInfo"].replace("Count=8", "Count=3")
        self.write()
        with self.assertRaises(ValueError):
            self.run_data()

    def test_insufficient_warmup_is_not_accepted_as_the_current_measurement_protocol(self):
        original = copy.deepcopy(self.report)
        for warmups in (0, 3, 11):
            with self.subTest(warmups=warmups):
                self.report = copy.deepcopy(original)
                self.report["Benchmarks"][0]["DisplayInfo"] = self.report["Benchmarks"][0]["DisplayInfo"].replace("WarmupCount=12", f"WarmupCount={warmups}")
                self.write()
                with self.assertRaisesRegex(ValueError, "twelve warmups"):
                    self.run_data()

    def test_missing_duplicate_or_single_process_measurements_are_rejected(self):
        original = copy.deepcopy(self.report)
        for samples in (original["Benchmarks"][0]["Measurements"][:16], original["Benchmarks"][0]["Measurements"][:23] + [original["Benchmarks"][0]["Measurements"][0]]):
            self.report = copy.deepcopy(original)
            self.report["Benchmarks"][0]["Measurements"] = samples
            self.write()
            with self.assertRaises(ValueError):
                self.run_data()

    def test_cpu_runtime_fixture_and_intrinsic_changes_require_matching_baseline(self):
        original = self.run_data()
        baseline = record(original, "synthetic")
        for field, change in (("runnerIdentity", dict(cpuIdentity="other CPU", logicalProcessors=4)), ("fixtureVersion", 2), ("fixtureSha256", "b" * 64), ("host", dict(original["host"], RuntimeVersion="other runtime"))):
            after = copy.deepcopy(original)
            after[field] = change
            with self.subTest(field=field), self.assertRaises(ValueError):
                compare(baseline, after, "synthetic")
        after = copy.deepcopy(original)
        after["cases"][METHODS[0]]["hardwareIntrinsics"] = "other"
        with self.assertRaises(ValueError):
            compare(baseline, after, "synthetic")
        with self.assertRaises(ValueError):
            compare(baseline, original, "other-runner")

    def test_incomplete_duplicate_failed_or_too_few_samples_are_rejected(self):
        original = copy.deepcopy(self.report)
        for rows in (original["Benchmarks"][:5], original["Benchmarks"][:5] + [original["Benchmarks"][0]]):
            self.report["Benchmarks"] = rows
            self.write()
            with self.assertRaises(ValueError):
                self.run_data()
        for statistic in (None, {}, dict(N=1, Mean=100), dict(N=8, Mean=0), dict(N=8, Mean=-1), dict(N=8, Mean=True), dict(N=8, Mean=float("nan"))):
            self.report = copy.deepcopy(original)
            self.report["Benchmarks"][0]["Statistics"] = statistic
            self.write()
            with self.subTest(stats=statistic), self.assertRaises(ValueError):
                self.run_data()

    def test_debugger_nonrelease_unknown_cpu_and_bad_contract_are_rejected(self):
        for field, value in (("HasAttachedDebugger", True), ("Configuration", "DEBUG"), ("HasRyuJit", False)):
            original = self.report["HostEnvironmentInfo"][field]
            self.report["HostEnvironmentInfo"][field] = value
            self.write()
            with self.assertRaises(ValueError):
                self.run_data()
            self.report["HostEnvironmentInfo"][field] = original
        for field, value in (("correctnessPassed", False), ("requiredMethods", list(METHODS[:5])), ("runnerIdentity", {}), ("fixtureSha256", "bad")):
            original = self.contract[field]
            self.contract[field] = value
            self.write()
            with self.assertRaises(ValueError):
                self.run_data()
            self.contract[field] = original

    def test_duplicate_json_fields_and_incomplete_baselines_are_rejected(self):
        baseline = record(self.run_data(), "synthetic")
        del baseline["cases"][METHODS[0]]
        with self.assertRaises(ValueError):
            compare(baseline, self.run_data(), "synthetic")
        self.contracts.write_text('{"correctnessPassed":false,"correctnessPassed":true}')
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            read_json(self.contracts)

    def test_cli_missing_baseline_returns_three_and_preserves_inputs(self):
        before = self.contracts.read_bytes()
        output = self.folder / "missing.json"
        code, result = self.cli("check", self.reports, self.contracts, "--runner", "synthetic", "--baseline", self.folder / "absent.json", "--output", output)
        self.assertEqual((3, "missing-baseline"), (code, result["status"]))
        self.assertEqual(result, read_json(output))
        self.assertEqual(before, self.contracts.read_bytes())

    def test_cli_record_compare_and_regression_return_real_process_status(self):
        baseline = self.folder / "baseline.json"
        code, result = self.cli("record", self.reports, self.contracts, "--runner", "synthetic", "--output", baseline)
        self.assertEqual(0, code)
        self.assertEqual(set(METHODS), set(result["cases"]))
        original = baseline.read_bytes()
        code, result = self.cli("check", self.reports, self.contracts, "--runner", "synthetic", "--baseline", baseline, "--output", self.folder / "pass.json")
        self.assertEqual((0, True), (code, result["passed"]))
        self.report["Benchmarks"][0]["Statistics"]["Mean"] = 121
        self.write()
        code, result = self.cli("check", self.reports, self.contracts, "--runner", "synthetic", "--baseline", baseline, "--output", self.folder / "fail.json")
        self.assertEqual((1, False), (code, result["passed"]))
        self.assertEqual(original, baseline.read_bytes())

    def test_cli_existing_outputs_and_source_paths_are_preserved(self):
        for output in (self.contracts, self.reports / "new.json"):
            before = self.contracts.read_bytes()
            code, result = self.cli("record", self.reports, self.contracts, "--runner", "synthetic", "--output", output)
            self.assertEqual((2, "invalid-input"), (code, result["status"]))
            self.assertEqual(before, self.contracts.read_bytes())
        existing = self.folder / "existing.json"
        existing.write_text("preserved")
        code, _ = self.cli("record", self.reports, self.contracts, "--runner", "synthetic", "--output", existing)
        self.assertEqual(2, code)
        self.assertEqual("preserved", existing.read_text())


if __name__ == "__main__":
    unittest.main()
