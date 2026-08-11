import numpy as np
from PIL import Image

B = "Captures/AbilityJuice"
img = np.asarray(Image.open(f"{B}/strips/r4/debris.jpg").convert("RGB")).astype(np.float32)
p1 = img[:, 0:512].mean(axis=2)

# horizontal seams -> vertical pitch
g = np.abs(np.diff(p1, axis=0)).sum(axis=1)
g = g - g.mean()
ac = np.correlate(g, g, mode="full")[len(g) - 1 :]
ac /= ac[0]
pk = np.argsort(ac[30:200])[::-1][:8] + 30
print("vertical pitch candidates:", sorted(pk.tolist()))
for k in range(30, 160):
    pass
# print top distinct
seen = []
order = np.argsort(ac[30:200])[::-1] + 30
for o in order:
    if all(abs(o - s) > 8 for s in seen):
        seen.append(int(o))
    if len(seen) >= 5:
        break
print("distinct vertical peaks:", seen, [round(float(ac[s]), 3) for s in seen])

g2 = np.abs(np.diff(p1, axis=1)).sum(axis=0)
g2 = g2 - g2.mean()
ac2 = np.correlate(g2, g2, mode="full")[len(g2) - 1 :]
ac2 /= ac2[0]
seen2 = []
order2 = np.argsort(ac2[30:250])[::-1] + 30
for o in order2:
    if all(abs(o - s) > 8 for s in seen2):
        seen2.append(int(o))
    if len(seen2) >= 5:
        break
print("distinct horizontal peaks:", seen2, [round(float(ac2[s]), 3) for s in seen2])
