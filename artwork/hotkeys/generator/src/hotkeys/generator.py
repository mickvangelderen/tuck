"""Complete, explicit mapping of the game's UnityEngine.KeyCode enum."""
from pathlib import Path
import json
import re
from math import floor
from PIL import Image
from .export import export_assets
from .layout import (ANSI_UNITS, ANSI_HEIGHTS, width_for_key, height_for_key,
                     TEXT_DEFAULT, ICON_DEFAULT, LEGEND_LAYOUTS)
from .render import TextFont, INK, nine_slice, inset_edges, icon_mask

# Names/codepoints from the pinned Phosphor 2.1.2 font's own stylesheet.
ICONS = {
    "Backspace": ("backspace", 0xE0AE),
    "Return": ("arrow-elbow-down-left", 0xE044),
    "KeypadEnter": ("arrow-elbow-down-left", 0xE044),
    "Tab": ("arrow-line-right", 0xE064),
    "CapsLock": ("arrow-fat-line-up", 0xE522),
    "LeftShift": ("arrow-fat-up", 0xE52E),
    "RightShift": ("arrow-fat-up", 0xE52E),
    "LeftWindows": ("windows-logo", 0xE692),
    "RightWindows": ("windows-logo", 0xE692),
    "LeftCommand": ("command", 0xE1C4), "RightCommand": ("command", 0xE1C4),
    "UpArrow": ("arrow-up", 0xE08E), "DownArrow": ("arrow-down", 0xE03E),
    "LeftArrow": ("arrow-left", 0xE058), "RightArrow": ("arrow-right", 0xE06C),
    "Menu": ("list", 0xE2F0),
}

# Variants live beside their base sprite; unsuffixed names are shared defaults.
OS_VARIANTS = {
    "LeftControl_mac": {"base": "LeftControl", "os": "mac", "icon": ("control", 0xECA6)},
    "RightControl_mac": {"base": "RightControl", "os": "mac", "icon": ("control", 0xECA6)},
    "LeftAlt_mac": {"base": "LeftAlt", "os": "mac", "icon": ("option", 0xE8A8)},
    "RightAlt_mac": {"base": "RightAlt", "os": "mac", "icon": ("option", 0xE8A8)},
}

# Long legends use familiar abbreviations or two lines to fit compact keycaps.
TEXT = {
    "Delete": "Del", "Clear": "Clr", "Pause": "Pause", "Escape": "Esc",
    "Space": "Space", "Insert": "Ins", "Home": "Home", "End": "End",
    "PageUp": "Pg\nUp", "PageDown": "Pg\nDn", "Numlock": "Num\nLk",
    "ScrollLock": "Scr\nLk", "LeftControl": "Ctrl", "RightControl": "Ctrl",
    "LeftAlt": "Alt", "RightAlt": "Alt", "AltGr": "AltGr", "Help": "Help",
    "Print": "Prt\nSc", "SysReq": "Sys\nRq", "Break": "Break",
    "KeypadPeriod": ".", "KeypadDivide": "/", "KeypadMultiply": "*",
    "KeypadMinus": "-", "KeypadPlus": "+", "KeypadEquals": "=",
}


def legend(name: str, code: int):
    if name in ICONS or name in OS_VARIANTS:
        return "icon", name, "keyboard"
    if name in TEXT:
        return "text", TEXT[name], "keyboard"
    if name.startswith("Mouse"):
        return "text", f"M{int(name[5:]) + 1}", "mouse"
    if name.startswith("Keypad") and name[6:].isdigit():
        return "text", name[6:], "keyboard"
    if re.fullmatch(r"F\d+", name):
        return "text", name, "keyboard"
    if 33 <= code <= 126:
        return "text", chr(code).upper(), "keyboard"
    raise ValueError(f"No deliberate legend for KeyCode.{name} ({code}).")


def text_mask(font: TextFont, label: str, size: int, pixel_scale: int = 1):
    rendered = [font.label(line, size) for line in label.split("\n")]
    w = max(mask.width for mask, _ in rendered)
    h = sum(mask.height for mask, _ in rendered) + (len(rendered)-1)*pixel_scale
    combined, y = Image.new("L", (w, h)), 0
    for mask, _ in rendered:
        combined.paste(mask, ((w-mask.width)//2, y))
        y += mask.height + pixel_scale
    return combined, rendered[0][1]


def render_entry(base, font, icon_directory, name, code, font_size, icon_size, treatment, pixel_scale=1):
    kind, label, category = legend(name, code)
    variant = OS_VARIANTS.get(name)
    base_name = variant["base"] if variant else name
    width, height = width_for_key(base_name), height_for_key(base_name)
    key = nine_slice(base, width, height)
    width, height = width*pixel_scale, height*pixel_scale
    key = key.resize((width, height), Image.Resampling.NEAREST)
    original_alpha = key.getchannel("A").tobytes()
    layout = {"category": category, "legend_kind": kind,
              "width_units": ANSI_UNITS.get(base_name, 1.0),
              "height_units": ANSI_HEIGHTS.get(base_name, 1.0),
              "baseline": 13*pixel_scale, "text_treatment": treatment,
              "pixel_scale": pixel_scale, "base_name": base_name, "os": variant["os"] if variant else None}
    rule = LEGEND_LAYOUTS.get(name, ICON_DEFAULT if kind == "icon" else TEXT_DEFAULT)
    size, anchor, dx, dy = rule
    layout.update(legend_anchor=anchor, legend_offset=[dx, dy], logical_legend_size=size)
    mask = None
    if kind == "icon":
        icon_name, codepoint = variant["icon"] if variant else ICONS[name]
        path = icon_directory / ("Phosphor-Fill.ttf" if icon_name == "windows-logo" else "Phosphor.ttf")
        mask = icon_mask(path, codepoint, round(size*icon_size/8*pixel_scale))
        layout.update(icon=icon_name, icon_font=path.name, icon_codepoint=f"U+{codepoint:04X}")
    elif label:
        actual_size = round(size*font_size/13*pixel_scale)
        mask, baseline = text_mask(font, label, actual_size, pixel_scale)
        layout["font_size"] = actual_size
    if mask is not None:
        y = 13*pixel_scale-baseline if anchor == "baseline" else (height-2*pixel_scale-mask.height)/2
        origin = (floor((width-mask.width)/2+dx*pixel_scale), floor(y+dy*pixel_scale))
        x, y = origin
        if (x < 2*pixel_scale or y < 2*pixel_scale
                or x + mask.width > width-2*pixel_scale or y + mask.height > height-4*pixel_scale):
            raise ValueError(f"{name}: legend lies outside the flat keycap face; adjust its explicit LEGEND_LAYOUTS entry.")
        if treatment == "inset":
            inset_edges(key, mask, origin, pixel_scale)
        key.paste(Image.new("RGBA", mask.size, INK), origin, mask)
        layout.update(label_origin=list(origin), label_size=list(mask.size))
    else:
        layout.update(label_origin=None, label_size=[0, 0])
    assert key.getchannel("A").tobytes() == original_alpha, name
    return {"name": name, "label": label, "keycode": code, "image": key, "layout": layout}


def excluded_reason(name: str):
    if name == "None":
        return "No input bound; no visible key."
    if name.startswith("Joystick"):
        return "Joystick sprites are not generated."
    if name in ("LeftMeta", "RightMeta"):
        return "Generic Meta names omitted; use concrete Windows or Command keys."
    return None


def generate(base_path: Path, font_path: Path, icon_directory: Path,
             keycodes_path: Path, output: Path, font_size: int = 13,
             icon_size: int = 8, treatment: str = "inset", pixel_scale: int = 2):
    if pixel_scale not in (1, 2):
        raise ValueError("--scale must be 1 or 2.")
    if font_size < 1 or not 1 <= icon_size <= 10:
        raise ValueError("Font size must be positive; icon size must be between 1 and 10.")
    if treatment not in ("flat", "inset"):
        raise ValueError("Unknown text treatment.")
    source = json.loads(keycodes_path.read_text(encoding="utf-8-sig"))
    base, font = Image.open(base_path).convert("RGBA"), TextFont(font_path)
    by_code = {}
    for entry in source["entries"]:
        name, code = entry["name"], entry["value"]
        if excluded_reason(name) or code in by_code:
            continue
        by_code[code] = render_entry(base, font, icon_directory, name, code,
                                     font_size, icon_size, treatment, pixel_scale)
    keys = []
    for key in sorted(by_code.values(), key=lambda key: key["keycode"]):
        keys.append(key)
        for name, variant in OS_VARIANTS.items():
            if variant["base"] == key["name"]:
                keys.append(render_entry(base, font, icon_directory, name, key["keycode"],
                                         font_size, icon_size, treatment, pixel_scale))
    os_overrides = {
        "windows": {"309": "RightWindows", "310": "LeftWindows"},
        "mac": {str(key["keycode"]): key["name"] for key in keys if key["layout"]["os"] == "mac"},
    }
    metadata = export_assets(keys, output, os_overrides)
    print(f"Exported {len(metadata['keys'])} keycodes / {len(keys)} sprites "
          f"in a {metadata['width']}x{metadata['height']} RGBA atlas to {output}")
