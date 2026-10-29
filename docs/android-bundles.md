# Android App Bundle validation

Current publisher setup is documented in [Android signing](android-signing.md): the real Taskscape upload key signs a validated 1.0.0/code 1 AAB, Google accepts it into a saved unpublished internal-test draft, and the Google-registered upload certificate exactly matches the local public certificate. Google-managed distribution certificates are preserved separately. The earlier QA paths below retain their historical scope; they do not replace actual Play/device/store acceptance.

The adopted Godot 4.7.2/.NET 10 host exports a real debug AAB in the isolated workspace. Pinned **bundletool 1.18.3** validates its structure and generates an offline universal APK. The APK passes the actual manifest, desktop editor/QA stripping, all 54 ETC2 texture byte checks and native Godot texture/model loading. Publisher and physical/store acceptance remain pending.

The initial debug AAB is **unsigned**. The reusable validator rejects it rather than treating bundletool's format validation as signing acceptance. A separate copy signed with a clearly labelled `CN=Android Debug, O=Block Dungeons QA` key passes technical validation, while `releaseAcceptancePassed`, `storeSubmissionReady` and `physicalAcceptance` remain false. Evidence: `artifacts/host-migration/aab-reusable-validation/` (rejected unsigned input), `aab-signed-reusable-validation/` (signed QA copy), and `aab-proof/`. The main runtime/package source is frozen for its long soak. The reusable export helper prepares its AAB preset only in a new isolated source workspace; main game presets retain their APK targets.

## Reusable checks

```powershell
$env:JAVA_HOME = 'C:/Projects/dungeons/.toolchain/jdk17/jdk-17.0.20.1+1'
$env:ANDROID_HOME = 'C:/Program Files (x86)/Android/android-sdk'
./tools/Install-Bundletool.ps1
$textures = (Get-Content artifacts/boot-async-workspace/artifacts/android/block_dungeons.apk.textures.json -Raw | ConvertFrom-Json).profileDirectory
./tools/Test-AndroidBundle.ps1 `
  -BundlePath artifacts/host-migration/aab-proof/block_dungeons_qa_signed.aab `
  -TextureProfileDirectory $textures `
  -DebugApks -EvidenceDirectory artifacts/release/my-new-debug-bundle-check
```

The installer verifies Google's published release-asset SHA256 and the running jar's version. Bundle validation uses bundletool's validator and manifest dump, JDK signature verification, an actual certificate/date/fingerprint check and an explicit single-signer requirement. It builds an APK set without connecting to a device, inspects its universal APK, loads the packaged ETC2 resources through Godot, and reuses the SDK/native/signature release audit. Passwords are supplied as files rather than command-line values. Explicit debug APK validation owns its temporary password file and removes only that file; caller-provided password files are retained.

For a publisher candidate, omit `-DebugApks` and supply `-ApkSigningKeyStore`, `-ApkSigningKeyAlias`, `-StorePasswordFile`, `-KeyPasswordFile`, `-ExpectedUploadCertificateSha256` and `-ExpectedApkCertificateSha256`. Upload and APK signing certificates are checked separately because they may differ. Missing expected identities cannot earn release acceptance, and mismatches are failures. No real publisher key, certificate or store identity is assumed here.

## Export/signing boundary

`tools/Export-AndroidBundle.ps1` copies the nonignored repository source into a new isolated workspace, verifies every copied hash, duplicates the selected normal/Low API 36/min 26 arm64 preset with AAB format, builds/imports from scratch and prepares/verifies all 54 native ETC2 variants before export. It installs the pinned Android source template and application hooks in that workspace. Both workspace and evidence must be new, nonoverlapping paths under repository `artifacts/`. Failed output is retained; existing paths are rejected. The main APK presets and generated project remain unchanged. `Export-M0.ps1` retains its APK-specific audits.

```powershell
./tools/Export-AndroidBundle.ps1 `
  -WorkspaceDirectory C:/Projects/dungeons/artifacts/release/my-bundle-workspace `
  -EvidenceDirectory C:/Projects/dungeons/artifacts/release/my-bundle-export
# Add -Low for the Compatibility preset. -Release selects the release template;
# actual publisher signing configuration and subsequent validation are required.
```

For `-Release`, supply Godot's `GODOT_ANDROID_KEYSTORE_RELEASE_PATH`, `GODOT_ANDROID_KEYSTORE_RELEASE_USER` and `GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD` in the invoking process environment. The path must identify an existing key store. These inputs are required before copying/building; the wrapper does not copy local editor credentials into the isolated source project or write signing passwords into presets/reports. Use the caller's secret provider rather than placing credentials in source. The supported variable names are defined by the [pinned exporter header](https://github.com/godotengine/godot/blob/4.7.2-stable/platform/android/export/export_plugin.h).

The actual cold export passes at `artifacts/host-migration/aab-cold-export/`, with its source manifest, warning-free build/import logs, native texture profile and AAB SHA256. A separate QA-signed copy passes `aab-cold-validation/`: actual JDK/APK signature verification, manifest/stripping, 54 byte/native texture checks, all eleven ELF/ZIP 16 KB checks and independent build-tools 36.1.0 `zipalign -c -P 16 -v 4`. Publisher, physical and store fields remain false.

`Test-AndroidBundleContracts.ps1` exercises actual exported inputs. The passing `aab-cold-contracts/` report proves rejection of the unsigned export, modified signed JSON payload, wrong expected certificate and the real QA fingerprint presented as a publisher. Existing evidence is preserved, and both original bundle hashes remain unchanged. A structurally valid bundle is insufficient when cryptographic verification fails.

`Test-AndroidBundlePipeline.ps1` combines cold export, explicit QA signing, validation and those rejection contracts. It owns/removes its temporary password file, retains its labelled QA key under ignored `.toolchain/` and cannot request a release export or publisher acceptance. The actual complete Low pipeline passes at `artifacts/host-migration/aab-low-pipeline/`, including Compatibility metadata and all 54 texture/native checks. Both normal and Low bundle paths now have real local evidence. The Android CI job invokes the normal chain and uploads its evidence; hosted execution remains pending.

The actual release-template path is also exercised with the same clearly labelled QA key at `artifacts/host-migration/aab-release-qa-export/` and `aab-release-qa-validation/`. Export and signed-bundle/universal-APK technical checks pass, including a non-debuggable manifest, native alignment, all 54 texture checks and zipalign. The release audit correctly rejects publisher identity, and delivery acceptance stays false. The earlier attempt without a release key failed before producing a bundle (`aab-release-export/`); it remains preserved, and the wrapper now rejects absent signing environment inputs before copying sources. Process signing inputs are restored after the local probe.

An AAB is signed using **jarsigner**, while generated APKs use APK signing. Structure checks and a valid QA signature do not identify a publisher. Universal-APK validation does not prove device/split delivery, Play Console acceptance, final download size or actual 16 KB-device execution. Google Play metadata/artwork, publisher signing and physical checks remain open.

Sources: [Godot 4.7 Android export options](https://docs.godotengine.org/en/4.7/classes/class_editorexportplatformandroid.html), [Android bundletool](https://developer.android.com/tools/bundletool), [Android command-line bundle signing](https://developer.android.com/build/building-cmdline), [pinned official bundletool release](https://github.com/google/bundletool/releases/tag/1.18.3).
## Offline split APK matrix

`tools/Test-AndroidSplits.ps1` extends an already successful, matching QA universal-APK validation. It generates a default APK set with the owned QA key, then extracts synthetic arm64 configurations for API 26/34/36, density 480 and en/pl resources. It never uses connected-device selection, installation or ADB. This follows Google's documented [manual device-specification and extract-apks workflow](https://developer.android.com/tools/bundletool).

The actual Godot bundle selects **six APKs** per profile: base, arm64 code, two languages, density and an install-time asset pack. The latter contains the game/Core DLLs and textures. `audit_android_splits.py` requires unique consistent package/version/split identities and the expected APK signer, the selected ABI/languages/density, actual independent zipalign, and complete native/assets payloads. Every asset and native library must match the validated universal APK byte-for-byte; duplicate/extra/missing payloads fail. Every ELF LOAD/ZIP prerequisite is checked with the existing native auditor. The base manifest and asset-pack resource/type/54-texture audits run on the correct split components. Identical asset-pack hashes can reuse the recorded native texture audit; that reuse is explicit in each result.

Both current normal and Low matrices pass at `artifacts/host-migration/native-aot-main/splits-normal/` and `splits-low/`: 36 selected APKs total, eleven native libraries and 646 asset entries per set, unchanged inputs and successful signatures/16 KB/zipalign checks. Actual incompatible API 25 and x86-64 extraction attempts fail with no APKs produced. Eight synthetic contracts cover missing roles, individual signer/zipalign requirements, package/version mismatch, byte tampering, ELF/ABI errors, duplication and SDK/density mismatch; all **87 Python contracts** pass at `split-contracts/`.

The cold QA bundle pipeline now invokes this matrix after universal validation and its existing negative contracts. Its complete updated cold run **passes** at `artifacts/host-migration/native-aot-main/split-full-pipeline/`, including fresh source/export/QA signing, universal checks, actual rejection contracts and all three split profiles/two incompatible-profile rejections. A successful offline matrix always leaves physical execution, actual 16 KB kernel/device support, graphics capabilities, real publisher signing and store delivery **false/pending**. Synthetic locale splits do not establish gameplay translation support or represent a measured phone configuration.

