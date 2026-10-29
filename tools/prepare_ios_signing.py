"""Prepare an export-only automatic-signing plan; never upload or invent an Apple identity."""
import argparse
import json
from pathlib import Path
import plistlib
import re
import uuid


def plan(team, key_id, issuer, exported):
    if not re.fullmatch(r"[A-Z0-9]{10}", team) or not re.fullmatch(r"[A-Z0-9]{10}", key_id):
        raise ValueError("Actual ten-character Apple team and API key identifiers are required")
    if str(uuid.UUID(issuer)).lower() != issuer.lower():
        raise ValueError("Actual API issuer UUID is required")
    if not isinstance(exported, dict) or any(exported.get(key) is not True for key in ("passed", "projectExportPassed", "unsignedArchiveBuilt", "archivePrerequisitesPassed", "releaseTemplate")) or exported.get("appleTeamId") != team:
        raise ValueError("A matching successful actual unsigned project/archive audit is required first")
    if not isinstance(exported.get("projectPbxprojSha256"), str) or not re.fullmatch(r"[0-9a-fA-F]{64}", exported["projectPbxprojSha256"]):
        raise ValueError("Exported project fingerprint is missing; repeat project export with the current helper")
    return {"method": "app-store-connect", "destination": "export", "signingStyle": "automatic", "teamID": team,
            "manageAppVersionAndBuildNumber": False, "uploadSymbols": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export_result", type=Path)
    parser.add_argument("--team", required=True)
    parser.add_argument("--key-id", required=True)
    parser.add_argument("--issuer", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Signing export options must be new")
    try:
        options = plan(args.team, args.key_id, args.issuer, json.loads(args.export_result.read_text(encoding="utf-8-sig")))
        with args.output.open("xb") as stream:
            plistlib.dump(options, stream)
    except (OSError, ValueError, TypeError) as error:
        parser.exit(1, str(error) + "\n")
    print("IOS_SIGNING_PLAN=PASS destination=export; actual signing remains to be executed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
