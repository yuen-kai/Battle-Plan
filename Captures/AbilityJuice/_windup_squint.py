"""Side-by-side squint test: round one's strip against the rebuilt mock, both at thumbnail scale."""

from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).parent
R1 = HERE / "strips" / "r1" / "windup.jpg"
MOCK = HERE / "_r2_mock_strip.png"
REF = HERE / "strips" / "reference" / "cm-everyability-09.jpg"
OUT = HERE / "_r2_squint.png"

PANEL = 300

old = Image.open(R1).convert("RGB")
panels_old = [old.crop((i * old.width // 3, 0, (i + 1) * old.width // 3, old.height)) for i in range(3)]

mock = Image.open(MOCK).convert("RGB")
# The mock strip is seven panels; take the three a picker would land on.
picks = [1, 4, 6]
step = mock.width // 7
panels_new = [mock.crop((i * step, 0, (i + 1) * step, mock.height)) for i in picks]

ref = Image.open(REF).convert("RGB")
panels_ref = [ref.crop((i * ref.width // 3, 0, (i + 1) * ref.width // 3, ref.height)) for i in range(3)]


def row(panels, label):
    tiles = [np.asarray(p.resize((PANEL, PANEL), Image.LANCZOS)) for p in panels]
    strip = np.concatenate(tiles, axis=1)
    print(f"{label}: {strip.shape[1]}x{strip.shape[0]}")
    return strip


rows = [row(panels_ref, "clash mini"), row(panels_old, "round 1"), row(panels_new, "round 2 mock")]
gap = np.full((10, PANEL * 3, 3), 20, dtype=np.uint8)
stack = np.concatenate([rows[0], gap, rows[1], gap, rows[2]], axis=0)
Image.fromarray(stack).save(OUT)

# Thumbnail version: what a stranger sees at a glance.
Image.fromarray(stack).resize((PANEL * 3 // 3, stack.shape[0] // 3), Image.LANCZOS).save(
    HERE / "_r2_squint_small.png"
)
print(f"wrote {OUT}")
