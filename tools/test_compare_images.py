import unittest
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

from PIL import Image
from compare_images import compare


class ImageComparisonTests(unittest.TestCase):
    def test_cli_mismatch_returns_one_and_writes_a_rejected_report(self):
        artifact_root = Path(__file__).resolve().parents[1] / 'artifacts/m8'
        artifact_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='image-compare-', dir=artifact_root) as directory:
            folder = Path(directory).resolve()
            self.assertTrue(folder.is_relative_to(artifact_root.resolve()))
            left, right, report = folder / 'left.png', folder / 'right.png', folder / 'result.json'
            Image.new('RGB', (4, 4), 'black').save(left)
            Image.new('RGB', (4, 4), 'white').save(right)
            result = subprocess.run([sys.executable, str(Path(__file__).with_name('compare_images.py')), str(left), str(right), '--report', str(report)], capture_output=True, check=False)
            self.assertEqual(1, result.returncode, result.stderr)
            self.assertFalse(json.loads(report.read_text())['passed'])

    def test_cli_cannot_overwrite_a_source_image_through_a_hardlink(self):
        artifact_root = Path(__file__).resolve().parents[1] / 'artifacts/m8'
        artifact_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='image-compare-', dir=artifact_root) as directory:
            folder = Path(directory).resolve()
            self.assertTrue(folder.is_relative_to(artifact_root.resolve()))
            source, alias = folder / 'source.png', folder / 'alias.json'
            Image.new('RGB', (4, 4), 'black').save(source)
            before = source.read_bytes()
            os.link(source, alias)
            result = subprocess.run([sys.executable, str(Path(__file__).with_name('compare_images.py')), str(source), str(source), '--report', str(alias)], capture_output=True, check=False)
            self.assertEqual(2, result.returncode, result.stderr)
            self.assertEqual(before, source.read_bytes())

    def test_exact_and_five_step_channel_differences_pass(self):
        expected = Image.new("RGBA", (20, 10), (100, 100, 100, 255))
        for colour in [(100, 100, 100, 255), (105, 95, 100, 250)]:
            result, _ = compare(expected, Image.new("RGBA", expected.size, colour))
            self.assertTrue(result["passed"])
            self.assertEqual(0, result["mismatchedPixels"])

    def test_one_in_two_hundred_pixels_is_the_inclusive_boundary(self):
        expected = Image.new("RGBA", (20, 10), (0, 0, 0, 255))
        actual = expected.copy()
        actual.putpixel((0, 0), (6, 0, 0, 255))
        result, _ = compare(expected, actual)
        self.assertTrue(result["passed"])
        self.assertEqual(.005, result["mismatchFraction"])
        actual.putpixel((1, 0), (0, 0, 0, 249))
        result, _ = compare(expected, actual)
        self.assertFalse(result["passed"])
        self.assertEqual(2, result["mismatchedPixels"])

    def test_unsigned_wraparound_cannot_hide_a_large_regression(self):
        result, _ = compare(Image.new("RGBA", (4, 4), (0, 0, 0, 0)), Image.new("RGBA", (4, 4), (255, 255, 255, 255)))
        self.assertFalse(result["passed"])
        self.assertEqual(255, result["maximumChannelDifference"])

    def test_dimensions_fail_and_oversized_inputs_are_rejected(self):
        result, mismatch = compare(Image.new("RGB", (1, 1)), Image.new("RGB", (2, 1)))
        self.assertFalse(result["passed"])
        self.assertEqual("dimensions", result["reason"])
        self.assertIsNone(mismatch)
        with self.assertRaises(ValueError):
            compare(Image.new("L", (4097, 4096)), Image.new("L", (4097, 4096)))


if __name__ == "__main__":
    unittest.main()
