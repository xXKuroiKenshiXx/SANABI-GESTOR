"""Targeted existing-mod updates: 2099 cape, Major size and Creeper damage.

Never rebuilds or modifies Spider Noir. Preserves existing Spider-Man sounds.
"""
import json, shutil, subprocess, sys, wave
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
import UnityPy
from build import SPRITES_BUNDLE,SOUND_BUNDLE,original_path,replace_resource,build_fsb,layer_of,build_preview
from extra_mods import load,write_images,swaps
from recolor import find_face
from pack_mods import pack

ROOT=Path(__file__).resolve().parents[3]

def metadata(slug,version,description=None,notes=None):
    folder=ROOT/'mods'/slug
    m=json.loads((folder/'mod.json').read_text(encoding='utf-8'));m['version']=version
    if description:m['description']=description
    if notes:m['compatibilityNotes']=notes
    (folder/'mod.json').write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    pack(folder)

def cape(game):
    folder=ROOT/'mods/spider-2099'
    env=UnityPy.load(str(folder/'files'/SPRITES_BUNDLE));ts=[o.read() for o in env.objects if o.type.name=='Texture2D']
    _,originals=load(game,SPRITES_BUNDLE)
    faces={t.m_Name:find_face(t.image.convert('RGBA')) for t in originals if layer_of(t.m_Name)=='body' and int(t.m_TextureFormat) in (3,4)}
    images={}
    for t in ts:
        if layer_of(t.m_Name)!='body' or int(t.m_TextureFormat) not in (3,4):continue
        face=faces.get(t.m_Name);body=t.image.convert('RGBA');bb=body.getbbox()
        if not bb:continue
        canvas=Image.new('RGBA',body.size);d=ImageDraw.Draw(canvas)
        sx=round(face['cx']) if face else round((bb[0]+bb[2])/2)
        sy=face['y0']+6 if face else bb[1]+6
        end=min(body.height-2,max(sy+17,bb[3]-3));tip=max(1,sx-13)
        d.polygon([(sx-2,sy),(sx+3,sy+2),(sx,end-2),(tip+5,end),(tip+2,end-3),(tip,end),(max(1,sx-10),sy+6)],fill=(203,27,49,255),outline=(69,16,35,255))
        d.line([(sx-2,sy+2),(sx-7,end-3)],fill=(255,64,76,255),width=2)
        canvas.alpha_composite(body);images[t.m_Name]=canvas
    write_images(env,ts,images,folder/'files'/SPRITES_BUNDLE)
    build_preview(game,folder/'preview.png')
    metadata('spider-2099','1.2.0',description='Spider-Man 2099 con capa roja en todas las poses del cuerpo, incluido reposo. Conserva el diseño de esta skin y los sonidos de telaraña. La variante Modelo completo usa figuras y animaciones distintas.')
    with (folder/'README.md').open('a',encoding='utf-8') as f:f.write('\n## Capa permanente (1.2.0)\n\nLa capa roja está integrada en todos los cuadros del cuerpo, incluido Idle, además de las capas de balanceo.\n')
    pack(folder)

def creeper(game):
    folder=ROOT/'mods/capa-creeper';source=folder/'fuente/creeper.mp3';source.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(ROOT/'creeper.mp3',source)
    env=UnityPy.load(str(original_path(game,SOUND_BUNDLE)));bundle=env.file
    key=next(k for k in bundle.files if k.endswith('.resource'));resource=bytearray(bundle.files[key].bytes)
    clips=[o for o in env.objects if o.type.name=='AudioClip' and o.read().m_Name=='SFX_SNB_Damaged']
    assert len(clips)==1
    obj=clips[0];tree=obj.read_typetree();ref=tree['m_Resource']
    original=bytes(resource[ref['m_Offset']:ref['m_Offset']+ref['m_Size']]);rate=tree['m_Frequency'];channels=tree['m_Channels']
    ffmpeg=shutil.which('ffmpeg')
    if not ffmpeg:raise RuntimeError('Se necesita ffmpeg para importar el MP3 del usuario.')
    pcm_bytes=subprocess.run([ffmpeg,'-v','error','-i',str(source),'-f','s16le','-ar',str(rate),'-ac',str(channels),'pipe:1'],check=True,capture_output=True).stdout
    pcm=np.frombuffer(pcm_bytes,dtype='<i2').reshape(-1,channels)
    # Damage is Vorbis; Unity's shoot clip already supplies a PCM16 FSB header
    # at the same rate/channels. Do not retain Vorbis metadata for PCM data.
    template=next(o.read_typetree() for o in env.objects if o.type.name=='AudioClip' and o.read().m_Name=='SFX_SNB_Shoot')
    tr=template['m_Resource'];assert (template['m_Frequency'],template['m_Channels'])==(rate,channels)
    pcm_header=bytes(resource[tr['m_Offset']:tr['m_Offset']+tr['m_Size']])
    fsb=build_fsb(pcm_header,pcm,channels)
    resource+=b'\x00'*((-len(resource))%32);ref['m_Offset']=len(resource);ref['m_Size']=len(fsb);resource+=fsb
    tree['m_Length']=len(pcm)/rate;tree['m_CompressionFormat']=0;obj.save_typetree(tree);replace_resource(bundle,key,resource)
    dst=folder/'files'/SOUND_BUNDLE;dst.parent.mkdir(parents=True,exist_ok=True);dst.write_bytes(bundle.save(packer='lz4'))
    with wave.open(str(folder/'fuente/daño-creeper.wav'),'wb') as wav:
        wav.setnchannels(channels);wav.setsampwidth(2);wav.setframerate(rate);wav.writeframes(pcm_bytes)
    description='Capa verde con rostro Creeper; reproduce el creeper.mp3 del usuario cuando el héroe recibe daño. Conserva los sprites de la capa.'
    notes='SANABI 1.3.53, campaña principal. Reemplaza snb.bundle y el bundle de audio del jugador; puede entrar en conflicto con skins o sonidos del protagonista. Solo sustituye SFX_SNB_Damaged; los otros sonidos se conservan. Prueba dentro del juego pendiente.'
    metadata('capa-creeper','1.1.0',description,notes)
    with (folder/'README.md').open('a',encoding='utf-8') as f:f.write('\n## Sonido al recibir daño (1.1.0)\n\nImporta `fuente/creeper.mp3` al AudioClip `SFX_SNB_Damaged`; no reemplaza salto, gancho ni otros sonidos.\n')
    pack(folder)

if __name__=='__main__':
    game=Path(sys.argv[1]);cape(game)
    swaps(game,False)
    metadata('major-son-en-campana','1.1.0',description='Major Son reemplaza al protagonista de la campaña con una figura 20% más grande que la edición anterior, manteniendo escala uniforme entre todas las poses.')
    creeper(game)
