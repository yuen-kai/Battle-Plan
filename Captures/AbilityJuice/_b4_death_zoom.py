"""Round-4 death builder: is there a corpse in there at all?"""
from PIL import Image

shot = "Captures/AbilityJuice/shots/r3/death"
crop = (380, 330, 700, 650)
frames = [11, 12, 13, 14, 16, 20, 24, 28]
tiles = [Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB").crop(crop).resize((480, 480), Image.NEAREST)
         for f in frames]
sheet = Image.new("RGB", (4 * 480, 2 * 480))
for i, im in enumerate(tiles):
    sheet.paste(im, ((i % 4) * 480, (i // 4) * 480))
sheet.save("Captures/AbilityJuice/_b4_death_zoom.png")
print(frames)
