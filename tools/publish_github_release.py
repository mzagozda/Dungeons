"""Publish an exact, validated installer set; failed uploads remain private drafts."""
import argparse
import json
from pathlib import Path
import re
import subprocess

from release_assets import PACKAGES, read_identity, sha256


def publish(prepared, repository, commit, run_id):
    manifest = json.loads((prepared / "release-manifest.json").read_text(encoding="utf-8"))
    if (manifest["repository"] != repository or manifest["commit"] != commit or manifest["runId"] != run_id
            or not re.fullmatch(r"[0-9a-f]{40}", commit) or not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository)
            or manifest["tag"] != f"build-{run_id}-{manifest['runAttempt']}"):
        raise ValueError("Publication manifest differs from the current workflow source")
    assets = prepared / "assets"
    if {path.name for path in assets.iterdir()} != set(manifest["assets"]):
        raise ValueError("Publication assets differ from the validated manifest")
    for name, digest in manifest["assets"].items():
        path = assets / name
        if not re.fullmatch(r"[A-Za-z0-9._-]+", name) or path.is_symlink() or not path.is_file() or sha256(path) != digest:
            raise ValueError("Publication asset checksum mismatch")
    expected = {"SHA256SUMS.txt"}
    for platform, suffixes in PACKAGES.items():
        reports = list(assets.glob("*-" + platform + "-build-info.json"))
        if len(reports) != 1:
            raise ValueError("Publication requires one build report for every platform")
        info = json.loads(reports[0].read_text(encoding="utf-8-sig"))
        identity = read_identity(info["identity"])
        if (identity["commit"] != commit or identity["runNumber"] != manifest["runNumber"] or info.get("passed") is not True
                or platform == "android-arm64" and info.get("privateFilesRemoved") is not True):
            raise ValueError("Publication build report does not prove a successful matching package")
        expected.add(identity["stem"] + "-" + platform + "-build-info.json")
        expected.update(identity["stem"] + suffix for suffix in suffixes)
    if set(manifest["assets"]) != expected:
        raise ValueError("Publication requires the complete installer set")
    subprocess.run(["gh", "release", "create", manifest["tag"], "--repo", repository, "--target", commit,
                    "--title", manifest["title"], "--notes-file", str(prepared / "notes.md"), "--draft", "--prerelease", "--latest=false",
                    *[str(assets / name) for name in manifest["assets"]]], check=True)
    release_id = json.loads(subprocess.check_output(["gh", "release", "view", manifest["tag"], "--repo", repository, "--json", "databaseId"], text=True))["databaseId"]
    release = json.loads(subprocess.check_output(["gh", "api", f"repos/{repository}/releases/{release_id}"], text=True))
    if not release["draft"] or not release["prerelease"] or release["target_commitish"] != commit:
        raise ValueError("Draft release differs from the validated source; draft retained")
    remote = {asset["name"]: asset for asset in release["assets"]}
    if set(remote) != set(manifest["assets"]):
        raise ValueError("Uploaded release asset inventory differs; draft retained")
    for name, digest in manifest["assets"].items():
        if remote[name]["state"] != "uploaded" or remote[name]["size"] != (assets / name).stat().st_size or remote[name].get("digest") != "sha256:" + digest:
            raise ValueError("Uploaded release asset verification failed; draft retained")
    subprocess.run(["gh", "release", "edit", manifest["tag"], "--repo", repository, "--draft=false", "--prerelease", "--latest=false"], check=True)
    print(release["html_url"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepared", type=Path, required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    args = parser.parse_args()
    publish(args.prepared, args.repository, args.commit, args.run_id)


if __name__ == "__main__":
    main()
