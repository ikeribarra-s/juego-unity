r"""
make_death_sheets.py - procedural pixel-art sheets for the death FX (Docs/Design/DeathFX.md section 5).
No ComfyUI needed.  Alpha is 0 or 255 (ordered dither), never anti-aliased.

  C:\Users\redux\ComfyUI\.venv\Scripts\python.exe -I Tools/ComfyUI/make_death_sheets.py \
      --out Assets/_Game/Rendering/VFX/Death [--preview <png>] [--seed 7]

  BoneDust_Sheet.png   4x4 x 16 px  grey-beige puff: grow, peak with flecks, dither out
  BoneChip_Sheet.png   4x4 x  8 px  tumbling bone chip (4 poses), last 4 frames dither out
  SkullWisp_Sheet.png  4x4 x 16 px  pale teal wisp (additive in the material, HDR x1.4)
"""
import argparse, math, os, random
from PIL import Image

G = 4
OUT = (0x0D, 0x0F, 0x14, 255)
SHADOW = (0x4A, 0x4A, 0x44, 255)
BONE = (0x8A, 0x84, 0x74, 255)
HI = (0xC9, 0xC1, 0xA8, 255)
T0 = (0x2E, 0x6F, 0x66, 255)
T1 = (0x5F, 0xB8, 0xA6, 255)
T2 = (0x7F, 0xD6, 0xC2, 255)
T3 = (0xD8, 0xFF, 0xF2, 255)
BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def drop(x, y, amount):
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


def dust(seed):
    rng = random.Random(seed)
    flecks = [(rng.uniform(0, 6.28), rng.uniform(0.7, 1.4)) for _ in range(6)]

    def fn(f, t):
        d = {}
        cx = cy = 7.5
        if f < 4:
            r = 1.0 + f * 1.7           # 1 -> 6 px
        elif f < 11:
            r = 6.0 + 0.25 * math.sin((f - 4) * 0.9)
        else:
            r = 6.0
        fade = max(0.0, (f - 10) / 5.0) if f >= 11 else 0.0
        for y in range(16):
            for x in range(16):
                dx, dy = x - cx, (y - cy) * 1.1
                n = math.sin(x * 1.7 + y * 0.9 + f * 0.4) * 0.6
                dd = math.hypot(dx, dy) + n
                if dd > r or (f >= 11 and drop(x, y, fade)):
                    continue
                edge = dd > r - 1.1
                shade = (dx * 0.5 + dy * 0.8) / max(r, 1)       # lit from top-left
                c = OUT if edge and r > 2.5 else (HI if shade < -0.45 else (SHADOW if shade > 0.35 else BONE))
                d[(x, y)] = c
        if 4 <= f <= 12:
            for a, s in flecks[: 2 + (f % 2)]:
                rr = r + (f - 3) * 0.5 * s
                x, y = int(cx + math.cos(a) * rr), int(cy + math.sin(a) * rr * 0.9)
                if not (f >= 11 and drop(x, y, fade)):
                    d[(x, y)] = BONE
        return d
    return sheet(16, fn)


CHIPS = [  # 4 rotation poses, 8 px cell
    [(3, 3), (4, 3), (3, 4), (4, 4), (5, 4)],
    [(3, 2), (3, 3), (4, 3), (4, 4), (4, 5)],
    [(2, 4), (3, 4), (4, 4), (4, 3), (5, 3)],
    [(4, 2), (4, 3), (3, 4), (4, 4), (3, 5)],
]


def chips(seed):
    def fn(f, t):
        pose = CHIPS[f % 4]
        fade = max(0.0, (f - 11) / 5.0) if f >= 12 else 0.0
        d = {}
        for i, (x, y) in enumerate(pose):
            if f >= 12 and drop(x, y, fade):
                continue
            d[(x, y)] = HI if i == 0 else (SHADOW if i == len(pose) - 1 else BONE)
        return d
    return sheet(8, fn)


def wisp(seed):
    def fn(f, t):
        d = {}
        ph = f / 16.0 * 2 * math.pi
        hx = 8 + 1.6 * math.sin(ph)
        hy = 5 + 1.0 * math.cos(ph)
        # tail trails down-left with a slight loop
        for i in range(1, 8):
            tt = i / 8.0
            x = hx - i * 0.9 + 1.2 * math.sin(ph - i * 0.5)
            y = hy + i * 0.95
            c = T1 if i < 3 else (T0 if i < 6 else None)
            if c is None and drop(int(x), int(y), 0.5):
                continue
            d[(int(round(x)), int(round(y)))] = c or T0
            if i < 4:
                d[(int(round(x)) + 1, int(round(y)))] = T0
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if abs(dx) + abs(dy) < 2:
                    d[(int(round(hx)) + dx, int(round(hy)) + dy)] = T2
        d[(int(round(hx)), int(round(hy)))] = T3
        return d
    return sheet(16, fn)


def preview(imgs, path, scale=6):
    w = sum(i.width for i in imgs) + 8 * (len(imgs) + 1)
    h = max(i.height for i in imgs) + 16
    bg = Image.new("RGBA", (w, h), (26, 26, 34, 255))
    x = 8
    for i in imgs:
        bg.alpha_composite(i, (x, 8))
        x += i.width + 8
    bg = bg.resize((w * scale, h * scale), Image.NEAREST)
    bg.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview")
    ap.add_argument("--seed", type=int, default=7)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    imgs = [dust(a.seed), chips(a.seed), wisp(a.seed)]
    for name, im in zip(("BoneDust", "BoneChip", "SkullWisp"), imgs):
        im.save(os.path.join(a.out, f"{name}_Sheet.png"))
        print("wrote", name)
    if a.preview:
        preview(imgs, a.preview)


if __name__ == "__main__":
    main()
