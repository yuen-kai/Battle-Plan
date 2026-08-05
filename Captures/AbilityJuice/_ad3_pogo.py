import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

fs = frames('r3','pogo')
print('pogo frames', len(fs))
idxs = list(range(42, 72, 3))
tiles=[]
for i in idxs:
    fr = load(fp('r3','pogo',i))
    im = Image.fromarray(fr.astype(np.uint8)).crop((860,320,1024,470))
    im = im.resize((im.width*3, im.height*3), Image.NEAREST)
    tiles.append((i,im))
w,h = tiles[0][1].size
out = Image.new('RGB',(w*len(tiles)+4*(len(tiles)-1), h),(15,15,18))
for k,(i,t) in enumerate(tiles): out.paste(t,(k*(w+4),0))
out.save(os.path.join(ROOT,'_ad3_pogo_track.png'))
print('saved', idxs, out.size)
