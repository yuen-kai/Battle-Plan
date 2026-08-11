"""Measure the Clash reference impact panels the same way, and squint ours against them."""

import numpy as np
from PIL import Image

REF = "Captures/AbilityJuice/strips/reference"
NAMES = ["cm-everyability-23", "cm-everyability-20", "cm-everyability-30",
         "cm-clashabilities-04", "cm-8newabilities-18", "cm-everyability-05"]


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx, mn = a.max(axis=-1), a.min(axis=-1)
    return np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


print("=== CLASH REFERENCE IMPACT PANELS (middle panel of each strip) ===")
print("name                      board  peak  clip>=254   clip%   sat@bright  hottest-blob")
for n in NAMES:
    im = Image.open(f"{REF}/{n}.jpg").convert("RGB")
    pw = im.width // 3
    left = np.asarray(im.crop((6, 0, pw - 6, im.height))).astype(np.float32)
    mid = np.asarray(im.crop((pw + 6, 0, 2 * pw - 6, im.height))).astype(np.float32)
    Lm, Ll = lum(mid), lum(left)
    clip = (mid >= 254).all(axis=-1)
    S = sat(mid)
    bright = Lm > np.percentile(Lm, 97)
    # halo: saturated ring around the clipped core
    print(f"{n:24s}  {np.median(Ll):5.0f}  {Lm.max():4.0f}  {clip.sum():9d}  "
          f"{100*clip.mean():5.2f}%  {S[bright].mean():9.3f}   "
          f"{100*(Lm > 240).mean():5.2f}% of panel >240")

# Squint sheet: our peak frame + our strip frame + two clash mids, all at 96px then blown up
tiles = []
for path, box in [("Captures/AbilityJuice/shots/r2/impactcore/f0012.jpg", None),
                  ("Captures/AbilityJuice/shots/r2/impactcore/f0013.jpg", None)]:
    tiles.append(Image.open(path).convert("RGB").resize((320, 320), Image.LANCZOS))
for n in ("cm-everyability-23", "cm-everyability-20"):
    im = Image.open(f"{REF}/{n}.jpg").convert("RGB")
    pw = im.width // 3
    tiles.append(im.crop((pw + 6, 0, 2 * pw - 6, im.height)).resize((320, 320), Image.LANCZOS))

sheet = Image.new("RGB", (320 * 4 + 24, 320), (0, 0, 0))
for k, t in enumerate(tiles):
    sheet.paste(t, (k * 328, 0))
sheet.save("Captures/AbilityJuice/_r2_core_squint4.png")
sheet.resize((sheet.width // 4, sheet.height // 4), Image.LANCZOS).resize(
    (sheet.width // 2, sheet.height // 2), Image.NEAREST
).save("Captures/AbilityJuice/_r2_core_squint4_small.png")
print("\nwrote _r2_core_squint4.png  (ours f0012 | ours f0013 strip | clash-23 | clash-20)")
print("wrote _r2_core_squint4_small.png (same, squinted)")

# rebuilt strip with the true peak, next to the clash strip
ours = Image.new("RGB", (1024, 341), (0, 0, 0))
for k, f in enumerate((0, 12, 30)):
    p = Image.open(f"Captures/AbilityJuice/shots/r2/impactcore/f{f:04d}.jpg").convert("RGB")
    ours.paste(p.resize((337, 341), Image.LANCZOS), (k * 344, 0))
ref = Image.open(f"{REF}/cm-everyability-23.jpg").convert("RGB").resize((1024, 341), Image.LANCZOS)
stack = Image.new("RGB", (1024, 690), (20, 20, 20))
stack.paste(ours, (0, 0))
stack.paste(ref, (0, 349))
stack.save("Captures/AbilityJuice/_r2_core_strip_stack.png")
print("wrote _r2_core_strip_stack.png (our strip w/ true peak over the clash strip)")
