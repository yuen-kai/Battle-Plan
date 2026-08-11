"""Visual crops: strip frame vs true peak, plus squint test."""

import os

import numpy as np
from PIL import Image

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
OUT = "Captures/AbilityJuice"


def load(i):
    return Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")


# side-by-side of the interesting window of the timeline, full res crops
box = (512 - 300, 493 - 300, 512 + 300, 493 + 300)
tiles = [load(i).crop(box).resize((360, 360), Image.LANCZOS) for i in (11, 12, 13, 14, 15, 16)]
sheet = Image.new("RGB", (360 * 6 + 5 * 6, 360), (0, 0, 0))
for k, t in enumerate(tiles):
    sheet.paste(t, (k * 366, 0))
sheet.save(f"{OUT}/_r2_core_timeline.png")
print("wrote _r2_core_timeline.png  (f0011..f0016, 600px crop each)")

# peak vs strip frame, big
a = load(12).crop(box).resize((520, 520), Image.LANCZOS)
b = load(13).crop(box).resize((520, 520), Image.LANCZOS)
pair = Image.new("RGB", (1048, 520), (0, 0, 0))
pair.paste(a, (0, 0))
pair.paste(b, (528, 0))
pair.save(f"{OUT}/_r2_core_peak_vs_strip.png")
print("wrote _r2_core_peak_vs_strip.png  (left f0012 peak | right f0013 strip)")

# squint: whole strip at thumbnail size, upscaled back so it is legible
strip = Image.open("Captures/AbilityJuice/strips/r2/impactcore.jpg").convert("RGB")
small = strip.resize((strip.width // 8, strip.height // 8), Image.LANCZOS)
small.resize((strip.width // 2, strip.height // 2), Image.NEAREST).save(f"{OUT}/_r2_core_squint.png")
print("wrote _r2_core_squint.png")

# a rebuilt strip using f0012 instead of f0013, to show what the pick costs
panels = [load(0), load(12), load(30)]
w = 512
rebuilt = Image.new("RGB", (w * 3 + 16, w), (0, 0, 0))
for k, p in enumerate(panels):
    rebuilt.paste(p.resize((w, w), Image.LANCZOS), (k * (w + 8), 0))
rebuilt.save(f"{OUT}/_r2_core_strip_if_peak.png")
print("wrote _r2_core_strip_if_peak.png")

# side by side: our strip mid panel vs a clash reference mid panel
ours = Image.open("Captures/AbilityJuice/strips/r2/impactcore.jpg").convert("RGB").crop((520, 0, 1032, 512))
for name in ("cm-everyability-23", "cm-everyability-20"):
    ref = Image.open(f"Captures/AbilityJuice/strips/reference/{name}.jpg").convert("RGB")
    pw = ref.width // 3
    rm = ref.crop((pw + 4, 0, 2 * pw - 4, ref.height)).resize((512, 512), Image.LANCZOS)
    cmp_ = Image.new("RGB", (1032, 512), (0, 0, 0))
    cmp_.paste(ours, (0, 0))
    cmp_.paste(rm, (520, 0))
    cmp_.save(f"{OUT}/_r2_core_vs_{name}.png")
    print(f"wrote _r2_core_vs_{name}.png")
