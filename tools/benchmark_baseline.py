"""Specification 20.7: complete six-workload, per-runner BenchmarkDotNet regression gate."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from decimal import Decimal
import hashlib
import json
import math
from pathlib import Path
import re

METHODS = (
    "Tick_MossyHollow_Replay", "Tick_Stress_64Monsters_32Arrows", "AStar_256_Open",
    "AStar_256_Maze", "ChunkMesher_256x256", "LevelLoad_256x256",
)
HOST_FIELDS = ("BenchmarkDotNetVersion", "OsVersion", "ProcessorName", "RuntimeVersion", "Architecture", "DotNetCliVersion", "Configuration")
REPORT = "Dungeons.Benchmarks.NightlyBenchmarks-report-full-compressed.json"
NAME = re.compile(r"[a-z][a-z0-9-]{0,63}\Z")
MAX_TICK_NS = 2_000_000  # Desktop simulation budget. Physical/mobile acceptance is separate.


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON property: " + key)
        result[key] = value
    return result


def read_json(path):
    if path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("Benchmark JSON exceeds 64 MiB")
    def reject_constant(value):
        raise ValueError("Non-finite JSON number: " + value)
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object, parse_constant=reject_constant)


def number(value, positive=True):
    if type(value) not in (int, float) or not math.isfinite(value) or (value <= 0 if positive else value < 0):
        raise ValueError("Invalid benchmark statistic")
    return value


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def load_run(reports: Path, contracts: Path) -> dict:
    contract = read_json(contracts)
    if not isinstance(contract, dict) or type(contract.get("formatVersion")) is not int or contract["formatVersion"] != 1 or contract.get("correctnessPassed") is not True or contract.get("requiredMethods") != list(METHODS):
        raise ValueError("A complete passing workload contract is required")
    if type(contract.get("fixtureVersion")) is not int or contract["fixtureVersion"] < 1 or not isinstance(contract.get("fixtureSha256"), str) or not re.fullmatch("[0-9a-f]{64}", contract["fixtureSha256"]):
        raise ValueError("Missing benchmark fixture identity")
    runner = contract.get("runnerIdentity")
    if not isinstance(runner, dict) or not isinstance(runner.get("cpuIdentity"), str) or not runner["cpuIdentity"].strip() or type(runner.get("logicalProcessors")) is not int or runner["logicalProcessors"] < 1:
        raise ValueError("Actual CPU identity and logical processor count are required")
    path = reports / REPORT
    report = read_json(path)
    if not isinstance(report, dict):
        raise ValueError("Invalid BenchmarkDotNet report")
    host = report.get("HostEnvironmentInfo")
    if not isinstance(host, dict) or any(not isinstance(host.get(key), str) or not host[key].strip() for key in HOST_FIELDS) or host.get("HasAttachedDebugger") is not False or host.get("HasRyuJit") is not True or host["Configuration"].upper() != "RELEASE":
        raise ValueError("A Release measurement without debugger and with recorded host details is required")
    rows = report.get("Benchmarks")
    if not isinstance(rows, list) or len(rows) != len(METHODS) or any(not isinstance(row, dict) for row in rows) or {row.get("Method") for row in rows} != set(METHODS):
        raise ValueError("Exactly the six required BenchmarkDotNet workloads must have results")
    cases = {}
    for row in rows:
        name = row["Method"]
        if row.get("Namespace") != "Dungeons.Benchmarks" or row.get("Type") != "NightlyBenchmarks" or row.get("Parameters") != "" or row.get("FullName") != "Dungeons.Benchmarks.NightlyBenchmarks." + name:
            raise ValueError("Unexpected benchmark workload or parameters")
        display = row.get("DisplayInfo")
        prefix = "NightlyBenchmarks." + name + ": "
        if not isinstance(display, str) or not display.startswith(prefix) or not display.removeprefix(prefix).strip():
            raise ValueError("Benchmark job configuration is unavailable")
        job = display.removeprefix(prefix)
        # BenchmarkDotNet's generated Job-XXXXXX label changes between runs.
        # Preserve all explicit settings, which appear in the parenthesised descriptor.
        if "(" not in job or not job.endswith(")"):
            raise ValueError("Explicit benchmark job settings are required")
        job = job.split("(", 1)[1][:-1]
        if not job.strip():
            raise ValueError("Benchmark job settings are empty")
        settings = set(job.split(", "))
        if not {"LaunchCount=3", "WarmupCount=12", "IterationCount=8", "OutlierMode=DontRemove"}.issubset(settings):
            raise ValueError("Three launches, twelve warmups, eight iterations and all samples are required")
        stats = row.get("Statistics")
        if not isinstance(stats, dict) or type(stats.get("N")) is not int or stats["N"] != 24:
            raise ValueError("All 24 successful measured samples are required")
        measurements = row.get("Measurements")
        if not isinstance(measurements, list) or any(not isinstance(item, dict) for item in measurements):
            raise ValueError("Per-launch measurements are required")
        measured = [item for item in measurements if item.get("IterationMode") == "Workload" and item.get("IterationStage") == "Result"]
        if len(measured) != 24 or any(type(item.get("LaunchIndex")) is not int or type(item.get("IterationIndex")) is not int for item in measured) or {(item["LaunchIndex"], item["IterationIndex"]) for item in measured} != {(launch, iteration) for launch in range(1, 4) for iteration in range(1, 9)}:
            raise ValueError("Each of the three processes must provide all eight measurements")
        memory = row.get("Memory")
        if not isinstance(memory, dict):
            raise ValueError("MemoryDiagnoser results are required")
        cases[name] = {
            "meanNs": number(stats.get("Mean")), "samples": stats["N"],
            "measuredLaunches": 3,
            "allocatedBytes": number(memory.get("BytesAllocatedPerOperation"), positive=False),
            "job": job, "hardwareIntrinsics": row.get("HardwareIntrinsics"),
        }
        if not isinstance(cases[name]["hardwareIntrinsics"], str):
            raise ValueError("Hardware intrinsic context is unavailable")
    return {
        "fixtureVersion": contract["fixtureVersion"], "fixtureSha256": contract["fixtureSha256"],
        "runnerIdentity": runner, "host": {key: host[key] for key in HOST_FIELDS}, "cases": cases,
        "reportSha256": sha(path), "contractSha256": sha(contracts),
    }


def absolute_budgets(run):
    return {name: run["cases"][name]["meanNs"] <= MAX_TICK_NS for name in METHODS[:2]}


def record(run, runner):
    budgets = absolute_budgets(run)
    if not all(budgets.values()):
        raise ValueError("A desktop tick workload exceeds the absolute 2 ms budget")
    return {"formatVersion": 1, "runnerId": runner, "recordedUtc": datetime.now(timezone.utc).isoformat(), "maximumRegression": .2, **run}


def compare(expected, current, runner):
    if not isinstance(expected, dict) or type(expected.get("formatVersion")) is not int or expected["formatVersion"] != 1 or expected.get("runnerId") != runner or expected.get("maximumRegression") != .2:
        raise ValueError("Baseline version/runner/threshold differs")
    for key in ("fixtureVersion", "fixtureSha256", "runnerIdentity", "host"):
        if expected.get(key) != current[key]:
            raise ValueError("Baseline context differs: " + key)
    cases = expected.get("cases")
    if not isinstance(cases, dict) or set(cases) != set(METHODS):
        raise ValueError("Baseline must contain all six workloads")
    budgets = absolute_budgets(current)
    results = []
    for name in METHODS:
        before, after = cases[name], current["cases"][name]
        if not isinstance(before, dict) or any(before.get(key) != after[key] for key in ("job", "hardwareIntrinsics")):
            raise ValueError("Benchmark job/intrinsics changed: " + name)
        old_mean = number(before.get("meanNs"))
        passed = Decimal(str(after["meanNs"])) <= Decimal(str(old_mean)) * Decimal("1.2")
        results.append({"method": name, "passed": passed and budgets.get(name, True), "baselineMeanNs": old_mean, "actualMeanNs": after["meanNs"], "changeFraction": after["meanNs"] / old_mean - 1, "allocatedBytes": after["allocatedBytes"], "samples": after["samples"]})
    return {"formatVersion": 1, "runnerId": runner, "status": "compared", "passed": all(item["passed"] for item in results), "maximumRegression": .2, "desktopTickBudgetNs": MAX_TICK_NS, "absoluteBudgetsPassed": budgets, "cases": results, "physicalAcceptance": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("record", "check"))
    parser.add_argument("reports", type=Path)
    parser.add_argument("contracts", type=Path)
    parser.add_argument("--runner", required=True)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    owned = None
    try:
        if not NAME.fullmatch(args.runner) or args.command == "check" and args.baseline is None:
            raise ValueError("A valid runner id and check baseline are required")
        output = args.output.resolve()
        if output.is_relative_to(args.reports.resolve()) or output == args.contracts.resolve() or args.baseline and output == args.baseline.resolve():
            raise ValueError("Output must be outside the source reports, contracts and baseline")
        # Claim the output exclusively before any writing, preserving existing files/hardlinks.
        output.parent.mkdir(parents=True, exist_ok=True)
        owned = output.open("x", encoding="utf-8")
        run = load_run(args.reports, args.contracts)
        if args.command == "record":
            result, code = record(run, args.runner), 0
        elif not args.baseline.is_file():
            result, code = {"passed": False, "status": "missing-baseline", "runnerId": args.runner, "physicalAcceptance": False}, 3
        else:
            result = compare(read_json(args.baseline), run, args.runner)
            code = 0 if result["passed"] else 1
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        result, code = {"passed": False, "status": "invalid-input", "message": str(error)}, 2
    if owned:
        with owned:
            owned.write(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
