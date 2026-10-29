"""Desktop policy and isolated/mobile package contracts, using small synthetic assets."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from PIL import Image
import mobile_texture_variants as variants
from audit_mobile_texture_package import audit
from texture_imports import expected, mipmaps, parameters, rewrite, sha, validate


SETTINGS = '[remap]\nimporter="texture"\ntype="CompressedTexture2D"\nuid="uid://abcdefghijkl"\npath="res://original.ctex"\n\n[params]\ncompress/mode=0\ncompress/high_quality=false\nmipmaps/generate=false\ndetect_3d/compress_to=0\n'


class TextureImportTests(unittest.TestCase):
    def setUp(self):
        root = Path(__file__).resolve().parents[1] / "artifacts/mobile-textures"
        root.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="texture-contract-", dir=root)
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name).resolve()
        self.assertTrue(self.folder.is_relative_to(root.resolve()))
        self.repository = self.folder / "repository"
        self.source = self.repository / "game/textures/models/hero.png"
        self.source.parent.mkdir(parents=True)
        Image.new("RGBA", (8, 8), (80, 100, 80, 255)).save(self.source)
        self.sidecar = self.source.with_suffix(".png.import")
        self.sidecar.write_text(SETTINGS)
        self.project = self.folder / "isolated"

    def prepare(self):
        with patch.object(variants, "ROOT", self.repository):
            return variants.prepare(self.project)

    def native_fixture(self):
        manifest = self.prepare()
        target = "res://.godot/imported/hero.png-synthetic.etc2.ctex"
        data = self.project / target.removeprefix("res://")
        data.parent.mkdir(parents=True)
        data.write_bytes(b"synthetic native audit contract only")
        record = dict(manifest["textures"][0], mobilePath=target, family="ETC2", mipmapCount=0, imageFormat=30, gpuBytes=32)
        (self.project / "native-audit.json").write_text(json.dumps(dict(passed=True, godot="synthetic unit fixture", textures=[record])))
        return manifest

    def finish(self):
        with patch.object(variants, "ROOT", self.repository):
            return variants.finish(self.project)

    def test_character_pickup_and_real_extracted_glb_mip_policy(self):
        for path in ("textures/models/hero.png", "textures/models/arrow_bundle.png", "textures/models/food.png", "models/characters/hero_0.png", "models/characters/zombie_0.png", "models/props/arrow_0.png"):
            self.assertFalse(mipmaps(path), path)
        for path in ("textures/terrain_atlas.png", "textures/terrain/water.png", "textures/models/tree_oak.png", "models/tiles/tree_oak_0.png", "models/props/chest_0.png"):
            self.assertTrue(mipmaps(path), path)

    def test_rewrite_only_changes_explicit_params_and_keeps_uid_and_remap(self):
        result = rewrite(SETTINGS, expected("textures/models/hero.png", mobile=True))
        self.assertEqual(SETTINGS.split("[params]")[0], result.split("[params]")[0])
        self.assertEqual("2", parameters(result)["compress/mode"])
        validate(result, "textures/models/hero.png", mobile=True)
        with self.assertRaises(ValueError):
            validate(result, "textures/models/hero.png")

    def test_missing_duplicate_and_reenabled_auto_detection_are_rejected(self):
        for source in (SETTINGS.replace("detect_3d/compress_to=0\n", ""), SETTINGS + "compress/mode=0\n", SETTINGS.replace("detect_3d/compress_to=0", "detect_3d/compress_to=1")):
            with self.subTest(source=source), self.assertRaises(ValueError):
                validate(source, "textures/models/hero.png")

    def test_isolated_project_preserves_sources_and_refuses_existing_or_game_destination(self):
        before = self.sidecar.read_bytes()
        original = self.source.read_bytes()
        manifest = self.prepare()
        self.assertEqual(1, len(manifest["textures"]))
        self.assertEqual(before, self.sidecar.read_bytes())
        self.assertEqual(original, self.source.read_bytes())
        validate((self.project / "textures/models/hero.png.import").read_text(), "textures/models/hero.png", mobile=True)
        with self.assertRaises(FileExistsError):
            self.prepare()
        with patch.object(variants, "ROOT", self.repository), self.assertRaises(ValueError):
            variants.prepare(self.repository / "game/inside")

    def test_finish_requires_complete_native_coverage_and_matching_mipmaps(self):
        self.native_fixture()
        audit_file = self.project / "native-audit.json"
        original = json.loads(audit_file.read_text())
        for result in (dict(original, passed=False), dict(original, textures=[]), dict(original, textures=[dict(original["textures"][0], mipmaps=True)])):
            audit_file.write_text(json.dumps(result))
            with self.assertRaises(ValueError):
                self.finish()

    def test_changed_desktop_source_or_import_cannot_be_exported_as_old_native_data(self):
        self.native_fixture()
        self.sidecar.write_text(SETTINGS + "\n")
        with self.assertRaisesRegex(ValueError, "inputs changed"):
            self.finish()

    def test_final_export_remap_contains_only_the_native_mobile_target(self):
        self.native_fixture()
        manifest = self.finish()
        record = manifest["textures"][0]
        source = (self.project / record["mobileImportFile"]).read_text()
        self.assertIn('uid="uid://abcdefghijkl"', source)
        self.assertIn('path="' + record["mobilePath"] + '"', source)
        self.assertNotIn("path.s3tc", source)
        self.assertNotIn("[params]", source)
        self.assertFalse(manifest["physicalAcceptance"])
        self.assertEqual(sha(self.project / record["mobileImportFile"]), record["mobileImportSha256"])
        with self.assertRaises(FileExistsError):
            self.finish()

    def test_package_audit_rejects_changed_bytes_and_desktop_variants(self):
        self.native_fixture()
        manifest = self.finish()
        record = manifest["textures"][0]
        metadata = (self.project / record["mobileImportFile"]).read_bytes()
        data = (self.project / record["mobileDataFile"]).read_bytes()
        for variant in ("valid", "changed", "desktop"):
            package = self.folder / (variant + ".apk")
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("assets/textures/models/hero.png.import", metadata)
                archive.writestr("assets/" + record["mobileDataFile"], data if variant != "changed" else b"changed")
                if variant == "desktop":
                    archive.writestr("assets/" + record["mobileDataFile"].replace(".etc2.ctex", ".ctex"), b"desktop variant")
            if variant == "valid":
                result = audit(package, self.project)
                self.assertEqual(1, result["textureCount"])
                self.assertFalse(result["physicalAcceptance"])
            else:
                with self.assertRaises(ValueError):
                    audit(package, self.project)


if __name__ == "__main__":
    unittest.main()
