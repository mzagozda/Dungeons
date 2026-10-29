# Android publisher signing — 2026-10-28

The owner's Google Play account is **Taskscape Ltd**, developer ID `5601502941079677358`. The **Block Dungeons** listing is app ID `4972227016795288536`, package `org.blockdungeons.game`, English (US), **free**. The owner explicitly approved the developer-policy and US export declarations before draft creation. The initial signing/upload work stopped at an unpublished draft. The internal release subsequently became available before the owner's tester request; its current audience setup is recorded below. No production release was performed by the agent.

Public account/certificate configuration is in `platform/android/play-console.json`. Google generated the managed app-signing key when the draft was created; its public certificate was downloaded through the signed-in console and its DER SHA-256 matched the visible console fingerprint. The additional Google certificate files are preserved alongside it. Google's private app-signing key is not downloadable; see the [official signing guide](https://developer.android.com/studio/publish/app-signing).

| Identity | SHA-256 |
| --- | --- |
| Local Taskscape upload certificate | `cf2186a633b8e81fa79c2a8b4b27081eb83d07c6a8fc8275694ab7e488de1e61` |
| Google Play app-signing certificate | `56d757ed6ffebfe5b95f14a777483f2ac0873fd9fd394ae33e675f8cb76aeb35` |

These identities have different purposes. The upload key signs the AAB sent to Google. Google signs distributed APKs with its managed key. Local bundletool APKs in the validation evidence are deliberately signed with the upload key; they do not prove the signature or delivery of Play-generated APKs.

The separate `main` release workflow now supports this registered identity through three repository secrets; see [CI release setup](ci-release-artifacts.md). The local DPAPI profile itself is not portable to a hosted runner. Existing debug/QA CI remains independent.

## Local private profile

The new upload key is RSA-3072, subject `CN=Taskscape Ltd, O=Taskscape Ltd`, alias `block-dungeons-upload`, valid until 2054-03-15. Private files are outside the repository in `%LOCALAPPDATA%/Taskscape/BlockDungeons/Signing/`:

- `block-dungeons-upload.p12`: encrypted private keystore.
- `upload-password.dpapi`: password encrypted with Windows CurrentUser DPAPI.
- `signing.json`: local paths/alias and the expected public upload fingerprint.

The directory's inheritance is disabled and its ACL permits only the current Windows user and SYSTEM. No password or private key is committed. DPAPI recovery and the ACL were checked; the temporary plaintext validation password is removed after use. CI needs separate secure provisioning of this keystore/password; the Windows local profile does not configure hosted secrets.

## Reusable export and validation

```powershell
./tools/Export-AndroidPublisher.ps1 `
  -WorkspaceDirectory artifacts/my-new-publisher-workspace `
  -EvidenceDirectory artifacts/release/my-new-publisher-export
# Add -Low for the Compatibility preset. Every output directory must be new.
```

The helper decrypts the local password only for the export/validation process, supplies Godot's supported signing environment, restores previous environment values, checks the real AAB upload signature, and validates a locally signed universal APK. Its short-lived password file remains inside the protected directory and is removed in `finally`; retained logs are checked for the literal password and redacted if necessary.

The actual first publisher **1.0.0 / version code 1** release-template AAB passes at `artifacts/release/publisher-1.0.0/`: **42,081,899 bytes**, SHA-256 `0ec611d978b0233dcebbfb9a170d8d69ffa2416ae8759505f3347a8a0c393b8f`, Taskscape upload signature, non-debuggable manifest, API 36/min 26, native ELF/ZIP 16 KB/independent zipalign, 54 ETC2 textures and editor/test stripping. The isolated source workspace is preserved. Local secret redactions: zero; temporary password removal passes. All 94 Python contracts pass separately at `artifacts/release/play-setup-20261028/`.

The owner approved uploading that exact AAB to an unpublished internal-test draft, without rollout. Google **accepted version 1.0.0/code 1** and reports API 26+, target 36 and arm64-v8a. The draft is saved with the notes “First internal test build of Block Dungeons 1.0.0.” The Google-registered upload certificate was then downloaded and its DER SHA-256 exactly matches the local upload identity above. This confirms registration independently of the local build. Public configuration records the draft URL, accepted bundle SHA and registration state. Version code **1 has now been uploaded**; use a higher code for the next different bundle rather than replacing this identity silently.

Evidence and explicit owner approvals are retained in `artifacts/release/play-setup-20261028/upload-proof.json`; `play-draft-saved.jpg` shows the initial accepted bundle and saved draft. That historical report predates the subsequent internal release availability. Store declarations/artwork, human copy/presentation review, actual Play-generated APK/split/device checks and production release remain separate acceptance gates. Apple credentials cannot be retrieved from Google Play and remain pending for the iOS workflow.

## Internal-test audience

On 2026-10-28 the owner requested a specific internal tester for the already-available 1.0.0/code 1 release. The dedicated **Block Dungeons Internal Testers** list contains that single account and is selected for this app; the pre-existing four-person list is not selected. A reload confirms the saved selection and active track. The agent changed only the tester audience in this step, without publishing another release.

The tester joins at `https://play.google.com/apps/internaltest/4701186886373700407` using the requested Google account. Public configuration stores the list name/count and opt-in URL; the personal email is confined to ignored local evidence in `artifacts/release/play-setup-20261028/internal-tester-proof.json`. `internal-tester-added.jpg` shows the active track and one-person selection. Eligibility is configured; tester opt-in, installation and physical QA are not inferred.
