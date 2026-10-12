"""Build cape variants and reciprocal character appearance swaps from local assets.

The player controllers, abilities, saves and story stay attached to their original
campaign. Animation groups are mapped explicitly; unmatched narrative poses use
an idle pose, and the mapping is exported for review.
"""
import json
import re
import sys
import zipfile
from collections import defaultdict
from pathlib import Path
import UnityPy
from PIL import Image, ImageDraw
from build import AA, SPRITES_BUNDLE, original_path, replace_resource, texture_bytes, rim_mask, layer_of, rectangularize_sprites, flat_normal
from recolor import classify, find_face, lum, frame_key

ROOT = Path(__file__).resolve().parents[3]
DLC = AA/'mainscene_scenes_assets/__scene/01.build/main/dlcplayerscene.unity.bundle'
INSTALL = 'Cerrá SANABI, elegí su carpeta en Ajustes y pulsá Instalar. El mod se instala y activa automáticamente. El gestor conserva el original y avisa si hay conflictos. Para volver al diseño original, desactivá el mod desde Mis mods.'
MASK = re.compile(r'^(.*)_M$')


def load(game, rel):
    env = UnityPy.load(str(original_path(game, rel)))
    textures = [o.read() for o in env.objects if o.type.name=='Texture2D']
    return env, textures


def write_images(env, textures, images, path):
    bundle=env.file
    resources={k:bytearray(v.bytes) for k,v in bundle.files.items() if k.endswith('.resS')}
    # Generate the outline of each changed silhouette, including alpha-only masks.
    for tex in textures:
        match=MASK.match(tex.m_Name)
        if match and match.group(1) in images and int(tex.m_TextureFormat) in (1,4):
            images[tex.m_Name]=rim_mask(images[match.group(1)])
        normal=re.match(r'^(.*)_N$',tex.m_Name)
        if normal and normal.group(1) in images and int(tex.m_TextureFormat) in (3,4):
            images[tex.m_Name]=flat_normal(images[normal.group(1)])
    written=0
    for tex in textures:
        im=images.get(tex.m_Name)
        if im is None: continue
        assert im.size==(tex.m_Width,tex.m_Height),tex.m_Name
        raw=texture_bytes(im,int(tex.m_TextureFormat))
        stream=tex.m_StreamData
        if stream and stream.path and stream.size:
            key=stream.path.split('/')[-1]
            assert len(raw)==stream.size and key in resources,tex.m_Name
            resources[key][stream.offset:stream.offset+stream.size]=raw
        else:
            tex.image=im;tex.save()
        written+=1
    rectangularize_sprites(env,{tex.object_reader.path_id for tex in textures if tex.m_Name in images})
    for key,res in resources.items():replace_resource(bundle,key,res)
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(bundle.save(packer='lz4'))
    return written


def package(slug,name,description,notes,rel,images,preview_name,verification='package'):
    folder=ROOT/'mods'/slug
    manifest=dict(id=slug,name=name,version='1.0.0',author='Kuro Kenshi',description=description,
                  category='personaje',tags=['skin','capa' if slug.startswith('capa') else 'intercambio'],
                  gameVersion='1.3.53',preview='preview.png',installInstructions=INSTALL,
                  compatibilityNotes=notes,verification=verification)
    (folder/'mod.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (folder/'README.md').write_text(f'# {name}\n\n{description}\n\n## Instalación\n\n{INSTALL}\n\n## Compatibilidad\n\n{notes}\n\n'
        'Paquete y sprites comprobados. Prueba dentro del juego pendiente.\n\n'
        'Regenerar con `python mods/traje-aracnido/fuente/extra_mods.py "<carpeta de SANABI>"`.\n',encoding='utf-8')
    canvas=Image.new('RGBA',(240,135),(15,18,25,255))
    im=images[preview_name]
    bb=im.getbbox()
    if bb:
        im=im.crop(bb)
        scale=min(2.6,110/im.height,210/im.width)
        im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.NEAREST)
    canvas.alpha_composite(im,((240-im.width)//2,(135-im.height)//2))
    canvas.resize((720,405),Image.Resampling.NEAREST).convert('RGB').save(folder/'preview.png')
    zdir=ROOT/'dist'/'mods';zdir.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(zdir/f'{slug}-1.0.0.zip','w',zipfile.ZIP_DEFLATED) as z:
        z.write(folder/'files'/rel,rel.as_posix())
        for f in ('mod.json','README.md','preview.png'):z.write(folder/f,f)


def cape_image(src,style,region=None):
    out=src.copy()
    bb=region or src.getbbox()
    if not bb:return out
    x0,y0,x1,y1=bb
    for y in range(y0,y1):
        for x in range(x0,x1):
            r,g,b,a=src.getpixel((x,y))
            if not a:continue
            shade=lum(r,g,b)/255
            if style=='white':
                level=round(148+min(1,shade*2)*99)
                color=(level,min(255,level+3),min(255,level+7))
            else:
                # Original pixel-art creeper motif; no downloaded texture.
                u=int((x-x0)*12/max(1,x1-x0));v=int((y-y0)*16/max(1,y1-y0))
                eye=(3<=u<=4 or 7<=u<=8) and 4<=v<=5
                mouth=(5<=u<=6 and 6<=v<=7) or (4<=u<=7 and 8<=v<=10) or (u in (4,7) and v==11)
                if eye or mouth:color=(15,30,20)
                else:
                    tile=((u//2+v//2)%3)*8
                    color=(round(35+shade*35)+tile,round(94+shade*75)+tile,round(42+shade*32)+tile)
            out.putpixel((x,y),(*color,a))
    return out


def capes(game,style):
    env,ts=load(game,SPRITES_BUNDLE)
    images={}
    for t in ts:
        if int(t.m_TextureFormat) not in (3,4):continue
        layer=layer_of(t.m_Name)
        if layer=='cape':
            images[t.m_Name]=cape_image(t.image.convert('RGBA'),style)
        elif layer=='body':
            im=t.image.convert('RGBA');face=find_face(im);bb=im.getbbox()
            if not face or not bb:continue
            shoulder=face['y0']+5
            out=im.copy()
            # The standing coat includes cape/faldón pixels in the body sprite.
            # Recolor the trailing fabric, retaining torso, face, arms and legs.
            cloth={(x,y) for y in range(max(0,shoulder),bb[3]) for x in range(bb[0],bb[2])
                   if im.getpixel((x,y))[3] and classify(*im.getpixel((x,y))[:3]) in ('coat','lining','scarf')}
            if not cloth:continue
            region=(min(x for x,y in cloth),min(y for x,y in cloth),max(x for x,y in cloth)+1,max(y for x,y in cloth)+1)
            pattern=cape_image(im,style,region)
            for p in cloth:out.putpixel(p,pattern.getpixel(p))
            images[t.m_Name]=out
    slug='capa-blanca' if style=='white' else 'capa-creeper'
    count=write_images(env,ts,images,ROOT/'mods'/slug/'files'/SPRITES_BUNDLE)
    title='Capa blanca' if style=='white' else 'Capa Creeper · Estilo Minecraft'
    desc='Capa y faldones del héroe en blanco, conservando sombras y animación.' if style=='white' else 'Capa verde con un rostro de creeper en pixel art, integrada en los frames animados del héroe.'
    package(slug,title,desc,'Campaña principal. Reemplaza snb.bundle: elegí una sola capa o skin del héroe a la vez. Conserva el gancho, los sonidos y las habilidades originales.',SPRITES_BUNDLE,images,'Spr_SNB_Idle (lp) (1)')
    print(slug,count,'textures')


def group(frames,prefix):
    groups=defaultdict(list)
    for name,im in frames.items():
        anim,index=frame_key(name)
        anim=anim[len(prefix):].strip()
        groups[anim].append((index,name))
    for ns in groups.values():ns.sort()
    return groups


def base_to_dlc(anim):
    lower=anim.lower()
    if 'runstart' in lower:return 'RunStart'
    if 'runstop' in lower:return 'RunStop'
    if 'land2run' in lower:return 'Land2Run'
    if 'running' in lower or 'walk' in lower:return 'RunLoop'
    if 'fallstart' in lower or 'fallingstart' in lower:return 'FallStart'
    if 'fall' in lower:return 'FallLoop'
    if 'wallclimbup' in lower:return 'WallClimpUpLoop'
    if 'wallclimbdown' in lower:return 'WallClimbDownLoop'
    if 'wall' in lower:return 'WallSlideLoop'
    if 'land' in lower:return 'Land'
    if 'damaged' in lower or 'death' in lower:return 'Damaged'
    if 'swing' in lower or 'jump' in lower or 'rope' in lower or 'skyline' in lower:return 'Jumping'
    if 'dash' in lower or 'rolling' in lower or 'reversal' in lower:return 'AirJump'
    if 'attack' in lower or 'shot' in lower or 'exc' in lower:return 'MeleeAttack1st'
    if 'idlerand1' in lower:return 'RandomIdle1Loop'
    if 'idlerand2' in lower:return 'RandomIdle2Loop'
    return 'IdleLoop'


def dlc_to_base(anim):
    lower=anim.lower()
    if 'runstart' in lower:return 'RunStart'
    if 'runstop' in lower:return 'RunStop'
    if 'land2run' in lower:return 'Land2Run'
    if 'run' in lower:return 'Running'
    if 'fallstart' in lower:return 'FallStart'
    if 'fall' in lower:return 'Falling'
    if 'wallclimpup' in lower:return 'WallClimbUp'
    if 'wallclimbdown' in lower:return 'WallClimbDown'
    if 'wall' in lower:return 'WallSliding'
    if 'land' in lower:return 'Landing'
    if 'damaged' in lower:return 'Damaged'
    if 'jump' in lower:return 'Jumping'
    if 'air' in lower:return 'ShotAIRUp' if '80' in lower or '60' in lower else 'ShotAIRFront'
    if 'rangeattack' in lower:return 'ShotGNDDown' if 'Minus' in anim else 'ShotGNDUp' if '80' in anim or '60' in anim else 'ShotGNDFront'
    if 'melee' in lower or 'burst' in lower:return 'ChargeAttack'
    if 'randomidle1' in lower:return 'IdleRand1'
    if 'randomidle2' in lower:return 'IdleRand2'
    return 'Idle'


def align(src,target):
    """Center at the same feet/ground baseline. Never resize one frame by its bbox."""
    a,b=src.getbbox(),target.getbbox()
    out=Image.new('RGBA',target.size)
    if not a or not b:return out
    # The campaigns use different native character heights. Apply a fixed scale
    # ratio selected for the whole character; cropping per frame causes jitter.
    # Both characters use 20 pixels per unit. DLC textures have a larger padded
    # canvas (256 vs 80), not a larger actor: keep native pixels and clip padding.
    dx=round((b[0]+b[2]-a[0]-a[2])/2)
    dy=b[3]-a[3]
    out.alpha_composite(src,(dx,dy))
    return out


def swaps(game,to_dlc):
    target_rel=DLC if to_dlc else SPRITES_BUNDLE
    source_rel=SPRITES_BUNDLE if to_dlc else DLC
    env,ts=load(game,target_rel)
    source_env,source_ts=load(game,source_rel)
    def actor(t,prefix):
        return t.m_Name.startswith(prefix) and not t.m_Name.endswith(('_N','_M','_A','_NORMALS')) and '_Bullet' not in t.m_Name and '_White' not in t.m_Name
    sp='Spr_SNB_' if to_dlc else 'Spr_SPC_'
    tp='Spr_SPC_' if to_dlc else 'Spr_SNB_'
    source={t.m_Name:t.image.convert('RGBA') for t in source_ts if actor(t,sp) and int(t.m_TextureFormat) in (3,4)}
    if to_dlc:
        # The original hero's arm is a separate animated sprite. Include it in
        # each whole-body DLC frame, or the replacement would lose his left arm.
        arm_groups=group({t.m_Name:t.image.convert('RGBA') for t in source_ts
                          if layer_of(t.m_Name)=='arm' and int(t.m_TextureFormat) in (3,4)},'Spr_SNBArm_')
        arm_images={t.m_Name:t.image.convert('RGBA') for t in source_ts if layer_of(t.m_Name)=='arm' and int(t.m_TextureFormat) in (3,4)}
        for n,im in source.items():
            anim,index=frame_key(n)
            anim=anim[len(sp):].strip()
            candidates=arm_groups.get(anim)
            if not candidates and anim.startswith('Shot'):candidates=arm_groups.get('Shoot')
            if candidates:
                _,arm_name=min(candidates,key=lambda item:abs(item[0]-index))
                im.alpha_composite(arm_images[arm_name])
    target={t.m_Name:t.image.convert('RGBA') for t in ts if actor(t,tp) and int(t.m_TextureFormat) in (3,4)}
    sg,tg=group(source,sp),group(target,tp)
    images={};mapping=[]
    converter=dlc_to_base if to_dlc else base_to_dlc
    for anim,items in tg.items():
        match=converter(anim)
        assert match in sg,(anim,match)
        candidates=sg[match]
        for pos,(idx,name) in enumerate(items):
            j=round(pos*(len(candidates)-1)/max(1,len(items)-1))
            sn=candidates[j][1]
            replacement=source[sn]
            if not to_dlc:
                # A single 20% increase for the complete animation set. The
                # previous native-size DLC figure looked too small in campaign.
                replacement=replacement.resize((round(replacement.width*1.20),round(replacement.height*1.20)),Image.Resampling.NEAREST)
            images[name]=align(replacement,target[name])
            mapping.append(dict(target=name,source=sn,group=match))
    if not to_dlc:
        for t in ts:
            if layer_of(t.m_Name) in ('arm','cape') and int(t.m_TextureFormat) in (3,4):
                images[t.m_Name]=Image.new('RGBA',(t.m_Width,t.m_Height))
    slug='heroe-en-dlc' if to_dlc else 'major-son-en-campana'
    count=write_images(env,ts,images,ROOT/'mods'/slug/'files'/target_rel)
    folder=ROOT/'mods'/slug
    (folder/'animation-map.json').write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    name='El héroe en el DLC' if to_dlc else 'Major Son en la campaña principal'
    desc='El protagonista original reemplaza visualmente a Major Son en el DLC.' if to_dlc else 'Major Son reemplaza visualmente al protagonista en la campaña principal.'
    notes=('DLC: conserva los ataques y la jugabilidad de Major Son. ' if to_dlc else 'Campaña principal: conserva el gancho y la jugabilidad del héroe. ')
    notes+='Intercambio de apariencia, no del controlador. Las animaciones se adaptan entre campañas; las poses sin equivalente usan una pose cercana o de reposo. Las escenas con sprites propios no se sustituyen. Prueba de movimiento dentro del juego pendiente. '
    notes+=('Puede combinarse con las capas de la campaña principal.' if to_dlc else 'Conflicta con capas y otras skins del héroe: reemplaza el mismo bundle.')
    preview='Spr_SPC_IdleLoop (1)' if to_dlc else 'Spr_SNB_Idle (lp) (1)'
    if preview not in images:preview=next(n for n in images if 'Idle' in n)
    package(slug,name,desc,notes,target_rel,images,preview)
    print(slug,count,'textures;',len(mapping),'actor frames')


if __name__=='__main__':
    game=Path(sys.argv[1])
    capes(game,'white');capes(game,'creeper')
    swaps(game,False);swaps(game,True)
