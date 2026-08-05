"""Contact-sheet timeline: charge onset, growth curve, and money-frame check. Plus robustness."""
import numpy as np
from PIL import Image
from scipy import ndimage
import os
from _r5c_wu_measure import load, luma, hsv, S
from _r5c_ref import panel_runs

OUT = os.path.dirname(os.path.abspath(__file__))


def sheet_cells(img):
    """Split a contact sheet into its frame tiles using the black label bars."""
    L = luma(img)
    rowdark = L.mean(1) < 40
    runs, st = [], None
    for i, d in enumerate(rowdark):
        if not d and st is None:
            st = i
        elif d and st is not None:
            runs.append((st, i)); st = None
    if st is not None:
        runs.append((st, len(rowdark)))
    rows = [r for r in runs if r[1] - r[0] > 40]
    return rows


def main():
    img = load(f"{S}/r5/windup_contact.jpg")
    print("contact sheet", img.shape)
    rows = sheet_cells(img)
    print("row bands:", rows)
    ncol = 10
    W = img.shape[1] / ncol
    recs = []
    for ri, (y0, y1) in enumerate(rows):
        for ci in range(ncol):
            x0, x1 = int(ci * W), int((ci + 1) * W)
            t = img[y0:y1, x0:x1]
            L = luma(t); h, s, v = hsv(t)
            cool = (h > 140) & (h < 210) & (s > 0.18) & (L > 190)
            bs = (L > 200) & (s >= 0.45)
            recs.append((ri, ci, int(cool.sum()), 100 * bs.mean(),
                         float(L[cool].mean()) if cool.sum() else 0.0,
                         float(s[cool].mean()) if cool.sum() else 0.0))
    print(f"\n{'idx':>4s} {'row':>3s} {'col':>3s} {'coolBright px':>13s} {'BS%':>7s} {'L':>6s} {'S':>6s}")
    for k, (ri, ci, n, bs, Lm, Sm) in enumerate(recs):
        bar = "#" * int(n / 25)
        print(f"{k:4d} {ri:3d} {ci:3d} {n:13d} {bs:7.3f} {Lm:6.1f} {Sm:6.3f} {bar}")

    print("\n### robustness of the 0.00% claim on r5 windup panels")
    w = load(f"{S}/r5/windup.jpg")
    for pi, (a, b) in enumerate(panel_runs(w)):
        p = w[:, a:b]
        h, s, v = hsv(p)
        l709 = 0.2126 * p[..., 0] + 0.7152 * p[..., 1] + 0.0722 * p[..., 2]
        l601 = 0.299 * p[..., 0] + 0.587 * p[..., 1] + 0.114 * p[..., 2]
        lmax = p.max(-1)
        for nm, L in (("Rec709", l709), ("Rec601", l601), ("HSV V", lmax)):
            print(f"  p{pi+1} {nm:7s} L>200&S>=.45 {100*((L>200)&(s>=0.45)).mean():7.4f}%   "
                  f"L>190&S>=.40 {100*((L>190)&(s>=0.40)).mean():7.4f}%   "
                  f"L>180&S>=.35 {100*((L>180)&(s>=0.35)).mean():7.4f}%")


if __name__ == "__main__":
    main()
