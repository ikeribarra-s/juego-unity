"""
make_ember_sheets.py — turn SDXL ember/flame renders into tiny pixel-art sprite sheets for the
torch particle systems (Assets/_Game/Rendering/VFX/TorchEmbers/).

Run with the ComfyUI venv's Python (has PIL):
  C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe -I Tools/ComfyUI/make_ember_sheets.py \\
      --embers <ember_a.png> <ember_b.png> ... --flames <flame_a.png> ... --out Assets/_Game/Rendering/VFX/TorchEmbers

Sources come from:  python Tools/ComfyUI/gen.py workflows/sdxl_fx_black.json "<subject>" --out <tmp> --seed S
(black background, bright subject in the middle).

Each sheet is a 4x4 grid of CELL px frames = one particle's whole life, row-major
(frame 0 = birth, frame 15 = death): the shape shrinks, cools from white-hot to ember red and
dissolves pixel by pixel. Consecutive frames alternate source image / rotation so the particle
"boils" like a gif. Colours are snapped to a 6-step warm ramp (ArtDirection §3) on pure black,
alpha = 255 wherever a pixel is lit, so the sheet works with additive OR alpha blending.
"""
import argparse, math, os, random
from PIL import Image, ImageFilter, ImageOps

GRID = 4
FRAMES = GRID * GRID

# Warm ramp: (min luminance, colour). Below the first threshold the pixel is empty.
RAMP = [
    (0.14, (0x4A, 0x14, 0x0C)),   # dying ember
    (0.30, (0xC2, 0x50, 0x2A)),   # ember  (ArtDirection)
    (0.48, (0xE8, 0x78, 0x2E)),   # orange
    (0.64, (0xFF, 0xB3, 0x47)),   # torch amber (ArtDirection)
    (0.82, (0xFF, 0xE0, 0x9A)),   # hot
    (0.94, (0xFF, 0xF8, 0xE8)),   # white-hot core
]


def extract(path, count=2, thresh=60):
    """The `count` largest bright shapes (connected components) that don't touch the image border,
    each as a square grayscale crop with everything else blacked out. SDXL tends to render a
    pattern of many embers/flames, so we harvest individual shapes from it."""
    lum = Image.open(path).convert("L")
    W = 256
    small = lum.resize((W, W), Image.BOX)
    sp = small.load()
    seen = [[False] * W for _ in range(W)]
    comps = []
    for y0 in range(W):
        for x0 in range(W):
            if seen[y0][x0] or sp[x0, y0] <= thresh:
                continue
            stack, pts, border = [(x0, y0)], [], False
            seen[y0][x0] = True
            while stack:
                x, y = stack.pop()
                pts.append((x, y))
                if x in (0, W - 1) or y in (0, W - 1):
                    border = True
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < W and 0 <= ny < W and not seen[ny][nx] and sp[nx, ny] > thresh:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            if not border and len(pts) > 40:
                comps.append(pts)
    comps.sort(key=len, reverse=True)
    out = []
    k = lum.size[0] / W
    for pts in comps[:count]:
        mask = Image.new("L", (W, W), 0)
        mp = mask.load()
        for x, y in pts:
            mp[x, y] = 255
        mask = mask.filter(ImageFilter.MaxFilter(3)).resize(lum.size, Image.NEAREST)
        obj = Image.composite(lum, Image.new("L", lum.size, 0), mask)
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        box = (int((min(xs) - 1) * k), int((min(ys) - 1) * k), int((max(xs) + 2) * k), int((max(ys) + 2) * k))
        obj = obj.crop(box)
        side = max(obj.size)
        sq = Image.new("L", (side, side), 0)
        sq.paste(obj, ((side - obj.size[0]) // 2, (side - obj.size[1]) // 2))
        out.append(sq)
    if not out:
        raise SystemExit(f"no isolated bright shape found in {path}")
    return out


def to_palette(lum_img, gain, rng, dissolve):
    """Map a small luminance image to the warm ramp, with random dissolve dropout."""
    out = Image.new("RGBA", lum_img.size, (0, 0, 0, 0))
    px, src = out.load(), lum_img.load()
    for y in range(lum_img.size[1]):
        for x in range(lum_img.size[0]):
            l = min(1.0, src[x, y] / 255.0 * gain)
            col = None
            for t, c in RAMP:
                if l >= t:
                    col = c
            if col is None or rng.random() < dissolve:
                continue
            px[x, y] = (*col, 255)
    return out


def build(sources, cell, out_path, *, size0, size1, gain0, gain1, dissolve1, stretch, seed):
    rng = random.Random(seed)
    srcs = [s for p in sources for s in extract(p)]
    sheet = Image.new("RGBA", (cell * GRID, cell * GRID), (0, 0, 0, 0))
    for i in range(FRAMES):
        t = i / (FRAMES - 1)
        src = srcs[i % len(srcs)]
        src = src.rotate(90 * ((i // len(srcs)) % 4) if stretch == 1.0 else 0)
        if stretch != 1.0 and (i % 2):
            src = ImageOps.mirror(src)
        s = max(2, round(size0 + (size1 - size0) * t ** 0.85))
        sw = max(1, round(s / stretch)) if stretch != 1.0 else s
        small = src.filter(ImageFilter.GaussianBlur(src.size[0] / 64)).resize((sw, s), Image.BOX)
        gain = gain0 + (gain1 - gain0) * t
        frame = to_palette(small, gain, rng, dissolve1 * t ** 1.5)
        ox = (i % GRID) * cell + (cell - sw) // 2
        oy = (i // GRID) * cell + (cell - s) // 2
        sheet.alpha_composite(frame, (ox, oy))
    # Unity reads RGB on black for additive: make sure transparent pixels are black.
    sheet.save(out_path)
    print(out_path)
    return sheet


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--embers", nargs="+", required=True)
    ap.add_argument("--flames", nargs="+", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--cell", type=int, default=16)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--preview", help="optional enlarged preview png (x8, both sheets side by side)")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    embers = build(a.embers, a.cell, os.path.join(a.out, "TorchEmbers_Sheet.png"),
                   size0=11, size1=4, gain0=1.7, gain1=0.8, dissolve1=0.4, stretch=1.0, seed=a.seed)
    flames = build(a.flames, a.cell, os.path.join(a.out, "TorchFlameBits_Sheet.png"),
                   size0=15, size1=5, gain0=1.25, gain1=0.6, dissolve1=0.45, stretch=1.2, seed=a.seed + 1)

    if a.preview:
        w = embers.size[0]
        pv = Image.new("RGBA", (w * 2 + 4, w), (20, 20, 28, 255))
        pv.alpha_composite(embers, (0, 0))
        pv.alpha_composite(flames, (w + 4, 0))
        pv = pv.resize((pv.size[0] * 8, pv.size[1] * 8), Image.NEAREST)
        pv.save(a.preview)


if __name__ == "__main__":
    main()
