"""ANSI-inspired keycaps with compact modifiers; one unit is 19 pixels."""
from math import floor

ANSI_UNITS = {
    "Backspace": 1.25,
    "Tab": 1.5,
    "CapsLock": 1.75,
    "Return": 1.25,
    "KeypadEnter": 1.25,
    "LeftShift": 1.25,
    "RightShift": 1.25,
    "LeftControl": 1.25,
    "RightControl": 1.25,
    "LeftAlt": 1.25,
    "RightAlt": 1.25,
    "LeftWindows": 1.25,
    "RightWindows": 1.25,
    "LeftCommand": 1.25,
    "RightCommand": 1.25,
    "Menu": 1.25,
    "AltGr": 1.25,
    "Space": 1.75,
    "Keypad0": 2.0,
    "Backslash": 1.5,
    "Pipe": 1.5,
}

ANSI_HEIGHTS = {"KeypadPlus": 2.0}

# Size, anchor, horizontal offset, vertical offset; all in 1x logical pixels.
# Center uses the flat face, baseline uses y=13. No per-label auto-shrinking.
TEXT_DEFAULT = (13, "baseline", 0, 0)
ICON_DEFAULT = (8, "center", 0, -0.5)
LEGEND_LAYOUTS = {
    **{f"F{i}": (8, "center", 0, 0) for i in range(1, 16)},
    **{f"Mouse{i}": (10, "center", 0, 0) for i in range(7)},
    **{f"Keypad{i}": (13, "center", 0, 0) for i in range(10)},
    "Pause": (5, "center", 0, 0),
    "Home": (5, "center", 0, 0), "End": (8, "center", 0, 0),
    "Clear": (9, "center", 0, 0), "Escape": (9, "center", 0, 0),
    "Delete": (9, "center", 0, 0), "Insert": (9, "center", 0, 0),
    "PageUp": (5, "center", 0, 0), "PageDown": (5, "center", 0, 0),
    "Numlock": (6, "center", 0, 0), "ScrollLock": (6, "center", 0, 0),
    "Print": (6, "center", 0, 0), "SysReq": (5, "center", 0, 0),
    "Break": (5, "center", 0, 0), "Help": (6, "center", 0, 0),
    "LeftControl": (10, "center", 0, 0), "RightControl": (10, "center", 0, 0),
    "LeftAlt": (10, "center", 0, 0), "RightAlt": (10, "center", 0, 0),
    "AltGr": (7, "center", 0, 0),
    "Space": (10, "center", 0, 0),
    "J": (13, "baseline", 0, -1), "Q": (13, "baseline", 0, -1),
    "LeftParen": (13, "baseline", 0, -1), "RightParen": (13, "baseline", 0, -1),
    "LeftBracket": (13, "baseline", 0, -1), "RightBracket": (13, "baseline", 0, -1),
    "LeftCurlyBracket": (13, "baseline", 0, -1), "RightCurlyBracket": (13, "baseline", 0, -1),
    "Pipe": (12, "center", 0, 0), "Underscore": (13, "baseline", 0, -1),
    "KeypadPeriod": (13, "baseline", 0, 0),
    "KeypadDivide": (13, "center", 0, 0), "KeypadMultiply": (13, "center", 0, 0),
    "KeypadMinus": (13, "center", 0, 0), "KeypadPlus": (13, "center", 0, 0),
    "KeypadEquals": (13, "center", 0, 0),
}


def width_for_key(name: str) -> int:
    # These are isolated UI keycaps: proportions are rounded to whole pixels,
    # without incorporating the physical keyboard's inter-key gaps.
    return floor(19 * ANSI_UNITS.get(name, 1.0) + 0.5)


def height_for_key(name: str) -> int:
    return floor(19 * ANSI_HEIGHTS.get(name, 1.0) + 0.5)
