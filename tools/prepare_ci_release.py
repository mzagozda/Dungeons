"""Stamp an ephemeral CI checkout; never allocate Play versions by editing Git history."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re


def prepare(root: Path, run_number: int, run_attempt: int, commit: str, offset: int = 1000) -> dict:
    if run_number < 1 or run_attempt < 1 or offset < 0:
        raise ValueError("Run number/attempt must be positive and version-code offset nonnegative")
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("A full lowercase Git commit SHA is required")
    metadata_path = root / "platform/store/en-US.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
    version = metadata["release"]["versionName"]
    if not re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", version):
        raise ValueError("Release version must have three numeric components")
    code = offset + run_number
    registered = json.loads((root / "platform/android/play-console.json").read_text(encoding="utf-8-sig"))
    floor = max(metadata["release"]["androidVersionCode"], registered["uploadedVersionCode"])
    if not floor < code <= 2_100_000_000:
        raise ValueError("CI Android version code must exceed configured/uploaded codes and stay within Play's limit")
    preset_path = root / "game/export_presets.cfg"
    text = preset_path.read_text(encoding="utf-8-sig")
    sections = re.split(r"(?m)(?=^\[preset\.\d+\]$)", text)
    changed = set()
    for index, section in enumerate(sections):
        name = re.search(r'^name="(Android(?: Low)?)"$', section, re.M)
        if not name:
            continue
        if name[1] in changed:
            raise ValueError("Duplicate Android export preset")
        section, count = re.subn(r"^version/code=\d+$", f"version/code={code}", section, flags=re.M)
        if count != 1 or f'version/name="{version}"' not in section:
            raise ValueError("Android preset version is missing or out of sync")
        sections[index] = section
        changed.add(name[1])
    if changed != {"Android", "Android Low"}:
        raise ValueError("Both Android presets are required")
    identity = {
        "formatVersion": 1,
        "versionName": version,
        "androidVersionCode": code,
        "runNumber": run_number,
        "runAttempt": run_attempt,
        "commit": commit,
        "stem": f"BlockDungeons-{version}-build{run_number}.{run_attempt}-{commit[:12]}",
    }
    # Validate everything before replacing either file in this disposable checkout.
    metadata["release"]["androidVersionCode"] = code
    metadata["reviewStatus"] = "pending"
    metadata["review"] = None
    preset_path.write_text("".join(sections), encoding="utf-8", newline="\n")
    metadata_path.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8", newline="\n")
    return identity


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--run-number", type=int, required=True)
    parser.add_argument("--run-attempt", type=int, required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--version-code-offset", type=int, default=1000)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    identity = prepare(args.root, args.run_number, args.run_attempt, args.commit, args.version_code_offset)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(identity, indent=2) + "\n", encoding="utf-8")
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as output:
            output.write(f"stem={identity['stem']}\n")
    print(json.dumps(identity))


if __name__ == "__main__":
    main()
