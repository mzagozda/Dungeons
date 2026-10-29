# M0 validation record

Updated: 2026-10-26 (Europe/Warsaw). Specification references: §3, §4.2, §5, §16.5, §18, §20.9, §21, §22.

**M0 is incomplete.** Windows and physical Android probes pass, and the local Android export timeout is resolved. Physical iPhone evidence and hosted CI remain outstanding. On 2026-10-26 the user explicitly authorized proceeding with implementation while those checks remain pending; see the upfront specification exception.

| Check | Result / evidence |
|---|---|
| SDK / language | .NET SDK 8.0.426 selected by `global.json`; C# 12; nullable; warnings as errors |
| Engine | Official Godot 4.4.1 .NET `49a5bc7b6`; identical Mono templates; downloaded archives verified against pinned SHA512 |
| Git / LFS | Repository initialized; LFS installed locally; binary rules added. Files remain uncommitted; no remote configured |
| Build | Five projects; Core builds `net8.0` and `netstandard2.1`; Debug build succeeded with zero warnings/errors |
| Fresh-source check | Copied only non-ignored source files to `artifacts/fresh-source/`, without bin/obj/.godot/tool caches. Locked restore, full build, both architecture tests, initial resource import and headless probe passed. Uses the same machine SDK/NuGet cache/editor settings; not a clean-machine test |
| Core tests | 2 passed, 0 failed, 0 skipped; architecture reference guard only, no gameplay coverage |
| Roslyn | Solution fully loaded (6 evaluated projects including both Core targets), no skipped projects/load diagnostics; Game compiler/analyzer gate returned no warnings/errors. Server used MSBuild 10.0.402; authoritative CLI build used pinned SDK 8.0.426 |
| Headless host probe | Passed JSON/translation/Core link and nonzero cube rotation; `artifacts/m0/editor-probe.log` |
| Negative probe checks | Malformed JSON, fractional schema version and string rotation speed each returned exit code 1 and `M0_PROBE=FAIL`; `artifacts/m0/negative-probe-{0,1,2}.log`. Original JSON restored byte for byte |
| Packaged Windows, rendered | Passed on NVIDIA GeForce RTX 3060, Mobile/Vulkan; executable has embedded PCK and separate managed runtime directory; `artifacts/windows/BlockDungeons.log`, `BlockDungeons.png`; screenshot inspected |
| Windows metadata | Product `Block Dungeons`, file version `0.0.1.0`, product version `0.0.1` verified |
| Android export on Windows | Passed using the direct editor executable (exit 0). APK ~100 MB, package `org.blockdungeons.game`, min SDK 26 / target SDK 34, arm64 only; debug signature verified. `artifacts/android/export-transcript.log` |
| Physical Android | Passed USB installation, installed APK SHA256 equality, cold launch and fresh JSON/translation/Core/rotation probe on `2107113SG` / `d211b69` (`vili_eea`), Android 14 / HyperOS `OS1.0`, Adreno 660 / Vulkan 1.1.128. Corrected sensor-landscape configuration and visible cube/status label verified. Evidence: `artifacts/m0/android-device.json`, `android-probe-landscape.png`, `android-runtime.log`. This is a single-device M0 smoke, not the M7 device matrix/performance/lifecycle acceptance |
| iOS export / device | Pending; user deferred Mac/iPhone checks. Real Apple team, Mac/Xcode and physical iPhone required. Documents/levels implementation has not been device-verified |
| Hosted CI | Skeleton written; not run. No remote or hosted run exists |
| M1–M9 | M1 implementation authorized to proceed while M0 platform/CI checks remain pending; no waiver of final validation |

## Reproduce

```powershell
./tools/Install-Godot.ps1 -WithExportTemplates
./tools/Test-M0.ps1
./tools/Export-M0.ps1 -Platform Windows -SmokeTest -Rendered
# With JAVA_HOME / ANDROID_HOME configured:
./tools/Export-M0.ps1 -Platform Android
./tools/Test-AndroidM0.ps1 -AdbPath '<android-sdk>/platform-tools/adb.exe'
```

Generated binaries, runtime DLLs, screenshots, device reports, TRX and logs stay under ignored `artifacts/`. The whole Windows export directory is required; copying only the EXE omits the managed runtime. The runtime also writes `user://m0-report.json`; on this Windows machine that resolves to `%APPDATA%/BlockDungeons/m0-report.json`. It records `packaged`, engine/platform/display, Core assembly, JSON path, cube rotation and user-level/Documents paths.

## Failures found during implementation

- **Missing Godot-side solution:** the root solution alone let `dotnet build` pass, but Godot refused to export C# scripts and produced an unusable app. Added `game/Dungeons.Game.sln` containing the host and Core. Packaged Windows then passed.
- **Missing resource editor:** Windows metadata export reported `rcedit` unavailable. Added a checksum-pinned local installer/configuration path; final EXE metadata was verified.
- **Android SDK path:** Godot's default pointed at a nonexistent SDK. Scripts now configure the explicit SDK/JDK paths from the environment, preserving other editor preferences.
- **Android min/target overrides require Gradle:** the stock APK template cannot enforce these preset overrides. Enabled Gradle and automatic installation of the matching Android build template. M0 target 34 follows that template; store-target policy must be revisited before release.
- **Export restore polluted Debug locks:** Godot publish changed the committed lock graph to a runtime-specific export graph, so the next locked desktop restore failed with NU1004. Core export/configuration locks now live under ignored `obj/locks/`, and host export locks under `.godot/mono/locks/`; desktop Debug locks remain separate. Generated `game/obj` and `game/android` directories also receive `.gdignore` and are explicitly excluded from export so generated build metadata cannot enter packages or trigger imports.
- **Android export timeout (resolved):** the Godot editor completed, but its Windows `_console.exe` wrapper remained alive. The [4.4.1 wrapper source](https://github.com/godotengine/godot/blob/4.4.1-stable/platform/windows/console_wrapper_windows.cpp) waits for the entire child-process job to become empty; Gradle's daemon outlives the export. Direct editor execution returned exit 0. The installer now selects the editor EXE, and `Get-GodotPath` also normalizes cached, environment and explicit console-wrapper paths to the sibling editor. No engine patch or forced success is used. Historical timeout evidence remains in `artifacts/m0/android-shutdown-180s.log`; the latest export transcript records success.
- **Android USB installation restriction (resolved on this phone):** initial installs returned `INSTALL_FAILED_USER_RESTRICTED`; Xiaomi required a SIM to enable USB installation. The APK was transferred for manual installation, and the user subsequently enabled USB installation. A fresh `adb install -r` then succeeded. `-SkipInstall` remains available for manual installs and verifies the installed APK hash before testing.
- **Device report startup race (resolved):** Windows PowerShell treated `cat` stderr for the not-yet-created report as a terminating error, before the intended retry loop could wait for startup. Polling now tests for file existence first; cold launch then waits and reads the fresh report successfully.
- **Mobile orientation (resolved on device):** configuration value 6 meant unrestricted sensor orientation, allowing portrait letterboxing. Changed it to 4 (`SCREEN_SENSOR_LANDSCAPE`) and verified the corrected APK in landscape on the physical phone.
- **First import translation:** a missing generated `strings.en.translation` produced a load error before CSV import. The generated resource and its import settings are included (binary translation uses LFS); clean checkouts must fetch LFS before importing.
- **Editor teardown diagnostic (resolved for batch tooling):** with Android marked `runnable=true`, Godot 4.4.1 printed `Condition "!EditorSettings::get_singleton() || !EditorSettings::get_singleton()->has_setting(p_setting)" is true` at `_EDITOR_GET (editor/editor_settings.cpp:1445)` on headless shutdown. Inspection of upstream `platform/android/export/export_plugin.cpp` showed that the ADB polling thread queries editor settings while shutting down when a runnable Android preset exists. Setting only Android to `runnable=false` avoids that unused editor one-click deployment path; subsequent headless imports completed without the error. The dedicated ADB script remains the device installation path. The earlier custom configuration SceneTree also leaked editor RIDs; it was removed in favour of updating only the necessary settings before editor launch.

## Toolchain compatibility note

The spec's exact .NET 8 requirement conflicts with the preference for the newest Godot, whose Android exports require .NET 9+. Godot 4.4.1 is the compatibility probe candidate and is end-of-life. Windows success does not establish mobile/store compatibility. Per §3.3, complete the physical-device export evidence before treating these pins as frozen. Revisit engine support and the Android store target before M8 release packaging.

M0 does not implement level validation, gameplay, replays, a coverage threshold, GdUnit4 or performance measurements. Those gates remain with their milestones; no placeholder test is presented as proof of them.
