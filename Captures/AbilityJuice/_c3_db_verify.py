import numpy as np, os
from PIL import Image
from _c3_db_setup import ROOT, load, lum, plate_of

d3 = os.path.join(ROOT, 'shots/r3/debris')
plate, fr = plate_of(d3)
st = Image.open(os.path.join(ROOT, 'strips/r3/debris.jpg')).convert('RGB')
W, H = st.size; pw = H; step = (W-pw)/2
p2 = np.asarray(st.crop((int(step), 0, int(step)+pw, H))).astype(np.float32)

print('strip', st.size, ' panel', p2.shape)
best = None
for i in range(10, 20):
    im = Image.open(os.path.join(d3, fr[i])).convert('RGB')
    for s in (512, 640, 768, 1024):
        c = im.resize((s, s), Image.LANCZOS)
        off = (s-512)//2
        c = np.asarray(c.crop((off, off, off+512, off+512))).astype(np.float32)
        err = float(np.abs(c-p2).mean())
        if best is None or err < best[0]:
            best = (err, i, s)
print('best match: frame %d (t=%+0.3fs), source scale %d, mean abs err %.2f'
      % (best[1], (best[1]-12)/30.0, best[2], best[0]))
st.crop((int(step), 0, int(step)+pw, H)).save(os.path.join(ROOT, '_c3_db_shipped_panel.jpg'), quality=95)
st.crop((int(2*step), 0, int(2*step)+pw, H)).save(os.path.join(ROOT, '_c3_db_shipped_after.jpg'), quality=95)
