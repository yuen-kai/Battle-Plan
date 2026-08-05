import os
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}


def crop(shot, idx, cx, cy, cells, out):
    p = PITCH[shot]
    half = int(cells*p/2)
    im = Image.open(os.path.join(SH, shot, 'f%04d.jpg' % idx)).convert('RGB')
    W, H = im.size
    x0 = max(0, int(cx-half)); y0 = max(0, int(cy-half))
    x1 = min(W, int(cx+half)); y1 = min(H, int(cy+half))
    c = im.crop((x0, y0, x1, y1))
    bg = Image.new('RGB', (2*half, 2*half), (0, 0, 0))
    bg.paste(c, (x0-int(cx-half), y0-int(cy-half)))
    return bg.resize((out, out), Image.LANCZOS)


def sheet(shot, frames, c, cells, name, cols=6, out=300):
    rows = (len(frames)+cols-1)//cols
    s = Image.new('RGB', (cols*out, rows*out), (0, 0, 0))
    for k, f in enumerate(frames):
        s.paste(crop(shot, f, c[0], c[1], cells, out), ((k % cols)*out, (k//cols)*out))
    s.save(os.path.join(BASE, name))
    print(name, s.size, 'frames', frames)


sheet('death', list(range(10, 22)), (518, 470), 3.0, '_ad_death_zoom.png')
sheet('pogo', list(range(43, 55)), (770, 500), 3.5, '_ad_pogo_zoom.png')
