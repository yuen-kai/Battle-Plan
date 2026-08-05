"""Round-3 critic: squint test and matched-scale comparison against Clash Mini."""

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

# ---- squint: our strip at thumbnail size, and greyscale
ours = Image.open("strips/r2/numbers.jpg")
r1 = Image.open("strips/r1/numbers.jpg")
w, h = ours.size
small = ours.resize((w // 6, h // 6), Image.LANCZOS)
small1 = r1.resize((w // 6, h // 6), Image.LANCZOS)
sheet = Image.new("RGB", (w // 6, h // 3 + 6), (20, 20, 20))
sheet.paste(small1, (0, 0))
sheet.paste(small, (0, h // 6 + 6))
sheet.save("_c3_num_squint.png")
print("squint sheet", sheet.size, "(top = r1, bottom = r2)")

# greyscale of the r2 strip: does the number survive with hue removed?
g = np.asarray(ours.convert("RGB")).astype(np.float32)
gL = 0.2126 * g[..., 0] + 0.7152 * g[..., 1] + 0.0722 * g[..., 2]
Image.fromarray(np.clip(gL, 0, 255).astype(np.uint8)).save("_c3_num_grey.png")

# ---- Clash Mini cell pitch, so numeral size can be quoted in cells
for name in ("cm-everyability-23", "cm-everyability-20"):
    im = np.asarray(Image.open(f"strips/reference/{name}.jpg").convert("RGB")).astype(np.float32)
    L = 0.2126 * im[..., 0] + 0.7152 * im[..., 1] + 0.0722 * im[..., 2]
    panel = L[:, 358:684]  # middle panel
    # checkerboard: alternating light/dark squares -> autocorrelate a row
    row = panel[170] - panel[170].mean()
    ac = np.correlate(row, row, "full")[len(row) - 1 :]
    peaks = [
        k
        for k in range(20, 140)
        if ac[k] > 0 and ac[k] == max(ac[max(20, k - 8) : k + 9])
    ]
    print(f"{name}: middle-panel autocorrelation peaks (px) = {peaks[:6]}")

# ---- matched-scale side by side: our badge next to CM's numerals, cell-normalised
OUR_CELL = 133.0  # px per cell on the 512 strip panel
CM_CELL = 57.0  # measured below / verified by autocorrelation


def panel(img, idx, n=3):
    w, h = img.size
    pw = (w - (n - 1) * 8) // n
    x = idx * (pw + 8)
    return img.crop((x, 0, x + pw, h))


our3 = panel(ours, 2)
cm = Image.open("strips/reference/cm-everyability-23.jpg")
cm3 = panel(cm, 2)
# scale CM so one cell matches ours
k = OUR_CELL / CM_CELL
cm3 = cm3.resize((int(cm3.width * k), int(cm3.height * k)), Image.LANCZOS)
H = max(our3.height, cm3.height)
comp = Image.new("RGB", (our3.width + cm3.width + 12, H), (12, 12, 12))
comp.paste(our3, (0, 0))
comp.paste(cm3, (our3.width + 12, 0))
comp.save("_c3_num_vs_cm.png")
print("matched-cell comparison", comp.size, f"(CM upscaled {k:.2f}x so cells match)")

# ---- thumbnail of that comparison (the stranger test)
comp.resize((comp.width // 5, comp.height // 5), Image.LANCZOS).save("_c3_num_vs_cm_small.png")
