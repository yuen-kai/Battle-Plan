"""Forward model of the capture pipeline: linear shader output -> screen 8-bit.

URP LutBuilderHdr order: contrast (LogC) -> saturation (linear) -> Neutral tonemap -> sRGB.
Calibrated against r3 measurements (dust palette and the gold ember hairline).
"""
import numpy as np

MIDGRAY = 0.4135884


def s2l(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def lin_to_logc(x):
    x = np.maximum(x, 0.0)
    return np.where(x > 0.01125000, (np.log10((x * 5.555556) + 0.052272) * 0.24719 + 0.385537),
                    (x * 5.367655 + 0.092809))


def logc_to_lin(x):
    return np.where(x > 0.1512050, (10.0 ** ((x - 0.385537) / 0.24719) - 0.052272) / 5.555556,
                    (x - 0.092809) / 5.367655)


def neutral_curve(x, a, b, c, d, e, f):
    return ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f


def neutral_tonemap(x):
    a, b, c, d, e, f = 0.2, 0.29, 0.24, 0.272, 0.02, 0.3
    white_level, white_clip = 5.3, 1.0
    ws = 1.0 / neutral_curve(white_level, a, b, c, d, e, f)
    x = neutral_curve(np.maximum(x, 0.0) * ws, a, b, c, d, e, f) * ws
    return x / white_clip


def grade(linear, contrast=1.14, saturation=1.08):
    x = np.asarray(linear, dtype=np.float64)
    x = logc_to_lin((lin_to_logc(x) - MIDGRAY) * contrast + MIDGRAY)
    x = np.maximum(x, 0.0)
    luma = (0.2126 * x[..., 0] + 0.7152 * x[..., 1] + 0.0722 * x[..., 2])[..., None]
    x = np.maximum(luma + saturation * (x - luma), 0.0)
    return neutral_tonemap(x)


def screen(linear):
    """Linear shader output -> 0..255 sRGB triple."""
    return np.clip(l2s(grade(linear)) * 255.0, 0, 255)


def LSH(rgb):
    rgb = np.asarray(rgb, dtype=np.float64)
    L = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
    mx, mn = rgb.max(-1), rgb.min(-1)
    S = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    ir, ig, ib = (mx == r) & m, (mx == g) & m, (mx == b) & m
    h[ir] = ((g - b)[ir] / d[ir]) % 6
    h[ig] = ((b - r)[ig] / d[ig]) + 2
    h[ib] = ((r - g)[ib] / d[ib]) + 4
    return L, S, h * 60


def show(tag, linear):
    px = screen(np.array([linear], dtype=np.float64))
    L, S, H = LSH(px)
    print('%-46s rgb(%3.0f,%3.0f,%3.0f)  L=%5.1f  S=%.3f  H=%3.0f'
          % (tag, px[0, 0], px[0, 1], px[0, 2], L[0], S[0], H[0]))
    return L[0], S[0], H[0]


if __name__ == '__main__':
    print('=== CALIBRATION against r3 measurements ===')
    print('-- opaque dust: authored sRGB fed as linear through SetVector, alpha 1.0 over the deck --')
    for v in (0.272, 0.470, 0.05, 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.65, 0.70):
        show('dust sRGB %.3f (8bit %3.0f)' % (v, v * 255), [s2l(v)] * 3)
    print('   r3 measured: DustShadow 0.272 -> core p5 67 ; DustLit 0.470 -> core p95 109, max ~130')

    print('\n-- deck plate --')
    show('deck target L182', [s2l(0.714)] * 3)

    print('\n-- r3 gold ember: EmberTint(1.2,0.56,0.042) x intensity, additive over dark dust --')
    dust = np.array([s2l(0.272)] * 3)
    for I in (2.4, 3.0, 3.1):
        show('ember x%.1f over dust' % I, dust + np.array([1.2, 0.56, 0.042]) * I)
    print('   r3 measured: bright-sat hue 50, sat 0.75, Lmax 235')

    print('\n-- r3 blue stroke: HSV(233.9, 0.88, 1) -> linear x intensity, additive over dark dust --')
    import colorsys
    rgb = colorsys.hsv_to_rgb(233.9 / 360, 0.88, 1.0)
    lin = s2l(np.array(rgb))
    print('   authored srgb', np.round(rgb, 3), ' linear', np.round(lin, 4))
    for I in (2.4, 3.1):
        show('blue x%.1f over dust' % I, dust + lin * I)
    print('   r3 measured: cool pixels top out at L154')
