"""Perceptual pass: does the camera response READ in the strip a stranger sees?"""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import os, json

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"


def panels(path):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    gut = (W - 3 * H) // 2
    return [im.crop((0, 0, H, H)),
            im.crop((H + gut, 0, 2 * H + gut, H)),
            im.crop((W - H, 0, W, H))], H


# ---------------------------------------------------------------- 1. squint test
for tag, path in (("r2", "strips/r2/camera.jpg"), ("r1", "strips/r1/camera.jpg"),
                  ("cm23", "strips/reference/cm-everyability-23.jpg"),
                  ("cm20", "strips/reference/cm-everyability-20.jpg"),
                  ("cm13", "strips/reference/cm-8newabilities-13.jpg")):
    im = Image.open(os.path.join(AJ, path)).convert("RGB")
    w, h = im.size
    sq = im.resize((w // 8, h // 8), Image.LANCZOS).resize((w // 2, h // 2), Image.NEAREST)
    sq.save(os.path.join(AJ, f"_r2_cam_squint_{tag}.png"))

# ---------------------------------------------------------------- 2. tilt legibility
print("=" * 112)
print("TILT LEGIBILITY — is panel 2 visibly rotated relative to panels 1 and 3?")
print("Measured as the vertical offset a board line accumulates across the panel width.")
print("=" * 112)
for roll, zoom, trans, lab in ((2.783, 5.252, 19.21, "r2 strip panel 2 (studio cam)"),
                               (0.046, 2.288, 76.98, "r1 strip panel 2 (studio cam)"),
                               (2.783, 1.500, 5.49, "r2 IN-GAME estimate (board cam)")):
    rise = 1024 * np.tan(np.radians(roll))
    print(f"  {lab:>32}: roll {roll:5.3f}deg tilts a full-width line by {rise:6.1f} px "
          f"top-to-bottom; zoom {zoom:5.3f}% moves the frame edge {zoom/100*512:5.1f} px; "
          f"translation {trans:5.1f} px")

# ---------------------------------------------------------------- 3. motion blur
print()
print("=" * 112)
print("MOTION BLUR at the frame the strip cuts — is there any smear to sell the move?")
print("=" * 112)
D = os.path.join(AJ, "shots/r2/camera")
D1 = os.path.join(AJ, "shots/r1/camera")


def edge_stats(path, box=(120, 120, 420, 900)):
    a = np.asarray(Image.open(path).convert("L"), dtype=np.float64)
    y0, x0, y1, x1 = box
    sub = a[y0:y1, x0:x1]
    gy, gx = np.gradient(sub)
    g = np.hypot(gx, gy)
    hi = g > np.percentile(g, 97)
    # edge width proxy: 2*|grad| / |laplacian| is unstable; use the ratio of the
    # 97th-percentile gradient to the local contrast across the same edge
    lap = np.abs(ndimage.laplace(sub))
    return float(np.percentile(g, 99.5)), float(g[hi].mean()), float(sub.std()), \
        float(np.percentile(lap, 99.5))


print(f"{'frame':>26} {'p99.5 |grad|':>13} {'mean top-3% |grad|':>19} {'p99.5 |lap|':>12}")
for lab, p in (("r2 f0010 (pre, panel 1)", f"{D}/f0010.jpg"),
               ("r2 f0012 (impulse start)", f"{D}/f0012.jpg"),
               ("r2 f0014 (panel 2, cut)", f"{D}/f0014.jpg"),
               ("r2 f0015 (freeze released)", f"{D}/f0015.jpg"),
               ("r2 f0027 (panel 3)", f"{D}/f0027.jpg"),
               ("r1 f0012 (r1 panel 2)", f"{D1}/f0012.jpg")):
    a, b, c, d = edge_stats(p)
    print(f"{lab:>26} {a:13.2f} {b:19.2f} {d:12.2f}")

# ---------------------------------------------------------------- 4. side-by-side
pr2, S = panels(os.path.join(AJ, "strips/r2/camera.jpg"))
pcm, Sc = panels(os.path.join(AJ, "strips/reference/cm-everyability-23.jpg"))
pr1, _ = panels(os.path.join(AJ, "strips/r1/camera.jpg"))
T = 340
rows = []
for name, ps in (("R2", pr2), ("R1", pr1), ("CM", pcm)):
    rows.append(np.hstack([np.asarray(p.resize((T, T), Image.LANCZOS)) for p in ps]))
sbs = np.vstack(rows)
Image.fromarray(sbs).save(os.path.join(AJ, "_r2_cam_sbs.png"))

# ---------------------------------------------------------------- 5. tilt overlay
ov = Image.new("RGB", (T * 3, T))
for k, p in enumerate(pr2):
    ov.paste(p.resize((T, T), Image.LANCZOS), (k * T, 0))
d = ImageDraw.Draw(ov)
for k in range(3):
    for yy in (0.28, 0.52, 0.76):
        d.line([(k * T, int(T * yy)), ((k + 1) * T - 1, int(T * yy))],
               fill=(255, 40, 40), width=1)
ov.save(os.path.join(AJ, "_r2_cam_tiltguide.png"))
print("\nwrote _r2_cam_squint_*.png, _r2_cam_sbs.png, _r2_cam_tiltguide.png")
