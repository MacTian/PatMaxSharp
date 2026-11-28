"""Draw the ShapeAnchor icon.

The motif is a machine-vision fiducial: four corner brackets (the search region), a ring and a
crosshair (the located feature), and a solid centre mark (the pattern origin). Everything is
drawn per size at 8x and downsampled, so the 16 px variant stays legible instead of turning
into mush.
"""

from PIL import Image, ImageDraw

DARK_TOP = (27, 27, 29)      # #1B1B1D  window background
DARK_BOTTOM = (38, 38, 44)   # #26262C
EDGE = (62, 98, 118)         # muted steel, tile border
CYAN = (79, 195, 247)        # #4FC3F7  accent
CYAN_DIM = (44, 122, 156)
GREEN = (123, 216, 143)      # #7BD88F  accepted
WHITE = (232, 240, 246)


def rounded_tile(size: int) -> Image.Image:
    """Dark rounded square with a subtle vertical gradient and a thin steel edge."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    grad = Image.new("RGBA", (1, size))
    for y in range(size):
        t = y / max(1, size - 1)
        grad.putpixel((0, y), (
            round(DARK_TOP[0] + (DARK_BOTTOM[0] - DARK_TOP[0]) * t),
            round(DARK_TOP[1] + (DARK_BOTTOM[1] - DARK_TOP[1]) * t),
            round(DARK_TOP[2] + (DARK_BOTTOM[2] - DARK_TOP[2]) * t),
            255,
        ))
    grad = grad.resize((size, size))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    edge = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * 0.22),
        outline=EDGE, width=max(1, int(size * 0.012)))
    img.alpha_composite(edge)
    return img


def draw_icon(size: int) -> Image.Image:
    s = size * 8                     # supersample
    img = rounded_tile(s)
    d = ImageDraw.Draw(img)

    # ---- corner brackets: the search region ----
    inset = s * 0.155
    arm = s * 0.175
    w = max(2, int(s * 0.052))
    r = int(w * 0.5)
    lo, hi = inset, s - inset
    for x0, y0, dx, dy in ((lo, lo, 1, 1), (hi, lo, -1, 1), (lo, hi, 1, -1), (hi, hi, -1, -1)):
        d.line([(x0, y0), (x0 + dx * arm, y0)], fill=CYAN, width=w)
        d.line([(x0, y0), (x0, y0 + dy * arm)], fill=CYAN, width=w)
        # rounded caps so the strokes do not look cut at 16 px
        for cx, cy in ((x0 + dx * arm, y0), (x0, y0 + dy * arm), (x0, y0)):
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=CYAN)

    # ---- ring: the model's extent ----
    ring_r = s * 0.235
    ring_w = max(2, int(s * 0.043))
    cx = cy = s / 2
    d.ellipse([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
              outline=CYAN_DIM, width=ring_w)

    # ---- crosshair: the located feature ----
    tick = s * 0.115
    tw = max(2, int(s * 0.036))
    d.line([(cx - ring_r - tick * 0.35, cy), (cx - ring_r + tick, cy)], fill=CYAN, width=tw)
    d.line([(cx + ring_r - tick, cy), (cx + ring_r + tick * 0.35, cy)], fill=CYAN, width=tw)
    d.line([(cx, cy - ring_r - tick * 0.35), (cx, cy - ring_r + tick)], fill=CYAN, width=tw)
    d.line([(cx, cy + ring_r - tick), (cx, cy + ring_r + tick * 0.35)], fill=CYAN, width=tw)

    # ---- centre mark: the pattern origin ----
    dmd = s * 0.105
    d.polygon([(cx, cy - dmd), (cx + dmd, cy), (cx, cy + dmd), (cx - dmd, cy)], fill=GREEN)
    if size >= 64:
        core = s * 0.032
        d.polygon([(cx, cy - core), (cx + core, cy), (cx, cy + core), (cx - core, cy)], fill=WHITE)

    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [draw_icon(n) for n in sizes]

    master = draw_icon(256)
    master.save("patmax.png")
    master.save("patmax.ico", format="ICO", sizes=[(n, n) for n in sizes])
    for n, f in zip(sizes, frames):
        f.save(f"patmax_{n}.png")
    print("wrote patmax.ico  sizes:", sizes)


if __name__ == "__main__":
    main()
