"""Validate reviewed replay bytes/identities; simulation verification is done by the Core CLI."""
import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path
import re

REQUIRED = {"mossy_hollow_full_clear.dreplay", "mossy_hollow_ambush_only.dreplay", "mossy_hollow_death_respawn.dreplay"}
FIELDS = ("levelId", "tickCount", "seed", "initialHash", "finalHash", "levelHash", "contentHash")


class PendingReview(ValueError):
    pass


def pairs(items):
    result = {}
    for key, value in items:
        if key in result:
            raise ValueError("Duplicate JSON key: " + key)
        result[key] = value
    return result


def bounded_bytes(path, maximum):
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    if len(data) > maximum:
        raise ValueError("Baseline input exceeds its size bound")
    return data


def decode(data):
    return json.loads(data.decode("utf-8"), object_pairs_hook=pairs, parse_constant=lambda value: (_ for _ in ()).throw(ValueError("Non-finite JSON number")))


def load(path, maximum):
    return decode(bounded_bytes(path, maximum))


def check(directory):
    manifest_path = directory / "manifest.json"
    if not manifest_path.is_file():
        raise PendingReview("Reviewed replay baseline has not been established")
    manifest = load(manifest_path, 256 * 1024)
    if not isinstance(manifest, dict) or type(manifest.get("formatVersion")) is not int or manifest.get("formatVersion") != 1:
        raise ValueError("Unsupported replay baseline manifest")
    if manifest.get("reviewStatus") != "approved" or manifest.get("reviewedGolden") is not True:
        raise PendingReview("Replay baseline requires explicit reviewer approval")
    review = manifest.get("review")
    if not isinstance(review, dict) or any(not isinstance(review.get(key), str) or not review[key].strip() for key in ("reviewer", "approvedUtc", "reference", "scope")):
        raise ValueError("Missing reviewer identity, timestamp, reference or scope")
    if datetime.fromisoformat(review["approvedUtc"].replace("Z", "+00:00")).tzinfo is None:
        raise ValueError("Review timestamp requires a timezone")
    cases = manifest.get("candidates")
    if not isinstance(cases, list) or len(cases) != len(REQUIRED) or any(not isinstance(item, dict) for item in cases):
        raise ValueError("Expected all three reviewed gameplay cases")
    names = [item.get("name") for item in cases]
    if any(not isinstance(name, str) for name in names) or set(names) != REQUIRED or len(set(names)) != len(names):
        raise ValueError("Missing, duplicate or unsafe replay case name")
    if {path.name for path in directory.glob("*.dreplay")} != REQUIRED:
        raise ValueError("Baseline directory contains missing/extra replay files")
    result = []
    for case in cases:
        path = directory / case["name"]
        if path.is_symlink() or not path.is_file():
            raise ValueError("Replay must be a regular file")
        data = bounded_bytes(path, 4 * 1024 * 1024)
        sha = hashlib.sha256(data).hexdigest()
        if not isinstance(case.get("fileSha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", case["fileSha256"]) or sha != case["fileSha256"]:
            raise ValueError("Reviewed replay bytes changed: " + case["name"])
        replay = decode(data)
        if not isinstance(replay, dict) or any(key not in case or case[key] != replay.get(key) for key in FIELDS):
            raise ValueError("Replay identity differs from its reviewed manifest")
        ticks, hashes = replay.get("tickCount"), replay.get("tickHashes")
        if type(ticks) is not int or ticks <= 0 or not isinstance(hashes, list) or len(hashes) != ticks or hashes[-1] != replay.get("finalHash") or any(not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{16}", value) for value in hashes):
            raise ValueError("Reviewed replay lacks complete per-tick hashes")
        result.append(case)
    return {"passed": True, "reviewedBytesPassed": True, "simulationVerified": False, "physicalAcceptance": False, "review": review, "cases": result}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists() or args.output.resolve().is_relative_to(args.directory.resolve()):
        parser.error("Output must be new and outside the reviewed baseline")
    code = 0
    try:
        result = check(args.directory)
    except PendingReview as error:
        code, result = 3, {"passed": False, "status": "pending-review", "error": str(error), "physicalAcceptance": False}
    except (OSError, ValueError, TypeError, KeyError, UnicodeError) as error:
        code, result = 1, {"passed": False, "status": "invalid", "error": str(error), "physicalAcceptance": False}
    with args.output.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(result, output, indent=2)
        output.write("\n")
    print("REPLAY_BASELINE=" + ("PASS" if code == 0 else result["status"]))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
