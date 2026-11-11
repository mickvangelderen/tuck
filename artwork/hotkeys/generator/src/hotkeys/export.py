"""Pack RGBA keycaps into a PNG atlas with Unity sprite rectangles."""
from pathlib import Path
import json
from PIL import Image

ATLAS_WIDTH = 1024
MAX_ATLAS_HEIGHT = 2048
PADDING = 2


def pack_atlas(keys: list[dict]):
    if not keys:
        raise ValueError("Cannot pack an empty atlas.")
    rectangles = {}
    x = y = row_height = 0
    for key in sorted(keys, key=lambda k: (-k["image"].height, -k["image"].width, k["name"])):
        name, image = key["name"], key["image"]
        if name in rectangles:
            raise ValueError(f"Duplicate sprite name: {name}")
        width, height = image.size
        if width + 2 * PADDING > ATLAS_WIDTH:
            raise ValueError(f"{name} is too wide for the {ATLAS_WIDTH}px atlas.")
        if x + width + 2 * PADDING > ATLAS_WIDTH:
            y += row_height
            x = row_height = 0
        rectangles[name] = {"x": x + PADDING, "y": y + PADDING, "width": width, "height": height}
        x += width + 2 * PADDING
        row_height = max(row_height, height + 2 * PADDING)
    atlas_height = 1 << (y + row_height - 1).bit_length()
    if atlas_height > MAX_ATLAS_HEIGHT:
        raise ValueError(f"Atlas exceeds the {MAX_ATLAS_HEIGHT}px height limit.")
    atlas = Image.new("RGBA", (ATLAS_WIDTH, atlas_height))
    for key in keys:
        rect = rectangles[key["name"]]
        # Copy without a mask so partially transparent pixels retain their exact RGBA.
        atlas.paste(key["image"], (rect["x"], rect["y"]))
        rect["y"] = atlas_height - rect["y"] - rect["height"]
    return atlas, rectangles


def export_assets(keys: list[dict], output: Path, os_overrides: dict):
    """Replace an existing atlas or legacy individual-PNG export."""
    legacy_pngs = {f"{key['name']}.png" for key in keys}
    allowed = {"hotkeys.json", "hotkeys.png", *legacy_pngs}
    if output.exists() and any(p.name not in allowed or not p.is_file() or p.is_symlink()
                               for p in output.iterdir()):
        raise ValueError("Clean export needs an empty directory or an existing hotkey export; choose another --output.")
    atlas, by_name = pack_atlas(keys)
    entries = []
    for key in keys:
        if key["layout"].get("os"):
            continue
        name, code = key["name"], str(key["keycode"])
        base_name = os_overrides.get("windows", {}).get(code, name)
        mac_name = os_overrides.get("mac", {}).get(code, name)
        variants = {"base": by_name[base_name]}
        if mac_name != base_name:
            variants["mac"] = by_name[mac_name]
        entries.append({"keycode": key["keycode"], "name": name, "variants": variants})
    assert len({entry["keycode"] for entry in entries}) == len(entries)
    metadata = {"atlas": "hotkeys.png", "width": atlas.width, "height": atlas.height,
                "origin": "bottom-left", "keys": entries}
    output.mkdir(parents=True, exist_ok=True)
    atlas.save(output / metadata["atlas"])
    (output / "hotkeys.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    for filename in legacy_pngs:
        (output / filename).unlink(missing_ok=True)
    return metadata

