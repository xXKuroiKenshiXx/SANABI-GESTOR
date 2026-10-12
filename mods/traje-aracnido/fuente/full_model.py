"""Build independent whole-body Spider-Man animation replacements.

Uses frames imported from the user's reference atlas, rather than recoloring
the SANABI silhouette. Original sprite references, animation timing, pivots and
physics remain compatible with the game. Mechanical arm/cape layers are hidden.
"""
import json, math, re, shutil, sys, hashlib
from pathlib import Path
from collections import defaultdict
from PIL import Image, ImageDraw
import UnityPy
from build import SPRITES_BUNDLE, SOUND_BUNDLE, WEB_TEXTURES, web_image, layer_of, texture_bytes, replace_resource, rectangularize_sprites, rim_mask, flat_normal
from extra_mods import load, write_images, INSTALL
from pack_mods import pack

ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'mods/spiderman-modelo-completo/fuente'
SEQUENCES={
    'idle':[9,10,11,12,13,14,15,16],
    'run':[56,57,58,59,60,61,62],
    'jump':[80,81,82,83,84,77,78,79,85,86,87,88],
    'fall':[87,88,90,91],
    'swing':[138,139,144,145,146,143,142,140,149,150,151,152,153,154,155,156],
    'wall':[103,104,105,106,107,108,109,110,111,112,113],
    'ceiling':[103,104,105,106,107,108,109,110,111,112,113],
    'shoot':[0,1,2,3,4,5,6,7],
    'shootup':[37,38,39,40],
    'crouch':[28,29,30,31,32,33,34],
    'roll':[116,117,118,119,120],
    'attack':[41,42,43,44,45,46,47,48,49,50,51,52,53,54,55],
    'damage':[121,122,123,124,125,126],
    'death':[125,126,127,128,129],
    'land':[100,101,102,81],
}

def write_model(env,textures,images,path):
    """Keep native texture dimensions, sprite rectangles and pixels per unit.

    The game's renderer and animation references expect the original padded
    canvases. Fit the replacement pixels inside them rather than scaling assets.
    """
    bundle=env.file
    resources={k:bytearray(v.bytes) for k,v in bundle.files.items() if k.endswith('.resS')}
    cache={}
    for t in textures:
        match=re.match(r'^(.*)_([MN])$',t.m_Name)
        if match and match[1] in images and int(t.m_TextureFormat) in (1,3,4):
            base=images[match[1]];key=(match[2],base.size,hashlib.sha256(base.tobytes()).digest())
            if key not in cache:cache[key]=rim_mask(base) if match[2]=='M' else flat_normal(base)
            images[t.m_Name]=cache[key]
    changed=set();written=0
    for t in textures:
        im=images.get(t.m_Name)
        if im is None:continue
        tree=t.object_reader.read_typetree();raw=texture_bytes(im,int(t.m_TextureFormat))
        assert im.size==(t.m_Width,t.m_Height), ('Native sprite canvas required',t.m_Name)
        tree['m_Width'],tree['m_Height']=im.size
        tree['m_CompleteImageSize']=len(raw)
        stream=tree['m_StreamData']
        if stream['path']:
            key=stream['path'].split('/')[-1];res=resources[key]
            res+=b'\x00'*((-len(res))%16);stream['offset']=len(res);stream['size']=len(raw);res+=raw
            tree['image data']=b''
        else:tree['image data']=raw
        t.object_reader.save_typetree(tree);changed.add(t.object_reader.path_id);written+=1
    rectangularize_sprites(env,changed)
    for key,res in resources.items():replace_resource(bundle,key,res)
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(bundle.save(packer='lz4'))
    return written

def sequence(anim):
    a=anim.lower()
    if 'death' in a:return 'death'
    if 'damag' in a or 'white' in a:return 'damage'
    if 'walljump' in a or 'wallleap' in a or 'ceiling' in a and 'jump' in a:return 'jump'
    if 'ceiling' in a:return 'ceiling'
    if 'wall' in a:return 'wall'
    if any(s in a for s in ('swing','rope','rappel','skyline','winding')):return 'swing'
    if 'rolling' in a or 'roll' in a:return 'roll'
    if 'crouch' in a:return 'crouch'
    if 'run' in a or 'walk' in a:return 'run'
    if 'fall' in a:return 'fall'
    if 'land' in a:return 'land'
    if 'jump' in a:return 'jump'
    if 'shot' in a or 'shoot' in a:return 'shootup' if 'up' in a else 'shoot'
    if any(s in a for s in ('attack','dash','exc','reversal')):return 'attack'
    return 'idle'

def imported(frame):
    im=Image.open(SOURCE/'cuadros'/f'{frame:03}.png').convert('RGBA')
    # Some reference frames include a thin white web above the raised hand.
    # Remove that external line: SANABI renders its own dynamic rope endpoint.
    body=[(x,y) for y in range(im.height) for x in range(im.width)
          if (lambda p:p[3] and (p[0]>p[1]*1.25 and p[0]>60 or p[2]>p[0]*1.7 and p[1]<90))(im.getpixel((x,y)))]
    if body:
        box=(min(x for x,y in body),min(y for x,y in body),max(x for x,y in body)+1,max(y for x,y in body)+1)
        im=im.crop(box)
    return im

def miguel(im):
    """2099 palette/material design on the imported anatomy, not SANABI."""
    im=im.copy();px=im.load()
    for y in range(im.height):
        for x in range(im.width):
            r,g,b,a=px[x,y]
            if not a:continue
            if r>g*1.2 and r>50:
                v=r/255;px[x,y]=(round(11+v*19),round(17+v*32),round(42+v*100),a)
                # Red angular suit accents and chest emblem, blue mask/limbs.
                u=x/max(1,im.width);v=y/max(1,im.height)
                if .30<v<.48 and abs(u-.52)<(.12+(v-.30)*.8):px[x,y]=(190,27,56,a)
            elif max(r,g,b)-min(r,g,b)<45 and r>150:
                px[x,y]=(244,56,76,a) # Miguel's red mask lenses
    return im

def render(frame, size, state, phase, variant):
    src=imported(frame)
    # The reference climbs toward the opposite wall to SANABI's unflipped
    # wall poses. Correct the source once; the game still mirrors both sides.
    if state=='wall':src=src.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    # Clockwise keeps the head facing right and contact points above the body.
    if state=='ceiling':src=src.transpose(Image.Transpose.ROTATE_270)
    if variant=='2099':src=miguel(src)
    # One scale for the complete character and all animations; no per-frame
    # stretching or fitting to the old coat silhouette.
    resolution=1 # native PPU 20 and 80/96/128 canvases, ~38px standing height
    src=src.resize((max(1,round(src.width*.54*resolution)),max(1,round(src.height*.54*resolution))),Image.Resampling.NEAREST)
    canvas=Image.new('RGBA',size)
    cx=round(size[0]*.5);ground=round(size[1]*.5+23*resolution)
    airborne=state in ('swing','fall','jump','wall','ceiling','roll')
    dx=cx-src.width//2;dy=round(size[1]*.5+4*resolution)-src.height//2 if airborne else ground-src.height
    if variant=='2099':
        d=ImageDraw.Draw(canvas)
        # Cape belongs to EVERY body frame, including idle and damage.
        shoulder=(dx+round(src.width*.42),dy+round(src.height*(.35 if airborne else .24)))
        sx,sy=shoulder;s=round(resolution);flutter=round(math.sin(phase*math.tau)*2*s)
        length=max(18*s,round(src.height*.68))
        tip=(max(2,sx-12*s-flutter),min(size[1]-3,sy+length))
        d.polygon([(sx-2*s,sy),(sx+4*s,sy+2*s),(sx+s,sy+length-3*s),
                   (tip[0]+7*s,tip[1]-2*s),(tip[0]+4*s,tip[1]+s),tip,(max(1,sx-14*s-flutter),sy+9*s)],fill=(177,22,52,255),outline=(55,12,34,255))
        d.polygon([(sx-s,sy+2*s),(sx+s,sy+length-5*s),(tip[0]+4*s,tip[1]-2*s),(sx-7*s,sy+9*s)],fill=(233,44,65,255))
        d.line([(sx-2*s,sy+3*s),(sx-7*s,sy+length-5*s)],fill=(124,14,43,255),width=s)
    canvas.alpha_composite(src,(dx,dy))
    return canvas

def build(game,variant):
    env,ts=load(game,SPRITES_BUNDLE)
    groups=defaultdict(list);images={}
    for t in ts:
        n=t.m_Name
        if int(t.m_TextureFormat) not in (3,4) or n.endswith(('_N','_M','_A','_NORMALS')):continue
        if n.startswith('Spr_SNB_'):
            anim=re.sub(r'\s*\(\d+\)$','',n[len('Spr_SNB_'):])
            idx=re.search(r'\((\d+)\)$',n);groups[anim].append((int(idx[1]) if idx else 0,t))
        elif layer_of(n) in ('arm','cape','grab'):
            images[n]=Image.new('RGBA',(t.m_Width,t.m_Height))
        elif n in WEB_TEXTURES:images[n]=web_image(n,t.m_Width,t.m_Height)
    mapping=[]
    for anim,items in groups.items():
        items.sort(key=lambda i:i[0]);state=sequence(anim);seq=SEQUENCES[state]
        if state in ('wall','ceiling') and not any(s in anim.lower() for s in ('climb','moving')):seq=[103]
        if state=='wall' and 'down' in anim.lower():seq=list(reversed(seq))
        looping=state in ('idle','run','wall','swing')
        for pos,(index,t) in enumerate(items):
            phase=pos/max(1,len(items)) if looping else pos/max(1,len(items)-1)
            j=min(len(seq)-1,int(phase*len(seq)) if looping else round(phase*(len(seq)-1)))
            frame=seq[j];im=render(frame,(t.m_Width,t.m_Height),state,phase,variant)
            if 'White' in anim:
                white=Image.new('RGBA',im.size,'white');white.putalpha(im.getchannel('A'));im=white
            images[t.m_Name]=im
            mapping.append(dict(target=t.m_Name,animation=state,sourceFrame=frame,phase=phase,mirrored=state=='wall',rotation=270 if state=='ceiling' else 0))
    slug='spiderman-modelo-completo' if variant=='classic' else 'spider-2099-modelo-completo'
    folder=ROOT/'mods'/slug
    count=write_model(env,ts,images,folder/'files'/SPRITES_BUNDLE)
    (folder/'files'/SOUND_BUNDLE).parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(ROOT/'mods/traje-aracnido/files'/SOUND_BUNDLE,folder/'files'/SOUND_BUNDLE)
    name='Spider-Man · Modelo completo' if variant=='classic' else 'Spider-Man 2099 · Modelo completo'
    description=('Figura completa de Spider-Man con sprites y secuencias del atlas de referencia: reposo, carrera, salto, balanceo, pared, disparo y daño. Sin capa ni brazo mecánico.' if variant=='classic' else
                 'Figura completa de Spider-Man 2099 con silueta y poses independientes del protagonista, traje azul y rojo y capa roja integrada en todos los cuadros, incluido reposo.')
    notes='SANABI 1.3.53, campaña principal. Sustituye el cuerpo completo y las secuencias visuales; conserva los controladores, colisiones y tiempos del juego. El gancho funciona como telaraña y no muestra el brazo mecánico. Los movimientos narrativos sin equivalente usan reposo. Conflicta con otras skins/capas del protagonista. Prueba de movimiento dentro del juego pendiente.'
    manifest=dict(id=slug,name=name,version='1.2.0',author='Kuro Kenshi · atlas de referencia del usuario',description=description,
                  category='skins',tags=['modelo completo','animaciones','spider-man'],gameVersion='1.3.53',preview='preview.png',
                  installInstructions=INSTALL,compatibilityNotes=notes,verification='package')
    (folder/'mod.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (folder/'animation-map.json').write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (folder/'README.md').write_text(f'# {name}\n\n{description}\n\n## Instalación\n\n{INSTALL}\n\n## Animaciones\n\n'
        'Importa cuadros de figura completa del atlas que adjuntaste. No repinta el abrigo o el brazo de SANABI. '
        'Cada grupo usa una secuencia propia; `animation-map.json` identifica los cuadros. '
        'SANABI usa sprites 2D: se reemplazan texturas, mallas de render, máscaras y normales, conservando referencias y pivotes. '
        'Versión 1.2: conserva lienzos de 80/96/128 píxeles y 20 píxeles por unidad, corrige la orientación de pared, invierte la secuencia al bajar, separa poses de techo y usa salto real al salir de la pared. '
        'Los controladores y tiempos originales enlazan las secuencias nuevas; no se sustituyen habilidades ni físicas.\n\n'
        f'## Compatibilidad\n\n{notes}\n\n## Referencia\n\n'
        'Hoja proporcionada por el usuario: Spider-Man / Spider-Man: Lethal Foes, ripeada por Grim y Xaden, '
        'con sección de balanceo de Xaden. Créditos conservados en la imagen original dentro de fuente/. '
        'La versión 2099 adapta la figura importada e integra una capa animada en cada cuadro.\n',encoding='utf-8')
    canvas=Image.new('RGBA',(280,160),(17,12,32));d=ImageDraw.Draw(canvas)
    for i in range(0,280,22):d.rectangle((i,100+(i%43),i+15,160),fill=(27,24,49))
    for x,frame,state in ((65,13,'idle'),(195,144,'swing')):
        im=render(frame,(80,80),state,.25,variant).resize((160,160),Image.Resampling.NEAREST)
        canvas.alpha_composite(im,(x-80,0))
    canvas.resize((840,480),Image.Resampling.NEAREST).convert('RGB').save(folder/'preview.png')
    # Export real frames, including swinging, for visual review.
    sheet=Image.new('RGBA',(1440,640),(17,12,32));d=ImageDraw.Draw(sheet)
    for row,state in enumerate(('idle','run','swing','wall')):
        for col in range(9):
            seq=SEQUENCES[state];frame=seq[round(col*(len(seq)-1)/8)]
            sheet.alpha_composite(render(frame,(80,80),state,col/9,variant).resize((160,160),Image.Resampling.NEAREST),(col*160,row*160))
    sheet.save(folder/'animaciones.png')
    pack(folder)
    print(slug,count,'textures',len(mapping),'full actor frames')

if __name__=='__main__':
    game=Path(sys.argv[1]);build(game,'classic');build(game,'2099')
