r"""
make_atmo_sheets.py - procedural pixel-art sprite sheets for the atmosphere particles
(Assets/_Game/Rendering/VFX/Atmosphere/). 4x4 grids, one particle's whole life row-major, Point/no mips.

  C:\Users\redux\ComfyUI\.venv\Scripts\python.exe -I Tools/ComfyUI/make_atmo_sheets.py --out Assets/_Game/Rendering/VFX/Atmosphere

  AtmoMote_Sheet.png   8 px cells : a speck that twinkles (additive-friendly, white on transparent)
  AtmoSmoke_Sheet.png  16 px cells: dithered smoke puff that grows, thins and dissolves (grey)
  AtmoSplash_Sheet.png 16 px cells: drip splash - crown of droplets + expanding ring (pale blue-white)
Deterministic (seeded).
"""
import argparse, math, os, random
from PIL import Image

G = 4


def sheet(cell, fn):
    img = Image.new("RGBA", (cell * G, cell * G), (0, 0, 0, 0))
    px = img.load()
    for f in range(G * G):
        ox, oy = (f % G) * cell, (f // G) * cell
        for (x, y, c) in fn(f, f / (G * G - 1)):
            if 0 <= x < cell and 0 <= y < cell:
                px[ox + x, oy + y] = c
    return img


def mote(f, t):
    # 2x2 core that twinkles; faint 1 px halo on bright frames
    a = 0.35 + 0.65 * (0.5 + 0.5 * math.sin(t * math.pi * 4))
    v = int(255 * a)
    out = [(x, y, (255, 240, 210, v)) for x in (3, 4) for y in (3, 4)]
    if a > 0.7:
        h = (255, 230, 190, int(90 * a))
        out += [(2, 3, h), (2, 4, h), (5, 3, h), (5, 4, h), (3, 2, h), (4, 2, h), (3, 5, h), (4, 5, h)]
    return out


def smoke(f, t):
    rnd = random.Random(1000 + f)
    r = 3.0 + 4.6 * t
    dens = 0.85 * (1 - t) ** 1.2
    out = []
    bayer = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
    for y in range(16):
        for x in range(16):
            dx, dy = x - 7.5, y - 7.5 - 1.5 * t
            d = math.hypot(dx * 0.9, dy) + rnd.uniform(-1.1, 1.1)
            if d > r:
                continue
            k = dens * (1 - d / r) ** 0.6
            if k * 16 > bayer[y % 4][x % 4]:
                g = int(120 + 70 * (1 - t))
                out.append((x, y, (g, g, g + 6, 255)))
    return out


def splash(f, t):
    out = []
    ring = 1.5 + 5.5 * t
    a = int(255 * (1 - t) ** 0.8)
    for i in range(24):
        an = i / 24 * math.tau
        x = int(round(7.5 + math.cos(an) * ring)); y = int(round(10 + math.sin(an) * ring * 0.45))
        out.append((x, y, (190, 215, 235, a)))
    if t < 0.7:
        for i in range(5):
            an = math.pi * (0.2 + 0.6 * i / 4)
            sp = 6.5 * t * (1 - 0.8 * t) * (0.8 + 0.1 * i)
            x = int(round(7.5 + math.cos(an) * sp * 1.3)); y = int(round(10 - math.sin(an) * sp * 2.2 + 12 * t * t))
            out.append((x, y, (225, 240, 255, a)))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    for name, cell, fn in (("AtmoMote", 8, mote), ("AtmoSmoke", 16, smoke), ("AtmoSplash", 16, splash)):
        p = os.path.join(a.out, f"{name}_Sheet.png")
        sheet(cell, fn).save(p)
        print(p)


if __name__ == "__main__":
    main()
