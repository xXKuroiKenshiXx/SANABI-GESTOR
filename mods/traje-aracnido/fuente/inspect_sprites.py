"""Contact sheet of representative animation frames, using the same conversion as the bundle."""
import sys
from pathlib import Path
import UnityPy
from PIL import Image, ImageDraw
from recolor import faces_for, recolor_image
from build import SPRITES_BUNDLE, layer_of, original_path

game = Path(sys.argv[1])
dest = Path(sys.argv[2])
frames = {}
for obj in UnityPy.load(str(original_path(game, SPRITES_BUNDLE))).objects:
    if obj.type.name == 'Texture2D':
        tex = obj.read()
        if layer_of(tex.m_Name) == 'body':
            frames[tex.m_Name] = tex.image.convert('RGBA')
faces = faces_for(frames)
names = sorted(frames)
selected = []
groups = {}
from recolor import frame_key
for n in names:
    groups.setdefault(frame_key(n)[0], []).append(n)
for ns in groups.values():
    selected.extend(ns[i] for i in sorted({0, len(ns)//2, len(ns)-1}))
sheet = Image.new('RGB', (960, ((len(selected)+5)//6)*150), '#242832')
d = ImageDraw.Draw(sheet)
for i, n in enumerate(selected):
    x, y = (i%6)*160, (i//6)*150
    src = frames[n]
    new = recolor_image(src, 'body', faces[n])
    for j, im in enumerate((src, new)):
        im = im.crop(im.getbbox() or (0,0,1,1))
        scale = min(2, 70/max(im.width, im.height))
        im = im.resize((max(1,int(im.width*scale)),max(1,int(im.height*scale))), Image.Resampling.NEAREST)
        sheet.paste(im, (x+j*78,y+28), im)
    d.text((x+3,y+3), n.replace('Spr_SNB_', '')[:25], fill='white')
    d.text((x+3,y+120), 'face' if faces[n] else 'NO FACE', fill='white')
dest.parent.mkdir(parents=True,exist_ok=True)
sheet.save(dest)
print(len(frames), 'frames;', len(groups), 'animations;', sum(v is None for v in faces.values()), 'without face')
