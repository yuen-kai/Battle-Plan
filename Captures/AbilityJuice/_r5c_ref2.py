"""Structure test: do reference bright+saturated zones wrap a genuinely clipped white core?
Plus cross-piece comparison inside r5 (shockwave / impactcore / death) and windup clip audit."""
import numpy as np
from scipy import ndimage
import os
from _r5c_wu_measure import load, luma, hsv, S
from _r5c_ref import panel_runs

REF = f"{S}/reference"
PICK = ["cm-8newabilities-13.jpg", "cm-everyability-05.jpg", "cm-season2-05.jpg",
        "cm-8newabilities-07.jpg", "cm-clashabilities-04.jpg", "cm-everyability-23.jpg"]


def core_surround(p):
    L = luma(p); h, s, v = hsv(p)
    bs = (L > 200) & (s >= 0.45)
    white = (p.min(-1) >= 235) & (s < 0.15)          # genuinely near-white
    clipped = (p.max(-1) >= 250)
    if bs.sum() == 0:
        return None
    # how much BS material sits within 6px of a white core
    near = ndimage.binary_dilation(white, np.ones((13, 13)))
    frac_near = (bs & near).sum() / bs.sum()
    return dict(bs=100 * bs.mean(), white=100 * white.mean(), clip=100 * clipped.mean(),
                frac_bs_near_white=frac_near,
                ratio_white_to_bs=(white.sum() / max(bs.sum(), 1)))


def main():
    print("### reference: is bright+saturated colour wrapped around a small white core?")
    print(f"{'file':28s} {'pan':>3s} {'BS%':>6s} {'white%':>7s} {'clip%':>6s} "
          f"{'BS near white':>13s} {'white/BS':>9s}")
    for fn in PICK:
        img = load(f"{REF}/{fn}")
        for pi, (a, b) in enumerate(panel_runs(img)):
            r = core_surround(img[:, a:b])
            if r:
                print(f"{fn:28s} {pi+1:3d} {r['bs']:6.2f} {r['white']:7.3f} {r['clip']:6.2f} "
                      f"{100*r['frac_bs_near_white']:12.1f}% {r['ratio_white_to_bs']:9.2f}")

    print("\n### our r5 pieces, same measurement")
    for piece in ("windup", "shockwave", "impactcore", "death", "aftermath", "debris", "grenade", "pogo"):
        p = f"{S}/r5/{piece}.jpg"
        if not os.path.exists(p):
            continue
        img = load(p)
        for pi, (a, b) in enumerate(panel_runs(img)):
            r = core_surround(img[:, a:b])
            sub = img[:, a:b]
            L = luma(sub); h, s, v = hsv(sub)
            bs = (L > 200) & (s >= 0.45)
            hstr = "-"
            if bs.sum() > 30:
                hv = h[bs]
                hstr = f"{np.degrees(np.arctan2(np.sin(np.radians(hv)).mean(), np.cos(np.radians(hv)).mean()))%360:.0f}"
            if r:
                print(f"{piece:14s} p{pi+1} BS {r['bs']:6.3f}%  white {r['white']:6.3f}%  "
                      f"clip {r['clip']:5.2f}%  BSnearWhite {100*r['frac_bs_near_white']:5.1f}%  hue(BS) {hstr}")
            else:
                print(f"{piece:14s} p{pi+1} BS  0.000%  clip {100*((sub.max(-1)>=250).mean()):5.2f}%")

    print("\n### r5 windup: hue histogram of pixels above L200 (where did the colour go?)")
    img = load(f"{S}/r5/windup.jpg")
    for pi, (a, b) in enumerate(panel_runs(img)):
        sub = img[:, a:b]
        L = luma(sub); h, s, v = hsv(sub)
        m = (L > 200)
        if m.sum() == 0:
            continue
        print(f"  p{pi+1}: {m.sum()}px above L200, mean S {s[m].mean():.3f}, "
              f"S p95 {np.percentile(s[m],95):.3f}, S max {s[m].max():.3f}")
        cool = m & (h > 140) & (h < 210)
        if cool.sum():
            print(f"       of which cool(140-210deg): {cool.sum()}px  S mean {s[cool].mean():.3f} "
                  f"S max {s[cool].max():.3f}  L mean {L[cool].mean():.0f}  "
                  f"mean RGB {sub[cool].mean(0).round(1)}")
        satc = (s >= 0.45) & (h > 140) & (h < 210)
        if satc.sum():
            print(f"       cool & S>=0.45: {satc.sum()}px  L mean {L[satc].mean():.1f} "
                  f"L max {L[satc].max():.0f}  mean RGB {sub[satc].mean(0).round(1)}")


if __name__ == "__main__":
    main()
