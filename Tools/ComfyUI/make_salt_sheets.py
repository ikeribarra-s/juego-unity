"""
make_salt_sheets.py - pixel-art salt sheets (Assets/_Game/Rendering/VFX/Salt/), same house rules as make_water_sheets.py:
real coverage alpha (0 or 255), no anti-aliasing, fixed cool-white ramp, ordered dither.

  C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe -I Tools/ComfyUI/make_salt_sheets.py \\
      --out Assets/_Game/Rendering/VFX/Salt [--preview <png>] [--seed 11]

  SaltPile_Sheet.png   4x2 x 16 px  top-down piles of salt lying on the floor (8 variants, floor decals)
  SaltGrain_Sheet.png  4x1 x  8 px  single falling grains (particles, random frame)
  SaltPuff_Sheet.png   4x4 x 16 px  dust puff when salt hits the floor (frame 0 first; rises, spreads, dithers out)
"""
import argparse, math, os, random
from PIL import Image

S0 = (0x7E, 0x88, 0x98, 255)   # shadow side / outline
S1 = (0xA9, 0xB2, 0xC0, 255)
S2 = (0xD0, 0xD6, 0xDE, 255)
S3 = (0xEC, 0xEF, 0xF3, 255)
S4 = (0xFF, 0xFF, 0xFF, 255)   # sparkle
RAMP = [S0, S1, S2, S3, S4]

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def dither(x, y, v):
    """Ordered-dither a 0..1 value onto the ramp index (never partial alpha)."""
    t = v * (len(RAMP) - 1)
    lo = int(math.floor(t))
    frac = t - lo
    hi = min(lo + 1, len(RAMP) - 1)
    return RAMP[hi if frac * 16 > BAYER[y % 4][x % 4] else lo]


def sheet(cols, rows, cell, fn):
    img = Image.new("RGBA", (cols * cell, rows * cell), (0, 0, 0, 0))
    px = img.load()
    for f in range(cols * rows):
        ox, oy = (f % cols) * cell, (f // cols) * cell
        for (x, y), c in fn(f).items():
            if 0 <= x < cell and 0 <= y < cell:
                px[ox + x, oy + y] = c
    return img


def pile(seed, cell=16):
    """A lumpy mound seen from above, lit from the upper left, a few stray grains around it."""
    rng = random.Random(seed)
    px = {}
    c = (cell - 1) / 2.0
    base = rng.uniform(4.6, 6.4)
    stretch = rng.uniform(0.75, 1.25)
    phase = [rng.uniform(0, math.tau) for _ in range(3)]
    amp = [rng.uniform(0.3, 0.9), rng.uniform(0.2, 0.6), rng.uniform(0.1, 0.4)]
    rot = rng.uniform(0, math.pi)
    for y in range(cell):
        for x in range(cell):
            dx, dy = x - c, y - c
            a = math.atan2(dy, dx) + rot
            r = base + sum(amp[k] * math.sin((k + 2) * a + phase[k]) for k in range(3))
            d = math.hypot(dx * stretch, dy / stretch)
            if d > r:
                continue
            h = 1.0 - d / r                    # 0 at the rim, 1 at the crest
            light = 0.5 + (-dx - dy) / (2.0 * r)   # upper left brighter
            v = 0.25 + 0.55 * h + 0.35 * (light - 0.5)
            v += (rng.random() - 0.5) * 0.18   # grainy surface
            px[(x, y)] = dither(x, y, max(0.0, min(1.0, v)))
    # rim shadow: lower-right edge pixels one step darker
    for (x, y) in list(px):
        if (x + 1, y + 1) not in px or (x + 1, y) not in px and (x, y + 1) not in px:
            px[(x, y)] = S0 if rng.random() < 0.7 else S1
    # stray grains
    for _ in range(rng.randint(4, 8)):
        a = rng.uniform(0, math.tau)
        d = base + rng.uniform(1.0, 2.6)
        x, y = int(round(c + math.cos(a) * d)), int(round(c + math.sin(a) * d))
        if 0 <= x < cell and 0 <= y < cell and (x, y) not in px:
            px[(x, y)] = rng.choice([S1, S2, S3, S4])
    return px


def grain(f):
    """4 falling grain shapes on an 8 px cell."""
    px = {}
    if f == 0:
        px = {(3, 3): S3, (4, 3): S4, (3, 4): S2, (4, 4): S3}
    elif f == 1:
        px = {(3, 2): S4, (3, 3): S3, (4, 3): S3, (3, 4): S2}
    elif f == 2:
        px = {(2, 3): S2, (3, 3): S4, (4, 3): S3, (3, 4): S1}
    else:
        px = {(3, 3): S4, (4, 3): S2, (4, 4): S1}
    return px


def puff(f, cell=16):
    """Dust puff: a dithered speck cloud that spreads, lifts and thins out over 16 frames."""
    rng = random.Random(1000 + f)
    t = f / 15.0
    c = (cell - 1) / 2.0
    r = 2.0 + 5.2 * (1 - (1 - t) ** 2)
    cy = c + 2.0 - 2.5 * t           # rises a little
    density = max(0.0, 1.0 - t * 1.05)
    px = {}
    for y in range(cell):
        for x in range(cell):
            dx, dy = x - c, (y - cy) * 1.25
            d = math.hypot(dx, dy)
            if d > r:
                continue
            edge = d / r
            keep = density * (1.0 - 0.55 * edge) + (rng.random() - 0.5) * 0.25
            if BAYER[y % 4][x % 4] / 16.0 > keep:
                continue
            v = 0.9 - 0.55 * edge - 0.2 * t + (-dx - dy) * 0.02
            px[(x, y)] = dither(x, y, max(0.0, min(1.0, v + (rng.random() - 0.5) * 0.15)))
    return px


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview")
    ap.add_argument("--seed", type=int, default=11)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    piles = sheet(4, 2, 16, lambda f: pile(a.seed * 100 + f))
    grains = sheet(4, 1, 8, grain)
    puffs = sheet(4, 4, 16, puff)
    piles.save(os.path.join(a.out, "SaltPile_Sheet.png"))
    grains.save(os.path.join(a.out, "SaltGrain_Sheet.png"))
    puffs.save(os.path.join(a.out, "SaltPuff_Sheet.png"))
    if a.preview:
        bg = Image.new("RGBA", (64 * 6 + 20, 64 * 4 + 20), (38, 38, 42, 255))
        for img, pos, k in ((piles, (5, 5), 6), (puffs, (5, 64 * 2 + 15), 3), (grains, (5, 64 * 2 + 5 + 64 * 2 - 100), 6)):
            big = img.resize((img.width * k, img.height * k), Image.NEAREST)
            bg.alpha_composite(big, pos)
        bg.save(a.preview)
    print("wrote sheets to", a.out)


if __name__ == "__main__":
    main()
