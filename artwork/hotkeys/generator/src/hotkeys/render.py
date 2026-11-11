"""Nine-slice the keycap and render vector fonts with Pillow/FreeType."""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

NEAREST = Image.Resampling.NEAREST
INK = (52, 43, 59, 255)
INSET_SHADOW = (52, 43, 59, 255)
INSET_HIGHLIGHT = (196, 208, 196, 255)
INSET_OFFSET = 0.35
SHADOW_STRENGTH = 0.12
HIGHLIGHT_STRENGTH = 0.18


def nine_slice(sprite: Image.Image, width: int, height: int = 19) -> Image.Image:
    if sprite.size != (19, 19) or min(width, height) < 19:
        raise ValueError("Expected a 19x19 source with 9-pixel borders and an output at least 19x19.")
    out = Image.new("RGBA", (width, height))
    cuts, xs, ys = (0, 9, 10, 19), (0, 9, width - 9, width), (0, 9, height - 9, height)
    for row in range(3):
        for col in range(3):
            tile = sprite.crop((cuts[col], cuts[row], cuts[col+1], cuts[row+1]))
            out.paste(tile.resize((xs[col+1]-xs[col], ys[row+1]-ys[row]), NEAREST), (xs[col], ys[row]))
    return out


class TextFont:
    def __init__(self, path: Path):
        self.path = path

    def label(self, text: str, size: int) -> tuple[Image.Image, int]:
        """Return a tight antialiased ink mask and baseline relative to its top."""
        font = ImageFont.truetype(str(self.path), size)
        box = font.getbbox(text, anchor="ls")
        mask = Image.new("L", (box[2]-box[0], box[3]-box[1]))
        ImageDraw.Draw(mask).text((-box[0], -box[1]), text, font=font, anchor="ls", fill=255)
        bounds = mask.getbbox()
        if bounds is None:
            raise ValueError(f"Label {text!r} rendered no pixels.")
        return mask.crop(bounds), -box[1] - bounds[1]


def inset_edges(key: Image.Image, mask: Image.Image, origin: tuple[int, int], pixel_scale: int = 1):
    """A shallow recess: upper-left shadow and lower-right catching light."""
    ink = Image.new("L", key.size)
    ink.paste(mask, origin)
    # Constrain the treatment to the face so it cannot paint over the bevel.
    face = Image.new("L", key.size)
    ImageDraw.Draw(face).rectangle((2*pixel_scale, 2*pixel_scale,
                                    key.width-2*pixel_scale-1, key.height-4*pixel_scale-1), fill=255)
    face = ImageChops.multiply(face, key.getchannel("A"))
    for offset, color, strength in (
        ((-INSET_OFFSET, -INSET_OFFSET), INSET_SHADOW, SHADOW_STRENGTH),
        ((INSET_OFFSET, INSET_OFFSET), INSET_HIGHLIGHT, HIGHLIGHT_STRENGTH),
    ):
        # Pillow's affine transform maps output coordinates back to the source.
        # Fractional coverage gives a shallow rim without moving whole pixels.
        shifted = ink.transform(key.size, Image.Transform.AFFINE,
                                (1, 0, -offset[0]*pixel_scale, 0, 1, -offset[1]*pixel_scale),
                                resample=Image.Resampling.BILINEAR, fillcolor=0)
        # Only the exposed rim receives the treatment, not a whole duplicate
        # letter. This preserves narrow counters and the original letter weight.
        rim = ImageChops.subtract(shifted, ink)
        rim = ImageChops.multiply(rim, face).point(lambda v: round(v*strength))
        key.paste(Image.new("RGBA", key.size, color), (0, 0), rim)


def icon_mask(path: Path, codepoint: int, height: int) -> Image.Image:
    font = ImageFont.truetype(str(path), 128)
    glyph = chr(codepoint)
    if bytes(font.getmask(glyph)) == bytes(font.getmask(chr(0x10FFFF))):
        raise ValueError(f"{path.name} is missing U+{codepoint:04X}")
    box = font.getbbox(glyph)
    mask = Image.new("L", (box[2]-box[0], box[3]-box[1]))
    ImageDraw.Draw(mask).text((-box[0], -box[1]), glyph, font=font, fill=255)
    bounds = mask.getbbox()
    if bounds is None:
        raise ValueError(f"Empty glyph U+{codepoint:04X}")
    mask = mask.crop(bounds)
    # Keep icons at the same optical size on 1.25u through 2.75u keycaps.
    ratio = min((height*1.25)/mask.width, height/mask.height)
    mask = mask.resize((max(1, round(mask.width*ratio)), max(1, round(mask.height*ratio))), Image.Resampling.LANCZOS)
    return mask.point(lambda v: 0 if v < 12 else 255 if v > 243 else v)

