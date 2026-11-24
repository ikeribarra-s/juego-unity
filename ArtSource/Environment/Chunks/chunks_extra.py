# Fluffterror chunks — round 2: unique rooms / set pieces, corridor variants, tags, theme heights.
# Exec'd from build_chunks.py (shares its globals: Chunk, MB, palette lists, helpers).
import math

# MultiSectionDungeonGenerator wall heights per SectionType (Transition corridors use the Gothic height).
THEME_HEIGHTS = {"GothicRuins": 5.0, "Transition": 5.0, "Graveyard": 1.4, "HellPillars": 6.5,
                 "Catacombs": 3.6, "Cavern": 7.5}

BOOKS = ["red0", "green", "wood1", "cloth", "brass", "glass_b", "rust", "bone2", "wood3"]

FONT5 = {"C": ["111", "100", "100", "100", "111"], "H": ["101", "101", "111", "101", "101"],
         "U": ["101", "101", "101", "101", "111"], "O": ["111", "101", "101", "101", "111"],
         "V": ["101", "101", "101", "101", "010"], "A": ["010", "101", "111", "101", "101"],
         "N": ["101", "111", "111", "111", "101"], "Y": ["101", "101", "010", "010", "010"]}


# ───────────────────────────── helpers ─────────────────────────────
def wall_box(c, n, plane, u0, u1, z0, z1, depth, key, cat=None):
    """box on the inner face of an axis-aligned wall. n = inward normal (nx, ny), plane = wall face coord."""
    old = c.mb.cat
    if cat:
        c.mb.cat = cat
    nx, ny = n
    if nx:
        c.mb.box(plane, u0, z0, plane + nx * depth, u1, z1, key)
    else:
        c.mb.box(u0, plane, z0, u1, plane + ny * depth, z1, key)
    c.mb.cat = old


def wall_quad(c, n, plane, u0, u1, z0, z1, key, off=0.01, cat="D"):
    """flat quad just in front of a wall, facing the room."""
    old = c.mb.cat
    c.mb.cat = cat
    nx, ny = n
    if nx:
        x = plane + nx * off
        pts = [(x, u0, z0), (x, u1, z0), (x, u1, z1), (x, u0, z1)]
        if nx < 0:
            pts.reverse()
    else:
        y = plane + ny * off
        pts = [(u0, y, z0), (u1, y, z0), (u1, y, z1), (u0, y, z1)]
        if ny > 0:
            pts.reverse()
    c.mb.poly(pts, key)
    c.mb.cat = old


def pixel_text(c, text, n, plane, u_start, z_top, px=0.08, key="bone0"):
    """chunky 3x5 pixel letters on a wall (reads left-to-right for a viewer facing the wall)."""
    nx, ny = n
    right = (-ny, nx)                       # viewer faces -n
    sgn = right[0] if nx == 0 else right[1]
    u = u_start
    for ch in text:
        if ch == " ":
            u += sgn * 3 * px
            continue
        for r, row in enumerate(FONT5[ch]):
            z = z_top - r * px
            for col, bit in enumerate(row):
                if bit == "1":
                    a = u + sgn * col * px
                    b = a + sgn * px
                    wall_quad(c, n, plane, min(a, b), max(a, b), z - px, z, key, off=0.03)
        u += sgn * 4 * px


def bookcase(c, x0, y0, x1, y1, h, fronts, density=0.85):
    """solid bookcase block with shelf boards and book spines on the given front sides ('n','s','e','w')."""
    mb = c.mb
    mb.box(x0, y0, 0.0, x1, y1, h, "wood2", skip=('b',))
    rows = [(z, z + 0.62) for z in (0.12, 0.84, 1.56, 2.28) if z + 0.62 < h - 0.1]
    for f in fronts:
        if f in "ns":
            plane, nn, u0, u1 = (y1, (0, 1), x0, x1) if f == 'n' else (y0, (0, -1), x0, x1)
        else:
            plane, nn, u0, u1 = (x1, (1, 0), y0, y1) if f == 'e' else (x0, (-1, 0), y0, y1)
        for (zb, zt) in rows:
            wall_box(c, nn, plane, u0, u1, zb - 0.06, zb, 0.04, "wood1", cat="S")
            # PS2 pass: one "books" panel per shelf row (spines are in the texture; u tiles every metre,
            # v is fitted to the row) instead of a quad per book.
            wall_quad(c, nn, plane, u0 + 0.04, u1 - 0.04, zb, zt - 0.04, "books", off=0.012)


def sector(mb, cx, cy, r0, r1, a0, a1, z0, z1, key):
    """annular sector prism (spiral stair step)."""
    am = (a0 + a1) / 2
    inner = [(cx + r0 * math.cos(a), cy + r0 * math.sin(a)) for a in (a0, a1)]
    outer = [(cx + r1 * math.cos(a), cy + r1 * math.sin(a)) for a in (a0, am, a1)]
    ring = [inner[0], outer[0], outer[1], outer[2], inner[1]]      # CCW seen from above
    top = [(p[0], p[1], z1) for p in ring]
    bot = [(p[0], p[1], z0) for p in ring]
    mb.poly(top, key)
    mb.poly(list(reversed(bot)), key)
    for i in range(5):
        j = (i + 1) % 5
        mb.poly([bot[i], bot[j], top[j], top[i]], key)


def rock_walls(c, masonry_below_y=None):
    """wall visual hook: faceted natural rock instead of masonry (optionally masonry south of a line)."""
    def vis(d, x0, y0, x1, y1, z0, line, span):
        n = -1 if d in "NE" else 1
        a0, a1 = span
        parts = [(a0, a1, "rock")]
        if masonry_below_y is not None:
            if d in "NS":
                parts = [(a0, a1, "mason" if line < masonry_below_y else "rock")]
            elif a0 < masonry_below_y < a1:
                parts = [(a0, masonry_below_y, "mason"), (masonry_below_y, a1, "rock")]
            elif a1 <= masonry_below_y:
                parts = [(a0, a1, "mason")]
        for (u0, u1, kind) in parts:
            if d in "NS":
                bx = (u0, y0, z0, u1, y1, c.H)
            else:
                bx = (x0, u0, z0, x1, u1, c.H)
            if kind == "mason":
                c.mb.box(*bx, WALL)
                c.wall_decor((u0, u1), line, d, z0)
                continue
            c.mb.box(*bx, ROCK)
            old = c.mb.cat
            c.mb.cat = "D"
            u = u0
            while u < u1 - 0.05:
                w = c.rng.uniform(0.7, 1.5)
                ue = min(u + w, u1)
                z = z0
                while z < c.H - 0.1:
                    hgt = c.rng.uniform(0.8, 1.7)
                    ze = min(z + hgt, c.H)
                    p = c.rng.uniform(0.06, 0.26)
                    um, zm = (u + ue) / 2, (z + ze) / 2
                    if d in "NS":
                        cen = (um, line + n * (p - 0.2) / 2 - n * 0.1, zm)
                        size = (ue - u + 0.25, p + 0.2, ze - z + 0.25)
                    else:
                        cen = (line + n * (p - 0.2) / 2 - n * 0.1, um, zm)
                        size = (p + 0.2, ue - u + 0.25, ze - z + 0.25)
                    c.mb.obox(cen, size, (c.rng.uniform(-0.12, 0.12), c.rng.uniform(-0.12, 0.12), c.rng.uniform(-0.15, 0.15)), ROCK)
                    z = ze
                u = ue
            c.mb.cat = old
    return vis


def hanging(c, x, y, z_top, z_body0, z_body1, key, w=0.45, col=True):
    """sack / ham on a rope hanging from the ceiling."""
    c.mb.hcyl((x, y, z_top), (x, y, z_body1), 0.02, 4, "rope")
    c.mb.obox((x, y, (z_body0 + z_body1) / 2), (w, w * 0.9, z_body1 - z_body0), (0, 0, c.rng.uniform(0, 1)), key)
    c.mb.cyl(x, y, z_body1, z_body1 + 0.1, w * 0.3, 5, "rope", r_top=0.05)
    if col:
        c.col("Block", x - w / 2, y - w / 2, z_body0, x + w / 2, y + w / 2, z_body1)


def bars(c, axis, plane, u0, u1, z0, z1, step=0.18, r=0.03):
    old = c.mb.cat
    c.mb.cat = "D"
    u = u0 + step / 2
    while u < u1:
        if axis == 'x':
            c.mb.cyl(plane, u, z0, z1, r, 4, "iron0", caps=(False, False))
        else:
            c.mb.cyl(u, plane, z0, z1, r, 4, "iron0", caps=(False, False))
        u += step
    c.mb.cat = old


def puddle(c, x, y, rx, ry, z=0.012):
    w = c.extra.get("water") or MB(c.rng)
    pts = [(x + rx * math.cos(a) * (0.85 + 0.15 * math.sin(3 * a)), y + ry * math.sin(a), z)
           for a in [2 * math.pi * k / 9 for k in range(9)]]
    w.poly(pts, "water")
    c.extra["water"] = w


# ───────────────────────────── new chunks ─────────────────────────────
def ch_room_library():
    c = Chunk("CH_Room_Library", 1, 2, H=5.0, role="room")
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    c.build_shell()
    # wall bookcases (gap at y 0 for the torches)
    for (ya, yb) in ((-6.6, -0.5), (0.5, 6.6)):
        bookcase(c, 2.45, ya, 3.0, yb, 3.2, "w"); c.col("Block", 2.45, ya, 0, 3.0, yb, 3.2)
    for (ya, yb) in ((-6.6, -0.5), (0.5, 3.2)):
        bookcase(c, -3.0, ya, -2.45, yb, 3.2, "e"); c.col("Block", -3.0, ya, 0, -2.45, yb, 3.2)
    c.mark_blocked(2.45, -6.6, 3, 6.6); c.mark_blocked(-3, -6.6, -2.45, 3.2)
    # row A (y -2.3..-1.8): two cases + a toppled case bridging them at 1.3 m = hidden crawl route
    bookcase(c, -2.45, -2.3, -1.0, -1.8, 2.6, "ns"); c.col("Block", -2.45, -2.3, 0, -1.0, -1.8, 2.6)
    bookcase(c, 0.4, -2.3, 1.0, -1.8, 2.6, "ns"); c.col("Block", 0.4, -2.3, 0, 1.0, -1.8, 2.6)
    c.mb.obox((-0.3, -2.05, 1.55), (1.9, 0.6, 0.5), (0.08, 0.0, 0.05), "wood2")
    c.col("Block", -1.0, -2.3, 1.3, 0.4, -1.8, 2.6)
    for k in range(9):          # spilled books on top and at the foot (hide the gap)
        c.mb.cat = "D"
        c.mb.obox((-0.9 + k * 0.16, -2.05 + c.rng.uniform(-0.2, 0.2), 1.83), (0.14, 0.22, 0.05), (0, c.rng.uniform(-0.6, 0.6), c.rng.uniform(0, 3)), BOOKS)
        c.mb.obox((c.rng.uniform(-1.8, 1.6), c.rng.uniform(-3.2, -1.0), 0.03), (0.18, 0.24, 0.05), (0, 0, c.rng.uniform(0, 3)), BOOKS)
        c.mb.cat = "S"
    c.mark_blocked(-2.45, -2.3, 1.0, -1.8)
    # row B (y 2.8..3.3): one long case, open on the west
    bookcase(c, -0.9, 2.8, 2.45, 3.3, 2.8, "ns"); c.col("Block", -0.9, 2.8, 0, 2.45, 3.3, 2.8)
    c.mark_blocked(-0.9, 2.8, 2.45, 3.3)
    # lectern with the giant open book (landmark) + reading desk + library ladder
    lec = c.scratch_mb() if HERO_PROPS else c.mb
    lec.box(-0.3, 5.1, 0, 0.3, 5.5, 1.0, "wood2", skip=('b',))
    lec.obox((0.0, 5.3, 1.1), (1.0, 0.7, 0.12), (0.35, 0, 0), "wood1")
    lec.obox((-0.24, 5.28, 1.2), (0.48, 0.62, 0.05), (0.35, 0, 0.08), "bone0")
    lec.obox((0.24, 5.28, 1.2), (0.48, 0.62, 0.05), (0.35, 0, -0.08), "bone1")
    if HERO_PROPS:
        c.hero("Lectern", (0.0, 5.3, 0.0), (0, -1))
    c.col("Block", -0.5, 4.9, 0, 0.5, 5.7, 1.2)
    c.mb.box(-1.6, -5.4, 0.72, 0.2, -4.6, 0.8, "wood1")
    for x in (-1.5, 0.1):
        for y in (-5.3, -4.7):
            c.mb.box(x - 0.04, y - 0.04, 0, x + 0.04, y + 0.04, 0.72, "wood2")
    c.col("Rail", -1.6, -5.4, 0, 0.2, -4.6, 0.8)
    c.candle(-1.3, -5.1, 0.8); c.candle(-0.1, -4.8, 0.8, 0.18)
    for s in (-1, 1):
        c.mb.obox((2.15, -4.0 + s * 0.22, 1.6), (0.06, 0.06, 3.3), (0, 0.28, 0), "wood1")
    for k in range(7):
        c.mb.obox((2.15 - 0.03 * k, -4.0, 0.3 + k * 0.42), (0.05, 0.5, 0.05), (0, 0, 0), "wood1")
    c.torch(3.0, 0.0, (-1, 0)); c.torch(-3.0, 3.85, (1, 0))
    c.sock("Landmark", (0.0, 5.3, 1.2), (0, -1))
    c.sock("Evidence", (0.0, 5.3, 1.25), (0, -1))
    c.sock("Evidence", (-0.3, -2.05, 0.0), (0, 1))      # inside the crawl gap under the toppled case
    c.sock("Candle", (-1.3, -5.1, 0.8), (0, 1))
    c.sock("Hide", (-1.8, 5.8, 0.0), (1, -1))           # behind row B
    c.sock("Prop", (-1.9, -6.2, 0), (1, 0)); c.sock("Prop", (1.8, 1.8, 0), (-1, 0))
    c.sock("Spawn", (0.0, -6.2, 0), (0, 1))
    c.sock("Drip", (0.5, 0.8, c.H - 0.1), (0, -1))
    c.extra["crawl_gap"] = {"x": [-1.0, 0.4], "y": [-2.3, -1.8], "height": 1.3}
    return c


def ch_room_larder():
    c = Chunk("CH_Room_Larder", 1, 1, role="room", floor_keys=FLOOR[:3] + ["dirt0"])
    c.rect(-3, -3, 3, 3); c.door('S'); c.door('E')
    c.build_shell()
    # hook rails + a curtain of hanging hams/sacks (LOS break, hiding aisle behind it)
    c.ceil.box(-3, 1.3, 3.55, 1.4, 1.5, 3.7, "iron0")
    c.ceil.box(-3, -2.2, 3.55, 3, -2.0, 3.7, "iron0")
    for k, x in enumerate((-2.4, -1.6, -0.8, 0.0, 0.8)):
        key = ["meat0", "meat1", "cloth"][k % 3]
        hanging(c, x, 1.4, 3.55, 1.0 + 0.1 * (k % 2), 2.15, key)
    for x in (-2.0, 0.6, 2.2):
        hanging(c, x, -2.1, 3.55, 2.3, 2.9, "meat0", w=0.32, col=False)
    # butcher table, cleaver, cheese wheels
    tb = c.scratch_mb() if HERO_PROPS else c.mb
    tb.box(-1.2, -1.6, 0.8, 1.2, -0.6, 0.92, "wood1")
    for x in (-1.1, 1.1):
        for y in (-1.5, -0.7):
            tb.box(x - 0.06, y - 0.06, 0, x + 0.06, y + 0.06, 0.8, "wood2")
    c.col("Rail", -1.2, -1.6, 0, 1.2, -0.6, 0.92)
    tb.cat = "D"
    tb.obox((0.3, -1.1, 1.05), (0.06, 0.4, 0.26), (0, 0.15, 0.3), "iron1")
    tb.cyl(-0.6, -1.0, 0.92, 1.05, 0.22, 7, "straw1"); tb.cyl(-0.3, -1.25, 0.92, 1.0, 0.14, 6, "straw0")
    tb.cat = "S"
    if HERO_PROPS:
        c.hero("Butcher", (0.0, -1.1, 0.0), (0, -1))
    # barrels SW, jar shelves on the west wall
    for (x, y) in ((-2.5, -2.5), (-1.85, -2.55)):
        c.mb.cyl(x, y, 0, 0.9, 0.32, 8, "wood0", r_top=0.3); c.mb.ring(x, y, 0.5, 0.31, 0.34, 8, "iron0")
    c.col("Block", -2.85, -2.9, 0, -1.5, -2.2, 0.9)
    for z in (0.6, 1.3, 2.0):
        c.mb.box(-3.0, -1.0, z, -2.6, 1.0, z + 0.05, "wood1")
        for y in (-0.8, -0.4, 0.1, 0.6):
            if c.rng.random() < 0.8:
                c.mb.cat = "D"; c.mb.cyl(-2.8, y, z + 0.05, z + 0.3, 0.1, 6, ["bone2", "glass_g", "rust"], r_top=0.06); c.mb.cat = "S"
    c.col("Block", -3.0, -1.0, 0, -2.6, 1.0, 2.1)
    c.mark_blocked(-1.2, -1.6, 1.2, -0.6); c.mark_blocked(-2.85, -2.9, -1.5, -2.2)
    c.torch(-2.5, -3.0, (0, 1))
    c.sock("Hide", (-1.0, 2.35, 0.0), (0, -1)); c.sock("Hide", (0.4, 2.4, 0.0), (0, -1))
    c.sock("Evidence", (0.6, -1.0, 0.92), (0, 1)); c.sock("Evidence", (-1.8, 2.5, 0.0), (1, 0))
    c.sock("Prop_Barrel", (2.3, 2.3, 0), (-1, -1)); c.sock("Prop_Crate", (2.4, -2.4, 0), (-1, 1))
    c.sock("Drip", (0.8, 0.2, c.H - 0.1), (0, -1))
    return c


def ch_sp_ossuary():
    c = Chunk("CH_SP_Ossuary", 1, 2, H=4.5, role="setpiece")
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    c.build_shell(decor=False)
    c.ribs('y', [-3.5, 3.5], (-3, 3), pil=False)
    # walls of skulls (east + west). PS2 pass: one textured "skulls" panel per wall (2 rows of skulls +
    # femur band per metre, T_Chunks_D cell 60) instead of ~1700 per-skull quads; same panel in both LODs.
    for side in (-1, 1):
        plane, n = side * 3.0, (-side, 0)
        if side < 0:      # leave the VACANCY niche (y -0.8..0.8, below 3.2 m) on the west wall
            wall_quad(c, n, plane, -6.6, -0.8, 0.4, 3.6, "skulls", off=0.015, cat="S")
            wall_quad(c, n, plane, 0.8, 6.6, 0.4, 3.6, "skulls", off=0.015, cat="S")
            wall_quad(c, n, plane, -0.8, 0.8, 3.2, 3.6, "skulls", off=0.015, cat="S")
        else:
            wall_quad(c, n, plane, -6.6, 6.6, 0.4, 3.6, "skulls", off=0.015, cat="S")
        wall_box(c, n, plane, -6.6, 6.6, 0.33, 0.4, 0.07, "bone2", cat="S")      # bottom femur sill
        wall_box(c, n, plane, -6.6, 6.6, 3.6, 3.68, 0.07, "bone2", cat="S")      # top femur sill
    # the VACANCY niche: a skeleton-shaped hole with a plaque (the hero fits right in)
    n, plane = (1, 0), -3.0
    wall_quad(c, n, plane, -0.15, 0.15, 2.25, 2.6, "void", off=0.03, cat="S")          # skull
    wall_quad(c, n, plane, -0.25, 0.25, 1.35, 2.2, "void", off=0.03, cat="S")          # ribs
    wall_quad(c, n, plane, -0.55, -0.3, 1.4, 2.15, "void", off=0.03, cat="S")          # arms
    wall_quad(c, n, plane, 0.3, 0.55, 1.4, 2.15, "void", off=0.03, cat="S")
    wall_quad(c, n, plane, -0.24, -0.06, 0.35, 1.3, "void", off=0.03, cat="S")         # legs
    wall_quad(c, n, plane, 0.06, 0.24, 0.35, 1.3, "void", off=0.03, cat="S")
    wall_box(c, n, plane, -0.75, 0.75, 2.78, 3.1, 0.05, "wood1", cat="S")
    pixel_text(c, "VACANCY", n, plane + 0.02, -0.66, 3.03, px=0.045, key="bone0")
    # central skull pyramid (landmark)
    # PS2 pass: the tiers wear the "skulls" texture; only the four bottom corners keep modelled skulls
    for (hs, z0, z1) in ((1.0, 0.0, 0.5), (0.7, 0.5, 1.0), (0.4, 1.0, 1.45)):
        c.mb.box(-hs, -hs, z0, hs, hs, z1, "skulls", skip=('b',))
    for (x, y, yaw) in ((-0.85, -1.0, 0.0), (0.85, -1.0, 0.0), (-0.85, 1.0, math.pi), (0.85, 1.0, math.pi)):
        c.skull(x, y, 0.08, yaw + math.pi, 0.22)
    if HERO_PROPS:
        c.hero("Skull", (0.0, 0.0, 1.45), (0, -1))
    else:
        c.skull(0, 0, 1.45, math.pi, 0.45)
    c.candle(-0.25, 0.25, 1.45, 0.22); c.candle(0.25, -0.25, 1.45, 0.3)
    c.col("Block", -1.0, -1.0, 0, 1.0, 1.0, 1.45)
    c.mark_blocked(-1.0, -1.0, 1.0, 1.0)
    c.bones_scatter(1.5, -4.0, 0.8, 5); c.bones_scatter(-1.6, 4.5, 0.7, 4)
    c.torch(3.0, -4.0, (-1, 0)); c.torch(-3.0, 4.2, (1, 0))
    c.sock("Landmark", (0.0, 0.0, 1.9), (0, -1))
    c.sock("Evidence", (-2.5, 0.0, 0.0), (1, 0))        # in front of the VACANCY niche
    c.sock("Evidence", (1.6, 5.8, 0.0), (0, -1))
    c.sock("Candle", (-0.25, 0.25, 1.45), (0, -1))
    c.sock("Prop_Bones", (2.2, -6.0, 0), (-1, 0))
    c.sock("Drip", (0.0, -3.0, c.H - 0.1), (0, -1))
    return c


def ch_room_chapel():
    c = Chunk("CH_Room_Chapel", 1, 2, H=6.0, role="room")
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('E', 0)
    c.build_shell()
    # pews (crouch-height cover) — east side starts past the side door
    rows = [-4.6, -3.0, -1.4, 0.2, 1.8]
    for y in rows:
        for (x0, x1) in ((-2.8, -1.0), (1.0, 2.8)):
            if x0 > 0 and y < -2.0:
                continue
            c.mb.box(x0, y, 0.42, x1, y + 0.45, 0.5, "wood1")
            c.mb.box(x0, y + 0.45, 0.0, x1, y + 0.55, 1.05, "wood2")
            for xe in (x0, x1 - 0.08):
                c.mb.box(xe, y, 0, xe + 0.08, y + 0.55, 0.8, "wood2")
            c.col("Rail", x0, y, 0, x1, y + 0.55, 1.05)
            c.mark_blocked(x0, y, x1, y + 0.55)
    # stepped dais, altar with red cloth, tall wooden cross
    c.mb.box(-3, 4.0, 0, 3, 7, 0.25, "wall3", skip=('b',))
    c.col("Floor", -3, 4.0, -0.5, 3, 7, 0.25)
    c.mb.box(-1.0, 5.2, 0.25, 1.0, 6.0, 1.25, "wall4", skip=('b',))
    c.mb.box(-0.35, 5.15, 0.4, 0.35, 6.05, 1.27, "red0")
    c.col("Block", -1.0, 5.2, 0.25, 1.0, 6.0, 1.25)
    c.mb.box(-0.12, 6.6, 0.25, 0.12, 6.85, 3.0, "wood2"); c.mb.box(-0.7, 6.6, 2.1, 0.7, 6.85, 2.35, "wood2")
    for x in (-0.8, -0.4, 0.4, 0.8):
        c.candle(x, 5.6, 1.25, 0.2 + 0.1 * (abs(x) < 0.5))
    # broken stained-glass window high on the north wall + a stone frame
    wy = 7.0
    c.mb.box(-1.45, wy - 0.15, 2.85, 1.45, wy, 3.0, "wall4"); c.mb.box(-1.45, wy - 0.15, 3.0, -1.25, wy, 5.6, "wall4")
    c.mb.box(1.25, wy - 0.15, 3.0, 1.45, wy, 5.6, "wall4")
    gl = ["glass_r", "glass_b", "glass_y", "glass_g", "glass_b", "glass_r"]
    for gx in range(5):
        for gz in range(6):
            x0 = -1.2 + gx * 0.48; z0 = 3.05 + gz * 0.42
            if gz == 5 and gx in (0, 4):
                continue
            broken = (gx, gz) in ((3, 1), (3, 2), (4, 2), (2, 1))
            wall_quad(c, (0, -1), wy, x0 + 0.03, x0 + 0.45, z0 + 0.03, z0 + 0.39, "void" if broken else gl[(gx + gz) % 6], off=0.02, cat="S")
    c.mb.obox((0.0, wy - 0.07, 5.75), (1.6, 0.14, 0.5), (0, 0.0, 0.0), "wall4")
    # moonlight shaft (separate additive object, no shadows)
    sh = MB(c.rng)
    top = [(-0.9, 6.9, 5.4), (0.9, 6.9, 5.4), (0.9, 6.9, 3.2), (-0.9, 6.9, 3.2)]
    bot = [(-1.3, 2.4, 0.02), (1.3, 2.4, 0.02), (1.3, 0.6, 0.02), (-1.3, 0.6, 0.02)]
    for i in range(4):
        j = (i + 1) % 4
        q = [top[i], top[j], bot[j], bot[i]]
        sh.poly(q, "bone0"); sh.poly(list(reversed(q)), "bone0")
    c.extra["shaft"] = sh
    # chandelier ring with candles (hanging)
    if HERO_PROPS:
        c.hero("Chandelier", (0.0, -1.0, c.H), (0, -1))
    else:
        c.ceil.hcyl((0.0, -1.0, c.H), (0.0, -1.0, 4.3), 0.02, 4, "iron0")
        c.ceil.ring(0.0, -1.0, 4.3, 0.55, 0.7, 10, "iron0"); c.ceil.ring(0.0, -1.0, 4.3, 0.7, 0.55, 10, "iron0")
        for k in range(6):
            a = k * math.pi / 3
            c.ceil.cyl(0.62 * math.cos(a), -1.0 + 0.62 * math.sin(a), 4.3, 4.5, 0.04, 4, "wax")
    c.torch(-3.0, -2.2, (1, 0)); c.torch(3.0, 2.6, (-1, 0))
    c.sock("LightShaft", (0.0, 6.8, 4.3), (0, -1))
    c.sock("Landmark", (0.0, 5.6, 1.3), (0, -1))
    c.sock("Evidence", (0.0, 5.6, 1.27), (0, -1)); c.sock("Evidence", (-2.0, -4.0, 0.0), (0, 1))   # under a pew
    for x in (-0.8, 0.8):
        c.sock("Candle", (x, 5.6, 1.25), (0, -1))
    c.sock("Hide", (-1.9, 0.9, 0.0), (0, 1)); c.sock("Hide", (1.9, -0.7, 0.0), (0, 1))
    c.sock("Spawn", (0.0, -6.0, 0.0), (0, 1))
    return c


def ch_room_prison():
    c = Chunk("CH_Room_PrisonBlock", 1, 2, role="room")
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    c.build_shell()
    cells = [(-7.0, -3.6), (-3.4, -0.1), (0.1, 3.4), (3.6, 7.0)]
    openc = {(-1, 1), (-1, 2), (1, 0), (1, 3)}
    for side in (-1, 1):
        xf = side * 1.0
        xb0, xb1 = min(xf, side * 3.0), max(xf, side * 3.0)
        for k in (1, 2, 3):                                   # dividers
            ym = (cells[k - 1][1] + cells[k][0]) / 2
            c.mb.box(xb0, ym - 0.1, 0, xb1, ym + 0.1, c.H, WALL)
            c.col("Wall", xb0, ym - 0.1, 0, xb1, ym + 0.1, c.H)
        c.mb.box(xf - 0.08, -7, 3.0, xf + 0.08, 7, c.H, "wall1")        # stone band over the bars
        c.col("Wall", xf - 0.08, -7, 3.0, xf + 0.08, 7, c.H)
        c.mb.box(xf - 0.06, -7, 2.9, xf + 0.06, 7, 3.0, "iron1")
        for k, (ya, yb) in enumerate(cells):
            ym = (ya + yb) / 2
            g0, g1 = ym - 0.6, ym + 0.6
            bars(c, 'x', xf, ya, g0, 0.0, 2.9); bars(c, 'x', xf, g1, yb, 0.0, 2.9)
            c.col("Rail", xf - 0.05, ya, 0, xf + 0.05, g0, 2.9); c.col("Rail", xf - 0.05, g1, 0, xf + 0.05, yb, 2.9)
            if (side, k) in openc:      # door leaf swung into the cell
                hx, hy = xf, g0
                ang = 0.0 if side > 0 else math.pi
                for b in range(6):
                    t = 0.1 + b * 0.2
                    px_, py_ = hx + side * t * 0.7, hy + t * 0.7
                    c.mb.cat = "D"; c.mb.cyl(px_, py_, 0.05, 2.7, 0.03, 4, "iron0", caps=(False, False)); c.mb.cat = "S"
                c.mb.obox((hx + side * 0.42, hy + 0.42, 2.6), (0.06, 1.2, 0.08), (0, 0, -side * math.pi / 4), "iron1")
                c.sock("Hide", (side * 2.1, ym, 0.0), (-side, 0))
            else:
                bars(c, 'x', xf, g0, g1, 0.05, 2.85)
                c.mb.box(xf - 0.05, g0, 1.2, xf + 0.05, g1, 1.3, "iron1")
                c.col("Rail", xf - 0.05, g0, 0, xf + 0.05, g1, 2.9)
                c.mark_blocked(xb0, ya, xb1, yb)
            # cot + bucket in every cell, tally marks on the back wall
            cx = side * 2.55
            c.mb.box(cx - 0.4, ya + 0.3, 0.35, cx + 0.4, ya + 2.0, 0.45, "wood1")
            c.mb.box(cx - 0.4, ya + 0.3, 0.0, cx + 0.4, ya + 0.4, 0.35, "wood2")
            c.mb.box(cx - 0.4, ya + 1.9, 0.0, cx + 0.4, ya + 2.0, 0.35, "wood2")
            c.mb.cat = "D"
            c.mb.cyl(side * 1.6, yb - 0.4, 0, 0.35, 0.15, 6, "wood0", r_top=0.17)
            for t in range(5 + k):
                wall_quad(c, (-side, 0), side * 3.0, ya + 0.6 + t * 0.07, ya + 0.63 + t * 0.07, 1.3, 1.7, "mortar")
            c.mb.cat = "S"
        # a prisoner who never left (closed cell)
    c.skull(-2.55, -6.2, 0.45, 0.0, 0.2); c.bones_scatter(-2.55, -5.5, 0.4, 4, z=0.45)
    c.torch(-1.0, 0.0, (1, 0), z=2.0); c.torch(1.0, 0.0, (-1, 0), z=2.0)
    c.sock("Evidence", (-2.2, 1.8, 0.0), (1, 0)); c.sock("Evidence", (2.2, -5.3, 0.0), (-1, 0))
    c.sock("Prop_Key", (-0.95, 5.0, 1.6), (1, 0))
    c.sock("Spawn", (0.0, -6.0, 0.0), (0, 1))
    c.sock("Drip", (0.0, 2.0, c.H - 0.1), (0, -1))
    return c


def ch_sp_well():
    c = Chunk("CH_SP_WellRoom", 1, 1, role="setpiece")
    c.rect(-3, -3, 3, 3); c.door('S'); c.door('W'); c.door('E')
    c.nofloor.update(c.tiles_in(-1, -1, 1, 1))
    c.build_shell()
    c.ribs('y', [-1.8, 1.8], (-3, 3))
    mb = c.mb
    # pedestal octagon (covers the 2x2 hole), rim, inner shaft (seen from inside), far water
    mb.cyl(0, 0, -0.05, 0.12, 1.42, 8, "wall3", r_top=1.36)
    mb.cyl(0, 0, 0.12, 0.8, 1.12, 8, "wall1", rot=math.pi / 8)
    mb.ring(0, 0, 0.8, 0.86, 1.12, 8, "wall4")
    n0 = len(mb.f)
    mb.cyl(0, 0, -2.5, 0.8, 0.86, 8, "wall3", caps=(False, False), rot=math.pi / 8)
    mb.cyl(0, 0, -6.0, -2.5, 0.86, 8, "void", caps=(False, False), rot=math.pi / 8)
    for i in range(n0, len(mb.f)):
        mb.f[i] = list(reversed(mb.f[i]))
    mb.poly([(0.86 * math.cos(math.pi / 8 + k * math.pi / 4), 0.86 * math.sin(math.pi / 8 + k * math.pi / 4), -5.0) for k in range(8)], "water")
    # winch: posts, roller, crank, rope + bucket (evidence)
    wm = c.scratch_mb() if HERO_PROPS else mb
    for x in (-1.0, 1.0):
        wm.box(x - 0.09, -0.09, 0.8, x + 0.09, 0.09, 2.3, "wood2")
    wm.hcyl((-1.05, 0, 2.05), (1.05, 0, 2.05), 0.1, 6, "wood1")
    wm.box(-0.2, -0.05, 2.2, 0.2, 0.05, 2.35, "wood2")
    c.mb.cat = "D"
    wm.hcyl((1.05, 0, 2.05), (1.3, 0, 2.05), 0.03, 4, "iron0"); wm.hcyl((1.3, 0, 2.05), (1.3, 0, 1.75), 0.03, 4, "iron0")
    if HERO_PROPS:
        c.hero("Winch", (0.0, 0.0, 0.8), (0, -1))
    mb.hcyl((0.0, 0.0, 1.95), (0.0, 0.0, 1.3), 0.02, 4, "rope")
    mb.cyl(0, 0, 0.95, 1.3, 0.2, 7, "wood0", r_top=0.24); mb.ring(0, 0, 1.15, 0.22, 0.25, 7, "iron0")
    c.mb.cat = "S"
    c.col("Block", -1.15, -1.15, 0, 1.15, 1.15, 0.8)
    c.col("Block", -1.1, -0.1, 0.8, -0.9, 0.1, 2.3); c.col("Block", 0.9, -0.1, 0.8, 1.1, 0.1, 2.3)
    c.mark_blocked(-1.15, -1.15, 1.15, 1.15)
    c.rubble(-2.4, 2.3, 0.4, 0.4, 0.3, 5, max_r=0.22)
    c.torch(-1.6, 3.0, (0, -1)); c.torch(1.6, 3.0, (0, -1))
    c.sock("Landmark", (0.0, 0.0, 0.8), (0, -1))
    c.sock("Evidence", (0.0, 0.0, 1.0), (0, -1))          # in the bucket
    c.sock("Prop", (-2.3, -2.3, 0), (1, 1)); c.sock("Prop", (2.3, 2.3, 0), (-1, -1))
    c.sock("Drip", (0.0, 0.0, c.H - 0.1), (0, -1)); c.sock("Drip", (2.0, -2.0, c.H - 0.1), (0, -1))
    c.sock("Spawn", (-2.2, 0.0, 0.0), (1, 0))
    c.extra["well"] = {"rim_height": 0.8, "shaft_depth": 6.0}
    return c


def ch_room_mausoleum():
    c = Chunk("CH_Room_Mausoleum", 1, 2, H=5.0, role="room")
    c.rect(-3, -7, 3, 7); c.door('S')
    c.build_shell()
    lids, lidcols = [], []
    c.mb.box(-3, 3.4, 0, 3, 7, 0.25, "wall3", skip=('b',))
    c.col("Floor", -3, 3.4, -0.5, 3, 7, 0.25)

    def sarcophagus(cx, cy, w, l, h, z0, effigy):
        mb = c.mb
        mb.box(cx - w / 2 - 0.1, cy - l / 2 - 0.1, z0, cx + w / 2 + 0.1, cy + l / 2 + 0.1, z0 + 0.15, "wall3", skip=('b',))
        mb.box(cx - w / 2, cy - l / 2, z0 + 0.15, cx + w / 2, cy + l / 2, z0 + h, "wall2", skip=('b', 't'))
        mb.box(cx - w / 2 + 0.08, cy - l / 2 + 0.08, z0 + 0.3, cx + w / 2 - 0.08, cy + l / 2 - 0.08, z0 + 0.32, "void")
        for sx in (-1, 1):
            mb.box(cx + sx * (w / 2 - 0.08), cy - l / 2, z0 + 0.3, cx + sx * w / 2, cy + l / 2, z0 + h, "wall2", skip=('b',))
        for sy in (-1, 1):
            mb.box(cx - w / 2, cy + sy * (l / 2 - 0.08), z0 + 0.3, cx + w / 2, cy + sy * l / 2, z0 + h, "wall2", skip=('b',))
        c.col("Block", cx - w / 2 - 0.1, cy - l / 2 - 0.1, z0, cx + w / 2 + 0.1, cy + l / 2 + 0.1, z0 + h)
        c.mark_blocked(cx - w / 2 - 0.1, cy - l / 2 - 0.1, cx + w / 2 + 0.1, cy + l / 2 + 0.1)
        lid = MB(c.rng)                         # pivot at the lid's centre bottom (slides along local X/Z)
        lid.box(-w / 2 - 0.06, -l / 2 - 0.06, 0.0, w / 2 + 0.06, l / 2 + 0.06, 0.14, "wall4")
        if effigy and HERO_PROPS:               # Meshy knight effigy rides the lid (HERO under SM_*_Lid_NN)
            c.hero("Effigy", (cx, cy, z0 + h + 0.14), (0, -1), part="Lid_%02d" % (len(lids) + 1))
        elif effigy:                            # knight effigy on the lid
            lid.box(-0.18, l / 2 - 0.55, 0.14, 0.18, l / 2 - 0.2, 0.36, "wall2")
            lid.box(-0.28, -l / 2 + 0.35, 0.14, 0.28, l / 2 - 0.6, 0.3, "wall2")
            lid.box(-0.06, -0.2, 0.3, 0.06, 0.4, 0.36, "wall0")
        lids.append((lid, (cx, cy, z0 + h)))
        lidcols.append(((cx, cy, z0 + h + 0.07), (w + 0.12, l + 0.12, 0.14)))
        c.sock("Evidence", (cx, cy, z0 + 0.32), (0, -1))

    sarcophagus(-1.6, -1.6, 0.95, 2.1, 0.85, 0.0, False)
    sarcophagus(1.6, -1.6, 0.95, 2.1, 0.85, 0.0, False)
    sarcophagus(0.0, 5.2, 1.3, 2.6, 0.95, 0.25, True)
    # angel statues at the entrance (silhouettes), urns, wreaths
    for x in (-2.3, 2.3):
        s = 1 if x > 0 else -1
        c.mb.box(x - 0.4, -6.4, 0, x + 0.4, -5.6, 0.6, "wall3", skip=('b',))
        st = c.scratch_mb() if HERO_PROPS else c.mb
        st.box(x - 0.22, -6.2, 0.6, x + 0.22, -5.8, 1.9, "wall4")
        st.box(x - 0.15, -6.15, 1.9, x + 0.15, -5.85, 2.2, "wall4")
        for w_ in (-1, 1):
            st.obox((x + w_ * 0.32, -5.85, 1.75), (0.12, 0.6, 1.1), (0.25, w_ * 0.35, 0), "wall2")
        if HERO_PROPS:
            c.hero("Angel", (x, -6.0, 0.6), (0, -1))
        c.col("Block", x - 0.4, -6.4, 0, x + 0.4, -5.6, 2.2)
        c.mark_blocked(x - 0.4, -6.4, x + 0.4, -5.6)
    c.mb.cat = "D"
    for (x, y) in ((-2.5, 3.0), (2.5, 3.0), (-2.6, 6.5), (2.6, 6.5)):
        c.mb.cyl(x, y, 0 if y < 3.4 else 0.25, (0.5 if y < 3.4 else 0.75), 0.16, 7, "brass", r_top=0.1)
    c.mb.cat = "S"
    c.extra["lids"] = lids
    c.extra["lid_colliders"] = lidcols
    c.torch(-3.0, 1.0, (1, 0)); c.torch(3.0, 1.0, (-1, 0))
    for (x, y) in ((-1.0, 3.9), (1.0, 3.9)):
        c.candle(x, y, 0.25, 0.25); c.sock("Candle", (x, y, 0.25), (0, 1))
    c.sock("Landmark", (0.0, 5.2, 1.4), (0, -1))
    c.sock("Spawn", (0.0, -5.0, 0.0), (0, 1))
    c.sock("Drip", (0.0, 1.0, c.H - 0.1), (0, -1))
    c.extra["lids_note"] = "SM_*_Lid_01..03 slide along local X/Z to open (each has its own COL_Lid child)"
    return c


def ch_sp_sewer():
    c = Chunk("CH_SP_SewerChannel", 1, 2, role="setpiece", wall_z0=-1.25)
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    chan = set(c.tiles_in(1, -7, 3, 7))
    c.nofloor.update(chan)
    c.build_shell()
    c.ribs('y', [-3.5, 3.5], (-3, 3), pil=False)
    CZ, WL = -1.0, -0.55
    for (i, j) in chan:
        x, y = c.t2m(i, j)
        if y >= -7.0:
            c.slab(x, y, CZ, ["slime", "moss", "floor3"])
    c.col("Floor", 1, -6.75, CZ - 0.5, 3, 7, CZ)
    # curb/liner between walkway and channel, north liner with a culvert mouth
    for (ya, yb) in ((-7.0, -6.0), (-4.6, 7.0)):        # curb, open where the side stair goes down
        c.mb.box(0.8, ya, CZ, 1.0, yb, 0.06, "wall3", skip=('b',))
        c.col("Rail", 0.8, ya, CZ, 1.0, yb, 0.06)
    c.mb.box(0.8, -6.0, CZ, 1.0, -4.6, 0.0, "wall3", skip=('b',))
    c.col("Floor", 0.8, -6.0, CZ, 1.0, -4.6, 0.0)
    c.mb.box(1.0, -7.0, CZ, 3.0, -6.75, 0.0, "wall3", skip=('b',))
    c.col("Wall", 1.0, -7.0, CZ, 3.0, -6.75, 0.0)
    c.mb.box(1.0, 6.75, CZ, 3.0, 7.0, 0.0, "wall3", skip=('b',))
    c.col("Wall", 1.0, 6.75, CZ, 3.0, 7.0, 0.0)
    c.mb.poly([(1.3, 6.74, CZ), (2.7, 6.74, CZ), (2.7, 6.74, -0.15), (2.0, 6.74, 0.0), (1.3, 6.74, -0.15)], "void")
    # side stair down from the walkway into the channel (rises of 0.25, 1.4 m wide)
    for s_, (x0, x1) in enumerate(((1.0, 1.4), (1.4, 1.8), (1.8, 2.2))):
        c.mb.box(x0, -6.0, CZ, x1, -4.6, -0.25 * (s_ + 1), ["wall1", "wall0"], skip=('b',))
        c.col("Floor", x0, -6.0, CZ, x1, -4.6, -0.25 * (s_ + 1))
    # footbridge to the grate platform
    for k in range(5):
        y = 0.3 + k * 0.28
        c.mb.box(1.0, y, -0.1, 2.25, y + 0.25, 0.0, WOOD)
    c.col("Floor", 1.0, 0.3, -0.3, 2.2, 1.7, 0.0)
    c.mb.box(2.2, 0.3, CZ, 3.0, 1.7, 0.0, "wall1", skip=('b',))
    c.col("Floor", 2.2, 0.3, CZ, 3.0, 1.7, 0.0)
    # big outflow grate on the east wall above the platform + trickle
    n = (-1, 0)
    wall_quad(c, n, 3.0, 0.35, 1.65, 0.3, 1.6, "void", off=0.01, cat="S")
    wall_box(c, n, 3.0, 0.2, 1.8, 0.15, 0.3, 0.12, "wall4", cat="S")
    wall_box(c, n, 3.0, 0.2, 1.8, 1.6, 1.75, 0.12, "wall4", cat="S")
    for k in range(6):
        y = 0.45 + k * 0.22
        c.mb.cat = "D"; c.mb.cyl(2.93, y, 0.3, 1.6, 0.035, 4, "rust", caps=(False, False)); c.mb.cat = "S"
    # pipes on the west wall, water + volume
    c.mb.hcyl((-2.8, -7, 2.6), (-2.8, 7, 2.6), 0.16, 6, "rust")
    c.mb.hcyl((-2.85, -7, 3.0), (-2.85, 7, 3.0), 0.1, 6, "iron0")
    w = MB(c.rng)
    w.poly([(1.0, -6.75, WL), (3.0, -6.75, WL), (3.0, 6.75, WL), (1.0, 6.75, WL)], "water")
    c.extra["water"] = w
    c.extra["water_vol"] = ((2.0, 0.3, (CZ + 0.0) / 2), (2.0, 12.8, -CZ))
    c.mb.cat = "D"
    for (y, a) in ((-3.0, 0.3), (4.2, -0.5)):
        c.mb.obox((2.0, y, WL + 0.03), (0.9, 0.2, 0.05), (0, 0, a), WOOD)
    c.mb.cat = "S"
    c.torch(-3.0, -4.0, (1, 0)); c.torch(-3.0, 4.0, (1, 0))
    c.sock("Grate", (2.95, 1.0, 0.95), (-1, 0))
    c.sock("Evidence", (2.6, 1.0, 0.0), (-1, 0))          # on the grate platform across the channel
    c.sock("Evidence", (2.0, -3.5, CZ), (0, -1))          # in the water (reach it by the south steps)
    for y in (-2.0, 2.5, 5.5):
        c.sock("Drip", (1.8, y, c.H - 0.1), (0, -1))
    c.sock("Prop", (-2.3, 6.0, 0), (1, 0))
    c.extra["sewer"] = {"channel_x": [1, 3], "channel_floor": CZ, "water_level": WL}
    return c


def ch_sp_cave():
    c = Chunk("CH_SP_CaveTransition", 1, 2, H=5.0, role="setpiece", floor_keys=ROCK + ["moss"])
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    c.wall_visual = rock_walls(c, masonry_below_y=-4.5)
    c.slab_jitter = 0.035
    c.build_shell()
    # natural column (stalagnate), boulders, stalactites, crystal cluster, puddle
    mb = c.mb
    col_mb = c.scratch_mb() if HERO_PROPS else mb
    col_mb.cyl(0.6, 0.5, 0.0, 1.2, 0.85, 7, "rock1", r_top=0.6)
    col_mb.cyl(0.6, 0.5, 1.2, 3.6, 0.6, 7, "rock0", r_top=0.45, rot=0.4)
    col_mb.cyl(0.6, 0.5, 3.6, c.H, 0.45, 7, "rock2", r_top=0.8, rot=0.8)
    if HERO_PROPS:
        c.hero("Stalagnate", (0.6, 0.5, 0.0), (0, -1))
    c.col("Block", -0.1, -0.2, 0, 1.3, 1.2, c.H)
    c.mark_blocked(-0.1, -0.2, 1.3, 1.2)
    for (x, y, r) in ((-2.3, 2.6, 0.55), (2.3, -2.0, 0.45), (2.4, 5.5, 0.5)):
        mb.obox((x, y, r * 0.6), (r * 2, r * 1.7, r * 1.3), (0.2, 0.1, c.rng.uniform(0, 3)), ROCK)
        c.col("Block", x - r, y - r * 0.85, 0, x + r, y + r * 0.85, r * 1.2)
        c.mark_blocked(x - r, y - r * 0.85, x + r, y + r * 0.85)
    for k in range(16):
        x, y = c.rng.uniform(-2.6, 2.6), c.rng.uniform(-3.5, 6.6)
        c.ceil.cyl(x, y, c.H - c.rng.uniform(0.5, 1.3), c.H, 0.03, 5, ROCK, r_top=c.rng.uniform(0.15, 0.3), caps=(True, False))
    for k in range(10):
        c.ceil.obox((c.rng.uniform(-2.5, 2.5), c.rng.uniform(-4, 6.6), c.H - 0.2), (1.2, 1.0, 0.5),
                    (c.rng.uniform(-0.3, 0.3), c.rng.uniform(-0.3, 0.3), c.rng.uniform(0, 3)), ROCK)
    mb.cat = "D"
    for k in range(7):
        a = k * 0.9
        mb.obox((-2.5 + 0.18 * math.cos(a), 4.6 + 0.2 * math.sin(a), 0.3), (0.12, 0.12, 0.7 - 0.05 * k),
                (0.35 * math.cos(a), 0.35 * math.sin(a), a), "crystal")
    mb.cat = "S"
    puddle(c, 1.6, -3.2, 0.7, 0.45)
    c.torch(-3.0, -5.8, (1, 0))
    c.sock("Landmark", (0.6, 0.5, 1.5), (0, -1))
    c.sock("Evidence", (-2.1, 4.0, 0.0), (1, 0))         # by the crystals
    c.sock("Prop_Rock", (2.0, 3.0, 0), (-1, 0))
    for y in (-3.2, 2.5, 5.0):
        c.sock("Drip", (1.6 if y < 0 else -1.0, y, c.H - 0.3), (0, -1))
    c.extra["transition"] = "masonry south of y -4.5 (S door), natural rock north of it (N door)"
    return c


def ch_room_spiralhall():
    c = Chunk("CH_Room_SpiralHall", 2, 2, H=7.0, role="room")
    c.rect(-7, -7, 7, 7); c.door('S', 0); c.door('S', 1); c.door('W', 0)
    c.build_shell()
    mb = c.mb
    cx = cy = 4.9
    rise, n_steps, step = 0.25, 14, math.radians(22.5)
    a_top = math.pi
    mb.cyl(cx, cy, 0.0, 14 * rise + 1.0, 0.28, 8, "wall1")
    c.col("Block", cx - 0.28, cy - 0.28, 0, cx + 0.28, cy + 0.28, 4.5)
    for i in range(n_steps):
        a = a_top - (n_steps - 1 - i) * step
        zt = (i + 1) * rise
        sector(mb, cx, cy, 0.28, 1.75, a - step / 2, a + step / 2, zt - 0.22, zt, ["wall2", "wall4"])
        for (ra, rb) in ((0.28, 1.0), (1.0, 1.75)):
            rm = (ra + rb) / 2
            c.col_o("Floor", (cx + rm * math.cos(a), cy + rm * math.sin(a), zt - 0.125), (rb - ra, 2 * rm * math.sin(step / 2) * 0.98, 0.25), (0, 0, a))
        if 3 <= i <= 11:      # outer railing above the drop (open at the top onto the landing)
            ro = 1.7
            mb.cat = "D"
            mb.box(cx + ro * math.cos(a) - 0.04, cy + ro * math.sin(a) - 0.04, zt, cx + ro * math.cos(a) + 0.04, cy + ro * math.sin(a) + 0.04, zt + 1.0, "iron0")
            mb.cat = "S"
            c.col_o("Rail", (cx + ro * math.cos(a), cy + ro * math.sin(a), zt + 0.5), (0.08, 0.62, 1.0), (0, 0, a))
    mb.hcyl((cx + 1.7 * math.cos(a_top - 10 * step), cy + 1.7 * math.sin(a_top - 10 * step), 0.25 * 4 + 1.0),
            (cx + 1.7 * math.cos(a_top - 2 * step), cy + 1.7 * math.sin(a_top - 2 * step), 3.0 + 1.0), 0.04, 4, "iron1")
    c.mark_blocked(cx - 1.75, cy - 1.75, 7, 7)
    # balcony along the north wall + landing at the stair top
    BZ = 3.5
    mb.box(-7, 5.0, BZ - 0.3, 2.4, 7, BZ, "wood1")
    mb.box(2.4, 4.0, BZ - 0.3, 3.5, 7, BZ, "wood1")
    c.col("Floor", -7, 5.0, BZ - 0.3, 2.4, 7, BZ); c.col("Floor", 2.4, 4.0, BZ - 0.3, 3.5, 7, BZ)
    for x in (-6.5, -4.0, -1.5, 1.0):
        mb.box(x - 0.15, 5.0, BZ - 0.8, x + 0.15, 7, BZ - 0.3, "wood2")
    mb.box(-7, 4.95, BZ, 2.4, 5.08, BZ + 1.0, "wood2"); mb.box(2.4, 3.95, BZ, 3.3, 4.08, BZ + 1.0, "wood2")
    for x in [-6.8 + 0.6 * k for k in range(16)]:
        mb.cat = "D"; mb.box(x, 4.97, BZ, x + 0.06, 5.06, BZ + 1.0, "wood0"); mb.cat = "S"
    c.col("Rail", -7, 4.93, BZ, 2.4, 5.1, BZ + 1.05); c.col("Rail", 2.4, 3.93, BZ, 3.3, 4.1, BZ + 1.05)
    # long table + benches, hanging chandelier, banners
    mb.box(-3.0, -1.4, 0.8, 1.0, -0.4, 0.9, "wood1")
    for x in (-2.9, 0.9):
        mb.box(x - 0.06, -1.3, 0, x + 0.06, -0.5, 0.8, "wood2")
    for y in (-2.0, 0.2):
        mb.box(-2.8, y - 0.15, 0.42, 0.8, y + 0.15, 0.5, "wood2")
    c.col("Rail", -3.0, -1.4, 0, 1.0, -0.4, 0.9)
    c.mark_blocked(-3.0, -1.4, 1.0, -0.4)
    if HERO_PROPS:
        c.hero("Chandelier", (-1.0, -0.9, c.H), (0, -1), scale=1.45)
    else:
        c.ceil.hcyl((-1.0, -0.9, c.H), (-1.0, -0.9, 4.6), 0.02, 4, "iron0")
        c.ceil.ring(-1.0, -0.9, 4.6, 0.9, 1.1, 12, "iron0"); c.ceil.ring(-1.0, -0.9, 4.6, 1.1, 0.9, 12, "iron0")
        for k in range(8):
            a = k * math.pi / 4
            c.ceil.cyl(-1.0 + 1.0 * math.cos(a), -0.9 + 1.0 * math.sin(a), 4.6, 4.85, 0.05, 4, "wax")
    for y in (-1.5, 2.5):
        mb.box(-7.0, y - 0.6, 2.0, -6.95, y + 0.6, 5.0, "red0")
    c.torch(-7.0, 0.5, (1, 0)); c.torch(7.0, -2.0, (-1, 0)); c.torch(-2.0, 7.0, (0, -1), z=BZ + 2.0)
    c.sock("Landmark", (cx, cy, 3.6), (-1, 0))
    c.sock("Evidence", (-6.2, 6.2, BZ), (1, -1))        # far end of the balcony (vantage + reward)
    c.sock("Evidence", (-1.0, -0.9, 0.9), (0, 1))
    c.sock("Prop", (-6.0, -6.0, 0), (1, 1)); c.sock("Prop", (6.0, -6.0, 0), (-1, 1)); c.sock("Prop", (0.0, 6.2, BZ), (0, -1))
    c.sock("Spawn", (0.0, -5.0, 0.0), (0, 1))
    c.sock("Drip", (3.0, -3.0, c.H - 0.1), (0, -1))
    c.extra["spiral"] = {"centre": [cx, cy], "steps": n_steps, "rise": rise, "balcony": BZ}
    return c


def ch_corridor_chewed():
    c = Chunk("CH_Corridor_Chewed", 1, 1, role="corridor", floor_keys=FLOOR[:3] + DIRT[:2])
    c.rect(-2, -4, 2, 4); c.door('N'); c.door('S')
    c.build_shell()
    c.ribs('y', [-2.0, 2.0], (-2, 2))
    mb = c.mb
    mb.cat = "D"
    # bite notches on every jamb + pilaster edge
    for y in (-3.5, 3.5):
        for x in (-1.5, 1.5):
            for z in (0.5, 0.9, 1.3):
                mb.obox((x, y + c.rng.uniform(-0.3, 0.3), z), (0.12, 0.18, 0.14), (0.4, 0.3, 0.7), "void")
    mb.cat = "S"
    for (x, y, z, f) in ((2.0, -2.8, 0.4, (-1, 0)), (2.0, 0.6, 0.8, (-1, 0)), (-2.0, -1.0, 0.3, (1, 0)),
                         (-2.0, 1.8, 0.9, (1, 0)), (2.0, 2.6, 0.2, (-1, 0))):
        c.scratches(x, y, z, f, 4)
    # chewed door leaf leaning on the west wall, fetched junk pile, dug holes
    mb.obox((-1.8, 0.4, 1.1), (0.12, 1.3, 2.2), (0, -0.2, 0.05), "wood1")
    mb.cat = "D"
    for z in (0.5, 1.2, 1.9):
        mb.obox((-1.73, 0.4, z), (0.06, 1.35, 0.12), (0, -0.2, 0.05), "iron0")
    mb.cat = "S"
    c.col("Block", -2.0, -0.25, 0, -1.65, 1.05, 2.2)
    mb.cat = "D"
    for k in range(6):
        mb.hcyl((1.2 + c.rng.uniform(-0.3, 0.3), -1.4 + c.rng.uniform(-0.3, 0.3), 0.05),
                (1.6 + c.rng.uniform(-0.3, 0.3), -0.8 + c.rng.uniform(-0.3, 0.3), 0.15), 0.04, 4, "wood0")
    mb.box(1.3, -2.0, 0, 1.6, -1.75, 0.25, "rust"); mb.box(1.45, -2.0, 0, 1.6, -1.55, 0.1, "rust")    # a boot
    mb.cat = "S"
    for (x, y) in ((-0.8, -2.4), (0.6, 2.2)):
        mb.cyl(x, y, 0.0, 0.2, 0.6, 7, "dirt1", r_top=0.25)
        mb.cyl(x, y, 0.2, 0.22, 0.25, 7, "void", r_top=0.0, caps=(False, False))
    c.bones_scatter(0.0, 0.0, 1.5, 9)
    c.sock("Evidence", (1.4, -1.2, 0.0), (0, 1))        # in Chucho's junk pile
    c.sock("Prop", (-1.3, -3.0, 0), (1, 0))
    c.sock("Drip", (0.4, -1.0, c.H - 0.1), (0, -1))
    c.wall_torch(2.0, 1.2, (-1, 0))
    return c


def ch_sp_trap():
    c = Chunk("CH_SP_TrapCorridor", 1, 1, role="setpiece", wall_z0=-1.45)
    c.rect(-2, -4, 2, 4); c.door('N'); c.door('S')
    pit = set(c.tiles_in(-2, -2, 1, 2))
    c.nofloor.update(pit)
    c.build_shell()
    c.ribs('y', [-2.6, 2.6], (-2, 2))
    PZ, LX = -1.2, 0.8
    mb = c.mb
    # ledge strip (x 0.8..1) + pit bottom + liners
    for y in range(-2, 2):
        mb.box(LX, y, -0.05, 1.0, y + 1, 0.0, "floor2", skip=('b',))
    c.col("Floor", LX, -2, -0.5, 2.0, 2, 0.0)
    mb.box(LX - 0.2, -2, PZ, LX, 2, 0.0, "wall3", skip=('b',))
    for (i, j) in pit:
        x, y = c.t2m(i, j)
        if x + 1 <= LX + 1e-6:
            c.slab(x, y, PZ, DIRT)
        else:
            mb.box(x, y, PZ - 0.05, LX, y + 1, PZ, "dirt0", skip=('b',))
    c.col("Floor", -2, -2, PZ - 0.5, LX, 2, PZ)
    for sy in (-1, 1):
        y0, y1 = (2.0, 2.25) if sy > 0 else (-2.25, -2.0)
        mb.box(-2, y0, PZ, LX, y1, 0.0, WALL, skip=('t',))
    # spikes
    for gx in range(5):
        for gy in range(6):
            x = -1.75 + gx * 0.5 + (0.25 if gy % 2 else 0)
            y = -1.75 + gy * 0.68
            if x < LX - 0.25:
                mb.cyl(x, y, PZ, PZ + c.rng.uniform(0.55, 0.75), 0.09, 4, ["iron0", "rust"], r_top=0.0, caps=(False, False))
    # an unlucky skeleton on the spikes, a warning skull on a stick, a narrow plank shortcut
    c.bones_scatter(-1.0, 0.3, 0.5, 5, z=PZ + 0.4)
    c.skull(-0.8, 0.6, PZ + 0.5, 0.7)
    mb.box(1.3, -3.2, 0, 1.36, -3.14, 1.2, "wood2")
    c.skull(1.33, -3.17, 1.2, math.pi, 0.22)
    pl_a = math.atan2(4.4, 0.8)
    mb.obox((-0.9, 0.0, -0.05), (0.5, 4.5, 0.1), (0, 0, -(math.pi / 2 - pl_a)), WOOD)
    c.col_o("Floor", (-0.9, 0.0, -0.1), (0.5, 4.5, 0.2), (0, 0, -(math.pi / 2 - pl_a)))
    c.extra["trap_vol"] = ((-0.6, 0.0, PZ + 0.45), (2.8, 4.0, 0.9))
    c.wall_torch(-2.0, -3.0, (1, 0))
    c.sock("Trap", (-0.6, 0.0, PZ), (0, 1))
    c.sock("Evidence", (-0.9, 0.0, 0.0), (0, 1))          # halfway along the narrow plank (risk/reward)
    c.sock("Prop_Warning", (1.5, -3.4, 0), (0, -1))
    c.sock("Drip", (-0.6, 0.6, c.H - 0.1), (0, -1))
    c.extra["trap"] = {"pit_x": [-2, LX], "pit_y": [-2, 2], "depth": -PZ, "ledge_width": 2.0 - LX,
                       "plank_width": 0.5, "VOL_Trap": "trigger over the spikes (hook a kill / damage)"}
    return c


def ch_junction_brokenfloor():
    c = Chunk("CH_Junction_BrokenFloor", 1, 1, role="corridor", wall_z0=-1.45)
    c.rect(-4, -2, 4, 2); c.rect(-2, -4, 2, 4)
    for s in "NESW":
        c.door(s)
    hole = set(c.tiles_in(-3, 0, 0, 2))
    c.nofloor.update(hole)
    c.build_shell()
    c.ribs('x', [-2.6, 2.6], (-2, 2)); c.ribs('y', [-2.6, 2.6], (-2, 2))
    mb, CZ = c.mb, -1.2
    # liners (south edge y 0, west edge x -3, east edge x 0 beside the steps)
    mb.box(-3, -0.25, CZ, 0, 0.0, 0.0, "wall3", skip=('t', 'b'))
    mb.box(-3.25, 0, CZ, -3.0, 2, 0.0, "wall3", skip=('t', 'b'))
    mb.box(-2.0, 2.0, CZ, 0.0, 2.25, 0.0, "wall3", skip=('t', 'b'))
    for (y0, y1) in ((0.0, 0.2), (1.4, 2.0)):
        mb.box(-0.4, y0, CZ, 0.0, y1, 0.0, "wall3", skip=('b',))
        c.col("Floor", -0.4, y0, CZ, 0.0, y1, 0.0)
    # cellar floor + broken grate over the deeper level, stair of 0.24 m rises
    for (i, j) in hole:
        x, y = c.t2m(i, j)
        if x < -2.0 + 1e-6:
            c.slab(x, y, CZ, DIRT)
    c.col("Floor", -3, 0, CZ - 0.5, -2.0, 2, CZ)
    steps = [(-2.0, -1.6, -0.96), (-1.6, -1.2, -0.72), (-1.2, -0.8, -0.48), (-0.8, -0.4, -0.24), (-0.4, 0.0, 0.0)]
    for (x0, x1, zt) in steps:
        mb.box(x0, 0.2, CZ, x1, 1.4, zt, ["wall1", "wall0"], skip=('b',))
        c.col("Floor", x0, 0.2, CZ, x1, 1.4, zt)
    for (x0, x1) in ((-2.0, -0.4),):
        mb.box(x0, 0.0, CZ, x1, 0.2, CZ + 0.3, "rock1"); mb.box(x0, 1.4, CZ, x1, 2.0, CZ + 0.3, "rock1")
    mb.poly([(-2.9, 1.0, CZ + 0.005), (-2.1, 1.0, CZ + 0.005), (-2.1, 1.9, CZ + 0.005), (-2.9, 1.9, CZ + 0.005)], "void")
    mb.cat = "D"
    for k in range(4):
        x = -2.8 + k * 0.22
        mb.box(x, 1.0 + (0.3 if k == 2 else 0), CZ, x + 0.05, 1.9, CZ + 0.04, "iron0")
    mb.cat = "S"
    # tilted broken slabs on the rim, rubble in the cellar, a chain down into the dark
    for (x, y, a) in ((-1.5, -0.1, 0.3), (-2.7, 0.15, -0.4), (-0.5, 2.05, 0.2)):
        mb.obox((x, y, -0.15), (0.9, 0.5, 0.1), (a, 0.2, 0.1), FLOOR)
    c.rubble(-2.5, 0.6, 0.4, 0.4, 0.35, 6, z=CZ, max_r=0.22)
    mb.hcyl((-1.5, 1.0, c.H), (-1.5, 1.0, 0.6), 0.03, 4, "iron0")
    c.torch(2.6, -2.0, (0, 1))
    c.sock("Evidence", (-2.5, 1.4, CZ), (1, 0))           # down in the cellar
    c.sock("Drip", (-1.5, 1.0, c.H - 0.1), (0, -1))
    c.sock("Prop", (1.4, -1.4, 0), (-1, 1))
    c.extra["broken_floor"] = {"hole": [[-3, 0], [0, 2]], "cellar_floor": CZ, "steps": "x -2..0, rises 0.24 m",
                               "grate": "visual hole to the level below, collider holds"}
    return c


def ch_corridor_straight_b():
    c = Chunk("CH_Corridor_Straight_B", 1, 1, role="corridor")
    c.rect(-2, -4, 2, 4); c.door('N'); c.door('S')
    c.build_shell()
    c.ribs('y', [-2.0], (-2, 2))
    mb = c.mb
    # wall breach on the east side: dark hole + rubble spilling in
    wall_quad(c, (-1, 0), 2.0, -0.9, 0.9, 0.6, 2.4, "void", off=0.02, cat="S")
    c.rubble(1.6, 0.0, 0.5, 1.1, 0.7, 22, max_r=0.35, cat="S")
    c.col("Block", 1.2, -1.0, 0, 2.0, 1.0, 0.7)
    c.mark_blocked(1.2, -1.0, 2.0, 1.0)
    # the fallen rib lying across the floor (low, walkable over)
    mb.obox((-0.2, 2.0, 0.14), (4.2, 0.35, 0.28), (0, 0.0, 0.25), "wall1")
    mb.cat = "D"
    for k in range(9):
        x, y = c.rng.uniform(-1.8, 1.0), c.rng.uniform(-3, 3)
        mb.box(x - 0.02, y - 0.02, c.H - c.rng.uniform(0.6, 1.5), x + 0.02, y + 0.02, c.H, "wood2")
    for (x, y) in ((-1.2, -2.6), (0.6, 0.9), (-1.5, 3.0)):
        mb.poly([(x - 0.4, y - 0.3, 0.008), (x + 0.4, y - 0.3, 0.008), (x + 0.45, y + 0.3, 0.008), (x - 0.35, y + 0.3, 0.008)], "moss")
    mb.cat = "S"
    c.wall_torch(-2.0, -1.0, (1, 0))
    c.sock("Prop", (-1.4, 1.0, 0), (1, 0))
    c.sock("Evidence", (1.5, 1.4, 0.0), (-1, 0))          # tucked behind the rubble
    c.sock("Drip", (0.0, 0.0, c.H - 0.1), (0, -1))
    return c


def ch_corridor_corner_b():
    c = Chunk("CH_Corridor_Corner_B", 1, 1, role="corridor")
    c.rect(-2, -2, 2, 4); c.rect(-2, -2, 4, 2); c.door('N'); c.door('E')
    c.build_shell()
    c.ribs('y', [2.6], (-2, 2)); c.ribs('x', [2.6], (-2, 2))
    mb = c.mb
    # skull alcove shelf in the outer corner + a broken barrel + puddle under a drip
    mb.box(-2.0, -2.0, 0.0, -0.9, -0.9, 0.8, "wall3", skip=('b',))
    mb.box(-2.0, -2.0, 0.8, -0.8, -0.8, 0.9, "wall2")
    c.col("Block", -2.0, -2.0, 0, -0.8, -0.8, 0.9)
    for (x, y) in ((-1.7, -1.2), (-1.25, -1.6), (-1.5, -1.5)):
        c.skull(x, y, 0.9, math.pi / 4 + math.pi, 0.2)
    c.candle(-1.85, -1.85, 0.9, 0.3)
    mb.cyl(1.4, -1.4, 0.0, 0.55, 0.32, 8, "wood0", r_top=0.33)
    mb.cat = "D"
    for k in range(4):
        mb.obox((1.0 + k * 0.15, -0.9, 0.04), (0.12, 0.6, 0.04), (0, 0, 0.3 * k), "wood1")
    mb.cat = "S"
    c.col("Block", 1.05, -1.75, 0, 1.75, -1.05, 0.55)
    puddle(c, 0.4, 0.6, 0.8, 0.55)
    c.wall_torch(-2.0, 1.2, (1, 0))
    c.sock("Candle", (-1.85, -1.85, 0.9), (1, 1))
    c.sock("Drip", (0.4, 0.6, c.H - 0.1), (0, -1))
    c.sock("Prop", (1.3, 1.3, 0), (-1, -1))
    return c


def ch_corridor_t_b():
    c = Chunk("CH_Corridor_T_B", 1, 1, role="corridor")
    c.rect(-4, -2, 4, 2); c.rect(-2, -2, 2, 4); c.door('N'); c.door('E'); c.door('W')
    c.build_shell()
    c.ribs('y', [2.6], (-2, 2))
    mb = c.mb
    # half-raised portcullis in the west arm (bottom at 2.5 m), banners, weapon rack
    for k in range(9):
        y = -1.8 + k * 0.45
        mb.box(-2.65, y - 0.05, 2.5, -2.55, y + 0.05, c.H, "iron0")
        mb.cyl(-2.6, y, 2.3, 2.5, 0.05, 4, "iron1", r_top=0.0, caps=(False, False))
    for z in (2.9, 3.5):
        mb.box(-2.68, -2.0, z, -2.52, 2.0, z + 0.1, "iron1")
    c.col("Block", -2.7, -2.0, 2.4, -2.5, 2.0, c.H)
    for x in (-1.0, 1.0):
        mb.box(x - 0.45, -1.97, 1.2, x + 0.45, -1.92, 3.4, "red0" if x < 0 else "glass_b")
        mb.box(x - 0.55, -1.98, 3.4, x + 0.55, -1.9, 3.5, "wood2")
    mb.box(1.9, -1.96, 0.0, 3.4, -1.75, 1.4, "wood2")
    mb.cat = "D"
    for k in range(4):
        mb.box(2.0 + k * 0.4, -1.8, 0.1, 2.05 + k * 0.4, -1.75, 1.6, "iron1")
    mb.cat = "S"
    c.col("Block", 1.9, -2.0, 0, 3.4, -1.7, 1.6)
    c.wall_torch(0.0, -2.0, (0, 1))
    c.sock("Prop", (-1.3, 1.4, 0), (1, -1)); c.sock("Prop_Weapon", (2.6, -1.6, 0), (0, 1))
    c.sock("Drip", (0.0, 0.0, c.H - 0.1), (0, -1))
    return c


EXTRA_CHUNKS = [ch_room_library, ch_room_larder, ch_sp_ossuary, ch_room_chapel, ch_room_prison, ch_sp_well,
                ch_room_mausoleum, ch_sp_sewer, ch_sp_cave, ch_room_spiralhall, ch_corridor_chewed, ch_sp_trap,
                ch_junction_brokenfloor, ch_corridor_straight_b, ch_corridor_corner_b, ch_corridor_t_b]
# height variants (3.6 m Catacombs / 5 m Gothic + Transition corridors) of the most repeated pieces
HEIGHT_VARIANT_FNS = [ch_corridor_straight, ch_corridor_corner, ch_corridor_t, ch_corridor_cross, ch_corridor_deadend]

# generator hints: themes (SectionType names or Any), role, rarity weight (relative pick chance),
# min/max per map, suggested lit torches (keep within DynamicLightBudget: corridors 1, rooms <= 3)
TAGS = {
    "CH_Corridor_Straight": dict(themes=["Any"], weight=1.0, min=0, max=12, torches_suggested=1),
    "CH_Corridor_Corner": dict(themes=["Any"], weight=1.0, min=0, max=10, torches_suggested=1),
    "CH_Corridor_T": dict(themes=["Any"], weight=0.8, min=0, max=8, torches_suggested=1),
    "CH_Corridor_Cross": dict(themes=["Any"], weight=0.5, min=0, max=4, torches_suggested=0),
    "CH_Corridor_DeadEnd": dict(themes=["Any"], weight=0.4, min=1, max=3, torches_suggested=1),
    "CH_Corridor_Straight_B": dict(themes=["Any"], weight=0.6, min=0, max=4, torches_suggested=1),
    "CH_Corridor_Corner_B": dict(themes=["Catacombs", "Cavern", "Transition"], weight=0.5, min=0, max=3, torches_suggested=1),
    "CH_Corridor_T_B": dict(themes=["GothicRuins", "Transition"], weight=0.4, min=0, max=2, torches_suggested=1),
    "CH_Corridor_Chewed": dict(themes=["Any"], weight=0.3, min=0, max=3, torches_suggested=1, near="CH_SP_ChuchoDen"),
    "CH_Junction_BrokenFloor": dict(role="corridor", themes=["GothicRuins", "Catacombs"], weight=0.25, min=0, max=1, torches_suggested=1),
    "CH_Room_Storage": dict(themes=["Any"], weight=0.6, min=0, max=3, torches_suggested=1),
    "CH_Room_PillarHall": dict(themes=["GothicRuins", "HellPillars"], weight=0.4, min=0, max=2, torches_suggested=3),
    "CH_Room_Crypt": dict(themes=["Catacombs", "Graveyard"], weight=0.5, min=0, max=2, torches_suggested=2),
    "CH_Room_Ritual": dict(themes=["HellPillars", "GothicRuins"], weight=0.25, min=0, max=1, torches_suggested=2),
    "CH_Room_Library": dict(themes=["GothicRuins"], weight=0.3, min=0, max=1, torches_suggested=2),
    "CH_Room_Larder": dict(themes=["GothicRuins", "Transition"], weight=0.35, min=0, max=1, torches_suggested=1),
    "CH_Room_Chapel": dict(themes=["GothicRuins", "Graveyard"], weight=0.25, min=0, max=1, torches_suggested=2),
    "CH_Room_PrisonBlock": dict(themes=["GothicRuins", "Catacombs"], weight=0.3, min=0, max=1, torches_suggested=2),
    "CH_Room_Mausoleum": dict(themes=["Graveyard", "Catacombs"], weight=0.3, min=0, max=1, torches_suggested=2),
    "CH_Room_SpiralHall": dict(themes=["GothicRuins"], weight=0.2, min=0, max=1, torches_suggested=3),
    "CH_SP_CollapsedCorridor": dict(role="setpiece", themes=["Any"], weight=0.3, min=0, max=2, torches_suggested=1),
    "CH_SP_FloodedCrypt": dict(themes=["Catacombs", "Cavern"], weight=0.25, min=0, max=1, torches_suggested=2),
    "CH_SP_PitBridge": dict(themes=["Cavern", "HellPillars", "GothicRuins"], weight=0.25, min=0, max=1, torches_suggested=2),
    "CH_SP_BellTower": dict(themes=["GothicRuins", "Graveyard"], weight=0.2, min=0, max=1, torches_suggested=2),
    "CH_SP_ChuchoDen": dict(themes=["Any"], weight=1.0, min=1, max=1, torches_suggested=1),
    "CH_SP_Ossuary": dict(themes=["Catacombs"], weight=0.25, min=0, max=1, torches_suggested=2),
    "CH_SP_WellRoom": dict(themes=["Any"], weight=0.3, min=0, max=1, torches_suggested=2),
    "CH_SP_SewerChannel": dict(themes=["Catacombs", "Transition"], weight=0.25, min=0, max=1, torches_suggested=2),
    "CH_SP_CaveTransition": dict(themes=["Cavern", "Catacombs"], weight=0.4, min=0, max=2, torches_suggested=1),
    "CH_SP_TrapCorridor": dict(role="setpiece", themes=["Any"], weight=0.25, min=0, max=2, torches_suggested=1),
    "CH_Generator_Alcove": dict(role="objective", themes=["Any"], weight=1.0, min=3, max=3, torches_suggested=1),
    "CH_Extraction_Gate": dict(role="objective", themes=["Any"], weight=1.0, min=1, max=1, torches_suggested=2),
}
