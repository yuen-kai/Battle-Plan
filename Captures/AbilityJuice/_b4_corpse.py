"""Round-4 death builder: lift the shadows so the corpse inside the mark is visible."""
import numpy as np
from PIL import Image

shot = "Captures/AbilityJuice/shots/r3/death"
crop = (400, 350, 680, 630)
frames = [12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 36]

tiles = []
for f in frames:
    a = np.asarray(Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB").crop(crop), dtype=np.float32) / 255.0
    lifted = np.clip(a ** 0.36, 0, 1)
    tiles.append(Image.fromarray((lifted * 255).astype(np.uint8)).resize((360, 360), Image.LANCZOS))

sheet = Image.new("RGB", (4 * 360, 3 * 360))
for i, im in enumerate(tiles):
    sheet.paste(im, ((i % 4) * 360, (i // 4) * 360))
sheet.save("Captures/AbilityJuice/_b4_corpse.png")
print(frames)
