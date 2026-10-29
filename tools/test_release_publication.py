import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from prepare_github_release import prepare
from publish_github_release import publish
from release_assets import PACKAGES, sha256


class ReleasePublicationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / "downloads"
        self.source.mkdir()
        self.output = self.root / "prepared"
        self.commit = "a" * 40
        for platform in PACKAGES:
            self.group(platform)

    def group(self, platform, attempt=1, commit=None):
        commit = commit or self.commit
        stem = f"BlockDungeons-1.0.0-build7.{attempt}-{commit[:12]}"
        directory = self.source / (stem + "-" + platform)
        directory.mkdir()
        info = {"passed": True, "privateFilesRemoved": True, "identity": {"formatVersion": 1, "versionName": "1.0.0",
            "commit": commit, "runNumber": 7, "runAttempt": attempt, "androidVersionCode": 1007, "stem": stem}}
        for suffix in PACKAGES[platform]:
            (directory / (stem + suffix)).write_bytes((platform + str(attempt)).encode())
        (directory / "build-info.json").write_text(json.dumps(info))
        self.checksums(directory)
        return directory

    def checksums(self, directory):
        paths = sorted(path for path in directory.iterdir() if path.name != "SHA256SUMS.txt")
        (directory / "SHA256SUMS.txt").write_text("".join(f"{sha256(path)}  {path.name}\n" for path in paths))

    def prepare(self):
        return prepare(self.source, self.output, self.commit, 7, 1234, 1, "owner/repo")

    def test_all_platforms_have_exact_checked_assets_and_source_identity(self):
        manifest = self.prepare()
        self.assertEqual("build-1234-1", manifest["tag"])
        self.assertEqual(9, len(manifest["assets"]))
        for name, digest in manifest["assets"].items():
            self.assertEqual(digest, sha256(self.output / "assets" / name))
        self.assertIn(self.commit, (self.output / "notes.md").read_text())

    def test_missing_platform_cannot_create_a_partial_release(self):
        directory = next(path for path in self.source.iterdir() if path.name.endswith("linux-amd64"))
        for path in directory.iterdir():
            path.unlink()
        directory.rmdir()
        with self.assertRaisesRegex(ValueError, "All Windows, Linux and Android"):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_changed_package_or_extra_private_material_is_rejected_before_output(self):
        directory = next(path for path in self.source.iterdir() if path.name.endswith("android-arm64"))
        apk = next(directory.glob("*.apk"))
        original = apk.read_bytes()
        apk.write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "checksum"):
            self.prepare()
        self.assertFalse(self.output.exists())
        apk.write_bytes(original)
        (directory / "private.p12").write_bytes(b"must not publish")
        with self.assertRaisesRegex(ValueError, "Unexpected"):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_wrong_commit_and_failed_signing_cleanup_are_rejected(self):
        directory = next(path for path in self.source.iterdir() if path.name.endswith("android-arm64"))
        path = directory / "build-info.json"
        info = json.loads(path.read_text())
        info["identity"]["commit"] = "b" * 40
        info["identity"]["stem"] = info["identity"]["stem"].replace("a" * 12, "b" * 12)
        path.write_text(json.dumps(info))
        self.checksums(directory)
        with self.assertRaisesRegex(ValueError, "exact commit"):
            self.prepare()
        info["identity"]["commit"] = self.commit
        info["identity"]["stem"] = info["identity"]["stem"].replace("b" * 12, "a" * 12)
        info["privateFilesRemoved"] = False
        path.write_text(json.dumps(info))
        self.checksums(directory)
        with self.assertRaisesRegex(ValueError, "cleanup"):
            self.prepare()

    def test_rerun_chooses_latest_valid_package_attempt_for_the_same_source(self):
        self.group("android-arm64", attempt=2)
        manifest = self.prepare()
        apks = [name for name in manifest["assets"] if name.endswith(".apk")]
        self.assertEqual(1, len(apks))
        self.assertIn("build7.2-", apks[0])

    def test_unexpected_files_and_modified_prepared_assets_never_reach_github(self):
        self.prepare()
        asset = next((self.output / "assets").glob("*.deb"))
        asset.write_bytes(b"changed after validation")
        with patch("publish_github_release.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "checksum"):
                publish(self.output, "owner/repo", self.commit, 1234)
            command.assert_not_called()

    def test_release_stays_draft_when_remote_uploaded_bytes_differ(self):
        manifest = self.prepare()
        remote = {"draft": True, "prerelease": True, "target_commitish": self.commit,
                  "assets": [{"name": name, "state": "uploaded", "size": (self.output / "assets" / name).stat().st_size,
                              "digest": "sha256:" + digest} for name, digest in manifest["assets"].items()]}
        remote["assets"][0]["digest"] = "sha256:" + "0" * 64
        with patch("publish_github_release.subprocess.run") as command, patch("publish_github_release.subprocess.check_output", side_effect=[json.dumps({"databaseId": 1}), json.dumps(remote)]):
            with self.assertRaisesRegex(ValueError, "verification failed"):
                publish(self.output, "owner/repo", self.commit, 1234)
            self.assertEqual(1, command.call_count)
            self.assertIn("--draft", command.call_args.args[0])

    def test_publisher_rechecks_complete_platform_inventory(self):
        manifest = self.prepare()
        missing = next(name for name in manifest["assets"] if name.endswith(".deb"))
        del manifest["assets"][missing]
        (self.output / "assets" / missing).unlink()
        (self.output / "release-manifest.json").write_text(json.dumps(manifest))
        with patch("publish_github_release.subprocess.run") as command:
            with self.assertRaisesRegex(ValueError, "complete installer set"):
                publish(self.output, "owner/repo", self.commit, 1234)
            command.assert_not_called()

    def test_complete_remote_verified_draft_is_published_as_a_prerelease(self):
        manifest = self.prepare()
        remote = {"draft": True, "prerelease": True, "target_commitish": self.commit, "html_url": "https://github.com/owner/repo/releases/tag/build-1234-1",
                  "assets": [{"name": name, "state": "uploaded", "size": (self.output / "assets" / name).stat().st_size,
                              "digest": "sha256:" + digest} for name, digest in manifest["assets"].items()]}
        with patch("publish_github_release.subprocess.run") as command, patch("publish_github_release.subprocess.check_output", side_effect=[json.dumps({"databaseId": 1}), json.dumps(remote)]):
            publish(self.output, "owner/repo", self.commit, 1234)
            self.assertEqual(2, command.call_count)
            self.assertIn("--draft=false", command.call_args.args[0])
            self.assertIn("--prerelease", command.call_args.args[0])


if __name__ == "__main__":
    unittest.main()
