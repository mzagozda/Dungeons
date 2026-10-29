# Per-push installation downloads and GitHub prereleases

`.github/workflows/release-artifacts.yml` builds on every push to `main`. Pull requests validate Windows/Linux packaging without publishing or receiving signing secrets. Manual runs may validate a branch's full package set, including registered-key Android signing; only `main` publishes. A multi-commit push builds its head. A later push does not cancel an earlier build. Existing PR/debug and nightly workflows remain independent; a successful package job does not establish that all other CI or platform acceptance gates passed.

Successful jobs retain these **30-day** Actions downloads; selected diagnostics have **14-day** retention:

| Download group | Deployable files |
| --- | --- |
| `BlockDungeons-<version>-build<run>.<attempt>-<sha12>-windows-x64` | `…-windows-x64-setup.exe`, `…-windows-x64-portable.zip` |
| `BlockDungeons-<version>-build<run>.<attempt>-<sha12>-linux-amd64` | `…-linux-amd64.deb` |
| `BlockDungeons-<version>-build<run>.<attempt>-<sha12>-android-arm64` | `…-android-arm64.apk`, `…-android-arm64.aab` |

Each group includes `build-info.json` and `SHA256SUMS.txt`. GitHub wraps Actions downloads in a ZIP: extract that before opening the installer/APK. Public GitHub Release assets are direct files. PDB debug symbols and logs are excluded from desktop distributions.

After Windows, Linux and Android pass, the release-bundle job verifies every package checksum, exact source commit, workflow run, version and signing cleanup. It rejects missing platforms, unexpected/private files and ambiguous package attempts. When jobs are rerun, the newest valid attempt for each platform is selected only from the same commit/run/version. Per-platform identities retain the actual build attempts.

The main-only publishing job is the only job granted `contents: write`. It creates a draft prerelease tagged `build-<workflow-run-id>-<publication-attempt>` against the full commit SHA, uploads five installation/package files plus three platform build reports and a combined `SHA256SUMS.txt`, verifies GitHub's uploaded names, sizes and SHA-256 digests, then publishes. A failed upload leaves a private draft. A failed platform job produces no public partial release. Build prereleases do not replace a stable release or claim store/physical-device acceptance. No Google Play upload or rollout occurs.

For a failed publication, rerun the workflow's complete package/preparation chain to create a new publication attempt. Existing public releases/assets are never overwritten. Earlier interrupted drafts may be reviewed and removed separately.

## Signing secrets

Configure under **Repository → Settings → Secrets and variables → Actions → Repository secrets**, or grant the repository access to equivalent organization secrets.

| Secret | Required | Value |
| --- | --- | --- |
| `ANDROID_UPLOAD_KEYSTORE_BASE64` | Android | Base64 of the **existing** `block-dungeons-upload.p12` binary, without line wrapping |
| `ANDROID_UPLOAD_KEY_ALIAS` | Android | `block-dungeons-upload` |
| `ANDROID_UPLOAD_KEYSTORE_PASSWORD` | Android | Existing decrypted keystore/private-key password; this profile uses the same password for both |
| `WINDOWS_SIGNING_PFX_BASE64` | Optional Windows signing | Base64 of an exportable Authenticode code-signing PFX containing its private key and certificate chain |
| `WINDOWS_SIGNING_PFX_PASSWORD` | With Windows PFX | Password protecting that PFX |

The registered Android key already exists in `%LOCALAPPDATA%\Taskscape\BlockDungeons\Signing\`. **Reuse it; do not generate a replacement.** `upload-password.dpapi` is encrypted for this Windows user; those file bytes are not the CI password. Google's managed private app-signing key is not downloadable and is not needed. The committed public upload certificate independently determines the expected AAB/APK signing identity.

The direct-download APK uses the upload key. It **cannot update an installation signed by Google's different Play app-signing key**. Use the direct APK and Play distribution as separate installation channels. Offline APK checks do not prove Google-delivered APK/split validation.

Missing Android secrets fail preflight; there is no debug-key fallback. Private signing files live in a restricted runner-temp directory, are removed in `finally`, and are excluded from uploads. Diagnostic text is sanitized before retention.

Windows builds **unsigned** when both optional secrets are absent. If either is configured, both are required. Signing covers the game EXE, installer and Inno uninstaller, uses SHA-256/RFC 3161 timestamping, and fails packaging if signature/trust verification fails. The imported certificate/private key is removed after use. No trusted Windows signing identity has been supplied or validated here. For a hardware-backed/non-exportable certificate or cloud signing service, use the provider's CI integration instead of this PFX route; do not invent/export a replacement key. Unsigned output is labelled `authenticodeSigned: false`.

No Google Play service-account JSON, console login, Apple credentials or additional GitHub token is required to build/download these four files.

## Provision the existing Android identity without printing secrets

After a remote exists and `gh auth status` identifies the intended account, run this in **PowerShell 7 as the Windows user who owns the DPAPI profile**. Replace `OWNER/REPOSITORY` first. This writes repository secrets, not a store release.

```powershell
$targetRepository = 'OWNER/REPOSITORY'
$signingDirectory = Join-Path $env:LOCALAPPDATA 'Taskscape/BlockDungeons/Signing'
$profile = Get-Content -LiteralPath (Join-Path $signingDirectory 'signing.json') -Raw | ConvertFrom-Json
$securePassword = ConvertTo-SecureString ([IO.File]::ReadAllText($profile.protectedPasswordPath).Trim())
try {
    [Convert]::ToBase64String([IO.File]::ReadAllBytes($profile.keyStorePath)) |
        gh secret set ANDROID_UPLOAD_KEYSTORE_BASE64 --repo $targetRepository
    if ($LASTEXITCODE -ne 0) { throw 'Keystore secret setup failed.' }
    $profile.keyAlias | gh secret set ANDROID_UPLOAD_KEY_ALIAS --repo $targetRepository
    if ($LASTEXITCODE -ne 0) { throw 'Alias secret setup failed.' }
    [PSCredential]::new('upload', $securePassword).GetNetworkCredential().Password |
        gh secret set ANDROID_UPLOAD_KEYSTORE_PASSWORD --repo $targetRepository
    if ($LASTEXITCODE -ne 0) { throw 'Password secret setup failed.' }
} finally {
    $securePassword.Dispose()
}
```

Values travel through stdin without plaintext temporary files or secret command-line arguments. Base64 is encoding, not encryption. This small PKCS#12 fits GitHub's per-secret size limit; check larger Windows PFX files before using the same approach. See [GitHub secret handling](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/use-secrets).

## Version allocation

`tools/prepare_ci_release.py` reads the display version from `platform/store/en-US.json` (currently `1.0.0`). It stamps the runner's disposable metadata and **both** Android presets with `versionCode = ANDROID_VERSION_CODE_OFFSET + github.run_number`. Nothing is committed or pushed; store-copy approval remains pending.

`ANDROID_VERSION_CODE_OFFSET` is an optional **repository variable**, default **1000**. The first run uses code **1001**, above uploaded code 1. Values at/below the configured or recorded uploaded code, and values above Play's 2,100,000,000 limit, fail before writing. The upload record is local configuration, not a live Play API lookup.

Both jobs share a run number. Reruns change the filename's attempt component but **reuse the Android code**. If an earlier attempt was uploaded to Play, start a **new run** for a new upload. Raise the offset after workflow-counter migrations/resets or external uploads of higher codes. Ensure the next generated code exceeds the highest code actually used in Play. Windows product/file versions retain the declared release version; filenames and payload `build-info.json` identify individual builds.

## Packaging and checks

Windows uses locked NuGet dependencies and the pinned engine/templates, exports the release template, and runs packaged headless smoke/UI/editor checks. `Build-WindowsArtifacts.ps1` stages the game EXE, complete `data_Dungeons.Game_windows_x86_64/` runtime, third-party notices and build identity; it strips symbols/logs. The Inno script creates a per-user Windows 10+ x64 installer, Start menu shortcut, optional desktop shortcut and uninstaller with Taskscape metadata. The runner's installed Inno Setup 6.3+ compiler output is retained. `Test-WindowsArtifacts.ps1` checks hashes, extracts/runs the portable package, silently installs into a fresh evidence directory, compares every payload byte, runs the installed game and uninstalls. It refuses to replace an existing registered installation. QA uses `/NOICONS` and never launches the post-install interactive game.

Android uses JDK 17, API 36/build tools 36.1.0/NDK 29, pinned engine/templates and bundletool. `Build-AndroidArtifacts.ps1` exports an isolated **release-template** AAB; the existing validator derives the universal APK with the same key. Checks cover actual bundle identity/version, signatures, non-debuggable manifest, min API 26/target 36, editor/test stripping, 54 ETC2 textures, all native 16 KB prerequisites and independent zipalign. The download is the exact validated APK. No device connection is used.

Linux exports the x86-64 release template with a self-contained .NET runtime. `build_linux_deb.py` stages the game under `/opt/block-dungeons`, a `/usr/bin/block-dungeons` launcher, desktop entry, icon and third-party notices. The Debian version includes the workflow run/attempt so later builds upgrade correctly. Native and .NET dependencies are declared for Debian/Ubuntu; the initial validation target is Ubuntu 24.04 amd64. Install with `sudo apt install ./BlockDungeons-*-linux-amd64.deb`. `Test-LinuxArtifacts.ps1` refuses to replace an existing installation, installs the package, checks its version and every installed payload byte, runs packaged smoke/palette/UI checks and removes it. Application user data is retained on removal.

## Local and hosted evidence

`specification.md` tracks implementation/checks up front. Local CI-equivalent evidence is under `artifacts/release-ci-validation-20261029/`, with a disposable stamped source checkout. Its all-zero commit SHA is an explicit **local placeholder** from the pre-commit validation checkpoint; Actions uses the real pushed SHA. The source is now in initial commit `8d2a70c`, and the owner selected `https://github.com/mzagozda/Dungeons.git` as origin. First successful hosted builds, repository-secret provisioning and trusted Windows signing remain separate. A local Windows-host Android export is not a hosted Linux result.

Final local runs pass: Windows release export, installer/ZIP generation and actual install/payload parity/launch/uninstall (`windows-final/`, `windows-install-final/`); registered-key code-1001 AAB and validated universal APK with private cleanup (`source/artifacts/release-ci-final/`); all **102 Python contracts**; workflow actionlint and PowerShell parsing. Eight failure-path checks pass in `guard-results.json`. A temporary self-signed fixture exercises game/Inno signing, is rejected by trust verification, and is removed from the current-user certificate store; the signed embedded-PCK game also launches (`windows-untrusted-signing-result.json`). No root trust is added, and this fixture does not establish trusted publisher signing. Failed/rejected packages stay separate from successful downloads.

The later hosted main run `40862458808` passes Windows installer/portable installation checks; Android stops at missing Actions signing secrets. The previous workflow retained Actions downloads only, and had no Linux package or GitHub Release publisher. The new Linux `.deb` passes actual installation, payload parity, packaged smoke/palette/UI and removal in a fresh Ubuntu 24.04 container without a preinstalled .NET runtime (`artifacts/release-publication/container-result.json`). All 118 Python contracts, workflow lint (Shellcheck/Pyflakes unavailable) and PowerShell parsing pass locally. Hosted validation of the new package/publisher and first public GitHub Release remain separately pending.

References: [workflow artifacts](https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts), [Inno compiler](https://jrsoftware.org/ishelp/topic_compilercmdline.htm), [Inno signing](https://jrsoftware.org/ishelp/topic_setup_signtool.htm), [Android signing](https://developer.android.com/studio/publish/app-signing), [Android versioning](https://developer.android.com/studio/publish/versioning).
