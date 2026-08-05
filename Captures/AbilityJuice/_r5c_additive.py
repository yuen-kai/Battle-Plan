"""Test the hypothesis: the r5 charge is ADDITIVE over a bright board, which caps saturation."""
import numpy as np
from scipy import ndimage
from _r5c_wu_measure import load, luma, hsv, S

RUNS = [(0, 512), (520, 1032), (1040, 1552)]


def main():
    r3 = load(f"{S}/r3/windup.jpg")
    r5 = load(f"{S}/r5/windup.jpg")
    r4 = load(f"{S}/r4/windup.jpg")

    a, b = RUNS[1]
    base = r3[:, a:b]
    L = luma(base); h, s, v = hsv(base)
    floor = (s < 0.12) & (L > 165) & (L < 205)
    fm = base[floor].mean(0)
    print(f"board floor mean RGB {fm.round(1)}   median RGB {np.median(base[floor],0).round(1)}")
    print(f"   floor luma {luma(fm[None,None,:])[0,0]:.1f}   floor min channel {fm.min():.1f}")
    cap = 1 - fm.min() / 255.0
    print(f"\n*** ADDITIVE CEILING ***")
    print(f"   Additive light can only raise channels. Over a floor whose lowest channel is "
          f"{fm.min():.0f},")
    print(f"   the result's min channel is >= {fm.min():.0f} and its max channel is <= 255.")
    print(f"   => saturation = 1 - min/max <= 1 - {fm.min():.0f}/255 = {cap:.3f}")
    print(f"   The bar is 0.45. Additive CANNOT reach it on this board at any intensity or hue.")

    for pi in (0, 1):
        aa, bb = RUNS[pi]
        cur5, cur4, ref = r5[:, aa:bb], r4[:, aa:bb], r3[:, aa:bb]
        for tag, cur in (("r5", cur5), ("r4", cur4)):
            d = cur - ref
            changed = np.abs(d).max(-1) > 18
            changed = ndimage.binary_opening(changed, np.ones((2, 2)))
            if changed.sum() < 50:
                continue
            dd = d[changed]
            neg = (dd < -12).any(-1)
            print(f"\n  {tag} p{pi+1}: {changed.sum()}px changed")
            print(f"      pixels where SOME channel got DARKER than the plate (occluding/opaque): "
                  f"{neg.sum()} = {100*neg.mean():.1f}%")
            print(f"      mean per-channel delta vs plate: {dd.mean(0).round(1)}  "
                  f"(all-positive => purely additive)")
            L5 = luma(cur); h5, s5, v5 = hsv(cur)
            bright = changed & (L5 > 200)
            if bright.sum():
                print(f"      bright(L>200) changed px: {bright.sum()}  "
                      f"mean RGB {cur[bright].mean(0).round(1)}  "
                      f"min-channel mean {cur[bright].min(-1).mean():.1f}  "
                      f"Smax {s5[bright].max():.3f}")
            sat = changed & (s5 >= 0.45)
            if sat.sum():
                print(f"      saturated(S>=.45) changed px: {sat.sum()}  "
                      f"mean RGB {cur[sat].mean(0).round(1)}  Lmax {L5[sat].max():.0f}")

    print("\n### what an OPAQUE fill would have to be")
    for name, hdeg, sat in (("gold 50deg", 50, 0.75), ("amber 45deg", 45, 0.70),
                            ("cyan 165deg", 165, 0.80), ("cyan 175deg", 175, 0.70)):
        import colorsys
        r, g, bl = colorsys.hsv_to_rgb(hdeg / 360, sat, 1.0)
        rgb = np.array([r, g, bl]) * 255
        print(f"   {name:12s} opaque RGB {rgb.round(0)}  L {0.2126*rgb[0]+0.7152*rgb[1]+0.0722*rgb[2]:.0f}  "
              f"S {sat:.2f}   (min channel must sit at {rgb.min():.0f}, "
              f"i.e. {fm.min()-rgb.min():.0f} BELOW the board)")


if __name__ == "__main__":
    main()
