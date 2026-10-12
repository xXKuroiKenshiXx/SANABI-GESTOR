"""Validate released local assets against original bundles and packaged hashes."""
import hashlib
import json
import struct
import sys
import zipfile
from pathlib import Path
import UnityPy
from build import original_path, SPRITES_BUNDLE, SOUND_BUNDLE
from extra_mods import DLC
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[3]
game=Path(sys.argv[1])
catalog=json.loads((ROOT/'catalog.json').read_text(encoding='utf-8'))
originals={}
report=[]
sheet=Image.new('RGB',(1120,((len(catalog['mods'])+3)//4)*210),'#292E39')
draw=ImageDraw.Draw(sheet)
for i,entry in enumerate(catalog['mods']):
    slug=entry['id']
    if slug=='traduccion-latam-fan':continue
    if entry['category'] in ('habilidades','personajes'):
        assert entry['verification']=='experimental' and entry['requires']==['bepinex']
        with zipfile.ZipFile(ROOT/'dist/mods'/f"{slug}-{entry['version']}.zip") as z:
            for f in entry['files']:
                data=z.read(f['dst']);assert data[:2]==b'MZ' and hashlib.sha256(data).hexdigest()==f['sha256']
            assert z.read('preview.png')==(ROOT/entry['preview']).read_bytes()
        report.append(dict(mod=slug,game_logic='compiled-plugin',verification='experimental'))
        preview=Image.open(ROOT/entry['preview']).convert('RGB');preview.thumbnail((270,165),Image.Resampling.NEAREST)
        x,y=(i%4)*280,(i//4)*210;sheet.paste(preview,(x,y+26));draw.text((x+5,y+6),entry['name'][:32],fill='white')
        continue
    rel=Path(entry['files'][0]['dst'])
    assert rel in (SPRITES_BUNDLE,DLC),rel
    if rel not in originals:
        env=UnityPy.load(str(original_path(game,rel)))
        originals[rel]={o.path_id:o for o in env.objects}
    env=UnityPy.load(str(ROOT/entry['files'][0]['src']))
    objects={o.path_id:o for o in env.objects}
    old=originals[rel]
    assert objects.keys()==old.keys(),slug+' object inventory'
    changed,meshes=0,0
    for oid,obj in objects.items():
        src=old[oid]
        assert obj.type.name==src.type.name
        if obj.type.name=='Texture2D':
            a,b=src.read(),obj.read()
            assert a.m_TextureFormat==b.m_TextureFormat
            factor=2 if slug.endswith('modelo-completo') and (a.m_Width,a.m_Height)!=(b.m_Width,b.m_Height) else 1
            assert (a.m_Width*factor,a.m_Height*factor)==(b.m_Width,b.m_Height)
            changed+=a.image.tobytes()!=b.image.tobytes()
            if b.m_Name.startswith(('Spr_HUD_','Spr_DLC_HUD_','Spr_vfxSPC_','Spr_SPC_Bullet')):
                assert a.image.tobytes()==b.image.tobytes(),b.m_Name
        elif obj.type.name=='Sprite':
            a,b=src.read_typetree(),obj.read_typetree()
            for key in ('m_Pivot','m_PhysicsShape','m_Bones'):
                assert a.get(key)==b.get(key),(slug,a['m_Name'],key)
            factor=b['m_PixelsToUnits']/a['m_PixelsToUnits']
            assert factor in ((1,2) if slug.endswith('modelo-completo') else (1,))
            assert {k:v*factor for k,v in a['m_Rect'].items()}==b['m_Rect'],(slug,a['m_Name'],'world scale')
            if a['m_RD']['m_VertexData']!=b['m_RD']['m_VertexData']:
                rd=b['m_RD'];vd=rd['m_VertexData']
                assert vd['m_VertexCount']==4 and len(vd['m_DataSize'])==208
                assert len(rd['m_IndexBuffer'])==12 and rd['m_SubMeshes'][0]['indexCount']==6
                assert struct.unpack('<6H',bytes(rd['m_IndexBuffer']))==(0,1,2,0,2,3)
                meshes+=1
        else:
            assert obj.get_raw_data()==src.get_raw_data(),(slug,obj.type.name,'game logic changed')
    assert changed>0 and meshes>0
    # ZIP payloads and manifests must match the visible catalog exactly.
    zpath=ROOT/'dist/mods'/f"{slug}-{entry['version']}.zip"
    with zipfile.ZipFile(zpath) as z:
        for f in entry['files']:
            data=z.read(f['dst'])
            assert hashlib.sha256(data).hexdigest()==f['sha256'],(slug,f['dst'])
        m=json.loads(z.read('mod.json'))
        assert m['id']==slug and m['version']==entry['version']
        assert z.read('preview.png')==(ROOT/entry['preview']).read_bytes()
    if slug in ('traje-aracnido','spider-noir','spider-2099'):
        previous='1.1.0' if slug=='traje-aracnido' else '1.0.0'
        baseline=ROOT/'dist/mods'/f'{slug}-{previous}.zip'
        if baseline.is_file():
            with zipfile.ZipFile(baseline) as z:
                assert z.read(SOUND_BUNDLE.as_posix())==(ROOT/'mods'/slug/'files'/SOUND_BUNDLE).read_bytes(),'Audio changed'
    if slug in ('major-son-en-campana','heroe-en-dlc'):
        mapping=json.loads((ROOT/'mods'/slug/'animation-map.json').read_text())
        assert len(mapping)==len({row['target'] for row in mapping}), 'Duplicate actor mapping'
    preview=Image.open(ROOT/entry['preview']).convert('RGB')
    preview.thumbnail((270,165),Image.Resampling.NEAREST)
    x,y=(i%4)*280,(i//4)*210
    sheet.paste(preview,(x,y+26))
    draw.text((x+5,y+6),entry['name'][:32],fill='white')
    report.append(dict(mod=slug,changed_textures=changed,replaced_render_meshes=meshes,game_logic='unchanged',audio='creeper-damage' if slug=='capa-creeper' else 'preserved' if slug.startswith('spider') or slug=='traje-aracnido' else 'original'))
qa=ROOT/'mods/_qa'
sheet.save(qa/'mods-finales.png')
(qa/'assets-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
