# Block Dungeons

Implementation follows [specification.md](specification.md). The checklist at the top of that document is authoritative for progress. **Current stage: M8 polish/hardening and platform acceptance remain open; M9 authoring APIs have passed local checks.** The user explicitly authorized proceeding with implementation while the M0 iPhone/Mac and hosted CI checks remain pending; this sequencing exception is recorded in the specification.

## Automatic release downloads

Pushes to `main` run [Main release artifacts](.github/workflows/release-artifacts.yml): Windows Inno Setup installer and portable ZIP, Debian/Ubuntu amd64 `.deb`, and publisher-signed Android APK/AAB. Once all three platform jobs pass, verified packages and SHA-256 checksums are published as a build prerelease on [GitHub Releases](https://github.com/mzagozda/Dungeons/releases). PRs test desktop packaging without signing secrets or publication; manual branch runs can validate the full package set without publishing. Configure the three required Android secrets and optional Windows Authenticode secrets described in [CI release setup](docs/ci-release-artifacts.md). No Google Play upload or rollout occurs.

## Toolchain candidate

| Tool | Pinned version / requirement |
|---|---|
| Language | C# 12; nullable enabled; warnings treated as errors |
| .NET SDK | **10.0.402**, exact match in `global.json` |
| Core targets | `net8.0;netstandard2.1`, no Godot reference |
| Host | **Godot 4.7.2 .NET**, `ed1daf0bf`, Mono export templates of the same version |
| Android probe | JDK **17**, SDK Platform **36**, build tools **36.1.0**, NDK **29.0.14206865** if requested by Gradle, min SDK **26**, arm64 |
| iOS probe | A Mac with Xcode 26+/iOS 26 SDK for current store builds, Apple signing team and physical iPhone; deployment target 14.0, arm64 remains to be validated |
| Scripts | Windows PowerShell 5.1 or PowerShell 7; PowerShell 7 on Linux/macOS; Git LFS; curl |
| Asset tooling / mobile exports | Python **3.14** with `python -m pip install -r tools/requirements.txt` |

The user approved the Godot 4.7.2/.NET 10 host/build migration on 2026-10-27. C# 12 and Core targets remain unchanged. Host, CLI and test executables target net10.0; Core remains independent of Godot. Android targets API 36 with minimum API 26. Physical platform, signed AAB and hosted CI acceptance remain pending; see [the migration evidence](docs/release-toolchain-candidate.md).

## Install, build and run

From the repository root (commands below assume PowerShell):

```powershell
git lfs install
./tools/Install-Godot.ps1 -WithExportTemplates
./tools/Test-M0.ps1
./tools/Test-Core.ps1
./tools/Export-M0.ps1 -Platform Windows -SmokeTest -Rendered
```

Nightly hardening uses separate [rendered regression](docs/nightly-hardening.md) and [per-runner benchmark](docs/performance-baselines.md) runners, both requiring PowerShell 7. `./tools/Test-NightlyBenchmarks.ps1 -Runner windows-r5-7600x` checks all six required CPU workloads against the measured local reference. Missing/mismatched references and regressions fail; candidate measurements never replace stored references automatically. Physical-device checks remain separately tracked in the specification.

The [desktop soak runner](docs/desktop-soak.md), `./tools/Test-DesktopSoak.ps1`, runs an isolated exported Windows package for two real hours. `-Seconds 10 -Fast` is an explicitly labelled diagnostic; short native scene diagnostics also run in the rendered M8/nightly gates. Automated export smoke uses Dummy audio so it works without an audio endpoint; listening and device acceptance remain separate.

[Release readiness](docs/release-readiness.md) records the Android SDK/native page-size gaps and the actual package audit. `./tools/Test-AndroidRelease.ps1` checks exported APK prerequisites and rejects the current development build; store AAB/iOS delivery remain pending.

The [Android bundle tools](docs/android-bundles.md) export from an isolated source copy, validate signed AABs and generate/check offline APKs. `./tools/Test-AndroidBundlePipeline.ps1 -WorkspaceDirectory artifacts/my-bundle-workspace -EvidenceDirectory artifacts/my-bundle-qa` runs the debug QA chain with a labelled QA key and rejection contracts. Add `-Low` for Compatibility. The [Taskscape publisher-signing helper](docs/android-signing.md), `Export-AndroidPublisher.ps1`, also passes a real signed 1.0.0 release export using the protected local upload-key profile. The Google Play draft exists; Play registration/delivery, physical checks and store acceptance remain separate. Hosted CI still uses the QA chain without publisher secrets.

English [store metadata drafts](docs/store-metadata.md) are editable at `platform/store/en-US.json`. `python tools/store_metadata.py export --output artifacts/store/my-new-draft` produces plain-text copy. Publisher details and first-release version 1.0.0/code 1 are configured; `check --require-ready` still requires human listing-copy review. Exporting a draft does not publish or approve it.

On Windows PowerShell, use `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-M0.ps1` if the local script execution policy requires it. Scripts never change that policy.

If the exact SDK is missing (including after a machine-wide SDK update), run `./tools/Install-Dotnet.ps1`. This downloads the pinned official SDK into ignored `.toolchain/dotnet/` and verifies its SHA512 against the [official .NET 10 release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json). Build/test/export scripts activate it automatically. For direct CLI commands in the same shell, run `. ./tools/Common.ps1` first. It changes only that shell's PATH/DOTNET_ROOT. The host's NuGet packages are also isolated under `game/.godot/mono/packages/` so global-cache cleanup cannot invalidate the engine generators.

The installer downloads the official .NET editor and templates from `godotengine/godot-builds`, verifies SHA512 against the committed manifest, and caches the editor under `.toolchain/`. Templates go to Godot's standard per-user template directory. On Windows it also installs a checksum-pinned `electron/rcedit` 2.0.0 locally for EXE version metadata. SDKs/signing credentials are not bundled.

Windows automation uses the direct Godot editor executable. Its console wrapper waits for descendant processes such as Gradle daemons and can remain alive after a completed Android export. The scripts normalize older cached or supplied `_console.exe` paths to the sibling editor automatically.

`Dungeons.sln` builds all five projects. `game/Dungeons.Game.sln` is the host-only solution Godot requires beside `project.godot`; it includes the Core project reference. Debug dependency locks are committed; Godot's publish restores use separate ignored locks so an export does not dirty the next locked desktop build.

To open the game interactively:

```powershell
$godotExe = (Get-Content .toolchain/godot-path.txt -Raw).Trim()
& $godotExe --path game
# Or launch artifacts/windows/BlockDungeons.exe after exporting.
```

Normal launch opens the game menu. Add `-- --m0-smoke-test` to select the original spinning-cube export probe: its green status label proves that packaged JSON, English translation and the Core assembly loaded, and it writes `user://m0-report.json`. The probe exits nonzero on failure; automation also requires its success marker within 45 seconds. `--m0-screenshot=<absolute-path>` captures the probe.

Windows uses Mobile/Vulkan by default. For the supported D3D12 fallback, launch `artifacts/windows/BlockDungeons.exe --rendering-driver d3d12`. The adopted Godot 4.7.2 package passes an actual D3D12 Mobile diagnostic on this RTX 3060, including full-clear, ambush, death/respawn and ordinary mission flows (`artifacts/host-migration/d3d12-diagnostic/`). This short check is separate from the Vulkan two-hour soak and its approved still-image profile.

Desktop application arguments follow Godot's `--` separator, in either `--key value` or `--key=value` form:

```powershell
& $godotExe --path game -- --level core.mossy_hollow --seed 42
& $godotExe --path game -- --level 'C:/Levels/My level.dlvl' --seed 42
& $godotExe --path game -- --replay 'C:/Replays/run.dreplay'
& $godotExe --headless --path game -- --headless-validate 'C:/Levels/level.json'
```

Level paths accept JSON, directories containing `level.json`, and bounded `.dlvl` packages. Explicit external files are opened directly; they are not imported into the catalogue. Seeds are unsigned decimal 64-bit integers. Validation prints diagnostics and exits 0 for a valid level (warnings allowed), 1 for invalid content, and 2 for command or IO errors; it opens no profile. Replay resolves its recorded level id unless an explicit `--level` path is supplied. It verifies the complete recording before play, uses its recorded seed/difficulty/editor origin, and leaves attempts, records and automatic replay files untouched. Escape pauses/resumes; Back at the recorded boundary returns to level selection. A conflicting explicit seed is rejected.

For automation, `--replay-quit` exits after successful playback; `--screenshot-test=<absolute.png>` captures normal play after 120 ticks or the final recorded replay frame, then exits. Automation captures ignore unrelated desktop focus/input and use Dummy audio in the test runner. In PowerShell 7, run `./tools/Test-LaunchCommands.ps1 -Rendered` after `Test-Core.ps1` and the Debug game build; pass `-Executable artifacts/windows/BlockDungeons.exe` to exercise the exported app. These process checks are also included in the M8/M9 runner and the headless CI job.

Named visual fixtures are available through `--screenshot-list` and `--screenshot-test hero-roll --screenshot-output C:/Captures/roll.png`. The 22 terrain/action/combat/mission/touch/settings scenarios use fixed capture clocks, new output files and hashed provenance sidecars. `./tools/Test-ScreenshotScenarios.ps1 -Repeat` captures and compares every fixture; select `-Renderer gl_compatibility` for the other renderer. [Capture commands, fixture names and image-diff thresholds](docs/visual-captures.md) describe the workflow. These generated candidates do not approve visual baselines or physical acceptance.

## Android

Install JDK 17 and Android command-line tools. Set local paths, then install the required SDK components:

```powershell
$env:JAVA_HOME = 'C:/path/to/jdk17'
$env:ANDROID_HOME = 'C:/path/to/android-sdk'
& "$env:ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager.bat" 'platform-tools' 'platforms;android-36' 'build-tools;36.1.0' 'ndk;29.0.14206865'
./tools/Export-M0.ps1 -Platform Android
./tools/Test-AndroidM0.ps1 -AdbPath "$env:ANDROID_HOME/platform-tools/adb.exe"
```

On Linux/macOS use `sdkmanager` and `adb` without `.bat`/`.exe`. The export script configures only the Android SDK/JDK and Windows resource-editor paths in the user's Godot editor settings, preserving other preferences. It installs the generated Gradle template under ignored `game/android/` on first export. Gradle is required to enforce min SDK 26. Use a debug APK for device checks; `-Release` needs a real private keystore configured locally. No signing material belongs in Git.

The device must appear as `device` in `adb devices -l`, with USB debugging authorized. If several devices are connected, select `-Serial`. The device check replaces only this package (`org.blockdungeons.game`), clears its old probe report, launches it and pulls a fresh report using debug `run-as`. It records the APK hash and serial in `artifacts/m0/android-device.json`. Confirm the cube/label visually too. Emulators do not satisfy M0.

If the phone's firmware blocks USB installation, copy the APK to Downloads and install it through the phone's Files/File Manager, approving that source if requested. Then use `Test-AndroidM0.ps1 -Serial <serial> -SkipInstall`. This skips only the installation command: the checker verifies the installed APK's SHA256 against the local build before launching it and reading a fresh report. Manual installation may also be restricted by the phone's policy; do not count it as passed until it succeeds.

Exports have a 600-second process timeout, adjustable with `-ExportTimeoutSeconds`. A timeout is a failed export even if an APK was written. The Android preset is intentionally disabled for editor one-click deployment (`runnable=false`) so batch tools do not start Godot's ADB polling thread; the dedicated device script handles installation and launch.

Android user levels are under the app's `user://levels/`. Level Select → Import opens the native picker on Android 10+; choose a local `.dlvl` package. Android 8–9 use Files → Open with → Block Dungeons (also available on newer devices). Selected/granted packages are validated and copied atomically into private app storage; no storage permission is needed. Actual picker/Open with interaction and API 26–28 checks remain pending. iOS users copy packages through Files → On My iPhone/iPad → Block Dungeons → levels, then choose Refresh; the physical Documents path check remains pending.

Use `./tools/Export-M0.ps1 -Platform Android -Low` for a separate `artifacts/android/block_dungeons_low.apk` that boots Compatibility with Low quality and does not require Vulkan in its manifest. Normal and Low APKs share the application id and replace each other when installed. Export applies the tracked hooks in `platform/android/` to the generated Gradle template and verifies the packaged manifest. With an awake, authorized physical phone, `./tools/Test-AndroidM7.ps1 -AdbPath "$env:ANDROID_HOME/platform-tools/adb.exe" -Serial <serial> [-Low]` checks the exact installed APK and captures native touch metrics plus a synthetic gesture replay; physical finger/battery/thermal acceptance remains separate.

## iPhone / Mac handoff

The user deferred Mac/iPhone validation. On the Mac, install the exact SDK/editor/templates, then use the isolated [iOS handoff](docs/ios-handoff.md) with the actual public Apple team:

```powershell
./tools/Install-Godot.ps1 -WithExportTemplates
./tools/Export-IosProject.ps1 -AppleTeamId <actual-ten-character-team-id> -WorkspaceDirectory artifacts/ios-source -EvidenceDirectory artifacts/ios-validation -Release -BuildUnsignedArchive
```

The helper creates an Xcode project and optional unsigned archive. The [iOS handoff](docs/ios-handoff.md) also documents opt-in automatic signing from real CI API credentials, an export-only IPA helper and the **manual TestFlight path**. These routes remain unexecuted on a Mac. For physical QA, open `artifacts/ios-validation/project/BlockDungeons.xcodeproj`, configure actual signing/provisioning and the physical iPhone. For the M0 cube/JSON probe, add Run arguments `--` and `--m0-smoke-test` to the scheme. Preserve the device console, visible rotation/JSON success and `m0-report.json` from the app container. Inspect deployment/framework metadata and both document-sharing keys. `PlatformPaths.UserLevelsDir` explicitly chooses Documents/levels on iOS; verify it using Files. This path is **unverified on device**. Export or unsigned compilation does not satisfy the gate.

## Validation and scope

[docs/m0-validation.md](docs/m0-validation.md) and [docs/m1-validation.md](docs/m1-validation.md) record evidence, failures and pending checks. `.github/workflows/ci.yml` includes Linux/Windows builds/headless probes, the Release Core tests/allocation/performance/85% coverage gate, Windows export and debug Android export. Hosted acceptance requires successful runs against the actual pushed revision. Physical device tests are separate; the optional iOS signing implementation awaits real Mac/credential execution.

The Core contains deterministic fixed ticks, movement/collision/stairs, separation, A*, independent random streams, state hashes/replays, level/content loading, safe packages and mission/objective logic. Hero actions include sword combos, roll, bow/projectiles, armour/damage/grace, potion, pickups and deterministic auto-aim. Zombies and skeletons now perceive, chase/position, telegraph, attack, retreat/return and drop loot; dynamic spawning obeys live caps and emergence. The host boots to the menu/catalogue and renders the level with original models, terrain, lighting, camera, HUD and pooled combat text. WASD/arrows move; J/left mouse attacks; K/right mouse fires; Space/Shift rolls; Q heals; Escape pauses. Gamepad defaults are X/RT/A or B/Y/Start. F3 shows counters and F4–F7 toggle world layers. Remaining presentation/final UI and reviewed gameplay/platform acceptance are pending.

Run `./tools/Test-M4.ps1 -Rendered` for the complete local hero/rendering gate, including deterministic scenarios, allocation/coverage checks, Godot input/view tests and reproducible action captures. See [M4 evidence](docs/m4-validation.md). Platform checks remain separate, as recorded up front in the specification.

Run `./tools/Test-M5.ps1 -Rendered -Benchmarks` for the expanded enemy/runtime checkpoint, including the 64-brain combat/path budgets, pools, combat feedback and rendered melee capture. See [M5 evidence](docs/m5-validation.md). [M6 progress](docs/m6-validation.md) covers the implemented mission/profile flow and its pending audit/golden requirements.

Run `./tools/Test-M6.ps1 -Rendered -Benchmarks` for the mission/runtime checkpoint. The unmodified level is completed using recorded player commands, three gameplay replay candidates are verified, profile/replay IO is checked, and death/failure/completion screens plus runtime object/light replacement are captured. A generated candidate manifest records exact file identities and pending review status. See [M6 evidence](docs/m6-validation.md) for remaining audit/review requirements. Visual QA explicitly uses Dummy audio; production audio and physical audio acceptance are separate. Normal play counts attempts immediately, chooses authored or run-counter seeds, retries the same seed, and saves profile records plus the last five automatic replays under `user://`.

The [offline gameplay review workflow](docs/gameplay-review.md) records the replay flows and default mix from a private Windows package/profile. The owner approved the exact three gameplay goldens from its 6:19 video; they are promoted under `assets/baselines/replays/mossy-hollow-v1/` and pass `Test-ReplayGoldens.ps1`. Motion/audio acceptance remains pending. Development recordings never automatically replace reviewed files. The [local Windows performance reference](docs/performance-baselines.md) is also explicitly approved and adopted; other platforms/hosted baselines remain separate.

Run `./tools/Test-M7.ps1 -Rendered` for the current local touch/graphics checkpoint, including phone, mirrored and 4:3 tablet captures and Low Vulkan/Compatibility checks with verified input replays. Add `--touch-preview` when launching normal desktop gameplay to show touch controls; real mobile builds enable them automatically. `--quality=Low|Medium|High` selects a development override; runtime defaults and conservative device rules are in `game/content/device_profiles.json`. See [M7 progress and device gaps](docs/m7-validation.md). Physical haptics/import acceptance remains pending; persisted settings are implemented below.

Settings are now available from the main menu and pause screen. Video/audio/controls/accessibility changes stay in a draft until Apply; Back discards unapplied edits. Keyboard/mouse and gamepad bindings, mirrored/draggable touch layout, aim assistance, vibration, health patterns/palettes and text size persist in atomic `user://settings.json`, with corrupt-file backups. Original sounds, ambience and a 144-second score bind gameplay/UI events to independent volume buses, bounded positional voices, music crossfades/ducking and background suspension. Editable audio source and regeneration instructions are in [assets/source/audio](assets/source/audio/README.md); listening/device review remains pending. `Alt+Enter` toggles desktop fullscreen. Debug builds also support a four-finger tap for F3. Run `./tools/Test-M8.ps1 -Rendered` for the expanded local gate; add `-Benchmarks` for combat/full-replay measurements. See [M8 evidence and remaining acceptance](docs/m8-validation.md).

The shared pixel UI uses pinned Silkscreen Regular with its complete OFL notice, plus original palette-only 16px nine-slice assets and bitmap symbol glyphs. Run `python tools/make_ui.py` to regenerate the skin, or add `--check` for an offline provenance/import/staleness audit. Font revision and licence details are in [assets/source/fonts](assets/source/fonts/README.md). Text accessibility scaling preserves the shared skin; rendered M8 checks cover both desktop backends and compact phone previews.

Level selection shows responsive preview cards with author, difficulty, best time, completed ribbons and user badges. Broken entries open their description and scrollable diagnostic codes with Play disabled; hover/long-press exposes the first diagnostic. Refresh rescans the levels folder and preserves selection. Optional previews must be 320×180 PNGs within 1 MiB; missing or rejected images use a placeholder. Thumbnail decoding is bounded and cached, and directory/packaged previews share the same admission checks.

The health group scales from its 1080p reference, with original potion/bow slots and a shared native-touch potion cooldown sweep. Below 25% health, the border pulses and a subtle red edge vignette appears; pause freezes it and Reduce Motion keeps a static warning. Health patterns and palette settings remain active. Native HUD checks cover 720p/1080p/1440p and maximum-scale phone previews; final visual/device acceptance remains separate.

The mission HUD shows a diamond primary objective and scrollable grey secondary goals, with emerald/hero-head counters on desktop and touch. Authored messages use dark pills with 0.2-second fades; checkpoints have a separate toast, and approaching a locked exit shows its current objective. Pause freezes fades, while Core retains message expiry. F3 includes counts by actual enemy AI mode. Reference-size and phone tests cover nested text bounds, all 32 goals, and locked-notice icon spacing.

Footsteps come from fixed Core ticks with retained terrain metadata, and melee sounds start on the first active attack frame. Application diagnostics use `user://logs/dungeons.log` plus four archives, each capped at 1,000,000 bytes. UTF-8 JSON records retain level/seed/tick context, dropped counts and recovery information; F3 shows queue/drop counts. Disk work runs outside simulation ticks and flushes on background/exit. Native engine stdout/stderr remains available to QA; renderer-relaunch checks use their own explicit diagnostic log.

After an export, restore `game/Dungeons.Game.csproj` in Debug before using `--no-restore` for scene-test builds. ExportDebug intentionally excludes the GdUnit dependencies; the standard milestone runners restore the correct Debug graph automatically.

Use `./tools/Test-M3.ps1 -Rendered` for the local rendering runner, or omit `-Rendered` for headless checks. See [M3 evidence](docs/m3-validation.md) for limitations and the exact pinned-engine headless glTF preview diagnostic. To launch a level directly, run the pinned Godot with `--path game -- --level core.mossy_hollow --seed 1`. Explicit `--m0-smoke-test` keeps the standalone export probe available; ordinary boot opens the menu without running QA replays. The Android M0 helper supplies the explicit probe flag and requires an awake phone for Vulkan.

For the complete M2 gate (including ten minutes of fuzz), install `python -m pip install -r tools/requirements.txt`, then run `./tools/Test-M2.ps1`. A shorter `-FuzzSeconds` is a smoke check and is explicitly recorded as not passing the full gate.

`./tools/Test-SimulationFuzz.ps1` separately runs §20.5 simulation fuzzing: 32 seeded Mossy Hollow/generated-map cases, 10,000 actual ticks twice, per-tick geometry/health/hash checks and retained reproduction evidence. It is included in M8 and nightly hardening. See [simulation fuzzing](docs/simulation-fuzz.md) for CLI arguments, seeds and evidence; fast simulation does not satisfy the platform soak requirement.

```powershell
. ./tools/Common.ps1
dotnet run --project src/Dungeons.Tools -c Release -- validate game/levels
dotnet run --project src/Dungeons.Tools -c Release -- pack game/levels/mossy_hollow artifacts/mossy.dlvl
dotnet run --project src/Dungeons.Tools -c Release -- preview game/levels/mossy_hollow/level.json
```

Tools also provides `unpack`, `migrate`, `schema`, `diagnostics`, `fuzz`, and replay verification/rerecording. `schema --triggers` dumps generated form metadata for all supported events/actions/conditions, including reference kinds and local schema definitions. `schema --editor` dumps the resolved content palette and property schemas; `--content <dir>` can select another registry. Missing optional authored hints remain empty/null. Package/folder `assets/` files are retained through `IAssetSource` and authoring edits; v1 reports that rendering overrides remain unsupported. Outputs must be new files/directories. `replay --record-idle <level> <ticks> <new.dreplay>` produces a deterministic tooling fixture; it is not a mission clear. New gameplay golden hashes require review. See [M2 evidence](docs/m2-validation.md), [M9 API evidence](docs/m9-validation.md) and the generated [diagnostic catalogue](docs/diagnostics.md).

`preview <level.json|level.dlvl> --png <new-output.png>` captures a 320×180 level overview using `WorldView.CapturePreview`. Run it from this checkout after toolchain setup and asset import, with a real graphics backend available. It uses `GODOT_BIN` or the installed pinned Godot path and builds the current Debug game sources before rendering; existing output files are preserved. Plain `preview <level>` retains the ASCII view. The PNG service is available to in-process editor saves without launching a second game or creating a simulation session.

All shipped tile/entity definitions carry names, categories, icon paths, editor characters and property schemas. ASCII preview uses the same tile/overlay/entity characters, including a custom registry's overrides. `tools/Make-EditorIcons.ps1` rebuilds their 32px icons from the existing models/terrain and editable `assets/source/editor-icons/` SVG glyphs, quantizes to the original palette and records source/output hashes. `tools/check_editor_icons.py` checks coverage, provenance and PNG contracts; editor art stays under `game/editor/` for mobile stripping. Definition metadata participates in the content hash, so metadata changes require fresh development replay candidates and renewed review.

`tools/Test-M9.ps1 -Rendered` runs the complete existing gate, metadata dumps, native session-free preview/thumbnail tests, Mobile and Compatibility captures and real raw/package PNG CLI checks. M9 and platform acceptance remain separately tracked in the specification.

For desktop level development, launch Godot with `--path game res://editor/level_reload.tscn -- --reload-level=<absolute-level.json>`. Save JSON edits on disk to reload through the production loader, Core session and renderer. Invalid/partial edits retain the previous run. The unrecorded prototype starts a fresh simulation epoch (ticks, mission, stats and loadout reset), retaining the first living hero's safe position; Escape pauses/resumes. `GameSession.ReloadLevel` replaces `World`, so callers reacquire world/entity references and rebind their view with `WorldView.Reload`. Source-changing reloads are rejected during v1 replay recording. Editor adapters are excluded from normal mobile resources and compiled assemblies; the portable APIs remain available.

Batch export derives the C# `DungeonsIncludeEditor` build flag from the selected preset's `editor` feature. Mobile targets default to excluding editor code. A future mobile editor preset can opt in through that feature and its resource filters; the same Core/rendering APIs are retained. Windows smoke exports also load every packaged palette texture and inspect the actual DLL's authoring/test type boundary.

All M0 cube/scene/JSON/strings and the simple probe SVG icon are original. No third-party game assets are included. The shared product/probe font is Silkscreen Regular; its complete OFL notice and engine/runtime notices are included under `game/licenses/` and named in Credits. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for tool/runtime provenance.
