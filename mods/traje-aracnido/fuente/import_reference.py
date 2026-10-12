"""Import the user-supplied sprite atlas into individual whole-figure frames.

This is an asset importer, not a procedural character redraw. The original
reference remains unchanged. Frames retain the artwork's outlines and anatomy.
"""
import json
from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'mods/spiderman-modelo-completo/fuente'

def extract():
    source=OUT/'referencia.png'
    im=Image.open(source).convert('RGB');a=np.array(im)
    bg=a[0,0].astype(int)
    mask=np.max(np.abs(a.astype(int)-bg),axis=2)>35
    seen=np.zeros(mask.shape,dtype=bool);h,w=mask.shape
    frames=[]
    for y,x in zip(*np.nonzero(mask)):
        if seen[y,x]:continue
        q=deque([(int(x),int(y))]);seen[y,x]=True;points=[]
        while q:
            xx,yy=q.popleft();points.append((xx,yy))
            for ny in range(max(0,yy-1),min(h,yy+2)):
                for nx in range(max(0,xx-1),min(w,xx+2)):
                    if mask[ny,nx] and not seen[ny,nx]:seen[ny,nx]=True;q.append((nx,ny))
        if len(points)<200:continue
        xs,ys=zip(*points);box=(min(xs),min(ys),max(xs)+1,max(ys)+1)
        x0,y0,x1,y1=box
        # Discard credits, logos, tiny disconnected marks, portrait and ropes.
        if x1-x0<12 or y1-y0<22 or x1-x0>110 or y1-y0>100 or y0>1950:continue
        if 1450<y0<1500 or (x0>460 and 180<y0<260):continue
        rgba=np.zeros((y1-y0,x1-x0,4),dtype=np.uint8)
        for xx,yy in points:rgba[yy-y0,xx-x0]=[*a[yy,xx],255]
        frames.append((box,Image.fromarray(rgba)))
    frames.sort(key=lambda f:(round(f[0][1]/35),f[0][0]))
    contact=Image.new('RGB',(800,((len(frames)+9)//10)*112),(26,19,42));d=ImageDraw.Draw(contact)
    records=[]
    for i,(box,frame) in enumerate(frames):
        path=OUT/'cuadros'/f'{i:03}.png';path.parent.mkdir(parents=True,exist_ok=True);frame.save(path)
        x=(i%10)*80;y=(i//10)*112
        show=frame.copy();show.thumbnail((76,88),Image.Resampling.NEAREST)
        contact.paste(show,(x+(80-show.width)//2,y+20),show)
        d.text((x+2,y+2),str(i),fill='white')
        records.append(dict(id=i,box=box,file=f'cuadros/{i:03}.png'))
    contact.save(ROOT/'mods/_qa/referencia-cuadros.png')
    (OUT/'cuadros.json').write_text(json.dumps(records,indent=2))
    print('Imported full-body frames:',len(records),'source:',im.size,'background:',bg.tolist())

if __name__=='__main__':extract()
