import numpy as np
from PIL import Image
import glob, os

B = "Captures/AbilityJuice"
D = f"{B}/shots/r4/debris"
files = sorted(glob.glob(f"{D}/f*.jpg"))
FRAME = 1024 * 1024
CELL = 201.0 * 193.0  # projected cell area on the 1024 full-res frame


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


plate = load(files[0])
pL = lum(plate)

# impact frame index 15 per picks.json -> t = (15-12)/30 ? derive from contact sheet: f0011 = -0.033s
# picks: windup_frame 11 -> -0.0333s, impact 15 -> +0.1s, aftermath 28 -> +0.5333s  => t = (n-12)/30
print(f"{'f':>3} {'t':>7} {'clip':>6} {'opaqCell2':>9} {'satA_%':>7} {'satA_cell2':>10} {'satB_%':>7} {'meanS_eff':>9} {'maxL':>5}")
rows = []
for i, f in enumerate(files):
    a = load(f)
    L, S = lum(a), sat(a)
    clip = ((a[..., 0] > 250) & (a[..., 1] > 250) & (a[..., 2] > 250)).sum()
    opaque = ((pL - L) > 60).sum()
    mA = (S >= 0.50) & (L >= 120)
    mB = (S > 0.45) & (L > 199)
    eff = np.abs(a - plate).max(axis=2) > 14
    ms = float(S[eff].mean()) if eff.sum() > 50 else 0.0
    t = (i - 12) / 30.0
    rows.append((i, t, clip, opaque / CELL, 100 * mA.sum() / FRAME, mA.sum() / CELL, 100 * mB.sum() / FRAME, ms, float(L.max())))
    print(f"{i:3d} {t:+7.3f} {clip:6d} {opaque/CELL:9.3f} {100*mA.sum()/FRAME:7.3f} {mA.sum()/CELL:10.3f} {100*mB.sum()/FRAME:7.3f} {ms:9.3f} {L.max():5.0f}")

r = np.array([[x[2], x[3], x[4], x[5]] for x in rows])
print("\npeak clip frame:", int(np.argmax(r[:, 0])), "peak opaque frame:", int(np.argmax(r[:, 1])), "peak satA frame:", int(np.argmax(r[:, 2])))
