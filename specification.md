# BLOCK DUNGEONS — Technical Specification

Working title: **Block Dungeons** (codename `dungeons`)
Document version: 1.1 — Status: Implementation-ready (1.1: merged review of earlier draft material; see §25 for the decision record)
Reference inspiration: concepts, mechanics and visual language of *Minecraft Dungeons* (see §1.4 for the legal boundary)

## Implementation progress (updated 2026-10-29)

This checklist is kept up front. `[x]` means implemented and checked; `[ ]` means pending. Deliverables and milestone acceptance are tracked separately: a scaffold is not a passed export gate.

The current requirement-to-evidence audit is in `docs/completion-audit.md`; concrete store/toolchain gaps are in `docs/release-readiness.md`.

- [x] **Per-push release artifact implementation** (§20.9): added the `main`-only workflow for a Windows Inno Setup installer, portable ZIP and publisher-signed Android APK/AAB, with per-run identity/checksums, version-code allocation, signature checks and separate downloadable diagnostics. Local implementation checks pass; hosted execution and trusted Windows identity acceptance remain separately pending below.
  - [x] Added disposable-checkout build identity/version allocation: both Android presets and metadata are stamped, display version remains unchanged, stale/uploaded codes and invalid input fail before writes. Eight version contracts pass; workflow syntax/action lint and PowerShell parsing pass.
  - [x] Fresh Windows release export, actual Inno installer and portable ZIP pass local packaged launch, checksum validation, installation, byte-for-byte payload parity, installed launch and uninstall. Export/runtime/notice payload is retained; logs/symbols are excluded. Final packaging/install checks pass at `artifacts/release-ci-validation-20261029/windows-final/` and `windows-install-final/`. Windows remains explicitly unsigned.
  - [x] A real registered-key release AAB and derived universal APK pass local CI-helper validation at code **1001**, with independent Taskscape certificate, manifest/version, texture/stripping/native alignment and zipalign checks; runner-temp private files are removed. The final helper run exits zero; disposable source/downloads/evidence: `artifacts/release-ci-validation-20261029/source/artifacts/release-ci-final/`. No upload or device check is performed. Local filenames use an explicitly labelled all-zero placeholder SHA; a real Actions run supplies the pushed SHA.
  - [x] All **102 Python contracts**, all workflow actionlint checks and PowerShell parsing pass. Eight actual failure guards reject absent/malformed/wrong Android credentials, incomplete Windows signing inputs/export, existing output and tampered downloads; private cleanup passes on failure. The optional Windows signing route also signs with a temporary self-signed fixture, rejects its untrusted chain, removes the imported certificate/key, and still launches the signed embedded-PCK game. This is rejection/command-path evidence, not trusted publisher approval (`guard-results.json`, `windows-untrusted-signing-result.json`).
  - [x] GitHub publication preflight (2026-10-29): the existing initial source commit `8d2a70c` is preserved; owner-selected `origin` is `https://github.com/mzagozda/Dungeons.git`, whose destination is empty. All 193 LFS assets pass fsck; tracked paths exclude private signing material, toolchains, generated builds and the design archive. No oversized ordinary Git blobs are present.
  - [ ] First successful hosted Windows/Ubuntu builds and repository-secret provisioning remain pending. The GitHub repository currently has no Actions signing secrets configured. Setup is documented in `docs/ci-release-artifacts.md`; regular CI remains independent, and no store upload/rollout is added.
  - [ ] Optional Windows Authenticode acceptance using a real trusted signing identity. The configured path fails closed; absent optional credentials produce explicitly unsigned Windows downloads.

- [ ] **M0 — Toolchain and export gate** (§3, §5, §16.5, §18, §20.9, §21, §22): in progress.
  - [x] Repository initialized, Git LFS configured, SDK/host candidate pinned, solution and coding standards added. Source is included in initial commit `8d2a70c`; the owner-selected GitHub remote is configured above.
  - [x] Empty Godot C# scene: spinning cube, localized label, JSON read from `res://content/`; headless and packaged Windows runtime verified.
  - [x] Windows, Android arm64 and iOS arm64 export presets; build/test/export/device-check scripts and CI skeleton implemented. Windows and Android exports exercised successfully; iOS and hosted CI not exercised.
  - [x] Local warning-free build (5 projects, both Core targets), 2 Core architecture tests and headless JSON/translation/Core-link/rotation probe passed.
  - [x] Exported Windows app runs and reads packaged JSON; rendered cube/label verified on NVIDIA RTX 3060 with Mobile/Vulkan. Screenshot: `artifacts/windows/BlockDungeons.png`.
  - [x] Physical Android USB install, cold launch, packaged JSON/translation/Core link and cube rotation passed on `2107113SG` / `d211b69` (Android 14, HyperOS, Adreno 660 / Vulkan 1.1.128). Installed APK hash verified; corrected sensor-landscape setting and rendered screenshots verified on device. Evidence: `artifacts/m0/android-device.json`, `android-probe-landscape.png`.
  - [ ] Physical iPhone install/launch and packaged JSON read, including Documents path verification. User deferred Mac/iPhone checks.
  - [ ] Hosted CI builds Core/tests and exports Windows. First successful hosted execution remains pending after GitHub setup.
  - [x] Prepared the adopted-host iOS handoff: complete hash-verified official template installation includes iOS; offline inspection finds iOS 14 minimums in the 4,743 inspected objects from the Godot Debug/Release and MoltenVK arm64 device archives. A separate isolated project/unsigned-archive helper, native Files-sharing preset transformation and manual/reusable nightly Mac workflow are implemented. Three preset contracts, PowerShell/non-Mac guards and all workflow lint pass. No Mac export/link, automatic signing or device acceptance is claimed; generated C# framework deployment metadata still requires inspection. Evidence: `docs/ios-handoff.md`.
  - [x] Added a read-only iOS archive prerequisite guard to the unsigned Mac archive path: compare archive/app bundle identity, Documents/Files keys, iPhone/iPad landscape support, iOS 14 plist minimums and every embedded Mach-O deployment command/architecture; require main app SDK 26+. Thin/fat 32-/64-bit containers and legacy minimum commands are supported; simulator/x86, higher native/framework minimums, malformed inputs and output overwrite are rejected. Twelve synthetic archive contracts, all 69 Python contracts, workflow lint and non-Mac no-write preflight pass (`artifacts/host-migration/ios-archive-audit/`). Actual Mac archive auditing remains pending and earns no signing/device acceptance.
  - [x] Prepared the §18.3 opt-in automatic iOS signing route: Mac workflow accepts an explicitly enabled signing input, real team/API identifiers and a scoped private-key secret; it gates Apple provisioning on a matching successful release-template unsigned archive/project fingerprint, uses Xcode automatic signing, exports an IPA without upload, audits the signed archive/extracted app and checks distribution team/signatures. Secret material stays in a restricted runner-temp file and is removed in `finally`; default runs remain unsigned. Six synthetic signing-plan contracts, extracted-app audit parity, all **94 Python contracts**, workflow/PowerShell lint and actual non-Mac no-write rejection pass (`native-aot-main/ios-signing-contracts/`). No credentials were used or actual Mac/signing success claimed. Setup/manual TestFlight steps: `docs/ios-handoff.md`.
  - [x] Reproduced the iOS NativeAOT serialization defect locally: with reflection disabled, settings/profile save and replay writing fail; strict analysis inventories 49 AOT/trim diagnostics. An isolated repair passes zero-warning shipping analysis, all 478 normal/464 coverage Core cases at 98.42%, and actual Windows NativeAOT persistence/catalogue/full-clear execution (11,591 ticks, unchanged `a6da84d1de933f5c`). All four JSON payloads remain byte-identical. Evidence: `docs/native-aot.md`, `artifacts/host-migration/ios-aot-audit/`.
  - [x] Hardened the isolated validation candidate against a vacuous test pass: restore the Debug test graph after shipping-subset AOT analysis, require actual complete passing TRX reports, and reject orphan/native-leak diagnostics. Captures now release their static gameplay binding and drain the scene before quitting; the ownership regression passes headless and on both renderers, and eight repeated full-clear captures pass with unchanged pixels. Earlier failed gates remain preserved and explained in `docs/native-aot.md`.
  - [x] The isolated AOT/capture candidate passes all 22 repeated screenshot pairs and comparison against the human-approved Windows still-image profile: every case has unchanged identity fields and **zero mismatched pixels** (`artifacts/host-migration/ios-aot-approved-comparison/`). This preserves the approved baseline without replacing it or extending its scope.
  - [x] Complete isolated AOT/capture rendered gate passes: **478 normal/464 coverage Core cases, 98.42% coverage, 93 headless Godot cases, 45 UI/four effects/nine editor cases per renderer, 57 Python contracts**, simulation fuzz, 22 launch contracts, 22 identical capture pairs, strict AOT analysis, both short native soak diagnostics and real CLI thumbnails (`artifacts/host-migration/ios-aot-capture-drain-full-gate.launch/`, candidate M9 `gate-11cad17c0db842c582c5e2ad68c98d5e`). Actual TRX reports confirm every Godot suite executed with no skips or orphan/native-leak diagnostics.
  - [x] Fresh candidate Windows/normal/Low Android packages pass: Windows rendered smoke, 27 packaged launch contracts and 54 desktop textures; both APKs pass API 36/min 26, editor/test stripping, 54 ETC2 textures, all eleven native libraries' ELF/ZIP 16 KB checks and independent zipalign. All three contain matching Core bytes. Debug/publisher checks correctly remain rejected. The initial texture-audit invocation omitted its required arguments; the corrected successful run is retained separately (`artifacts/host-migration/ios-aot-package-candidate-corrected/`).
  - [x] Activated the 33-file NativeAOT/capture repair in the main workspace, with preflight/hash verification, previous source/packages preserved and concurrent documentation edits retained (`artifacts/host-migration/native-aot-adoption.json`, `pre-native-aot-20261027/`). The fresh main Windows export/rendered smoke passes, and 879 implementation/data files are archived for the repaired checkpoint (`native-aot-main/implementation/`). C# 12/Core targets and simulation behavior remain unchanged; six Core files change JSON construction only.
  - [x] Repaired main-workspace full rendered gate and fresh package validation pass: **478 normal/464 coverage Core cases, 98.42%, 93 headless Godot cases, 45 UI/four effects/nine editor cases per renderer**, 22 source/27 packaged launch contracts, 22 identical capture pairs and 22 approved comparisons with zero pixel differences. The gate runs the 57 existing Python contracts; all 69 pass separately after adding the twelve offline iOS archive cases. Windows/normal/Low packages share Core SHA256 `04dbf1072934e674dffbce05847a5ecff19b2897278009da42c6684fcfff3af4`. All Android SDK/stripping/54-texture/eleven-library 16 KB/zipalign checks pass; Windows payload matches the private running soak. Actual main Windows NativeAOT checks also pass. Evidence: `artifacts/host-migration/native-aot-main-full-gate.launch/`, main M9 `gate-b6db3bae77ec4112b2669e0fe077c003`, `native-aot-main/packages/`.
  - [x] Repaired-main performance reproducibility passes all six workloads/18 processes/144 samples and unchanged budgets. The owner approved the exact .NET 10/twelve-warmup reference on **2026-10-28**; it is now canonical at `assets/baselines/performance/windows-r5-7600x.json`, with exact approval/hash provenance in the adjacent `.review.json` and the old .NET 8 reference preserved under `archive/windows-r5-7600x-net8-pre-host47.json`. Both independent datasets pass again against the canonical file; the repaired-main largest increase is **+2.59%**. Original +20%/absolute/allocation limits are unchanged (`artifacts/approvals/baselines-20261028/`).
  - [x] Both repaired-source normal/Low QA AAB pipelines pass: isolated fresh source exports, owned QA signing, bundletool validation, offline generated universal APK SDK/manifest/stripping/54-texture/native-16-KB/zipalign checks and real negative contracts (`native-aot-main/bundles/`). Actual publisher signing, device splits, physical execution and store delivery remain pending.
  - [x] Added offline generated **split APK** validation for both normal/Low AABs: synthetic arm64 API **26/34/36**, density 480 and en/pl resource selection each produce six required APKs (base/ABI/languages/density/install-time assets). Every selected APK signature and independent zipalign pass; all eleven native libraries and all **646 asset entries** match the validated universal payload byte-for-byte, including the game DLLs/textures in the install-time asset pack. SDK/orientation/renderer/import, resource/type stripping and native 54-texture checks pass. Actual API 25/x86-64 profiles are rejected. Eight new synthetic contracts and all **87 Python contracts** pass (`native-aot-main/splits-normal/`, `splits-low/`, `split-contracts/`). No device is queried/installed; real publisher/physical/16-KB-device/store acceptance remains pending.
  - [x] Updated cold QA bundle pipeline also passes end to end with its new split gate: new isolated source/export, owned QA signing, universal validation/negative contracts, all three synthetic split profiles and both incompatible-profile rejections (`native-aot-main/split-full-pipeline/`). The same gate is wired into the existing CI bundle pipeline; hosted execution remains pending.
  - [x] Final repaired package's own two-hour Windows/Vulkan soak passes at **22:18:50 UTC** (`artifacts/soak/windows-mobile-2h-aot-20261027/`): native **7,239.30 seconds**, **423,904 ticks**, **359,857 drawn frames**, 20 clears/19 ambushes/19 respawns/20 ordinary attempts and longest frame gap **1.308 seconds**. Native, external and aggregate results pass; all 198 package files and 879 implementation files remain unchanged, loaded modules match, keep-awake is released and strict logs contain no errors/leaks (`final-verification.json`). Mac/iOS link, physical/platform budgets and human reviews remain pending.
  - [x] Windows application resources (§18.1), checked **2026-10-28**: x64 PE, original cube icon and version resources are present. Removed the obsolete `M0 platform probe` file description; a fresh rendered Windows export now reports `Block Dungeons`. Packaged UI/palette checks and restored warning-free Debug build pass (`artifacts/approvals/windows-resources-20261028/`). Native code sections and all runtime DLLs match the soaked package; 548 packed resources match, and four QA/editor scenes differ only in their single generated root-node IDs. Only the Windows description line changes among the 879 archived implementation files. The old soak package remains unchanged; this resource rebuild does not claim a new two-hour run or final publisher/store-artwork approval.
  - [x] Isolated release-stack feasibility probe: official Godot 4.7.2/.NET 10 exports an API 36/min 26 debug APK with all eleven arm64 libraries passing ELF/ZIP 16 KB checks, independently confirmed by Android zipalign. Both native desktop renderers verify the existing full-clear/ambush/death recordings using the unchanged net8.0 Core DLL. Exact hashes and limitations: `docs/release-toolchain-candidate.md`, `artifacts/release-stack-probe/evidence.json`.
  - [x] User approved the newer host/build toolchain on 2026-10-27, retaining C# 12 and Core's net8.0/netstandard2.1 targets. This explicitly overrides the host/SDK .NET 8 pins in §3.1/§3.5.
  - [x] Activated the validated full-game Godot 4.7.2/.NET 10 port in the main workspace: SDK/editor installers pass, 185 changed/new files are hash-verified, and original files/pins are preserved under `artifacts/host-migration/pre-adoption-20261027/`. Core source/targets remain unchanged. The isolated port passes the complete rendered gate, 92 integration cases in each mode (headless/Vulkan/Compatibility), fresh normal/Low Android and Windows exports, 27 packaged launch contracts, 54 desktop texture checks and native packaged soak diagnostics. Both final APKs pass API 36/min 26, all eleven native libraries' 16 KB checks and independent zipalign; debug/publisher signing remain pending. Evidence: `docs/host-migration.md`.
  - [x] Post-activation main-workspace regression and fresh packages pass: **478 normal/464 coverage Core cases, 98.42% coverage, 92 headless Godot cases, 44 UI/four effects/nine editor cases per renderer, 46 Python contracts, 22 source and 27 packaged launch contracts, 22 identical screenshot pairs**, 54 desktop textures and both packaged soak diagnostics. Normal/Low APKs pass strict payload/manifest/stripping, API 36/min 26 and all eleven libraries' ELF/ZIP 16 KB checks; Windows rendered smoke and locked Debug restore/build pass. All three packages contain matching Core bytes. Evidence: `artifacts/host-migration/combined-main-lock-path-fixed.launch/`, `payloads.json`; main M9 gate `artifacts/m9/gate-734d48ceaba24e2caf93206f62d7a693/`.
  - [x] New-host six-workload performance reproducibility: the independent twelve-warmup run passes all 18 processes/144 samples, unchanged +20% comparisons and absolute tick/allocation gates. Full Core checks pass again (478 normal/464 coverage, 98.42%). This reference was temporary at that checkpoint; its explicit owner-approved canonical adoption is recorded above. Earlier context failures and the three-warmup loading regression remain preserved. Evidence: `artifacts/host-migration/benchmarks-net10-warm12-independent/`, `docs/performance-baselines.md`.
  - [x] Windows D3D12 fallback (§18.1) is documented and exercised with the actual adopted-host package on RTX 3060: full-clear, ambush, death/respawn and ordinary mission flows pass with strict error/leak checks. This is a short diagnostic, separate from the Vulkan long soak and approved visual profile. Evidence: `artifacts/host-migration/d3d12-diagnostic/`.
  - [x] Adopted-host Windows/Vulkan two-hour soak passes native and external results: **7,237.23 seconds, 423,908 ticks, 359,856 drawn frames**, 20 clears/19 ambushes/19 death-respawns/20 ordinary attempts; longest gap 1.435 seconds. Package files are unchanged, loaded modules match and the keep-awake request is released. Its 875-file source checkpoint is archived (`artifacts/soak/windows-mobile-2h-host10-20261027/`, `artifacts/host-migration/source-seal/`). This checkpoint predates the isolated NativeAOT/capture repair, which requires its own final-package checks. At that historical checkpoint physical acceptance, signed release, canonical-reference review and hosted CI remained pending; the later canonical-reference adoption is recorded above.
  - [x] Approved-host port builds warning-free and passes 478 normal Core cases (464 functional + 14 allocation/performance cases in separate processes), 464 coverage cases at 98.42%, and all 92 headless Godot cases. SDK/editor installers run successfully. A standalone same-DLL control reproduces .NET 10 allocation-counter noise during background collections; exact assertions remain unchanged. Rendered/package gates are in progress; evidence and retained failed attempts: `docs/host-migration.md`.
  - [x] Disabled Android editor one-click deployment to address the original teardown diagnostic; batch exports/device checks remain available. One intermittent `_EDITOR_GET` teardown diagnostic recurred in M3 and is retained in its failed-run log; it is not allowlisted.
  - [x] Resolved the local Android export timeout: automation now uses the direct Windows editor executable instead of the console wrapper that waits for Gradle daemons. Export exits successfully. See `docs/m0-validation.md`.
- [x] **M1 — Core simulation skeleton**: local gate passed under the user-authorized M0 exception below. Evidence: `docs/m1-validation.md`.
  - [x] Math and coordinate conventions; xoshiro128** streams; FNV-1a known-answer tests.
  - [x] Entity/World/session state and ordered systems scaffold; deterministic movement and bounded event buffer. Later gameplay stages are explicitly reserved.
  - [x] Terrain collision, height/stairs rules, two-pass spatial separation and shared NavGrid/A*; weighted paths checked against a Dijkstra oracle.
  - [x] ScenarioBuilder, complete M1 state hashing, bounded in-memory RLE replay recording/verification with first-divergence reports. JSON replay identity/serialization follows with M2.
  - [x] SC-MOV-01/02/03, collision/A*/hash/RNG/replay tests: 39 passed in Release; empty and 65-actor recorded loops allocate zero bytes after warm-up. Core line coverage 98.41% (gate ≥85%).
  - [x] Real BenchmarkDotNet M1 fixtures executed: empty tick, hero + 64 stationary enemies, open 256×256 A*. Full combat/Mossy Hollow benchmarks remain later work.
- [x] **M2 — Level format and content**: local gate passed. Evidence: `docs/m2-validation.md`.
  - [x] Content definitions/property schemas, immutable registry, deep `extends`/elite multipliers and SHA256 content identity; shipped v1 data transcribed from the specification. Referenced art/audio files remain later milestones.
  - [x] Mossy Hollow JSON loaded with zero errors/warnings; strict parsing, duplicate-key/encoding/culture tests and initial semantic diagnostics passed.
  - [x] Bounded in-memory ZIP packages, path traversal/duplicate/bomb rejection and deterministic round trips; pure catalogue keeps invalid entries and applies user/version precedence.
  - [x] Versioned level document/loader, embedded JSON Schema, all listed diagnostic fixtures (the overlapping LVL1030/1031 rules share a fixture), populated environment defaults and typed authored metadata.
  - [x] CLI validate/pack/unpack/migrate/preview/replay exercised; authoring edits survive serialization; stored spawner-arena fixture validates.
  - [x] 83 Release tests, 96.49% Core line coverage, independent draft-2020-12 validation of 40 shipped JSON files and 256×256/64-monster load budget passed.
  - [x] M2 checkpoint 10-minute fuzz: 593,624 cases, no boundary failures or >2 s loads, longest 105.39 ms. The published Core SHA256 matched the Release binary at that checkpoint; later milestone changes are tracked separately.
  - [x] Godot editor, rendered Windows export and physical Android USB install/cold launch load the packaged registry/level and round-trip/verify a 120-tick idle replay. Desktop hash matches the CLI; Android report confirms success. Actual gameplay goldens remain M5/M6.
- [ ] **M3 — Rendering of levels**: implementation, desktop and available-Android checks passed; reference-device/platform acceptance remains pending.
  - [x] Original 48-colour master palette, 17 deterministic 16×16 texture sources and 1024×1024 atlas with 2 px bleed gutters; palette/size/staleness checks passed.
  - [x] Original editable cuboid model sources and rigid animation keyframes, GLB exports, three tree/two wall variants. All 18 referenced entity/tile model variants, required clips, palette textures and import settings pass asset checks.
  - [x] Worker-thread 16×16 chunk mesher, four-step stairs, thin bridge/riverbed/water geometry, corner AO, nearest shader materials and deterministic MultiMesh variants/rotations. Six chunks/four water meshes render; CPU geometry and real-engine foundation checks pass.
  - [x] Eight GdUnit4 C# runtime tests: full scene/HUD/first-objective wiring, render foundation, axes/screen-up movement, 60/120/144 Hz interpolation continuity, authored environment/camera bounds, nearest material guard, eight-light cap with 20 extra torches, and catalogue IO. Debug toggles use real input events.
  - [x] Authored environment, interpolated camera focus/bounds, eight-light culling, DDA occluder dithering, desktop menu/catalogue/level details, health/objective/message HUD and F3 debug counters with F4–F7 navigation/collision/trigger/path layers.
  - [x] Desktop rendered bridge/stairs/ruin captures pass draw-call/triangle/frame limits; 90 Release Core tests, 96.54% Core coverage and 8 Godot tests pass. Exported Windows probe and real level/HUD launch pass. Evidence: `docs/m3-validation.md`.
  - [x] Pixel-art magnification enforced by exact texel fetches while retaining mipmap/anisotropic minification; per-vertex authored sRGB tints are converted to linear. Animated gates and static instances share 0.15 s dither fading; committed CPU geometry is released after upload.
  - [x] Clean Android rendering/performance rerun after removing unsupported FSR1: fresh APK installed/cold-launched on d211b69; PID-specific log contains no runtime errors. 1,814 frames, p95 8.448 ms, ≤127 draws, ≤44,584 triangles, 54,864,512 texture bytes and 7 lights; screenshot inspected. Evidence: `artifacts/m3/android-0faeebf013c74f37931cf821f532a6ba/`. iPhone/reference GPU/hosted CI remain pending.
- [x] **M4 — Hero**: local gate passed under the authorized pending-platform exception. Evidence: `docs/m4-validation.md`.
  - [x] Data-driven sword/bow definitions, immutable loadouts, typed hero action rules, projectile/pickup effects and faction metadata; JSON Schema/content-reference checks and 92 Release tests pass.
  - [x] Simulation action states, exact 24-tick roll/iframes/cooldown, three-hit melee chaining, bow windup/ammunition, pooled swept projectiles, deferred damage/armour/grace, potion and pickup attraction/effects; all mutable action/launch state and immutable tuning participate in hashes.
  - [x] Keyboard/mouse/gamepad InputMap defaults, camera projection, 240 Hz input latch, focus-loss/disconnect pause, Core auto-aim/LOS/leading/mouse snap, original sword/bow/pickup geometry and action clips, reusable arrow views, health/ammo/cooldown HUD and scheme glyphs. Physical gamepad QA remains M7.
  - [x] SC-ROLL/CMB/RNG-02/PICK, actual incoming hits on all 24 roll frames, latch, replay and zero-allocation combat/projectile loops; 126 Release tests, 97.03% Core coverage and 11 Godot tests pass. Recorded movement walkthrough traverses Mossy Hollow's all-gates-open terrain without enemies; authored mission clear remains M5/M6.
  - [x] Full rendered runner and roll/bow/melee/pickup captures pass; desktop bridge/stairs/ruin p95 ≤1.897 ms, ≤179 draws, ≤69,110 triangles on RTX 3060. Platform/reference-device acceptance remains separately pending.
- [ ] **M5 — Enemies and AI**.
  - [x] Typed brain strategy/perception/range/telegraph definitions, staggered sight/hearing and allied alerts, zombie chase/attack/return and skeleton positioning/retreat/aim/recovery state machines. SC-AI-01/02, facing lock, cover cancellation and exact ranged cadence pass.
  - [x] Queued navigation policy, live monster cap/deferred FIFO spawns, 30-tick emergence, pooled runtime entities, independent loot rolls/scatter/arming, per-type kill counts and difficulty-aware spawning/replays. 146 Release tests pass, including a zero-allocation spawn/kill/loot loop; Core coverage 97.46%.
  - [x] Pooled projected health bars, damage numbers and alert markers, presentation timers/visual RNG, damage flash decay, emergence clip and speed-scaled walking/pickup spin. The original 12 Godot scene tests pass, including real melee feedback and unchanged Core hashes; rendered combat inspected.
  - [x] Rendered combat inspected; actor pools now prewarm per definition (16 melee, 8 ranged, 64 per arrow/emerald type), characters have 0.6 m shadow blobs, terrain-hit arrows use a bounded two-second decoration pool and melee hit-stop honors reduce motion. The expanded 13-test Godot suite verifies pool counts, effect expiry and unchanged Core state. Surplus authored monsters defer with immutable placement settings/IDs; focused queue tests pass.
  - [x] Expanded route scenarios, 64-brain/32-arrow stress fixture and real BenchmarkDotNet combat window. Full runtime checkpoint passes 163 Release tests, 97.54% coverage, 13 Godot tests, rendered captures and local CPU/GPU budgets. Evidence: `artifacts/m5/gate-e12823a2fa2a4626b0d38a8cb0686d47/`.
  - [x] AnimationTree state machines drive idle/walk, split melee phases, aim/fire, roll, hit/stagger, death and emergence from read-only Core snapshots. Per-phase timing follows combo/cooldown rules, walking follows actual velocity, normal fades take 30 ms and newer interrupts replace unfinished transitions. Pooled trees reset, shared graphs retain independent playback, AnimationPlayer remains a clip library and presentation-only hit-stop freezes both participants. Five pure cases and all 34 Godot cases pass; native combat capture passes (`artifacts/m5/pose-policy/`, `gdunit-animation-phases-final/`, `animation-tree-combat.png`). Physical animation acceptance remains pending.
  - [x] Separate visual transforms provide a 0.1 s hit squash and a final 0.5 s death sink without moving Core wrappers/shadow blobs. Death emits one dust puff, and respawn clears the pose. Two preallocated alpha-dust/additive-spark square batches cap all live particles at 2,000; damage, pickups and chests emit bounded bursts. Authored shake, heavy hero hits and opening gates drive decaying camera offsets capped at 0.25 m; reduce motion clears shake and bypasses squash. Eight pure presentation cases and all 36 Godot cases pass, including real death/respawn, overflow/expiry and unchanged hashes (`artifacts/m5/presentation-policy/`, `gdunit-effects-followup/`). Native combat capture validates uploaded billboard sizes/orientation (`effects-combat.png` / `.log`). The combined gate passes **295 normal/287 coverage cases**, **97.68% Core coverage**, **36 Godot cases**, native captures and renderer relaunch (`artifacts/m8/gate-00dc3c5a94b6461c92107f1183a20944/`), desktop p95 <=1.899 ms. Physical effects acceptance remains pending.
  - [x] Authored torch flames and vertical beacon streams share the existing 2,000-square particle cap, cull offscreen and use a bounded frame-independent emission clock. Locked exits use authored 0.2 emission versus 2.5 unlocked and show a chain when approached. Pickup events drive a preallocated 64-glyph, 0.65-second fly-to-HUD pool with live safe-area/touch targets and reduce-motion cancellation. Four pure clock cases pass; six effects cases pass on each real desktop renderer, including actual pickup collection/removal, pool expiry/overflow, locked/unlocked emission and unchanged Core hashes (`artifacts/m5/pickup-native-complete/`, `pickup-compatibility/`). Native captures inspected; combined regression and physical effects acceptance follow separately.
  - [x] Combined effects regression passes **421 normal/408 coverage cases**, **98.31% Core coverage**, **51 Godot cases**, four ambient/pickup cases on each real renderer, all M3–M8 captures/relaunch and nine editor cases per renderer (`artifacts/m9/gate-4de6881a8633414aa95a40f56684d1a7/`). Desktop p95 ≤2.786 ms, ≤189 draws/69,270 triangles; content, level and development replay hashes are unchanged. Refreshed icon provenance passes (`artifacts/m9/palette-36a24d6382d1464cb5cb9d1b6d9d36e6/`).
  - [x] Fresh normal/Low Android and Windows packages include these effects; strict import/export/manifest, mobile editor stripping, packaged Windows smoke/33-icon loading and restored warning-free Debug build pass. Package hashes are recorded in `artifacts/m5/ambient-pickups-packages.json`; transcript: `artifacts/m5/ambient-pickups-exports.log`. No new physical-device execution is claimed.
  - [x] VFX/SFX implementation follow-ups are complete: ambient/collectible effects and event-timed audio are exercised by the later M8/M9 gates; details and checkpoint identities are in `docs/m5-validation.md` and `docs/m8-validation.md`.
  - [x] Human-approved gameplay goldens: on **2026-10-28** the owner approved gameplay goldens only. The exact reviewed full-clear/ambush/death recordings are promoted to `assets/baselines/replays/mossy-hollow-v1/`, with explicit scope/attribution and unchanged SHA256 bytes. All three verify through the current Core CLI with final hashes `a6da84d1de933f5c`, `2a8bf663642acddd` and `395328b0fce05079`; before/after byte checks pass (`artifacts/approvals/baselines-20261028/gameplay-regression/`). Source changes remain uncommitted.
  - [ ] Motion/audio/platform acceptance. Gameplay-golden approval does not approve the recorded motion/mix, real-time pacing or physical devices (`docs/gameplay-review.md`).
- [x] **M6 — Level logic and flow**: local functional gate passed under the authorized pending-platform exception; golden review remains M5/M8 acceptance. Evidence: `docs/m6-validation.md`.
  - [x] Trigger-created gates also compile their `openWhen` objective bindings before ticks, keep preallocated implicit-trigger state and use the same `set_state` executor; missing spawn-property references are rejected at load. Seven shortcut/transition/initial-placement cases pass, including zero allocation during gate creation and every-tick replay (`artifacts/m6/mission-canonical/mzag_R5-7600X_2026-10-26_20_30_09.trx`).
  - [x] Initial actor/player-spawn overlap with a closed gate is rejected at load with LVL1012; code scenarios enforce the same boundary. Loader and runtime share footprint geometry. Open gates remain valid at those positions; 36 focused loader/mission/object cases pass (`artifacts/m6/mission-canonical/mzag_R5-7600X_2026-10-26_20_25_39.trx`).
  - [x] Loader compiles `openWhen` into a separate mission program while preserving authored serialization; generated trigger ids are validated and referenceable. SC-SPW-02 verifies identical every-tick simulation hashes against the explicit trigger, while replay envelopes still reject different exact source bytes. Terminal objective transitions, simultaneous monster-area coalescing and original gate-terrain identity have focused regression checks (`artifacts/m6/mission-canonical/mzag_R5-7600X_2026-10-26_20_23_59.trx`). Development candidates have been refreshed after this identity change.
  - [x] Runtime-created gates register collision/navigation footprints, validate bounds and unregister before pooled storage reuse. Closing preflights actor/respawn displacement and rolls back without state changes when no safe ground exists; deferred monsters revalidate gate-blocked positions without further RNG draws. Eleven object/lives cases pass, including the three added runtime edge cases (`artifacts/m6/runtime-objects/mzag_R5-7600X_2026-10-26_20_06_53.trx`).
  - [x] Trigger-created spawner instances share precompiled templates, use runtime-id ownership for independent live/deferred caps and contribute expected wave totals. A two-spawner scenario verifies six kills, independent caps, early-completion protection, deterministic replay and zero allocation during runtime creation (`artifacts/m6/runtime-spawners/mzag_R5-7600X_2026-10-26_20_10_13.trx`). Ownership changes development hashes; candidate replays have been regenerated, with no reviewed golden replaced.
  - [x] Runtime-created props/gates/lights receive views; removal hides original static batches and releases lights, and view bindings retain ids rather than pooled entity references. Native Vulkan object capture verifies two hidden batches and one replacement light (`artifacts/m6/objects.png`, `objects.log`). The combined 16-case Godot suite and refreshed full runner pass.
  - [x] Startup scaffold replaced by one compiled mission program: level-start actions execute at tick 0; array-order events/AND/not conditions, delayed cursors, enable/disable, flags, tagged death history, all action kinds, automatic objective evaluation, locked exits and frozen mission results are wired into Core. Nine focused mission/startup cases pass, including recorded replay verification and zero-allocation action ticks.
  - [x] Core gate footprints share collision/navigation/projectile flags, immediate opening passability and safe closing displacement; 45-tick transitions, one-time chest proximity/loot, forward checkpoint activation and 120-tick death/respawn/failure are implemented. Focused object/lives cases pass, including zero-allocation transitions/respawning and preserved enemy/inventory progress. Host results/retry/profile flow is checked below.
  - [x] Declarative spawners support immediate/proximity/sight/trigger activation, initial delay, intervals, ownership limits including deferred requests, Spawn RNG scatter, difficulty-scaled totals and completion events. Seven spawner cases pass, including the shipped six-monster arena, early-completion protection and zero-allocation spawn/death/recording ticks.
  - [x] `ScriptedPlaythroughTest` completes the unmodified Mossy Hollow in 11,591 ticks (3:13.18), using only player commands: all 15 monsters defeated, both chests opened, all objectives complete, checkpoint order 3, no lives lost. Every-tick replay verification matches a fresh session. Candidate replay: `artifacts/m6/mossy-hollow-scripted.dreplay`; golden review/commit acceptance remains pending.
  - [x] Atomic profile persistence counts attempts at launch, selects authored/override/run-counter seeds, retains records/totals once per completed run, backs up corrupt JSON and retains five automatic replay files. Four focused persistence cases pass, including replay verification and preserved manual files; host/store integration also passes Godot tests.
  - [x] Death/respawn, failed/retry, complete/continue, pause/restart and persisted profile binding pass the expanded 15-test Godot suite. Rendered completion uses the actual full-clear replay (71 emeralds, 15 kills, 0 deaths, 160 XP); completion/failure/death captures inspected. Visual QA uses explicit Dummy audio after a separate WASAPI device-init failure; production audio remains unchanged and unvalidated.
  - [x] Full-clear, death/respawn and ambush candidate replays are generated and verified in `artifacts/m6/candidates/`; the M8 footstep checkpoint's full-clear final hash was `0f3bbaeec7476db1`. M9's later metadata identity refresh is recorded below. Full-clear replay plus hash verification allocates zero bytes. Historical candidate manifests retain their original pending status; the exact later reviewed gameplay baseline is promoted above. Source changes remain uncommitted.
  - [x] Full combined runtime runner passes: 200 normal Release tests, 194 coverage cases, 97.66% Core coverage, 16 Godot tests, clean import/runtime captures and both combat/full-replay benchmarks. Full-clear benchmark mean 23.41 microseconds/tick; desktop render p95 ≤2.259 ms. Evidence: `artifacts/m6/gate-1ca4b8e8b83644a8b2bb9857fbfc54c1/`.
  - [ ] Global acceptance still requires motion/audio/presentation acceptance and required platform/device checks. Gameplay-golden review is approved; M8 settings/UI are implemented and checked separately. The M6 local gate does not grant remaining platform approvals.
- [ ] **M7 — Mobile**: implementation and local evidence are in progress; physical acceptance remains pending. See `docs/m7-validation.md`.
  - [x] Floating 60 dp joystick/20 dp dead zone, bounded multitouch, mirrored/configurable safe-area arc, tap-only melee, slide-off cancellation and bow tap/manual-release/cancel state. Camera-projected touch commands retain manual release aim across 240 Hz frames and suppress emulated mouse attacks.
  - [x] Original touch controls/icons/aim arrow, safe-area HUD margins, pause, Back/menu/quit routing and screen-on lifecycle binding. Native phone/mirror/tablet captures and replays pass; expanding canvas aspect fixes letterboxing. Physical gesture/lifecycle feel remains separately pending.
  - [x] Bounded device-profile rules and Low/Medium/High settings, adaptive 60/300-frame decisions, 0.05 scale steps, thermal history and 4/8-light limits. Shared runtime binds only the 3D scale while native UI/Core state stay unchanged; pause/level replacement preserve thermal history. Nineteen focused policy cases pass.
  - [x] Low Vulkan and Compatibility native captures pass after neutral MultiMesh colour and terrain colour-space fixes. Real desktop renderer relaunch passes new-PID/backend/retry-guard/argument/audio preservation checks with strict parent/child logs. Evidence: `artifacts/m7/relaunch-3e5f4095f6fc4524a19568575b33b87f/`.
  - [x] Hero hit/death haptic routing selects one 30/60 ms pulse and respects an enable control; active handheld/gamepad routing and pause/exit cleanup are implemented. Validated, bounded, atomic package import deduplicates exact bytes and preserves existing different content. Five host service cases plus a real Godot import/catalogue test pass. Persisted settings are covered under M8; physical haptics remain pending.
  - [x] Level Select native picker, Android API 26–28 Open with fallback and iOS Documents/Files instructions are implemented. Android application hooks compile into both debug APKs; generated manifest checks cover VIEW filters, vibration, landscape and a Low APK without a Vulkan requirement. Renderer rebirth waits for native import copies. Both exports and the follow-up 22-case Godot run pass; physical picker/intent/relaunch acceptance remains pending.
  - [x] Combined local gate passes **232 normal tests**, **226 coverage cases**, **97.66% Core coverage**, **22 Godot cases**, six native captures and a two-process renderer relaunch: `artifacts/m7/gate-4c682536954c41fd8e65a7455a884cfa/`. Desktop p95 ≤1.905 ms on RTX 3060. Import/rebirth follow-up: `artifacts/m7/gdunit-relaunch/mzag_R5-7600X_2026-10-26_21_43_55.trx`.
  - [x] Persisted settings and the draggable touch editor are integrated and locally verified under M8 below; device-profile provenance remains visible alongside saved/custom preference provenance in F3.
  - [x] Debug-build four-finger tap toggles the same overlay as F3. The bounded recognizer requires four overlapping contacts within 350 ms/16 dp, rejects drags/cancellation/fifth fingers and clears on focus/layout changes. Captured releases cannot fire gameplay actions. Nine pure cases and all 27 Godot cases pass (`artifacts/m7/debug-gesture/`, `gdunit-debug-gesture/`); physical finger acceptance remains pending.
  - [x] Mobile GPU texture variants (§13.1): an isolated asset-only Godot import project generates native ETC2 data; the export hook packages only those bytes for Android/iOS without temporarily rewriting desktop imports. All **54** 3D/source/extracted GLB textures now have checked lossless desktop settings, explicit mipmaps (off for actual character/arrow textures) and disabled automatic import overrides. Normal and Low Android exports pass exact APK byte/remap checks and real Godot loading of **54 textures / 21 model variants / 80 material slots**; only ETC2 variants are present, totaling **1,492,936 GPU data bytes**. Eight pure preservation/error/policy cases pass. Native desktop checks confirm all 54 originals remain uncompressed with their expected mipmaps. An inspected Compatibility capture uses actual APK resources (p95 0.885 ms on RTX 3060); this is desktop QA, with mobile physical quality/performance acceptance still pending. Complete regression and refreshed Windows export follow separately.
  - [x] Texture-inclusive complete local regression passes **447 normal/434 coverage cases**, **98.31% Core coverage**, **86 headless Godot cases**, 34 native UI/four effects/nine editor cases per renderer, **36 Python contracts**, all 22 application process contracts and 22 pixel-identical repeated Mobile scenario pairs (`artifacts/m9/gate-25c109767f5c4012803aa1d4810a9dd1/`). Strict source imports and palette/schema/audio/font checks pass. Normal/Low ETC2 payloads match for all 54 textures; four real exporter rejection cases preserve the original profile. Fresh Windows export, rendered packaged smoke, all 33 editor textures/UI resources and actual packaged 54-texture lossless/mipmap audit pass. Build and package/source hashes are recorded in `artifacts/mobile-textures/checkpoint.json`; desktop p95 ≤2.186 ms, session-free preview p95 1.347 ms Mobile / 0.464 ms Compatibility. All 109 C#/shader/project source hashes are unchanged; 54 import presets and export tooling intentionally change. Physical/iOS/hosted and visual review remain pending. See `docs/mobile-textures.md`.
  - [ ] Physical Android/iPhone/gamepad/safe-area/lifecycle/haptics/import checks, GPU captures and battery/thermal acceptance. The installed Android build predates M7/M8; the user deferred further device checks and instructed implementation to continue. iPhone/Mac and hosted CI remain pending under the authorized exception.
  - [x] Explicit host service boundaries now match §1.5/§18.3: `IHaptics.Play(pattern)` routes named hero-hit/death patterns through the active-device adapter, and `IPlatformPaths.UserLevelsDir` supports a separately owned Documents directory. AppRoot accepts an injected path service; discovery, import and Open Levels Folder use that same directory. Six focused Core cases and four Godot service cases pass, including replay/disabled suppression, pause/settings/scene-exit cleanup and actual catalogue discovery without moving the profile (`artifacts/platform-services/`). Physical pulses and iOS Documents execution remain pending.
  - [x] Complete gate-fix/service-adapter regression passes **478 normal/464 coverage cases**, **98.42% Core coverage**, **88 Godot cases**, **46 Python contracts**, **34 UI/four effects/nine editor cases per renderer**, all **22 launch contracts** and **22 repeated capture pairs**, plus both short native soak diagnostics (`artifacts/m9/gate-329c2b31d2d34cf78ac138776d3e1181/`, `artifacts/platform-services/combined-detached.launch/`). This is local current-source evidence; package refresh, long-run checkpoint identity and physical/review/release gates remain separately tracked.
- [ ] **M8 — Polish and hardening**: settings/accessibility/audio local checks passed; remaining delivery and acceptance work is pending. Evidence: `docs/m8-validation.md`.
  - [x] Recorded and technically validated a **6:19 offline Windows gameplay review candidate** at 30 fps, using a private unchanged package and isolated profile through full-clear/ordinary recording/ambush/death-respawn flows. All three original replay identities verify; full MP4/preview decode and non-silent audio checks pass. The source seal still matches all 879 files. Actual video is 1600×900, with a smaller 720p preview and inspected contact sheet. The projected-oversize 60 fps attempt is preserved as superseded/failed. Human motion/audio/golden review remains pending (`docs/gameplay-review.md`).
  - [x] Implemented immutable reviewed-replay regression checks: explicit attribution/approval, complete case set, byte/identity/per-tick-hash checks, actual current Core playback and post-playback byte checks. Missing review returns 3; tampering/malformed data and overwrites are rejected. M6/CI now discover the promoted approved baseline automatically. All three actual approved-baseline playbacks pass, alongside ten replay and twelve performance comparator contracts; Tools builds with zero warnings/errors (`artifacts/approvals/baselines-20261028/`). Historical unreviewed proposal rejection remains preserved in `validation-guards/`.
  - [x] Human approved the exact 22-scene Windows RTX 3060 Mobile/Vulkan still-image candidate on 2026-10-27. Its unchanged images/sidecars are promoted to `assets/baselines/visual/windows-rtx3060-mobile-host47/`, with explicit review reference, candidate/contact-sheet hashes and restricted scope in the manifest. Comparison against the originating full capture run passes. Motion, audio, other renderers/devices and gameplay golden review remain pending; the full M8 visual/platform gate is not closed.
  - [x] Fresh independent rendered regression passes against that approved profile: all **92 native Godot cases**, strict import with zero diagnostics, short native soak diagnostic, **22 repeated scenario pairs** and all **22 reviewed baseline comparisons** pass. Evidence: `artifacts/host-migration/approved-visual-independent/`. This is a local Windows/Vulkan gate, separate from hosted nights and other profiles/platforms.
  - [x] §16.1 boot flow is active: explicit logo/loading screen, logo hidden after 1.5 seconds, worker-thread registry/catalogue validation, cancellation/drain on exit and observed failure/retry. All 92 cases pass headless and on each native renderer, including four dedicated boot cases (`artifacts/boot-async-workspace/artifacts/migration/`). The complete isolated gate and packages pass; post-activation main-workspace checks are in progress.
  - [x] Versioned video/audio/controls/accessibility settings model and atomic bounded store: first-launch preset persistence, independent editor snapshots, corrupt/duplicate/unknown-key recovery with backups, validation and storage failures. Fifteen settings-store cases pass in the latest combined suite.
  - [x] Main-menu/pause settings panel uses Apply/Back drafts and binds custom graphics, volumes, window/fullscreen/VSync, input rebinding, haptics and touch layout. A real GUI drag and actual menu→play→pause→settings→Back integration pass; Back keeps gameplay paused. Desktop window restoration and physical controls remain acceptance checks.
  - [x] Off/Normal/Strong aim assistance resolves into recorded commands; Normal/manual behavior and mandatory touch assistance are preserved. Strong uses half angular cost and 1.8 m cursor snap without changing combat range/LOS. Transactional touch positions preserve gaps/bounds/mirroring and safely fit smaller screens.
  - [x] Live damage-number toggle, patterned health bars, three colour-blind palettes, text size and reduce motion. Native desktop and maximum-scale phone previews pass after compact-layout/HUD fixes; original emerald/head counters and concise binding labels remain readable. These are local previews, not reviewed golden or physical-device acceptance.
  - [x] Combined runner passes **251 normal tests**, **245 coverage cases**, **97.66% Core coverage**, **26 Godot cases**, strict native captures and renderer relaunch (`artifacts/m8/gate-233fa97ca5344fafb548dc875cd444cb/`); desktop p95 ≤2.240 ms. The benchmark checkpoint records 114.1 microseconds/combat tick and 24.47 microseconds/full-clear replay tick (short runs, wide confidence intervals). Existing candidate replay identities remain unchanged.
  - [x] Follow-up graphics-provenance/unchanged-settings history checks pass all 26 Godot cases (`artifacts/m8/gdunit-final/mzag_R5-7600X_2026-10-26_22_38_59.trx`). Current normal/Low Android debug APKs pass strict import/export/packaged-manifest checks. Export and test runners now share the narrow pinned-engine import check; a prior editor shutdown diagnostic is retained rather than accepted as a clean run.
  - [x] Original procedural v1 audio bank: 39 sound IDs/46 Vorbis files, 44.1 kHz, mono SFX/ambience and a 144-second stereo score, editable source/provenance and checked Godot loop imports. AudioManager preloads SFX, incrementally decodes compressed music, bounds/steals oldest voices, uses independent presentation RNG, routes four buses, crossfades for one second, ducks music by 6 dB and pauses in background. Initial gameplay/UI/terrain-footstep/nearest-torch routing is bound; immutable event metadata survives removal. Seven bank-policy cases, two Core event cases and all 31 Godot cases pass (`artifacts/m8/audio-bank/`, `audio-events/`, `gdunit-audio-integration/`), including finite non-silent mixed PCM. Exact cue cadence/phase and listening/device acceptance remain below.
  - [x] Combined audio/gesture regression passes **269 normal cases**, **263 coverage cases**, **97.66% Core coverage**, **31 Godot cases**, all native captures and renderer relaunch (`artifacts/m8/gate-fe45c80ab73742f5b89d5aaa41e7963e/`), desktop p95 <=1.905 ms. Shipped JSON now enforces UTF-8 without BOM/LF; seed/audio generators preserve it. Candidate manifests refresh with regenerated content identities; initial hash `55a96a05c2252e8e`, full clear `a30bc2c7579400d3`, same 11,591 ticks. Audio F3/packaged-probe follow-up passes all 31 Godot cases. Listening/device acceptance remains pending.
  - [x] Audio cue timing now uses Appendix E's Footstep event every 15 consecutive voluntary moving ticks, with retained cell/height/definition/surface metadata and a hashed/reset pooled cadence counter. Standing/blocked/rolling/impulse-only movement emits none; pause preserves cadence. Melee whoosh binds AttackActive with retained combo metadata. Nine focused Core cases, including mission-only source compatibility and zero-allocation walking/hash checks, and all 32 Godot cases pass; soft/stone/wood routing uses actual Core events. Combined rendered/benchmark gate passes 276 normal/269 coverage cases, 97.68% Core coverage and 32 Godot cases (`artifacts/m8/gate-b71fa034ad4240d493989b04c40c67ff/`); development replay candidates are refreshed, with the same 11,591-tick clear.
  - [ ] Audio listening/balance and physical device playback review.
  - [x] App-scoped ILogger queues bounded records without tick IO and rolls UTF-8 JSON lines through five files of at most 1 MB (1,000,000 bytes). Retained level/seed/tick context, overflow counts, clipped/escaped records, interrupted-tail recovery, storage retry, shutdown/background flush and release-elided Debug wrappers are implemented. Eleven logger cases and the real menu/game/background persistence path pass, including complete Unicode scalar clipping. Final combined gate passes **287 normal cases**, **279 coverage cases**, **97.68% Core coverage**, **32 Godot cases**, all native captures and explicit renderer-relaunch logs (`artifacts/m8/gate-1bf93858b282410ab2106b6fea4cfd7e/`), desktop p95 <=1.903 ms. Both normal/Low debug APKs pass fresh strict import/export/manifest checks; Debug builds cleanly after restoring its dependency graph. Physical storage/lifecycle acceptance remains pending.
  - [x] Shared pixel typography and nine-slice UI skin (§13.1): pinned unmodified Silkscreen Regular/OFL notice and hashes, 12 original 16px hard-alpha palette panel/button/check/slider/arrow assets, six original bitmap symbol glyphs and deterministic offline regeneration/checks. Runtime theme installation passes strict imports, CanvasLayer branches and accessibility text scaling. Three font/menu cases pass on each native renderer; four existing settings cases also pass. Actual maximum-scale menu/attributed credits and desktop/phone settings/HUD captures are inspected (`artifacts/m8/pixel-ui-native-final/`, `pixel-ui-compatibility/`, `pixel-ui-previews/`). Compact headings preserve message/health separation; desktop objectives retain 24 px reference size. Full regression/export checks follow separately; final visual/device review remains pending.
  - [x] Focus loss during scene construction pauses simulation/clears input immediately and attaches only the pause UI on the next safe frame. Early resume cancels the queued menu. Twelve native input/settings/font cases pass, including deterministic focus loss inside `ChildEnteredTree`; the actual application screenshot also passes (`artifacts/m8/pixel-ui-focus-lifecycle/`, `pixel-ui-previews/app-focus-isolated.log`). Screenshot-only game instances isolate desktop input/focus events; normal play retains its lifecycle behavior. Full combined rerun follows separately.
  - [x] Final pixel-UI/lifecycle regression passes **421 normal/408 coverage cases**, **98.31% Core coverage**, **55 Godot cases**, four native UI/lifecycle cases and four effects cases per renderer, nine editor cases per renderer, all captures/relaunch, and palette/font/license/import audits (`artifacts/m9/gate-cbf5d60ac47444369289e247bb53b585/`). Actual TabContainer slots use the original pixel arrows/gold selected border; maximum-scale phone text bounds pass. Desktop p95 ≤2.912 ms, ≤189 draws/69,270 triangles. All content/level/development replay hashes remain unchanged.
  - [x] Fresh normal/Low Android and Windows exports pass. APK audits verify both font resources, all UI textures/theme/remap targets, the exact copyright/OFL notice and retained runtime type; editor assets/types remain stripped. The actual packaged Windows runtime verifies the upstream font SHA256, six glyphs, inherited nine-slice style and notice, alongside its existing smoke/palette checks. Restored Debug builds warning-free. Package identities: `artifacts/m8/pixel-ui-packages.json`; export transcripts: `artifacts/m8/pixel-ui-*-export.log`. No new physical-device execution is claimed.
  - [x] Level selection now uses responsive preview cards with author, difficulty pips, persisted best time/completion and user badges. Details retain descriptions/records for invalid entries, gate Play, and expose scrollable diagnostic codes; refresh restores selection. Eight native PNG interoperability cases and the real catalogue/resize/focus/long-press flows pass (`artifacts/m8/level-browser-native-visible-text/`); desktop card/detail captures are inspected. Optional thumbnails pass bounded dimensions/CRC/inflation/palette validation before native decoding. Compact/accessibility, full regression and packaged-preview checks follow separately.
  - [x] Catalogue-inclusive full local gate passes **432 normal/419 coverage cases**, **98.31% Core coverage**, **67 Godot cases**, **16 native UI/lifecycle/catalogue cases per renderer**, four effects and nine editor cases per renderer, all M3–M8 captures/relaunch and real CLI thumbnails (`artifacts/m9/gate-923895f81432449b98e744d98f1b05ad/`, `combined-catalogue-final.log`). Maximum-scale landscape phone safe-area/focus/heading checks pass; tall cards and descriptions scroll. Native card text is checked for actual rendered ink. Desktop p95 ≤2.922 ms, ≤189 draws/69,272 triangles; all content/level/development replay identities remain unchanged. Package and physical acceptance remain separate.
  - [x] Fresh normal/Low Android and Windows catalogue exports pass strict import/export/manifest/resource/type checks. Both APKs include the built-in imported preview and runtime catalogue UI, while excluding editor/test content. The actual rendered Windows executable decodes the remapped built-in thumbnail at 320×180 and passes JSON/replay/font/33-icon checks; locked Debug restore/build passes afterward. Package hashes and transcripts: `artifacts/m8/catalogue-packages.json`, `catalogue-*-export.log`. No installation or physical execution was performed; device checks remain deferred.
  - [x] Health/action presentation (§16.2): original potion/bow slots flank the health bar, display live cooldown/ammunition and share a clockwise sweep with the native touch potion target. The health group scales from 400×28/64px at 1920×1080; native 720p/1080p/1440p size and full-health border pixels pass. Strict below-25% health pulses the border and adds a bounded 0.3-opacity edge vignette; pause freezes the clock, reduce motion retains a static warning, and healing/death clear it. Three pure policy cases and six native HUD/binding cases pass (`artifacts/m8/hud-policy/`, `hud-native-reference-final/`), including real damage, potion use, bow fire, half cooldown at maximum-scale phone layout and unchanged Core hashes. Combined regression/package acceptance follows separately.
  - [x] HUD-inclusive complete local runner passes **435 normal/422 coverage cases**, **98.31% Core coverage**, **72 Godot cases**, **21 native UI/lifecycle cases per renderer**, four effects and nine editor cases per renderer, all captures/relaunch and actual CLI thumbnails (`artifacts/m9/gate-540a4e1f4cf3488e8f30c34b70ba0d21/`, `combined-hud-feedback.log`). Final HUD captures include ready terrain at all three exact render sizes and the maximum-scale native-touch cooldown. Desktop p95 ≤2.779 ms, ≤189 draws/69,272 triangles; all content/level/development replay identities remain unchanged. Physical and final visual acceptance remain pending.
  - [x] Fresh HUD normal/Low APKs and Windows export pass clean strict imports, packaged manifests, runtime HUD-type/resource checks and editor/test exclusion. The actual Windows game renders Mossy Hollow with the new health group (`artifacts/m8/hud-feedback-packaged-windows.png` / `.log`), alongside packaged JSON/replay/thumbnail/font/33-icon checks. Debug restore/build passes afterward with zero warnings/errors. Package hashes: `artifacts/m8/hud-feedback-packages.json`; transcripts: `hud-feedback-*-export.log`. No new physical-device execution is claimed.
  - [x] Mission HUD (§16.2): diamond/two-line primary objective, all remaining active goals in authored order in a bounded grey scroll area, emerald icon/three hero heads on desktop and touch, dark message pills with 0.2-second fades, checkpoint toast, proximity-based locked-exit text/chain and actual AI mode counts in F3. Core owns message expiry; pause freezes presentation clocks, repeated messages restart their entrance and the newest checkpoint toast has priority over the locked hint. Three pure fade cases and eight native mission/ambient cases pass (`artifacts/m8/banner-policy/`, `mission-hud-native-chain/`), including real objective/checkpoint events, all 32 goals, exact 720p/1080p/1440p and maximum-scale phone layouts, nested-label bounds, icon/text separation and unchanged Core hashes. Screenshot review is recorded separately; complete regression/export follows.
  - [x] Mission-HUD-inclusive complete local runner passes **438 normal/425 coverage cases**, **98.31% Core coverage**, **78 Godot cases**, **27 native UI/lifecycle cases per renderer**, four effects and nine editor cases per renderer, all captures/relaunch and real CLI thumbnails (`artifacts/m9/gate-e8df0d2e7e5f4a72af9f23457d09aa09/`, `combined-mission-hud.log`). Strict import has zero engine diagnostics. Desktop p95 ≤3.000 ms, ≤189 draws/69,272 triangles; content/level/development replay identities are unchanged. Final visual/platform acceptance and remaining polish/hardening remain open.
  - [x] Fresh mission-HUD normal/Low APKs and Windows export pass strict imports/manifests/resource/type audits. Both APKs retain all objective/banner/fade/toast types with editor/tests excluded. The real Windows game renders the new objective, pill and icon counters (`artifacts/m8/mission-hud-packaged-windows.png` / `.log`), and packaged JSON/replay/thumbnail/font/33-icon checks pass. Debug restore/build is warning-free. Package hashes/transcripts: `artifacts/m8/mission-hud-packages.json`, `mission-hud-*-export.log`. No installation or physical-device execution was performed.
  - [x] Desktop launch boundary (§18.1): strict `--level <id|JSON/directory/dlvl>`, unsigned `--seed`, `--replay` and `--headless-validate` support both argument forms and Unicode/space paths. Replay verifies source/content and recorded fingerprints, uses recorded difficulty/editor origin, ignores player actions, stops at its recorded boundary and leaves profile/automatic replay storage untouched. Five pure parser/cursor cases and five native host cases pass (`artifacts/m8/launch-policy-complete/`, `launch-native-suite/`); process checks verify invalid input/exit codes and the 11,591-tick full-clear hash `a6da84d1de933f5c`. Full rendered/process regression and refreshed package checks follow separately.
  - [x] Replay shutdown and partial-death boundaries: application exit stops/detaches audio, releases scenes and finalizes C# resource wrappers before native teardown, with no collection during gameplay. Six verbose fast exits have zero diagnostics; all 18 headless process contracts pass, including a warning-only level returning success (`artifacts/m8/launch-warning-final/`). Six launch/viewer cases pass headless and on both native renderers (`replay-boundaries-final-*`); a 968-tick recording ending at hero death keeps Back clickable, hides frozen respawn text and preserves its exact hash. Actual native mouse clicks and the corrected screenshot are checked. Full combined rerun and current package verification follow separately.
  - [x] Launch/replay-inclusive complete local gate passes **443 normal/430 coverage cases**, **98.31% Core coverage**, **85 headless Godot cases**, **33 native UI/lifecycle cases per renderer**, all **22 application process contracts**, four effects and nine editor cases per renderer, strict import/schema/palette/audio/font checks, M3–M8 captures/relaunch and actual CLI thumbnails (`artifacts/m9/gate-2650642a6561497a995975a604da53f5/`, `combined-launch-boundaries.log`). Full-clear/native playback preserves `a6da84d1de933f5c`; all development identities and review status remain unchanged. Desktop p95 ≤2.838 ms; native reload apply 169.746 ms Mobile / 116.530 ms Compatibility. Refreshed exports follow separately.
  - [x] Fresh normal/Low APKs and Windows export pass clean imports, manifests and resource/type checks; both APKs retain launch/replay types with editor/tests excluded. The actual exported Windows app passes all **22 process contracts** and renders the full-clear replay (`artifacts/m8/launch-packaged/replay.png`); font/OFL, 33-icon, packaged JSON/preview/replay checks and restored warning-free Debug build pass. Package hashes plus 93 runtime source hashes are recorded in `artifacts/m8/launch-packages.json`. One prior `_EDITOR_GET` import attempt was rejected and retained at `launch-android-import-rejected.log`; a strict clean retry passed. No installation or physical execution was performed.
  - [x] Named debug screenshot scenarios (§18.1): `--screenshot-list` exposes 22 fixed terrain/action/combat/mission/touch/settings fixtures; `--screenshot-test <name> --screenshot-output <new.png>` routes the actual acceptance scenes, preserves existing outputs and rejects a dummy renderer. Shader time and presentation steps are controlled only during these captures; native water tests prove normal animation stays live. All 22 fixtures pass with pixel-identical repeat captures on Mobile/Vulkan and Compatibility (`artifacts/m8/named-mobile-initial/`, `named-compatibility/`); two parser/catalogue cases and six image-diff cases pass. PNG sidecars record exact source/content/Core state, renderer/adapter/quality, dimensions and image hashes. The comparison tool enforces 2% per-channel tolerance and <=0.5% mismatched pixels, preserves source images even through hardlinks and returns a failing process status on mismatch. Named output/companion collisions are rejected; create-new writes pass focused capture pairs (`named-write-contracts/`). These are unreviewed candidates; approved baselines/nightly comparison remain pending. Full regression and fresh packages follow separately.
  - [x] Named-capture-inclusive complete local gate passes **445 normal/432 coverage cases**, **98.31% Core coverage**, **86 headless Godot cases**, **34 native UI/lifecycle cases per renderer**, six image-comparison cases, all 22 Mobile fixture pairs with zero mismatched pixels, seven screenshot rejection/preservation contracts, all 22 launch/process contracts, four effects and nine editor cases per renderer, strict asset/import/schema/audio/font checks, captures/relaunch and CLI thumbnails (`artifacts/m9/gate-4cec4a787ab64d2ab256dd05ea80a374/`, `combined-named-uncapped.log`). The separate Compatibility fixture pairs also pass. QA replay capture disables VSync only for paired replay-quit/PNG flags and completes in 5.827 s without relaxing the 120 s bound. All gameplay identities remain unchanged. Current exports follow separately.
  - [x] Fresh named-capture normal/Low APKs and Windows export pass clean imports/manifests/resource/type audits; mobile DLLs retain all capture/launch APIs with editor/test types absent. The actual Windows executable passes all 22 launch/process contracts, all 22 repeated Compatibility scenarios, and seven screenshot rejection/preservation checks. Every repeated capture and every editor-to-package image comparison has zero mismatched pixels (`artifacts/m8/named-packaged-scenarios/`, `named-package-parity/`). Font/OFL, 33-icon, packaged JSON/preview/replay checks and restored warning-free Debug build pass. Package/source hashes: `artifacts/m8/named-screenshot-packages.json`; inspected review sheet: `named-packaged-contact.png`. No baseline approval, installation or physical execution is claimed.
  - [x] Reviewed-profile comparison tooling (§20.1): `visual_baseline.py prepare` validates a complete executable catalogue/run and creates pending candidates with image/sidecar hashes; `check` requires a complete explicit review record, unchanged file hashes, exact scenario coverage and matching render/source/Core context before applying the image tolerance. Missing/pending review returns 3, invalid input 2 and regression 1; outputs cannot overwrite inputs or existing reports. Eleven synthetic baseline contract cases and six image-diff cases pass. Complete 22-case Mobile and packaged Compatibility candidates are retained under `artifacts/m8/baseline-candidates/`; both correctly fail comparison with pending review (`baseline-pending-mobile/`, `baseline-pending-gl/`). At that historical checkpoint no gameplay image or replay golden had been approved; later explicit approvals are recorded above. See `docs/visual-captures.md` and `assets/baselines/visual/README.md`.
  - [x] Nightly rendered orchestration (§20.1/§20.9) is implemented and locally exercised: locked builds, development replay verification, strict import (zero diagnostics), **86 actual Compatibility GdUnit cases**, **22 repeated scenario pairs with zero mismatched pixels**, candidate manifest and contact sheet pass (`artifacts/nightly/local-rendered-quality-fixed/`). The final comparison deliberately fails with exit 3 because the profile has no approved baseline; aggregate `passed` stays false. The separate Linux Xvfb/llvmpipe job uploads evidence even on failure; actionlint 1.7.12 validates both workflows. Its first hosted run and matching Linux baseline review remain pending. Three prior Vulkan-only quality expectations now verify Compatibility's forced-Low policy; focused cases also pass on Mobile and headless (`quality-contracts/`). All 109 packaged runtime source hashes are unchanged; this tooling/test change needs no new device install. See `docs/nightly-hardening.md`.
  - [x] All six named §20.7 BenchmarkDotNet workloads are implemented and execute successfully: full mission replay, 64-monster/32-arrow/128-pickup combat window, open 256×256 A*, a genuine seeded 256×256 maze, all 256 rich-terrain CPU mesh chunks and 256×256 level loading. Fixture preflight verifies the 6,868-step maze route, 601,980 indices, 64 surviving monsters and unchanged full-clear hash. Two new fixture cases and the expanded combat allocation/path-budget cases pass. Eleven regression-tool contracts pass, including the inclusive 20% boundary, the absolute 2 ms desktop tick budget, partial/error/context/process rejection and output preservation. See `docs/performance-baselines.md`.
  - [x] Per-runner nightly benchmark gate (§20.7/§20.9) passes an independent complete Windows run: **447 normal/434 coverage cases**, **98.31% Core coverage**, six measured workloads and all **144 samples** from **18 processes** (`artifacts/nightly/benchmarks-three-process-comparison/`). All means stay within the fixed +20% limit; largest increase is 0.36%, and both tick means stay below 2 ms. The stored Windows reference uses protocol 3 (isolated setup, three launches, all 24 samples per workload) and is never automatically replaced. Earlier actual failures (meshing +37.02%, loading +22.78%) and their references are preserved. The Linux job and CI comparator cases are configured; workflow lint passes. Hosted execution, a separately measured Linux reference and five green hosted nights remain pending. All 109 packaged runtime source hashes are unchanged; no device check was performed.
  - [x] §20.5 simulation fuzz implementation and default isolated run pass: **32 cases / 640,000 actual ticks / 16,395,248 entity checks**, alternating Mossy Hollow and random 8/16/32/64 maps with one through four players. Every tick checks finite transforms, bounds, living HP, independent circle penetration ≤0.001 m and two-run hash/state agreement. Exact maps/inputs/epoch seeds and failures are retained; terminal no-op calls cannot count as ticks. Nine focused cases pass. The first failing seed exposed loot scatter beyond map edges; scatter and magnet attraction now resolve terrain collision while preserving Loot RNG draws. Added M8 and nightly orchestration; full regression/package refresh follows separately. Evidence: `artifacts/simulation-fuzz/published-gate/`, `docs/simulation-fuzz.md`.
  - [x] Fuzz/loot-inclusive complete local runner passes **456 normal/443 coverage cases**, **98.32% Core coverage**, **86 Godot cases**, **34 native UI and nine editor cases per renderer**, four effects cases per renderer, all 22 launch contracts and 22 repeated screenshot pairs with zero mismatched pixels (`artifacts/m9/gate-d2f692b52e0745f7921b79c809b7a5bc/`, `artifacts/simulation-fuzz/combined-gate-final.log`). Both default and date-seeded 32-case fuzz suites pass (1.28 million ticks combined). Development replay/content/level identities are unchanged. Desktop p95 ≤1.903 ms; preview p95 Mobile 1.354 / Compatibility 0.530 ms. Earlier runner and one unreproduced allocation failure are retained; the next full gate, three isolated assertions and 128 additional full replays measure zero allocation without changing limits. Package refresh is tracked separately; physical, soak, hosted and human review acceptance remain pending.
  - [x] Refreshed normal/Low Android debug and Windows debug packages include the loot collision fix. Actual APK/Windows Core DLL SHA256 matches current ExportDebug (`8F659A7AB7A92DCC3946F318CBB5075229C4A8EC19BE345B9733E9518034B37F`). Mobile manifest/editor stripping and native 54-texture/21-model/80-material ETC2 audits pass; packaged Windows smoke/theme/icons, 54 lossless textures and all 22 launch contracts pass. Strict Windows import rejected one `_EDITOR_GET` shutdown error; its log is preserved and the unchanged strict retry passes. Locked Debug restore/build passes. Package hashes and evidence are in `artifacts/simulation-fuzz/checkpoint.json`; no device execution or full milestone acceptance is claimed.
  - [x] Desktop-only single-process soak harness and external supervisor are implemented. Headless and both real renderers pass accelerated diagnostics through full-clear/ambush/death-respawn replays, genuine results/catalogue controls and an ordinary profile/recording attempt; the final packaged runs verify persisted replay bytes through Core and loaded module IDs against an isolated, unchanged package. Ten policy cases, five actual CLI rejection cases and deliberate termination of the supervisor's own native process pass. Short/accelerated/headless execution cannot claim two-hour acceptance. Automated Windows export smoke now explicitly uses Dummy audio and passes without an endpoint; real listening remains pending. Evidence: `artifacts/soak/`, `docs/desktop-soak.md`.
  - [x] Dynamic gate-volume hardening is implemented and focused checks pass: closing gates transactionally relocate collectible circles and immediately terminate intersecting projectiles with one terrain-impact event; tangencies/misses survive, and an unplaceable collectible rolls back terrain/actors/projectiles without changing the hash. Repeated displacement/impact/hash transitions allocate zero bytes. Fuzz protocol 2 adds three validated generated gate maps, exact canonical source archives and mandatory closing-transition coverage. Default/date-seeded 32-case suites pass; the date suite records 43 closings. The same new corpus rejects the exact old shipped Core at case 9/tick 11 for 0.2 m food penetration. Mission/object checks and refreshed packaged diagnostics pass; full regression/current mobile refresh follow. Evidence: `artifacts/hardening/gate-volume/`.
  - [x] Detached local validation supervisor preserves request/process identity, live transcript and aggregate result after its launcher exits. Parallel packaged diagnostics pass on Mobile and Compatibility, including four focus-loss/resume checks before view readiness, ordinary replay persistence, strict shutdown checks and unchanged matching packages (`artifacts/soak/detached-natural-mobile/`, `detached-natural-gl/`). QA shutdown uses natural collection after frame/mixer draining; earlier native bridge errors remain preserved, with no error allowlist added.
  - [x] Archived gate-fix Windows/Vulkan checkpoint passes its real two-hour soak: **7,251.49 wall-clock seconds**, **423,891 ticks**, **359,081 drawn frames**, 20 clears/19 ambushes/19 death-respawn flows/20 ordinary attempts, matching unchanged package/modules, zero exit and no native errors/leaks. Both native and supervisor results pass (`artifacts/soak/windows-mobile-2h-detached-20261027/` and sibling `.launch/`). This older checkpoint predates the service adapters and approved-host/boot migration.
  - [x] Archived old-host service-adapter checkpoint also passes its real two-hour Windows/Vulkan soak: **7,244.06 wall-clock seconds**, **423,899 ticks**, **359,540 drawn frames**, 20 clears/19 ambushes/19 death-respawn flows/20 ordinary attempts; longest gap 1.273 seconds. Native and external supervisor results pass with unchanged matching payload, zero exit and no errors/leaks (`artifacts/soak/windows-mobile-2h-services-20261027/` and sibling `.launch/`). This predates the approved host/boot migration.
  - [x] Adopted-host Windows/Vulkan checkpoint passes its two-hour soak: **7,237.23 seconds**, **423,908 ticks**, **359,856 drawn frames**, 20 clears/19 ambushes/19 death-respawn flows/20 ordinary attempts; longest gap 1.435 seconds. Both native and external results pass with unchanged matching payload, zero exit and no native errors/leaks; keep-awake is released (`artifacts/soak/windows-mobile-2h-host10-20261027/` and sibling `.launch/`, completed **19:59:18 UTC**). This sealed checkpoint predates the isolated AOT/capture repair. Other platforms/device-matrix checks remain pending; superseded/interrupted runs earn no acceptance.
  - [x] Refreshed current normal/Low Android and Windows packages pass strict exports/resource audits. All three Core payloads match current ExportDebug source; Windows passes 22 packaged launch contracts, all 54 desktop texture checks and rendered smoke. Locked Debug restore/build and both packaged short renderer diagnostics pass (`artifacts/platform-services/verified-packages/payloads.json`, `packaged-mobile/`, `packaged-gl/`). Failed export attempts are retained in the M8 evidence document. No device execution, reviewed-baseline promotion or release acceptance is claimed.
  - [ ] Remaining motion/audio/other-context presentation acceptance, publisher/store delivery, physical platform acceptance and five green hosted nightly days. Windows still images, the gameplay-golden identities and local Windows performance reference are approved; the final repaired-package Windows soak passes. Local implementation and hardening evidence are recorded above and in `docs/completion-audit.md`.
  - [x] Android release-prerequisite audit inspects actual APK SDK/debug/signature identity, every arm64 ELF LOAD alignment and uncompressed ZIP offset. Ten malformed-input/policy contracts pass and are included in the local/CI runners. The current development APK is correctly rejected; unknown signing keys cannot count as publisher acceptance (`artifacts/release/android-normal-audited/`, `docs/release-readiness.md`).
  - [ ] Android store delivery: API 36/min 26, native ELF/ZIP 16 KB prerequisites and normal/Low offline split matrices pass. A Taskscape-signed **1.0.0 / code 1** AAB is also accepted in an unpublished Google Play internal-test draft, with registered upload and Play app-signing certificates independently verified below. Actual Play-generated split/device checks, final listing/artwork/declarations and release approval remain pending; the draft is not rolled out.
  - [x] Reusable Android bundle validation and isolated debug AAB proof: pinned official bundletool 1.18.3 validates the exported structure and generates an offline universal APK; manifest, editor/QA stripping, 54 ETC2 textures and native loading pass. The validator rejects the actual unsigned debug export. A separate QA-signed copy passes technical checks with publisher/store/device acceptance explicitly false. Upload/APK certificate identities are separate, and secrets are passed through password files. Evidence: `docs/android-bundles.md`, `artifacts/host-migration/aab-signed-reusable-validation/`.
  - [x] Reproducible batch AAB export from a fresh, hash-verified isolated source workspace (`tools/Export-AndroidBundle.ps1`): warning-free build, clean import, 54 native ETC2 variants, pinned Android template/hooks and real bundle export pass. Its QA-signed copy passes the reusable validator plus independent zipalign. Actual negative contracts reject unsigned/tampered bundles, wrong publisher identity and a QA certificate masquerading as publisher, while preserving existing evidence and both inputs. Evidence: `artifacts/host-migration/aab-cold-{export,validation,contracts}/`.
  - [x] Complete explicit QA bundle pipeline (`tools/Test-AndroidBundlePipeline.ps1`) also passes the cold **Low/Compatibility** path, signing/validation and all rejection contracts; its temporary password file is removed. Normal and Low AABs have real local evidence. Android CI invokes the normal chain and retains reports; workflow lint passes, with hosted execution still pending. Evidence: `artifacts/host-migration/aab-low-pipeline/`.
  - [x] The actual Android **release template** also exports and passes signed-bundle/universal-APK technical checks using a clearly labelled QA key: non-debuggable manifest, stripping, 54 ETC2 textures, native alignment and independent zipalign. Publisher acceptance is correctly rejected. Release signing inputs use the supported process environment; absent credentials fail before source copying, and the earlier failed native attempt is retained. Evidence: `artifacts/host-migration/aab-release-qa-{export,validation}/`.
  - [x] Editable English Google Play/App Store metadata drafts and a local text export/check tool are implemented (`platform/store/en-US.json`, `tools/store_metadata.py`). Actual draft export and seven contracts pass, covering platform text/keyword-byte limits, both Android/iOS identity/version binding, duplicate/reserved references, changed approved copy and input/output preservation. CI/M8 invoke those checks. The original checkpoint lacked publisher details, now supplied and checked below. Human copy review and final versions remain pending; no submission/device acceptance is inferred. Evidence: `docs/store-metadata.md`, `artifacts/store/english-validated{,-not-ready}/`.
  - [x] Publisher metadata supplied by the owner on **2026-10-28**: **Taskscape Ltd**, `info@taskscape.com`, `https://www.taskscape.com`, `https://www.taskscape.com/privacy.html`, copyright **2026 Taskscape Ltd**. Values are stored exactly in `platform/store/en-US.json`; fresh draft export and all seven metadata contracts pass. Both HTTPS URLs return HTTP 200 (`artifacts/store/taskscape-publisher-20261028/`). The negative privacy fixture now explicitly omits its URL, and changed publisher/copy data still invalidates approval. Readiness correctly leaves only human copy review and the two release-version fields pending. URL availability does not approve game-specific privacy coverage, store delivery or signing.
  - [x] First-release configuration supplied by the owner: version **1** is normalized to **1.0.0**, with Android version code **1** for this new app, iOS build **1**, Windows file version **1.0.0.0**. Store metadata and Windows/normal Android/Low Android/iOS presets are synchronized; fresh draft export and seven metadata contracts pass (`artifacts/store/first-release-20261028/`). The version-mismatch fixture uses owned synthetic version values and checks both Android presets independently. A live uncached HTTP fetch confirms the updated privacy policy explicitly covers Block Dungeons and games/apps. At that configuration checkpoint signed exports were still pending; the subsequent real publisher Android export/upload is recorded below. Listing copy review, remaining platform exports, store declarations and platform acceptance remain pending.
  - [x] Google Play draft and public signing identities: the owner approved a **free**, English-US game and the required policy/export declarations; the unpublished `Block Dungeons` app is created under Taskscape Ltd, ID **4972227016795288536**, package `org.blockdungeons.game`. The Google-managed signing certificate was downloaded and its SHA-256 matched the console. Account IDs and public certificates are retained in `platform/android/play-console.json` and `platform/android/signing/`; no Google private key is retrieved or exposed (`docs/android-signing.md`).
  - [x] Actual Taskscape upload-key configuration and signed **1.0.0 / code 1** AAB: RSA-3072 private keystore outside Git, Windows CurrentUser DPAPI password, owner/SYSTEM-only ACL, public certificate fingerprint bound to the release helper. `tools/Export-AndroidPublisher.ps1` passes a fresh isolated release export and actual signature/universal-APK/manifest/54-texture/native-16-KB/zipalign checks (`artifacts/release/publisher-1.0.0/`). All 94 Python contracts pass; temporary password removal and zero literal-password log leaks are verified. The generated local APK set uses the upload certificate, distinct from Google's distribution certificate.
  - [x] Google accepted the owner-approved **1.0.0 / code 1** AAB in the unpublished internal-test draft and reports API **26+**, target **36**, **arm64-v8a**. Draft save is explicitly confirmed in the console; factual English release notes are saved. The subsequently downloaded Google upload certificate matches the local upload key byte-for-byte by DER SHA-256 (`cf2186…e1e61`), confirming registration. Proof, exact bundle identity, approval references and screenshot: `artifacts/release/play-setup-20261028/upload-proof.json`, `play-draft-saved.jpg`. Public configuration records the draft URL and both certificate identities. No testers or production rollout occurred.
  - [x] Subsequent owner-requested internal tester setup: the console now reports **1.0.0/code 1 available to internal testers**. Created the dedicated **Block Dungeons Internal Testers** list with the requested account, selected only that one-person list for this app and saved it. Reload confirms the selection and **active** track; the existing four-person list remains unselected. The opt-in URL is recorded in `platform/android/play-console.json`; personal tester identity is retained only in ignored local evidence (`artifacts/release/play-setup-20261028/internal-tester-proof.json`, `internal-tester-added.jpg`). This step changes the audience for the already-available internal release; it does not publish a new release or establish device QA.
  - [ ] Actual Play-generated split/device checks and store delivery. The owner approved upload to an unpublished internal-test draft only; no rollout is authorized. Local publisher signing and offline APK validation do not establish these remaining acceptance items.
  - [x] Current-source six-workload benchmark measurement completes all 18 processes/144 samples and passes the Core/allocation/path/absolute tick-budget checks (`artifacts/platform-services/benchmarks-current/`). The stored-reference comparison correctly rejects changed replay tick-hash and Windows build contexts; failure evidence and the original reference are preserved. A separate comparison against the new temporary candidate is tracked independently.
  - [x] Independent current-context comparison against that temporary candidate passes all six workloads, 18 processes/144 samples and unchanged +20%/absolute-budget checks (`artifacts/platform-services/benchmarks-candidate-independent/`). Means change by -1.79% to +2.66%; both simulation allocation estimates are zero and the full Core allocation/coverage gate passes. This is candidate reproducibility evidence, separate from canonical-reference acceptance.
  - [x] Current stored Windows performance-reference acceptance: the owner explicitly approved the exact matching .NET 10/twelve-warmup proposal on 2026-10-28. It is canonical, its incompatible .NET 8 predecessor is archived unchanged, and both independent six-workload datasets pass against the canonical path (`artifacts/approvals/baselines-20261028/`). Fixture/context checks, +20% and absolute/allocation limits remain strict. Linux/device/hosted acceptance remains separate. See `docs/performance-baselines.md`.
- [ ] **M9 — Editor-readiness verification**: local implementation and complete regression/export checks pass; required earlier milestone/platform acceptance remains pending. Evidence: `docs/m9-validation.md`.
  - [x] `LevelSerializer.Write(doc)` provides ordinal key order, original array order, two-space indentation, LF and normalized exact decimal numbers, retaining unknown fields, Unicode and arbitrary-size exponent metadata. Byte-identical canonical reload/write and culture independence pass without changing the original document, source hash or simulation identity. Twenty serializer cases include 2,000 generated values checked against an independent exact integer oracle. The full Core runner passes **315 normal/307 coverage cases**, **97.71% Core coverage**, both Core targets and warning-free tools/benchmark builds (`artifacts/core/mzag_R5-7600X_2026-10-27_00_45_17.trx`, `2a2661f56c6242149500d8e512878756/`).
  - [x] `ILevelCommand.Apply/Undo` and all listed built-ins, plus atomic batches, persisted undo/redo and compact value/array-splice history. Commands rebind source, terrain, entities, environment and mission caches together; semantic editing errors remain diagnosable and block play, while structural errors and stale/tampered histories roll back. Resize supports all nine anchors, retains cropped entities/trigger areas for repair and restores cropped cells on undo. Forty-seven command cases and the complete Core runner pass **362 normal/354 coverage cases**, **97.89% Core coverage**, with both target frameworks and warning-free tools/benchmark builds (`artifacts/core/mzag_R5-7600X_2026-10-27_01_11_06.trx`, `2b6b59545444485b9715201126c9d84f/`). Command commits currently perform full rebinding/validation; incremental checks remain below.
  - [x] Combined regression gate passes the same **362 normal/354 coverage cases**, **97.89% Core coverage**, **36 Godot cases**, schema/asset/audio checks, native captures and renderer relaunch (`artifacts/m8/gate-3f5910a2dc2e45fd9a6abfec9fbed4e2/`). Desktop p95 <=1.858 ms; replay candidate identities remain unchanged and unreviewed.
  - [x] Direct full validation and `ValidateCells(doc, CellRect)` share loader terrain/property/stairs rules without serializing/reloading the document. Regional checks cover a one-cell dependency halo, retain shared palette/index diagnostics and preserve the original bake/session. Full 256×256/64-monster checks take 10.751–16.378 ms; a disconnected graph takes 22.557 ms; regional checks with 65,536 overrides take 0.254–0.318 ms on this desktop. Eleven new cases include exhaustive representative navigation equivalence and connectivity oracles. The canonical Core gate passes **373 normal/362 coverage cases**, **97.86% coverage**, both Core targets and warning-free tools/benchmark builds (`artifacts/core/mzag_R5-7600X_2026-10-27_02_07_23.trx`, `850828b138dc404187d7d668b177d6ae/`). These are local warmed measurements; physical/reference-device acceptance remains separate.
  - [x] Combined validation regression passes all **373 normal/362 coverage cases**, **36 Godot cases**, schema/asset/audio checks, native mission/combat/touch/settings captures and actual renderer relaunch (`artifacts/m8/gate-39bd921877a54abc96c05863480a9d6f/`). Desktop p95 <=1.839 ms; all three development replay identities remain unchanged and unreviewed.
  - [x] `TriggerSchema.Events/Actions/Conditions` expose all 31 supported types with per-type parameters, nested object/array fields, required/default/range/enum data, placed-id versus content-prototype reference kinds and picker constraints. Metadata derives from the production schema; recursive conditions retain a reference to the condition catalogue. CLI `schema --triggers` exports an independent JSON catalogue. Eight cases compile every generated form and reject missing required/mistyped fields, unknown types and reserved actions. All 31 dumps pass an independent draft-2020-12 schema check; all 45 shipped JSON files validate. The canonical Core gate passes **381 normal/370 coverage cases**, **98.20% coverage**, both Core targets and warning-free tools/benchmark builds (`artifacts/core/mzag_R5-7600X_2026-10-27_02_13_57.trx`, `d010b7134ac14ddab59702197d903e18/`).
  - [x] Package and folder `assets/` bytes flow through `IAssetSource` into loaded/catalogued documents and survive command apply/undo/redo. V1 preserves bytes and emits one LVL0100 unsupported-override warning; simulation and source/content identities remain unchanged. Memory sources enforce portable paths, duplicates/parent conflicts, 512-entry and 64 MiB limits. Godot and CLI directory readers reject linked paths and share the level/asset byte budget. Thirteen new Core cases, CLI validate/pack/unpack byte retention and real Godot import/folder discovery pass. The canonical gate passes **394 normal/383 coverage cases**, **98.22% coverage**, both Core targets and warning-free tools/benchmark builds; all **37 Godot cases** pass (`artifacts/core/mzag_R5-7600X_2026-10-27_02_22_59.trx`, `12394d3d71e847db97dc6f546e105282/`, `artifacts/m9/gdunit-assets/`).
  - [x] Typed `DefinitionData.EditorIcon/EditorChar` and `ContentEditorCatalog.ToJson(registry)` expose resolved tile/entity palette metadata and property schemas without prototype-specific lists. CLI `schema --editor` dumps all 13 tiles/20 entities with stable ordering and registry identity. Two new cases cover arbitrary inherited prototypes, nested property schemas, copy isolation and culture independence; the Core gate passes **396 normal/385 coverage cases**, **98.28% coverage** (`artifacts/core/mzag_R5-7600X_2026-10-27_02_26_42.trx`, `ebf93b85802646509ea565c0e1a5c16e/`). At that checkpoint all definitions had names/categories, while 27 icon hints and 28 character hints were absent. The later authored palette completion is recorded below.
  - [x] Combined asset/catalogue checkpoint passes **396 normal/385 coverage cases**, **37 Godot cases**, all schema/asset/audio checks, native captures and renderer relaunch (`artifacts/m8/gate-45970c1970194cdc996fa8babaa356a6/`). Desktop p95 <=1.854 ms; candidate replay identities remain unchanged and pending review.
  - [x] `GameSession.Create`/constructor accept immutable `StartOverride { Pos, Checkpoint }`, moving the first hero before startup triggers, initial events, camera/respawn setup and recording. Optional Checkpoint uses an authored checkpoint id; invalid bounds/footprints/gates/references are rejected. Replay snapshots/JSON, verifier, CLI verification/rerecording and the replay benchmark retain the override; ordinary sessions keep existing replay bytes/identities. Eight new Core cases cover startup/checkpoint/respawn, malformed input, co-op slot isolation and zero-allocation recording. The canonical Core gate passes **404 normal/393 coverage cases**, **98.32% coverage**, both Core targets and warning-free tools/benchmark builds; all **38 Godot cases** pass, including an actual overridden actor/camera view (`artifacts/core/mzag_R5-7600X_2026-10-27_02_34_48.trx`, `b819a9ebdfce4d68b96da3920c4b00e1/`, `artifacts/m9/gdunit-start-override/`). Two 120-tick Mossy Hollow override replays verify and rerecord identically through the real CLI.
  - [x] Fresh normal/Low Android debug exports include the M9 API work and pass strict import/export/manifest checks; Debug restore/build passes afterward. SHA256 normal `86D9795783F2A5F45E6A189A16DEEFADB0DD024E44C9B1D427C85B47390F79AB`, Low `5A3F1EB9B3C77C83513D1D961E35CBE04B5A9CB42242AA6E614D4CEC8AEBA65A`. A fresh ADB check still reports Dozing; these builds have not passed new physical acceptance.
  - [x] Picker TargetKinds now use exact EntityKind/content-dump names, avoiding a hidden editor-specific name mapping. Eighteen metadata/content/start cases, both Core target builds and all 45 shipped JSON schema checks pass (`artifacts/m9/editor-contracts/`). The preceding APK checkpoint predates this metadata spelling correction.
  - [x] Session-free `WorldView.Build(doc)` renders authored idle placements, camera and lights without constructing simulation entities, actor pools or combat HUD. `ApplyChange(ChunkRect)` replaces affected chunk roots, preserves untouched resources and coalesces superseded worker jobs. Immutable 16×16 terrain snapshots share unchanged chunks; `ChunkRect.FromCells` includes cliff/AO neighbours. Six new Core cases and four new Godot cases cover snapshot isolation, concurrent reads, clipping, stale jobs, placement/environment changes and preview/runtime rebuilds. The local checkpoint passes **410 normal/398 coverage cases**, **98.33% coverage**, **42 Godot cases** and four native Mobile renderer cases (`artifacts/m8/gate-e5eb7a94fc7144c5811718a8ed9290f9/`, `artifacts/m9/gdunit-preview-native-final/`). Native chunk scheduling/build/commit measured 1.185/0.131/0.412 ms; session-free Mobile and Compatibility captures pass (`artifacts/m9/editor-preview-native-e4de3ea8505e473a8bdce2c264761396/`). Final combined-run follow-up is recorded in the M9 evidence document.
  - [x] `WorldView.CapturePreview(doc, width, height)` returns a real rendered overview PNG from an isolated SubViewport, using idle authored entities and a fixed orthographic camera fitted to terrain/model bounds. Gameplay camera, viewport scale and Core state remain unchanged; temporary worlds are released on success or rejected input. CLI `preview <level> --png <new.png>` validates source/content identity, builds current game sources, invokes the pinned GPU host and preserves existing outputs. The built-in Mossy Hollow now has a 320×180 `preview.png`. Six native preview cases and raw/packaged/custom-content/Unicode-path CLI checks pass (`artifacts/m9/thumbnail-native/`, `thumbnail-56c26a5adcd34ffe88f18614ff2b9a0e/`). Headless dummy capture is explicitly rejected; physical acceptance remains separate.
  - [x] Complete local M9 regression runner passes **410 normal/398 coverage cases**, **98.33% coverage**, **44 Godot cases**, **six native preview/thumbnail cases**, strict schema/asset/audio/import checks, M3–M8 native captures/relaunch, both preview renderers and real PNG CLI integration (`artifacts/m9/gate-9455de4688004d5ea8230083d43375ef/`). Preview p95 is 1.522 ms Mobile / 0.525 ms Compatibility. This is implemented-API acceptance; remaining M9 requirements and physical/hosted/review gates stay pending.
  - [x] Desktop editor adapters live under `game/editor/`; a feature-aware export plugin strips those resources when the preset lacks `editor`. Only the Windows desktop preset includes that tag. Mobile compilation also removes editor C# types, since resource filters alone do not shrink the DLL. Fresh normal/Low APKs pass manifest checks and metadata/resource audits with zero editor resources/types and retained Core/WorldView authoring APIs; Android/iOS evaluated Compile lists exclude editor sources. Exported Windows retains the adapter and successfully renders a thumbnail; its packaged cube/JSON/Core probe passes after correcting package detection to use `template` independently of custom editor tags. Evidence: `artifacts/m9/android-editor-stripping.log`, `android-low-editor-stripping.log`, `windows-editor-export-fixed.log`, `thumbnail-exported-windows.log`. iOS compilation/export and physical execution remain deferred platform checks.
  - [x] `GameSession.ReloadLevel(doc)` transactionally rebuilds terrain/navigation, prototypes, actors, triggers and startup state for the same level id. A living first hero keeps its position or moves to the deterministic nearest safe cell; the prototype resets ticks/mission/stats/loadout as a fresh epoch and preserves pause. Recorded sessions reject unrepresentable source changes. Seven new Core cases pass; the canonical gate passes **417 normal/404 coverage cases**, **98.31% coverage** (`artifacts/m9/reload-core-full.log`). The desktop `game/editor/level_reload.tscn` tool watches actual disk JSON, rejects partial/invalid files while retaining the old run, diffs documents and rebinds actors while keeping unaffected terrain nodes. Actual native disk-edit/resize tests pass in **117.105 ms end-to-end** / **98.455 ms apply-to-ready** on Mobile (`artifacts/m9/reload-native-fixed/...04_00_25.trx`); Core-only reload samples are 3.246–19.755 ms. Complete combined-run follow-up remains separately recorded; physical/reference-device timing is pending.
  - [x] Complete reload-inclusive local runner passes **417 normal/404 coverage cases**, **98.31% coverage**, **46 Godot cases**, **eight native editor cases on each of Mobile and Compatibility**, strict asset/schema/audio/import checks, M3–M8 captures/relaunch and real CLI PNG checks (`artifacts/m9/gate-b2a09a1cd61b4bfc9f550d60681475b2/`). Disk-write-to-ready reload is **139.891 ms Mobile / 141.694 ms Compatibility**; preview p95 is **1.448 / 0.509 ms**. A controlled mouse event reproduced touch-capture cancellation; the QA harness now isolates its deterministic touch stream from unrelated hardware input, with the injected interference included in the passing captures. Production input routing and acceptance limits remain unchanged. Full M9 acceptance remains pending authored hints and earlier review/platform requirements.
  - [x] Reload-inclusive normal/Low Android exports pass clean imports, manifest checks and actual APK PE/resource audits: zero desktop editor/test types or editor resources, with `GameSession.ReloadLevel` and `WorldView.Reload` retained. SHA256 normal `6353CAF2D22B962A06542DB5682F61417F5A5595CFA76F3669566CAB57087EF3`, Low `9EAB4042B0EDFA0C73B251404C3668F62EA9117F3070D1343656FC2EDF08E8F7`. Fresh Windows export/rendered packaged smoke passes; Debug restore/build passes afterward. Android/iOS Compile evaluations also omit project editor/test sources and test packages in Debug. Evidence: `artifacts/m9/android-reload-export.log`, `android-low-reload-export.log`, `windows-reload-export.log`. These builds have not been installed or physically accepted.
  - [x] All 13 tiles/20 entities now have authored icon paths and editor characters. Thirty-three original 32×32 hard-alpha icons derive from existing models, terrain meshes and five editable SVG marker glyphs, using only the 48-colour palette. Icons live under `game/editor/icons/` for mobile exclusion. `tools/Make-EditorIcons.ps1` regenerates them through the pinned real renderer; `check_editor_icons.py` checks metadata coverage, safe paths, PNG dimensions/palette/transparency/bounds, registry identity and input/output provenance. Source/CLI checks and the stricter two-case metadata suite pass (`artifacts/m9/palette-64b632fd97634224a5cbcdbdcd0ced5c/`, `editor-hints/`). New content SHA256 is `2ca5f6f1a50da8787185c6c3b555be5e41cc4686e4be10f4a3f70e4546fec8a6`; exact level JSON is unchanged. Previous replay evidence was preserved before candidate regeneration; review remains pending. Combined runner/engine texture checks follow separately.
  - [x] Final local §23 contract runner passes **417 normal/404 coverage cases**, **98.31% coverage**, **47 Godot cases**, **nine native editor cases per renderer**, palette/provenance/schema/audio/import checks, M3–M8 captures/relaunch, data-driven ASCII preview with custom registry characters and actual PNG CLI checks (`artifacts/m9/gate-bbae1a53d09440c3a27b97b7eafef3ab/`). Every icon loads through actual engine resources. Reload is **142.006 ms Mobile / 121.933 ms Compatibility**; preview p95 **1.345 / 0.579 ms**. New development hashes: initial `10c9999851f4b6ad`, full clear `a6da84d1de933f5c`, ambush `2a8bf663642acddd`, death/respawn `395328b0fce05079`; mission behavior remains 11,591 ticks/15 kills/two chests/no deaths. Review remains pending.
  - [x] Final normal/Low APK audits prove editor art, hashed imported textures, C# editor types and tests are absent while portable APIs remain. Windows PE audit retains the authoring tools, and the actual packaged executable loads all **33 editor textures**. Preset `editor` features drive the C# build flag; Android/iOS item evaluations verify default exclusion and explicit opt-in without porting code. Fresh exports/imports/manifests, packaged Windows smoke and restored Debug build pass (`artifacts/m9/*-preset-feature-export.log`). SHA256 normal `5BCEC1193C922D035AAB5AAC1152EC5635A8CA5FEB0F4328199F3C087DDFA9B2`, Low `0EA774D9BB79C67A37826B3BEBC8EA612A0CC104393FAD985C010891FCAFEB06`, Windows `4B6AE6B0DD81558516CF1C6949A80F89F966B7D336196D39BC6C6E256C7E2825`. No new device execution or iOS export is claimed.
  - [ ] Earlier platform, review and hardening requirements remain open. Physical checks are deferred at the user's request; continue implementation without further wake requests.

Evidence and reproduction commands are recorded in `docs/m0-validation.md` and `docs/m1-validation.md`. The user confirmed Android availability and deferred iPhone/Mac validation on 2026-10-26; the original §21 sequencing gate is overridden only as recorded below.

M3 evidence is in `docs/m3-validation.md`. Fresh headless glTF import on the pinned Godot 4.4.1 emits the known editor-preview diagnostic [godotengine/godot#108994](https://github.com/godotengine/godot/issues/108994); rendered runtime and GdUnit4 checks pass. This engine limitation is retained in the logs rather than treated as a clean import.

**Authorized execution exception (2026-10-26):** the user explicitly instructed "Proceed with implementation; keep platform checks pending." This overrides the M0-before-M1 sequencing requirement in §3.3/§21. Implementation may proceed through later milestones; the iPhone/Mac and hosted CI checks remain unchecked and are still required before final completion. Other milestone acceptance criteria remain in force.

Historical M0 toolchain candidate: .NET SDK 8.0.426, C# 12 and Godot 4.4.1 .NET, chosen for the original .NET 8 host requirement. The user approved its replacement on 2026-10-27: Godot 4.7.2/.NET SDK 10.0.402 is now active, retaining C# 12 and Core's net8.0/netstandard2.1 targets. The earlier pins/packages remain archived; new-host physical acceptance is still pending under §3.3 and the authorized sequencing exception.

---

## Table of contents

1. [Project overview](#1-project-overview)
2. [Reference design: what we take from Minecraft Dungeons](#2-reference-design)
3. [Technology selection](#3-technology-selection)
4. [System architecture](#4-system-architecture)
5. [Repository and project layout](#5-repository-and-project-layout)
6. [Coordinate systems, units and conventions](#6-coordinate-systems-units-and-conventions)
7. [Simulation core](#7-simulation-core)
8. [Gameplay mechanics](#8-gameplay-mechanics)
9. [Enemy AI and pathfinding](#9-enemy-ai-and-pathfinding)
10. [Level system (loadable levels)](#10-level-system)
11. [Content registry (tiles, entities, loot)](#11-content-registry)
12. [The basic level: "Mossy Hollow"](#12-the-basic-level-mossy-hollow)
13. [Rendering and visual style](#13-rendering-and-visual-style)
14. [Audio](#14-audio)
15. [Input](#15-input)
16. [UI / UX](#16-ui--ux)
17. [Persistence](#17-persistence)
18. [Platform specifics](#18-platform-specifics)
19. [Performance budgets](#19-performance-budgets)
20. [Testing strategy](#20-testing-strategy)
21. [Implementation plan and milestones](#21-implementation-plan-and-milestones)
22. [Coding standards and mistake-proofing](#22-coding-standards-and-mistake-proofing)
23. [Level editor readiness](#23-level-editor-readiness)
24. [Risks and mitigations](#24-risks-and-mitigations)
25. [Design decision record](#25-design-decision-record)
- Appendix A — Constants table
- Appendix B — `level.json` JSON Schema
- Appendix C — Content definition examples
- Appendix D — Trigger/action reference
- Appendix E — Simulation event reference
- Appendix F — Glossary
- Appendix G — Future level editor requirements (informative)

Keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** are used as in RFC 2119. Anything marked **MUST** is an acceptance criterion; a build that violates it is not done.

---

## 1. Project overview

### 1.1 Goal

Build a top-down, fixed-camera, hack-and-slash action game in the style of Minecraft Dungeons:

- Blocky voxel world, pixel-art textures, warm dramatic lighting.
- One hero controlled directly (not click-to-move), melee combo, ranged weapon, dodge roll, healing potion.
- Hordes of simple but readable enemies (zombie, skeleton archer) in hand-authored levels with checkpoints, ambushes, gates, chests, emeralds, and an exit.

Version 1 ships **one small level** ("Mossy Hollow", §12). The engine, however, is the product: everything about a level lives in a data package (§10) that can be authored outside the codebase, validated, listed in a level-select screen, and later produced by a level editor (§23).

### 1.2 Scope of version 1

In scope:

| Area | v1 content |
|---|---|
| Hero | 1 hero, fixed stats, sword (3-hit combo), bow with arrows, roll, potion, 3 lives |
| Enemies | Zombie (melee), Skeleton (archer) |
| World objects | Gate, chest, checkpoint, exit beacon, torch, spawn marker, emerald pile, food, arrow bundle, decorative props |
| Levels | 1 built-in level; loader for any number of built-in and user-provided level packages |
| Level logic | Data-driven triggers (areas/events) → actions (spawn, open gate, messages, objectives, completion) |
| Platforms | Windows x64, Android arm64, iOS arm64 |
| UI | Main menu, level select, HUD, pause, death/retry, mission complete, settings |
| Testing | Headless simulation tests, level validation CLI, replay determinism, integration tests, platform QA checklist |

Out of scope for v1 (but the design **MUST NOT** prevent them): multiplayer, procedural generation, item/enchantment system, hero progression beyond XP counter, additional heroes, the level editor itself. §1.5 lists the full backlog and the concrete rule each item imposes on v1 code.

### 1.3 Design pillars (tie-breakers for every decision)

1. **Engine first.** Gameplay code never knows which level it is running. If a feature needs a special case for a specific level, the feature is wrong.
2. **Data over code.** Tiles, entities, enemies, loot, triggers, objectives, camera, lighting are data. Code provides *kinds*; data provides *instances*.
3. **Deterministic simulation.** Same level + same seed + same input stream ⇒ identical result. This is how we test the game without a GPU and how a future editor gets "play from here".
4. **Readable action.** Enemies telegraph, hits flash, numbers pop, the camera never surprises. Matching the reference feel beats adding features.
5. **Mobile is not a port.** Touch controls, auto-aim, UI scale, battery and thermal budgets are designed in from day one.

### 1.4 Legal boundary (mandatory)

Minecraft Dungeons is a trademarked product of Mojang/Microsoft. The team **MUST**:

- Create all textures, models, animations, sounds, music and fonts from scratch or from assets with licences permitting commercial redistribution. No Minecraft/Minecraft Dungeons asset, texture, sound, or font file may be present in the repository, even "temporarily".
- Not use the names "Minecraft", "Minecraft Dungeons", "Mojang", "Creeper", "Enderman", "Illager", "Redstone", "Arch-Illager", "Camp", "Squid Coast", "Creeper Woods" or other Mojang-specific names in the product, the UI, level names, file names, or store listings. Generic fantasy names (zombie, skeleton, spider, emerald, torch, chest) are fine.
- Avoid pixel-identical character designs. Proportions and the blocky style are generic; a specific texture is not.
- Keep this document internal; "Minecraft Dungeons" appears here only as a design reference.

### 1.5 Post-v1 backlog and the engine guards it imposes

The following features are **not** built in v1, but each one imposes a rule on v1 code so that adding it later is additive rather than a rewrite. Reviewers reject v1 code that breaks a guard.

| Backlog feature | Guard in v1 |
|---|---|
| Gear/inventory (weapons, armour, item levels) and enchantments | Hero and monster numbers are read from a per-entity `StatBlock` (hp, armor, speed, damage multipliers, cooldown multipliers) computed at spawn from the def; systems never read `GameConstants` for a value that could become gear-dependent. Sword/bow parameters live in a `WeaponDef` referenced by the hero, not in the hero def. |
| Artifacts (3 slots with cooldowns) and souls resource | `InputButtons` reserves `Artifact1..3` bits; HUD layout reserves three slots right of the potion; `Inventory` struct holds named counters (`emeralds`, `arrows`, `souls`) rather than separate fields. |
| More mobs (spider, mage, brute…), elite/enchanted variants, bosses | Behaviours are selected by `brain` id; new monsters are JSON. Elite variants are defs with a `baseDef` + stat multipliers (loader supports `extends`). Health bars support a `boss` style flag (not rendered in v1). |
| Camp/hub, mission map, difficulty tiers | `GameSession.Create` accepts a `DifficultyProfile` (hp/damage multipliers, extra spawns multiplier); v1 passes `Default`. Level catalogue entries carry `difficulty` already. |
| Procedural assembly from authored rooms | `LevelDocument` can be built in memory from pieces (command API in §23); nothing in the loader assumes a file. Reachability validation works on in-memory documents. |
| Local/online co-op (up to 4) | `World.Players` is a list; no `World.Player` singleton; AI targets are `Entity` references chosen by a `TargetSelector`; `PlayerInput` is indexed by player slot; camera focuses on the centroid of alive players (v1: one). See §4.5. |
| Secrets/hidden chests, destructibles | Props have a `Health` component optional via def (`breakable: true`); tiles have a `secret` prop reserved; nothing renders differently in v1. |
| Cutscene/dialogue triggers | Action list already sequential with `delay`; `camera_focus` and `dialogue` action types are reserved names (validator rejects as "not yet supported" rather than "unknown"). |
| Achievements, cloud saves | Profile is a single JSON document with a schema version; stats counters (`kills by type`, `emeralds total`) are incremented in v1 even though no achievement reads them. |
| Controller rumble patterns | Haptics go through `IHaptics.Play(pattern id)`; v1 has two patterns. |
| Localisation | All strings externalised from M0 (§16.5). |

---

## 2. Reference design

This section describes the concepts we reproduce so that implementers have a shared mental model. All numbers are ours (Appendix A); nothing is measured from the reference product.

### 2.1 Camera and presentation

- Fixed three-quarter perspective camera, pitched ~50° downward, yawed 45° relative to the world grid, narrow field of view so the picture looks almost isometric but keeps depth. The player cannot rotate the camera. The camera follows the hero smoothly with slight look-ahead.
- World built from 1 m cubes ("blocks"), heights in whole blocks, cliffs visible as block faces. Terrain is not a flat plane: raised platforms, stairs, bridges over water.
- 16×16 pixel textures per block face, nearest-neighbour filtered. Characters are made of textured boxes with rigid-part animation (head, body, arms, legs).
- Dramatic, moody lighting: a cool bluish ambient/sky and warm orange torches and glowing items; soft bloom on emissives; distance fog tinted per level; real-time shadows from one sun light.
- Heavy feedback: enemies flash white on hit, damage numbers pop, hit particles, small camera shake on big hits, enemy health bars appear over damaged enemies.

### 2.2 Core loop

Choose a mission (level) → traverse a linear-ish path with optional side pockets → fight groups of enemies → ambush arenas lock you in until a wave is dead → loot chests and emeralds → reach the exit → results screen → back to level select.

### 2.3 Hero controls

- Direct control: move with stick/WASD/virtual joystick. Hero faces movement direction; attacks face the aim direction (mouse position, right stick, or auto-aim on touch).
- Melee: 3-hit combo with increasing damage; last hit has knockback.
- Ranged: bow consuming arrows; arrows are a finite resource replenished by pickups.
- Dodge roll: short invulnerability, cancels recovery frames, short cooldown.
- Healing potion: full heal, long cooldown.
- Three lives per mission; death returns the hero to the last checkpoint.

### 2.4 Enemies

- Zombie: slow walker, wind-up telegraphed swing, swarms the player.
- Skeleton: keeps distance, visible aim pose before shooting, retreats when approached.
- Both: aggro on sight within radius, alert nearby friends, drop emeralds, play distinct death animation.

### 2.5 Level dressing and logic

- Paths through forests, ruins, water crossings; decorative tall grass, flowers, rocks, barrels, broken pillars.
- Gates that open when a wave is defeated; checkpoints marked by a visible object; the exit is an unmistakable glowing structure.
- Objective text at the top of the screen tells the player what to do next.

---

## 3. Technology selection

### 3.1 Decision

| Concern | Decision |
|---|---|
| Programming language | **C# 12**. Core retains `net8.0;netstandard2.1`; the approved Godot host, tools and executable tests use **net10.0** (user decision 2026-10-27). |
| Host engine (rendering, audio, input, platform packaging) | **Godot 4.7.2, .NET edition**, identical Mono export templates. |
| Simulation / level system | **Engine-agnostic C# class library** (`Dungeons.Core`) with zero Godot references. |
| Tests | xUnit (+ FluentAssertions optional) for Core; GdUnit4 (C# API) for in-engine integration tests; BenchmarkDotNet for performance tests. |
| Data format | JSON (UTF-8, no BOM) validated by JSON Schema (Appendix B) and by the in-code validator. |
| Asset authoring | Blockbench (models/animations → glTF 2.0), Aseprite/any pixel editor (PNG), Audacity/any DAW (OGG Vorbis). |

### 3.2 Why C#

- First-class on all three targets through Godot .NET (and through Unity if the host must ever change), with one codebase and one toolchain (`dotnet`).
- Strong static typing, mature tooling, good refactoring support — essential for a long-lived engine and a future editor.
- Excellent test ecosystem that runs headless on any CI machine: the entire simulation is testable without a GPU.
- Performance adequate for thousands of entities per tick when allocation-free patterns are used (§22.3).
- Shared code between game, tools (validator/packager CLI) and the future level editor: the same `Dungeons.Core` assembly.

Rejected alternatives:

| Option | Why not |
|---|---|
| C++ (custom engine or Unreal) | Highest portability ceiling but much larger implementation surface (platform layers, iOS/Android build systems), slower iteration, higher chance of implementer mistakes. Unreal is heavy for a small blocky game and its mobile footprint is large. |
| GDScript | Fine for small projects; weaker for a large deterministic simulation (dynamic typing, limited tooling, cannot run outside Godot, cannot be shared with CLI tools or an external editor). |
| Unity + C# | Viable fallback (same language). Not chosen first due to proprietary licensing/pricing and closed source; keep as fallback (§3.4). |
| Rust (Bevy/Macroquad) | Mobile toolchains and editor tooling less mature; smaller talent pool. |
| Kotlin/Swift native | Not portable across the three targets without duplicating the game. |
| Web (TypeScript + Three.js) | Store packaging, performance on low-end phones and offline file handling are weaker; WebGL feature parity issues. |
| Flutter/Flame, libGDX | 2D-centric or fragile iOS story (RoboVM). |

### 3.3 Why Godot 4 (.NET)

- Open-source (MIT), no royalties; exports to Windows, Android and iOS from one project.
- Its *Mobile* rendering method (Vulkan on Windows/Android, Metal on iOS in 4.4+) gives us shadows, glow, fog and per-pixel lighting within mobile budgets, and the same method can be used on desktop for identical visuals.
- Node/scene model is good for presentation-only concerns; we deliberately do **not** use its physics for gameplay (§4.2).

Known constraint: C# export for Android and iOS exists since Godot 4.2 but the iOS path has historically been marked experimental. **Milestone 0 (§21) is an export gate**: an empty C# project must run on a real Android device and a real iPhone before any feature work starts. The exact Godot version and .NET SDK are then frozen in `global.json` / `README`.

### 3.4 Host fallback

Because `Dungeons.Core` has no engine dependency, if the Milestone 0 gate fails for a platform, the host layer may be reimplemented in Unity (C#) without touching simulation, level format, tools or tests. The host layer is kept thin for this reason (§4.3); it **MUST NOT** contain gameplay rules.

### 3.5 Toolchain versions (freeze in `README.md` at M0)

- .NET SDK **10.0.402** (approved replacement for the original 8.0.x host/build pin; exact `global.json` pin; Core targets remain unchanged)
- Godot **4.7.2 .NET** (pinned; export templates of the identical version)
- Android: JDK 17, Android SDK Platform **36**, build tools **36.1.0**, NDK **29.0.14206865** when native compilation is required by the pinned template, `minSdk` 26, `arm64-v8a` (+ `x86_64` for emulators in debug)
- iOS: macOS with Xcode 26+/iOS 26 SDK for current App Store builds; iOS deployment target 14.0 remains a requirement pending actual new-host/Mac validation. Current Apple build-SDK requirements are documented in `docs/release-readiness.md`.
- Windows: Windows 10 x64 minimum, Vulkan 1.0-capable GPU (Direct3D 12 driver fallback where Godot supports it)

---

## 4. System architecture

### 4.1 Layer diagram

```
┌──────────────────────────────────────────────────────────────────┐
│  Dungeons.Game (Godot .NET project)  — PRESENTATION + PLATFORM   │
│  scenes, meshes, materials, shaders, particles, audio, UI,       │
│  input devices, file system access, export presets               │
│  ─ WorldView, EntityView, ChunkRenderer, HudController,          │
│    InputAdapter, LevelCatalogUi, SaveStore                       │
└────────────▲────────────────────────────┬────────────────────────┘
             │ reads state + events        │ PlayerInput per tick
┌────────────┴────────────────────────────▼────────────────────────┐
│  Dungeons.Core (netstandard2.1 + net8.0 class library)           │
│  ─ Level: LevelDocument, LevelLoader, LevelValidator, Migrations  │
│  ─ Content: ContentRegistry, TileDef, EntityDef, LootTable       │
│  ─ Sim: World, Entity, Systems, Rng, Collision, NavGrid, A*,     │
│         Triggers, Objectives, Events, Replay, WorldHasher        │
│  ─ Math: Vec2, Vec3, Rect, MathUtil (no System.Numerics reliance)│
└──────────────────────────────────────────────────────────────────┘
             ▲
┌────────────┴────────────────────────────────────────────────────┐
│  Dungeons.Tools (console app) — validate / pack / unpack /       │
│  migrate / ascii-preview / replay-verify                         │
└──────────────────────────────────────────────────────────────────┘
```

### 4.2 Rules of separation (acceptance criteria)

1. `Dungeons.Core` **MUST NOT** reference `GodotSharp`, `System.Drawing`, `System.Windows`, or any platform assembly. CI enforces this with a reference check (§20.9).
2. All gameplay rules (movement, collision, combat, AI, triggers, objectives, loot, lives, checkpoints) live in Core. The Godot project contains **no** `if (hp <= 0)`-style logic.
3. Core never does I/O except through injected interfaces (`IFileSystem`, `ILogger`, `IClock` for tooling). Level loading takes a `Stream`/`string`, not a path.
4. Presentation **reads** simulation state and **consumes** simulation events; it never writes to simulation state except through `PlayerInput` and session commands (`Pause`, `Restart`, `UsePotion` is an input bit, not a command).
5. Godot physics (`PhysicsServer3D`, `CharacterBody3D`, `Area3D`) **MUST NOT** be used for gameplay. It MAY be used for presentation-only queries (none required in v1).

### 4.3 Runtime flow

```
App start
  ├─ load settings/profile (user://)
  ├─ ContentRegistry.LoadBuiltIn()        (res://content/**/*.json)
  ├─ LevelCatalog.Scan()                  (res://levels/index.json + user://levels/**)
  └─ MainMenu → LevelSelect
LevelSelect.Play(levelRef)
  ├─ LevelLoader.Load(stream) → LevelDocument (+diagnostics)
  ├─ LevelValidator.Validate(doc, registry) → must be error-free
  ├─ GameSession.Create(doc, registry, seed)
  │     └─ World.FromLevel(): nav grid, entities, triggers, objectives
  ├─ WorldView.Build(doc, world): chunk meshes, props, lights, env, camera
  └─ Game loop (§7.2)
       every frame: InputAdapter.Sample(); accumulate; while (acc ≥ DT) session.Tick(input);
                    WorldView.Interpolate(alpha); Hud.Update(); drain events → VFX/SFX
  Level end → ResultsScreen → profile update → LevelSelect
```

### 4.4 Key interfaces (Core)

```csharp
public sealed class GameSession {
    public GameSession(LevelDocument level, ContentRegistry content, ulong seed, ILogger log);
    public World World { get; }
    public int Tick { get; }                       // number of ticks executed
    public SessionState State { get; }             // Running, Paused, Won, Failed
    public void Step(in PlayerInput input);        // exactly one fixed tick
    public ReadOnlySpan<SimEvent> DrainEvents();   // events since last drain (presentation calls once per frame)
    public ulong Hash();                           // deterministic state hash
    public ReplayRecorder Recorder { get; }        // optional
}

public readonly struct PlayerInput {
    public Vec2 Move;        // length ≤ 1, world XZ axes (not screen axes) — adapter converts
    public Vec2 Aim;         // unit vector or zero
    public bool AimValid;    // false ⇒ use facing / auto-aim
    public InputButtons Pressed;  // edge-triggered since last tick (latched)
    public InputButtons Held;     // level-triggered
}

[Flags] public enum InputButtons : ushort {
    None=0, Attack=1, Ranged=2, Roll=4, Potion=8, Interact=16, Pause=32,
    Artifact1=64, Artifact2=128, Artifact3=256      // reserved (§1.5), ignored in v1
}
```

Presentation calls `Step` only from the main thread. `World` exposes read-only views (`IReadOnlyList<Entity>`, `NavGrid`, `TriggerState`, `ObjectiveState`).

### 4.5 Multiplayer readiness (not implemented in v1)

Co-op is a backlog item, but the simulation is shaped so that it can be added as a host-authoritative layer later without touching gameplay rules:

- `Step` takes a `ReadOnlySpan<PlayerInput>` internally (one per player slot); the public single-player `Step(in PlayerInput)` overload wraps it. `World.Players` is a list of hero entities; v1 always has exactly one. No system may reference "the hero" except through `World.Players[i]` or an entity id.
- All world mutation already passes through systems fed by inputs, which is exactly what lockstep or host-authoritative replication needs; the replay recorder is a single-player instance of the same input stream.
- `CameraFocus` is computed from the alive players' centroid (v1: the one hero).
- Nothing in v1 should implement actual networking, lobbies, or interpolation of remote players.

---

## 5. Repository and project layout

```
/                              repo root
├─ global.json                 pins .NET SDK
├─ Dungeons.sln
├─ README.md                   toolchain versions, how to build/export/test
├─ specification.md            this document
├─ src/
│  ├─ Dungeons.Core/           class library, TargetFrameworks net8.0;netstandard2.1
│  │  ├─ Math/                 Vec2, Vec3, Rect, IntVec2, MathUtil
│  │  ├─ Content/              ContentRegistry, TileDef, EntityDef, LootTable, PropertySchema
│  │  ├─ Level/                LevelDocument, LevelLoader, LevelValidator, LevelMigrations, LevelPackage
│  │  ├─ Sim/                  World, Entity, components, systems, Rng, Collision, NavGrid, AStar,
│  │  │                        Triggers, Objectives, SimEvent, Replay, WorldHasher
│  │  └─ Util/                 ILogger, IFileSystem, Pool<T>, Fnv1a
│  ├─ Dungeons.Core.Tests/     xUnit; TestLevels/ (small JSON fixtures), Replays/ (golden .dreplay)
│  ├─ Dungeons.Tools/          console: dungeons-tool validate|pack|unpack|migrate|preview|replay
│  └─ Dungeons.Benchmarks/     BenchmarkDotNet
├─ game/                       Godot project (project.godot, Dungeons.Game.csproj → ProjectReference Core)
│  ├─ scenes/                  Main.tscn, Menu/, Game/, Hud/, Debug/
│  ├─ scripts/                 C# partial classes for nodes (presentation only)
│  ├─ shaders/                 terrain.gdshader, water.gdshader, flash.gdshader, dither_fade.gdshader
│  ├─ assets/                  models/ (glTF), textures/ (PNG atlases), audio/ (OGG), fonts/, ui/
│  ├─ content/                 tiles/*.json, entities/*.json, loot/*.json (shipped definitions)
│  ├─ levels/                  index.json, mossy_hollow/level.json, mossy_hollow/preview.png
│  └─ export_presets.cfg
├─ tools/                      CI scripts, Blockbench templates, palette.gpl, texture atlas packer config
└─ .github/workflows/          ci.yml (build, test, export)
```

Naming: namespaces follow folders (`Dungeons.Core.Sim`). Godot node scripts are `partial` classes in `Dungeons.Game.*`. Assets use `snake_case`. Content ids use `namespace:name` (`core:zombie`).

Binary assets (`*.png`, `*.glb`, `*.ogg`, `*.ttf`, `*.dlvl` fixtures) are tracked with **Git LFS** from M0 (`.gitattributes` committed); text assets (JSON, shaders, scenes, `.import` files) stay in plain Git so they diff.

---

## 6. Coordinate systems, units and conventions

Confusing axes is the most common source of bugs in this kind of project. The following is normative.

| Item | Convention |
|---|---|
| Unit | 1 unit = 1 block = 1 metre. |
| World axes (Core) | Right-handed, **Y up**. Ground plane is XZ. **+X = east**, **+Z = south**, **−Z = north**. Matches Godot (Godot forward is −Z). |
| Cell coordinates | Cell `(cx, cz)` covers `[cx, cx+1) × [cz, cz+1)` in XZ. Cell centre = `(cx+0.5, cz+0.5)`. Integers, `0 ≤ cx < width`, `0 ≤ cz < height`. |
| ASCII layers | Row index = `cz`; row 0 is the **north** edge (top of the text). Column index = `cx`. |
| Heights | Integer blocks `0..15`. An entity standing on cell height `h` has its feet at `y = h`. |
| Facing | Stored as a unit `Vec2 (x, z)`. No angles in simulation data. Default facing north = `(0, −1)`. |
| Angles (when needed) | `atan2(z, x)` in radians, used only transiently. |
| Time | Simulation time is tick-based (`int tick`, `TICK_RATE = 60`, `DT = 1/60 s`). All durations in data are seconds (floats) and converted to ticks on load with `ceil(seconds * 60)`. |
| Random | Only `Rng` streams from Core; never `System.Random` in Core. |
| Culture | All number parsing/formatting uses `CultureInfo.InvariantCulture`. |
| Text | UTF-8 without BOM; LF line endings in JSON. |
| Screen → world | Camera yaw 45° means screen-up corresponds to world north-east diagonal; the input adapter rotates stick input by the camera yaw so that "push up" moves the hero *up on screen* (§15.4). |

Godot-side conversion: `new Vector3(v.X, y, v.Z)`. There is no sign flip anywhere. A unit test (`CoordinateConventionTests`) and an in-engine test (`AxisSanityTest`) verify that an entity at cell (0,0) renders at the north-west corner of the level and that cell (width−1, height−1) is south-east.

---

## 7. Simulation core

### 7.1 Design

- Fixed timestep: 60 ticks per second, independent of render framerate.
- Fully deterministic given `(LevelDocument, ContentRegistry, seed, PlayerInput[])`.
- Entity data model: class `Entity` with optional component structs (not a generic ECS framework; keep it debuggable). Systems run in a fixed order (§7.4) and iterate entities in ascending `Id` order.
- Zero steady-state heap allocations per tick (verified by test, §20.4).
- No wall-clock or frame-time access in Core.

### 7.2 Game loop (host)

```csharp
// Presentation (_Process)
accumulator += Math.Min(delta, 0.25);               // clamp: avoids spiral of death after hitches
int steps = 0;
while (accumulator >= DT && steps < 5) {             // max 5 catch-up ticks per frame
    var input = inputAdapter.ConsumeTickInput();     // latched presses are consumed here
    session.Step(input);
    accumulator -= DT; steps++;
}
if (steps == 5) accumulator = 0;                     // drop time rather than freeze
float alpha = (float)(accumulator / DT);
worldView.Interpolate(alpha);                         // lerp prev→curr transforms
foreach (var ev in session.DrainEvents()) vfx.Handle(ev);
```

When the game is paused (menu, OS focus lost, app backgrounded) `Step` is not called and the accumulator is reset on resume.

### 7.3 Entity model

```csharp
public sealed class Entity {
    public int Id;                      // runtime id, ascending allocation, never reused within a session
    public string? LevelId;             // authored id from level.json (may be null for spawned)
    public EntityDef Def;               // immutable definition from ContentRegistry
    public EntityKind Kind;             // Player, Enemy, Projectile, Pickup, Prop, Gate, Chest, Checkpoint, Exit, SpawnMarker, Light, Marker
    public Tags Tags;                   // small set of interned strings (max 8)
    public Vec2 Pos, PrevPos;           // XZ, metres
    public float Y, PrevY;              // derived from terrain each tick (stairs interpolate)
    public Vec2 Facing;                 // unit
    public Vec2 Vel;                    // intended movement velocity this tick (m/s)
    public Vec2 Knockback;              // decaying impulse velocity (m/s)
    public float Radius;                // collision circle; 0 = no collision
    public bool Solid;                  // participates in blocking (gates closed, props)
    public bool Alive;                  // false after death until removal
    public Health Health;               // Max, Current, Armor, InvulnTicks
    public Combat Combat;               // attack state machine (§8.3)
    public Ai Ai;                       // enemy state (§9)
    public Projectile Proj;             // speed, damage, owner, lifetime
    public Pickup Pickup;               // kind, amount
    public Interactable Inter;          // chest/gate state
    public int LifetimeTicks;           // -1 = infinite
    public int SpawnTick;
}
```

Removed entities are stably compacted out of the active list at end of tick (`RemovalSystem`) so indices are stable during a tick and surviving entities remain in ascending id order.

### 7.4 System order (per tick)

| # | System | Responsibility |
|---|---|---|
| 1 | `TimerSystem` | Snapshot `Pos→PrevPos`, `Y→PrevY` before movement; decrement all tick counters (invulnerability, cooldowns, lifetimes, AI timers). |
| 2 | `PlayerControlSystem` | Convert `PlayerInput` into intended velocity / facing / action requests (§8.2). |
| 3 | `AiSystem` | Enemy state machines, path requests, intended velocity (§9). |
| 4 | `PathfindingSystem` | Serve up to `MAX_PATH_REQUESTS_PER_TICK` (20) A* requests from the queue. |
| 5 | `CombatSystem` | Advance attack state machines; on *active* frames compute hits; spawn projectiles (§8.3). |
| 6 | `ProjectileSystem` | Move projectiles, test tile and entity hits, apply damage. |
| 7 | `MovementSystem` | Integrate `Vel + Knockback` with terrain collision (§7.6). |
| 8 | `SeparationSystem` | Resolve entity–entity circle overlaps (§7.7). |
| 9 | `TerrainHeightSystem` | Update `Y` from cell height (stairs interpolate). |
| 10 | `PickupSystem` | Magnet + collect emeralds/food/arrows. |
| 11 | `InteractionSystem` | Chest opening, checkpoint activation, exit detection. |
| 12 | `DamageResolutionSystem` | Apply queued damage, deaths, drops, XP, events. |
| 13 | `TriggerSystem` | Evaluate area/event triggers; run action queues (§10.7). |
| 14 | `ObjectiveSystem` | Update objective states; check completion/failure. |
| 15 | `LivesSystem` | Death timer → respawn at checkpoint or mission failed. |
| 16 | `RemovalSystem` | Remove dead/expired entities; stable compaction preserving ascending ids. Previous transforms retain the start-of-tick snapshot for §13 interpolation. |

Damage is **queued** during systems 5–6 and applied in system 12 so that order of enemies never changes outcomes within a tick (two enemies hitting each other… not possible in v1, but player and skeleton arrow can trade in the same tick; both apply).

### 7.5 Random numbers

- Algorithm: **xoshiro128\*\*** (32-bit outputs), seeded via SplitMix64 from `seed`.
- Streams (independent generators, each seeded from `SplitMix64(seed ^ streamSalt)`): `Combat` (damage variance), `Loot` (drops, chest contents), `Ai` (wander, idle timings), `Spawn` (variant selection). Presentation uses its own unseeded RNG for purely visual noise.
- API: `uint NextU32()`, `float NextFloat()` (`[0,1)`), `int NextInt(minInclusive, maxExclusive)`, `float Range(min, max)`, `bool Chance(p)`.
- Seed selection: `seed = level.seed ?? (profile.runCounter * 0x9E3779B97F4A7C15)`; recorded in replays and results screen (shown as hex for bug reports).

### 7.6 Terrain collision

Terrain is the cell grid from the level (§10.4) baked at load into `NavGrid`:

```csharp
struct Cell { byte Height; bool Walkable; bool Solid; bool BlocksProjectiles; bool BlocksVision; byte StairsDir; float MoveCost; }
```

- `Solid` cells (walls, trees, rocks, closed gates) block movement for everyone.
- Non-walkable, non-solid cells (water, void) block ground movement but not projectiles.
- Passability between orthogonally adjacent cells `a → b`:
  - both `Walkable`, and
  - `|h(a) − h(b)| == 0`, **or** one of them is a stairs cell whose `StairsDir` points along the move axis toward the higher cell and `|Δh| == 1`.
- Diagonal moves require both orthogonal intermediates passable (no corner cutting).

Circle vs terrain resolution (per entity, per tick):

1. Compute candidate position along X only: `p' = (x + vx·DT, z)`. For each of the up-to-3×3 cells around `p'`, treat a cell as a blocking AABB if it is `Solid`, or not passable from the entity's current cell by the rule above. If the circle overlaps a blocking AABB, clamp `x` to the AABB edge ± radius and set `vx = 0`.
2. Repeat along Z.
3. Entities never tunnel: speed cap `MAX_SPEED = 12 m/s` ⇒ `0.2 m` per tick < min(radius) `0.3`; projectiles use swept circle vs cell DDA.

Each entity's `Cell` is the cell containing `Pos`. A stairs cell counts as height `h` for passability but `TerrainHeightSystem` interpolates `Y` linearly across it toward the higher neighbour.

### 7.7 Entity separation

- Circles only. Pairs are found with a uniform spatial hash (cell size 2 m) rebuilt each tick (preallocated).
- Pair rules:
  - Projectiles, pickups, markers, lights: no separation.
  - Enemy–enemy: soft separation, push apart 50/50 by overlap × `0.5` (prevents stacking while allowing crowds).
  - Player–enemy: hard, 50/50 by full overlap. Enemies in `Dead` state are ignored.
  - Anyone–static solid entity (prop with radius, closed gate footprint): entity moves 100 %.
- Separation runs twice per tick for stability.
- After separation, terrain collision is re-validated for pushed entities (a push into a wall is clamped).

### 7.8 Events

Core emits `SimEvent` structs into a preallocated ring buffer (capacity 4096) for presentation (Appendix E). Events carry only ids and values, never object references the host could mutate. Unconsumed events are dropped with a logged warning.

### 7.9 Determinism checklist (enforced by review and tests)

- No `Dictionary`/`HashSet` iteration where order affects results; use `List` sorted by id or arrays.
- No `float` accumulation of time; count ticks.
- No `MathF` functions whose results differ by platform are used in gameplay-critical branches except `Sqrt`, `Abs`, `Min`, `Max`, `Floor`, `Ceiling`. `Sin/Cos/Atan2` are allowed only for facing vectors and arc tests where tiny differences do not change discrete outcomes (hit arcs use dot products instead).
- `WorldHasher` hashes: tick, all entity `Id, Kind, Pos (quantised to 1/1024), Y, Health.Current, Alive`, RNG states, objective/trigger states, lives, inventory. Two runs of the same replay on the same platform **MUST** hash identical at every tick; cross-platform identity is a goal, not a requirement.

---

## 8. Gameplay mechanics

All numeric values are in Appendix A and in content JSON; the text below repeats the v1 defaults for readability. Where text and Appendix A disagree, Appendix A wins.

### 8.1 Hero statistics

| Stat | Value |
|---|---|
| Max health | 100 |
| Armor | 0 (formula supports it) |
| Move speed | 4.5 m/s |
| Collision radius | 0.35 m |
| Visual height | 1.8 m |
| Pickup radius | 1.0 m (magnet radius 2.5 m) |
| Lives per mission | 3 |
| Starting arrows | 30 (max 99) |
| Potion | 1 charge, heals 100 % of max, cooldown 30 s |

### 8.2 Movement, facing, roll

- `Vel = Move * MOVE_SPEED` (Move is already rotated into world axes by the adapter and clamped to length 1). No acceleration in simulation; presentation blends animations.
- Facing: if `Move` length > 0.1, `Facing = normalize(Move)`. When an attack/ranged action starts and `AimValid`, `Facing = Aim` for the duration of the action (the hero can turn to shoot toward the cursor while walking). On touch (no aim), §15.6 auto-aim supplies `Aim`.
- Roll:
  - Trigger: `Pressed.Roll`, not already rolling, roll cooldown 0, hero alive, not in death animation.
  - Direction: `Move` if length > 0.1 else `Facing`.
  - Duration 24 ticks (0.40 s), distance 3.0 m ⇒ speed 7.5 m/s, constant (no easing in sim; presentation eases the animation).
  - Invulnerable ticks 3..20 inclusive (`Health.InvulnTicks` set accordingly).
  - Cancels any attack in windup/recovery (not during active frames — active frames are only 6–7 ticks, cancellation there is unnecessary and complicates hit bookkeeping).
  - Cooldown 36 ticks (0.6 s) measured from roll end.
  - Rolling through enemies: hard separation still applies (no phasing) — matches reference feel; rolling is for i-frames and repositioning.
- Hero cannot move during attack windup/active frames; may move at 40 % speed during recovery ("recovery walk"). Ranged attack allows full movement during recovery.

### 8.3 Melee combat

Attack state machine (`Combat.State`: `Idle, Windup, Active, Recovery`), one per entity.

Hero sword combo:

| Swing | Windup | Active | Recovery | Damage | Knockback (m/s impulse) | Arc half-angle | Reach |
|---|---|---|---|---|---|---|---|
| 1 | 6 ticks | 6 ticks | 12 ticks | 12 | 1.0 | 60° | 1.7 m |
| 2 | 6 | 6 | 12 | 12 | 1.0 | 60° | 1.7 m |
| 3 | 9 | 7 | 21 | 18 | 2.5 | 70° | 1.9 m |

- `Pressed.Attack` in `Idle` ⇒ swing 1. During `Active` or `Recovery` of swing *n* (< 3) a buffered press (buffer window 15 ticks) starts swing *n+1* immediately when `Active` ends (chaining skips the remaining recovery). After swing 3 or a 30-tick gap the combo resets.
- Hit test on the **first** active tick only (one check per swing; simpler and deterministic): a target is hit if `Alive`, hostile, `|Δheight| ≤ 0.5` or target on stairs adjacent, `dist(center, target.Pos) − target.Radius ≤ reach` and `dot(Facing, normalize(target.Pos − Pos)) ≥ cos(halfAngle)`; targets within `0.3 m` of the attacker centre are always inside the arc. Each entity hit at most once per swing.
- Knockback: `target.Knockback += normalize(target.Pos − attacker.Pos) * kb * (1 − target.KnockbackResistance)`. Knockback decays `*= 0.85` per tick and is zeroed below 0.05 m/s. Knockback into a wall simply clamps.
- Damage formula (all sources): `final = max(1, round(base * U(0.90, 1.10) * (1 − armor / (armor + 100))))`, `U` from the `Combat` RNG stream.
- Hit reaction: a target entering `Stagger` (enemies only, 12 ticks) when knockback impulse ≥ 2.0; stagger cancels its current attack.
- After taking damage the hero gets `INVULN_AFTER_HIT = 15 ticks` (0.25 s) during which further damage is ignored (prevents instant multi-zombie deletion on touch devices). Enemies have no such grace.

### 8.4 Ranged combat (bow)

- `Pressed.Ranged` with `arrows > 0` and bow cooldown 0 ⇒ `Windup 6 ticks` → fire arrow → `Recovery 12 ticks` (movement allowed). Cooldown 30 ticks from fire.
- Arrow: speed 14 m/s, radius 0.15 m, damage 10, lifetime 90 ticks, `y = shooterCellHeight + 1.0`.
- Direction: `Aim` if valid else auto-aim (§15.6) else `Facing`.
- Projectile movement: swept per tick in up to 4 sub-steps (`0.2333 m / 4`), test cells with `BlocksProjectiles` or `Height ≥ proj.y` ⇒ destroyed (event `ProjectileHitTerrain`); test hostile entities (circle–circle, hostile = different faction) ⇒ apply damage, destroy (no pierce in v1).
- Arrow pickups ("arrow bundle"): +10 arrows. Skeletons drop one with 50 % probability.

### 8.5 Potion and food

- `Pressed.Potion` with cooldown 0 and hero alive: `Health.Current = Max`, cooldown 1800 ticks, event `PotionUsed`. Usable while attacking/rolling.
- Food pickup: heals 25 % of max immediately on collection (not stored).
- Respawn refills potion (cooldown 0).

### 8.6 Health, death, lives, checkpoints

- Hero death: `Health.Current ≤ 0` ⇒ `Dying` for 120 ticks (death animation, input ignored, no damage). Lives −1.
  - Lives > 0 ⇒ respawn at the active checkpoint position (default: player spawn), full HP, potion ready, arrows unchanged (min 10), knockback cleared, 60 ticks invulnerability, event `PlayerRespawned`. Enemies are **not** reset (reference behaviour: progress persists), but every enemy is forced to `Idle` and loses target so the hero is not instantly swarmed; enemies within 3 m of the checkpoint are pushed out to 3 m along their current offset direction.
  - Lives = 0 ⇒ `SessionState = Failed`, event `MissionFailed`.
- Checkpoint entity: activates when the hero enters within `radius` (default 1.5 m). Only moves forward: a checkpoint with lower `order` than the active one is ignored. Event `CheckpointActivated`.

### 8.7 Pickups and loot

| Pickup | Effect | Magnet | Notes |
|---|---|---|---|
| Emerald | `+amount` emeralds | yes | Spawns in a burst of `n` individual entities with random (Loot RNG) scatter radius 0.3–0.9 m, 0.4 s before collectable. |
| Food | +25 % HP | yes | Only collected when HP < Max; otherwise stays. |
| Arrow bundle | +10 arrows | yes | Only collected when arrows < 99. |

Magnet: pickups within 2.5 m of the hero move toward the hero at 6 m/s (they ignore terrain collision, fly over water); collected within 1.0 m.

Drops on enemy death use the enemy's `lootTable` id (Appendix C): each row rolled independently via Loot RNG.

Chest: interactable prop. Opens automatically when the hero is within 1.2 m (no button; reference behaviour) after 20 ticks of proximity; spawns its `lootTable` result in a burst; event `ChestOpened`; stays open.

### 8.8 Experience (counter only)

Enemies grant XP (`xp` in def). Hero level `L = floor(sqrt(totalXp / 50))`. Displayed in results; no stat effect in v1. Stored per profile.

### 8.9 Objectives and completion

- Objective states: `Hidden → Active → Complete` (or `Failed` for `survive` types). Only `Active` objectives are shown in the HUD; the first active objective is the primary text.
- Objective kinds:
  - `reach` — completes when `target` (checkpoint/marker/exit id) is reached by the hero.
  - `kill_tag` — completes when all entities carrying `tag` that have been spawned so far are dead **and** `minSpawned` (default 1) have been spawned.
  - `interact` — completes when entity `target` is interacted (chest opened, gate opened).
  - `flag` — completes when trigger action `set_flag` sets `flag` true.
  - `survive` — completes `seconds` after the objective became `Active` provided the hero did not die in that window; a hero death resets the timer (the objective is not failed, it restarts — missions are retryable by design). Typical use: hold an arena while a `core:spawner` keeps emitting monsters (§10.6.1).
- Completion rule (`level.completion`): `type: "exit"` — hero enters exit entity radius while every objective id in `requires` is `Complete`. If requirements are missing, the exit shows the "locked" state and a HUD hint. Event `MissionComplete`; results computed: time (ticks/60), emeralds, kills, deaths, XP.

### 8.10 Gates

- Entity `core:gate` with `width` (cells along its `axis`), states `closed | open | opening | closing`. Footprint cells are `Solid` and `BlocksProjectiles` when not `open`. Opening takes 45 ticks (presentation animates; simulation flips solidity at tick 0 of `opening` so the hero is never stuck in it, and pushes out any entity overlapping on `closing`).
- Gates change state only through trigger actions (`set_state`) or `interact` when `def.props.interactable = true` (not used in Mossy Hollow).
- Initial player/enemy footprints must not overlap a closed gate; validation reports LVL1012 before session creation. Closing during play relocates overlapping actors only after every displacement and future respawn has a safe terrain position; otherwise the closing request warns and leaves state unchanged.
- Declarative shortcut: `props.openWhen` may name an objective id. At load the loader **desugars** it into an implicit trigger `{ event: { type: "objective", id: <openWhen>, state: "complete" }, actions: [ { type: "set_state", entity: <gate>, state: "open" } ] }` with id `__gate_<gateId>`. The runtime therefore has exactly one gate-opening mechanism; the editor can offer the simple field and the advanced trigger alike. Validator rule `LVL1025` reports an unknown `openWhen` objective.

---

## 9. Enemy AI and pathfinding

### 9.1 Factions and perception

- Factions: `Hero`, `Monster`. Hostile = different faction. Monsters never damage each other (arrows pass through friends).
- Perception each `PERCEPTION_INTERVAL = 6 ticks` (staggered by `Id % 6` to spread load):
  - Sight: `dist ≤ aggroRadius` and line of sight (grid DDA from eye cell to target cell; blocked by `BlocksVision` cells; height difference > 1 blocks unless on stairs path).
  - Hearing: `dist ≤ hearingRadius` when the hero attacks or an ally within 6 m becomes alerted (alert propagation, one hop per 6 ticks).
- Target is always the hero in v1 (single target), but the code path takes an `Entity target` for future summons.

### 9.2 Zombie (`core:zombie`)

| Stat | Value |
|---|---|
| HP / armor | 40 / 0 |
| Speed | 2.6 m/s |
| Radius | 0.35 |
| Attack | damage 8, reach 1.4 m, half-angle 45°, windup 27 ticks, active 6, recovery 42 |
| Attack trigger distance | 1.3 m (centre to centre minus radii ≤ 1.3) |
| Aggro / hearing | 7 m / 10 m |
| Knockback resistance | 0 |
| XP | 10 |
| Loot | `core:loot_zombie` (emerald 60 % ×1–3, food 10 %) |

State machine:

```
Idle ──sees/hears──▶ Alert(18 ticks, '!' event) ──▶ Chase
Chase: repath to target (§9.4); move along path at speed; if in attack range & facing within 30° ⇒ Attack
       if target lost for 180 ticks ⇒ Return (path to spawn pos) ⇒ Idle
Attack: Windup (turn toward target allowed for first 12 ticks, then locked) → Active (hit test once) → Recovery → Chase
Stagger(12) → Chase
Dead: 90 ticks corpse (no collision) → removed
```

Idle behaviour: every 3–6 s (Ai RNG) pick a wander point within 2 m of spawn, walk there at 50 % speed.

### 9.3 Skeleton (`core:skeleton`)

| Stat | Value |
|---|---|
| HP / armor | 25 / 0 |
| Speed | 2.8 m/s |
| Radius | 0.30 |
| Arrow | damage 6, speed 11 m/s, lifetime 90 ticks |
| Shoot cycle | Aim 36 ticks (telegraph, facing tracks target for first 24 ticks) → Fire → Recovery 84 ticks |
| Preferred range | 5–8 m |
| Retreat distance | < 3.5 m ⇒ retreat for up to 90 ticks (move away along path-free direction with wall sliding), then shoot regardless |
| Aggro / hearing | 10 m / 12 m |
| XP | 12 |
| Loot | `core:loot_skeleton` (emerald 60 % ×1–3, arrows 50 %) |

State machine: `Idle → Alert → Position (move until within 5–8 m with LOS) → Aim → Fire → Recovery → Position`, with `Retreat` interrupting `Position/Recovery` when too close, and `Stagger` as for zombie. Skeleton requires LOS to shoot; if no LOS for 60 ticks it repaths to the hero.

### 9.4 Pathfinding

- Grid A* on `NavGrid`, 8-connected, costs `1.0` orthogonal, `1.41421` diagonal, multiplied by cell `MoveCost` (tall grass 1.0, path 0.9 to prefer roads visually, stairs 1.2). Octile heuristic. Open list = binary heap preallocated to `width*height`. Closed/visited arrays reused (generation counter, no clearing).
- Requests are queued; `PathfindingSystem` serves at most 20 per tick, oldest first, result stored as an `IntVec2[]` in a pooled buffer (max 256 waypoints; longer paths are truncated and re-requested on arrival).
- Repath policy: every 15 ticks while chasing **if** target moved > 1 cell since last request, or on arrival at the last waypoint, or when blocked (progress < 0.05 m over 20 ticks ⇒ repath with a 1-cell random (Ai RNG) offset goal).
- Path following: steer toward the next waypoint centre; advance when within 0.3 m; string-pulling lookahead of 1 waypoint when LOS over walkable cells exists (keeps movement from looking grid-aligned).
- Impossible paths (goal unreachable): A* returns the closest reached node; enemy walks there, then idles for 60 ticks before retrying.
- Budget: a 256×256 level with 64 chasing enemies must stay under 1.0 ms/tick for pathfinding on the reference desktop (§19); test enforces.

### 9.5 Spawning and caps

- Max 64 live monsters; spawn actions beyond the cap are queued and spawn as monsters die (event `SpawnDeferred` logged in debug).
- Spawned monsters appear with a 30-tick `Emerging` state (no collision/damage, presentation plays rise-from-ground) then `Alert`.

---

## 10. Level system

### 10.1 Goals

- A level is a self-contained **package** readable without the game codebase: `level.json` + optional `preview.png` + optional `assets/` overrides.
- Human-readable and diff-friendly (ASCII layers), yet machine-validatable (JSON Schema + semantic validator).
- Versioned (`formatVersion`) with forward migrations so old levels keep loading.
- Everything an editor needs (metadata, property schemas, validation diagnostics with locations) is available from `Dungeons.Core`.

### 10.2 Package layout

```
mossy_hollow/                       (directory form, used in repo and in user://levels/)
├─ level.json                       required
├─ preview.png                      optional, 320×180 PNG shown in level select
└─ assets/                          optional (v1: ignored except for validation warning)
mossy_hollow.dlvl                   (packed form = ZIP of the directory, deflate, UTF-8 names)
```

Sources scanned by `LevelCatalog`:

1. Built-in: `res://levels/index.json` lists relative directories (explicit manifest because exported PCKs do not support reliable directory enumeration of `.json` files on all platforms).
2. User: `user://levels/*/level.json` and `user://levels/*.dlvl` (zip read in memory via `System.IO.Compression`).

Identity: `id` (reverse-DNS-like, `[a-z0-9_.]+`, e.g. `core.mossy_hollow`). If two packages share an `id`, the user copy wins over built-in, and the higher `version` (SemVer string) wins among user copies; conflicts are reported as warnings in the catalogue.

### 10.3 `level.json` top-level structure

```jsonc
{
  "formatVersion": 1,                   // integer; loader migrates older versions
  "id": "core.mossy_hollow",
  "name": "Mossy Hollow",               // string or {"en": "...", "de": "..."}
  "author": "Block Dungeons Team",
  "version": "1.0.0",
  "description": "A quiet forest hides an old ruin.",
  "difficulty": 1,                      // 1..5, informational
  "recommendedTimeSeconds": 300,        // informational
  "seed": null,                         // null ⇒ random per run; integer ⇒ fixed
  "size": { "width": 32, "height": 40 },
  "environment": { ... },               // §10.5
  "palette": { ... },                   // char → tile ref, §10.4
  "layers": {
    "floor":   [ "TTTT...", ... ],      // height rows × width chars, required
    "heights": [ "0000...", ... ],      // digits 0-9 or letters a-f for 10-15, required
    "overlay": [ "    ...", ... ]       // optional second tile layer (decorations on top of floor)
  },
  "cells": [ { "x": 15, "z": 12, "props": { "dir": "north" } } ],   // optional per-cell property overrides
  "entities": [ ... ],                  // §10.6
  "triggers": [ ... ],                  // §10.7
  "objectives": [ ... ],                // §8.9
  "completion": { "type": "exit", "exit": "exit_main", "requires": ["obj_ambush"] },
  "flags": { "gate_opened": false }     // optional initial flags
}
```

Limits: `width, height ∈ [8, 256]`; entities ≤ 4096; triggers ≤ 512; objectives ≤ 32.

### 10.4 Tile layers and palette

- `palette` maps **one character** to a tile reference: either a string tile id (`"core:grass"`) or an object `{ "tile": "core:stairs", "props": { "dir": "north" } }`. The object form may also carry `"overlay": "core:tall_grass"` so that simple levels can express floor + decoration in a single layer without the `overlay` layer.
- Reserved characters: space `" "` in `overlay` = nothing. Space is invalid in `floor`.
- `floor` is the walkable/structural layer. Composite tiles such as `core:tree_oak` declare a `baseFloor` (grass) that is rendered under the model.
- `overlay` (optional) places a decoration tile (tall grass, flowers, cracks) on top of a floor tile; an overlay tile **MUST** have `overlay: true` in its definition. Overlay tiles may add `MoveCost` but cannot change walkability/solidity (validator error otherwise).
- `heights` gives the walking-surface height of each cell. Water cells have a height too: it is the height of the surrounding banks; the riverbed is rendered at `h − 1` and the water surface at `h − 0.15` (§13.3). A bridge tile over water is walkable at height `h`.
- `cells` overrides props for individual cells (e.g. stairs direction, variant index). Palette-level props are the default.

### 10.5 Environment block

```jsonc
"environment": {
  "sky":      { "top": "#1b2a44", "horizon": "#35506e", "ground": "#101820" },
  "sun":      { "yawDeg": 35, "pitchDeg": -55, "color": "#fff1dc", "energy": 1.1, "shadows": true },
  "ambient":  { "color": "#5a6f99", "energy": 0.45 },
  "fog":      { "color": "#2e4a62", "density": 0.015, "startDistance": 18, "enabled": true },
  "glow":     { "enabled": true, "intensity": 0.6, "threshold": 1.0 },
  "camera":   { "yawDeg": 45, "pitchDeg": -50, "distance": 22, "fovDeg": 25, "lookAhead": 1.5 },
  "music":    "core:music_forest",
  "ambience": "core:amb_forest_night",
  "waterColor": "#2f6f8f"
}
```

All fields optional with the above defaults (except per-level mood colors which SHOULD be set). The loader fills defaults so presentation code never checks for null.

### 10.6 Entities

```jsonc
{ "id": "z_corridor_1",            // optional, unique within level, [a-zA-Z0-9_]+
  "type": "core:zombie",           // required, must exist in ContentRegistry
  "x": 24.5, "z": 30.5,            // required, world metres (not cells)
  "facing": "south",               // optional: north|east|south|west or [dx, dz]; default south
  "tags": ["corridor"],            // optional
  "props": { }                     // optional, validated against EntityDef.PropertySchema
}
```

Special types (all in Appendix C): `core:player_spawn` (exactly one required), `core:checkpoint` (`order` int, `radius`), `core:exit` (`radius`), `core:gate` (`axis: "x"|"z"`, `width`, `state`, `openWhen`), `core:chest` (`loot`), `core:emerald_pile` (`amount`), `core:food`, `core:arrow_bundle`, `core:spawn_marker`, `core:spawner` (§10.6.1), `core:torch` (`color`, `radius`, `energy`, `flicker`), `core:light` (same, invisible), `core:prop_*`, monsters.

#### 10.6.1 Declarative spawner (`core:spawner`)

Explicit `spawn` actions (§10.7) are the general mechanism, but the most common authoring need — "when the player gets here, N monsters appear over time, at most K alive" — should not require a hand-written action list. `core:spawner` is a marker entity with:

| Prop | Type | Default | Meaning |
|---|---|---|---|
| `entity` | ref(entity) | — | Monster def to spawn (must be `kind: Enemy`). |
| `count` | int 1..100 | 1 | Total to spawn over the spawner's life. |
| `interval` | float s | 1.0 | Seconds between spawns. |
| `maxAlive` | int 1..64 | `count` | Spawner pauses while this many of its own spawns are alive. |
| `radius` | float m | 0 | Random (Spawn RNG) offset around the marker, snapped to the nearest walkable cell. |
| `activation` | enum | `trigger` | `immediate` (level start), `proximity` (hero within `activationRadius`), `trigger` (started by action `set_state` with state `active`), `sight` (hero has LOS within `activationRadius`). |
| `activationRadius` | float m | 8 | For `proximity`/`sight`. |
| `delay` | float s | 0 | Delay after activation before the first spawn. |
| `tag` | ref(tag) | — | Tag applied to every spawn (feeds `kill_tag` objectives and `entities_dead` events). |

Desugaring at load: the spawner becomes an entity with a `SpawnerState { Remaining, NextTick, Active }` component updated by `SpawnSystem` (runs inside `TriggerSystem`'s slot, before actions) and uses the same `SpawnEntity` code path and monster cap as the `spawn` action. It emits `SpawnerFinished` when `Remaining == 0`. The validator checks `entity`, `tag`, and that the marker cell is walkable (`LVL1026`). `kill_tag` objectives count the spawner's `count` toward `minSpawned` expectations so that "kill all" cannot complete before the spawner has finished (`ObjectiveSystem` treats `minSpawned = max(minSpawned, Σ count of spawners with that tag)`).

Mossy Hollow deliberately keeps its explicit action-based ambush as the regression fixture for the general mechanism; a second fixture level `test_spawner_arena` (8×8, in `src/Dungeons.Core.Tests/TestLevels/`) covers the spawner path.

Position rules validated: entity centre must be inside level bounds; monsters/hero/chests/checkpoints/exit must stand on a walkable cell (error); props with collision must not overlap each other more than 50 % (warning).

### 10.7 Triggers and actions

Triggers are data-driven event handlers:

```jsonc
{
  "id": "trg_ambush",
  "once": true,                             // default true
  "enabled": true,                          // default true; actions can toggle
  "event": { "type": "enter", "area": { "x": 11, "z": 14, "w": 10, "h": 5 }, "who": "player" },
  "conditions": [ { "type": "objective", "id": "obj_cross", "state": "complete" } ],   // all must hold
  "actions": [
    { "type": "message", "text": "Ambush!", "seconds": 2.5 },
    { "type": "objective", "id": "obj_ambush", "state": "active" },
    { "type": "spawn", "at": "sm_1", "entity": { "type": "core:zombie", "tags": ["wave1"] } },
    { "type": "delay", "seconds": 0.5 },
    { "type": "sound", "id": "core:sfx_horn" }
  ]
}
```

Event types: `level_start`, `enter`/`exit` (area, `who: player|monster|any`), `entities_dead` (`tag` — all spawned-so-far with tag dead, at least `min` spawned), `objective` (`id`, `state`), `interact` (`entity` id), `checkpoint` (`id`), `flag` (`flag`, `value`), `timer` (`seconds` after enable).

Actions are executed **sequentially** by a per-trigger action cursor; `delay` suspends the cursor for N ticks; all other actions complete in the same tick. Full list in Appendix D.

Semantics guaranteed by Core:
- Triggers are evaluated once per tick in array order after movement; a trigger fires at most once per tick.
- `once` triggers disable themselves after their action list completes.
- An action referencing a missing id is a **validation error** at load, never a runtime exception.

### 10.8 Loading pipeline

```
bytes ─▶ Utf8 decode ─▶ JSON parse (System.Text.Json, strict: no comments in packages, trailing commas disallowed,
          max depth 32) ─▶ formatVersion dispatch ─▶ migrations (v1→v2→…) on the JSON DOM ─▶ bind to LevelDocument
       ─▶ LevelValidator (structural + semantic) ─▶ LevelLoadResult { Document?, Diagnostics[] }
```

- `Diagnostic { Severity (Error|Warning|Info), Code ("LVL0001"), Message, JsonPath ("/entities/3/x"), Cell? }` — every diagnostic has a stable code (documented list in `docs/diagnostics.md` generated from the enum) so an editor can map them to UI.
- Loading never throws for bad input; it returns errors. Unexpected exceptions are caught at the boundary and converted to `LVL9999 Internal error` with the exception text (still logged).
- Unknown JSON properties ⇒ `Warning LVL0100 unknown property` (forward compatibility), except inside `props` where unknown keys are errors according to the schema (`additionalProperties` false per def).
- Load must complete in < 300 ms for a 256×256 level on reference desktop (excluding rendering).

### 10.9 Validation rules (semantic, beyond JSON Schema)

| Code | Severity | Rule |
|---|---|---|
| LVL1001 | Error | `layers.floor` row count ≠ `size.height` or any row length ≠ `size.width` (same for `heights`, `overlay`). |
| LVL1002 | Error | Character not in palette. |
| LVL1003 | Error | Palette tile id unknown to registry. |
| LVL1004 | Error | Overlay tile without `overlay: true`, or floor tile with `overlay: true`. |
| LVL1005 | Error | Height char invalid or > 15. |
| LVL1006 | Error | Stairs cell whose `dir` neighbour is not exactly `h+1` and walkable. |
| LVL1010 | Error | Missing or multiple `core:player_spawn`. |
| LVL1011 | Error | Entity type unknown; props fail schema. |
| LVL1012 | Error | Entity requiring ground placed on non-walkable or out-of-bounds cell. |
| LVL1013 | Error | Duplicate entity id. |
| LVL1014 | Warning | Monster placed with no path to player spawn (unreachable). |
| LVL1020 | Error | Trigger/action/objective references unknown id (entity, marker, objective, flag, sound, tag never used). |
| LVL1021 | Error | Completion requires unknown objective or exit id. |
| LVL1022 | Warning | Objective never set `active` by any action and not `active` initially. |
| LVL1023 | Warning | `entities_dead` tag not produced by any entity, spawn action or spawner. |
| LVL1024 | Error | `survive` objective without positive `seconds`. |
| LVL1025 | Error | Gate `openWhen` references an unknown objective. |
| LVL1026 | Error | Spawner `entity` unknown or not an enemy, or marker cell not walkable. |
| LVL1027 | Error | Action type is a reserved-but-unsupported name (`camera_focus`, `dialogue`) — reported as "not supported in this version" to distinguish from typos. |
| LVL1030 | Warning | Exit unreachable from player spawn considering gates as open. |
| LVL1031 | Error | Exit unreachable from player spawn even with all gates open. |
| LVL1040 | Warning | Level has no checkpoint. |
| LVL1050 | Warning | More than 64 monsters authored (cap). |
| LVL1060 | Info | Tile variant/props default applied. |

Reachability uses the same `NavGrid` and A* as the game — one implementation, no divergence.

### 10.10 Migrations

- `LevelMigrations.Migrate(JsonNode, fromVersion) → JsonNode` chain; each step is a pure function with a unit test that migrates a stored fixture and compares to the expected output.
- `formatVersion` newer than supported ⇒ error `LVL0002` ("made with a newer version").
- `dungeons-tool migrate <in> <out>` rewrites packages to the current version.

### 10.11 Level catalogue and selection UI contract

`LevelCatalog.Entry { Id, Name, Author, Version, Difficulty, Source (BuiltIn|User), Path, PreviewBytes?, Diagnostics[], IsPlayable, Record? (best time, completed, emeralds) }`

- Scanning happens at startup and on "Refresh" (user levels); it reads only `level.json` headers plus a full validation so broken levels appear greyed out with a diagnostic tooltip instead of vanishing.
- Selection screen (§16.3) shows built-in first, then user levels alphabetically.
- Launch: `--level <path-or-id>` command-line argument (Windows) / Godot `OS.GetCmdlineUserArgs()` starts the level directly (for automated tests and the future editor's "play").

### 10.12 Packaging tool

`dungeons-tool pack <dir> <out.dlvl>` validates then zips; `unpack` reverses; `preview <level.json>` prints ASCII map with entity overlays; `validate` returns exit code 1 on errors (used by CI for built-in levels).

---

## 11. Content registry

### 11.1 Purpose

A single in-memory catalogue of every *kind* the engine knows: tiles, entities, loot tables, sounds, music. Loaded from JSON at startup (`res://content/**`), optionally extended per level later. The registry is what makes the editor possible: every kind self-describes its properties.

### 11.2 Tile definition (`content/tiles/*.json`)

```jsonc
{
  "id": "core:tree_oak",
  "displayName": "Oak Tree",
  "category": "nature",                 // editor grouping
  "baseFloor": "core:grass",            // rendered under the model (composite tile)
  "walkable": false,
  "solid": true,
  "blocksProjectiles": true,
  "blocksVision": true,
  "moveCost": 1.0,
  "overlay": false,
  "render": {
    "kind": "model",                    // block | model | water | none
    "model": "models/tiles/tree_oak.glb",
    "variants": 3,                      // model has variants tree_oak_0..2 or texture variants
    "randomRotation": true,             // 0/90/180/270 by Spawn RNG, deterministic from cell hash
    "castsShadow": true
  },
  "editor": { "icon": "editor/icons/tiles/tree_oak.png", "char": "T" },
  "props": { }                          // property schema (§11.4)
}
```

Block-rendered tiles: `"render": { "kind": "block", "top": "atlas:grass_top", "side": "atlas:dirt_side", "tint": "#7bb04a" }`. Water: `"kind": "water"`. Stairs: `"kind": "stairs"` with `props.dir`.

v1 tile set: `core:grass`, `core:tall_grass` (overlay), `core:flowers` (overlay), `core:dirt_path`, `core:stone_floor`, `core:stone_floor_cracked`, `core:stone_wall`, `core:water`, `core:bridge_planks`, `core:stairs`, `core:tree_oak`, `core:rock`, `core:void` (non-walkable abyss, renders nothing, used for level edges in future levels).

### 11.3 Entity definition (`content/entities/*.json`)

```jsonc
{
  "id": "core:zombie",
  "displayName": "Zombie",
  "category": "monster",
  "kind": "Enemy",
  "faction": "Monster",
  "radius": 0.35,
  "solid": false,
  "stats": { "hp": 40, "armor": 0, "speed": 2.6, "knockbackResistance": 0, "xp": 10,
             "aggroRadius": 7, "hearingRadius": 10 },
  "attack": { "kind": "melee", "damage": 8, "reach": 1.4, "halfAngleDeg": 45,
              "windup": 0.45, "active": 0.1, "recovery": 0.7, "triggerDistance": 1.3, "knockback": 1.0 },
  "brain": "core:brain_melee_chaser",     // AI behaviour id implemented in Core (§9)
  "loot": "core:loot_zombie",
  "render": { "model": "models/characters/zombie.glb", "scale": 1.0,
              "animations": { "idle": "idle", "walk": "walk", "attack": "attack", "hit": "hit", "death": "death", "emerge": "emerge" },
              "healthBar": true, "shadowBlob": true },
  "audio": { "alert": "core:sfx_zombie_alert", "attack": "core:sfx_zombie_swing", "hit": "core:sfx_zombie_hit", "death": "core:sfx_zombie_death", "footstep": "core:sfx_step_soft" },
  "editor": { "icon": "editor/icons/entities/zombie.png", "char": "Z" },
  "props": {
    "patrolRadius": { "type": "float", "default": 2.0, "min": 0, "max": 10, "label": "Wander radius" }
  }
}
```

Brains available in v1: `core:brain_melee_chaser`, `core:brain_archer`, `core:brain_none`. New behaviours are code; new monsters with existing behaviours are data only.

### 11.4 Property schema

Used for both tile and entity `props`, and consumed by the validator and the future editor to auto-generate inspectors:

```jsonc
"props": {
  "dir":    { "type": "enum", "values": ["north","east","south","west"], "default": "north", "label": "Direction" },
  "amount": { "type": "int", "default": 5, "min": 1, "max": 99 },
  "radius": { "type": "float", "default": 1.5, "min": 0.5, "max": 10, "step": 0.5 },
  "state":  { "type": "enum", "values": ["closed","open"], "default": "closed" },
  "loot":   { "type": "ref", "refKind": "loot", "default": "core:loot_chest_common" },
  "label":  { "type": "string", "default": "", "maxLength": 64, "localized": true },
  "flag":   { "type": "bool", "default": false }
}
```

Types: `int, float, bool, string, enum, color, ref (refKind: entity|loot|sound|tag|objective|flag|marker), vec2`.

### 11.5 Loot tables (`content/loot/*.json`)

```jsonc
{ "id": "core:loot_zombie",
  "rows": [
    { "item": "emerald", "chance": 0.6, "min": 1, "max": 3 },
    { "item": "food",    "chance": 0.1 }
  ] }
```

### 11.6 Registry API

```csharp
public sealed class ContentRegistry {
    public IReadOnlyDictionary<string, TileDef> Tiles { get; }
    public IReadOnlyDictionary<string, EntityDef> Entities { get; }
    public IReadOnlyDictionary<string, LootTable> Loot { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public static ContentRegistry Load(IEnumerable<(string path, string json)> files, ILogger log);
    public TileDef Tile(string id);     // throws ContentMissingException only for programmer errors; validator prevents at level load
}
```

Registry load is itself validated (duplicate ids, missing models are warnings, invalid schema errors). The game refuses to start with registry errors (fail-fast dialog with the diagnostic list).

---

## 12. The basic level: "Mossy Hollow"

### 12.1 Design intent

A 32 × 40 forest level teaching every mechanic once, playable in 3–5 minutes:

1. **Start clearing** (south-west): safe area, emerald pile to teach pickups, an optional **side pocket** to the north with a chest guarded by two zombies (teaches exploration).
2. **Bridge** over a river (teaches water blocks movement, arrows fly over it).
3. **Forest corridor** going north along the east side: three zombies spaced out, one skeleton on the path (teaches melee, then ranged threat and roll).
4. **Ambush clearing**: entering triggers a wave of four zombies and one skeleton emerging around the player; the gate north stays closed until the wave is dead (teaches gates/objectives).
5. **Stairs** up to a raised **ruin** with three skeletons and a chest; the **exit beacon** at the north wall.

Two riverbank nooks (dead ends reachable from the bridge) hold an emerald pile and an arrow bundle to reward looking around.

### 12.2 Map (floor layer, row 0 = north)

```
col:      0         1         2         3
          01234567890123456789012345678901
row  0    TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT
row  1    TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT
row  2    TTTTTTT###################TTTTTT
row  3    TTTTTTT#_______c_______c_#TTTTTT
row  4    TTTTTTT#__c______________#TTTTTT
row  5    TTTTTTT#_#_____________#_#TTTTTT
row  6    TTTTTTT#_______________c_#TTTTTT
row  7    TTTTTTT#____c____________#TTTTTT
row  8    TTTTTTT#_#_____________#_#TTTTTT
row  9    TTTTTTT#__________c______#TTTTTT
row 10    TTTTTTT#______c__________#TTTTTT
row 11    TTTTTTT########__#########TTTTTT
row 12    TTTTTTTTTTTTTTTssTTTTTTTTTTTTTTT
row 13    TTTTTTTTTT#####::#####TTTTTTTTTT
row 14    TTTTTTTTTT.,..,::.,.,.TTTTTTTTTT
row 15    TTTTTTTTTT..,.f::..,..TTTTTTTTTT
row 16    TTTTTTTTTT,...,::r..,.TTTTTTTTTT
row 17    TTTTTTTTTT..,..::.,...TTTTTTTTTT
row 18    TTTTTTTTTT.,...::.,..,TTTTTTTTTT
row 19    TTTTTTTTTT..,,.::::::::::.,..TTT
row 20    TTTTTTTTTTTTTT~~~~TTTT.,:::.,TTT
row 21    TTTTTTTTTTTTTT~~~~TTTT,.:::..TTT
row 22    TTTTTTTTTTTTTT.~~.TTTT..:::.,TTT
row 23    TTTTTTTTTTTTTT.~~.TTTT.,.:::.TTT
row 24    TTT.,f.,.TTTTT.~~.TTTT,..:::,TTT
row 25    TTT,..r.,TTTTT.~~.TTTT...:::.TTT
row 26    TTT..,..fTTTTT.~~.TTTT.,.:::,TTT
row 27    TTT.f...,TTTTT.~~.TTTT..:::.,TTT
row 28    TTT,..,..TTTTT.~~.TTTT.,:::..TTT
row 29    TTT.:...,TTTTT.~~.TTTT..:::.,TTT
row 30    TTTT:TTTTTTTTT.~~.TTTT.,:::,.TTT
row 31    TTTT:TTTTTTTTT.~~.TTTT..:::..TTT
row 32    TTTT:TTTTTTTTT.~~.TTTT.,:::.,TTT
row 33    TTT.:..,..,..T.~~.,..,..:::..TTT
row 34    TT..:,..:::::::==::::::::::.,TTT
row 35    TT.,..:::::::::==:::::.,..,..TTT
row 36    TT,...,..,.,...~~..,..,..,...TTT
row 37    TT.,..f...,..,,~~.,..f..,..,.TTT
row 38    TTTTTTTTTTTTTTT~~TTTTTTTTTTTTTTT
row 39    TTTTTTTTTTTTTTT~~TTTTTTTTTTTTTTT
```

Heights: rows 2–11, columns 7–25 are height `1` (the ruin plateau including its walls); every other cell is `0`. Row 12 columns 15–16 are stairs (height 0, `dir: north`).

Legend: `T` oak tree, `.` grass, `,` grass + tall grass, `f` grass + flowers, `:` dirt path, `#` stone wall, `_` stone floor, `c` cracked stone floor, `~` water, `=` bridge planks, `s` stairs, `r` rock.

### 12.3 `level.json` (complete)

```json
{
  "formatVersion": 1,
  "id": "core.mossy_hollow",
  "name": "Mossy Hollow",
  "author": "Block Dungeons Team",
  "version": "1.0.0",
  "description": "A quiet forest hides an old ruin. Cross the river, survive the ambush, and reach the beacon.",
  "difficulty": 1,
  "recommendedTimeSeconds": 300,
  "seed": null,
  "size": { "width": 32, "height": 40 },
  "environment": {
    "sky":     { "top": "#17233a", "horizon": "#2f4a66", "ground": "#0e1620" },
    "sun":     { "yawDeg": 35, "pitchDeg": -55, "color": "#ffe9c8", "energy": 1.1, "shadows": true },
    "ambient": { "color": "#55698f", "energy": 0.45 },
    "fog":     { "color": "#24405a", "density": 0.018, "startDistance": 16, "enabled": true },
    "glow":    { "enabled": true, "intensity": 0.6, "threshold": 1.0 },
    "camera":  { "yawDeg": 45, "pitchDeg": -50, "distance": 22, "fovDeg": 25, "lookAhead": 1.5 },
    "music": "core:music_forest",
    "ambience": "core:amb_forest_night",
    "waterColor": "#2f6f8f"
  },
  "palette": {
    "T": "core:tree_oak",
    ".": "core:grass",
    ",": { "tile": "core:grass", "overlay": "core:tall_grass" },
    "f": { "tile": "core:grass", "overlay": "core:flowers" },
    ":": "core:dirt_path",
    "#": "core:stone_wall",
    "_": "core:stone_floor",
    "c": "core:stone_floor_cracked",
    "~": "core:water",
    "=": "core:bridge_planks",
    "s": { "tile": "core:stairs", "props": { "dir": "north" } },
    "r": "core:rock"
  },
  "layers": {
    "floor": [
      "TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT",
      "TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT",
      "TTTTTTT###################TTTTTT",
      "TTTTTTT#_______c_______c_#TTTTTT",
      "TTTTTTT#__c______________#TTTTTT",
      "TTTTTTT#_#_____________#_#TTTTTT",
      "TTTTTTT#_______________c_#TTTTTT",
      "TTTTTTT#____c____________#TTTTTT",
      "TTTTTTT#_#_____________#_#TTTTTT",
      "TTTTTTT#__________c______#TTTTTT",
      "TTTTTTT#______c__________#TTTTTT",
      "TTTTTTT########__#########TTTTTT",
      "TTTTTTTTTTTTTTTssTTTTTTTTTTTTTTT",
      "TTTTTTTTTT#####::#####TTTTTTTTTT",
      "TTTTTTTTTT.,..,::.,.,.TTTTTTTTTT",
      "TTTTTTTTTT..,.f::..,..TTTTTTTTTT",
      "TTTTTTTTTT,...,::r..,.TTTTTTTTTT",
      "TTTTTTTTTT..,..::.,...TTTTTTTTTT",
      "TTTTTTTTTT.,...::.,..,TTTTTTTTTT",
      "TTTTTTTTTT..,,.::::::::::.,..TTT",
      "TTTTTTTTTTTTTT~~~~TTTT.,:::.,TTT",
      "TTTTTTTTTTTTTT~~~~TTTT,.:::..TTT",
      "TTTTTTTTTTTTTT.~~.TTTT..:::.,TTT",
      "TTTTTTTTTTTTTT.~~.TTTT.,.:::.TTT",
      "TTT.,f.,.TTTTT.~~.TTTT,..:::,TTT",
      "TTT,..r.,TTTTT.~~.TTTT...:::.TTT",
      "TTT..,..fTTTTT.~~.TTTT.,.:::,TTT",
      "TTT.f...,TTTTT.~~.TTTT..:::.,TTT",
      "TTT,..,..TTTTT.~~.TTTT.,:::..TTT",
      "TTT.:...,TTTTT.~~.TTTT..:::.,TTT",
      "TTTT:TTTTTTTTT.~~.TTTT.,:::,.TTT",
      "TTTT:TTTTTTTTT.~~.TTTT..:::..TTT",
      "TTTT:TTTTTTTTT.~~.TTTT.,:::.,TTT",
      "TTT.:..,..,..T.~~.,..,..:::..TTT",
      "TT..:,..:::::::==::::::::::.,TTT",
      "TT.,..:::::::::==:::::.,..,..TTT",
      "TT,...,..,.,...~~..,..,..,...TTT",
      "TT.,..f...,..,,~~.,..f..,..,.TTT",
      "TTTTTTTTTTTTTTT~~TTTTTTTTTTTTTTT",
      "TTTTTTTTTTTTTTT~~TTTTTTTTTTTTTTT"
    ],
    "heights": [
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000001111111111111111111000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000",
      "00000000000000000000000000000000"
    ]
  },
  "entities": [
    { "id": "spawn",        "type": "core:player_spawn", "x": 4.5,  "z": 36.5, "facing": "north" },

    { "id": "cp_bridge",    "type": "core:checkpoint", "x": 19.5, "z": 34.5, "props": { "order": 1, "radius": 1.5 } },
    { "id": "cp_clearing",  "type": "core:checkpoint", "x": 20.5, "z": 19.5, "props": { "order": 2, "radius": 1.5 } },
    { "id": "cp_gate",      "type": "core:checkpoint", "x": 15.5, "z": 14.5, "props": { "order": 3, "radius": 1.5 } },

    { "id": "exit_main",    "type": "core:exit",       "x": 16.0, "z": 4.0,  "props": { "radius": 1.0 } },

    { "id": "gate_main",    "type": "core:gate",       "x": 16.0, "z": 13.5, "props": { "axis": "x", "width": 2, "state": "closed" } },

    { "id": "chest_pocket", "type": "core:chest",      "x": 7.5,  "z": 27.5, "props": { "loot": "core:loot_chest_common" } },
    { "id": "chest_ruin",   "type": "core:chest",      "x": 9.5,  "z": 4.5,  "props": { "loot": "core:loot_chest_common" } },

    { "id": "em_start",     "type": "core:emerald_pile", "x": 8.5,  "z": 36.5, "props": { "amount": 5 } },
    { "id": "em_corridor",  "type": "core:emerald_pile", "x": 27.5, "z": 31.5, "props": { "amount": 5 } },
    { "id": "em_nook_w",    "type": "core:emerald_pile", "x": 14.5, "z": 22.5, "props": { "amount": 8 } },
    { "id": "em_ruin",      "type": "core:emerald_pile", "x": 23.5, "z": 6.5,  "props": { "amount": 8 } },
    { "id": "arrows_nook_e","type": "core:arrow_bundle", "x": 17.5, "z": 24.5 },
    { "id": "food_ruin",    "type": "core:food",         "x": 12.5, "z": 9.5 },

    { "id": "z_pocket_1",   "type": "core:zombie",   "x": 5.5,  "z": 25.5, "facing": "south", "tags": ["pocket"] },
    { "id": "z_pocket_2",   "type": "core:zombie",   "x": 4.5,  "z": 28.5, "facing": "south", "tags": ["pocket"] },
    { "id": "z_bank_1",     "type": "core:zombie",   "x": 21.5, "z": 36.5, "facing": "west" },
    { "id": "z_corridor_1", "type": "core:zombie",   "x": 24.5, "z": 30.5, "facing": "south" },
    { "id": "z_corridor_2", "type": "core:zombie",   "x": 26.5, "z": 27.5, "facing": "south" },
    { "id": "z_corridor_3", "type": "core:zombie",   "x": 23.5, "z": 24.5, "facing": "south" },
    { "id": "s_corridor_1", "type": "core:skeleton", "x": 25.5, "z": 22.5, "facing": "south" },
    { "id": "s_ruin_1",     "type": "core:skeleton", "x": 10.5, "z": 5.5,  "facing": "south" },
    { "id": "s_ruin_2",     "type": "core:skeleton", "x": 21.5, "z": 8.5,  "facing": "west" },
    { "id": "s_ruin_3",     "type": "core:skeleton", "x": 16.5, "z": 7.5,  "facing": "south" },

    { "id": "sm_1", "type": "core:spawn_marker", "x": 11.5, "z": 14.5 },
    { "id": "sm_2", "type": "core:spawn_marker", "x": 20.5, "z": 14.5 },
    { "id": "sm_3", "type": "core:spawn_marker", "x": 11.5, "z": 18.5 },
    { "id": "sm_4", "type": "core:spawn_marker", "x": 20.5, "z": 18.5 },
    { "id": "sm_5", "type": "core:spawn_marker", "x": 13.5, "z": 17.5 },

    { "id": "torch_gate_w", "type": "core:torch", "x": 13.5, "z": 14.5 },
    { "id": "torch_gate_e", "type": "core:torch", "x": 18.5, "z": 14.5 },
    { "id": "torch_ruin_1", "type": "core:torch", "x": 8.5,  "z": 3.5 },
    { "id": "torch_ruin_2", "type": "core:torch", "x": 24.5, "z": 3.5 },
    { "id": "torch_exit_w", "type": "core:torch", "x": 14.5, "z": 3.5 },
    { "id": "torch_exit_e", "type": "core:torch", "x": 17.5, "z": 3.5 },
    { "id": "torch_bridge", "type": "core:torch", "x": 18.5, "z": 35.5 },

    { "id": "prop_barrel_1", "type": "core:prop_barrel", "x": 8.5,  "z": 9.5 },
    { "id": "prop_barrel_2", "type": "core:prop_barrel", "x": 24.5, "z": 9.5 },
    { "id": "prop_crate_1",  "type": "core:prop_crate",  "x": 3.5,  "z": 37.5 },
    { "id": "prop_pillar_1", "type": "core:prop_pillar_broken", "x": 13.5, "z": 6.5 },
    { "id": "prop_pillar_2", "type": "core:prop_pillar_broken", "x": 19.5, "z": 6.5 }
  ],
  "triggers": [
    {
      "id": "trg_start",
      "event": { "type": "level_start" },
      "actions": [
        { "type": "objective", "id": "obj_cross", "state": "active" },
        { "type": "message", "text": "Find a way to the old ruins.", "seconds": 3 }
      ]
    },
    {
      "id": "trg_ambush",
      "event": { "type": "enter", "area": { "x": 11, "z": 14, "w": 10, "h": 5 }, "who": "player" },
      "actions": [
        { "type": "objective", "id": "obj_ambush", "state": "active" },
        { "type": "message", "text": "Ambush!", "seconds": 2 },
        { "type": "sound", "id": "core:sfx_ambush_horn" },
        { "type": "camera_shake", "strength": 0.3, "seconds": 0.4 },
        { "type": "spawn", "at": "sm_1", "entity": { "type": "core:zombie",   "tags": ["wave1"] } },
        { "type": "spawn", "at": "sm_2", "entity": { "type": "core:zombie",   "tags": ["wave1"] } },
        { "type": "delay", "seconds": 0.6 },
        { "type": "spawn", "at": "sm_3", "entity": { "type": "core:zombie",   "tags": ["wave1"] } },
        { "type": "spawn", "at": "sm_4", "entity": { "type": "core:zombie",   "tags": ["wave1"] } },
        { "type": "delay", "seconds": 0.6 },
        { "type": "spawn", "at": "sm_5", "entity": { "type": "core:skeleton", "tags": ["wave1"] } }
      ]
    },
    {
      "id": "trg_wave_cleared",
      "event": { "type": "entities_dead", "tag": "wave1", "min": 5 },
      "actions": [
        { "type": "objective", "id": "obj_ambush", "state": "complete" },
        { "type": "set_state", "entity": "gate_main", "state": "open" },
        { "type": "sound", "id": "core:sfx_gate_open" },
        { "type": "message", "text": "The gate opens.", "seconds": 2 },
        { "type": "objective", "id": "obj_exit", "state": "active" }
      ]
    },
    {
      "id": "trg_cross",
      "event": { "type": "checkpoint", "id": "cp_bridge" },
      "actions": [ { "type": "objective", "id": "obj_cross", "state": "complete" } ]
    }
  ],
  "objectives": [
    { "id": "obj_cross",  "text": "Cross the river",      "type": "flag" },
    { "id": "obj_ambush", "text": "Survive the ambush",   "type": "kill_tag", "tag": "wave1", "minSpawned": 5 },
    { "id": "obj_exit",   "text": "Reach the ruin beacon", "type": "reach", "target": "exit_main" }
  ],
  "completion": { "type": "exit", "exit": "exit_main", "requires": ["obj_ambush"] }
}
```

Notes for implementers:

- `obj_cross` is of type `flag` driven entirely by triggers so the sample exercises the trigger → objective path; `obj_ambush` demonstrates the automatic `kill_tag` evaluation (the trigger `trg_wave_cleared` also sets it complete explicitly — both paths must be idempotent).
- The gate occupies cells (15,13) and (16,13); `LVL1030` must *not* fire for this level (exit reachable with gates open) and `LVL1031` must not fire either.
- The validator must report zero errors and zero warnings for this level — it is the golden fixture used by `LevelLoaderTests`.

### 12.4 Expected playthrough (used by the scripted acceptance test, §20.6)

| Step | Expectation |
|---|---|
| Spawn | Hero at (4.5, 36.5) facing north; HUD shows 100 HP, 3 lives, 30 arrows, potion ready, objective "Cross the river". |
| Walk east along row 34/35 to the bridge | Water blocks diagonal shortcuts; hero crosses planks; `cp_bridge` activates at ~ (19.5, 34.5); objective completes; "Survive the ambush" is not yet visible. |
| Bank zombie `z_bank_1` | Aggroes at ≤ 7 m, walks to hero, telegraphs 0.45 s before swinging. A full combo kills it in most runs (12+12+18 = 42 vs 40 HP; only low variance on all three hits, e.g. 11+11+17 = 39, needs a 4th hit) — the test asserts ≤ 4 hits. |
| Corridor north | Zombies engage one at a time; skeleton at (25.5, 22.5) shoots when hero within 10 m and LOS; arrows blocked by trees. |
| Enter clearing from row 19 | `cp_clearing` activates; stepping into rows 14–18 fires `trg_ambush`; five monsters emerge over 1.2 s; gate closed (solid). |
| Kill wave | On fifth death `trg_wave_cleared` fires within the same tick; gate opens over 0.75 s; objective "Reach the ruin beacon". |
| Stairs | Hero ascends from height 0 to 1 between rows 12 and 11; camera remains stable; cliff faces render around the plateau. |
| Ruin | Three skeletons; arrows from the plateau can hit a hero still on the stairs only when height rule allows (|Δh| ≤ 1 with LOS). |
| Exit | Entering radius 1.0 of (16.0, 4.0) ⇒ `MissionComplete`; results screen with time, emeralds, kills, deaths. |

---

## 13. Rendering and visual style

### 13.1 Style guide (normative)

| Element | Rule |
|---|---|
| Geometry | Everything is built from axis-aligned cuboids. No cylinders, spheres, bevels, or smooth normals. Characters: head 8×8×8 px, body 8×12×4 px, arms/legs 4×12×4 px at 16 px per block (hero may use 32 px per block for the face). |
| Textures | 16 px per block for terrain and props; characters 64×64 px sheets; nearest-neighbour sampling always. No texture may be bilinear-filtered. Mipmaps: **on** for the terrain atlas and props (`Nearest Mipmap Anisotropic`) because oblique cliff/side faces and the 0.6 mobile resolution scale can minify texels and shimmer; **off** for characters, pickups and UI (`Nearest`), which are always magnified at the fixed camera distance (≈ 7 screen px per texel at 1080p) and look crisper without them. |
| Palette | Saturated mid-tones, dark cool shadows, warm highlights. Master palette file `tools/palette.gpl` (48 colours) is the only allowed source for new textures. Grass `#7bb04a`, dirt `#8a6a3f`, stone `#8c8c8c`/`#6f6f6f`, oak leaves `#3f7a2f`, water `#2f6f8f`, torch flame `#ffb347`, emerald `#3ae374`, blood/hit `#ff5a3c`. |
| Lighting | One sun directional light with shadows; cool ambient; warm omni lights for torches/beacons; emissive textures glow via bloom. Per-vertex baked ambient occlusion in corners of terrain blocks (Minecraft-style smooth lighting) for cheap depth cues. |
| Camera | §13.4. Never rotates; never zooms in v1. |
| Animation | Rigid part rotation only (no skinning deformation). Walk cycle 0.5 s at 4.5 m/s, scaled by speed. Attack poses exaggerated (arm swings 150°+). Hit reaction: 0.1 s white flash + 0.1 s squash (scale 1.1, 0.9, 1.1). Death: fall over + 0.5 s sink into ground + dust puff. |
| Particles | Square sprites (4–8 px pixel squares in world units 0.1–0.25 m), unlit or emissive, additive for sparks, alpha for dust/smoke. Max 2000 live particles. |
| Water | Flat quad at `h − 0.15` over a riverbed at `h − 1`, scrolling 16 px texture, alpha 0.7, vertex "breathing" ± 0.03 m, foam strip (lighter texture) on edges adjacent to land. |
| Foliage | Tall grass/flowers as two crossed quads (`X` billboard), unlit × ambient, gentle sway in vertex shader (presentation-only). |
| Post-processing | ACES tonemap, glow (intensity 0.6, threshold 1.0, soft-light blend), vignette 0.25, distance fog. No depth of field, no motion blur, no SSAO (not available in Mobile renderer; AO is baked in vertex colours). |
| UI | Pixel font (open licence, e.g. a 7×9 or 8×8 bitmap font), 9-slice dark panels (`#1a1a1f` fill, `#3c3c46` border, gold accent `#e2b644`), no gradients, icons 16×16 or 32×32 px. UI scaled by integer factors where possible. |

A visual acceptance review (§20.8) compares screenshots against this table.

### 13.2 Godot renderer configuration

- `rendering/renderer/rendering_method = "mobile"` on **all** platforms (identical look, identical feature set). `rendering_method.mobile = "mobile"`. Compatibility (GLES3) fallback preset for Android devices without Vulkan: shadows off, glow off, fog on (project settings override `.android_low` feature tag).
- MSAA 2× on desktop, off on mobile; FXAA off (pixel art). `rendering/textures/default_filters/texture_filter = Nearest with mipmaps` — but per-material setting is authoritative; a startup check (debug build) iterates all materials and fails if any is linear-filtered.
- Shadow atlas: desktop 4096, mobile 2048; directional shadow mode *Orthogonal* (single cascade suits a fixed camera), `shadow max distance 45`, `shadow bias 0.03`, `normal bias 1.0`.
- Max 8 omni lights influencing any mesh (Mobile renderer limit). Presentation enables only the 8 nearest torch lights to the camera focus; the others keep their emissive flame mesh but no light (`LightCuller` node, updates every 10 frames).
- Color space linear; `WorldEnvironment` per level built from `environment` block (sky gradient via `ProceduralSkyMaterial` colors, fog depth mode, glow).
- Viewport: 3D rendered at native resolution on desktop; on mobile `scaling_3d_scale` 0.75–1.0 adaptively (`AdaptiveResolution` node: if last 60 frames average > 18 ms, step down by 0.05 to min 0.6; step up after 300 good frames). UI always native.

### 13.3 Terrain rendering

- Level is split into **chunks of 16×16 cells**. For each chunk, `ChunkMesher` (pure C#, lives in `Dungeons.Game` but depends only on `LevelDocument`/`ContentRegistry`, so the editor reuses it) produces one `ArrayMesh` with a single surface and the terrain atlas material:
  - Top face at `y = h` for every block-rendered tile (the walking surface; the block under it spans `[h−1, h]`), with the tile's `top` atlas rect. This matches §6: feet at `y = h`.
  - Side faces where a neighbour's *effective terrain height* is lower (cliff faces), textured with `side`, repeated per block of drop; the bottom block of a cliff uses `side` darkened 20 % via vertex colour. Effective terrain height is `h` for land, `h − 1` for water cells (riverbed), `−1` outside the map.
  - Vertex colour = `tint × AO`, AO per corner from the count of higher neighbours (0 → 1.0, 1 → 0.8, 2 → 0.65, 3 → 0.5).
  - Water cells emit a riverbed top at `h − 1` (dark sand texture) and a separate water surface quad at `h − 0.15` per chunk (second `MeshInstance3D`, water shader). Banks therefore show a one-block side face into the water.
  - Stairs tiles emit a stepped mesh (4 steps) rising from `h` to `h+1` toward `dir`.
  - Bridge planks: thin box at `h − 0.1 .. h` spanning the water cell (walking surface flush with the banks).
  - Level boundary: nothing is rendered outside the map; because the outside counts as height `−1`, edge cells show a side face down to `y = −1` (prevents a visibly hollow map edge). Levels are expected to border with trees/walls/void anyway; the debug overlay draws the boundary rectangle.
- Model-rendered tiles (trees, rocks, walls) and props use one `MultiMeshInstance3D` per (chunk, model variant). Random rotation and variant are derived from `hash(cx, cz, levelId)` so they are identical across loads and platforms.
- Chunk meshing runs on a worker thread (`System.Threading.Tasks`), producing arrays; `ArrayMesh` creation/commit happens on the main thread, maximum 4 chunks committed per frame to avoid hitching. Load screen stays until all chunks are committed.
- Texture atlas: `textures/terrain_atlas.png` 1024×1024 with 16×16 tiles on a 2 px gutter (bleed-padded) grid; atlas rects are generated by `tools/atlas_pack.py` into `content/atlas.json`.

### 13.4 Camera

```
focus      = lerp(prevFocus, heroRenderPos + heroFacing * lookAhead, 1 - exp(-8 * frameDelta))
cameraPos  = focus + dir(yaw, pitch) * distance             // yaw 45°, pitch -50°, distance 22 from level.environment.camera
camera.look_at(focus); fov = 25° vertical; near 1; far 120
```

- Camera shake: additive offset from a decaying noise (`strength * decay(t)`, max 0.25 m), triggered by events (`camera_shake` action, heavy hit on hero, gate open) and disabled by the accessibility setting.
- Focus clamp: optional `environment.camera.bounds` (default: level rect inset by 4 m) prevents showing beyond the map edge when levels do not border with trees.
- Occluder handling: tall tiles/props (trees, walls, pillars) in cells between the camera and the hero (2D DDA from the hero cell along the camera's ground-projected direction, 6 cells) receive `dither_alpha = 0.35` through a per-instance custom data channel (MultiMesh `custom_data`); the shader dithers them out with a screen-door pattern (no sorting issues). Transition 0.15 s.
- Screen → world for mouse aim: `Camera3D.ProjectRayOrigin/ProjectRayNormal`, intersect with the plane `y = heroY + 0.9` (chest height), convert to `Vec2`.

### 13.5 Entities

- `EntityView` nodes are pooled per `EntityDef` (prewarm 16 zombies, 8 skeletons, 64 arrows, 64 emeralds). They are attached when `EntitySpawned` events arrive and detached on `EntityRemoved`.
- Transform each frame: `pos = lerp(PrevPos, Pos, alpha)`, `y = lerp(PrevY, Y, alpha)`; facing rotates toward `Facing` at ≤ 720°/s (purely visual; simulation facing is instant).
- Animation selection by state (from `Combat`/`Ai` snapshot): `idle, walk (speed-scaled), attack_windup, attack_active, attack_recovery, roll, hit, stagger, death, emerge`. Driven by `AnimationTree` state machine with transitions ≤ 0.08 s.
- Hit flash: material shader uniform `flash` set to 1 on `DamageApplied`, decays to 0 over 6 frames (presentation timer).
- Shadow blob: a dark 0.6 m quad under every character in addition to real shadows (reads well at distance; matches reference).
- Hit-stop (presentation only): on `DamageApplied` from a hero melee hit, the attacker's and victim's `AnimationTree` time scale is set to 0 for 40 ms (swing 3: 70 ms), then restored. The simulation is never paused — this is a view-side effect, so determinism and multiplayer readiness are unaffected. Disabled by the accessibility "reduce motion" setting together with screen shake.
- Health bars: `CanvasLayer` controls positioned via `Camera3D.UnprojectPosition(headPos)`. Shown for enemies with `HP < Max`, hide 3 s after last damage. Width 48 px (×UI scale), red fill, dark border; boss style not needed.
- Damage numbers: pooled `Label` controls, spawn at head position with random horizontal jitter ±12 px, rise 40 px over 0.7 s, fade last 0.3 s; colour white for hero-dealt, red for hero-received, yellow for critical (not in v1). Font size 16 px base.
- Alert indicator: `!` sprite above an enemy for 0.3 s on `EnemyAlerted`.
- Arrows: stretched box 0.6 m, oriented along velocity; stuck arrows on terrain hit remain 2 s as decoration (pooled, no simulation).
- Pickups: emerald spins 1 turn/s and bobs ±0.05 m; collected ⇒ sparkle particles + fly-to-HUD counter (UI tween).
- Gate: two door leaves rotating 90° over 0.75 s on `GateStateChanged`.
- Chest: lid rotates −110° over 0.4 s; loot burst particles.
- Exit beacon: 2×2 stone base, central emissive column (bloom), vertical particle stream; "locked" state dims the column and shows a chain icon in HUD when the hero approaches.

### 13.6 Shaders (all `gdshader`, Mobile-compatible)

| Shader | Purpose | Uniforms |
|---|---|---|
| `terrain.gdshader` | Atlas sampling, vertex colour AO, per-level tint | `albedo_atlas`, `tint` |
| `character.gdshader` | Standard lit + `flash` (lerp to white) + `dissolve` for death sink | `flash`, `dissolve` |
| `water.gdshader` | UV scroll, alpha, foam mask from vertex colour, slight vertex wave | `color`, `speed`, `foam_tex` |
| `foliage.gdshader` | Crossed quads with wind sway, alpha scissor (no sorting) | `sway_strength` |
| `dither_fade.gdshader` | Screen-door transparency for occluders using `INSTANCE_CUSTOM.a` | — |
| `emissive.gdshader` | Flames, beacon, emerald sparkle (HDR emission > 1 for bloom) | `emission_strength` |

Rule: no `discard` except alpha scissor in foliage/dither; no transparent sorting dependence between large objects (only water and particles are alpha blended).

### 13.7 Asset production pipeline

1. Model in Blockbench (generic model format, pixel-per-block 16). Export glTF with animations; naming `name.glb`, animations named exactly as in the entity def.
2. Textures exported as PNG, indexed to the master palette; atlas packer run in CI (`tools/atlas_pack.py --check` fails CI if atlas is stale).
3. Godot import presets committed (`.import` files): textures nearest (mipmaps per §13.1), no compression on desktop (lossless), ETC2/ASTC on mobile via Godot's per-platform import (`rendering/textures/vram_compression/import_etc2_astc = true`).
5. Required animation clips are enforced: `tools/check_clips.py` reads every entity def's `render.animations` map, opens the referenced `.glb` and fails CI if a clip name is missing or has zero length. A missing clip is otherwise a silent T-pose on device.
4. Each model has a documented pivot: characters at feet centre; tiles at cell centre on the floor plane; props at base centre.

---

## 14. Audio

- Format: OGG Vorbis 44.1 kHz; SFX mono (positional sources must be mono to spatialise), music stereo with loop points set in the Godot import (`loop_offset`) so tracks loop seamlessly without a hard cut; music streamed, SFX preloaded.
- Buses: `Master → Music`, `Master → SFX`, `Master → UI`, `Master → Ambience`. Settings expose each volume (0–100).
- Positional SFX via `AudioStreamPlayer3D` with `unit size 8`, max distance 30; UI and music non-positional.
- Sound ids resolve through `content/audio.json` (`"core:sfx_sword_1": { "files": ["audio/sfx/sword_1a.ogg","audio/sfx/sword_1b.ogg"], "volumeDb": -3, "pitchRange": [0.95, 1.05], "maxInstances": 4 }`); variants chosen by presentation RNG; `maxInstances` prevents stacking (oldest instance stolen).
- Required v1 sound list: sword ×3 swings, sword hit flesh, bow draw, bow release, arrow hit wood/flesh, roll, potion, hero hurt ×2, hero death, footsteps (soft/stone/wood), zombie alert/attack/hit/death, skeleton alert/draw/hit/death (rattle), emerald pickup, food pickup, arrows pickup, chest open, gate open, checkpoint chime, ambush horn, mission complete fanfare, mission failed sting, UI click/hover/back, torch loop (positional, quiet), forest night ambience loop, one 2–3 minute music loop.
- Music ducks −6 dB during the death screen and results; crossfade 1 s between tracks.
- Mobile: audio session configured for mixing with other apps (Godot default on iOS is fine), pause audio when app is backgrounded.

---

## 15. Input

### 15.1 Devices

| Platform | Primary | Secondary |
|---|---|---|
| Windows | Keyboard + mouse | Gamepad (XInput/DirectInput via Godot) |
| Android | Touch | Gamepad (Bluetooth) |
| iOS | Touch | MFi gamepad |

All three schemes produce the same `PlayerInput` per tick (§4.4). Scheme switches automatically on the last used device; HUD glyphs update accordingly.

### 15.2 Default bindings

| Action | Keyboard/mouse | Gamepad | Touch |
|---|---|---|---|
| Move | WASD / arrows | Left stick | Left virtual joystick (floating origin in left 45 % of screen) |
| Aim | Mouse position | Right stick (when deflected > 0.3) | Auto-aim (§15.6) |
| Attack | Left mouse / `J` | X (west button) | Attack button (largest, bottom-right) |
| Ranged | Right mouse / `K` | RT / RB | Bow button |
| Roll | Space / Shift | A (south) / B (east) | Roll button |
| Potion | `Q` | Y (north) | Potion button (shows cooldown radial) |
| Interact (reserved) | `E` | D-pad down | Context button (hidden in v1) |
| Pause | Esc | Start | Pause icon top-right; Android back button |
| Debug overlay (debug builds) | F3 | — | 4-finger tap |

Bindings are stored in `settings.json` via Godot `InputMap` action names (`move_up`, `attack`, …). Rebinding UI for keyboard and gamepad is in Settings.

### 15.3 Sampling and latching

`InputAdapter` runs in `_Process` (every frame) and accumulates: `Pressed` flags are OR-ed from `Input.IsActionJustPressed`/touch down events across all frames since the last tick; `Held` is the state at tick time; `Move`/`Aim` are the latest values. `ConsumeTickInput()` returns the struct and clears `Pressed`. This guarantees that a 1-frame tap at 144 Hz is never lost by the 60 Hz simulation.

### 15.4 Screen-relative movement

Stick/WASD vector `(sx, sy)` (screen right/up) is rotated by the camera yaw into world axes:

```
forward = normalize(XZ(cameraFocus - cameraPos))   // on ground plane
right   = (-forward.Z, forward.X)                   // perpendicular, pointing screen-right
Move    = right * sx + forward * sy;  clamp length ≤ 1
```

Test: with yaw 45°, pressing "up" moves the hero toward world north-east on screen top; verified by `AxisSanityTest`.

### 15.5 Touch layout

- Landscape only. Virtual joystick radius 60 dp with 20 dp dead zone, origin where the finger lands, follows if dragged beyond 1.5× radius.
- Buttons: Attack 84 dp, Roll 64 dp, Bow 64 dp, Potion 56 dp arranged in an arc at bottom-right; 12 dp spacing; all respect safe-area insets. Buttons support multi-touch (joystick + button simultaneously) and slide-off cancel.
- Tapping the world does nothing (no click-to-move). Opacity 0.6 idle, 0.9 pressed.
- Layout scale and button positions are configurable in Settings (left-handed mirror option).
- **Bow drag-to-aim**: a *tap* on the Bow button fires with auto-aim (§15.6). A *hold* (> 120 ms) or a drag beyond 16 dp turns the button into a mini-stick: an aim arrow is drawn from the hero in the dragged direction (rotated by camera yaw as in §15.4), `Aim` is set and `AimValid = true`, and the arrow is fired on release (`Pressed.Ranged` latched on release). Dragging back inside the button's dead zone (12 dp) and releasing cancels. The hero keeps walking with the joystick during the drag; this is the only manual-aim path on touch and the reason skeletons behind corners are fair on phones.
- Melee is tap-only; drag-aiming a sword swing is slower than auto-aim in practice and was rejected.

### 15.6 Auto-aim (all schemes; mandatory on touch)

When `AimValid == false` for an attack/ranged action:

1. Candidates: alive hostile entities within `AUTO_AIM_RANGE` (melee 2.5 m, ranged 12 m) with LOS.
2. Score = `angleDeg(Facing, toTarget) * 1.0 + distance * 8` (lower is better); candidates with angle > 100° are excluded unless no other candidate exists.
3. Chosen target direction becomes `Facing` for the action. Ranged shots additionally lead the target: aim at `target.Pos + target.Vel * (dist / arrowSpeed)`.
4. With mouse, auto-aim is only used if the cursor is within 1.2 m of an enemy (snap), otherwise raw aim. With right stick, no auto-aim unless the stick is centred.

Auto-aim runs in Core (`AutoAim.Resolve(world, hero, mode)`), so it is deterministic and testable.

### 15.7 Robustness

- Losing window focus / app pause ⇒ all `Held` cleared, pause menu opened.
- Gamepad disconnect ⇒ pause.
- Mouse cursor hidden only in fullscreen while aiming is active; shown on pause.

---

## 16. UI / UX

### 16.1 Screen flow

```
Boot (logo ≤ 1.5 s, async content/catalog load) → Main Menu
Main Menu: Play (→ Level Select), Settings, Credits, Quit (desktop only)
Level Select: cards grid → Level Details (preview, description, record, diagnostics if invalid) → Play
In-game: HUD; Pause (Resume, Restart, Settings, Quit to Level Select)
Death (lives left): "You died — respawning…" 2 s overlay (no input)
Mission Failed: Retry (same seed), Quit to Level Select
Mission Complete: stats (time, emeralds, kills, deaths, XP, hero level) → Continue
```

### 16.2 HUD layout (reference resolution 1920×1080, scaled by shorter side)

- Top-left: current objective text (max 2 lines, 24 px) with a small diamond bullet; secondary objectives below in smaller grey text.
- Top-right: emerald counter (icon + number), lives as three small hero heads, pause button (touch).
- Bottom-centre: health bar (400×28 px, red, white border, numbers `73/100` centred), left of it the potion slot (64 px, radial cooldown sweep, greyed when cooling, number of seconds), right of it the bow slot with arrow count. Low HP (< 25 %) pulses the bar border red and adds a 0.3-opacity red vignette.
- Centre-top (transient): trigger messages (`message` action) in a dark pill, fade in/out 0.2 s.
- Centre: "LOCKED — defeat the ambush" hint when approaching a locked exit; checkpoint "Checkpoint reached" toast.
- Debug overlay (F3): FPS, frame ms, tick ms, entity count, draw calls, visible lights, chunk count, seed, hero cell/height, AI states; toggles for nav-grid, collision circles, trigger areas, path lines.

### 16.3 Level select

Card: preview (320×180), name, author, difficulty pips, best time, "Completed" ribbon, "User level" badge, invalid overlay with ⚠ and first diagnostic on hover/long-press ("Show all" opens a scrollable list with codes). Buttons: Play, Refresh (rescans user folder), Open Levels Folder (desktop), Import… (file picker where available; §18). Keyboard/gamepad navigable; touch scrollable.

### 16.4 Settings

Video: quality preset **Low / Medium / High / Custom** (table below), then the individual fields the presets drive: fullscreen/windowed (desktop), vsync, resolution scale, shadows (off/low/high), glow, MSAA (desktop), UI scale (80–150 %). Changing an individual field switches the preset to Custom. Audio: four volumes. Controls: rebinding, touch layout editor, left-handed mode, auto-aim strength (off/normal/strong — "off" still applies to touch), vibration (mobile/gamepad). Accessibility: damage numbers on/off, high-contrast health bars (adds pattern), text size, colour-blind palette for health bars, reduce motion (disables screen shake and hit-stop).

| Preset | Renderer | Shadows | Glow | Resolution scale | Omni lights | Notes |
|---|---|---|---|---|---|---|
| Low | Compatibility (GLES3/ANGLE) or Mobile if Vulkan present | off | off | 0.75 | 4 | Default for blocklisted/low-end devices (§18.2) |
| Medium | Mobile | low (1024 atlas) | on | 1.0 (adaptive down to 0.6) | 8 | Default on phones |
| High | Mobile | high (4096 atlas, 2× MSAA) | on | 1.0 | 8 | Default on desktop |

The initial preset is chosen once at first launch from the platform and the device blocklist, then persisted.

### 16.5 Localization

All strings through `tr()` keys in `game/i18n/*.csv` (`en` shipped; keys stable). Level texts may be plain strings or `{ "en": ..., "xx": ... }`; fallback to `en` then to the first available.

---

## 17. Persistence

| File | Location | Contents |
|---|---|---|
| `settings.json` | `user://` | video/audio/controls/accessibility, schema version |
| `profile.json` | `user://` | `runCounter`, total emeralds, total XP, per-level records `{ levelId: { completed, bestTimeTicks, bestEmeralds, attempts, lastSeed } }` |
| `levels/` | `user://levels/` | user level packages |
| `logs/` | `user://logs/` | rolling log files, 5 × 1 MB |
| `replays/` | `user://replays/` | last 5 runs (`.dreplay`), auto-saved on mission end (debug and release) for bug reports |

- Writes are atomic: write `file.tmp`, flush, rename over the original. Corrupt JSON ⇒ backup to `*.corrupt-<timestamp>` and recreate defaults; never crash.
- `user://` resolves to the platform's app data directory (Godot handles this: `%APPDATA%\Godot\app_userdata\<name>` or a custom dir via `application/config/use_custom_user_dir`, Android app-specific storage, iOS app container). Set `use_custom_user_dir = true`, `custom_user_dir_name = "BlockDungeons"` on desktop.
- Save in-run progress is **not** supported (missions are short); quitting mid-mission counts as an attempt.

---

## 18. Platform specifics

### 18.1 Windows

- x86_64, Godot Windows Desktop export, embedded PCK, icon, version info. Vulkan primary; add `--rendering-driver d3d12` fallback launcher instruction in README if Godot version supports D3D12.
- Window: default 1600×900 windowed, remembers size/position; borderless fullscreen toggle (Alt+Enter).
- Command line: `--level <id|path>`, `--seed <n>`, `--replay <file>`, `--headless-validate <path>` (exits with code), `--screenshot-test <scenario>` (debug).
- User levels: `%APPDATA%\BlockDungeons\levels\`; "Open Levels Folder" button uses `OS.ShellOpen`.

### 18.2 Android

- `minSdk 26`, `targetSdk` as required by current Play policy, `arm64-v8a` only in release; APK for testing, AAB for store.
- Orientation landscape (sensor landscape), immersive mode, `keep screen on` during gameplay.
- Back button: in-game ⇒ pause; menus ⇒ back; main menu ⇒ confirm quit.
- Lifecycle: `NOTIFICATION_APPLICATION_PAUSED` ⇒ pause sim, mute audio, flush settings; `RESUMED` ⇒ resume at pause menu.
- Permissions: no runtime permission prompts required; declare the normal `VIBRATE` permission for optional haptics. User levels: `user://levels/` (app-specific storage). Import via Godot native file dialog if available in the frozen Godot version; otherwise via "Open with" intent filter for `.dlvl` (`application/octet-stream` + path pattern) copying the file into `user://levels/`. Document the chosen path in README after M0.
- Low-end detection and device blocklist: `content/device_profiles.json` maps GPU/SoC name patterns (`RenderingServer.GetVideoAdapterName()`, `OS.GetModelName()`) to a preset and optional renderer override (e.g. "force Compatibility" for adapters with known Vulkan driver defects). Unknown adapter with Vulkan ⇒ Medium; no Vulkan ⇒ Low. The file is data so it can be updated in a patch without code changes; the debug overlay shows which rule matched.
- Vibration: 30 ms on hero hit, 60 ms on death (setting).

### 18.3 iOS

- Deployment target iOS 14, arm64, Metal. Godot exports an Xcode project; CI on macOS runs `xcodebuild` with automatic signing from secrets.
- Landscape only; safe-area insets from `DisplayServer.GetDisplaySafeArea()` applied to HUD margins and touch controls.
- Lifecycle as Android (`APPLICATION_PAUSED/RESUMED`). Audio session category ambient (no interruption of user music unless game music is enabled).
- User levels: `Info.plist` keys `UIFileSharingEnabled = true` and `LSSupportsOpeningDocumentsInPlace = true` so the Files app can place `.dlvl` files into the app's Documents folder; `user://levels/` must resolve inside Documents — verify at M0 and, if Godot maps `user://` elsewhere, use `OS.GetSystemDir`/an explicit Documents path for the levels folder (abstracted behind `IPlatformPaths.UserLevelsDir`).
- Haptics via `Input.VibrateHandheld` (setting).

### 18.4 Common

- Frame pacing: vsync on; 60 Hz target; on 120 Hz devices simulation remains 60 Hz, render at 120 with interpolation (verified visually smooth).
- Thermal: after 10 min, if average frame time rises > 20 % on mobile, `AdaptiveResolution` steps down earlier (lower threshold to 16 ms).
- Crash/bug reports: `--replay` + logs + seed are enough to reproduce simulation issues on any desktop.

---

## 19. Performance budgets

Reference devices (acceptance): Windows laptop with integrated GPU (Intel Iris Xe class), Android mid-range 2021 (Snapdragon 7-series class, 6 GB), iPhone 11 or newer. Numbers are per frame at 1080p (desktop) / native (mobile, with adaptive scale ≥ 0.75).

| Metric | Budget |
|---|---|
| Frame time | ≤ 16.6 ms p95 on reference devices; ≤ 33 ms p99 |
| Simulation tick (64 monsters, 32 projectiles, 128 pickups) | ≤ 2.0 ms desktop, ≤ 3.5 ms mobile |
| Pathfinding share of tick | ≤ 1.0 ms |
| Steady-state managed allocations per tick | 0 bytes (debug counter asserts) |
| Draw calls | ≤ 150 mobile, ≤ 300 desktop (level 32×40 ≈ 6 chunks × (terrain + water) + multimesh batches + entities + UI) |
| Triangles | ≤ 300 k visible |
| Live particles | ≤ 2000 |
| Omni lights with shadows | 0 (torches have no shadows) |
| Texture memory | ≤ 96 MB mobile |
| Level load (32×40) | ≤ 1.0 s to playable; 256×256 ≤ 4 s mobile with progress bar |
| App size | ≤ 150 MB Android/iOS |
| Memory | ≤ 600 MB RSS mobile |
| Cold start to main menu | ≤ 3 s |

Verification methods are in §20.7.

---

## 20. Testing strategy

Testing is layered so that 90 % of gameplay bugs are caught on a CI machine without a GPU or a device.

### 20.1 Test pyramid

| Layer | Tooling | Runs | Covers |
|---|---|---|---|
| Unit (Core) | xUnit, `dotnet test` | every push, < 60 s | math, RNG, collision, A*, combat formulas, state machines, loader, validator, migrations, triggers, objectives, hashing |
| Scenario (Core) | xUnit + `ScenarioBuilder` | every push | multi-system behaviour in tiny code-built levels with scripted inputs |
| Replay / golden (Core) | xUnit + `.dreplay` fixtures | every push | determinism and regression of whole runs, incl. Mossy Hollow full playthrough |
| Fuzz (Core) | xUnit theory with generated inputs; optional SharpFuzz job nightly | nightly | loader never throws, validator never crashes, sim never NaNs |
| Benchmarks (Core) | BenchmarkDotNet | nightly + on demand | tick time, pathfinding, chunk meshing (pure parts), load time |
| Integration (Godot) | GdUnit4 C# tests, Godot headless with `--headless` where rendering is not required, otherwise Xvfb/desktop runner | every push (headless), nightly (rendered) | scene wiring, input adapter, HUD binding, level catalogue I/O, event → view mapping |
| Visual regression | Godot `--screenshot-test` scenarios, image diff (per-channel tolerance 2 %, mismatch ≤ 0.5 % pixels) | nightly, on art/shader PRs | style compliance, lighting/camera regressions |
| Device QA | Manual checklist on reference devices + Firebase Test Lab/physical lab smoke | per milestone / release candidate | touch, lifecycle, performance, store compliance |

### 20.2 Unit tests — required list (minimum)

`Math`: Vec2 ops; `Rng` reproducibility (known sequence for seed 1 — store first 8 outputs); `Fnv1a`.

`Collision`: circle vs single solid cell from 8 directions (position clamped, velocity zeroed on that axis); sliding along a wall preserves tangential speed; corner case (two cells) no tunnelling at `MAX_SPEED`; height step blocking (h0 → h1 without stairs is a wall); stairs passability in correct direction only; diagonal corner-cutting prevented.

`NavGrid/AStar`: straight path length; path around an obstacle equals known optimum; no diagonal through corners; unreachable goal returns closest node; `MoveCost` preference; performance smoke (256×256 open grid ≤ 2 ms).

`Combat`: damage formula bounds and rounding (`base 12, armor 0 ⇒ 11..13`; `armor 100 ⇒ half`); arc hit test (inside/outside angle, reach minus radius, height mismatch); combo chaining and buffer window; roll i-frames exact ticks; invulnerability after hit; knockback decay and wall clamp; potion cooldown; arrows ammo and pickup caps.

`Projectiles`: blocked by `BlocksProjectiles` cell; passes over water; blocked by higher cell; hits hostile not friendly; lifetime removal; sub-stepping catches thin walls at max speed.

`AI`: zombie state transitions with a fake target (idle → alert → chase → attack → recovery → chase; loses target → return); skeleton keeps range and retreats; perception staggering; alert propagation hop; monster cap and deferred spawns.

`Pickups`: magnet speed/radius; food not collected at full HP; emerald burst count and scatter determinism.

`Lives/Checkpoints`: death → respawn at highest-order activated checkpoint; enemies pushed out 3 m; last life ⇒ Failed.

`Triggers/Objectives`: each event type fires once with `once`; `delay` suspends only its own cursor; conditions; `entities_dead` with `min`; `kill_tag` objective idempotency; completion requires objectives; exit locked state.

`Level`: schema acceptance of Mossy Hollow; every `LVL1xxx` rule has a negative fixture that triggers exactly that code; unknown property warning; migration fixtures; `.dlvl` zip round trip; catalogue precedence (user over built-in, higher version); invariant culture parsing (`"x": 24.5` parsed under `de-DE` thread culture).

`Hash`: identical runs identical hashes; a one-tick input change changes the hash.

### 20.3 Scenario tests

`ScenarioBuilder` creates a `LevelDocument` from a tiny ASCII string, places entities, and provides `Run(ticks, Func<int, PlayerInput>)`, `Advance(seconds)`, `Press(button)`, `MoveToward(Vec2, maxSeconds)` (uses the same A* to drive the hero, so scenario tests don't hand-author paths). Required scenarios:

| Id | Scenario | Assertion |
|---|---|---|
| SC-MOV-01 | Open 10×10, hold east 60 ticks | `Pos.X` increased by `4.5 ± 0.01` |
| SC-MOV-02 | Wall to the east | Hero stops at `wall.X − 0.35`, `Vel.X == 0`, Z movement unaffected |
| SC-MOV-03 | Stairs 0→1 then back | `Y` rises monotonically to 1.0 across the stairs cell; cannot enter the h1 cell from the side |
| SC-ROLL-01 | Roll east | Travels 3.0 ± 0.05 m in 24 ticks; damage during ticks 3–20 ignored; cooldown respected |
| SC-CMB-01 | Zombie at 1.2 m | Swing 1 hits once (damage in 11–13), swing 3 applies knockback ≥ 2.0 ⇒ stagger |
| SC-CMB-02 | Zombie behind hero | No hit |
| SC-RNG-01 | Skeleton 6 m away, tree between | Skeleton repositions; no arrow spawned until LOS |
| SC-RNG-02 | Hero shoots across water | Arrow crosses water and hits |
| SC-AI-01 | Zombie 6.9 m, no LOS obstacles | Alert within 6 ticks of perception window; reaches attack range in ≤ 3 s |
| SC-AI-02 | Hero teleported 25 m away | Zombie returns to spawn and idles |
| SC-LIFE-01 | Hero takes 100 damage with checkpoint active | Respawn at checkpoint, lives 2, invulnerable 60 ticks |
| SC-TRG-01 | Enter area trigger spawning 2 tagged zombies; kill both | `entities_dead` fires exactly once, gate opens, cells become passable |
| SC-OBJ-01 | Exit entered with requirement incomplete | No completion; event `ExitLocked` |
| SC-PICK-01 | Emerald at 2.4 m | Magnetised and collected within 1 s; counter +amount |
| SC-CAP-01 | Spawn 70 monsters | 64 live; 6 deferred; spawn as deaths occur |
| SC-SPW-01 | `core:spawner` count 6, interval 0.5 s, maxAlive 2, activation proximity 4 m | Nothing spawns until hero within 4 m; never more than 2 of its spawns alive; 6 total; `SpawnerFinished` once; `kill_tag` objective cannot complete before the 6th spawn |
| SC-SPW-02 | Gate with `openWhen: obj_a`; complete `obj_a` via `set_flag` | Gate opens via the desugared trigger; identical hash to the explicit-trigger version of the same level |
| SC-SURV-01 | `survive` 5 s objective; hero dies at 3 s, respawns | Timer restarts; completes 5 s after respawn |
| SC-ALLOC-01 | Mossy Hollow, 600 ticks of recorded play | `GC.GetAllocatedBytesForCurrentThread()` delta == 0 after warm-up |

### 20.4 Replay / golden tests

- Format `.dreplay` v1 (JSON): `{ formatVersion:1, levelId, levelHash (SHA-256 of exact level.json bytes), contentHash, seed, tickCount, initialHash, inputs: [ { t, duration, move:[x,z], aim:[x,z], aimValid, pressed, held } ], finalHash, checkpoints: [{t, hash}] every 60 ticks }`. Hashes are fixed-width hexadecimal strings. Runs use zero-based `t`; `tickCount` makes the last run's end explicit (the earlier sketch omitted this necessary boundary). `duration` may be inferred from the next run / `tickCount`. Internal co-op captures use `players:[{move,aim,aimValid,pressed,held},…]` with at most 4 slots. Optional `tickHashes` preserves every-tick diagnostics; without it verification reports the first differing checkpoint/final tick. Recorded float components are preserved exactly after validation, rather than normalized a second time.
- Golden replays committed: `mossy_hollow_full_clear.dreplay` (complete run, 3–5 min), `mossy_hollow_death_respawn.dreplay`, `mossy_hollow_ambush_only.dreplay`. Tests replay and compare hashes at every checkpoint; a mismatch prints the first diverging tick and a diff of entity states.
- Changing gameplay constants intentionally requires re-recording goldens via `dungeons-tool replay --rerecord` and a reviewer explicitly approving the new hash in the PR.
- Allocation test (SC-ALLOC-01) and benchmark reuse the full-clear replay.

### 20.5 Fuzz tests

- Loader: mutate Mossy Hollow JSON (delete keys, change types, truncate, random unicode, huge numbers, NaN strings, 10 000-row layers, zip bombs ≤ 64 MB limit) — `Load` must return a result with diagnostics, never throw, in < 2 s.
- Simulation: random inputs for 10 000 ticks on Mossy Hollow and on random code-generated maps — no NaN/Infinity positions, no entity outside bounds, no entity inside a solid cell by more than 1e-3, no negative HP alive entities, hash stable across two runs.

### 20.6 Integration tests (Godot, GdUnit4)

| Test | Checks |
|---|---|
| `AxisSanityTest` | Spawn marker entity at cell (0,0) and (w−1,h−1); unproject to screen; the first is upper-left of the second with yaw 45° camera. Press "up" ⇒ hero screen position moves up. |
| `LevelCatalogTest` | Built-in index lists Mossy Hollow; a copied `.dlvl` in a temp `user://levels/` appears; an invalid one appears as not playable with diagnostic text. |
| `SceneWiringTest` | Launch with `--level core.mossy_hollow --seed 1`; after 120 frames: world view has 6 terrain chunks, ≥ 1 water mesh, hero view exists, HUD shows `100/100`, objective text equals the level's first active objective. |
| `EventToViewTest` | Inject `DamageApplied` ⇒ damage label spawned, flash uniform set; `EntityRemoved` ⇒ view returned to pool. |
| `InputLatchTest` | Simulate `attack` press for one frame at 240 Hz ⇒ next tick input has `Pressed.Attack`. |
| `MaterialFilterTest` | Iterate all loaded materials: texture filter is nearest-family. |
| `LightBudgetTest` | Level with 20 torches ⇒ ≤ 8 enabled `OmniLight3D` at any time. |
| `PauseLifecycleTest` | Send `NOTIFICATION_APPLICATION_PAUSED` ⇒ tick count stops; resume ⇒ pause menu visible, accumulator reset. |
| `SafeAreaTest` | Fake insets (left 44 px) ⇒ touch controls and HUD shift. |
| `ScriptedPlaythroughTest` | Drive the recorded full-clear replay through the real `GameSession` inside Godot; expect `MissionComplete`, results screen visible, profile record updated. |

### 20.7 Performance tests

- BenchmarkDotNet: `Tick_MossyHollow_Replay`, `Tick_Stress_64Monsters_32Arrows`, `AStar_256_Open`, `AStar_256_Maze`, `ChunkMesher_256x256`, `LevelLoad_256x256`. Thresholds in §19 enforced by a nightly job that fails on > 20 % regression vs. stored baseline (per runner).
- In-engine: `--perf-capture <seconds>` records frame times, draw calls (`RenderingServer.GetRenderingInfo`), triangles, memory to `user://perf/*.csv`; a script summarises p50/p95/p99. Run on each reference device per milestone; results stored in `docs/perf/`.
- Load-time test: measured from `Play` press to first rendered frame with all chunks committed.

### 20.8 Visual acceptance review (per milestone, checklist)

- [ ] Camera yaw/pitch/FOV as specified; no rotation possible; follow is smooth with no visible stutter at 60/120/144 Hz (interpolation on).
- [ ] All textures crisp (no bilinear blur) at native resolution; no shimmer on distant grass.
- [ ] Cliff faces visible around the ruin; AO darkens inner corners; shadows from trees and walls on the ground; torches cast warm light that falls off visibly; emissives bloom.
- [ ] Characters: blocky proportions, rigid animations, hit flash, squash, death sink, emerge from ground on spawn, `!` alert.
- [ ] Damage numbers readable on all three platforms; enemy health bars appear/hide correctly.
- [ ] Water scrolls, foam at banks, arrows fly over it, hero cannot enter.
- [ ] Gate animation, chest lid, beacon column and locked state.
- [ ] HUD matches §16.2 at 1920×1080, 1280×720, 2560×1440, phone aspect 20:9 with notch insets.
- [ ] Fog tint and sky gradient match the level's environment block; changing a colour in `level.json` changes the scene without code changes.
- [ ] Occluder dither fade engages when the hero walks behind the ruin wall or trees.

### 20.9 CI pipeline (`.github/workflows/ci.yml`)

1. `dotnet build -warnaserror` (Core, Tools, Tests, Benchmarks); analyzer rules on.
2. Reference guard: a test asserts `typeof(World).Assembly.GetReferencedAssemblies()` contains no `GodotSharp`.
3. `dotnet test` with coverage (Core ≥ 85 % line coverage gate).
4. `dungeons-tool validate game/levels/**/level.json` ⇒ zero errors/warnings for built-in levels.
5. Atlas staleness check; animation clip check (`tools/check_clips.py`); JSON Schema validation of all `content/**/*.json` and `levels/**/level.json`.
6. Godot headless: import project, build C#, run GdUnit4 headless suite.
7. Export Windows (always), Android APK (always, debug keystore), iOS Xcode project (macOS runner, nightly/release).
8. Nightly: fuzz, benchmarks, rendered integration + visual regression, perf capture on a desktop runner.

Additional owner-requested release downloads (2026-10-29): `.github/workflows/release-artifacts.yml` runs on every push to `main` and supports manual runs on `main`. Independent Windows/Android jobs retain an Inno Setup installer `.exe`, portable `.zip`, signed direct-install `.apk` and Play-upload `.aab`, each identified by display version/run/attempt/commit SHA, with JSON build reports and SHA-256 checksums. Downloads have 30-day retention; selected diagnostics have 14 days. Android uses the registered upload key via three required Actions secrets; missing/invalid signing fails without a QA-key fallback. Windows is explicitly unsigned without its optional two Authenticode PFX secrets; configured signing must verify. CI version codes default to `1000 + github.run_number`, stamp both Android presets/metadata in the disposable checkout and reject reuse/overflow. Reruns retain their code and change only the attempt suffix. The workflow builds/downloads only, with no Play upload/rollout or GitHub Release publication. Windows install/uninstall and existing Android release-package checks gate the respective downloadable artifacts. Setup, signing and evidence scope: `docs/ci-release-artifacts.md`.

### 20.10 Manual device QA checklist (release candidate)

Device matrix (minimum): Windows with integrated GPU **and** with a discrete GPU; one low-end Android forced to the Compatibility renderer (Low preset); one mid-range Android (Medium); one iPhone; one iPad (4:3 aspect — HUD anchoring and touch arc must still fit; auto-layout must not assume 16:9+).

Checklist: install/launch time; orientation lock; touch controls under multi-touch; bow drag-to-aim; auto-aim feel; backgrounding during ambush and resume; incoming call (phones); Bluetooth gamepad hot-plug; audio with headphones/plug events; battery drain over 20 min (< 15 % on reference phones); thermal throttling behaviour; text legibility; safe-area; store screenshots; import of a user `.dlvl`; corrupt settings file recovery; uninstall/reinstall keeps nothing unexpected. GPU captures with Android GPU Inspector / Xcode Metal debugger at M7 and M8 to confirm draw-call and overdraw budgets (§19).

---

## 21. Implementation plan and milestones

Each milestone has a **gate**; the next does not start until the gate passes. Estimates are for a team of 2–3 engineers plus 1 artist.

| M | Name | Deliverables | Gate (all must pass) |
|---|---|---|---|
| M0 | Toolchain & export gate (1 wk) | Repo with Git LFS, solution, pinned versions, empty Godot C# scene with a spinning cube, a label and a JSON file read from `res://content/`, export presets, CI skeleton | Runs on Windows, a physical Android device and a physical iPhone (JSON read succeeds on device); CI builds Core tests and Windows export |
| M1 | Core simulation skeleton (2 wk) | Math, Rng, Entity/World, systems scaffold, collision, NavGrid/A*, ScenarioBuilder, hashing, replay recorder | SC-MOV-*, collision/A*/hash unit tests green; allocation test green on an empty loop |
| M2 | Level format & content (2 wk) | ContentRegistry, LevelDocument, loader, validator (all LVL codes), migrations scaffold, Tools CLI, Mossy Hollow JSON, JSON Schema | Mossy Hollow validates clean; all negative fixtures fire their codes; fuzz loader 10 min clean |
| M3 | Rendering of levels (3 wk) | ChunkMesher, atlas, terrain/water/foliage shaders, props multimesh, lights, environment, camera, debug overlay, level select (desktop) | Visual checklist items for terrain/camera/lighting; `SceneWiringTest`, `AxisSanityTest`, `MaterialFilterTest` green; draw calls within budget |
| M4 | Hero (2 wk) | Input adapter (kb/mouse/gamepad), movement, roll, melee, bow, potion, pickups, HUD, hero model/animations | SC-ROLL/CMB/RNG/PICK green; `InputLatchTest`; playable walk-through of Mossy Hollow without enemies |
| M5 | Enemies & AI (3 wk) | Zombie, skeleton, perception, pathfinding integration, drops, health bars, damage numbers, VFX/SFX hooks | SC-AI-*, SC-CAP-01 green; full-clear replay recorded as golden; tick budget met |
| M6 | Level logic & flow (2 wk) | Triggers/actions, objectives, gates, chests, checkpoints, lives, exit, results, profile, pause/fail screens | SC-TRG/OBJ/LIFE green; `ScriptedPlaythroughTest` green; Mossy Hollow completable start to finish |
| M7 | Mobile (3 wk) | Touch controls, auto-aim, safe areas, lifecycle, adaptive resolution, low preset, haptics, user-level import paths | Device QA checklist on reference Android & iPhone; perf budgets met; 20-min battery test |
| M8 | Polish & hardening (2 wk) | Art pass, audio pass, settings, localisation keys, accessibility, visual regression baselines, store packaging | Visual acceptance review signed off; nightly CI green 5 days; crash-free 2 h soak on each platform |
| M9 | Editor-readiness verification (1 wk) | Prototype "level reload" dev tool: edit `level.json` on disk ⇒ hot reload in game via `LevelDocument` diff; property schema dump | Checklist §23 fully satisfied; prototype reload < 500 ms for Mossy Hollow |

Definition of done for any feature: spec section referenced in PR; unit/scenario tests added; no new warnings; replay goldens re-recorded only with reviewer approval; debug overlay updated if a new system is introduced.

---

## 22. Coding standards and mistake-proofing

### 22.1 General

- C# 12, nullable reference types enabled, `TreatWarningsAsErrors`, `.editorconfig` committed (4 spaces, `PascalCase` public, `_camelCase` private fields).
- Core public API documented with XML comments; `docs/` generated by DocFX optional.
- One behaviour, one place: constants in `GameConstants` (Appendix A) or content JSON, never inline literals in systems (`0.35f` in code is a review blocker).
- Logging through `ILogger` with categories `Level`, `Sim`, `Ai`, `View`, `Io`; `Debug` level compiled out in release via `[Conditional("DEBUG")]` wrappers for hot paths.

### 22.2 Godot specifics

- Node scripts are presentation only; a node may hold a reference to `GameSession` but may call only `Step`, `DrainEvents`, read-only properties and session commands.
- Use `partial` classes, `[Export]` for tweakables that are *visual*; gameplay tweakables are JSON.
- Never use `GetNode` with string paths in `_Process`; cache in `_Ready`.
- Avoid LINQ, `string.Format`, boxing and closures in per-frame code; use `Span<T>` and pooled arrays.
- Scene instancing is pooled; `QueueFree` only when a pool shrinks.
- Resource paths are **case-sensitive** in exported builds (Android/iOS). CI runs on Linux to catch casing errors early; all asset paths are lowercase.
- Export filters must include `*.json` under `content/` and `levels/` (non-resource files are excluded by default).

### 22.3 Allocation discipline (Core)

- All per-tick collections preallocated to caps (entities 8192, projectiles 512, events 4096, path nodes `w*h`, pair list 16 384).
- Structs for components and events; `in` parameters; no `IEnumerable` iteration over interfaces in systems (use `List<T>` indexers or spans).
- Strings interned at load (`Tags`, ids ⇒ ints). No string concatenation in the tick.
- Verified by SC-ALLOC-01.

### 22.4 Known pitfalls checklist (read before coding each area)

1. **Axis confusion** — north is −Z, rows increase southward; the adapter rotates input by camera yaw; never flip signs "to make it look right".
2. **Frame vs tick** — anything that changes game state goes in `Step`; anything reading `delta` is presentation.
3. **Culture** — `float.Parse("24.5")` in a German locale yields 245; always `InvariantCulture`. Test exists.
4. **Dictionary order** — never iterate `Dictionary` to affect simulation.
5. **Shared Random** — never `new Random()` in Core.
6. **Texture filtering** — a single linear-filtered texture breaks the style; the material check test exists.
7. **Light limits** — > 8 omni lights per mesh silently drops lights on Mobile; use the culler.
8. **Alpha sorting** — only water and particles are alpha-blended; foliage uses scissor; occluders use dither.
9. **Touch** — joystick and buttons must work simultaneously; test with two fingers plus a third tapping potion.
10. **Lifecycle** — a backgrounded app must not advance the simulation and must not lose input state on resume.
11. **Safe areas** — HUD anchored to safe area, not viewport.
12. **Paths** — use `/` in all JSON; `user://` and `res://` only through helpers; never `Path.Combine` with `res://`.
13. **Exported JSON** — verify content/levels JSON actually ends up in the PCK (M0 gate checks a JSON read on device).
14. **Zip handling** — limit entry count/size when reading `.dlvl` (64 MB, 512 entries) to avoid zip bombs.
15. **Float drift** — compare positions with epsilon in tests; hash with quantisation.
16. **Spiral of death** — clamp accumulator; max 5 ticks per frame.
17. **Gate solidity** — flip solidity in simulation at state change, push out overlapping entities; never leave an entity inside a solid cell.
18. **Stairs** — passability depends on direction; an entity pushed sideways onto a stairs cell from a non-connected neighbour must be blocked by collision rules, not special-cased.
19. **Spawn inside walls** — spawn actions validate the marker cell at load; at runtime, if a cell became solid (closed gate), spawn at the nearest walkable cell within 3 m or defer.
20. **Events overflow** — never emit more than the ring capacity in one tick; `DamageApplied` is batched per target per tick.

---

## 23. Level editor readiness

The future editor is a Godot scene in the same project (or a separate Godot project referencing `Dungeons.Core` and `Dungeons.Game`'s presentation assemblies). To make it "easy", v1 **MUST** satisfy:

1. **Single source of truth**: `LevelDocument` is the only representation of a level; game and editor both use it. It is mutable through a command API (`ILevelCommand { Apply(doc); Undo(doc); }`) with built-ins: `SetTile(x,z,ref)`, `SetHeight(x,z,h)`, `SetOverlay`, `SetCellProps`, `Resize(w,h,anchor)`, `AddEntity`, `RemoveEntity`, `MoveEntity`, `SetEntityProps`, `AddTrigger`, `EditTrigger`, `SetObjective`, `SetEnvironment`, `SetMetadata`. Commands are serialisable (for macro/undo history).
2. **Incremental validation**: `LevelValidator.Validate(doc)` ≤ 50 ms for 256×256; `ValidateCells(rect)` for incremental checks; every diagnostic has `JsonPath` and/or `Cell` for UI highlighting.
3. **Self-describing content**: `ContentRegistry` exposes display names, categories, icons, editor chars and `PropertySchema` for every tile/entity; the editor builds palettes and inspectors from it with no hard-coded lists.
4. **Reusable rendering**: `ChunkMesher` and `WorldView.Build(doc)` work from a `LevelDocument` without a `GameSession`; `WorldView.ApplyChange(ChunkRect)` re-meshes only affected chunks (< 16 ms per chunk). Entities render in a "preview" mode (idle pose) without simulation.
5. **Play-from-editor**: `GameSession.Create(doc, …)` accepts an in-memory document (no file) and an optional override spawn position (`StartOverride { Pos, Checkpoint }`) so the designer can test from anywhere.
6. **Deterministic serialisation**: `LevelSerializer.Write(doc)` produces canonical JSON (stable key order, 2-space indent, `.1f`-minimal floats, LF) so diffs are meaningful and round-trips are byte-identical for unmodified documents (test exists).
7. **Trigger/action metadata**: every event/action/condition type has a schema (`TriggerSchema`) listing its parameters, types and reference kinds — the editor renders forms from it; Core rejects unknown types at validation.
8. **Hot reload hook**: `GameSession.ReloadLevel(doc)` rebuilds static data (nav grid, triggers, entity prototypes) while keeping the hero where possible (M9 prototype).
9. **Asset overrides reserved**: `assets/` folder in packages is read into an `IAssetSource` abstraction (v1 returns "unsupported" warning) so per-level textures/models can be added later without changing the package layout.
10. **No hidden state in files**: nothing about a level lives outside `level.json` (no scene files, no baked nav data, no resource `.tres` per level).
11. **Editor code is excluded from mobile exports by default**: editor scenes/scripts live under `game/editor/` and are stripped with a Godot export feature tag (`editor` included only in desktop presets) so the phone binaries do not grow; the Core API they rely on ships everywhere, so an iPad editor later is a preset change, not a port.
12. **Thumbnail capture is a game-side service**: `WorldView.CapturePreview(doc, 320×180)` renders a level from a fixed overview camera to a PNG. v1 uses it in the Tools CLI (`dungeons-tool preview --png`) to produce `preview.png` for built-in levels; the editor will call the same function on save.

The functional editor requirements captured so far (tools, UX bar, file operations) are kept in Appendix G so that v1 API decisions can be checked against them.

---

## 24. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Godot C# export on iOS/Android regresses or is unavailable in the chosen version | Blocks two of three platforms | M0 export gate on physical devices before feature work; Core has no engine dependency; documented Unity fallback for the host layer (§3.4) |
| Android Vulkan driver variance | Crashes/artefacts on specific devices | Mobile renderer with Compatibility fallback, Low preset, data-driven device blocklist (§18.2), per-device captures in QA |
| Determinism broken by a careless change (dictionary iteration, `System.Random`, frame-time use) | Replay tests and future co-op collapse | Golden replays on every push, hash checkpoints every 60 ticks, review checklist §7.9, analyzer rule banning `System.Random` in Core |
| Per-tick allocations creep in and cause GC hitches on phones | Frame spikes during fights | SC-ALLOC-01 fails CI on any steady-state allocation; pooled events/projectiles/paths |
| Art production bottleneck | Visual quality below the reference style | Palette capped (~14 tiles, 2 monsters, 1 hero for v1); Blockbench pipeline; style table §13.1 reviewed per milestone; grey-box placeholders unblock engineering |
| Level format changes after levels exist | Breaks user levels | `formatVersion` + migration chain with fixture tests; unknown fields warn not fail |
| Editor scope creep pulled into v1 | Delays the game | Editor explicitly out of scope; §23 lists only API-level obligations; Appendix G is informative |
| iOS signing / macOS CI availability | Blocked iOS builds | Separate macOS job, secrets-based automatic signing, manual TestFlight path documented in README |
| Performance on 256×256 user levels | Long loads, low FPS on phones | Chunked meshing on worker threads, light culler, adaptive resolution, load progress UI, benchmarks with thresholds |
| Legal exposure from reference similarity | Store takedown | §1.4 rules; asset review checklist in M8; no reference assets ever in repo |

---

## 25. Design decision record

Alternatives that were considered and the reasons for the chosen option. New proposals should be weighed against the same criteria: engine-first, data over code, deterministic simulation, readable action, mobile-first (§1.3).

| # | Decision | Alternative considered | Why the chosen option |
|---|---|---|---|
| D1 | **C# with an engine-agnostic Core** | GDScript throughout, Godot autoloads (`EventBus`, `GameState`) and node components (`HealthComponent`, `HitboxComponent`) | GDScript cannot run headless outside Godot, cannot be shared with CLI tools or an external editor, and makes deterministic allocation-free simulation hard. Typed GDScript performance in hot loops was itself listed as a risk in the alternative. Node-component gameplay couples rules to the scene tree, which blocks headless tests and replay determinism. |
| D2 | **2.5-D tile grid with integer heights** | Full 3-D block volume (`ivec3` blocks with rotations, props at `vec3`, baked navmesh sidecar) | A 3-D voxel level enables overhangs and multi-storey rooms but requires 3-D collision, navmesh baking (load-time spikes; non-deterministic across engine versions), a far harder editor (layer slicing), and a much larger validator. The reference game's spaces are overwhelmingly readable as height-mapped platforms, cliffs, stairs and bridges, all expressible here; overhead decoration (arches, canopies) is covered by visual-only props. A 3-D layer can be added in a later `formatVersion` as stacked floors without invalidating v1 files. |
| D3 | **Grid A\* in Core** | Godot `NavigationServer3D` navmesh | Navmesh is non-deterministic across platforms/versions, needs baking, and lives outside Core. Grid A\* is deterministic, microsecond-cheap to build from tiles, and reused by the validator for reachability. |
| D4 | **Triggers as a first-class array with typed events/actions + declarative sugar** | Triggers/spawners/doors as entities only (`spawner{mob,count,group,trigger}`, `door{locked_until}`) | The entity-only model is simpler for the common case but cannot express sequences, delays, messages, or multi-step arenas. We keep the general trigger model and *add* the simple forms (`core:spawner`, `gate.openWhen`) as sugar desugared at load, so authors get both and the runtime has one path. |
| D5 | **Checkpoint by radius** | Checkpoint as a box volume (`size` vec3) | Radius matches the "touch the banner" reading of the reference and keeps checkpoints as point entities for the editor; areas already exist for triggers when a volume is needed. |
| D6 | **Loot tables on chests/monsters** | Fixed `emeralds` integer per chest | Tables give variance and are needed for food/arrows now and gear later. |
| D7 | **Registry-driven entity types** | Mob names hard-coded as a schema enum | Hard-coded enums require a schema change per monster and prevent per-level content later. The schema stays generic; the semantic validator checks ids against the registry. |
| D8 | **No click-to-move** | Click-to-move toggle in settings | The reference is direct-control; click-to-move would need a second movement model (path following for the hero) in Core and conflicts with mouse-aim. Dropped; may be revisited as an accessibility option. |
| D9 | **Hit-stop in presentation only** | Hit-stop applied in the combat resolver | Pausing the simulation per hit breaks determinism and multiplayer readiness; freezing the animation time scale gives the same feel. |
| D10 | **Nearest + mipmaps for terrain, nearest only for characters** | Nearest without mipmaps everywhere | At the fixed camera distance textures are magnified, so mipmaps barely matter for characters; oblique cliff faces and the adaptive mobile resolution scale do minify and would shimmer. |
| D11 | **64 live monster cap** | 40 | Budgets in §19 are met at 64 on reference hardware with the allocation-free simulation; waves in future levels want headroom. Data-configurable per difficulty profile. |
| D12 | **ASCII row layers in JSON** | RLE-encoded block arrays | ASCII rows are human-readable, diffable, and editor-trivial; zip compression in `.dlvl` handles size. RLE only pays off for 3-D volumes (see D2). |
| D13 | **Editor deferred to post-v1; API obligations only** | Built-in editor as a v1 milestone | Per the project brief, v1 is the engine and the level system. §23 and Appendix G make the editor a UI project over existing APIs rather than new engine work. |
| D14 | **Survive objective included** | Omit until needed | It is a timer over existing state and pairs naturally with the spawner; cost is negligible and it broadens what data-only levels can express. |
| D15 | **Explicit positive-damage rounding and bounded hit queue** | Depend on runtime rounding defaults or silently drop overflow | Damage uses double intermediates, midpoint rounding away from zero and saturation to a positive integer. The 65,536-entry deferred queue fails visibly on overflow. Roll/action timers and projectile ownership are hashed; pooled storage never reuses runtime ids. |
| D16 | **Auto-aim angles from an interpolated cosine table** | Call inverse trigonometry in critical tick branches | A startup-built 0.1-degree cosine table supplies angle scoring with no trigonometric calls in ticks, following §7.9. Sight uses a supercover terrain DDA; ties keep ascending runtime ids. |
| D17 | **Cache terrain as the first canonical FNV section** | Rehash all 65,536 unchanged terrain cells every recorded tick | A copied FNV accumulator resumes the exact row-major cell byte stream before mutable world/entity sections. Core terrain edits invalidate it. This changes development checkpoint hashes; no approved gameplay golden exists yet. The 64-brain stress gate includes per-tick hashing and checks zero allocations. |
| D18 | **Spawner ownership uses stable runtime ids** | Generate a private tag and immutable settings for every runtime-created spawner | Shared templates are compiled before ticks; preallocated instance state and owner ids cover live and deferred monsters without tick allocation. User tags remain independent and expected totals protect authored waves. Ownership is hashed; development replay candidates are regenerated before review. |
| D19 | **Hash effective simulation state; preserve exact replay source identity separately** | Hash raw level bytes and unexpanded shortcut properties inside every world state | The loader expands `openWhen` into a separate program without altering authored serialization. Canonical mission/property identity plus original and current terrain preserve future behavior; equivalent explicit and shortcut programs have identical simulation hashes (SC-SPW-02). Replay envelopes still require exact level/content SHA256 identities. Development candidates are refreshed before golden review. |
| D20 | **Coalesce simultaneous area crossings** | Appendix D's earlier once-per-entity wording | §10.7 requires at most one firing per trigger per tick. Appendix D now agrees; ascending runtime-id scans and spawn-inside entry are deterministic. Objective states follow the one-way §8.9 lifecycle; attempts to hide active or reactivate terminal goals warn and do nothing. |
| D21 | **Settings assist resolves into recorded commands** | Add mutable host settings to World/replay identity | Normal remains unchanged. Off supplies facing/raw mouse aim outside touch; touch retains normal assistance. Strong halves angular score cost and expands cursor snap from 1.2 m to 1.8 m, preserving range/LOS and manual aim. Optional host assistance becomes explicit `PlayerInput.Aim`, so replay needs no settings file. |
| D22 | **Four category volumes plus Master** | Interpret §16.4's four volumes as excluding a separate master control | The four Music/SFX/UI/Ambience buses retain independent controls; Master adds a global control as required by §14. Quality fields select Custom; UI scale, VSync and fullscreen remain independent of preset tuning. |

---

## Appendix A — Constants table

Authoritative values; mirrored in `GameConstants.cs` (simulation) and content JSON (per-kind). Ticks at 60 Hz.

| Constant | Value |
|---|---|
| `TICK_RATE` | 60 |
| `MAX_TICKS_PER_FRAME` | 5 |
| `MAX_FRAME_DELTA` | 0.25 s |
| `MAX_SPEED` | 12 m/s |
| `MAX_ENTITIES` | 8192 |
| `MAX_MONSTERS` | 64 |
| `MAX_PROJECTILES` | 512 |
| `MAX_PATH_REQUESTS_PER_TICK` | 20 |
| `MAX_PATH_WAYPOINTS` | 256 |
| `PERCEPTION_INTERVAL` | 6 ticks |
| `SPATIAL_HASH_CELL` | 2 m |
| `SEPARATION_ITERATIONS` | 2 |
| `HERO_HP` | 100 |
| `HERO_SPEED` | 4.5 m/s |
| `HERO_RADIUS` | 0.35 m |
| `HERO_RECOVERY_WALK` | 0.4 × speed |
| `HERO_PICKUP_RADIUS` | 1.0 m |
| `MAGNET_RADIUS` / `MAGNET_SPEED` | 2.5 m / 6 m/s |
| `PICKUP_ARM_DELAY` | 24 ticks |
| `ROLL_TICKS` / `ROLL_DISTANCE` / `ROLL_IFRAMES` / `ROLL_COOLDOWN` | 24 / 3.0 m / ticks 3–20 / 36 ticks |
| `INVULN_AFTER_HIT` | 15 ticks |
| `RESPAWN_INVULN` | 60 ticks |
| `DEATH_TICKS` | 120 |
| `LIVES` | 3 |
| `COMBO_BUFFER` / `COMBO_RESET_GAP` | 15 / 30 ticks |
| `FOOTSTEP_INTERVAL` | 15 consecutive voluntary moving ticks; reset when idle, blocked or rolling; pause preserves phase |
| Sword swings (windup/active/recovery/dmg/kb/half-angle/reach) | 1: 6/6/12/12/1.0/60°/1.7 · 2: 6/6/12/12/1.0/60°/1.7 · 3: 9/7/21/18/2.5/70°/1.9 |
| `BOW_WINDUP` / `BOW_RECOVERY` / `BOW_COOLDOWN` | 6 / 12 / 30 ticks |
| `ARROW_DAMAGE` / `ARROW_SPEED` / `ARROW_RADIUS` / `ARROW_LIFETIME` | 10 / 14 m/s / 0.15 m / 90 ticks |
| `ARROWS_START` / `ARROWS_MAX` / `ARROW_BUNDLE` / `RESPAWN_MIN_ARROWS` | 30 / 99 / 10 / 10 |
| `POTION_COOLDOWN` | 1800 ticks |
| `FOOD_HEAL` | 25 % |
| `DAMAGE_VARIANCE` | U(0.90, 1.10) |
| `KNOCKBACK_DECAY` / `KNOCKBACK_STOP` / `STAGGER_THRESHOLD` / `STAGGER_TICKS` | 0.85 per tick / 0.05 m/s / 2.0 / 12 |
| `CHEST_OPEN_RADIUS` / `CHEST_OPEN_DELAY` | 1.2 m / 20 ticks |
| `CHECKPOINT_RADIUS_DEFAULT` | 1.5 m |
| `GATE_ANIM_TICKS` | 45 |
| `EMERGE_TICKS` | 30 |
| `CORPSE_TICKS` | 90 |
| `ALERT_TICKS` | 18 |
| `TARGET_LOST_TICKS` | 180 |
| `REPATH_INTERVAL` | 15 ticks |
| `WAYPOINT_REACH` | 0.3 m |
| `STUCK_WINDOW` / `STUCK_DISTANCE` | 20 ticks / 0.05 m |
| `AUTO_AIM_RANGE_MELEE` / `AUTO_AIM_RANGE_RANGED` / `AUTO_AIM_MAX_ANGLE` | 2.5 m / 12 m / 100° |
| `CURSOR_SNAP_RANGE` / `STRONG_CURSOR_SNAP_RANGE` / `STRONG_AIM_ANGLE_WEIGHT` | 1.2 m / 1.8 m / 0.5 |
| `XP_LEVEL_FORMULA` | `floor(sqrt(xp / 50))` |
| Zombie | HP 40, speed 2.6, r 0.35, dmg 8, reach 1.4, half-angle 45°, windup 27, active 6, recovery 42, trigger 1.3 m, aggro 7, hearing 10, xp 10 |
| Skeleton | HP 25, speed 2.8, r 0.30, arrow dmg 6, arrow speed 11, aim 36, recovery 84, range 5–8, retreat < 3.5 m (max 90 ticks), aggro 10, hearing 12, xp 12 |
| Camera default | yaw 45°, pitch −50°, distance 22, FOV 25°, look-ahead 1.5 m, follow rate 8/s |

## Appendix B — `level.json` JSON Schema (draft 2020-12)

Stored at `src/Dungeons.Core/Level/Schema/level.v1.schema.json` and embedded as a resource; `dungeons-tool validate` runs both schema and semantic validation.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://blockdungeons.example/schema/level.v1.json",
  "title": "Block Dungeons level",
  "type": "object",
  "required": ["formatVersion", "id", "name", "version", "size", "palette", "layers", "entities", "completion"],
  "additionalProperties": true,
  "properties": {
    "formatVersion": { "type": "integer", "const": 1 },
    "id": { "type": "string", "pattern": "^[a-z0-9_]+(\\.[a-z0-9_]+)*$", "maxLength": 64 },
    "name": { "$ref": "#/$defs/localizedString" },
    "author": { "type": "string", "maxLength": 64 },
    "version": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
    "description": { "$ref": "#/$defs/localizedString" },
    "difficulty": { "type": "integer", "minimum": 1, "maximum": 5, "default": 1 },
    "recommendedTimeSeconds": { "type": "integer", "minimum": 0 },
    "seed": { "type": ["integer", "null"], "default": null },
    "size": {
      "type": "object", "required": ["width", "height"], "additionalProperties": false,
      "properties": {
        "width":  { "type": "integer", "minimum": 8, "maximum": 256 },
        "height": { "type": "integer", "minimum": 8, "maximum": 256 }
      }
    },
    "environment": { "$ref": "#/$defs/environment" },
    "palette": {
      "type": "object",
      "minProperties": 1,
      "propertyNames": { "type": "string", "minLength": 1, "maxLength": 1 },
      "additionalProperties": { "$ref": "#/$defs/tileRef" }
    },
    "layers": {
      "type": "object", "required": ["floor", "heights"], "additionalProperties": false,
      "properties": {
        "floor":   { "$ref": "#/$defs/charRows" },
        "heights": { "type": "array", "items": { "type": "string", "pattern": "^[0-9a-f]+$" } },
        "overlay": { "$ref": "#/$defs/charRows" }
      }
    },
    "cells": {
      "type": "array", "maxItems": 65536,
      "items": {
        "type": "object", "required": ["x", "z", "props"], "additionalProperties": false,
        "properties": {
          "x": { "type": "integer", "minimum": 0 }, "z": { "type": "integer", "minimum": 0 },
          "props": { "type": "object" }
        }
      }
    },
    "entities": { "type": "array", "maxItems": 4096, "items": { "$ref": "#/$defs/entity" } },
    "triggers": { "type": "array", "maxItems": 512, "items": { "$ref": "#/$defs/trigger" } },
    "objectives": { "type": "array", "maxItems": 32, "items": { "$ref": "#/$defs/objective" } },
    "completion": { "$ref": "#/$defs/completion" },
    "flags": { "type": "object", "additionalProperties": { "type": "boolean" } }
  },
  "$defs": {
    "localizedString": {
      "oneOf": [
        { "type": "string", "maxLength": 512 },
        { "type": "object", "required": ["en"], "additionalProperties": { "type": "string", "maxLength": 512 } }
      ]
    },
    "color": { "type": "string", "pattern": "^#[0-9a-fA-F]{6}$" },
    "id": { "type": "string", "pattern": "^[A-Za-z0-9_]+$", "maxLength": 64 },
    "contentId": { "type": "string", "pattern": "^[a-z0-9_]+:[a-z0-9_]+$" },
    "charRows": { "type": "array", "minItems": 8, "maxItems": 256, "items": { "type": "string", "minLength": 8, "maxLength": 256 } },
    "tileRef": {
      "oneOf": [
        { "$ref": "#/$defs/contentId" },
        {
          "type": "object", "required": ["tile"], "additionalProperties": false,
          "properties": {
            "tile": { "$ref": "#/$defs/contentId" },
            "overlay": { "$ref": "#/$defs/contentId" },
            "props": { "type": "object" }
          }
        }
      ]
    },
    "facing": {
      "oneOf": [
        { "type": "string", "enum": ["north", "east", "south", "west"] },
        { "type": "array", "items": { "type": "number" }, "minItems": 2, "maxItems": 2 }
      ]
    },
    "entity": {
      "type": "object", "required": ["type", "x", "z"], "additionalProperties": false,
      "properties": {
        "id": { "$ref": "#/$defs/id" },
        "type": { "$ref": "#/$defs/contentId" },
        "x": { "type": "number" }, "z": { "type": "number" },
        "facing": { "$ref": "#/$defs/facing" },
        "tags": { "type": "array", "maxItems": 8, "items": { "$ref": "#/$defs/id" } },
        "props": { "type": "object" }
      }
    },
    "area": {
      "type": "object", "required": ["x", "z", "w", "h"], "additionalProperties": false,
      "properties": {
        "x": { "type": "number" }, "z": { "type": "number" },
        "w": { "type": "number", "exclusiveMinimum": 0 }, "h": { "type": "number", "exclusiveMinimum": 0 }
      }
    },
    "event": {
      "type": "object", "required": ["type"],
      "properties": { "type": { "type": "string", "enum": ["level_start", "enter", "exit", "entities_dead", "objective", "interact", "checkpoint", "flag", "timer"] } },
      "allOf": [
        { "if": { "properties": { "type": { "enum": ["enter", "exit"] } } },
          "then": { "required": ["area"], "properties": { "area": { "$ref": "#/$defs/area" }, "who": { "enum": ["player", "monster", "any"], "default": "player" } } } },
        { "if": { "properties": { "type": { "const": "entities_dead" } } },
          "then": { "required": ["tag"], "properties": { "tag": { "$ref": "#/$defs/id" }, "min": { "type": "integer", "minimum": 1, "default": 1 } } } },
        { "if": { "properties": { "type": { "const": "objective" } } },
          "then": { "required": ["id", "state"], "properties": { "id": { "$ref": "#/$defs/id" }, "state": { "enum": ["active", "complete", "failed"] } } } },
        { "if": { "properties": { "type": { "enum": ["interact", "checkpoint"] } } },
          "then": { "required": ["id"], "properties": { "id": { "$ref": "#/$defs/id" } } } },
        { "if": { "properties": { "type": { "const": "flag" } } },
          "then": { "required": ["flag"], "properties": { "flag": { "$ref": "#/$defs/id" }, "value": { "type": "boolean", "default": true } } } },
        { "if": { "properties": { "type": { "const": "timer" } } },
          "then": { "required": ["seconds"], "properties": { "seconds": { "type": "number", "minimum": 0 } } } }
      ]
    },
    "condition": {
      "type": "object", "required": ["type"],
      "properties": {
        "type": { "enum": ["objective", "flag", "entities_dead", "lives_at_least", "not"] },
        "id": { "$ref": "#/$defs/id" }, "state": { "enum": ["hidden", "active", "complete", "failed"] },
        "flag": { "$ref": "#/$defs/id" }, "value": { "type": "boolean" },
        "tag": { "$ref": "#/$defs/id" }, "min": { "type": "integer" },
        "lives": { "type": "integer" },
        "condition": { "$ref": "#/$defs/condition" }
      }
    },
    "action": {
      "type": "object", "required": ["type"],
      "properties": {
        "type": { "enum": ["message", "objective", "spawn", "delay", "sound", "music", "camera_shake", "set_state", "set_flag",
                           "checkpoint", "enable_trigger", "disable_trigger", "complete_level", "fail_level", "remove_entity", "heal_player", "give"] }
      },
      "allOf": [
        { "if": { "properties": { "type": { "const": "message" } } }, "then": { "required": ["text"], "properties": { "text": { "$ref": "#/$defs/localizedString" }, "seconds": { "type": "number", "default": 2.5 } } } },
        { "if": { "properties": { "type": { "const": "objective" } } }, "then": { "required": ["id", "state"], "properties": { "id": { "$ref": "#/$defs/id" }, "state": { "enum": ["active", "complete", "failed", "hidden"] } } } },
        { "if": { "properties": { "type": { "const": "spawn" } } }, "then": { "required": ["at", "entity"], "properties": { "at": { "$ref": "#/$defs/id" }, "count": { "type": "integer", "minimum": 1, "maximum": 16, "default": 1 }, "entity": { "type": "object", "required": ["type"], "properties": { "type": { "$ref": "#/$defs/contentId" }, "tags": { "type": "array", "items": { "$ref": "#/$defs/id" } }, "props": { "type": "object" } } } } } },
        { "if": { "properties": { "type": { "const": "delay" } } }, "then": { "required": ["seconds"], "properties": { "seconds": { "type": "number", "minimum": 0, "maximum": 600 } } } },
        { "if": { "properties": { "type": { "enum": ["sound", "music"] } } }, "then": { "required": ["id"], "properties": { "id": { "$ref": "#/$defs/contentId" } } } },
        { "if": { "properties": { "type": { "const": "camera_shake" } } }, "then": { "properties": { "strength": { "type": "number", "minimum": 0, "maximum": 1, "default": 0.3 }, "seconds": { "type": "number", "default": 0.3 } } } },
        { "if": { "properties": { "type": { "const": "set_state" } } }, "then": { "required": ["entity", "state"], "properties": { "entity": { "$ref": "#/$defs/id" }, "state": { "type": "string" } } } },
        { "if": { "properties": { "type": { "const": "set_flag" } } }, "then": { "required": ["flag"], "properties": { "flag": { "$ref": "#/$defs/id" }, "value": { "type": "boolean", "default": true } } } },
        { "if": { "properties": { "type": { "enum": ["checkpoint", "enable_trigger", "disable_trigger", "remove_entity"] } } }, "then": { "required": ["id"], "properties": { "id": { "$ref": "#/$defs/id" } } } },
        { "if": { "properties": { "type": { "const": "heal_player" } } }, "then": { "properties": { "percent": { "type": "number", "minimum": 0, "maximum": 100, "default": 100 } } } },
        { "if": { "properties": { "type": { "const": "give" } } }, "then": { "required": ["item", "amount"], "properties": { "item": { "enum": ["emerald", "arrows"] }, "amount": { "type": "integer", "minimum": 1 } } } }
      ]
    },
    "trigger": {
      "type": "object", "required": ["id", "event", "actions"], "additionalProperties": false,
      "properties": {
        "id": { "$ref": "#/$defs/id" },
        "once": { "type": "boolean", "default": true },
        "enabled": { "type": "boolean", "default": true },
        "event": { "$ref": "#/$defs/event" },
        "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } },
        "actions": { "type": "array", "minItems": 1, "maxItems": 64, "items": { "$ref": "#/$defs/action" } }
      }
    },
    "objective": {
      "type": "object", "required": ["id", "text", "type"], "additionalProperties": false,
      "properties": {
        "id": { "$ref": "#/$defs/id" },
        "text": { "$ref": "#/$defs/localizedString" },
        "type": { "enum": ["reach", "kill_tag", "interact", "flag", "survive"] },
        "target": { "$ref": "#/$defs/id" },
        "tag": { "$ref": "#/$defs/id" },
        "minSpawned": { "type": "integer", "minimum": 1, "default": 1 },
        "flag": { "$ref": "#/$defs/id" },
        "seconds": { "type": "number", "exclusiveMinimum": 0 },
        "initialState": { "enum": ["hidden", "active"], "default": "hidden" },
        "optional": { "type": "boolean", "default": false }
      }
    },
    "completion": {
      "type": "object", "required": ["type"], "additionalProperties": false,
      "properties": {
        "type": { "enum": ["exit", "objectives"] },
        "exit": { "$ref": "#/$defs/id" },
        "requires": { "type": "array", "items": { "$ref": "#/$defs/id" } }
      }
    },
    "environment": {
      "type": "object", "additionalProperties": false,
      "properties": {
        "sky": { "type": "object", "properties": { "top": { "$ref": "#/$defs/color" }, "horizon": { "$ref": "#/$defs/color" }, "ground": { "$ref": "#/$defs/color" } } },
        "sun": { "type": "object", "properties": { "yawDeg": { "type": "number" }, "pitchDeg": { "type": "number", "maximum": -10, "minimum": -89 }, "color": { "$ref": "#/$defs/color" }, "energy": { "type": "number", "minimum": 0, "maximum": 4 }, "shadows": { "type": "boolean" } } },
        "ambient": { "type": "object", "properties": { "color": { "$ref": "#/$defs/color" }, "energy": { "type": "number", "minimum": 0, "maximum": 2 } } },
        "fog": { "type": "object", "properties": { "color": { "$ref": "#/$defs/color" }, "density": { "type": "number", "minimum": 0, "maximum": 0.2 }, "startDistance": { "type": "number", "minimum": 0 }, "enabled": { "type": "boolean" } } },
        "glow": { "type": "object", "properties": { "enabled": { "type": "boolean" }, "intensity": { "type": "number", "minimum": 0, "maximum": 2 }, "threshold": { "type": "number", "minimum": 0, "maximum": 4 } } },
        "camera": { "type": "object", "properties": { "yawDeg": { "type": "number" }, "pitchDeg": { "type": "number", "minimum": -80, "maximum": -30 }, "distance": { "type": "number", "minimum": 12, "maximum": 40 }, "fovDeg": { "type": "number", "minimum": 15, "maximum": 45 }, "lookAhead": { "type": "number", "minimum": 0, "maximum": 4 }, "bounds": { "$ref": "#/$defs/area" } } },
        "music": { "$ref": "#/$defs/contentId" },
        "ambience": { "$ref": "#/$defs/contentId" },
        "waterColor": { "$ref": "#/$defs/color" }
      }
    }
  }
}
```

## Appendix C — Content definition examples

`content/tiles/grass.json`
```json
{ "id": "core:grass", "displayName": "Grass", "category": "ground",
  "walkable": true, "solid": false, "blocksProjectiles": false, "blocksVision": false, "moveCost": 1.0, "overlay": false,
  "render": { "kind": "block", "top": "atlas:grass_top", "side": "atlas:dirt_side", "tint": "#7bb04a", "topVariants": 4 },
  "audio": { "footstep": "core:sfx_step_soft" },
  "editor": { "icon": "editor/icons/tiles/grass.png", "char": "." } }
```

`content/tiles/tall_grass.json`
```json
{ "id": "core:tall_grass", "displayName": "Tall grass", "category": "decoration", "overlay": true,
  "walkable": true, "solid": false, "blocksProjectiles": false, "blocksVision": false, "moveCost": 1.0,
  "render": { "kind": "cross", "texture": "atlas:tall_grass", "sway": 0.6, "randomRotation": true },
  "editor": { "icon": "editor/icons/tiles/tall_grass.png", "char": "," } }
```

`content/tiles/water.json`
```json
{ "id": "core:water", "displayName": "Water", "category": "ground",
  "walkable": false, "solid": false, "blocksProjectiles": false, "blocksVision": false, "moveCost": 1.0,
  "render": { "kind": "water", "bed": "atlas:sand_dark", "color": "#2f6f8f", "foam": "atlas:foam" },
  "editor": { "icon": "editor/icons/tiles/water.png", "char": "~" } }
```

`content/tiles/stairs.json`
```json
{ "id": "core:stairs", "displayName": "Stairs", "category": "structure",
  "walkable": true, "solid": false, "blocksProjectiles": false, "blocksVision": false, "moveCost": 1.2,
  "render": { "kind": "stairs", "top": "atlas:stone_top", "side": "atlas:stone_side" },
  "props": { "dir": { "type": "enum", "values": ["north", "east", "south", "west"], "default": "north", "label": "Up direction" } },
  "editor": { "icon": "editor/icons/tiles/stairs.png", "char": "s" } }
```

`content/tiles/stone_wall.json`
```json
{ "id": "core:stone_wall", "displayName": "Stone wall", "category": "structure", "baseFloor": "core:stone_floor",
  "walkable": false, "solid": true, "blocksProjectiles": true, "blocksVision": true, "moveCost": 1.0,
  "render": { "kind": "model", "model": "models/tiles/stone_wall.glb", "variants": 2, "autoTile": "wall", "castsShadow": true },
  "editor": { "icon": "editor/icons/tiles/stone_wall.png", "char": "#" } }
```
(`autoTile: "wall"` lets the mesher pick straight/corner/end variants by neighbours; presentation only.)

`content/entities/player_spawn.json`
```json
{ "id": "core:player_spawn", "displayName": "Player spawn", "category": "logic", "kind": "Marker", "radius": 0, "solid": false,
  "unique": true, "requiresGround": true, "render": { "editorOnly": "editor/icons/entities/player_spawn.png" } }
```

`content/entities/checkpoint.json`
```json
{ "id": "core:checkpoint", "displayName": "Checkpoint", "category": "logic", "kind": "Checkpoint", "radius": 0, "solid": false, "requiresGround": true,
  "render": { "model": "models/props/checkpoint_banner.glb", "activeEmissive": true },
  "audio": { "activate": "core:sfx_checkpoint" },
  "props": { "order": { "type": "int", "default": 1, "min": 1, "max": 99 }, "radius": { "type": "float", "default": 1.5, "min": 0.5, "max": 5 } } }
```

`content/entities/gate.json`
```json
{ "id": "core:gate", "displayName": "Gate", "category": "structure", "kind": "Gate", "radius": 0, "solid": true, "requiresGround": true,
  "render": { "model": "models/props/gate_wood.glb", "animations": { "open": "open", "close": "close" } },
  "audio": { "open": "core:sfx_gate_open", "close": "core:sfx_gate_close" },
  "props": { "axis": { "type": "enum", "values": ["x", "z"], "default": "x" },
             "width": { "type": "int", "default": 2, "min": 1, "max": 8 },
             "state": { "type": "enum", "values": ["closed", "open"], "default": "closed" },
             "openWhen": { "type": "ref", "refKind": "objective", "default": "", "label": "Open when objective completes" },
             "interactable": { "type": "bool", "default": false } } }
```

`content/entities/spawner.json`
```json
{ "id": "core:spawner", "displayName": "Spawner", "category": "logic", "kind": "SpawnMarker", "radius": 0, "solid": false, "requiresGround": true,
  "render": { "editorOnly": "editor/icons/entities/spawner.png" },
  "props": { "entity": { "type": "ref", "refKind": "entity", "default": "core:zombie" },
             "count": { "type": "int", "default": 1, "min": 1, "max": 100 },
             "interval": { "type": "float", "default": 1.0, "min": 0, "max": 60 },
             "maxAlive": { "type": "int", "default": 0, "min": 0, "max": 64, "label": "Max alive (0 = count)" },
             "radius": { "type": "float", "default": 0, "min": 0, "max": 8 },
             "activation": { "type": "enum", "values": ["immediate", "proximity", "trigger", "sight"], "default": "trigger" },
             "activationRadius": { "type": "float", "default": 8, "min": 1, "max": 30 },
             "delay": { "type": "float", "default": 0, "min": 0, "max": 60 },
             "tag": { "type": "ref", "refKind": "tag", "default": "" } } }
```

`content/entities/torch.json`
```json
{ "id": "core:torch", "displayName": "Torch", "category": "light", "kind": "Light", "radius": 0.15, "solid": true, "requiresGround": true,
  "render": { "model": "models/props/torch.glb", "emissive": 2.5, "particles": "torch_flame", "light": true },
  "audio": { "loop": "core:sfx_torch_loop" },
  "props": { "color": { "type": "color", "default": "#ffa64d" },
             "radius": { "type": "float", "default": 6, "min": 1, "max": 20 },
             "energy": { "type": "float", "default": 2.0, "min": 0, "max": 8 },
             "flicker": { "type": "bool", "default": true, "label": "Flicker (±10 % at 8 Hz noise)" } } }
```

`content/entities/exit.json`
```json
{ "id": "core:exit", "displayName": "Exit beacon", "category": "logic", "kind": "Exit", "radius": 0, "solid": false, "requiresGround": true, "unique": true,
  "render": { "model": "models/props/exit_beacon.glb", "lockedEmissive": 0.2, "unlockedEmissive": 2.5, "particles": "beacon_stream" },
  "props": { "radius": { "type": "float", "default": 1.0, "min": 0.5, "max": 3 } } }
```

`content/entities/skeleton.json`
```json
{ "id": "core:skeleton", "displayName": "Skeleton", "category": "monster", "kind": "Enemy", "faction": "Monster", "radius": 0.3, "solid": false, "requiresGround": true,
  "stats": { "hp": 25, "armor": 0, "speed": 2.8, "knockbackResistance": 0, "xp": 12, "aggroRadius": 10, "hearingRadius": 12 },
  "attack": { "kind": "ranged", "projectile": "core:arrow_skeleton", "aim": 0.6, "recovery": 1.4, "minRange": 5, "maxRange": 8, "retreatBelow": 3.5, "retreatMax": 1.5 },
  "brain": "core:brain_archer", "loot": "core:loot_skeleton",
  "render": { "model": "models/characters/skeleton.glb", "animations": { "idle": "idle", "walk": "walk", "aim": "aim", "fire": "fire", "hit": "hit", "death": "death", "emerge": "emerge" }, "healthBar": true, "shadowBlob": true },
  "audio": { "alert": "core:sfx_skeleton_alert", "attack": "core:sfx_bow_release", "hit": "core:sfx_skeleton_hit", "death": "core:sfx_skeleton_death", "footstep": "core:sfx_step_soft" } }
```

`content/entities/arrow_skeleton.json`
```json
{ "id": "core:arrow_skeleton", "displayName": "Arrow (skeleton)", "category": "projectile", "kind": "Projectile", "radius": 0.15, "solid": false,
  "projectile": { "damage": 6, "speed": 11, "lifetime": 1.5, "pierce": 0, "knockback": 0.5 },
  "render": { "model": "models/props/arrow.glb", "stickOnTerrain": 2.0 } }
```

`content/loot/loot_chest_common.json`
```json
{ "id": "core:loot_chest_common",
  "rows": [ { "item": "emerald", "chance": 1.0, "min": 10, "max": 20 }, { "item": "food", "chance": 0.5 }, { "item": "arrows", "chance": 0.5 } ] }
```

## Appendix D — Trigger/action reference

Events (§10.7) — when they fire:

| Event | Fires |
|---|---|
| `level_start` | Tick 0, after all entities exist. |
| `enter` / `exit` | Entity of class `who` whose centre crosses into/out of `area` (cell-unit rectangle, inclusive of edges) this tick. Simultaneous crossings coalesce into one firing per trigger per tick, following §10.7; entities are examined in ascending runtime-id order. Spawning inside counts as entry. |
| `entities_dead` | At the end of a tick when all entities ever spawned with `tag` are dead (count ≥ `min`). Re-arms if new tagged entities spawn later and `once` is false. |
| `objective` | Objective `id` transitions to `state`. |
| `interact` | Chest `id` opened or gate `id` opened by interaction. |
| `checkpoint` | Checkpoint `id` activates (not when ignored due to lower order). |
| `flag` | Flag `flag` becomes `value`. |
| `timer` | `seconds` after the trigger became enabled (level start or `enable_trigger`). |

Actions:

| Action | Effect | Event emitted |
|---|---|---|
| `message` | HUD message `text` for `seconds`. | `ShowMessage` |
| `objective` | Set objective state (idempotent; illegal transitions ignored and logged). | `ObjectiveChanged` |
| `spawn` | Spawn `count` entities of `entity` at marker `at` (+ random offset ≤ 0.5 m by Spawn RNG, nearest walkable cell), with `Emerging` state for monsters. | `EntitySpawned` |
| `delay` | Suspend this trigger's action cursor for `ceil(seconds*60)` ticks. | — |
| `sound` / `music` | Play one-shot / switch music (crossfade). | `PlaySound`, `MusicChange` |
| `camera_shake` | Presentation shake. | `CameraShake` |
| `set_state` | Change gate (`open`/`closed`), chest (`open`), checkpoint (`active`) state. | `GateStateChanged` etc. |
| `set_flag` | Set level flag. | `FlagChanged` |
| `checkpoint` | Activate checkpoint `id` (respects order). | `CheckpointActivated` |
| `enable_trigger` / `disable_trigger` | Toggle another trigger. | — |
| `complete_level` / `fail_level` | Force session end. | `MissionComplete` / `MissionFailed` |
| `remove_entity` | Remove entity `id` (no death event; used for props/pickups). | `EntityRemoved` |
| `heal_player` | Heal `percent`. | `Healed` |
| `give` | Add emeralds/arrows. | `InventoryChanged` |

Conditions: `objective {id, state}`, `flag {flag, value}`, `entities_dead {tag, min}`, `lives_at_least {lives}`, `not {condition}`. All conditions in the array must hold (AND).

## Appendix E — Simulation event reference

All events carry `Tick`. Presentation treats unknown event types as no-ops.

| Event | Payload |
|---|---|
| `EntitySpawned` | `EntityId, DefIndex, Pos, Y, Facing, Emerging` |
| `EntityRemoved` | `EntityId, Reason (Despawn|Death|Collected|Removed)` |
| `DamageApplied` | `TargetId, SourceId, Amount, Pos, Knockback, Lethal` |
| `AttackStarted` | `EntityId, Swing (1..3 or 0 for ranged/enemy), Facing` |
| `AttackActive` | `EntityId, Swing, Facing, Pos, Y, DefinitionId (Text)` (swing sound/whoosh on first active frame) |
| `ProjectileSpawned` | `EntityId, OwnerId, Pos, Dir, Speed` |
| `ProjectileHitTerrain` | `Pos, Dir, Cell` |
| `ProjectileHitEntity` | `TargetId, Pos` |
| `EnemyAlerted` | `EntityId` |
| `EnemyStateChanged` | `EntityId, State` |
| `RollStarted` / `RollEnded` | `EntityId, Dir` |
| `PotionUsed` | `Amount` |
| `Healed` | `EntityId, Amount` |
| `PickupCollected` | `Kind, Amount, Pos` |
| `ChestOpened` | `EntityId` |
| `GateStateChanged` | `EntityId, State` |
| `CheckpointActivated` | `EntityId, Order` |
| `SpawnerActivated` / `SpawnerFinished` | `EntityId` |
| `PlayerDied` / `PlayerRespawned` | `LivesLeft, Pos` |
| `ObjectiveChanged` | `ObjectiveIndex, State` |
| `ShowMessage` | `TextKey/Text, Seconds` |
| `PlaySound` / `MusicChange` | `SoundIndex, Pos?` |
| `CameraShake` | `Strength, Seconds` |
| `ExitLocked` | `Pos` |
| `MissionComplete` / `MissionFailed` | `Results { Ticks, Emeralds, Kills, Deaths, Xp }` |
| `Footstep` | `EntityId, Pos, Y, Cell, DefinitionId (Text), SurfaceKind` (ground tile id, composite tiles resolve to base floor; emitted every 15 consecutive voluntary moving ticks; presentation may throttle) |

## Appendix F — Glossary

| Term | Meaning |
|---|---|
| Block / unit | 1 m cube; the world's grid unit. |
| Cell | Integer grid position `(cx, cz)` in a level. |
| Tile | The kind of a cell (grass, wall, water…), defined in content JSON. |
| Overlay | Decorative tile layered on a floor tile. |
| Entity | Any dynamic or placed object: hero, monster, projectile, pickup, prop, gate, marker. |
| Def / kind | Immutable definition from the ContentRegistry; entities are instances of defs. |
| Tick | One fixed simulation step (1/60 s). |
| Session | One attempt at one level: `GameSession`. |
| Package | A level directory or `.dlvl` zip. |
| Trigger | Data-driven event handler inside a level. |
| Objective | Player-facing goal with states hidden/active/complete/failed. |
| Checkpoint | Respawn point with an order; activates on proximity. |
| Replay | Recorded input stream + seed for deterministic playback. |
| Golden | A committed replay with expected hashes used as a regression test. |
| Emerging | Short spawn state for monsters rising from the ground. |
| LOS | Line of sight on the cell grid. |
| Host | The engine layer (Godot) that renders and feeds input to Core. |

## Appendix G — Future level editor requirements (informative)

Captured from earlier design work so that v1 API choices (§23) can be checked against the editor that will eventually consume them. Nothing here is a v1 deliverable.

**Core workflow**
- Painting tools over the tile grid: pencil, rectangle, flood fill, erase, eyedropper, select/move; a height brush (raise/lower by one block, flatten); overlay painting; per-cell property editing (stairs direction, variants).
- Entity placement from a palette generated from the `ContentRegistry` (icons, categories, search); inspector auto-generated from `PropertySchema`; drag to move, rotate facing with `R`, duplicate, multi-select.
- Trigger authoring: draw areas on the grid; event/condition/action forms generated from `TriggerSchema`; action lists reorderable; spawner and `openWhen` shortcuts exposed as simple fields.
- Objectives list editor with drag ordering; completion rule editor.
- Environment panel (sky/sun/fog/glow/camera/music) with live preview via `WorldView.ApplyEnvironment`.
- Problems dock showing `LevelValidator` diagnostics with codes; clicking a diagnostic focuses the cell/entity (`JsonPath`/`Cell` from §10.8).
- Play-test (`F5`) runs `GameSession.Create(doc)` in place; "Return to editor" (`F6`) restores the document view; "Start here" (`Shift+F5`) uses `StartOverride` at the cursor. Hot reload from §23.8 keeps the session when only non-structural data changed.
- Keyboard-first: every tool on a single key (`B` pencil, `U` rect, `G` fill, `E` erase, `I` pick, `S` select, `H` height brush, `Q/W` height down/up, `1–9` palette slots); tooltips with shortcuts; one-line status-bar help.

**File operations**
- New (size, base tile → flat level with a player spawn and exit), Open (`level.json` or `.dlvl`), Save, Save As, Export `.dlvl` (validates first, embeds `preview.png` from `WorldView.CapturePreview`), Import.
- Autosave every 2 minutes to `user://levels/<id>/autosave.json` with a recovery prompt; unlimited undo/redo via `ILevelCommand` with per-stroke coalescing of paint commands.

**Quality bar**
- Same UI theme as the game with a lighter "workshop" variant; smooth cursor/ghost previews; subtle placement sounds; 3-D rotating previews on palette hover.
- 16 ms frame budget while painting on a 128×128 level: paint commands batch cell edits and the renderer re-meshes only affected chunks (§23.4); validation runs incrementally (§23.2).
- First-run tutorial (paint ground, raise a wall, place spawn, place exit, play-test).

**Architecture expectations (already satisfied by v1 design)**
- `EditorDocument` wraps a `LevelDocument` + undo stack; views subscribe to `DocumentChanged(ChunkRect)`; the editor never writes to render nodes directly — it mutates the document and the renderer re-syncs, so saved files always equal what is displayed.
- Tools derive from an `EditorTool` base (`OnPress/OnDrag/OnRelease/OnHover/DrawGizmo`) registered in a list, so new tools do not touch the viewport.
- Not MVP: multi-biome palettes, prefab/room library with doorway snapping, scripted cutscene events, co-op spawn points, in-editor enemy preview playback, in-app sharing/workshop upload, iPad build.

---

*End of specification.*
