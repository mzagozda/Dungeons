"""Synthetic automatic-signing plan contracts; no credentials or Apple account is contacted."""
import copy
import json
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
import unittest

from prepare_ios_signing import plan

TEAM = "ABCDE12345"
KEY_ID = "KEY1234567"
ISSUER = "11111111-2222-3333-4444-555555555555"
SCRIPT = Path(__file__).with_name("prepare_ios_signing.py")


class IosSigningTests(unittest.TestCase):
    def setUp(self):
        self.exported = {"passed": True, "projectExportPassed": True, "unsignedArchiveBuilt": True, "archivePrerequisitesPassed": True, "releaseTemplate": True,
                         "appleTeamId": TEAM, "projectPbxprojSha256": "ab" * 32}

    def test_plan_is_automatic_export_only_and_preserves_release_version(self):
        result = plan(TEAM, KEY_ID, ISSUER, self.exported)
        self.assertEqual("automatic", result["signingStyle"])
        self.assertEqual("export", result["destination"])
        self.assertEqual("app-store-connect", result["method"])
        self.assertFalse(result["manageAppVersionAndBuildNumber"])
        self.assertNotIn("upload", result.values())

    def test_all_actual_unsigned_gates_must_pass_before_signing(self):
        for field in ("passed", "projectExportPassed", "unsignedArchiveBuilt", "archivePrerequisitesPassed", "releaseTemplate"):
            exported = copy.deepcopy(self.exported)
            exported[field] = False
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "actual unsigned"):
                plan(TEAM, KEY_ID, ISSUER, exported)

    def test_team_and_exported_project_fingerprint_are_required(self):
        for field, value in (("appleTeamId", "OTHER12345"), ("projectPbxprojSha256", None), ("projectPbxprojSha256", "bad")):
            exported = copy.deepcopy(self.exported)
            exported[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                plan(TEAM, KEY_ID, ISSUER, exported)

    def test_malformed_public_identifiers_are_rejected_without_any_secret(self):
        for team, key, issuer in (("guess", KEY_ID, ISSUER), (TEAM, "missing", ISSUER), (TEAM, KEY_ID, "not-a-uuid")):
            with self.subTest(team=team, key=key, issuer=issuer), self.assertRaises(ValueError):
                plan(team, key, issuer, self.exported)

    def test_cli_outputs_real_plist_and_preserves_existing_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "result.json"
            source.write_text(json.dumps(self.exported), encoding="utf-8")
            original = source.read_bytes()
            output = root / "ExportOptions.plist"
            command = [sys.executable, str(SCRIPT), str(source), "--team", TEAM, "--key-id", KEY_ID, "--issuer", ISSUER, "--output", str(output)]
            self.assertEqual(0, subprocess.run(command, capture_output=True, check=False).returncode)
            self.assertEqual("export", plistlib.loads(output.read_bytes())["destination"])
            previous = output.read_bytes()
            self.assertEqual(2, subprocess.run(command, capture_output=True, check=False).returncode)
            self.assertEqual(previous, output.read_bytes())
            self.assertEqual(original, source.read_bytes())

    def test_cli_rejects_unvalidated_export_before_writing_options(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "result.json"
            source.write_text(json.dumps({**self.exported, "archivePrerequisitesPassed": False}), encoding="utf-8")
            output = root / "ExportOptions.plist"
            command = [sys.executable, str(SCRIPT), str(source), "--team", TEAM, "--key-id", KEY_ID, "--issuer", ISSUER, "--output", str(output)]
            self.assertEqual(1, subprocess.run(command, capture_output=True, check=False).returncode)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
