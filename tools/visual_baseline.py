"""Prepare unreviewed visual candidates, or compare captures with an approved profile."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import shutil

from PIL import Image
from compare_images import compare

IDENTITY_FIELDS = (
    "scenario", "width", "height", "godot", "renderer", "adapter", "quality",
    "shaderTime", "visualStepSeconds", "levelHash", "contentHash", "seed", "tick", "finalHash",
)
NAME = re.compile(r"[a-z][a-z0-9-]{0,63}\Z")


class PendingReview(Exception):
    pass


def load_json(path: Path):
    if path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("Visual metadata exceeds 8 MiB")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path: Path) -> str:
    if path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("Visual file exceeds 64 MiB")
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def safe_file(root: Path, relative: str) -> Path:
    if not isinstance(relative, str) or "\\" in relative or relative.startswith("/") or ":" in relative or any(part in ("", ".", "..") for part in relative.split("/")):
        raise ValueError("Unsafe visual file path")
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()) or not path.is_file():
        raise ValueError("Missing or escaping visual file: " + relative)
    return path


def metadata(path: Path, image: Path) -> dict:
    record = load_json(path)
    if not isinstance(record, dict) or type(record.get("formatVersion")) is not int or record["formatVersion"] != 1 or record.get("passed") is not True:
        raise ValueError("Invalid capture metadata")
    if any(key not in record for key in IDENTITY_FIELDS) or not isinstance(record["scenario"], str) or not NAME.fullmatch(record["scenario"]):
        raise ValueError("Capture identity is incomplete")
    if any(type(record[key]) is not int or record[key] <= 0 for key in ("width", "height")) or type(record["tick"]) is not int or record["tick"] < 0 or type(record["seed"]) is not int or not 0 <= record["seed"] <= 2**64 - 1:
        raise ValueError("Invalid capture dimensions/tick/seed")
    if any(not isinstance(record[key], str) or not record[key].strip() for key in ("godot", "renderer", "adapter", "quality")) or type(record["shaderTime"]) not in (float, int) or record["shaderTime"] != 0 or type(record["visualStepSeconds"]) not in (float, int) or not math.isfinite(record["visualStepSeconds"]) or not 0 < record["visualStepSeconds"] <= 1:
        raise ValueError("Invalid capture render context")
    if any(not isinstance(record[key], str) or not re.fullmatch(r"[0-9a-f]{" + str(length) + "}", record[key]) for key, length in (("levelHash", 64), ("contentHash", 64), ("finalHash", 16))):
        raise ValueError("Invalid capture source/state fingerprints")
    if record.get("baselineApproved") is not False or record.get("physicalAcceptance") is not False:
        raise ValueError("Raw capture metadata must not claim approval or device acceptance")
    if record.get("imageSha256") != digest(image):
        raise ValueError("Capture image hash differs")
    with Image.open(image) as pixels:
        if pixels.format != "PNG" or pixels.size != (record["width"], record["height"]):
            raise ValueError("Capture image dimensions/format differ")
        if pixels.width <= 0 or pixels.height <= 0 or pixels.width * pixels.height > 4096 * 4096:
            raise ValueError("Capture image exceeds its pixel budget")
    return record


def capture_set(directory: Path) -> dict[str, tuple[dict, Path, Path]]:
    result = load_json(directory / "result.json")
    if not isinstance(result, dict) or result.get("passed") is not True or not isinstance(result.get("captures"), list):
        raise ValueError("Capture run did not pass")
    catalogs = [line.removeprefix("SCREENSHOT_SCENARIOS=") for line in (directory / "catalog.log").read_text(encoding="utf-8-sig").splitlines() if line.startswith("SCREENSHOT_SCENARIOS=")]
    if len(catalogs) != 1:
        raise ValueError("Exactly one executable scenario catalogue is required")
    catalog = json.loads(catalogs[0])
    if not isinstance(catalog, list) or any(not isinstance(item, dict) for item in catalog):
        raise ValueError("Invalid executable scenario catalogue")
    required = [item["id"] for item in catalog]
    if not required or len(set(required)) != len(required) or any(not isinstance(name, str) or not NAME.fullmatch(name) for name in required):
        raise ValueError("Invalid executable scenario catalogue")
    rows = result["captures"]
    if any(not isinstance(row, dict) or not isinstance(row.get("scenario"), str) for row in rows) or {row["scenario"] for row in rows} != set(required) or type(result.get("scenarios")) is not int or result["scenarios"] != len(required):
        raise ValueError("Baseline capture must cover every executable scenario")
    captures = {}
    for name in required:
        image = safe_file(directory, name + "-1.png")
        sidecar = safe_file(directory, name + "-1.capture.json")
        record = metadata(sidecar, image)
        matching = [row for row in rows if row["scenario"] == name]
        if record != matching[0] or any(any(row.get(field) != record[field] for field in IDENTITY_FIELDS) for row in matching):
            raise ValueError("Run/sidecar provenance differs: " + name)
        if record["renderer"] != result.get("renderer"):
            raise ValueError("Capture backend differs from its run")
        captures[name] = record, image, sidecar
    return captures


def prepare(captures: Path, destination: Path, profile: str) -> dict:
    if not NAME.fullmatch(profile):
        raise ValueError("Profile id must use lowercase letters, digits and hyphens")
    if destination.resolve().is_relative_to(captures.resolve()):
        raise ValueError("Candidate destination must be outside the source capture run")
    source = capture_set(captures)
    destination.mkdir(parents=True, exist_ok=False)
    (destination / "cases").mkdir()
    cases = []
    for name, (_, image, sidecar) in source.items():
        image_name, metadata_name = f"cases/{name}.png", f"cases/{name}.capture.json"
        shutil.copyfile(image, destination / image_name)
        shutil.copyfile(sidecar, destination / metadata_name)
        cases.append({"scenario": name, "image": image_name, "capture": metadata_name, "imageSha256": digest(image), "captureSha256": digest(sidecar)})
    manifest = {
        "formatVersion": 1, "profileId": profile, "reviewStatus": "pending", "review": None,
        "preparedUtc": datetime.now(timezone.utc).isoformat(), "cases": cases,
    }
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def check(baseline: Path, captures: Path, profile: str, output: Path) -> dict:
    if not NAME.fullmatch(profile):
        raise ValueError("Invalid visual profile id")
    if output.resolve().is_relative_to(baseline.resolve()) or output.resolve().is_relative_to(captures.resolve()):
        raise ValueError("Comparison output must be outside the read-only baseline and capture runs")
    if not output.is_dir():
        raise ValueError("Comparison output directory is unavailable")
    manifest_path = baseline / "manifest.json"
    if not manifest_path.is_file():
        raise PendingReview("Approved baseline profile is unavailable: " + profile)
    manifest = load_json(manifest_path)
    if not isinstance(manifest, dict) or type(manifest.get("formatVersion")) is not int or manifest["formatVersion"] != 1 or manifest.get("profileId") != profile:
        raise ValueError("Baseline version/profile differs")
    if manifest.get("reviewStatus") != "approved":
        raise PendingReview("Visual baseline review is pending: " + profile)
    review = manifest.get("review")
    if not isinstance(review, dict) or any(not isinstance(review.get(key), str) or not review[key].strip() for key in ("reviewer", "approvedUtc", "reference")):
        raise PendingReview("Baseline has no complete human review record")
    approved = datetime.fromisoformat(review["approvedUtc"].replace("Z", "+00:00"))
    if approved.tzinfo is None or approved.utcoffset().total_seconds() != 0:
        raise ValueError("Review time must include UTC")
    actual = capture_set(captures)
    cases = manifest.get("cases")
    if not isinstance(cases, list) or any(not isinstance(case, dict) for case in cases):
        raise ValueError("Invalid baseline case list")
    names = [case.get("scenario") for case in cases]
    if len(set(names)) != len(names) or set(names) != set(actual):
        raise ValueError("Baseline must cover the exact executable scenario catalogue")
    results = []
    for case in cases:
        name = case["scenario"]
        if case.get("image") != f"cases/{name}.png" or case.get("capture") != f"cases/{name}.capture.json":
            raise ValueError("Baseline case paths differ from their scenario")
        expected_image = safe_file(baseline, case["image"])
        expected_metadata = safe_file(baseline, case["capture"])
        if digest(expected_image) != case.get("imageSha256") or digest(expected_metadata) != case.get("captureSha256"):
            raise ValueError("Reviewed baseline files changed: " + name)
        expected = metadata(expected_metadata, expected_image)
        current, current_image, _ = actual[name]
        changed = [field for field in IDENTITY_FIELDS if expected[field] != current[field]]
        with Image.open(expected_image) as left, Image.open(current_image) as right:
            pixels, mismatch = compare(left, right)
            if mismatch is not None:
                import numpy as np
                heatmap = np.asarray(right.convert("RGB"), dtype=np.uint8) // 4
                heatmap[mismatch] = [255, 32, 64]
                Image.fromarray(heatmap).save(output / (name + ".diff.png"))
        results.append({"scenario": name, "passed": not changed and pixels["passed"], "changedIdentityFields": changed, "pixels": pixels})
    return {"formatVersion": 1, "profileId": profile, "passed": all(case["passed"] for case in results), "status": "compared", "review": review, "cases": results, "physicalAcceptance": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    export = commands.add_parser("prepare")
    export.add_argument("captures", type=Path)
    export.add_argument("destination", type=Path)
    export.add_argument("--profile", required=True)
    verify = commands.add_parser("check")
    verify.add_argument("baseline", type=Path)
    verify.add_argument("captures", type=Path)
    verify.add_argument("--profile", required=True)
    verify.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    owns_output = False
    try:
        if args.command == "prepare":
            manifest = prepare(args.captures, args.destination, args.profile)
            print(json.dumps({"prepared": True, "reviewStatus": manifest["reviewStatus"], "cases": len(manifest["cases"])}))
            return 0
        if args.output.resolve().is_relative_to(args.baseline.resolve()) or args.output.resolve().is_relative_to(args.captures.resolve()):
            raise ValueError("Comparison output must be outside the read-only baseline and capture runs")
        args.output.mkdir(parents=True, exist_ok=False)
        owns_output = True
        result = check(args.baseline, args.captures, args.profile, args.output)
        code = 0 if result["passed"] else 1
    except PendingReview as error:
        result, code = {"passed": False, "status": "pending-review", "message": str(error)}, 3
    except (OSError, ValueError, TypeError, KeyError, Image.DecompressionBombError) as error:
        result, code = {"passed": False, "status": "invalid-input", "message": str(error)}, 2
    if owns_output:
        with (args.output / "result.json").open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
