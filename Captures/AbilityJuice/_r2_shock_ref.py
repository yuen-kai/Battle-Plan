"""Measure the Clash Mini reference strips the same way, so the comparison is not vibes."""
import numpy as np
from PIL import Image
import glob

lum = lambda a: 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def edge_hardness(L):
    """Median |dL| across the strongest 1% of horizontal steps — a proxy for silhouette hardness."""
    g = np.abs(np.diff(L, axis=1))
    return np.percentile(g, 99.5), np.percentile(g, 99.9), g.max()


print(f"{'strip':32s} {'panel':>5s} {'medL':>6s} {'satMean':>8s} {'sat_p90':>8s} "
      f"{'hotSat':>7s} {'clip%':>6s} {'dark%':>6s} {'edge99.5':>9s} {'edgeMax':>8s}")
rows = []
for p in sorted(glob.glob("Captures/AbilityJuice/strips/reference/*.jpg")):
    a = np.asarray(Image.open(p)).astype(np.float64)
    H, W, _ = a.shape
    pw = (W - 2 * 8) // 3
    name = p.split("/")[-1].replace(".jpg", "")
    for pi, x0 in enumerate([0, pw + 8, 2 * (pw + 8)]):
        q = a[:, x0:x0 + pw]
        L = lum(q)
        s = sat(q)
        hot = L > 200
        med = np.median(L)
        hs = s[hot].mean() if hot.sum() > 100 else float("nan")
        clip = (q.min(axis=2) >= 250).mean() * 100
        dark = (L < med - 60).mean() * 100
        e995, e999, emax = edge_hardness(L)
        rows.append((name, pi + 1, med, s.mean(), np.percentile(s, 90), hs, clip, dark, e995, emax))
        print(f"{name:32s} {pi+1:5d} {med:6.1f} {s.mean():8.3f} {np.percentile(s,90):8.3f} "
              f"{hs:7.3f} {clip:6.2f} {dark:6.2f} {e995:9.1f} {emax:8.1f}")

print()
print("=" * 100)
print("OURS, same measurement")
for tag in ["r1", "r2"]:
    a = np.asarray(Image.open(f"Captures/AbilityJuice/strips/{tag}/shockwave.jpg")).astype(np.float64)
    H, W, _ = a.shape
    pw = 512
    for pi, x0 in enumerate([0, 520, 1040]):
        q = a[:, x0:x0 + pw]
        L = lum(q)
        s = sat(q)
        hot = L > 200
        med = np.median(L)
        hs = s[hot].mean() if hot.sum() > 100 else float("nan")
        clip = (q.min(axis=2) >= 250).mean() * 100
        dark = (L < med - 60).mean() * 100
        e995, e999, emax = edge_hardness(L)
        print(f"{'OURS-'+tag+'-shockwave':32s} {pi+1:5d} {med:6.1f} {s.mean():8.3f} "
              f"{np.percentile(s,90):8.3f} {hs:7.3f} {clip:6.2f} {dark:6.2f} {e995:9.1f} {emax:8.1f}")

print()
print("=" * 100)
print("REFERENCE AGGREGATE vs OURS")
import statistics
ref_sat = [r[3] for r in rows]
ref_hot = [r[5] for r in rows if not np.isnan(r[5])]
ref_dark = [r[7] for r in rows]
ref_edge = [r[8] for r in rows]
print(f"reference mean saturation : {statistics.mean(ref_sat):.3f}  (range {min(ref_sat):.3f}-{max(ref_sat):.3f})")
print(f"reference hot-zone sat    : {statistics.mean(ref_hot):.3f}  (range {min(ref_hot):.3f}-{max(ref_hot):.3f})")
print(f"reference dark-mass %     : {statistics.mean(ref_dark):.2f}%")
print(f"reference edge p99.5 L/px : {statistics.mean(ref_edge):.1f}")
