# Store metadata draft

The editable English source is `platform/store/en-US.json`. It contains draft Google Play and App Store copy for the implemented v1 adventure. The owner supplied publisher details on **2026-10-28**: **Taskscape Ltd**, `info@taskscape.com`, support `https://www.taskscape.com`, privacy `https://www.taskscape.com/privacy.html`, copyright **2026 Taskscape Ltd**. These values are stored exactly. The owner selected version **1** for a new app: configured as **1.0.0**, Android version code **1**, iOS build **1**, Windows file version **1.0.0.0**, with matching export presets. Listing copy has **not** been approved or submitted. Store declarations, age ratings, pricing, countries, final icons/artwork and device screenshots remain separate work.

Draft title: **Block Dungeons**. Android short description: “Explore Mossy Hollow, battle monsters and find your way to the beacon.” Apple subtitle: “Sword, bow and forest ruins”. The shared description covers the one authored mission, sword combo/bow/roll, supplies/checkpoints, mission replay and compatible level-package import. It makes no multiplayer, procedural-generation, equipment or level-editor promise. Source evidence is `game/levels/mossy_hollow/level.json`, the Core mission/combat tests and the implemented settings/catalogue/import flows; actual iOS/device acceptance remains pending before publication.

`tools/store_metadata.py` validates the draft and exports plain UTF-8 text files into a new directory:

```powershell
. ./tools/Common.ps1
python tools/store_metadata.py check
python tools/store_metadata.py export --output artifacts/store/my-new-draft
# This currently returns 1, with each missing field listed:
python tools/store_metadata.py check --require-ready
```

The check enforces the current text limits, Apple's UTF-8 keyword byte limit, plain normalized text, strict keys, the reserved-reference rule in specification §1.4 and matching application identifiers in **both** Android presets and iOS. When versions are supplied they must match the Android normal/Low version settings and iOS short version. Existing output and source files are preserved. A pending review cannot contain an approval record. An approved record needs the actual reviewer, UTC timestamp, approval reference and the reported `copySha256`; changes to copy, publisher details, release fields or application identities invalidate that approval. The tool has no approve, upload or automatic replacement command.

| Field | Maximum |
| --- | ---: |
| Google Play title / short description / full description | 30 / 80 / 4,000 characters |
| Apple name / subtitle / promotional text / description | 30 / 30 / 170 / 4,000 characters |
| Apple keywords | 100 UTF-8 bytes |

Limits checked on 2026-10-27 against [Google's listing reference](https://support.google.com/googleplay/android-developer/answer/9859152?hl=en), [Apple app information](https://developer.apple.com/help/app-store-connect/reference/app-information/app-information/) and [Apple version metadata](https://developer.apple.com/help/app-store-connect/reference/app-information/platform-version-information/).

The original publisher evidence remains under `artifacts/store/taskscape-publisher-20261028/`. The subsequent first-release evidence is `artifacts/store/first-release-20261028/`: the fresh draft export passes and **only human copy review** remains pending in local metadata readiness. A live uncached HTTP 200 fetch confirms the owner-updated [privacy page](https://www.taskscape.com/privacy.html) explicitly covers Block Dungeons, applications and games; the prior website-only observation is superseded. The exact fetched page and hash are preserved in that evidence directory. This verifies content scope, not legal accuracy or store acceptance. Earlier eight-/three-item reports retain their historical status.

All seven metadata contracts pass. Negative fixtures explicitly omit a privacy URL rather than assuming the live publisher fields remain empty; changed copy or publisher fields invalidate a synthetic approval. The other cases cover keyword bytes, normal/Low identities, release-version mismatch, reserved/duplicate fields and input/output preservation. Synthetic approvals remain test-only. CI/M8 invoke these checks; hosted execution remains pending.

`metadataReady` means the local metadata fields/review are complete. The result always keeps `storeSubmissionReady` and `physicalAcceptance` false: this checker cannot prove publisher ownership, public URL reachability, store declarations, artwork, signed delivery or device execution. HTTPS support/privacy links are this project's input rule, and are syntax-checked only. The desktop still-image approval does not approve a store listing or replace mobile screenshots.
