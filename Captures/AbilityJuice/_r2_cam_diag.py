"""Which frames did the r2 camera strip cut, and is the similarity fit trustworthy?"""
import numpy as np
from PIL import Image
import os

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
D2 = os.path.join(AJ, "shots/r2/camera")
D1 = os.path.join(AJ, "shots/r1/camera")


def frames(D, n):
    return [np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"),
                       dtype=np.float32) for i in range(n)]


def identify(strip_path, D, n, label):
    st = Image.open(strip_path).convert("L")
    W, H = st.size
    a = np.asarray(st, dtype=np.float32)
    colmean = a.mean(axis=0)
    print(f"\n--- {label}: {os.path.basename(strip_path)}  {W}x{H} ---")
    print(f"    darkest columns: {sorted(np.argsort(colmean)[:24].tolist())}")
    gut = (W - 3 * H) // 2
    bounds = [(0, H), (H + gut, 2 * H + gut), (W - H, W)]
    print(f"    assumed panel x-bounds: {bounds}  (gutter {gut}px)")
    pool = frames(D, n)
    picks = []
    for pi, (x0, x1) in enumerate(bounds):
        pan = a[:, x0:x1]
        ph, pw = pan.shape
        scored = []
        for i, f in enumerate(pool):
            fi = np.asarray(Image.fromarray(f).resize((pw, ph), Image.BILINEAR),
                            dtype=np.float32)
            scored.append((float(np.abs(fi - pan).mean()), i))
        scored.sort()
        picks.append(scored[0][1])
        top = ", ".join(f"f{i:04d}:{d:.2f}" for d, i in scored[:3])
        print(f"    panel {pi+1} -> {top}")
    return picks


p2 = identify(os.path.join(AJ, "strips/r2/camera.jpg"), D2, 52, "ROUND 2 strip")
p1 = identify(os.path.join(AJ, "strips/r1/camera.jpg"), D1, 40, "ROUND 1 strip")
print(f"\nr2 camera strip frames = {p2}   (t = {[round((f-12)*33.333) for f in p2]} ms)")
print(f"r1 camera strip frames = {p1}   (t = {[round((f-12)*33.333) for f in p1]} ms)")
