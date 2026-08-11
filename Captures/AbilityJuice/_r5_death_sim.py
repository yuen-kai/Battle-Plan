"""Numerical port of BP_DeathMark at the money frame (f18, +0.200s), r5 rebuild.

Rasterises the hole quad at the measured death-shot scale (81 px per world unit on the 1024
capture, 0.956 vertical foreshortening), composites it over a synthetic deck with tile seams,
runs URP Bloom + the calibrated grade, and reports the exact statistics the r4 critic measured:

  * bright-and-saturated share (L>200 & S>0.45) on the 512 strip panel
  * the vertical luminance profile straight down the hole
  * eroded dark-core local 9x9 sigma
  * tile-seam contrast under the mass (occlusion)

`legacy=True` reproduces the shipped r4 shader so the model can be checked against the real
captured frame before anything is tuned.
"""
import importlib.util
import os
import sys

import numpy as np
from scipy import ndimage

_here = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("pipe", os.path.join(_here, "_b4_sw_pipe.py"))
pipe = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pipe)
screen, LSH, s2l, l2s = pipe.screen, pipe.LSH, pipe.s2l, pipe.l2s

# --- capture geometry, measured off shots/r4/death -------------------------------------------
FRAME = 1024
PX_PER_UNIT = 218.0 / 2.7          # one 2.7 unit cell spans 218 px at the death camera
FORESHORTEN = 0.956                # sin(73 deg): the ground's away-axis on screen
CELL = 2.7
QUAD_UNITS = 1.35 * CELL * 2.15    # HoleCells * cell * HoleQuadPad
DECK_LINEAR = 0.5215               # lands at display L185, the measured clean floor

BLOOM_THRESHOLD = 1.8
BLOOM_KNEE = 0.5 * BLOOM_THRESHOLD
BLOOM_INTENSITY = 0.55
BLOOM_SIGMA = 26.0                 # scatter 0.55 over a 1024 frame


def sat01(x):
    return np.clip(x, 0.0, 1.0)


def fwidth(a, sx, sy):
    gy, gx = np.gradient(a)
    return np.abs(gx) + np.abs(gy)


def death_hash(n):
    return np.modf(np.sin(n) * 43758.5453)[0]


def pool_distance(px, py, ax, ay, sx, sy, span, wobble, seed):
    ox, oy = (px - ax) / sx, (py - ay) / sy
    reach = np.hypot(ox, oy)
    around = np.arctan2(oy, ox)
    bound = span * (1.0 + wobble * np.sin(around * 3.0 + seed)
                    + wobble * 0.45 * np.sin(around * 5.0 - seed * 1.7))
    return reach / np.maximum(bound, 1e-5)


class Cfg:
    """Every value here is also a shader property or a DeathCollapseRunner constant."""
    # --- boundary (unchanged from r4: the critic passed the silhouette) ---
    radius = 0.465
    rag = 0.36
    rag_count = 3.0
    close = 0.0
    close_axis = (0.6, -0.8)
    erode = -0.7
    seed = 7.3
    phase = 4.4
    edge_px = 1.2
    crack = 1.0
    crack_glow = 1.0
    crack_length = 0.9
    opacity = 1.0

    # --- r5: interior ---
    fill = 0.0                     # throat brim-full of light -> 0 once the black has arrived
    glow = 1.0
    throat = 0.88                  # how far the pit floor slides down the screen, in rim radii
    wall_soft = 0.09               # width of the hard cut at the base of the inner wall
    crest_at = 0.30                # where along the wall the spill over the rim starts to climb
    lip_width = 0.16
    grain_scale = 168.0            # rubble pitch: 2pi/168 uv = 22 px on the 1024 frame
    crevice_scale = 74.0
    vein_scale = 41.0
    vein = 0.7
    wall_gain = 1.0

    # --- linear rgb, authored so blue clips, green nearly does, red is deliberately non-zero ---
    void_color = (0.46, 3.80, 4.95)
    throat_color = (0.020, 0.135, 0.95)
    glint_color = (3.20, 4.60, 4.40)
    floor_deep = (0.0062, 0.0082, 0.0104)
    floor_lit = (0.0195, 0.0375, 0.0530)
    wall_matter = (0.0062, 0.0105, 0.0148)
    lip_color = (0.0290, 0.0385, 0.0455)


def death_mark(cfg, w, h, legacy=False):
    """Returns (rgb_linear, alpha) for the quad, in the quad's own uv frame."""
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    U, V = np.meshgrid(u, v)
    px, py = U - 0.5, V - 0.5

    span = np.hypot(px, py)
    radius = span * 2.0
    angle = np.arctan2(py, px)
    aa = np.maximum(fwidth(radius, w, h), 1e-6) * cfg.edge_px

    rag = (np.sin(angle * cfg.rag_count + cfg.phase) * 0.52
           + np.sin(angle * (cfg.rag_count * 1.9 + 1.0) - cfg.phase * 0.6 + cfg.seed) * 0.31
           + np.sin(angle * (cfg.rag_count * 3.3 + 2.0) + cfg.phase * 0.35 + cfg.seed * 1.7) * 0.17)

    outx, outy = px / np.maximum(span, 1e-5), py / np.maximum(span, 1e-5)
    ca = np.array(cfg.close_axis, float)
    ca = ca / np.linalg.norm(ca)
    lean = 0.55 + 0.45 * (outx * ca[0] + outy * ca[1])
    edge = np.maximum(cfg.radius * (1.0 + cfg.rag * rag) * sat01(1.0 - cfg.close * lean), 1e-5)
    bore = sat01((edge - radius) / aa)

    # The bite that eats the mask on the way out stays on r4's coarse pitch: the close was passed
    # and a fine bite dissolves the outline instead of taking pieces out of it.
    grain = 13.0
    lump = (np.sin(px * grain + cfg.seed * 3.1) * np.sin(py * grain * 1.27 - cfg.seed * 2.2)
            + 0.55 * np.sin((px - py) * grain * 2.3 + cfg.seed * 0.7))

    drift = cfg.phase * 0.25
    bite = (np.sin(px * grain * 0.85 + drift + cfg.seed * 1.4)
            * np.sin(py * grain * 1.1 - drift * 0.76 + cfg.seed * 2.6)
            + 0.55 * np.sin((px + py) * grain * 1.9 + drift * 0.52))
    intact = 0.5 + 0.32 * bite
    inside = bore * sat01((intact - cfg.erode) / np.maximum(fwidth(intact, w, h) * cfg.edge_px, 1e-5))

    # fissures running out across the deck
    fracture = np.zeros_like(px)
    for i in range(5):
        aim = cfg.seed * 0.83 + i * 1.2566 + np.sin(cfg.seed + i * 2.7) * 0.66
        delta = angle - aim + 0.14 * np.sin(radius * 7.0 + cfg.seed * 1.9 + i * 2.2)
        delta = np.arctan2(np.sin(delta), np.cos(delta))
        run = (cfg.crack_length * (0.68 + 0.32 * death_hash(cfg.seed * 1.7 + i * 5.3))
               * sat01(cfg.crack * (1.35 - 0.6 * death_hash(cfg.seed * 2.3 + i * 3.9))))
        along = sat01((run - radius) / np.maximum(run - edge, 1e-3))
        base = 0.035 if legacy else 0.100
        gain = 0.035 if legacy else 0.092
        width = (base + gain * death_hash(cfg.seed * 2.9 + i * 7.1)) * along
        fracture = np.maximum(fracture, sat01(1.0 - np.abs(delta) / np.maximum(width, 1e-5)))
    fracture = fracture * (1.0 - bore)
    crack_mask = sat01((fracture - 0.18) / np.maximum(fwidth(fracture, w, h) * cfg.edge_px, 1e-5))

    alpha = np.maximum(inside, crack_mask)

    if legacy:
        depth = sat01((edge - radius) / np.maximum(edge, 1e-5))
        beyond = sat01(0.5 + 0.5 * outy)
        lip_w = 0.028 + 0.062 * (1.0 - beyond)
        lip = sat01((lip_w - depth) / np.maximum(lip_w, 1e-5))
        wall_w = 0.06 + 0.34 * beyond
        wall = sat01((wall_w - depth) / np.maximum(wall_w, 1e-5))
        floor_c = np.array([0.006, 0.0092, 0.0115])
        wall_c = np.array([0.0115, 0.018, 0.0225])
        lip_c = np.array([0.03, 0.04, 0.047])
        matter = np.repeat(floor_c[None, None, :], h, 0).repeat(w, 1).copy()
        matter = matter + (wall_c - matter) * (wall * wall * beyond)[..., None]
        matter = matter + (lip_c - matter) * (lip ** 3)[..., None]
        matter = matter * sat01(1.0 + 0.45 * 0.7 * lump)[..., None]
        unit = cfg.radius * 0.5
        mp = pool_distance(px, py, -0.28 * unit, 0.50 * unit, 1.45, 1.0, 0.50 * unit, 0.26, cfg.seed * 2.1)
        sp = pool_distance(px, py, 0.44 * unit, -0.26 * unit, 1.0, 1.3, 0.27 * unit, 0.34, cfg.seed * 3.7 + 2.0)
        wash = np.maximum(sat01(1 - mp) ** 2, sat01(1 - sp) ** 2 * 0.55)
        core = np.maximum(sat01((0.44 - mp) / 0.30) ** 2, sat01((0.30 - sp) / 0.30) ** 2 * 0.55)
        glint = np.maximum(sat01((0.115 - mp) / 0.05) ** 2, sat01((0.14 - sp) / 0.07) ** 2 * 0.85)
        light = (np.array([0.02, 0.86, 2.15]) * wash[..., None]
                 + np.array([0.03, 1.85, 5.6]) * core[..., None]
                 + np.array([3.9, 4.3, 4.7]) * glint[..., None]) * cfg.glow
        fissure = lip_c + (floor_c - lip_c) * sat01((fracture - 0.25) / 0.55)[..., None]
        fissure = fissure + np.array([0.02, 0.86, 2.15]) * (cfg.crack_glow * 0.8 * sat01((fracture - 0.58) / 0.42))[..., None]
        color = fissure + (matter + light - fissure) * inside[..., None]
        return color, alpha

    # ---------------- r5 interior: a pit, not a stain -----------------------------------------
    # A cylindrical pit of radius R and depth D, seen at 73 degrees, projects to the rim ellipse
    # with its own floor drawn as the same ellipse slid down the screen by D*cos(73). Everything
    # between the two is inner wall; everything inside the lower one is floor. That single shift
    # is what puts the lit surface on the far rim and the deepest point below the middle.
    qx, qy = px * 2.0 / cfg.radius, py * 2.0 / cfg.radius
    rim = np.hypot(qx, qy)
    below = np.hypot(qx, qy + cfg.throat)

    # Everything on the wall is indexed by the angle around the sunk floor, so facets, grooves and
    # the broken base all run down the shaft together instead of being three unrelated patterns.
    around = np.arctan2(qy + cfg.throat, qx)
    facet = (0.55 * np.sin(around * 6.0 + cfg.seed * 1.3)
             + 0.30 * np.sin(around * 11.0 - cfg.seed * 1.9)
             + 0.15 * np.sin(around * 3.0 + cfg.seed * 0.5))
    base_wobble = 0.075 * facet
    wall_band = sat01((below - 1.0 - base_wobble) / cfg.wall_soft)
    floor_in = 1.0 - wall_band
    # 1 at the centre of the sunk ellipse, which is on the centre line below the middle
    sink = sat01(1.0 - below) ** 0.85

    # the deck overhanging the near rim: dead material, no light on it at all
    near_lip = sat01((rim - (1.0 - cfg.lip_width)) / cfg.lip_width) * sat01(-qy - 0.15)

    # rubble relief: three octaves at unrelated orientations so the field is lumpy rather than a
    # woven grid, with the slope taken analytically to give every lump a lit and an unlit face.
    fx, fy = px * cfg.grain_scale, py * cfg.grain_scale
    # The last octave is deliberately near the 9 px window the interior is scored in: relief
    # coarser than that averages out inside the window and measures as a flat fill.
    waves = ((1.00, 0.31, 0.62, cfg.seed * 3.1),
             (0.63, 1.44, 0.44, cfg.seed * 1.7 + 1.9),
             (1.71, -1.13, 0.29, cfg.seed * 0.9 + 3.6),
             (2.90, 2.30, 0.24, cfg.seed * 2.4 + 5.1))
    height = np.zeros_like(px)
    gx = np.zeros_like(px)
    gy = np.zeros_like(px)
    for ax, ay, amp, ph in waves:
        arg = fx * ax + fy * ay + ph
        height = height + amp * np.sin(arg)
        gx = gx + amp * ax * np.cos(arg)
        gy = gy + amp * ay * np.cos(arg)

    # the light lives down the throat, so a wall face turned downward catches it while a floor
    # face turned back up toward the far wall catches it. One key, flipped between the surfaces.
    kx = -0.26 + 0.50 * floor_in
    ky = -0.97 + 1.94 * floor_in
    facing = (gx * kx + gy * ky) / (np.hypot(kx, ky) * 1.6)
    shade = sat01(0.5 + 0.5 * np.tanh(facing * 1.5))

    # a crack network rather than a stripe field: three warped sine fronts, thinned by min().
    def crackle(scale, seed):
        cxx, cyy = px * scale, py * scale
        a = np.sin(cxx * 1.13 + 1.2 * np.sin(cyy * 0.61 + seed) + seed)
        b = np.sin(cyy * 0.97 - 1.1 * np.sin(cxx * 0.73 + seed * 1.7) + seed * 2.1)
        c = np.sin((cxx * 0.62 + cyy * 0.79) * 1.31
                   + np.sin((cxx * 0.79 - cyy * 0.62) * 0.55 + seed) + seed * 0.7)
        return np.minimum(np.minimum(np.abs(a), np.abs(b)), np.abs(c))

    crevice = sat01(1.0 - crackle(cfg.crevice_scale, cfg.seed) / 0.30) ** 1.5

    # runnels down the inner wall: the same field squeezed across, so it streaks the way water
    # and heat scar a shaft rather than reading as a rock texture pasted on a curve.
    rx, ry = px * cfg.grain_scale * 0.62, py * cfg.grain_scale * 0.16
    runnel = (np.sin(rx * 1.07 + 0.8 * np.sin(ry * 1.3 + cfg.seed) + cfg.seed * 2.6) * 0.6
              + np.sin(rx * 2.13 - 0.5 * np.sin(ry * 0.9 - cfg.seed) + cfg.seed) * 0.4)

    vein = sat01(1.0 - crackle(cfg.vein_scale, cfg.seed * 2.3 + 4.0) / 0.07) ** 2
    vein = vein * cfg.vein * sink ** 1.6 * (0.4 + 0.6 * shade)

    # ---- matter: dead rock, shaded, never a flat fill ----
    deep = np.array(cfg.floor_deep)
    lit = np.array(cfg.floor_lit)
    wallm = np.array(cfg.wall_matter)
    lipc = np.array(cfg.lip_color)
    boulder = 0.5 + 0.5 * np.sin(px * cfg.grain_scale * 0.27 + cfg.seed * 2.0) * np.sin(
        py * cfg.grain_scale * 0.21 - cfg.seed * 1.1)
    rock_amount = (shade * (0.72 + 0.28 * boulder) * (1.0 - 0.85 * crevice)
                   * (1.0 - 0.30 * sink))
    rock = deep + (lit - deep) * rock_amount[..., None]
    rock = rock + (wallm - rock) * (wall_band * 0.85)[..., None]
    rock = rock + (lipc - rock) * near_lip[..., None]

    # ---- light: the band sits on the rim and the far inner wall, never in the middle ----
    voidc = np.array(cfg.void_color)
    throatc = np.array(cfg.throat_color)

    # One colour driven across a narrow amount band, so every lit pixel on the wall clears both
    # the brightness and the saturation threshold instead of only one of them. Brightest against
    # the rim itself, which is where the light spills over the edge of the deck.
    # How far up the shaft this pixel sits: 1 against the rim where the light spills over the
    # broken edge, 0 down at the base where the wall meets the floor.
    up_wall = sat01((rim - cfg.crest_at) / max(1.0 - cfg.crest_at, 1e-4))
    fall = 0.18 + 0.82 * up_wall ** 0.8

    # A shaft is faceted, not turned. Big planes carry the read at thumbnail size, the rubble
    # relief carries it up close, and the grooves where the planes meet are what puts sixty to
    # eighty levels between the face toward the light and the face away from it.
    plane = 0.5 + 0.5 * facet
    groove = sat01(1.0 - np.abs(facet) / 0.13) ** 1.4
    wall_light = (wall_band * fall
                  * (0.56 + 0.44 * plane)
                  * (0.82 + 0.18 * shade)
                  * (1.0 - 0.85 * groove) * (1.0 - 0.40 * crevice)
                  * (0.90 + 0.10 * sat01(0.5 + 0.5 * runnel)) * cfg.wall_gain)

    # what is left in the throat once the light has drained; _Fill holds it brim-full early.
    throat_light = (floor_in * sat01(1.0 - sink * 2.6) * (0.05 + 0.16 * shade)
                    + cfg.fill * floor_in * sat01(1.0 - 0.35 * sink))
    throat_light = np.minimum(throat_light, 1.35)

    glintc = np.array(cfg.glint_color)
    glint = (sat01((rim - 0.90) / 0.10) ** 2 * wall_band
             * sat01((crackle(cfg.crevice_scale * 0.6, cfg.seed + 9.0) - 0.72) / 0.24))

    light = (voidc * wall_light[..., None]
             + throatc * (throat_light + vein * 2.4)[..., None]
             + glintc * glint[..., None]) * cfg.glow

    # ---- fissures: broken deck either side of a lit core, out on the floor beyond the hole ----
    # Colour has to hold area, and these run across ground the hole never darkens, so widening
    # them buys saturated brightness without spending any of the void's black.
    fissure = lipc + (deep - lipc) * sat01((fracture - 0.18) / 0.34)[..., None]
    fissure = fissure + throatc * (cfg.crack_glow * 1.5 * sat01((fracture - 0.46) / 0.22) ** 1.3)[..., None]
    fissure = fissure + voidc * (cfg.crack_glow * (0.30 + 0.54 * sat01(0.5 + 0.5 * runnel))
                                 * sat01((fracture - 0.62) / 0.20) ** 1.2)[..., None]

    color = fissure + (rock + light - fissure) * inside[..., None]
    return color, alpha


def bloom(linear):
    brightness = linear.max(-1)
    softness = np.clip(brightness - BLOOM_THRESHOLD + BLOOM_KNEE, 0.0, 2.0 * BLOOM_KNEE)
    softness = softness * softness / (4.0 * BLOOM_KNEE + 1e-4)
    mult = np.maximum(brightness - BLOOM_THRESHOLD, softness) / np.maximum(brightness, 1e-4)
    pre = linear * np.maximum(mult, 0.0)[..., None]
    out = np.empty_like(pre)
    for c in range(3):
        out[..., c] = ndimage.gaussian_filter(pre[..., c], BLOOM_SIGMA, mode="nearest")
    return linear + out * BLOOM_INTENSITY


def deck(frame=FRAME):
    board = np.full((frame, frame, 3), DECK_LINEAR)
    seam = int(round(CELL * PX_PER_UNIT))
    for k in range(0, frame, seam):
        board[k:k + 3, :, :] *= 0.45
        board[:, k:k + 3, :] *= 0.45
    return board


def compose(cfg, legacy=False, frame=FRAME):
    w = int(round(QUAD_UNITS * PX_PER_UNIT))
    h = int(round(QUAD_UNITS * PX_PER_UNIT * FORESHORTEN))
    rgb, alpha = death_mark(cfg, w, h, legacy=legacy)
    board = deck(frame)
    x0, y0 = (frame - w) // 2, (frame - h) // 2
    # the shader's v axis points away from the camera, which is up the screen
    rgb = rgb[::-1]
    alpha = alpha[::-1]
    dst = board[y0:y0 + h, x0:x0 + w]
    board[y0:y0 + h, x0:x0 + w] = dst + (rgb - dst) * (alpha * cfg.opacity)[..., None]
    return bloom(board), (x0, y0, w, h)


def local_sigma(L, mask, k=9):
    m = mask.astype(np.float32)
    mu = ndimage.uniform_filter(L * m, k) / np.maximum(ndimage.uniform_filter(m, k), 1e-6)
    mu2 = ndimage.uniform_filter(L * L * m, k) / np.maximum(ndimage.uniform_filter(m, k), 1e-6)
    core = ndimage.binary_erosion(mask, np.ones((k, k), bool))
    if core.sum() < 50:
        core = mask
    return float(np.sqrt(np.maximum(mu2 - mu * mu, 0))[core].mean()), int(core.sum())


def report(cfg, legacy=False, label="r5"):
    lin, (x0, y0, w, h) = compose(cfg, legacy=legacy)
    px = screen(lin)
    L, S, H = LSH(px)
    keep = L > 8
    bs = keep & (L > 200) & (S > 0.45)
    panel = bs.sum() / 4.0                       # the strip panel is the frame at half scale
    pct = 100.0 * bs.sum() / keep.sum()

    mass = np.zeros_like(L, bool)
    mass[y0:y0 + h, x0:x0 + w] = True
    _, a = death_mark(cfg, w, h, legacy=legacy)
    inside = np.zeros_like(L, bool)
    inside[y0:y0 + h, x0:x0 + w] = a[::-1] > 0.5
    lab, nlab = ndimage.label(ndimage.binary_closing(inside, np.ones((5, 5), bool)))
    if nlab:
        sizes = ndimage.sum(inside, lab, range(1, nlab + 1))
        inside = lab == (int(np.argmax(sizes)) + 1)

    print("=== %s ===" % label)
    print("  hole footprint      %6d px (%.2f%% of frame)  = %d px on the 512 panel"
          % (inside.sum(), 100.0 * inside.sum() / FRAME ** 2, inside.sum() // 4))
    print("  bright & saturated  %6d px on frame -> %5.0f px on panel = %.3f%% of panel"
          % (bs.sum(), panel, pct))
    if bs.sum():
        print("      hue median %3.0f  sat median %.3f  L median %5.1f  L max %3.0f"
              % (np.median(H[bs]), np.median(S[bs]), np.median(L[bs]), L[bs].max()))
    print("  clipped (all 3 >=250) %d px" % ((px.min(-1) >= 250).sum()))
    print("  inside the mass: L median %5.1f   frac < L40 %.3f   sat median %.3f"
          % (np.median(L[inside]), (L[inside] < 40).mean(), np.median(S[inside])))

    cx = FRAME // 2
    col = np.where(inside[:, cx])[0]
    if len(col):
        ys = np.linspace(col.min(), col.max(), 17).astype(int)
        print("  vertical profile down x=%d (top -> bottom):" % cx)
        print("     L  ", " ".join("%3.0f" % L[y, cx] for y in ys))
        print("     S  ", " ".join("%.2f" % S[y, cx] for y in ys))
        top = L[ys[:3], cx].max()
        bot = L[ys[9:14], cx].min()
        print("     brightest in the top fifth: %3.0f   deepest at centre-bottom: %3.0f" % (top, bot))

    dark = inside & (L < 45)
    s9, npx = local_sigma(L, dark, 9)
    print("  eroded dark-core (L<45) local 9x9 sigma = %.2f over %d px" % (s9, npx))
    s9m, _ = local_sigma(L, inside, 9)
    print("  whole-mass local 9x9 sigma = %.2f   global std in mass %.1f" % (s9m, L[inside].std()))

    seam = int(round(CELL * PX_PER_UNIT))
    sy = min((k for k in range(0, FRAME, seam) if abs(k - FRAME // 2) < seam),
             key=lambda k: abs(k - FRAME // 2))

    def seam_show(cols):
        """How far the seam rows sit below the deck either side of them."""
        if not len(cols):
            return float("nan")
        on = L[sy:sy + 3, cols].mean(0)
        off = np.concatenate([L[sy - 6:sy - 2, cols], L[sy + 5:sy + 9, cols]]).mean(0)
        return float(np.median(off - on))

    clean_cols = np.arange(40, 190)
    dark_cols = np.array([c for c in range(FRAME)
                          if inside[sy, c] and L[sy - 6:sy + 9, c].max() < 70])
    print("  tile seam contrast: clean floor %.0f   under the mass %s"
          % (seam_show(clean_cols),
             "%.0f" % seam_show(dark_cols) if len(dark_cols) else "n/a (seam row is lit)"))

    try:
        from PIL import Image
        out = "/tmp/_r5_death_%s.png" % ("legacy" if legacy else "r5")
        Image.fromarray(px.astype(np.uint8)[210:820, 200:830]).save(out)
        print("  wrote", out)
    except Exception:
        pass
    return px, L, S, H, inside


def drive(t):
    """DeathCollapseRunner's shipped clock, so the sim and the game read the same frame."""
    open_s, submerged = 0.17, 0.10
    peak = open_s + submerged
    light_s, drain0, drain1 = 0.075, 0.10, 0.185

    radius = 0.465 * (min(t / open_s, 1.0) ** 0.85 if t < open_s else 1.0)
    if t >= open_s:
        radius = 0.465 * (1.0 - 0.03 * min((t - open_s) / submerged, 1.0))

    if t <= 0.02:
        glow = 0.0
    elif t < light_s:
        glow = ((t - 0.02) / (light_s - 0.02)) ** 0.65
    else:
        glow = 1.0 + (0.93 - 1.0) * min((t - light_s) / (peak - light_s), 1.0)

    if t < drain0:
        fill = 1.0
    else:
        x = min(max((t - drain0) / (drain1 - drain0), 0.0), 1.0)
        fill = 1.0 - (x * x * (3.0 - 2.0 * x))
    return radius, glow, fill


def timeline():
    """The five frames the corpse has to survive, plus the frame the strip cuts."""
    print("=== r5 timeline (f12 fires the death; the strip cut r4 at f18) ===")
    print("%5s %8s %8s %7s %7s %11s %11s %9s"
          % ("frame", "t", "radius", "glow", "fill", "hole px", "bright&sat", "%panel"))
    for n in range(12, 21):
        t = (n - 12) / 30.0 + 1.0 / 60.0
        radius, glow, fill = drive(t)
        c = Cfg()
        c.radius, c.glow, c.fill = radius, glow, fill
        c.crack = 1.0 if t > 0.07 else 0.55 + 0.45 * t / 0.07
        c.crack_glow = 1.0
        lin, (x0, y0, w, h) = compose(c)
        px = screen(lin)
        L, S, _ = LSH(px)
        keep = L > 8
        bs = keep & (L > 200) & (S > 0.45)
        _, a = death_mark(c, w, h)
        ins = np.zeros_like(L, bool)
        ins[y0:y0 + h, x0:x0 + w] = a[::-1] > 0.5
        lit = ins & (L > 90)
        print("%5d %+8.3f %8.3f %7.2f %7.2f %11d %11d %9.3f   throat lit %5d px"
              % (n, t - 1.0 / 60.0, radius, glow, fill, ins.sum(), bs.sum(),
                 100.0 * bs.sum() / keep.sum(), lit.sum()))


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "both"
    if which in ("legacy", "both"):
        report(Cfg(), legacy=True, label="r4 shipped (model check)")
        print()
    if which in ("r5", "both"):
        report(Cfg(), legacy=False, label="r5 rebuild")
    if which in ("timeline", "both"):
        print()
        timeline()
