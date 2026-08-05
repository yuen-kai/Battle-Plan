"""Round-4 death builder: how legible is the rider through the whole landing?"""
import numpy as np
from PIL import Image

shot = "Captures/AbilityJuice/shots/r3/pogo"
crop = (330, 300, 810, 780)
frames = [44, 45, 46, 47, 48, 50, 52, 56, 60, 66, 74, 84]
tiles = [Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB").crop(crop) for f in frames]
w, h = tiles[0].size
sheet = Image.new("RGB", (4 * w, 3 * h))
for i, im in enumerate(tiles):
    sheet.paste(im, ((i % 4) * w, (i // 4) * h))
sheet.save("Captures/AbilityJuice/_b4_pogo_look.png")

# the rider's blue base, as the critic counted it
for f in frames:
    a = np.asarray(Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB"), dtype=np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    blue = (b > 90) & (b - r > 45) & (b - g > 30)
    print("f%02d t=%+.3f  blue px %5d" % (f, -0.4 + f / 30.0, int(blue.sum())))
