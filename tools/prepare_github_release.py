"""Collect only verified installers from one workflow run for a GitHub prerelease."""
import argparse
import json
from pathlib import Path
import re
import shutil

from release_assets import PACKAGES, sha256, verify_group


def prepare(source, output, commit, run_number, run_id, attempt, repository):
    if (not re.fullmatch(r"[0-9a-f]{40}", commit) or any(type(value) is not int or value < 1 for value in (run_number, run_id, attempt))
            or not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository)):
        raise ValueError("Invalid publication source identity")
    if output.exists():
        raise ValueError("Publication output must be new")
    selected = {}
    for directory in source.iterdir():
        platforms = [platform for platform in PACKAGES if directory.name.endswith("-" + platform)]
        if len(platforms) != 1:
            raise ValueError("Unexpected release artifact group")
        platform = platforms[0]
        identity, info = verify_group(directory, platform, commit, run_number)
        previous = selected.get(platform)
        if previous and previous[1]["runAttempt"] == identity["runAttempt"]:
            raise ValueError("Ambiguous package attempt")
        if previous is None or identity["runAttempt"] > previous[1]["runAttempt"]:
            selected[platform] = directory, identity, info
    if set(selected) != set(PACKAGES):
        raise ValueError("All Windows, Linux and Android packages must pass before publication")
    versions = {(identity["versionName"], identity["androidVersionCode"]) for _, identity, _ in selected.values()}
    if len(versions) != 1:
        raise ValueError("Package versions do not agree")
    version, _ = versions.pop()
    assets = output / "assets"
    assets.mkdir(parents=True)
    for platform, (directory, identity, _) in selected.items():
        for suffix in PACKAGES[platform]:
            name = identity["stem"] + suffix
            shutil.copyfile(directory / name, assets / name)
        shutil.copyfile(directory / "build-info.json", assets / (identity["stem"] + "-" + platform + "-build-info.json"))
    checksums = {path.name: sha256(path) for path in sorted(assets.iterdir())}
    (assets / "SHA256SUMS.txt").write_text("".join(f"{digest}  {name}\n" for name, digest in checksums.items()), encoding="utf-8")
    checksums["SHA256SUMS.txt"] = sha256(assets / "SHA256SUMS.txt")
    notes = (
        f"Build prerelease from commit `{commit}`.\n\n"
        f"[Packaging and installation checks](https://github.com/{repository}/actions/runs/{run_id}).\n\n"
        "Downloads:\n"
        "- Windows x64: installer EXE and portable ZIP.\n"
        "- Debian/Ubuntu amd64: install the `.deb` with `sudo apt install ./BlockDungeons-*-linux-amd64.deb`.\n"
        "- Android arm64: direct-install APK; AAB is intended for store tooling.\n\n"
        "Verify downloads against `SHA256SUMS.txt`. Per-platform build-info files record source and signing status.\n\n"
        "The Android APK uses the registered upload key and cannot update an installation signed by Google's Play key. "
        "Windows signing status is recorded in its build-info file. This prerelease does not establish store or physical-device acceptance.\n"
    )
    (output / "notes.md").write_text(notes, encoding="utf-8")
    manifest = {"commit": commit, "runNumber": run_number, "runId": run_id, "runAttempt": attempt, "repository": repository,
                "tag": f"build-{run_id}-{attempt}", "title": f"Block Dungeons {version} - build {run_number}.{attempt}",
                "assets": checksums}
    (output / "release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--run-number", type=int, required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--attempt", type=int, required=True)
    parser.add_argument("--repository", required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.artifacts, args.output, args.commit, args.run_number, args.run_id, args.attempt, args.repository)))


if __name__ == "__main__":
    main()
