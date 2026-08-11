"""Round-4 death builder: what a linear-authored colour becomes in frame.

URP grades in LogC (contrast), applies saturation in linear, then Neutral-tonemaps, then encodes
sRGB. The volume runs contrast +14 and saturation +8. Predicting the shipped hue/saturation of an
authored HDR colour matters more here than anywhere else: the brief's whole ask is a measured hue
at a measured saturation.
"""
import numpy as np

CONTRAST = 1.14
SATURATION = 1.08
ACEScc_MIDGRAY = 0.4135884


def linear_to_logc(x):
    # ARRI LogC v3 (EI 800), the curve URP grades contrast in
    hi = 0.247190 * np.log10(5.555556 * np.maximum(x, 1e-9) + 0.052272) + 0.385537
    lo = 5.367655 * x + 0.092809
    return np.where(x > 0.010591, hi, lo)


def logc_to_linear(x):
    hi = (10.0 ** ((np.minimum(x, 4.0) - 0.385537) / 0.247190) - 0.052272) / 5.555556
    lo = (x - 0.092809) / 5.367655
    return np.where(x > 0.1496582, hi, lo)


def neutral_curve(x, a=0.2, b=0.29, c=0.24, d=0.272, e=0.02, f=0.3):
    return ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f


def neutral_tonemap(x):
    white_scale = 1.0 / neutral_curve(5.3)
    return neutral_curve(x * white_scale) * white_scale


def srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)


def grade(linear):
    c = np.asarray(linear, dtype=np.float64)
    c = np.maximum(c, 0)
    c = logc_to_linear((linear_to_logc(c) - ACEScc_MIDGRAY) * CONTRAST + ACEScc_MIDGRAY)
    c = np.maximum(c, 0)
    grey = (c * np.array([0.2126, 0.7152, 0.0722])).sum(axis=-1, keepdims=True)
    c = np.maximum(grey + (c - grey) * SATURATION, 0)
    c = neutral_tonemap(c)
    return np.clip(np.round(srgb(c) * 255), 0, 255)


def report(name, linear):
    d = grade(linear)
    mx, mn = d.max(), d.min()
    delta = max(mx - mn, 1e-6)
    if mx == d[0]:
        h = 60 * ((d[1] - d[2]) / delta)
    elif mx == d[1]:
        h = 60 * (2 + (d[2] - d[0]) / delta)
    else:
        h = 60 * (4 + (d[0] - d[1]) / delta)
    h %= 360
    s = 0 if mx < 1e-6 else (mx - mn) / mx
    lum = 0.2126 * d[0] + 0.7152 * d[1] + 0.0722 * d[2]
    print(f"{name:26s} lin {tuple(round(v,4) for v in linear)}  ->  rgb {tuple(int(v) for v in d)}"
          f"   hue {h:5.1f}  sat {s:.3f}  L {lum:5.1f}"
          f"{'   FULLY CLIPPED' if mn >= 254 else ('   clips ' + 'RGB'[int(np.argmax(d))] if mx >= 254 else '')}")


if __name__ == "__main__":
    print("--- calibration: what round 3 authored, against what the critics measured ---")
    report("mark core", (0.020, 0.021, 0.024))
    report("mark edge", (0.086, 0.089, 0.098))
    report("shard lit", (0.115, 0.110, 0.104))
    report("shard shadow", (0.020, 0.020, 0.022))
    print("   critics measured the death material at median L 57, and 57-67 for the clod")

    print()
    print("--- the void: everything here must land at or under L 35 ---")
    for v in (0.0022, 0.0030, 0.0042, 0.0060, 0.0090, 0.0130, 0.0170, 0.0220, 0.0300, 0.0450, 0.060):
        report(f"grey {v:.4f}", (v, v * 1.03, v * 1.16))

    print()
    print("--- the death's colour, against the pogo's 33 deg orange ---")
    report("wash", (0.020, 0.86, 2.15))
    report("wash x0.5", (0.010, 0.43, 1.075))
    report("wash x0.25", (0.005, 0.215, 0.5375))
    report("core", (0.030, 1.85, 5.60))
    report("core+wash", (0.050, 2.71, 7.75))
    report("glint total", (5.65, 8.05, 12.20))
    report("crack light", (0.014, 0.60, 1.50))
    print()
    report("pogo ember (for scale)", (3.6, 0.5, 0.055))
