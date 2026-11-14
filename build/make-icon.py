#!/usr/bin/env python3
"""Generate the Cleaner application icon (and a documentation preview PNG).

The artwork is drawn once at 1024x1024 and then down-sampled with a high
quality filter, so every icon size stays crisp.

Usage:
    python make-icon.py <output-dir>

Writes: app.ico, app.png
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

MASTER = 1024
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def mix(c1, c2, t):
    return tuple(int(round(lerp(c1[i], c2[i], t))) for i in range(3))


def rounded_gradient_tile(size: int) -> Image.Image:
    """Diagonal gradient inside a rounded square."""
    tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    grad = Image.new("RGB", (size, size))
    px = grad.load()
    top = (26, 148, 143)      # teal
    bottom = (16, 74, 104)    # deep blue
    for y in range(size):
        for x in range(size):
            t = (x / size * 0.45) + (y / size * 0.55)
            # add a soft radial highlight in the upper left
            dx = (x - size * 0.28) / size
            dy = (y - size * 0.16) / size
            glow = max(0.0, 1.0 - math.hypot(dx, dy) * 2.4) * 0.30
            base = mix(top, bottom, min(1.0, t))
            px[x, y] = mix((255, 255, 255), base, 1.0) if glow <= 0 else mix(base, (255, 255, 255), glow)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (0, 0, size - 1, size - 1), radius=int(size * 0.235), fill=255
    )
    tile.paste(grad, (0, 0), mask)
    return tile


def draw_ring(layer: Image.Image, box, width: int, start: float, end: float, color):
    ImageDraw.Draw(layer).arc(box, start=start, end=end, fill=color, width=width)
    # round the caps
    cx = (box[0] + box[2]) / 2
    cy = (box[1] + box[3]) / 2
    rx = (box[2] - box[0]) / 2
    ry = (box[3] - box[1]) / 2
    for angle in (start, end):
        rad = math.radians(angle)
        px = cx + rx * math.cos(rad)
        py = cy + ry * math.sin(rad)
        r = width / 2
        ImageDraw.Draw(layer).ellipse((px - r, py - r, px + r, py + r), fill=color)


def sparkle(draw: ImageDraw.ImageDraw, cx: float, cy: float, radius: float, color, waist: float = 0.22):
    """Four pointed star."""
    pts = []
    for i in range(8):
        angle = math.pi / 4 * i - math.pi / 2
        r = radius if i % 2 == 0 else radius * waist
        pts.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    draw.polygon(pts, fill=color)


def build_master() -> Image.Image:
    size = MASTER
    img = rounded_gradient_tile(size)
    draw = ImageDraw.Draw(img)

    # ---- soft inner border ------------------------------------------------
    draw.rounded_rectangle(
        (6, 6, size - 7, size - 7),
        radius=int(size * 0.228),
        outline=(255, 255, 255, 46),
        width=6,
    )

    # ---- disc ring ("C" shaped) ------------------------------------------
    # The ring is built as an alpha mask so the mouth of the "C" can be cut
    # with straight edges, then the whole glyph is rotated so the opening
    # faces the top-right corner where the sparkle lives.
    margin = int(size * 0.205)
    box = (margin, margin, size - margin, size - margin)
    ring_width = int(size * 0.102)
    cx = cy = size / 2.0
    ro = (size - 2 * margin) / 2.0
    ri = ro - ring_width

    ring_mask = Image.new("L", (size, size), 0)
    rm = ImageDraw.Draw(ring_mask)
    rm.ellipse(box, fill=255)
    rm.ellipse(
        (cx - ri, cy - ri, cx + ri, cy + ri),
        fill=0,
    )
    # straight-edged mouth, cut horizontally at the centre then rotated
    rm.rectangle(
        (cx + ri * 0.30, cy - ring_width * 0.5, size * 2, cy + ring_width * 0.5),
        fill=0,
    )
    ring_mask = ring_mask.rotate(-38, resample=Image.BICUBIC, center=(cx, cy))

    # soft drop shadow
    shadow_mask = ring_mask.point(lambda v: int(v * 0.55))
    shadow_mask = shadow_mask.transform(
        (size, size), Image.AFFINE, (1, 0, -size * 0.006, 0, 1, -size * 0.012),
        resample=Image.BICUBIC,
    )
    shadow_layer = Image.new("RGBA", (size, size), (0, 0, 0, 255))
    shadow_layer.putalpha(shadow_mask.filter(ImageFilter.GaussianBlur(size * 0.013)))
    img.alpha_composite(shadow_layer)

    white = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    img.paste(white, (0, 0), ring_mask)

    # ---- sparkle (the "cleaned" highlight) -------------------------------
    # Parked inside the opening of the "C" (which now points up-right).
    mouth_radius = (ri + ro) / 2.0
    angle = math.radians(-38)
    sparkle_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sparkle(
        ImageDraw.Draw(sparkle_layer),
        cx + mouth_radius * math.cos(angle) * 1.02,
        cy + mouth_radius * math.sin(angle) * 1.02,
        size * 0.115,
        (255, 204, 92, 255),
    )
    img.alpha_composite(sparkle_layer)

    small = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sparkle(
        ImageDraw.Draw(small),
        size * 0.862,
        size * 0.142,
        size * 0.050,
        (255, 233, 168, 220),
    )
    img.alpha_composite(small)

    return img


def main(argv: list[str]) -> int:
    out_dir = Path(argv[1]) if len(argv) > 1 else Path(".")
    out_dir.mkdir(parents=True, exist_ok=True)

    master = build_master()
    master.save(out_dir / "app.png")

    # Pillow renders every ICO frame by resizing this master.
    images = []
    for s in sorted(SIZES):
        # progressive halving keeps edges cleaner for very small sizes
        im = master
        cur = MASTER
        while cur // 2 >= s and cur > 64:
            im = im.resize((cur // 2, cur // 2), Image.LANCZOS)
            cur //= 2
        if cur != s:
            im = im.resize((s, s), Image.LANCZOS)
        images.append(im)

    biggest = images[-1]
    biggest.save(
        out_dir / "app.ico",
        format="ICO",
        sizes=[(s, s) for s in sorted(SIZES)],
        append_images=images[:-1],
    )
    print(f"wrote {out_dir / 'app.ico'} and {out_dir / 'app.png'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
