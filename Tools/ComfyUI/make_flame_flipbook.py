"""
make_flame_flipbook.py — build a seamlessly looping pixel-art flame flipbook (for the
Fluffterror/Flipbook shader) from ONE SDXL flame render.

SDXL can't draw consistent animation frames, so only the silhouette comes from the AI image; every
frame is derived procedurally from it, so it reads as one flame flickering:
  * vertical stretch + width breathing  (sum of integer-harmonic sines -> exact loop)
  * sway / shear that grows toward the tip (base stays planted on the wick)
  * edge erosion with periodic noise scrolling upward (tongues licking off the top)
  * core pulse (heat gain)
Heat = blurred silhouette (thick = hot) x vertical falloff (base hotter), plus a little of the source
luminance for texture, then snapped to the shared 6-step warm ramp (make_ember_sheets.RAMP) on black.

Run with the ComfyUI venv's Python (has PIL):
  C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe -I Tools/ComfyUI/make_flame_flipbook.py \\
      Tools/ComfyUI/sources/torch_flame/cflame_XX.png --out Assets/_Game/Rendering/VFX/TorchEmbers \\
      [--cols 4 --rows 4 --cw 32 --ch 48] [--preview tmp.png]
"""
import argparse, math, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I drops the script dir
from PIL import Image, ImageFilter
from make_ember_sheets import RAMP, extract

W, H = 96, 144          # working resolution of one frame (3x the 32x48 output)


def silhouette(src_lum):
    """Largest flame shape -> float mask (W x H), base at the bottom, centred."""
    m = src_lum.point(lambda v: 255 if v > 50 else 0)
    bbox = m.getbbox()
    shape = src_lum.crop(bbox)
    # fit into 70% width / 78% height (room for stretch + sway), bottom aligned
    sw, sh = shape.size
    k = min(W * 0.7 / sw, H * 0.78 / sh)
    shape = shape.resize((max(1, int(sw * k)), max(1, int(sh * k))), Image.LANCZOS)
    canvas = Image.new("L", (W, H), 0)
    canvas.paste(shape, ((W - shape.size[0]) // 2, H - shape.size[1] - 2))
    return canvas


def periodic_noise(seed):
    """Smooth 2-D noise that is periodic in t (sum of random waves with integer time harmonics)."""
    rng = random.Random(seed)
    waves = [(rng.uniform(0.05, 0.16), rng.uniform(0.04, 0.12), rng.choice([1, 2, 3]),
              rng.uniform(0, 2 * math.pi), rng.uniform(0.5, 1.0)) for _ in range(7)]
    def n(x, y, t):
        s = 0.0
        for fx, fy, k, ph, a in waves:
            # scrolls upward with t (y + t-term), exact loop because k is an integer
            s += a * math.sin(fx * x + fy * y + 2 * math.pi * k * t + ph)
        return s / 3.0
    return n


def frame(base_px, lum_px, t, noise):
    """One frame as a float heat grid (H rows of W)."""
    tau = 2 * math.pi * t
    stretch = 1.0 + 0.10 * math.sin(tau) + 0.05 * math.sin(2 * tau + 1.3)
    widen   = 1.0 - 0.06 * math.sin(tau + 0.7) + 0.03 * math.sin(3 * tau)
    sway    = 5.0 * math.sin(tau + 0.4) + 2.5 * math.sin(2 * tau + 2.1)
    pulse   = 1.0 + 0.10 * math.sin(2 * tau + 0.5) + 0.05 * math.sin(3 * tau + 1.0)
    base_y = H - 2
    out = [[0.0] * W for _ in range(H)]
    for y in range(H):
        h = (base_y - y) / H                       # 0 at base, ~1 at top
        hs = max(0.0, h)
        sx = sway * hs * hs                         # top sways most
        sy = base_y - (base_y - y) / stretch        # vertical stretch about the base
        for x in range(W):
            ux = (x - W / 2 - sx) / widen * (1.0 + 0.35 * hs) + W / 2   # taper the tip
            if not (0 <= ux < W - 1 and 0 <= sy < H - 1):
                continue
            # bilinear sample of the silhouette
            x0, y0 = int(ux), int(sy); fx, fy = ux - x0, sy - y0
            def s(px, a, b): return px[a, b]
            v = (s(base_px, x0, y0) * (1 - fx) * (1 - fy) + s(base_px, x0 + 1, y0) * fx * (1 - fy)
                 + s(base_px, x0, y0 + 1) * (1 - fx) * fy + s(base_px, x0 + 1, y0 + 1) * fx * fy) / 255.0
            if v <= 0.12:
                continue
            l = lum_px[min(W - 1, int(ux)), min(H - 1, int(sy))] / 255.0
            erosion = noise(x, y, t) * (0.25 + 0.6 * hs)        # edges eaten more near the tip
            heat = (v ** 2.0 * (1.0 - 0.75 * hs) + 0.10 * l + erosion * 0.3) * pulse
            out[y][x] = heat
    return out


def to_rgba(grid, cw, ch):
    """Downsample heat grid to cw x ch (box) and snap to the warm ramp."""
    im = Image.new("F", (W, H))
    im.putdata([v for row in grid for v in row])
    small = im.resize((cw, ch), Image.BOX)
    px = small.load()
    out = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    op = out.load()
    for y in range(ch):
        for x in range(cw):
            l = px[x, y]
            col = None
            for th, c in RAMP:
                if l >= th:
                    col = c
            if col:
                op[x, y] = (*col, 255)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("--out", required=True)
    ap.add_argument("--name", default="TorchFlame_Flipbook")
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--rows", type=int, default=4)
    ap.add_argument("--cw", type=int, default=32)
    ap.add_argument("--ch", type=int, default=48)
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--preview")
    a = ap.parse_args()

    src = extract(a.source, count=1)[0]                      # largest isolated bright shape
    sil_img = silhouette(src)
    # soft silhouette -> thick parts are hot; keep a crisp copy for the outline
    blur = sil_img.point(lambda v: 255 if v > 40 else 0).filter(ImageFilter.GaussianBlur(9))
    blur = blur.point(lambda v: min(255, int(v * 1.6)))
    base_px, lum_px = blur.load(), sil_img.load()
    noise = periodic_noise(a.seed)

    n = a.cols * a.rows
    sheet = Image.new("RGBA", (a.cols * a.cw, a.rows * a.ch), (0, 0, 0, 0))
    for i in range(n):
        fr = to_rgba(frame(base_px, lum_px, i / n, noise), a.cw, a.ch)
        sheet.alpha_composite(fr, ((i % a.cols) * a.cw, (i // a.cols) * a.ch))
    os.makedirs(a.out, exist_ok=True)
    path = os.path.join(a.out, a.name + ".png")
    sheet.save(path)
    print(path)
    if a.preview:
        pv = Image.new("RGBA", sheet.size, (18, 18, 26, 255))
        pv.alpha_composite(sheet)
        pv.resize((sheet.size[0] * 5, sheet.size[1] * 5), Image.NEAREST).save(a.preview)


if __name__ == "__main__":
    main()
