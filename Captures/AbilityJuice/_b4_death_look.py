"""Round-4 death builder: look at the frames around the deletion, zoomed."""
import sys
from PIL import Image

shot = "Captures/AbilityJuice/shots/r3/death"
frames = [10, 11, 12, 13, 14, 15, 16, 18, 20, 24, 30, 40]
crop = (300, 250, 780, 730)  # centred on the dying unit
scale = 1

tiles = []
for f in frames:
    im = Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB").crop(crop)
    tiles.append((f, im))

w, h = tiles[0][1].size
cols, rows = 4, 3
sheet = Image.new("RGB", (cols * w, rows * h), (0, 0, 0))
for i, (f, im) in enumerate(tiles):
    sheet.paste(im, ((i % cols) * w, (i // cols) * h))
sheet.save("Captures/AbilityJuice/_b4_death_look.png")
print("frames", frames)
print("tile", w, h)
