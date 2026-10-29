"""Original procedural v1 sound bank and editable 48-bar score. No sampled recordings.

Build: python tools/make_audio.py
Audit committed source/content/imports/decoded Ogg: python tools/make_audio.py --check
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import struct

import numpy as np
import soundfile as sf

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "assets/source/audio/score.json"
MANIFEST = ROOT / "assets/source/audio/manifest.json"
RATE = 44100
TAU = 2 * np.pi


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def source_hash() -> str:
    return digest(SOURCE.read_bytes() + pathlib.Path(__file__).read_bytes())


def tone(midi: float, seconds: float, kind: str = "bell") -> np.ndarray:
    t = np.arange(round(seconds * RATE)) / RATE
    frequency = 440 * 2 ** ((midi - 69) / 12)
    if kind == "pad":
        sound = sum(np.sin(TAU * frequency * harmonic * t) / harmonic ** 2 for harmonic in (1, 2, 3))
        return (sound * np.sin(np.pi * t / seconds) ** 2).astype(np.float32)
    sound = sum(np.sin(TAU * frequency * harmonic * t) * np.exp(-t * decay) * amplitude
                for harmonic, decay, amplitude in ((1, 4, 1), (2.002, 7, .28), (3.01, 12, .12)))
    return (sound * np.minimum(t / .008, 1) * np.minimum((seconds - t) / .03, 1)).astype(np.float32)


def noise(rng: np.random.Generator, count: int, cutoff: float, highpass: float = 0) -> np.ndarray:
    frequencies = np.fft.rfftfreq(count, 1 / RATE)
    spectrum = np.fft.rfft(rng.normal(size=count))
    spectrum *= np.exp(-(frequencies / cutoff) ** 2)
    if highpass:
        spectrum *= 1 - np.exp(-(frequencies / highpass) ** 2)
    result = np.fft.irfft(spectrum, n=count)
    return (result / max(float(np.std(result)), 1e-8)).astype(np.float32)


def add(target: np.ndarray, sound: np.ndarray, seconds: float, gain: float = 1, pan: float = 0) -> None:
    """Circular overlap/add retains natural tails over a loop seam."""
    start = round(seconds * RATE) % len(target)
    if target.ndim == 2 and sound.ndim == 1:
        sound = sound[:, None] * np.sqrt(np.array([(1 - pan) / 2, (1 + pan) / 2]))
    for offset in range(0, len(sound), len(target)):
        part = sound[offset:offset + len(target)]
        first = min(len(part), len(target) - start)
        target[start:start + first] += part[:first] * gain
        target[:len(part) - first] += part[first:] * gain


def music(score: dict, seconds: float) -> np.ndarray:
    beat = 60 / score["bpm"]
    if score["bars"] * 4 * beat != seconds:
        raise ValueError("Music duration must match the authored number of bars")
    result = np.zeros((round(seconds * RATE), 2), dtype=np.float32)
    for bar in range(score["bars"]):
        start = bar * 4 * beat
        chord = score["chords"][bar % len(score["chords"])]
        phrase = bar // 8
        for index, note in enumerate(chord):
            add(result, tone(note, 4.8 * beat, "pad"), start, .07, (index - 1) * .45)
        add(result, tone(chord[0] - 12, 2.5 * beat, "pad"), start, .09, 0)
        # Alternating sparse phrases leave room for combat and forest detail.
        for index, note in enumerate(score["melody"][bar % len(score["melody"])]):
            if phrase in (0, 3) and index % 2:
                continue
            add(result, tone(note + (12 if phrase == 4 else 0), 1.6 * beat), start + (index + .25) * beat, .12, -.25 if index % 2 else .25)
        if phrase in (2, 4, 5):
            for index in range(8):
                add(result, tone(chord[index % 3] + 12, beat), start + index * beat / 2, .035, .5 if index % 2 else -.5)
    # Wrapped delay provides a continuous room tail instead of a fade-to-silence seam.
    dry = result.copy()
    for seconds_delay, gain in ((.19, .18), (.37, .13), (.61, .08)):
        result += np.roll(dry[:, ::-1], round(seconds_delay * RATE), axis=0) * gain
    return result


def synth(item: dict, score: dict, variant: int) -> np.ndarray:
    seconds = item["seconds"]
    count = round(seconds * RATE)
    kind = item["kind"]
    rng = np.random.default_rng(int(digest((item["id"] + str(variant) + str(score["seed"])).encode())[:16], 16))
    t = np.arange(count) / RATE
    f = item.get("frequency", 440) * (1 + variant * .065)
    envelope = np.minimum(t / .006, 1) * np.minimum((seconds - t) / .025, 1)
    if kind == "music":
        result = music(score["music"], seconds)
    elif kind in ("torch", "forest"):
        result = noise(rng, count, 900 if kind == "torch" else 350, 35) * .025
        if kind == "torch":
            for onset in rng.uniform(0, seconds, 65):
                click = noise(rng, round(.035 * RATE), 5000, 800) * np.exp(-np.arange(round(.035 * RATE)) / (RATE * .007))
                add(result, click, onset, float(rng.uniform(.03, .12)))
        else:
            for onset in rng.uniform(0, seconds, 24):
                length = .22; u = np.arange(round(length * RATE)) / RATE
                chirp = np.sin(TAU * (float(rng.uniform(2600, 4200)) * u + 220 * u * u)) * np.sin(np.pi * u / length) ** 2
                chirp *= (.5 + .5 * np.sin(TAU * 28 * u)) ** 3
                add(result, chirp, onset, .035)
    elif kind == "chime":
        result = np.zeros(count, dtype=np.float32)
        for index, note in enumerate(item["notes"]):
            onset = index * min(.18, seconds / (len(item["notes"]) + 2))
            part = tone(note, seconds - onset)
            first = round(onset * RATE); result[first:first + len(part)] += part[:count - first] * .5
    elif kind == "whoosh":
        result = noise(rng, count, f * 2, 90) * np.sin(np.pi * t / seconds) ** 2
        result += .15 * np.sin(TAU * (f * .12 * t - f * .04 * t * t / seconds)) * np.sin(np.pi * t / seconds) ** 2
    elif kind in ("wood", "stone", "impact", "step"):
        result = noise(rng, count, {"wood": 3000, "stone": 6000, "impact": 1700, "step": 900}[kind]) * np.exp(-t / .024) * .5
        result += sum(np.sin(TAU * f * multiple * t) * np.exp(-t / decay) * gain for multiple, decay, gain in ((1, .055, .7), (2.7, .025, .25), (4.1, .018, .1)))
    elif kind == "twang":
        result = sum(np.sin(TAU * f * harmonic * t) / harmonic ** 1.3 for harmonic in range(1, 8)) * np.exp(-t / .08)
        result += noise(rng, count, 7000, 1000) * np.exp(-t / .012) * .25
    elif kind == "creak":
        phase = TAU * f * (t + .05 * np.sin(TAU * 7 * t))
        result = (.3 * np.sin(phase) + .15 * np.sin(2.3 * phase) + .12 * noise(rng, count, 1800)) * np.sin(np.pi * t / seconds) ** .7
        result += noise(rng, count, 2000) * np.exp(-t / .018) * .5
    elif kind == "voice":
        phase = TAU * f * (t - .15 * t * t / seconds + .004 * np.sin(TAU * 13 * t))
        result = sum(np.sin(harmonic * phase) / harmonic for harmonic in (1, 2, 3, 5, 7))
        result = (result * .6 + noise(rng, count, 1500, 200) * .2) * np.sin(np.pi * t / seconds) ** .7
    elif kind == "bones":
        result = np.zeros(count, dtype=np.float32)
        for onset in np.arange(.005, seconds * .8, .07):
            u = t[t < min(.18, seconds - onset)]
            click = sum(np.sin(TAU * f * ratio * u) * np.exp(-u / decay) * gain for ratio, decay, gain in ((1, .025, .4), (1.6, .017, .3), (2.9, .01, .2)))
            first = round(onset * RATE); result[first:first + len(click)] += click[:count - first]
    elif kind == "bubbles":
        result = np.zeros(count, dtype=np.float32)
        for index, onset in enumerate(np.arange(.03, seconds * .8, .08)):
            u = t[t < min(.12, seconds - onset)]
            bubble = np.sin(TAU * f * (1 + index * .13) * (u + 2 * u * u)) * np.sin(np.pi * u / .12) ** 2 * np.exp(-u * 15)
            first = round(onset * RATE); result[first:first + len(bubble)] += bubble[:count - first]
    elif kind == "horn":
        result = sum(np.sin(TAU * f * h * t + .04 * np.sin(TAU * 5 * t)) / h ** 1.2 for h in range(1, 8)) * np.sin(np.pi * t / seconds) ** .6
    else:
        raise ValueError(f"Unknown synthesis kind {kind}")
    if not item.get("loop", False):
        result *= envelope
    peak = float(np.max(np.abs(result)))
    return (result * (.55 if kind == "music" else .65) / max(peak, 1e-8)).astype(np.float32)


def canonical_ogg(path: pathlib.Path) -> None:
    """libsndfile randomizes Ogg serials. Normalize each page and repair its CRC."""
    data = bytearray(path.read_bytes())
    table = []
    for value in range(256):
        crc = value << 24
        for _ in range(8):
            crc = ((crc << 1) ^ (0x04C11DB7 if crc & 0x80000000 else 0)) & 0xFFFFFFFF
        table.append(crc)
    position = 0
    serial = int(digest(path.name.encode())[:8], 16)
    while position < len(data):
        if data[position:position + 4] != b"OggS":
            raise ValueError("Invalid Ogg page")
        segments = data[position + 26]
        length = 27 + segments + sum(data[position + 27:position + 27 + segments])
        struct.pack_into("<I", data, position + 14, serial)
        data[position + 22:position + 26] = b"\0" * 4
        crc = 0
        for value in data[position:position + length]:
            crc = ((crc << 8) & 0xFFFFFFFF) ^ table[((crc >> 24) ^ value) & 0xFF]
        struct.pack_into("<I", data, position + 22, crc)
        position += length
    path.write_bytes(data)


def entries(score: dict):
    for item in score["sounds"]:
        count = item.get("variants", 1)
        paths = [f"audio/{'music' if item['kind'] == 'music' else 'ambience' if item.get('loop') else 'sfx'}/{item['id']}{'_' + str(i + 1) if count > 1 else ''}.ogg" for i in range(count)]
        yield item, paths


def catalogue(score: dict) -> dict:
    return {"core:" + item["id"]: {"files": paths, "volumeDb": item.get("volumeDb", -3),
            "pitchRange": [1, 1] if item.get("loop") or item["kind"] == "chime" else [.95, 1.05],
            "maxInstances": 1 if item["kind"] == "music" else 4, "bus": item.get("bus", "SFX"),
            "loop": item.get("loop", False), "loopOffset": 0,
            "positional": item["kind"] not in ("music", "forest") and item.get("bus") != "UI"} for item, paths in entries(score)}


def audit(score: dict) -> None:
    manifest = json.loads(MANIFEST.read_text())
    if manifest["sourceHash"] != source_hash():
        raise ValueError("Audio source/tool changed; regenerate the sound bank")
    content = json.loads((ROOT / "game/content/audio.json").read_text())
    if content != catalogue(score):
        raise ValueError("Audio registry differs from editable source")
    expected = {path for _, paths in entries(score) for path in paths}
    if expected != set(manifest["files"]):
        raise ValueError("Audio manifest coverage differs")
    for item, paths in entries(score):
        for relative in paths:
            path = ROOT / "game" / relative
            metadata = manifest["files"][relative]
            if digest(path.read_bytes()) != metadata["sha256"]:
                raise ValueError(f"Stale audio output: {relative}")
            with sf.SoundFile(path) as stream:
                rate = stream.samplerate
                decoded = np.concatenate(list(stream.blocks(blocksize=4096, dtype="float32", always_2d=True)))
            channels = 2 if item["kind"] == "music" else 1
            if rate != RATE or decoded.shape != (round(item["seconds"] * RATE), channels) or sf.info(path).subtype != "VORBIS":
                raise ValueError(f"Format/channel/duration mismatch: {relative}")
            if not np.isfinite(decoded).all() or not .01 < float(np.max(np.abs(decoded))) < .99 or float(np.sqrt(np.mean(decoded ** 2))) < .001:
                raise ValueError(f"Silent, clipped or invalid decoded samples: {relative}")
            if item.get("loop") and float(np.max(np.abs(decoded[-1] - decoded[0]))) > .08:
                raise ValueError(f"Loop boundary has a discontinuity: {relative}")
            importer = path.with_suffix(".ogg.import").read_text()
            parameters = dict(line.split("=", 1) for line in importer.splitlines() if "=" in line)
            if parameters.get("loop") != ("true" if item.get("loop") else "false") or float(parameters.get("loop_offset", "nan")) != 0:
                raise ValueError(f"Wrong Godot loop import {relative}")
    print(f"Audio checks passed: {len(content)} IDs / {len(expected)} original Vorbis files, 44.1 kHz, 144 s stereo score, mono SFX/ambience and loop imports")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--write-imports", action="store_true", help="Set loop imports after Godot's initial import; does not regenerate audio")
    args = parser.parse_args()
    score = json.loads(SOURCE.read_text())
    if score["sampleRate"] != RATE:
        raise ValueError("The v1 asset contract requires 44.1 kHz")
    if args.check:
        audit(score)
        return
    if args.write_imports:
        for item, paths in entries(score):
            for relative in paths:
                path = (ROOT / "game" / relative).with_suffix(".ogg.import")
                text = path.read_text()
                text = "\n".join(f"loop={'true' if item.get('loop') else 'false'}" if line.startswith("loop=") else "loop_offset=0.0" if line.startswith("loop_offset=") else line for line in text.splitlines()) + "\n"
                path.write_text(text, encoding="utf-8", newline="\n")
        return
    files = {}
    for item, paths in entries(score):
        for variant, relative in enumerate(paths):
            path = ROOT / "game" / relative; path.parent.mkdir(parents=True, exist_ok=True)
            samples = synth(item, score, variant)
            # Keep native Vorbis conversion buffers bounded, including on Windows.
            with sf.SoundFile(path, "w", samplerate=RATE, channels=2 if samples.ndim == 2 else 1, format="OGG", subtype="VORBIS") as stream:
                for first in range(0, len(samples), 4096):
                    stream.write(samples[first:first + 4096])
            canonical_ogg(path)
            files[relative] = {"sha256": digest(path.read_bytes()), "seconds": item["seconds"], "channels": 2 if samples.ndim == 2 else 1}
            print(relative)
    (ROOT / "game/content/audio.json").write_text(json.dumps(catalogue(score), indent=2) + "\n", encoding="utf-8", newline="\n")
    MANIFEST.write_text(json.dumps({"formatVersion": 1, "sourceHash": source_hash(), "numpy": np.__version__, "soundfile": sf.__version__, "libsndfile": sf.__libsndfile_version__, "files": files}, indent=2) + "\n", encoding="utf-8", newline="\n")
    print("Import in Godot, set loop/loop_offset from the catalogue, then run --check")


if __name__ == "__main__":
    main()
