# Reviewed visual profiles

The human user approved **`windows-rtx3060-mobile-host47`** on 2026-10-27: the exact 22-scene Windows RTX 3060 Mobile/Vulkan still-image candidate. Its manifest records the explicit chat approval, original candidate/contact-sheet hashes and restricted review scope. Raw captures remain unchanged. Motion, audio, other renderer/GPU/device contexts and gameplay replay goldens are excluded from this approval. Missing or pending profiles still fail the nightly visual gate.

Use `tools/visual_baseline.py prepare <passed-full-capture-run> <new-candidate-directory> --profile <id>` to collect every scenario from the executable's catalogue. The command copies images and raw sidecars and records both file hashes. It always writes `reviewStatus: "pending"` and `review: null`; it has no approval switch.

After explicit human acceptance of the full-size images, copy that exact candidate directory here as `<id>/`, preserving its `cases/` files and hashes. Record `reviewStatus: "approved"` and a `review` object with the actual reviewer's name, UTC `approvedUtc` timestamp and review `reference` (for example, a review URL or the dated approval in the project record). Do not invent a reviewer, reuse approval for changed images, or update expected images automatically after a failure. Raw sidecars retain `baselineApproved: false`: approval belongs to the separate reviewed manifest.

Each profile contains `manifest.json` plus `cases/<scenario>.png` and `cases/<scenario>.capture.json` for the exact catalogue. Images use the repository's Git LFS rule. The manifest is version 1, with `profileId`, `reviewStatus`, `review`, `preparedUtc` and `cases`; every case records `scenario`, `image`, `capture`, `imageSha256` and `captureSha256`.

Review profiles independently for each actual renderer/adapter/quality context. The configured hosted profile is `linux-gl-llvmpipe`; the local Windows RTX 3060 candidates do not approve it. The checker also requires unchanged dimensions, engine version, fixed visual/shader clocks, level/content hashes, seed, tick and final Core hash. A context/source change requires a new reviewed candidate, even if pixels happen to match. Physical-device acceptance and replay golden review remain separate.

See [capture and comparison commands](../../../docs/visual-captures.md) and [nightly scope](../../../docs/nightly-hardening.md).
