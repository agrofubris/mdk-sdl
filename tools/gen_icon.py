"""Generates the application icon: assets/icon.svg (the source), assets/icon.png (512 px, the
window's) and assets/icon.ico (16-256 px, the Windows executable's). Original artwork: a sniper
reticle over a glowing orb, dark teal and orange.

Both outputs come from the same shapes below; small sizes get thicker strokes to stay readable.
Needs Pillow and numpy. Usage: python tools/gen_icon.py
"""
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "assets")

# Design space: a 512 x 512 canvas.
CANVAS = 512
CENTRE = CANVAS / 2
PNG_SIZE = 512
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
# Samples per output pixel along each axis; the supersampled image stays <= 2048 px wide.
MAX_SAMPLES = 16
MAX_SUPERSAMPLED = 2048

# Rounded square behind everything.
TILE_INSET = 16
TILE_RADIUS = 104
TILE_RIM = 8
# The tile's gradient: its light's height (fraction of the centre's) and reach (of the canvas).
TILE_LIGHT_Y = 0.8
TILE_SPREAD = 0.62

# The orb, its highlight's offset (top left) and its gradient's reach.
ORB_RADIUS = 138
ORB_LIGHT = (-52, -58)
ORB_SPREAD = 1.5

# The reticle: a ring, four posts from its outside towards the centre, a centre dot.
RING_RADIUS = 186
RING_WIDTH = 18
POST_OUTER = 218
POST_INNER = 64
POST_WIDTH = 20
DOT_RADIUS = 13

# Icons this small or smaller drop the dot and keep the posts out of the orb's middle.
SMALL_ICON = 32
SMALL_POST_INNER = 92
# Dark outline around the reticle, so it reads over the orb.
HALO = 10

# A stroke never gets thinner than this many output pixels.
MIN_STROKE_PX = 1.6

TILE_LIGHT = "#14505a"
TILE_DARK = "#051317"
TILE_RIM_COLOUR = "#2a8c94"
ORB_HOT = "#ffd27a"
ORB_MID = "#ff7a1a"
ORB_DARK = "#6e1d00"
ORB_RIM = "#2a9a96"
RETICLE = "#5ff2e0"
HALO_COLOUR = "#04171b"


def rgb(hex_colour):
    return np.array([int(hex_colour[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float64) / 255


def lerp_stops(t, stops):
    """Colour at t (array, 0-1) along gradient stops [(offset, hex)]."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,))
    for (o0, c0), (o1, c1) in zip(stops, stops[1:]):
        k = np.clip((t - o0) / (o1 - o0), 0, 1)[..., None]
        inside = ((t >= o0) & (t <= o1))[..., None]
        out = np.where(inside, rgb(c0) * (1 - k) + rgb(c1) * k, out)
    return out


TILE_STOPS = [(0, TILE_LIGHT), (1, TILE_DARK)]
ORB_STOPS = [(0, ORB_HOT), (0.4, ORB_MID), (0.86, ORB_DARK), (1, ORB_RIM)]


def strokes(size):
    """Stroke widths in design units for an output size: thickened for small icons."""
    floor = MIN_STROKE_PX * CANVAS / size
    return {
        "ring": max(RING_WIDTH, floor),
        "post": max(POST_WIDTH, floor),
        "halo": max(HALO, floor * 0.5),
        "dot": max(DOT_RADIUS, floor * 0.9),
        "rim": max(TILE_RIM, floor * 0.6),
    }


def rounded_box(x, y, half, radius):
    """Signed distance to a centred rounded square."""
    qx = np.abs(x - CENTRE) - (half - radius)
    qy = np.abs(y - CENTRE) - (half - radius)
    outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0))
    return outside + np.minimum(np.maximum(qx, qy), 0) - radius


def posts(x, y, width, inner):
    """Signed distance to the four posts (union), from inner to POST_OUTER."""
    dx = np.abs(x - CENTRE)
    dy = np.abs(y - CENTRE)
    mid = (POST_OUTER + inner) / 2
    half_len = (POST_OUTER - inner) / 2

    # Horizontal pair: along x, thin in y; vertical pair: the same, transposed.
    horizontal = np.maximum(np.abs(dx - mid) - half_len, dy - width / 2)
    vertical = np.maximum(np.abs(dy - mid) - half_len, dx - width / 2)
    return np.minimum(horizontal, vertical)


def render(size):
    """The icon at size x size, RGBA."""
    ss = max(1, min(MAX_SAMPLES, MAX_SUPERSAMPLED // size))
    n = size * ss
    scale = CANVAS / n
    coords = (np.arange(n) + 0.5) * scale
    x, y = np.meshgrid(coords, coords)
    s = strokes(size)
    r = np.hypot(x - CENTRE, y - CENTRE)

    colour = np.zeros((n, n, 3))
    alpha = np.zeros((n, n))

    def paint(mask, fill):
        nonlocal colour, alpha
        m = mask[..., None]
        colour = np.where(m, fill, colour)
        alpha = np.where(mask, 1.0, alpha)

    # Tile: radial gradient from above the centre, a thin lighter rim. Edges on whole pixels.
    pixel = CANVAS / size
    half = CENTRE - round(TILE_INSET / pixel) * pixel
    tile = rounded_box(x, y, half, TILE_RADIUS)
    tile_t = np.hypot(x - CENTRE, y - CENTRE * TILE_LIGHT_Y) / (CANVAS * TILE_SPREAD)
    paint(tile <= 0, lerp_stops(tile_t, TILE_STOPS))
    paint((tile <= 0) & (tile > -s["rim"]), rgb(TILE_RIM_COLOUR))

    # Orb: lit from the top left.
    orb_t = np.hypot(x - CENTRE - ORB_LIGHT[0], y - CENTRE - ORB_LIGHT[1]) / (ORB_RADIUS * ORB_SPREAD)
    paint(r <= ORB_RADIUS, lerp_stops(orb_t, ORB_STOPS))

    # Reticle halo, then the reticle.
    ring = np.abs(r - RING_RADIUS) - s["ring"] / 2
    small = size <= SMALL_ICON
    post = posts(x, y, s["post"], SMALL_POST_INNER if small else POST_INNER)
    reticle = np.minimum(ring, post)
    if not small:
        reticle = np.minimum(reticle, r - s["dot"])

    paint((reticle <= s["halo"]) & (tile <= -s["rim"]), rgb(HALO_COLOUR))
    paint(reticle <= 0, rgb(RETICLE))

    # Box-filter the supersamples (premultiplied).
    rgba = np.concatenate([colour * alpha[..., None], alpha[..., None]], axis=2)
    rgba = rgba.reshape(size, ss, size, ss, 4).mean(axis=(1, 3))
    a = rgba[..., 3:4]
    rgba[..., :3] = np.where(a > 0, rgba[..., :3] / np.maximum(a, 1e-9), 0)
    return Image.fromarray(np.round(rgba * 255).astype(np.uint8), "RGBA")


def svg():
    """The same design as SVG (the 512 px strokes)."""
    half = CENTRE - TILE_INSET
    side = 2 * half
    c = CENTRE

    def post_lines(stroke, width, grow):
        """The four posts as lines, each end pushed out by grow (the halo)."""
        out = []
        inner, outer = POST_INNER - grow, POST_OUTER + grow
        for sx, sy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            out.append(f'    <line x1="{c + sx * inner:g}" y1="{c + sy * inner:g}" x2="{c + sx * outer:g}" y2="{c + sy * outer:g}" '
                       f'stroke="{stroke}" stroke-width="{width:g}"/>')
        return "\n".join(out)

    lx, ly = c + ORB_LIGHT[0], c + ORB_LIGHT[1]
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{CANVAS}" height="{CANVAS}" viewBox="0 0 {CANVAS} {CANVAS}">
  <!-- Generated by tools/gen_icon.py (the PNG and ICO come from the same shapes). -->
  <defs>
    <radialGradient id="tile" gradientUnits="userSpaceOnUse" cx="{c:g}" cy="{c * TILE_LIGHT_Y:g}" r="{CANVAS * TILE_SPREAD:g}">
      <stop offset="0" stop-color="{TILE_LIGHT}"/>
      <stop offset="1" stop-color="{TILE_DARK}"/>
    </radialGradient>
    <radialGradient id="orb" gradientUnits="userSpaceOnUse" cx="{lx:g}" cy="{ly:g}" r="{ORB_RADIUS * ORB_SPREAD:g}">
      <stop offset="0" stop-color="{ORB_HOT}"/>
      <stop offset="0.4" stop-color="{ORB_MID}"/>
      <stop offset="0.86" stop-color="{ORB_DARK}"/>
      <stop offset="1" stop-color="{ORB_RIM}"/>
    </radialGradient>
  </defs>
  <rect x="{TILE_INSET}" y="{TILE_INSET}" width="{side:g}" height="{side:g}" rx="{TILE_RADIUS}" fill="url(#tile)"/>
  <rect x="{TILE_INSET + TILE_RIM / 2:g}" y="{TILE_INSET + TILE_RIM / 2:g}" width="{side - TILE_RIM:g}" height="{side - TILE_RIM:g}" rx="{TILE_RADIUS - TILE_RIM / 2:g}" fill="none" stroke="{TILE_RIM_COLOUR}" stroke-width="{TILE_RIM}"/>
  <circle cx="{c:g}" cy="{c:g}" r="{ORB_RADIUS}" fill="url(#orb)"/>
  <g fill="none">
    <circle cx="{c:g}" cy="{c:g}" r="{RING_RADIUS}" stroke="{HALO_COLOUR}" stroke-width="{RING_WIDTH + 2 * HALO}"/>
{post_lines(HALO_COLOUR, POST_WIDTH + 2 * HALO, HALO)}
    <circle cx="{c:g}" cy="{c:g}" r="{DOT_RADIUS + HALO}" fill="{HALO_COLOUR}"/>
    <circle cx="{c:g}" cy="{c:g}" r="{RING_RADIUS}" stroke="{RETICLE}" stroke-width="{RING_WIDTH}"/>
{post_lines(RETICLE, POST_WIDTH, 0)}
    <circle cx="{c:g}" cy="{c:g}" r="{DOT_RADIUS}" fill="{RETICLE}"/>
  </g>
</svg>
'''


def main():
    os.makedirs(ASSETS, exist_ok=True)
    with open(os.path.join(ASSETS, "icon.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(svg())

    render(PNG_SIZE).save(os.path.join(ASSETS, "icon.png"), optimize=True)

    # Each ICO size is drawn on its own (not scaled down from the largest).
    images = [render(size) for size in ICO_SIZES]
    largest = images[-1]
    largest.save(os.path.join(ASSETS, "icon.ico"), sizes=[(s, s) for s in ICO_SIZES], append_images=images[:-1])


if __name__ == "__main__":
    main()
