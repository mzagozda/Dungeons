# iOS build handoff

Mac/iPhone validation remains deferred. The local checks below prepare the handoff; they do not establish a working iOS export, signed app or physical-device acceptance.

The finer C# audit found and repaired a real NativeAOT serialization defect. The repair is active after complete isolated/main rendered/package checks, zero-warning AOT analysis and actual Windows native persistence/full-clear execution. The repaired package's separate long run remains in progress. Follow [the NativeAOT repair evidence](native-aot.md); Windows native execution does not establish iOS arm64 linking or device acceptance.

`tools/Export-IosProject.ps1` now provides a separate project export path. The older generic `Export-M0.ps1 -Platform iOS` expects an output file, whereas the current project-only exporter creates an Xcode directory tree. Use the new tool for the adopted host:

```powershell
# On the Mac, after installing the exact .NET SDK and Python requirements:
./tools/Install-Godot.ps1 -WithExportTemplates
./tools/Export-IosProject.ps1 `
  -AppleTeamId <actual-ten-character-team-id> `
  -WorkspaceDirectory artifacts/ios-source `
  -EvidenceDirectory artifacts/ios-validation `
  -Release -BuildUnsignedArchive
```

The tool requires macOS, Xcode 26+/iOS 26 SDK and an explicitly supplied public team ID. It copies/hash-verifies nonignored source into a new isolated workspace, prepares the private iOS preset, builds/imports, generates native ETC2 resources and exports project files. It checks the actual Xcode project/shared scheme and can compile an **unsigned** device archive. Signing, archive-content verification, installation and store acceptance are separately false. No credentials or synthetic team ID are installed, no device is queried, and the original checkout/presets are preserved.

`prepare_ios_preset.py` keeps the specified iOS 14/arm64 target and uses Godot 4.7's native `user_data/accessible_from_files_app` and `user_data/accessible_from_itunes_sharing` options. It removes only their old duplicate keys from `application/additional_plist_content`, retains unrelated plist fields and sets project-only export. Three real-source transformation contracts pass, including idempotence, preservation of Android/Windows sections and rejection of a silently raised target. Example team IDs appear only in synthetic preparation/guard tests; none is a signing identity.

The manual/reusable `.github/workflows/ios.yml` is called by the nightly workflow. Configure the actual public repository variable `DUNGEONS_IOS_TEAM_ID`; optionally set `DUNGEONS_XCODE_DEVELOPER_DIR` to the installed Xcode's `Contents/Developer`. An absent/malformed team fails rather than silently skipping the job. The workflow installs the pinned stack, exports and builds an unsigned archive, and retains evidence. All three workflows pass local actionlint; the PowerShell scripts parse and reject Windows before creating output. **No Mac/hosted positive run has occurred.** Automatic signing from real CI secrets/provisioning and signed archive validation remain unfinished requirements of §18.3.

## Template/source evidence

The complete official template archive matches the active SHA512 pin. The actual installer now installs all templates, including `ios.zip`; the earlier Windows/Android subset did not establish this. Evidence: `artifacts/host-migration/full-template-install/`. Installed iOS ZIP SHA256 is `d72d9a103baea67b5bd1247efa3f78aa985a55e3d6b1803fad7922632c0382a8`.

Offline inspection reads every arm64 Mach-O object from the actual signed-off archive bytes without executing Apple code:

| Library | arm64 objects | Encoded minimum | Build SDK |
| --- | ---: | --- | --- |
| Godot Debug | 2,372 | iOS 14.0 | iOS 26.1 |
| Godot Release | 2,370 | iOS 14.0 | iOS 26.1 |
| MoltenVK device slice | 1 | iOS 14.0 | iOS 18.5 |

The [pinned exporter header](https://github.com/godotengine/godot/blob/4.7.2-stable/platform/ios/export/export_plugin.h) defaults new presets to 15.0, while its [actual preset handling](https://github.com/godotengine/godot/blob/4.7.2-stable/platform/ios/export/export_plugin.cpp) writes the selected deployment target. The template objects above retain 14.0. Neither fact proves that the final C# NativeAOT framework and complete app run on iOS 14. The [framework Info.plist generator](https://github.com/godotengine/godot/blob/4.7.2-stable/editor/export/editor_export_platform_apple_embedded.cpp) also uses the exporter's default minimum; inspect generated framework metadata and actual load commands on the Mac before accepting the requirement. No deployment target is raised here. Raw source, archive identity, parser and full object counts are in `artifacts/host-migration/ios-compatibility/`.

## Actual Mac/device checks still required

Open `artifacts/ios-validation/project/BlockDungeons.xcodeproj`, configure the real signing/provisioning credentials and select the physical iPhone. Inspect app/framework deployment metadata, executable architectures, framework embedding, signatures and archive contents. Verify both sharing plist keys and landscape-only orientation for iPhone/iPad. For the M0 cube/JSON probe, add separate Run arguments `--` and `--m0-smoke-test` to the Xcode scheme; preserve the console and app-container `m0-report.json`. Remove those arguments for ordinary gameplay checks.

The report must prove packaged JSON/translation/Core linkage, visible cube rotation and the actual Documents/levels path. Use Files to import a compatible level. Run the separate touch/safe-area/lifecycle/audio/haptics/gamepad, iPad, reference-budget, battery/thermal and two-hour platform checks. Project export, unsigned compilation and desktop replay tests cannot replace them.

## Unsigned archive prerequisite guard

`Export-IosProject.ps1 -BuildUnsignedArchive` now runs `tools/audit_ios_archive.py` against the actual completed archive, writing `archive-prerequisites.json` outside the input. It checks archive/app identity against the prepared iOS preset, both sharing keys, iPhone/iPad device families and landscape orientations, the retained app/framework plist minimums, and every embedded Mach-O's arm64 device platform/deployment command. The main app metadata and native executable must use SDK 26+. A framework with plist or native minimum above 14 fails even when the app plist still says 14; the exporter default described above is not silently accepted or lowered.

The parser supports thin arm64, fat 32-/64-bit containers and legacy iOS minimum commands, rejects malformed/truncated/overlapping slices and refuses to overwrite output or write inside its input archive. Binary/XML plist fixtures, incompatible native/framework minimums, simulator/x86, identity/sharing/orientation/SDK failures and CLI preservation are covered by **12 synthetic archive tests**. All **69 Python contracts**, workflow lint and the actual non-Mac no-write guard pass at `artifacts/host-migration/ios-archive-audit/`. These are offline contract tests; there is no real Mac archive result yet. A passing report still leaves signing, physical execution and store acceptance false, and does not replace linkage/runtime/payload review.

Binary layouts follow Apple's [Mach-O load-command definitions](https://github.com/apple-oss-distributions/xnu/blob/main/EXTERNAL_HEADERS/mach-o/loader.h) and [fat architecture definitions](https://github.com/apple-oss-distributions/xnu/blob/main/EXTERNAL_HEADERS/mach-o/fat.h). Deployment metadata also follows Apple's [MinimumOSVersion reference](https://developer.apple.com/documentation/bundleresources/information-property-list/minimumosversion).

## Optional automatic signing from CI secrets

The Mac workflow now has a **`sign` boolean, default false**. It prepares the ordinary unsigned Release project/archive and checks its deployment prerequisites first. Opting in invokes `tools/Sign-IosProject.ps1`, which requires the actual matching team, successful release-template archive result, recorded Xcode project fingerprint and real App Store Connect API credentials. Missing prerequisites stop before Apple provisioning is contacted.

Configure these real repository values when the deferred Mac/signing work resumes:

- Variable `DUNGEONS_IOS_TEAM_ID`: the actual ten-character team.
- Variable `DUNGEONS_IOS_API_KEY_ID`: actual ten-character API key ID.
- Variable `DUNGEONS_IOS_API_ISSUER_ID`: actual issuer UUID.
- Secret `DUNGEONS_IOS_API_PRIVATE_KEY`: the downloaded `.p8` PEM contents, scoped only to the signing step.
- Optional variable `DUNGEONS_XCODE_DEVELOPER_DIR`: selected Xcode 26+ developer directory.

The key file is created with mode 600 in a unique `RUNNER_TEMP` directory, outside the checkout/artifact tree, and deleted in `finally`. The helper accepts a key **file path**, not private-key text on the command line. Xcode receives API authentication flags and `-allowProvisioningUpdates` with `CODE_SIGN_STYLE=Automatic`; enabling this operation permits Xcode to create/update signing assets in the configured Apple team. Required Apple account roles and signing assets must be available. The workflow never supplies device-registration flags.

`prepare_ios_signing.py` writes an exclusive `ExportOptions.plist` with automatic signing, `destination=export`, `method=app-store-connect` and version/build-number changes disabled. The signed archive and actual extracted IPA app both pass the native deployment/resource metadata audit before success. The exported app must pass deep/strict `codesign` verification and match the expected Apple distribution authority/team and bundle ID. This prepares an IPA; it never uploads to App Store/TestFlight. Uploaded signing evidence is limited to the IPA, public result/prerequisite/options files and logs; runner key material is excluded.

Six synthetic plan contracts and extracted-application audit parity pass, together with **94 Python cases**, workflow lint, PowerShell parsing and the actual Windows no-write signing preflight (`artifacts/host-migration/native-aot-main/ios-signing-contracts/`). **No real Apple credential, Mac archive, provisioning or signing operation has run.** Real execution may reveal an account-role, certificate/profile or toolchain issue; that remains an unpassed gate. The known iOS 14 framework metadata/native-minimum risk is still enforced rather than lowered.

For local Mac use after a successful current Release unsigned export:

```powershell
./tools/Sign-IosProject.ps1 `
  -ExportEvidenceDirectory artifacts/ios-validation `
  -EvidenceDirectory artifacts/my-new-signed-ipa `
  -AppleTeamId <actual-team-id> -ApiKeyId <actual-key-id> `
  -ApiIssuerId <actual-issuer-uuid> -ApiKeyFile <private-key-path-outside-repository>
```

The API authentication and cloud-signing approach is described by [Apple's Xcode distribution session](https://developer.apple.com/videos/play/wwdc2021/10204/) and [automatic signing build settings](https://developer.apple.com/documentation/xcode/build-settings-reference). Real secrets belong in the configured CI secret store, following [GitHub's Xcode credential guidance](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications).

## Manual TestFlight handoff

After actual signing, archive/IPA verification and final publisher/version/artwork/privacy review, the owner can open the signed archive in **Window → Organizer → Archives**, validate it, then choose **Distribute App → TestFlight & App Store** (or **Custom → App Store Connect** for explicit options). Confirm the real app record/account and choose upload deliberately; the scripts above stop at export. App Store Connect processes the build before the owner configures testers and any required beta review. Device acceptance, provisioning/entitlements, release metadata and Apple processing remain separate. This procedure follows [Apple's distribution guide](https://developer.apple.com/documentation/xcode/distributing-your-app-for-beta-testing-and-releases) and [build upload reference](https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds/); it is not evidence of an upload or TestFlight build.
