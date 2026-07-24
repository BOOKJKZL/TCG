#!/usr/bin/env python3
"""Build the small CJK fallback font used by the Unity UI.

Requires fonttools (`python -m pip install fonttools`). The source font must be
licensed under the SIL Open Font License; the generated font is deliberately
renamed because it is a modified subset.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont


TEXT_SUFFIXES = {".asset", ".cs", ".uss", ".uxml"}
TEXT_ROOTS = (
    Path("Assets/Scripts"),
    Path("Assets/Resources/Data/Localization"),
)


def collect_ui_codepoints(repo_root: Path) -> set[int]:
    codepoints: set[int] = set()
    for relative_root in TEXT_ROOTS:
        root = repo_root / relative_root
        for path in root.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in TEXT_SUFFIXES:
                continue
            text = path.read_text(encoding="utf-8", errors="ignore")
            codepoints.update(ord(character) for character in text if ord(character) >= 0x2E80)
            for encoded in re.findall(r"\\u([0-9a-fA-F]{4})", text):
                value = int(encoded, 16)
                if value >= 0x2E80:
                    codepoints.add(value)
    if not codepoints:
        raise RuntimeError("No CJK UI characters were found.")
    return codepoints


def rename_modified_font(font: TTFont) -> None:
    replacements = {
        1: "Universal UI Chinese Subset",
        2: "Regular",
        3: "Universal UI Chinese Subset Regular",
        4: "Universal UI Chinese Subset Regular",
        6: "UniversalUIChineseSubset-Regular",
        16: "Universal UI Chinese Subset",
        17: "Regular",
    }
    names = font["name"]
    for name_id, value in replacements.items():
        names.removeNames(nameID=name_id)
        names.setName(value, name_id, 3, 1, 0x409)
        names.setName(value, name_id, 1, 0, 0)


def build(source_font: Path, output_font: Path, repo_root: Path) -> None:
    codepoints = collect_ui_codepoints(repo_root)
    font = TTFont(source_font)
    if "fvar" in font:
        font = instantiateVariableFont(font, {"wght": 400}, inplace=False)

    options = subset.Options()
    options.hinting = False
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.notdef_glyph = True
    options.recommended_glyphs = True
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(unicodes=codepoints)
    subsetter.subset(font)
    rename_modified_font(font)

    output_font.parent.mkdir(parents=True, exist_ok=True)
    font.save(output_font)
    print(f"Wrote {output_font} with {len(codepoints)} UI codepoints ({output_font.stat().st_size} bytes).")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-font", type=Path, required=True)
    parser.add_argument("--output-font", type=Path, required=True)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    arguments = parser.parse_args()
    build(
        arguments.source_font.resolve(),
        arguments.output_font.resolve(),
        arguments.repo_root.resolve(),
    )


if __name__ == "__main__":
    main()
