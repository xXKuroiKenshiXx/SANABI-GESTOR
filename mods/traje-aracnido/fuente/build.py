"""Genera los archivos del mod "Traje aracnido" a partir de tu copia de SANABI.

Uso (necesita Python 3.10+ y `pip install UnityPy pillow numpy`):
    python build.py "C:/Program Files (x86)/Steam/steamapps/common/SANABI" ../files

Que hace:
  * Repinta los sprites del protagonista (cuerpo, brazo, capa y garra) con un traje rojo y azul.
  * Cambia la cadena del gancho por un hilo de telarana.
  * Reemplaza los sonidos metalicos del gancho por sonidos de telarana sintetizados.

Si el juego se actualiza, volve a correr el script con la version nueva para regenerar el mod.
"""
import re
import struct
import sys
import json
from pathlib import Path

import UnityPy
from PIL import Image
from UnityPy.streams import EndianBinaryWriter

sys.path.insert(0, str(Path(__file__).parent))
from recolor import faces_for, recolor_image, set_variant, frame_key  # noqa: E402
import sonidos  # noqa: E402

AA = Path("SNB_Data/StreamingAssets/aa/StandaloneWindows64")
SPRITES_BUNDLE = AA / "player_assets_assets/_resource/character/snb.bundle"
SOUND_BUNDLE = AA / "sfx_assets_assets/_sound/_sound/new_sfx/player.bundle"


def original_path(game, relative):
    """Read a manager backup when that asset is currently owned by an installed mod."""
    state_path = game / 'SanabiGestor/state.json'
    if state_path.is_file():
        state = json.loads(state_path.read_text(encoding='utf-8'))
        record = state.get('files', {}).get(relative.as_posix())
        if record and record.get('originalExisted'):
            backup = game / 'SanabiGestor/backup' / relative
            if not backup.is_file():
                raise FileNotFoundError(f'Falta el respaldo original: {backup}. Desactiva el mod antes de regenerar.')
            return backup
    return game / relative

LAYERS = {
    "body": re.compile(r"^Spr_SNB_"),
    "arm": re.compile(r"^Spr_SNBArm_"),
    "cape": re.compile(r"^(Sannabi_Cape|Spr_SNBCape)"),
    "grab": re.compile(r"^(Spr_SNBGrab|Grab_|Spr_SNBGrabLower|Lower_Grab)"),
}
SKIP = re.compile(r"(_N|_M|_A|_NORMALS)$")
MASK = re.compile(r"^(.*)_M$")
NORMAL = re.compile(r"^(.*)_N$")

SOUNDS = {
    "SFX_SNB_Shoot": sonidos.thwip,
    "SFX_SNB_Return": sonidos.zip_back,
    "SFX_SNB_ReturnWtihClang": sonidos.zip_back,
    "SFX_Grab_SNB_Metal": sonidos.splat,
    "SFX_Grab_SNB_Concrete": sonidos.splat,
    "SFX_Grab_SNB_Wood": sonidos.splat,
    "SFX_Grab_SNB_Gravel": sonidos.splat,
    "SFX_Grab_SNB_Pullable": sonidos.splat,
}

W = (238, 241, 250)
WEB_TEXTURES = {
    # 3x6 con transparencia: un hilo de 1 pixel con un nudo cada tanto
    "Chain01": [
        [(0, 0, 0, 0), (*W, 255), (0, 0, 0, 0)],
        [(0, 0, 0, 0), (210, 214, 226, 255), (0, 0, 0, 0)],
        [(200, 204, 216, 150), (255, 255, 255, 255), (200, 204, 216, 150)],
        [(0, 0, 0, 0), (210, 214, 226, 255), (0, 0, 0, 0)],
        [(0, 0, 0, 0), (*W, 255), (0, 0, 0, 0)],
        [(0, 0, 0, 0), (225, 228, 238, 255), (0, 0, 0, 0)],
    ],
    "Chain02": [[(230, 233, 242)], [(255, 255, 255)], [(214, 218, 230)], [(255, 255, 255)], [(230, 233, 242)]],
}


def layer_of(name):
    if SKIP.search(name):
        return None
    # Flashes y efectos no son el cuerpo; conservar su transparencia y aspecto.
    if re.match(r'^Spr_SNB_(White|GChangeFlash|HandChange)', name):
        return None
    for layer, rx in LAYERS.items():
        if rx.search(name):
            return layer
    return None


def resource_name(path):
    return path.split("/")[-1]


def replace_resource(bundle, name, data):
    old = bundle.files[name]
    writer = EndianBinaryWriter(bytes(data))
    writer.flags = getattr(old, "flags", 0)
    bundle.files[name] = writer
    bundle.mark_changed()


def texture_bytes(img, fmt):
    img = img.transpose(Image.FLIP_TOP_BOTTOM)
    if fmt == 4:  # RGBA32
        return img.convert("RGBA").tobytes()
    if fmt == 3:  # RGB24
        return img.convert("RGB").tobytes()
    if fmt == 1:  # Alpha8
        return img.convert("RGBA").getchannel("A").tobytes()
    raise ValueError(f"formato {fmt} no soportado")


def rectangularize_sprites(env, changed_ids):
    """Replace tight rendering polygons, preserving pivots, units and physics.

    Textures alone cannot expand beyond the original sprite mesh. A full quad
    renders the new silhouette through its alpha without changing game colliders.
    """
    count=0
    for obj in env.objects:
        if obj.type.name!='Sprite':continue
        tree=obj.read_typetree()
        rd=tree['m_RD']
        if rd['texture']['m_FileID']!=0 or rd['texture']['m_PathID'] not in changed_ids:continue
        if tree.get('m_Bones'):raise ValueError('Skinned sprite unsupported: '+tree['m_Name'])
        rect,pivot=tree['m_Rect'],tree['m_Pivot']
        ppu=tree['m_PixelsToUnits']
        w,h=rect['width'],rect['height']
        left,bottom=-pivot['x']*w/ppu,-pivot['y']*h/ppu
        right,top=left+w/ppu,bottom+h/ppu
        vertices=[(left,bottom,0),(right,bottom,0),(right,top,0),(left,top,0)]
        positions=b''.join(struct.pack('<3f',*p) for p in vertices)
        uv=b''.join(struct.pack('<6f4I',u,v,0,0,0,0,0,0,0,0) for u,v in ((0,0),(1,0),(1,1),(0,1)))
        channels=rd['m_VertexData']['m_Channels']
        # Retain the established position/UV/bone layout of these unskinned sprites.
        assert channels[0]['dimension']==3 and channels[4]['dimension']==2
        assert channels[0]['stream']==0 and channels[4]['stream']==1
        rd['m_VertexData']['m_VertexCount']=4
        rd['m_VertexData']['m_DataSize']=positions+uv
        rd['m_IndexBuffer']=list(struct.pack('<6H',0,1,2,0,2,3))
        sub=rd['m_SubMeshes'][0]
        sub.update(firstByte=0,indexCount=6,baseVertex=0,firstVertex=0,vertexCount=4)
        sub['localAABB']={'m_Center':{'x':(left+right)/2,'y':(bottom+top)/2,'z':0},'m_Extent':{'x':w/ppu/2,'y':h/ppu/2,'z':0}}
        rd['m_SubMeshes']=[sub]
        rd['textureRect']={'x':rect['x'],'y':rect['y'],'width':w,'height':h}
        rd['textureRectOffset']={'x':0,'y':0}
        obj.save_typetree(tree)
        count+=1
    return count


def web_image(name, w, h):
    rows = WEB_TEXTURES[name]
    img = Image.new("RGBA", (w, h))
    for y in range(h):
        for x in range(w):
            p = rows[y % len(rows)][x % len(rows[0])]
            img.putpixel((x, y), p if len(p) == 4 else (*p, 255))
    return img


def rim_mask(img):
    """Mascara de borde (_M) del sprite: borde interior en 255 y un segundo anillo en 64, como el original."""
    w, h = img.size
    px = img.load()
    solid = lambda x, y: 0 <= x < w and 0 <= y < h and px[x, y][3] > 0  # noqa: E731
    n4 = ((1, 0), (-1, 0), (0, 1), (0, -1))
    e1 = {(x, y) for y in range(h) for x in range(w) if solid(x, y) and any(not solid(x + dx, y + dy) for dx, dy in n4)}
    out = Image.new("RGBA", (w, h))
    o = out.load()
    for y in range(h):
        for x in range(w):
            if (x, y) in e1:
                o[x, y] = (0, 0, 0, 255)
            elif solid(x, y) and any((x + dx, y + dy) in e1 for dx, dy in n4):
                o[x, y] = (0, 0, 0, 64)
    return out


def flat_normal(img):
    out=Image.new('RGBA',img.size,(128,128,255,0))
    out.putalpha(img.convert('RGBA').getchannel('A'))
    return out


def build_sprites(game, out):
    src = original_path(game, SPRITES_BUNDLE)
    env = UnityPy.load(str(src))
    bundle = env.file
    res_name = next(k for k in bundle.files if k.endswith(".resS"))
    res = bytearray(bundle.files[res_name].bytes)
    textures = [o.read() for o in env.objects if o.type.name == "Texture2D"]

    # primera pasada: ubicar la cara en todos los frames del cuerpo (los que no la muestran copian la del vecino)
    body = {t.m_Name: t.image.convert("RGBA") for t in textures
            if layer_of(t.m_Name) == "body" and int(t.m_TextureFormat) in (3, 4)}
    faces = faces_for(body)
    face_keys={frame_key(n):f for n,f in faces.items() if f}

    new_images = {}
    for tex in textures:
        name = tex.m_Name
        fmt = int(tex.m_TextureFormat)
        if name in WEB_TEXTURES:
            new_images[name] = (tex, web_image(name, tex.m_Width, tex.m_Height))
            continue
        layer = layer_of(name)
        if layer is None or fmt not in (3, 4):
            continue
        img = body.get(name) or tex.image
        arm_face=face_keys.get(frame_key(name.replace('Spr_SNBArm_','Spr_SNB_')))
        anchor=(arm_face['cx']+(-3 if arm_face['right'] else 3),arm_face['y0']+6) if arm_face else None
        new_images[name] = (tex, recolor_image(img, layer, faces.get(name),anchor))
    # las mascaras de borde se rehacen con la silueta nueva (si no, se veria el contorno del abrigo)
    for tex in textures:
        m = MASK.match(tex.m_Name)
        if m and m.group(1) in new_images and int(tex.m_TextureFormat) in (1, 4):
            new_images[tex.m_Name] = (tex, rim_mask(new_images[m.group(1)][1]))
        n = NORMAL.match(tex.m_Name)
        if n and n.group(1) in new_images and int(tex.m_TextureFormat) in (3,4):
            new_images[tex.m_Name] = (tex, flat_normal(new_images[n.group(1)][1]))

    changed = 0
    for tex in textures:
        name=tex.m_Name
        if name not in new_images:continue
        new_img=new_images[name][1]
        fmt = int(tex.m_TextureFormat)
        raw = texture_bytes(new_img, fmt)
        stream = tex.m_StreamData
        if stream and stream.path and stream.size:
            if resource_name(stream.path) != res_name or len(raw) != stream.size:
                print(f"  aviso: {name} no coincide en tamano, se omite")
                continue
            res[stream.offset:stream.offset + stream.size] = raw
        else:
            tex.image = new_img
            tex.save()
        changed += 1
    meshes=rectangularize_sprites(env,{tex.object_reader.path_id for tex in textures if tex.m_Name in new_images})
    replace_resource(bundle, res_name, res)
    dst = out / SPRITES_BUNDLE
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(bundle.save(packer="lz4"))
    print(f"Sprites: {changed} texturas y {meshes} mallas de render -> {dst}")


def build_fsb(original, pcm, channels):
    """Copia el encabezado FSB5 original y cambia solo la cantidad de muestras y los datos (PCM16)."""
    magic, version, count, sh_size, names_size, data_size, mode = struct.unpack_from("<4s6I", original, 0)
    if magic != b"FSB5" or mode != 2 or count != 1:
        raise ValueError("solo se reemplazan sonidos FSB5 PCM16 de una muestra")
    header = bytearray(original[:60 + sh_size + names_size])
    samples = len(pcm)
    raw = struct.unpack_from("<Q", header, 60)[0]
    raw = (raw & ((1 << 34) - 1)) | (samples << 34)
    struct.pack_into("<Q", header, 60, raw)
    data = pcm.reshape(-1, channels).tobytes()
    data += b"\x00" * ((-len(data)) % 32)
    struct.pack_into("<I", header, 20, len(data))
    return bytes(header) + data


def build_sounds(game, out):
    src = original_path(game, SOUND_BUNDLE)
    env = UnityPy.load(str(src))
    bundle = env.file
    res_name = next(k for k in bundle.files if k.endswith(".resource"))
    res = bytearray(bundle.files[res_name].bytes)
    changed = 0
    for obj in env.objects:
        if obj.type.name != "AudioClip":
            continue
        clip = obj.read()
        maker = SOUNDS.get(clip.m_Name)
        if maker is None:
            continue
        tree = obj.read_typetree()
        r = tree["m_Resource"]
        original = bytes(res[r["m_Offset"]:r["m_Offset"] + r["m_Size"]])
        rate = int(tree["m_Frequency"])
        channels = int(tree["m_Channels"])
        pcm = sonidos.to_pcm16(maker(), rate)
        if channels == 1:
            pcm = pcm.mean(axis=1).astype("<i2")
        fsb = build_fsb(original, pcm, channels)
        res += b"\x00" * ((-len(res)) % 32)
        r["m_Offset"] = len(res)
        r["m_Size"] = len(fsb)
        res += fsb
        tree["m_Length"] = len(pcm) / rate
        obj.save_typetree(tree)
        changed += 1
    replace_resource(bundle, res_name, res)
    dst = out / SOUND_BUNDLE
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(bundle.save(packer="lz4"))
    print(f"Sonidos: {changed} reemplazados -> {dst}")


def build_preview(game, path):
    # Inspect the exported assets, including the newly drawn arm, rather than
    # running a second conversion on an arbitrary uncombined frame.
    env = UnityPy.load(str(path.parent / 'files' / SPRITES_BUNDLE))
    frames = {}
    wanted = {'Spr_SNB_Idle (lp) (1)', 'Spr_SNBArm_Idle (1)', 'Spr_SNB_Swing (5)', 'Spr_SNBCape_SwingFront (4)', 'Spr_SNBArm_SwingJumpUp (1)'}
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            tex = obj.read()
            if tex.m_Name in wanted:
                frames[tex.m_Name] = tex.image.convert('RGBA')
    canvas = Image.new("RGBA", (240, 135), (13, 15, 20, 255))
    px = canvas.load()
    for y in range(135):
        for x in range(240):
            if y > 98 + (x * 7 % 13) - (x // 20 % 3) * 6:
                px[x, y] = (22, 26, 36, 255)
    for cx,names in ((57,('Spr_SNB_Idle (lp) (1)','Spr_SNBArm_Idle (1)')),
                     (168,('Spr_SNBCape_SwingFront (4)','Spr_SNB_Swing (5)'))):
        composed=Image.new('RGBA',(80,80))
        for name in names:
            if name in frames:composed.alpha_composite(frames[name])
        grip=None
        if cx==168 and 'Spr_SNBArm_SwingJumpUp (1)' in frames:
            # Preview pose only: rotate the exported forearm into a visible raised grip.
            # The line terminates in that glove, never at the body centre. No game sprites are edited.
            source=frames['Spr_SNBArm_SwingJumpUp (1)']
            arm=source.crop(source.getbbox()).rotate(68,Image.Resampling.NEAREST,expand=True)
            ax,ay=43,17
            composed.alpha_composite(arm,(ax,ay))
            coords=[(x,y) for y in range(arm.height) for x in range(arm.width) if arm.getpixel((x,y))[3]>128]
            top=min(y for x,y in coords)
            tip=[x for x,y in coords if y==top]
            grip=(ax+round(sum(tip)/len(tip)),ay+top)
            # Four visible knuckle pixels surround the rope endpoint with the exported glove palette.
            colors=[source.getpixel((x,y)) for y in range(source.height) for x in range(source.width) if source.getpixel((x,y))[3]>128]
            main=max(colors,key=lambda c:c[0]+c[1]+c[2])
            from PIL import ImageDraw
            d=ImageDraw.Draw(composed)
            gx,gy=grip;d.rectangle((gx-2,gy,gx+1,gy+3),fill=(12,16,26,255));d.rectangle((gx-1,gy,gx+1,gy+2),fill=main)
            d.point((gx+1,gy+1),fill=(12,16,26,255))
        bb=composed.getbbox()
        if bb:
            composed=composed.crop(bb)
            scale=min(2.4,108/composed.height)
            composed=composed.resize((round(composed.width*scale),round(composed.height*scale)),Image.Resampling.NEAREST)
            ox,oy=cx-composed.width//2,126-composed.height
            if grip:
                ex,ey=ox+round((grip[0]-bb[0])*scale),oy+round((grip[1]-bb[1])*scale)
                from PIL import ImageDraw
                ImageDraw.Draw(canvas).line((228,0,ex,ey+2),fill=(*W,255),width=1)
            canvas.alpha_composite(composed,(ox,oy))
    canvas.resize((960, 540), Image.NEAREST).convert("RGB").save(path)
    print(f"Portada -> {path}")


def export_samples(folder):
    """Guarda los sonidos como .wav para escucharlos sin abrir el juego."""
    import wave
    folder.mkdir(parents=True, exist_ok=True)
    for name, maker in (("disparo", sonidos.thwip), ("impacto", sonidos.splat), ("retorno", sonidos.zip_back)):
        pcm = sonidos.to_pcm16(maker())
        with wave.open(str(folder / f"{name}.wav"), "wb") as w:
            w.setnchannels(2)
            w.setsampwidth(2)
            w.setframerate(sonidos.RATE)
            w.writeframes(pcm.tobytes())
    print(f"Muestras de sonido -> {folder}")


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    game = Path(sys.argv[1])
    out = Path(sys.argv[2])
    set_variant(sys.argv[3] if len(sys.argv) > 3 else 'classic')
    if not (game / "SNB.exe").exists():
        sys.exit("No encuentro SNB.exe en esa carpeta.")
    build_sprites(game, out)
    if '--keep-audio' not in sys.argv or not (out / SOUND_BUNDLE).is_file():
        build_sounds(game, out)
    build_preview(game, out.parent / "preview.png")
    export_samples(Path(__file__).parent / "muestras")


if __name__ == "__main__":
    main()
