"""
make_water_sheets.py - pixel-art water particle sheets (Assets/_Game/Rendering/VFX/WaterSplash/).
Spec: Docs/Design/WaterSplashFX.md.  Sibling of make_ember_sheets.py, but ALPHA blended:
real coverage alpha (0 or 255), opaque dark outline, murky cold palette, no anti-aliasing.

  C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe -I Tools/ComfyUI/make_water_sheets.py \\
      --out Assets/_Game/Rendering/VFX/WaterSplash [--splash-src Tools/ComfyUI/sources/water_splash/splash_02.png] \\
      [--preview <png>] [--seed 7]

  WaterSplash_Sheet.png  4x4 x 16 px  crown splash (rise / crown / fall)
  WaterRipple_Sheet.png  4x4 x 16 px  flat ring that grows, thins, dithers out
  WaterDrop_Sheet.png    4x4 x  8 px  teardrop falls, splat, gone
Without --splash-src the crown rim profile is procedural; with it, the rim profile (column
heights of the SDXL crown silhouette) shapes the crown tips. Frames are row-major, frame 0 first.
"""
import argparse, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I strips the script dir
from PIL import Image

G = 4
OUT = (0x0D, 0x0F, 0x14, 255)    # outline / shadow
DEEP = (0x1E, 0x3A, 0x3F, 255)   # deep teal
MID = (0x3B, 0x5A, 0x5E, 255)    # murky water
LIGHT = (0x6E, 0x8A, 0x8C, 255)  # grey-blue
FOAM = (0xB9, 0xCB, 0xCB, 255)   # foam (crests only)

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def bayer_drop(x, y, amount):
    """True = drop this pixel. amount 0..1, ordered dither, never partial alpha."""
    return BAYER[y % 4][x % 4] < amount * 16


def sheet(cell, fn):
    img = Image.new("RGBA", (cell * G, cell * G), (0, 0, 0, 0))
    px = img.load()
    for f in range(G * G):
        ox, oy = (f % G) * cell, (f // G) * cell
        for (x, y), c in fn(f, f / (G * G - 1)).items():
            if 0 <= x < cell and 0 <= y < cell:
                px[ox + x, oy + y] = c
    return img


# ---------------------------------------------------------------- splash
def rim_profile(srcs, rng, n=33):
    """Crown tip profile u in [-1,1] -> 0..1 (1 = tallest tip). From an SDXL silhouette if given."""
    if srcs:
        import make_ember_sheets as me
        lum = me.extract(srcs[0], count=1, thresh=60)[0].convert("L")
        w, h = lum.size
        px = lum.load()
        tops = []
        for x in range(w):
            t = next((y for y in range(h) if px[x, y] > 60), None)
            tops.append(h - t if t is not None else 0)
        lo, hi = min(tops), max(tops)
        span = max(1, hi - lo)
        return [(tops[min(w - 1, int(i / (n - 1) * (w - 1)))] - lo) / span for i in range(n)]
    ph = rng.random() * 6.28
    return [0.5 + 0.5 * (0.7 * abs(math.cos(2.5 * math.pi * (i / (n - 1) * 2 - 1))) + 0.3 * math.sin(i * 1.7 + ph) ** 2)
            for i in range(n)]


def make_splash(profile, rng):
    """Open crown: 5 separate tapering spikes (no body, no outline) that shoot up and fan out, then
    detach their tips into droplets that arc and fall. Bottom row of the cell = water surface."""
    BY = 15
    # (x offset from centre, max height, lean px per row) - uneven, fanned outward
    spikes = [(-5.0, 7, -0.34), (-1.8, 12, -0.10), (1.6, 10, 0.12), (4.8, 6, 0.36)]
    grow = [0.18, 0.28, 0.40, 0.65, 0.88, 1.0, 1.0, 0.92, 0.72, 0.50, 0.32, 0.18, 0, 0, 0, 0]
    # tip droplets: per spike two droplets (vx, vy) launched at detach frame 8
    drops = []
    for i, (ox, H, lean) in enumerate(spikes):
        tx = 7.5 + ox + lean * H * 0.72
        ty = BY - H * 0.72
        for j in range(2):
            vx = lean * 2.2 + (j - 0.5) * 0.9 + rng.uniform(-0.15, 0.15)
            vy = 1.0 + rng.uniform(0.0, 0.9) + (0.5 if j == 0 else 0.0)
            drops.append((tx, ty, vx, vy, 8 + j))

    def frame(f, t):
        d = {}
        g = grow[f]
        if g > 0:
            for i, (ox, H, lean) in enumerate(spikes):
                h = int(round(H * g))
                if f < 3:
                    h = min(h, 3 if i in (1, 2) else 0)   # small low burst, centre spikes only
                if h < 1:
                    continue
                for y in range(h):
                    cx = 7.5 + ox + lean * y
                    wide = y < max(2, h * 0.5) and h >= 4
                    x0 = int(round(cx - (0.5 if wide else 0.0)))
                    row = BY - y
                    cols = [x0, x0 + 1] if wide else [x0]
                    for k, x in enumerate(cols):
                        col = MID if y > 0 or not wide else MID
                        if wide and k == 0 and y > 0:
                            col = DEEP                      # 1 px shade on the left side
                        elif wide and k == 1 and y > 1:
                            col = LIGHT
                        elif not wide:
                            col = MID if (y + i) % 3 else LIGHT
                        d[(x, row)] = col
                if h >= 4 and 3 <= f <= 8:
                    ty = BY - h + 1
                    tx = int(round(7.5 + ox + lean * (h - 1)))
                    d[(tx, ty)] = FOAM                        # foam only on the tips
        # base strip (1 px, only early)
        if f <= 6:
            w = 1 + f // 3
            for x in range(8 - w - 1, 8 + w + 1):
                d.setdefault((x, BY), DEEP)
        # falling droplets
        for (tx, ty, vx, vy, f0) in drops:
            k = f - f0
            if k < 0:
                continue
            x = tx + vx * k
            y = ty - vy * k + 0.5 * k * k * 0.9
            if y > BY or x < 0 or x > 15 or y < 0:
                continue
            size = 2 if k < 2 else 1
            for ix in range(size):
                for iy in range(size):
                    xx, yy = int(x) + ix, int(y) + iy
                    if f >= 14 and bayer_drop(xx, yy, 0.55):
                        continue
                    d.setdefault((xx, yy), LIGHT if size == 1 else MID)
            if size == 2:
                d[(int(x), int(y))] = FOAM if k == 0 else LIGHT
        # settling blip at the base
        blip = {12: [7, 8, 9], 13: [7, 8], 14: [8]}.get(f)
        if blip:
            for x in blip:
                d[(x, BY)] = MID
        return d
    return frame


# ---------------------------------------------------------------- ripple
def make_ripple():
    def frame(f, t):
        r = 2.0 + 5.2 * (t ** 0.8)
        th = 2.0 - 1.0 * min(1.0, t * 1.6)
        d = {}
        for y in range(16):
            for x in range(16):
                dx, dy = x + 0.5 - 8, y + 0.5 - 8
                dist = math.hypot(dx, dy)
                if r - th < dist <= r + 0.15:
                    col = MID if dist < r - th * 0.45 else LIGHT
                    if th < 1.4:
                        col = MID if (x + y) % 3 else LIGHT
                    if dx + dy < -r * 0.6 and f <= 12:  # foam arc, upper-left
                        col = FOAM
                    d[(x, y)] = col
        if f >= 12:
            amount = (f - 11) / 5.0
            d = {k: v for k, v in d.items() if not bayer_drop(k[0], k[1], amount)}
        return d
    return frame


# ---------------------------------------------------------------- drop
def make_drop():
    def frame(f, t):
        d = {}
        if f <= 8:
            y = int(round(f * 0.45))
            tall = 3 if f < 6 else 4
            for yy in range(tall):
                for xx in (3, 4):
                    d[(xx, y + yy)] = MID
            d[(3, y + tall - 1)] = DEEP
            d[(4, y + tall - 1)] = DEEP
            d[(3, y)] = FOAM  # foam pixel top-left
        elif f <= 12:
            k, row = f - 9, 6
            if k == 0:
                for x in (2, 3, 4, 5):
                    d[(x, row)] = MID
                d[(3, row - 1)] = LIGHT
            elif k == 1:
                for x in (2, 3, 4):
                    d[(x, row)] = MID
                d[(2, row)] = LIGHT
            elif k == 2:
                for x in (1, 3, 5):
                    d[(x, row)] = MID
            else:
                d[(2, row)] = DEEP
                d[(5, row)] = DEEP
        return d
    return frame


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--splash-src", nargs="*", default=[])
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--preview")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rng = random.Random(a.seed)
    profile = rim_profile(a.splash_src, rng)
    sheets = []
    for name, cell, fn in (("WaterSplash", 16, make_splash(profile, rng)),
                           ("WaterRipple", 16, make_ripple()),
                           ("WaterDrop", 8, make_drop())):
        img = sheet(cell, fn)
        p = os.path.join(a.out, f"{name}_Sheet.png")
        img.save(p)
        print(p)
        sheets.append(img)
    if a.preview:
        W = sum(s.size[0] for s in sheets) + 4 * (len(sheets) - 1)
        pv = Image.new("RGBA", (W, 64), (40, 40, 44, 255))
        x = 0
        for s in sheets:
            pv.alpha_composite(s, (x, 0))
            x += s.size[0] + 4
        pv.resize((pv.size[0] * 8, pv.size[1] * 8), Image.NEAREST).save(a.preview)


if __name__ == "__main__":
    main()
