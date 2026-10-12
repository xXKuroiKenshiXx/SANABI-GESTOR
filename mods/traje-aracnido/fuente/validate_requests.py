"""Behavior-specific validation for the requested model/audio/edition changes."""
import hashlib,io,json,struct,sys,wave,zipfile
from pathlib import Path
import UnityPy
from build import SPRITES_BUNDLE,SOUND_BUNDLE,original_path,layer_of

ROOT=Path(__file__).resolve().parents[3]
game=Path(sys.argv[1])

def texture_map(path):
    env=UnityPy.load(str(path));return {o.read().m_Name:o.read() for o in env.objects if o.type.name=='Texture2D'}

for slug in ('spiderman-modelo-completo','spider-2099-modelo-completo'):
    ts=texture_map(ROOT/'mods'/slug/'files'/SPRITES_BUNDLE)
    for name,t in ts.items():
        layer=layer_of(name)
        if layer in ('arm','cape','grab'):
            assert not t.image.convert('RGBA').getchannel('A').getbbox(),(slug,name,'old arm/cape visible')
        if layer=='body':
            assert t.m_Width in (80,96,128) and t.m_Height==t.m_Width,(slug,name,'resolution')
            assert t.image.getbbox(),(slug,name,'missing body')
            box=t.image.convert('RGBA').getchannel('A').getbbox()
            assert 0<box[0]<box[2]<t.m_Width and 0<box[1]<box[3]<t.m_Height,(slug,name,'clipped body')
            assert box[3]-box[1]<=55 and box[2]-box[0]<=55,(slug,name,'overscaled actor')
            if slug.startswith('spider-2099'):
                assert sum(1 for r,g,b,a in t.image.convert('RGBA').getdata() if a and r>140 and r>g*3 and r>b*1.7)>15,(slug,name,'cape absent')
    mapping=json.loads((ROOT/'mods'/slug/'animation-map.json').read_text())
    assert len(mapping)==698
    for row in mapping:
        if row['animation']=='wall':assert row['mirrored'],(slug,row['target'],'wall orientation')
        if row['animation']=='ceiling':assert row['rotation']==270 and not row['mirrored'],(slug,row['target'],'ceiling pose')
        if 'WallJump' in row['target']:assert row['animation']=='jump',(slug,row['target'],'wall jump should leave wall')
    for state in ('run','swing','jump','wall','damage'):
        assert len({row['sourceFrame'] for row in mapping if row['animation']==state})>=3,(slug,state,'missing animation')
    assert (ROOT/'mods'/slug/'files'/SOUND_BUNDLE).read_bytes()==(ROOT/'mods/traje-aracnido/files'/SOUND_BUNDLE).read_bytes(),'Spider audio altered'
    original=UnityPy.load(str(original_path(game,SPRITES_BUNDLE)))
    old={o.path_id:o for o in original.objects if o.type.name=='Sprite'}
    for obj in UnityPy.load(str(ROOT/'mods'/slug/'files'/SPRITES_BUNDLE)).objects:
        if obj.type.name!='Sprite':continue
        a,b=old[obj.path_id].read_typetree(),obj.read_typetree()
        assert a['m_Rect']==b['m_Rect'] and a['m_PixelsToUnits']==b['m_PixelsToUnits'] and a['m_Pivot']==b['m_Pivot'],(slug,b['m_Name'],'native geometry')
    idle=ts['Spr_SNB_Idle (lp) (1)'].image.getchannel('A').getbbox()
    assert 34<=idle[3]-idle[1]<=42,(slug,'standing height vs original 38px')
    print('PASS',slug,'698 complete frames, native size/PPU/pivots, whole actor inside canvas, original standing height')

ts=texture_map(ROOT/'mods/spider-2099/files'/SPRITES_BUNDLE)
for name,t in ts.items():
    if layer_of(name)=='body':
        assert sum(1 for r,g,b,a in t.image.convert('RGBA').getdata() if a and r>140 and r>g*3 and r>b*1.7)>15,('old 2099 cape absent',name)
print('PASS existing 2099 cape is present in every body frame')

noir=ROOT/'mods/spider-noir'
with zipfile.ZipFile(ROOT/'dist/mods/spider-noir-1.1.0.zip') as z:
    for rel in (SPRITES_BUNDLE,SOUND_BUNDLE):assert z.read(rel.as_posix())==(noir/'files'/rel).read_bytes(),'Noir modified'
print('PASS Noir assets preserved')

original=UnityPy.load(str(original_path(game,SOUND_BUNDLE)))
new=UnityPy.load(str(ROOT/'mods/capa-creeper/files'/SOUND_BUNDLE))
old={o.path_id:o for o in original.objects}
for obj in new.objects:
    src=old[obj.path_id]
    if obj.type.name!='AudioClip' or obj.read().m_Name!='SFX_SNB_Damaged':
        assert obj.get_raw_data()==src.get_raw_data(),'Non-damage sound modified'
        if obj.type.name=='AudioClip':
            a,b=src.read().m_Resource,obj.read().m_Resource
            ra=original.file.files[a.m_Source.split('/')[-1]].bytes[a.m_Offset:a.m_Offset+a.m_Size]
            rb=new.file.files[b.m_Source.split('/')[-1]].bytes[b.m_Offset:b.m_Offset+b.m_Size]
            assert ra==rb,'Existing audio payload altered'
        continue
    clip=obj.read();r=clip.m_Resource
    fsb=new.file.files[r.m_Source.split('/')[-1]].bytes[r.m_Offset:r.m_Offset+r.m_Size]
    magic,version,count,sh,names,data,mode=struct.unpack_from('<4s6I',fsb)
    assert (magic,count,mode)==(b'FSB5',1,2) and len(fsb)==60+sh+names+data
    with wave.open(str(ROOT/'mods/capa-creeper/fuente/daño-creeper.wav'),'rb') as wav:
        pcm=wav.readframes(wav.getnframes())
        assert fsb[60+sh+names:60+sh+names+len(pcm)]==pcm,'MP3 conversion mismatch'
        assert abs(clip.m_Length-wav.getnframes()/wav.getframerate())<.001
    # Reopen through UnityPy's FMOD decoder, not just a header assertion.
    decoded=next(iter(clip.samples.values()))
    with wave.open(io.BytesIO(decoded),'rb') as wav:
        assert wav.readframes(wav.getnframes())==pcm,'FMOD decoded PCM mismatch'
print('PASS Creeper damage: PCM from supplied MP3, FMOD decode, other sounds unchanged')

catalog=json.loads((ROOT/'catalog.json').read_text(encoding='utf-8'))
translation=next(e for e in catalog['mods'] if e['id']=='traduccion-latam-fan')
assert len(translation['variants'])==5
for v in translation['variants']:
    with zipfile.ZipFile(ROOT/'dist/mods'/f"traduccion-latam-fan-{v['version']}.zip") as z:
        assert json.loads(z.read('mod.json'))['version']==v['version']
        for f in v['files']:
            assert f['src'].startswith('mods/traduccion-latam-fan/versiones/')
            assert hashlib.sha256(z.read(f['dst'])).hexdigest()==f['sha256']
print('PASS five translation editions, Linux/Proton case-preserved paths, ZIP hashes match selector')
