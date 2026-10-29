# Pixel UI font provenance

Silkscreen Regular is an unmodified font by The Silkscreen Project Authors,
vendored from revision `206ccf3f5234c281461e63ecc59cbc6b0563472b` of
[googlefonts/silkscreen](https://github.com/googlefonts/silkscreen).
Its upstream path is `fonts/ttf/Silkscreen-Regular.ttf`.

The complete copyright and SIL Open Font License 1.1 text ships in
`game/licenses/silkscreen_ofl.txt`. `manifest.json` pins both downloaded files;
`python tools/make_ui.py --check` verifies their SHA256 identities offline.
No network download occurs during build, import or gameplay.

The Godot import disables antialiasing, subpixel positioning, mipmaps and MSDF,
with oversampling 1, to preserve the original rectangular pixel contours.
System fallback remains available for user-authored level names outside the
font's Latin/Latin Extended coverage. English shipped strings are checked against
the actual font by the engine integration tests.

`tools/make_ui.py` also authors six original 8-pixel arrow/diamond/warning glyphs
as `block_symbols.fnt` and its hard-alpha page. The BMFont importer embeds that
page losslessly, marks the standalone page as skipped, and scales the glyphs by
integer factors. Runtime adds this font as a fallback without changing the
upstream Silkscreen binary.
