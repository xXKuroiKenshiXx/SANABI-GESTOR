"""Reopen exported bundles and verify geometry, cape, masks and audio headers."""
import json
import struct
import sys
from pathlib import Path
import UnityPy
from PIL import Image, ImageDraw
from build import SPRITES_BUNDLE, SOUND_BUNDLE, SOUNDS, layer_of, rim_mask, original_path

ROOT = Path(__file__).resolve().parents[3]
game = Path(sys.argv[1])

def textures(path):
    return {t.m_Name: t for o in UnityPy.load(str(path)).objects
            if o.type.name == 'Texture2D' for t in [o.read()]}

original = textures(original_path(game,SPRITES_BUNDLE))
names = ['Spr_SNB_Idle (lp) (1)', 'Spr_SNB_Running (lp) (4)', 'Spr_SNB_Swing (5)', 'Spr_SNB_Jumping (lp) (4)']
sheet = Image.new('RGB', (960, 420), '#242832')
draw = ImageDraw.Draw(sheet)
summary = []
for row, slug in enumerate(('traje-aracnido', 'spider-noir', 'spider-2099')):
    folder = ROOT/'mods'/slug
    updated = textures(folder/'files'/SPRITES_BUNDLE)
    assert original.keys() == updated.keys(), 'Texture inventory changed'
    changed, capes = 0, 0
    for name, tex in updated.items():
        src = original[name]
        assert (tex.m_Width, tex.m_Height, tex.m_TextureFormat) == (src.m_Width, src.m_Height, src.m_TextureFormat), name
        if layer_of(name):
            assert tex.image.size == src.image.size
            changed += tex.image.tobytes() != src.image.tobytes()
        if layer_of(name) == 'cape':
            alpha = tex.image.convert('RGBA').getchannel('A')
            if slug != 'spider-2099':
                assert not alpha.getbbox(), name
            else:
                assert alpha.tobytes() == src.image.convert('RGBA').getchannel('A').tobytes(), name
                capes += bool(alpha.getbbox())
        if name.startswith(('Spr_SNB_White', 'Spr_SNB_GChangeFlash', 'Spr_SNB_HandChange')):
            assert tex.image.tobytes() == src.image.tobytes(), 'Flash changed: '+name
    for col, name in enumerate(names):
        assert name in updated, name
        im = updated[name].image.convert('RGBA')
        mask = updated.get(name+'_M')
        if mask:
            assert mask.image.convert('RGBA').getchannel('A').tobytes() == rim_mask(im).getchannel('A').tobytes(), name
        im = im.crop(im.getbbox()).resize((im.getbbox()[2]-im.getbbox()[0], im.getbbox()[3]-im.getbbox()[1]))
        im = im.resize((im.width*3, im.height*3), Image.Resampling.NEAREST)
        sheet.paste(im, (col*240+80,row*140+25),im)
        draw.text((col*240+4,row*140+4), slug+' / '+name.replace('Spr_SNB_', '')[:15], fill='white')
    env = UnityPy.load(str(folder/'files'/SOUND_BUNDLE))
    checked_audio = 0
    for obj in env.objects:
        if obj.type.name == 'AudioClip':
            clip = obj.read()
            if clip.m_Name in SOUNDS:
                r = clip.m_Resource
                resource = env.file.files[r.m_Source.split('/')[-1]].bytes
                header = resource[r.m_Offset:r.m_Offset+r.m_Size]
                magic, version, count, sh, namesize, datasize, mode = struct.unpack_from('<4s6I',header)
                assert magic == b'FSB5' and count == 1 and mode == 2
                assert len(header) == 60+sh+namesize+datasize
                checked_audio += 1
    assert checked_audio == len(SOUNDS)
    summary.append(dict(mod=slug, changed_textures=changed, animated_capes=capes, audio_clips=checked_audio))
qa = ROOT/'mods'/'_qa'
qa.mkdir(exist_ok=True)
sheet.save(qa/'collection.png')
(qa/'validation.json').write_text(json.dumps(summary,indent=2)+'\n')
print(json.dumps(summary,indent=2))
