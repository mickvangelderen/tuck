"""Generate the clean asset set from inputs beside the generator project."""
from pathlib import Path
import argparse
from .generator import generate

HOTKEYS = Path(__file__).resolve().parents[3]
DEFAULT_OUTPUT = HOTKEYS.parents[1] / "assets/hotkeys"


def main():
    parser = argparse.ArgumentParser(description="Generate a hotkey PNG atlas and hotkeys.json from local font inputs.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--keycap", type=Path, default=HOTKEYS / "keyboard-key.png")
    parser.add_argument("--scale", type=int, choices=(1, 2), default=2)
    parser.add_argument("--font-size", type=int, default=13, help="Base logical text size; default 13.")
    parser.add_argument("--icon-size", type=int, default=8, help="Base logical icon height; default 8.")
    parser.add_argument("--text-treatment", choices=("flat", "inset"), default="inset")
    args = parser.parse_args()
    generate(args.keycap, HOTKEYS / "fonts/noto-sans-medium/NotoSans-Medium.ttf", HOTKEYS / "fonts/phosphor",
             HOTKEYS / "unity-keycodes.json", args.output, args.font_size, args.icon_size,
             args.text_treatment, args.scale)


if __name__ == "__main__":
    main()
