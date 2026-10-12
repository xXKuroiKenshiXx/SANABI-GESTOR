"""Inspect character assets and export representative textures for design work."""
import json
import sys
from collections import Counter
from pathlib import Path
import UnityPy
from PIL import Image, ImageDraw
from build import original_path

root = Path(sys.argv[1])/'SNB_Data/StreamingAssets/aa/StandaloneWindows64'
out = Path(__file__).resolve().parents[2]/'_qa'
out.mkdir(exist_ok=True)
paths = [p for p in root.rglob('*.bundle') if ('_resource/character/' in p.as_posix() or p.name in ('dlcplayerscene.unity.bundle', 'player_assets_snb.bundle') or '_resource/boss/major.bundle' in p.as_posix())]
for path in paths:
    env = UnityPy.load(str(original_path(Path(sys.argv[1]), path.relative_to(Path(sys.argv[1])))))
    names = []
    for obj in env.objects:
        if obj.type.name in ('Texture2D', 'AnimationClip', 'Sprite', 'GameObject'):
            t = obj.read()
            names.append((obj.type.name, t.m_Name))
    label = path.name.replace('.bundle', '')+'-'+path.parent.name
    (out/(label+'.json')).write_text(json.dumps(names,ensure_ascii=False,indent=2),encoding='utf-8')
    textures = [o.read() for o in env.objects if o.type.name == 'Texture2D']
    prefixes = Counter(t.m_Name.split('(')[0].rsplit('_',1)[0] for t in textures)
    print(path.relative_to(root), Counter(o.type.name for o in env.objects))
    print(prefixes.most_common(24))
    chosen = [t for t in textures if not t.m_Name.endswith(('_N','_M','_A')) and ('Idle' in t.m_Name or 'Swing (5)' in t.m_Name or 'Arm' in t.m_Name and '(1)' in t.m_Name)][:30]
    sheet = Image.new('RGB',(900, max(1,(len(chosen)+5)//6)*180),'#414550')
    d = ImageDraw.Draw(sheet)
    for i,t in enumerate(chosen):
        x,y=i%6*150,i//6*180
        im=t.image.convert('RGBA'); bb=im.getbbox()
        if not bb: continue
        im=im.crop(bb)
        scale=min(3,135/max(im.width,im.height))
        im=im.resize((max(1,round(im.width*scale)),max(1,round(im.height*scale))),Image.Resampling.NEAREST)
        sheet.paste(im,(x+5,y+25),im)
        d.text((x+3,y+3),t.m_Name[:25],fill='white')
    sheet.save(out/(label+'.png'))
