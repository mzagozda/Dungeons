# Gameplay review and approved goldens

On **2026-10-28**, the owner approved **gameplay goldens only** from the Windows RTX 3060 Mobile/Vulkan recording. The exact three reviewed recordings are promoted to `assets/baselines/replays/mossy-hollow-v1/`, with explicit attribution, scope, original candidate-manifest hash and unchanged file hashes in `manifest.json`. `committedGolden` remains false because the source changes are uncommitted. Motion/audio review, real-time pacing, audio hardware and physical-device acceptance remain pending; this approval does not extend the separate 22-scene still-image review.

- Preview: `artifacts/host-migration/native-aot-main/review-video-30fps/preview.mp4` (1280×720, 30 fps, 6:19, about 116 MiB).
- Preserved full-quality recording: `gameplay.mp4` in that directory (actual settings resize the window to 1600×900).
- Contact sheet: `contact.png`; exact package/source/replay identities and decoding results are in `result.json`, `preview.json` and `postchecks.json`.
- Approved golden files/manifest: `assets/baselines/replays/mossy-hollow-v1/`; the original pending proposal remains preserved at `artifacts/host-migration/native-aot-main/gameplay-golden-proposal/`. The three recordings retain full-clear `a6da84d1de933f5c` / 11,591 ticks, ambush `2a8bf663642acddd` / 8,364 ticks and death/respawn `395328b0fce05079` / 1,114 ticks. Their bytes match the video run and all three verify through the actual current Core CLI.

`tools/Record-DesktopReview.ps1` copies the exported Windows payload and validates the supplied source seal, uses the existing isolated-profile QA scene, and records all three replay flows plus an ordinary recorded attempt without accelerated simulation. It converts the AVI to MP4, fully decodes video/audio, requires finite non-silent audio RMS, and checks unchanged package/source inputs. This is **offline movie time**, not real-time pacing, performance, physical audio, mobile/device or two-hour soak acceptance. Human motion/audio/golden approval remains separate. The 60 fps first attempt was deliberately stopped after its observed growth projected exceeding the AVI writer's 4 GB limit; its partial output, failed result and `superseded.json` remain preserved. The current combined recording uses 30 fps.

```powershell
./tools/Record-DesktopReview.ps1 `
  -SourceManifest artifacts/host-migration/native-aot-main/source-files.json `
  -EvidenceDirectory artifacts/my-new-review
```

The optional Windows converter is downloaded only into ignored `.toolchain/video/` by `Install-VideoTools.ps1`, with wheel SHA256 and binary verification from `tools/video-toolchain.json`. It is never a game dependency. The [maintainer's pinned wheel](https://pypi.org/project/imageio-ffmpeg/0.6.0/) supplies FFmpeg; [Godot's Movie Maker documentation](https://docs.godotengine.org/en/stable/tutorials/animation/creating_movies.html) describes offline recording, AVI limits and conversion.

## Reviewed replay regression gate

`tools/replay_baseline.py` checks explicit approval/attribution, the complete three-case set, exact immutable bytes, envelope identities and every-tick hash coverage. Missing/unreviewed baselines return **3**, invalid/tampered inputs return **1**, and existing outputs are never overwritten. Metadata checking alone cannot claim simulation or physical acceptance.

`tools/Test-ReplayGoldens.ps1 -EvidenceDirectory <new-directory>` verifies the approved manifest, runs the actual current Core CLI against all three unchanged recordings, requires matching successful final identities and rechecks the immutable baseline afterward. The baseline now exists, so M6 and CI automatically invoke this read-only regression gate. A malformed/partial existing baseline fails. No runner promotes or rerecords approved files. `.dreplay` bytes are protected from Git newline conversion.

Ten synthetic metadata/CLI contracts pass, including pending review, missing attribution, tampering, identity changes, incomplete hashes, duplicate JSON/case names and output preservation. The historical 79-contract checkpoint and original pending-proposal rejection remain preserved at `artifacts/host-migration/native-aot-main/validation-guards/`.

After promotion, the actual approved-baseline integration **passes** at `artifacts/approvals/baselines-20261028/gameplay-regression/`: reviewed bytes/envelopes checked before and after all three successful current-Core playbacks. The CLI project builds with zero warnings/errors; all ten replay contracts and twelve performance comparator contracts pass in the parent evidence directory. No runtime/gameplay code, recording bytes or video files changed for this approval.
