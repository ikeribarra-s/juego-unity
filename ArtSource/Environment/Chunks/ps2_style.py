# Fluffterror chunks — PS2-era surface pass (exec'd from build_chunks.py, shares its globals).
#
#  1. make_ps2_atlas(): T_Chunks_D.png = 512 x 512, 8 x 8 cells of 64 px. Cell i = palette entry i (PAL order),
#     filled with a TILEABLE low-res pattern of that colour (masonry courses, flagstones, planks, rivets,
#     straw, rock...). Each cell is quantised to a short ramp of its base colour with a 4x4 ordered dither,
#     and its mean is re-centred on the palette colour, so ArtDirection §3 values still hold on average.
#     Point filter, no mips (ChunkPrefabBuilder.ConfigureTexture).
#  2. World-grid UVs: every visual face is projected on its dominant axis plane and cut at integer metres,
#     so each piece lies inside one texture period and maps into its cell (u, v = world position mod period).
#     The cuts are also the vertex-lighting resolution (1 m), like PS2 tessellated walls.
#  3. Vertex colours (corner domain, linear float, exported to FBX COLOR): ambient occlusion by short
#     raycasts against the chunk itself + floor-contact grime + low-frequency mottling. Fluffterror/PS2Lit
#     multiplies albedo and lighting by it.
import math, random
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

CELL = 64                     # px per cell
ATLAS = CELL * 8              # 512

# family per palette key -> (pattern, period_m)
FAMILY = {}
for k in ("wall0", "wall1", "wall2", "wall3", "wall5"):
    FAMILY[k] = ("masonry", 2.0)
FAMILY["wall4"] = ("ashlar", 2.0)          # dressed trim stone: bigger, smoother blocks
FAMILY["stone_dk"] = ("ceiling", 2.0)
FAMILY["mortar"] = ("speckle", 1.0)
for k in ("floor0", "floor1", "floor2", "floor3", "floor4"):
    FAMILY[k] = ("flagstone", 2.0)
for k in ("dirt0", "dirt1", "dirt2"):
    FAMILY[k] = ("dirt", 1.0)
for k in ("wood0", "wood1", "wood2", "wood3"):
    FAMILY[k] = ("wood", 2.0)                # 8 px planks (0.25 m) at 32 px/m, same density as the stone
FAMILY["wood_v"] = ("wood_v", 2.0)           # vertical planks (door jambs, posts)
for k in ("iron0", "iron1", "brass", "brass1"):
    FAMILY[k] = ("metal", 1.0)
FAMILY["rust"] = ("rust", 1.0)
for k in ("bone0", "bone1", "bone2", "wax"):
    FAMILY[k] = ("bone", 1.0)
for k in ("straw0", "straw1", "straw2"):
    FAMILY[k] = ("straw", 1.0)
for k in ("rope", "cloth"):
    FAMILY[k] = ("weave", 1.0)
for k in ("moss", "slime", "meat0", "meat1", "blood0", "blood1"):
    FAMILY[k] = ("organic", 1.0)
for k in ("rock0", "rock1", "rock2", "rock3"):
    FAMILY[k] = ("rock", 2.0)
FAMILY["skulls"] = ("skulls", 1.0)
FAMILY["books"] = ("books", 1.0)
FIT_V = {"books"}                            # v is fitted to the face (one shelf row), u tiles


def family(key):
    return FAMILY.get(key, ("flat", 1.0))


# ───────────────────────────── tileable pattern helpers ─────────────────────────────
def _vnoise(rng, n, size=CELL):
    """tileable value noise: n x n random lattice, bilinear (smoothstep) upsample with wrap."""
    g = rng.random((n, n))
    t = np.arange(size) * n / size
    i0 = np.floor(t).astype(int) % n
    i1 = (i0 + 1) % n
    f = t - np.floor(t)
    f = f * f * (3 - 2 * f)
    a = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
    b = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
    return a * (1 - f)[:, None] + b * f[:, None]


def _fbm(rng, octaves=(4, 8, 16), gains=(0.55, 0.3, 0.15)):
    v = sum(_vnoise(rng, o) * g for o, g in zip(octaves, gains))
    return (v - v.mean()) / (v.std() + 1e-6)


def _crack(img, rng, n=2, length=(10, 26), depth=0.45):
    for _ in range(n):
        y, x = rng.integers(0, CELL, 2)
        dy, dx = rng.choice([-1, 0, 1]), rng.choice([-1, 1])
        for _ in range(rng.integers(*length)):
            img[y % CELL, x % CELL] -= depth
            if rng.random() < 0.5:
                y += dy if rng.random() < 0.7 else rng.choice([-1, 1])
            x += dx if rng.random() < 0.8 else 0


def _blocks(rng, rows, min_w, max_w, mortar=1, bevel=0.22, tone=0.28, chip=0.05):
    """running-bond block field (tileable in u): returns value image."""
    img = np.zeros((CELL, CELL))
    rh = CELL // rows
    for r in range(rows):
        y0 = r * rh
        # break points along the row, wrapped so the row tiles
        x = int(rng.integers(0, max_w))
        pts = []
        while True:
            pts.append(x)
            x += int(rng.integers(min_w, max_w + 1))
            if x >= pts[0] + CELL - min_w // 2:
                break
        pts.append(pts[0] + CELL)
        for a, b in zip(pts[:-1], pts[1:]):
            t = rng.normal(0, tone)
            for xx in range(a, b):
                img[y0:y0 + rh, xx % CELL] += t
            # mortar on the left edge + bottom row, bevel highlight on the top row, shade on the bottom
            for m in range(mortar):
                img[y0:y0 + rh, (a + m) % CELL] = -1.1
            cols = [xx % CELL for xx in range(a + mortar, b)]
            img[y0, cols] += bevel
            img[y0 + rh - 1 - mortar + 1:y0 + rh, cols] = -1.1 if mortar else img[y0 + rh - 1, cols]
            img[y0 + rh - 1 - mortar, cols] -= bevel * 0.8
            # a chipped corner now and then
            if rng.random() < chip * 6:
                cx = cols[0] if rng.random() < 0.5 else cols[-1]
                img[y0 + 1:y0 + 3, cx] -= 0.5
    return img


def pattern(kind, rng):
    f = _fbm(rng) * 0.18
    if kind == "masonry":            # 4 courses of 0.5 m (period 2 m), blocks 0.6-1.25 m
        v = _blocks(rng, 4, 20, 40) + f
        _crack(v, rng, 2)
        v += (rng.random((CELL, CELL)) < 0.03) * -0.3
    elif kind == "ashlar":
        v = _blocks(rng, 2, 28, 48, bevel=0.3, tone=0.18) + f * 0.6
    elif kind == "ceiling":
        v = _blocks(rng, 2, 30, 34, tone=0.2) + f
        _crack(v, rng, 1)
    elif kind == "flagstone":        # irregular 1 m flags (period 2 m), seams, chips
        v = np.zeros((CELL, CELL))
        rows = [0, 30, 64] if rng.random() < 0.5 else [0, 34, 64]
        for r in range(2):
            y0, y1 = rows[r], rows[r + 1]
            x = int(rng.integers(0, 16)); first = x
            while x < first + CELL:
                w = int(rng.integers(22, 36))
                w = min(w, first + CELL - x)
                t = rng.normal(0, 0.3)
                for xx in range(x, x + w):
                    v[y0:y1, xx % CELL] += t
                    v[y0, xx % CELL] = -1.0
                    v[(y0 + 1) % CELL, xx % CELL] += 0.18
                v[y0:y1, x % CELL] = -1.0
                v[y0:y1, (x + 1) % CELL] += 0.15
                x += w
        v += f * 1.2
        _crack(v, rng, 3, depth=0.5)
    elif kind == "dirt":
        v = f * 1.6 + (rng.random((CELL, CELL)) < 0.06) * 0.5 - (rng.random((CELL, CELL)) < 0.08) * 0.5
    elif kind == "speckle":
        v = f + (rng.random((CELL, CELL)) - 0.5) * 0.4
    elif kind in ("wood", "wood_v"):  # planks 8 px (0.25 m at 32 px/m) along u, grain streaks, nails
        g = np.repeat(_vnoise(rng, 16)[:, :1], CELL, axis=1)
        streak = (_vnoise(rng, 32) * 0.4 + np.sin(np.arange(CELL)[:, None] * 1.7 + _vnoise(rng, 4) * 6) * 0.25)
        v = streak + g * 0.3 + f * 0.5
        for y0 in range(0, CELL, 8):
            v[y0, :] = -1.2
            v[y0 + 1, :] += 0.2
            off = int(rng.integers(0, CELL))
            v[y0:y0 + 8, off] = -1.0             # plank butt joint
            for nx in (off + 3, off - 4):
                v[y0 + 2, nx % CELL] += 0.9; v[y0 + 5, nx % CELL] += 0.9
        if kind == "wood_v":
            v = np.ascontiguousarray(v.T)
    elif kind == "metal":            # 32 px plates with rivets
        v = f * 0.6
        for y0 in (0, 32):
            for x0 in (0, 32):
                v[y0:y0 + 32, x0:x0 + 32] += rng.normal(0, 0.15)
                v[y0, x0:x0 + 32] = -0.9; v[y0:y0 + 32, x0] = -0.9
                for (ry, rx) in ((4, 4), (4, 27), (27, 4), (27, 27)):
                    v[y0 + ry, x0 + rx] = 0.9; v[y0 + ry + 1, x0 + rx + 1] = -0.6
        v += np.clip(_fbm(rng, (3, 6), (0.7, 0.3)), 0.8, None) * -0.35     # rust stains
    elif kind == "rust":
        v = _fbm(rng, (4, 8, 32), (0.5, 0.3, 0.2)) * 0.35
    elif kind == "bone":
        v = f * 0.7
        _crack(v, rng, 1, (5, 12), 0.35)
    elif kind == "straw":
        v = np.zeros((CELL, CELL))
        for _ in range(140):
            y, x = rng.integers(0, CELL, 2); L = rng.integers(5, 14); s = rng.choice([-1, 1])
            b = rng.normal(0.1, 0.35)
            for k in range(L):
                v[(y + k) % CELL, (x + s * (k // 2)) % CELL] += b
        v += f * 0.6
    elif kind == "weave":
        yy, xx = np.mgrid[0:CELL, 0:CELL]
        v = ((xx // 2 + yy // 2) % 2) * 0.25 - 0.12 + f * 0.6
    elif kind == "organic":
        v = _fbm(rng, (4, 8, 16), (0.6, 0.3, 0.1)) * 0.35
        v += (rng.random((CELL, CELL)) < 0.04) * 0.4
    elif kind == "rock":             # blotchy strata + cracks
        v = _fbm(rng, (3, 6, 12, 32), (0.45, 0.3, 0.15, 0.1)) * 0.35
        yy = np.arange(CELL)[:, None]
        v += np.sin(yy * 0.6 + _vnoise(rng, 4) * 5) * 0.08
        _crack(v, rng, 4, (8, 30), 0.5)
    elif kind == "skulls":           # ossuary wall: 2 rows (0.5 m) of 3 skulls, femur bands between rows
        v = np.full((CELL, CELL), -1.1)
        for r in range(2):
            y0 = r * 32
            v[y0:y0 + 4, :] = 0.05 + _vnoise(rng, 8)[:4, :] * 0.2     # femur band
            v[y0 + 1, ::7] = -0.5
            off = 0 if r == 0 else 11
            for s in range(3):
                x0 = off + s * 21 + 2
                t = rng.normal(0.15, 0.15)
                for yy in range(y0 + 6, y0 + 30):
                    for xx in range(x0, x0 + 18):
                        dy, dx = (yy - (y0 + 16)) / 11.0, (xx - (x0 + 8.5)) / 9.0
                        if dx * dx + dy * dy < 1.0 or (yy > y0 + 24 and abs(xx - (x0 + 8.5)) < 6):
                            v[yy % CELL, xx % CELL] = t + 0.2 * (dy < -0.5) - 0.15 * (dy > 0.6)
                for ex in (x0 + 4, x0 + 10):                   # eye sockets
                    v[y0 + 15:y0 + 19, (ex) % CELL:(ex + 4) % CELL or CELL] = -1.3
                v[y0 + 21:y0 + 23, (x0 + 8) % CELL:(x0 + 10) % CELL or CELL] = -1.2     # nose
                for tx in range(x0 + 5, x0 + 13, 2):            # teeth gaps
                    v[y0 + 26:y0 + 29, tx % CELL] = -0.9
        v += f * 0.3
    elif kind == "books":            # one shelf row of spines (u tiles 1 m, v fitted to the row)
        v = np.full((CELL, CELL), -1.2)
        x = 0
        cols = []
        while x < CELL - 2:
            w = int(rng.integers(3, 7)); h = int(rng.integers(38, 62))
            if rng.random() < 0.88:
                t = rng.choice([-0.55, -0.2, 0.15, 0.45, 0.75])
                v[CELL - h:, x:x + w] = t
                v[CELL - h + 6:CELL - h + 8, x:x + w] += 0.5     # spine bands
                v[CELL - 14:CELL - 12, x:x + w] += 0.5
                v[CELL - h:, x] -= 0.3
            x += w + 1
        v += f * 0.2
        return v, True
    else:                            # flat colour with a whisper of noise (glows stay clean)
        v = f * 0.15
    return v, False


BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) + 0.5) / 16.0 - 0.5


def _ramp(base, v, levels=7, spread=0.42, books=False):
    """quantise value field v to `levels` shades of base (sRGB 0..1), ordered dither, mean kept on base."""
    v = (v - v.mean()) / (v.std() + 1e-6) * spread * 0.5
    step = spread * 2 / (levels - 1)
    tiled = np.tile(BAYER, (CELL // 4, CELL // 4))
    q = np.round(v / step + tiled * 0.9) * step
    q = np.clip(q, -spread, spread)
    lum = 1.0 + q
    col = np.clip(base[None, None, :] * lum[..., None], 0, 1)
    # darker shades drift slightly cooler, lighter ones slightly warmer (hand-painted PS2 ramps)
    col[..., 2] *= 1 + np.clip(-q, 0, None) * 0.08
    col[..., 0] *= 1 + np.clip(q, 0, None) * 0.05
    if books:   # multi-coloured spines: hue rotates with the shade so the row is not one colour
        hues = np.array([[0.55, 0.16, 0.18], [0.24, 0.42, 0.3], [0.36, 0.25, 0.16], [0.5, 0.42, 0.25], [0.25, 0.3, 0.5]])
        idx = np.clip(((q + spread) / (2 * spread) * 4.99).astype(int), 0, 4)
        tint = hues[idx] / hues.mean(axis=0)
        col = np.where((q > -spread + 1e-6)[..., None], np.clip(col * tint, 0, 1), col * 0.4)
    # re-centre the mean on the palette colour (per channel)
    m = col.reshape(-1, 3).mean(axis=0)
    col = np.clip(col * (base / np.maximum(m, 1e-3))[None, None, :], 0, 1)
    return col


def _hex(h):
    return np.array([int(h[k:k + 2], 16) / 255.0 for k in (0, 2, 4)])


def _books_rgb(rng):
    """one shelf row of book spines in the palette's book colours, dark shelf back, light top bands."""
    spines = [_hex(dict(PAL)[k]) for k in ("red0", "green", "wood1", "cloth", "brass", "glass_b", "rust", "bone2", "wood3")]
    back = _hex(dict(PAL)["wood2"]) * 0.45
    col = np.tile(back, (CELL, CELL, 1))
    x = 0
    while x < CELL - 2:
        w = int(rng.integers(3, 7)); h = int(rng.integers(38, 62))
        if rng.random() < 0.9:
            c = spines[int(rng.integers(len(spines)))] * rng.uniform(0.8, 1.1)
            col[CELL - h:, x:x + w] = c
            col[CELL - h:, x] = c * 0.7
            for yb in (CELL - h + 6, CELL - 14):
                col[yb:yb + 2, x:x + w] = np.clip(c * 1.35 + 0.05, 0, 1)
        x += w + 1
    return np.clip(col, 0, 1)


HEIGHTS = np.zeros((ATLAS, ATLAS))     # per-cell height field of the last atlas build (feeds the normal atlas)


def make_ps2_atlas(path):
    img = np.zeros((ATLAS, ATLAS, 3))
    HEIGHTS[:] = 0.0
    for i, (key, hx) in enumerate(PAL):
        base = np.array([int(hx[k:k + 2], 16) / 255.0 for k in (0, 2, 4)])
        kind, _ = family(key)
        rng = np.random.default_rng(1000 + i)
        if kind == "flat":
            col = np.tile(base, (CELL, CELL, 1))
            if key not in ("void", "amber", "ember", "green1", "crystal"):
                hv = pattern(kind, rng)[0]
                col = _ramp(base, hv, levels=3, spread=0.06)
                HEIGHTS[(i // 8) * CELL:(i // 8 + 1) * CELL, (i % 8) * CELL:(i % 8 + 1) * CELL] = hv
        else:
            v, books = pattern(kind, rng)
            spread = {"masonry": 0.36, "ashlar": 0.3, "ceiling": 0.3, "flagstone": 0.4, "rock": 0.4,
                      "metal": 0.36, "bone": 0.18, "skulls": 0.55, "books": 0.5, "wood": 0.34, "wood_v": 0.34}.get(kind, 0.3)
            col = _ramp(base, v, levels=7 if kind not in ("skulls", "books") else 6, spread=spread, books=books)
            HEIGHTS[(i // 8) * CELL:(i // 8 + 1) * CELL, (i % 8) * CELL:(i % 8 + 1) * CELL] = v
        if key == "books":
            col = _books_rgb(np.random.default_rng(1000 + i))
        cx, cy = i % 8, i // 8
        img[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL] = col
    _write_png(path, img)


def _write_png(path, img):
    """8-bit RGB PNG (top row first)."""
    import struct, zlib as _z
    n = img.shape[0]
    px = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + px[y].tobytes() for y in range(n))

    def chunk(t, data):
        c = struct.pack(">I", len(data)) + t + data
        return c + struct.pack(">I", _z.crc32(t + data) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", _z.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


NORMAL_STRENGTH = 1.0
NORMAL_QUANT = 0            # >0: quantise the gradient to that many steps per side (PS2-ish chunkiness)


def make_ps2_normal_atlas(path):
    """tangent-space normal atlas from the height fields captured by make_ps2_atlas. Per 64 px cell: slight blur,
    wrapped central-difference gradients inside the cell (tiles seamlessly), n = (-gx, +gy, 1) * strength.
    Atlas rows are top-first, so +image-down = -V: dh/dV = -(dh/drow) -> green = +gy(row) flipped by the sign below
    (mortar/joints read RECESSED under a side light; flip GREEN_SIGN if it looks inverted)."""
    out = np.zeros((ATLAS, ATLAS, 3)); out[..., 2] = 1.0
    for i in range(64):
        cy, cx = i // 8, i % 8
        h = HEIGHTS[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL]
        if not h.any():
            out[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL] = (0.5, 0.5, 1.0)
            continue
        b = h.copy()
        for ax in (0, 1):
            b = 0.25 * np.roll(b, 1, ax) + 0.5 * b + 0.25 * np.roll(b, -1, ax)
        gx = (np.roll(b, -1, 1) - np.roll(b, 1, 1)) * 0.5
        gy = (np.roll(b, -1, 0) - np.roll(b, 1, 0)) * 0.5      # per row, rows go down the image
        if NORMAL_QUANT:
            q = NORMAL_QUANT
            gx = np.round(gx * q) / q; gy = np.round(gy * q) / q
        nx = -gx * NORMAL_STRENGTH
        ny = GREEN_SIGN * gy * NORMAL_STRENGTH
        n = np.stack([nx, ny, np.ones_like(nx)], -1)
        n /= np.linalg.norm(n, axis=-1, keepdims=True)
        out[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL] = n * 0.5 + 0.5
    _write_png(path, out)


GREEN_SIGN = 1.0


def atlas_uv(key, s, t):
    """s, t in [0,1] inside one texture period -> atlas UV (half-texel inset so Point never bleeds)."""
    i = PAL_IDX[key]
    cx, cy = i % 8, i // 8
    s = min(max(s, 0.0), 1.0); t = min(max(t, 0.0), 1.0)
    u = (cx * CELL + 0.5 + s * (CELL - 1)) / ATLAS
    v = 1.0 - (cy * CELL + 0.5 + (1 - t) * (CELL - 1)) / ATLAS
    return u, v


# ───────────────────────────── face splitting ─────────────────────────────
PROJ = [(1, 2), (0, 2), (0, 1)]     # dominant normal axis -> (u axis, v axis); v = z on walls


def poly_normal(pts):
    n = Vector((0, 0, 0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y)
    return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))


def _clip(pts, ax, val, keep_below):
    out = []
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        ina = (a[ax] <= val + 1e-7) if keep_below else (a[ax] >= val - 1e-7)
        inb = (b[ax] <= val + 1e-7) if keep_below else (b[ax] >= val - 1e-7)
        if ina:
            out.append(a)
        if ina != inb:
            t = (val - a[ax]) / (b[ax] - a[ax])
            out.append(a.lerp(b, t))
    # drop duplicates
    res = []
    for p in out:
        if not res or (p - res[-1]).length > 1e-6:
            res.append(p)
    if len(res) > 1 and (res[0] - res[-1]).length < 1e-6:
        res.pop()
    return res


def cut_values(ax, lo, hi, period):
    """cut positions inside (lo, hi): every texture period, plus every metre below 2 m on the vertical
    axis so wall-to-floor grime / AO gets vertex resolution where the player looks."""
    out = []
    k = math.floor(lo + 1e-6) + 1
    while k < hi - 1e-6:
        if abs(k / period - round(k / period)) < 1e-6 or (ax == 2 and k <= 2):
            out.append(float(k))
        k += 1
    return out


def split_grid(pts, axes, period=1.0):
    """cut a convex planar polygon at the cut_values() along the given world axes."""
    pieces = [pts]
    for ax in axes:
        out = []
        for p in pieces:
            lo, hi = min(q[ax] for q in p), max(q[ax] for q in p)
            rest = p
            for val in cut_values(ax, lo, hi, period):
                if len(rest) < 3:
                    break
                below = _clip(rest, ax, val, True)
                if len(below) >= 3:
                    out.append(below)
                rest = _clip(rest, ax, val, False)
            if len(rest) >= 3:
                out.append(rest)
        pieces = out
    return pieces


def face_uvs(pts, key, origin):
    """per-vertex atlas UVs for one (already split) piece, from world-natural coordinates."""
    n = poly_normal(pts)
    ax = max(range(3), key=lambda a: abs(n[a]))
    ua, va = PROJ[ax]
    _, P = family(key)
    w = [p + origin for p in pts]
    us = [q[ua] for q in w]; vs = [q[va] for q in w]
    def axis(vals, fit=False):
        """(base, span) so that s = (x - base) / span lies in [0, 1]: world-anchored when the face sits in
        one period, local origin for a small face straddling a period line, stretched for a long unsplit
        thin face (bars, trims) or a FIT_V row."""
        lo, hi = min(vals), max(vals)
        if fit:
            return lo, (hi - lo) or 1.0
        b0 = math.floor(lo / P + 1e-5) * P
        if hi - b0 <= P + 1e-5:
            return b0, P
        if hi - lo <= P + 1e-5:
            return lo, P
        return lo, hi - lo
    ub, us_ = axis(us)
    vb, vs_ = axis(vs, key in FIT_V)
    return [atlas_uv(key, (u - ub) / us_, (v - vb) / vs_) for u, v in zip(us, vs)]


# ───────────────────────────── vertex colour bake ─────────────────────────────
def _hemi_dirs(n=14, seed=7):
    rng = random.Random(seed)
    out = []
    for i in range(n):        # cosine-weighted, stratified
        a = 2 * math.pi * (i + rng.random()) / n
        r = math.sqrt((i % 7 + rng.random()) / 7.0) * 0.95
        out.append((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1 - r * r))))
    return out


HEMI = _hemi_dirs()


class Baker:
    """AO + grime per (world position, face normal), memoised."""

    def __init__(self, mbs, max_d=1.3, strength=0.55):
        verts, polys = [], []
        for mb, off in mbs:
            for f, cat in zip(mb.f, mb.c):
                if cat == "L1":
                    continue
                b = len(verts)
                verts += [mb.v[i] + off for i in f]
                polys.append(list(range(b, b + len(f))))
        self.bvh = BVHTree.FromPolygons(verts, polys) if polys else None
        self.max_d, self.strength = max_d, strength
        self.memo = {}

    def color(self, p, n):
        key = (round(p.x, 3), round(p.y, 3), round(p.z, 3), round(n.x, 2), round(n.y, 2), round(n.z, 2))
        c = self.memo.get(key)
        if c is not None:
            return c
        occ = 0.0
        if self.bvh:
            t = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
            b1 = n.cross(t).normalized(); b2 = n.cross(b1)
            o = p + n * 0.02
            for (x, y, z) in HEMI:
                d = b1 * x + b2 * y + n * z
                hit = self.bvh.ray_cast(o, d, self.max_d)
                if hit[0] is not None:
                    occ += 1.0 - (hit[3] / self.max_d) ** 0.7
            occ /= len(HEMI)
        ao = 1.0 - self.strength * occ
        # grime: dirt collects where walls meet the floor (and on the lowest course of everything)
        g = 0.0
        if n.z < 0.7:
            g = max(0.0, 1.0 - max(p.z, 0.0) / 0.9) * 0.22
        # low-frequency mottling so long walls are not evenly lit (hash of the metre cell)
        h = math.sin(p.x * 1.73 + p.y * 2.31 + p.z * 1.17) * 0.5 + math.sin(p.x * 0.61 - p.y * 0.83 + p.z * 2.9) * 0.5
        m = 1.0 + h * 0.07
        r = ao * m * (1 - g)
        c = (min(1.0, r), min(1.0, r * (1 - g * 0.25)), min(1.0, r * (1 - g * 0.55)), 1.0)
        self.memo[key] = c
        return c
