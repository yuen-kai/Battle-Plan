import numpy as np
from PIL import Image

B = "Captures/AbilityJuice"
D = f"{B}/shots/r4/debris"


def load(p):
    return Image.open(p).convert("RGB")


# zoom on money frame + aftermath, centred at (515, 500)
cx, cy, r = 515, 495, 300
for fi, tag in [(15, "money_+0.100"), (28, "after_+0.533"), (33, "tail_+0.700")]:
    im = load(f"{D}/f{fi:04d}.jpg").crop((cx - r, cy - r, cx + r, cy + r)).resize((600, 600), Image.NEAREST)
    im.save(f"{B}/_ad4_zoom_{tag}.png")

# side by side: ours money frame vs strongest reference impact panel, matched height
ours = load(f"{B}/strips/r4/debris.jpg")
ref = load(f"{B}/strips/reference/cm-8newabilities-19.jpg")
ref2 = load(f"{B}/strips/reference/cm-everyability-20.jpg")
sbs = Image.new("RGB", (1552, 512 * 3), (0, 0, 0))
sbs.paste(ours, (0, 0))
sbs.paste(ref, (0, 512))
sbs.paste(ref2, (0, 1024))
sbs.resize((1024, 1013)).save(f"{B}/_ad4_vs_ref.png")

# squint test: heavy blur + downscale, ours vs r3 vs refs
def squint(p, n=64):
    return load(p).resize((n * 3 + 2, n), Image.LANCZOS).resize((512 * 3, 512), Image.NEAREST)


rows = [
    f"{B}/strips/r4/debris.jpg",
    f"{B}/strips/r3/debris.jpg",
    f"{B}/strips/reference/cm-8newabilities-19.jpg",
    f"{B}/strips/reference/cm-everyability-20.jpg",
]
sq = Image.new("RGB", (1536, 512 * len(rows)), (0, 0, 0))
for i, p in enumerate(rows):
    sq.paste(squint(p), (0, i * 512))
sq.resize((1024, int(1024 * len(rows) * 512 / 1536))).save(f"{B}/_ad4_squint.png")

# aftermath hue map: highlight non-ember hues
import numpy as np

a = np.asarray(load(f"{D}/f0028.jpg")).astype(np.float32)
mx, mn = a.max(axis=2), a.min(axis=2)
S = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
d = mx - mn
r_, g_, b_ = a[..., 0], a[..., 1], a[..., 2]
H = np.zeros_like(mx)
m = (d > 1e-6) & (mx == r_); H[m] = (60 * ((g_ - b_) / np.maximum(d, 1e-6)))[m] % 360
m = (d > 1e-6) & (mx == g_); H[m] = (60 * ((b_ - r_) / np.maximum(d, 1e-6)) + 120)[m]
m = (d > 1e-6) & (mx == b_); H[m] = (60 * ((r_ - g_) / np.maximum(d, 1e-6)) + 240)[m]
sel = S >= 0.35
sub = H[sel]
hist, e = np.histogram(sub, bins=36, range=(0, 360))
print("aftermath f28 hue histogram of S>=0.35 pixels (bin start : count):")
for i, c in enumerate(hist):
    if c > 50:
        print(f"  {e[i]:5.0f}-{e[i+1]:5.0f} : {c}")
print("total S>=0.35 px:", int(sel.sum()))
green = ((H > 60) & (H < 160) & sel).sum()
print("green-band (60-160deg) saturated px:", int(green), f"{100*green/max(sel.sum(),1):.1f}% of saturated")
