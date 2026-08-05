"""Zoom crops and the squint test."""
import numpy as np
from PIL import Image

# --- 1. zoomed crops of our three panels -----------------------------------------------
frames = {"p1_f13_+0.033": 13, "p2_f17_+0.167": 17, "p3_f30_+0.600": 30}
tiles = []
for name, fr in frames.items():
    im = Image.open(f"Captures/AbilityJuice/shots/r2/shockwave/f{fr:04d}.jpg")
    c = im.crop((160, 190, 864, 894)).resize((704, 704), Image.LANCZOS)
    tiles.append(c)
sheet = Image.new("RGB", (704 * 3 + 16, 704), (0, 0, 0))
for i, tl in enumerate(tiles):
    sheet.paste(tl, (i * (704 + 8), 0))
sheet.save("Captures/AbilityJuice/_r2_shock_zoom.png")

# --- 2. tight zoom on the crest at the impact frame ------------------------------------
im = Image.open("Captures/AbilityJuice/shots/r2/shockwave/f0017.jpg")
im.crop((330, 200, 700, 570)).resize((740, 740), Image.NEAREST).save(
    "Captures/AbilityJuice/_r2_shock_crest.png"
)
im3 = Image.open("Captures/AbilityJuice/shots/r2/shockwave/f0030.jpg")
im3.crop((330, 330, 780, 780)).resize((760, 760), Image.LANCZOS).save(
    "Captures/AbilityJuice/_r2_shock_panel3.png"
)

# --- 3. squint: ours vs the strongest references, thumbnailed --------------------------
def panels(p, w3=None):
    a = Image.open(p)
    W, H = a.size
    if w3 is None:
        w3 = (W - 16) // 3
        offs = [0, w3 + 8, 2 * (w3 + 8)]
    else:
        offs = [0, 520, 1040]
    return [a.crop((o, 0, o + w3, H)) for o in offs]


rows = [
    ("OURS r2", "Captures/AbilityJuice/strips/r2/shockwave.jpg", 512),
    ("OURS r1", "Captures/AbilityJuice/strips/r1/shockwave.jpg", 512),
    ("CM clashabilities-04", "Captures/AbilityJuice/strips/reference/cm-clashabilities-04.jpg", None),
    ("CM everyability-22", "Captures/AbilityJuice/strips/reference/cm-everyability-22.jpg", None),
    ("CM 8newabilities-07", "Captures/AbilityJuice/strips/reference/cm-8newabilities-07.jpg", None),
]
TH = 118
sq = Image.new("RGB", (TH * 3 + 8, TH * len(rows) + 4 * (len(rows) - 1)), (20, 20, 20))
for ri, (nm, p, w3) in enumerate(rows):
    for pi, pan in enumerate(panels(p, w3)):
        t = pan.resize((TH, TH), Image.LANCZOS).resize((TH, TH), Image.NEAREST)
        sq.paste(t, (pi * (TH + 4), ri * (TH + 4)))
sq.resize((sq.width * 2, sq.height * 2), Image.NEAREST).save(
    "Captures/AbilityJuice/_r2_shock_squint.png"
)
print("wrote zoom, crest, panel3, squint")
