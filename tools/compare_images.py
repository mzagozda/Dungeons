"""Specification 20.1: 2% per-channel tolerance, at most 0.5% mismatched pixels."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

CHANNEL_TOLERANCE = 0.02
MAX_MISMATCH = 0.005
MAX_PIXELS = 4096 * 4096


def compare(expected: Image.Image, actual: Image.Image) -> tuple[dict, np.ndarray | None]:
    if expected.size != actual.size:
        return {"passed": False, "reason": "dimensions", "expectedSize": expected.size, "actualSize": actual.size}, None
    width, height = expected.size
    if width <= 0 or height <= 0 or width * height > MAX_PIXELS:
        raise ValueError("Image exceeds the 4096-square pixel budget")
    left = np.asarray(expected.convert("RGBA"), dtype=np.int16)
    right = np.asarray(actual.convert("RGBA"), dtype=np.int16)
    difference = np.abs(left - right)
    mismatch = np.any(difference > 255 * CHANNEL_TOLERANCE, axis=2)
    count = int(np.count_nonzero(mismatch))
    ratio = count / (width * height)
    return {
        "passed": ratio <= MAX_MISMATCH, "width": width, "height": height,
        "channelTolerance": CHANNEL_TOLERANCE, "maximumMismatchFraction": MAX_MISMATCH,
        "mismatchedPixels": count, "mismatchFraction": ratio,
        "maximumChannelDifference": int(difference.max()),
    }, mismatch


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("expected", type=Path)
    parser.add_argument("actual", type=Path)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--diff", type=Path)
    args = parser.parse_args()
    try:
        inputs = {args.expected.resolve(), args.actual.resolve()}
        outputs = [args.report.resolve()] + ([args.diff.resolve()] if args.diff else [])
        if len(set(outputs)) != len(outputs) or any(path in inputs or path.exists() and any(path.samefile(source) for source in inputs) for path in outputs):
            raise ValueError("Comparison outputs must be distinct from each other and the source images")
        with Image.open(args.expected) as expected, Image.open(args.actual) as actual:
            result, mismatch = compare(expected, actual)
            if args.diff and mismatch is not None:
                background = np.asarray(actual.convert("RGB"), dtype=np.uint8) // 4
                background[mismatch] = [255, 32, 64]
                args.diff.parent.mkdir(parents=True, exist_ok=True)
                Image.fromarray(background).save(args.diff)
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result))
        return 0 if result["passed"] else 1
    except (OSError, ValueError, Image.DecompressionBombError) as error:
        print("IMAGE_COMPARE=FAIL " + str(error))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
