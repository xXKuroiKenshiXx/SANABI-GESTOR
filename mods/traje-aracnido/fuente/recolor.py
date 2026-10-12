"""Redibujado del traje aracnido a partir de los sprites originales.

El protagonista usa sombrero, abrigo largo y capa. Para que se lea como un heroe arana:
  * la capa (capa separada) desaparece;
  * en cada frame se ubica la cara y la cintura;
  * arriba de la cintura todo es torso rojo con red de telarana oscura;
  * abajo se borra el abrigo y su forro y quedan solo las piernas (azules, botas rojas); si el abrigo tapaba
    parte de una pierna, se completa con una linea desde la cadera;
  * el sombrero se borra y se dibuja una cabeza con mascara y ojos blancos grandes con borde negro;
  * la garra del gancho pasa a ser una bola de telarana redonda.
Los colores del original se usan solo para saber que es cada pixel (abrigo, forro, pantalon, piel, metal).
"""
import colorsys
import math
import re

from PIL import Image, ImageDraw

RED = [(40, 4, 10), (92, 8, 18), (146, 14, 26), (196, 26, 36), (226, 46, 52), (246, 98, 96)]
BLUE = [(10, 20, 58), (22, 50, 118), (36, 84, 170), (58, 124, 210), (96, 160, 232)]
WEB = (78, 6, 16)            # lineas de la telarana sobre el rojo
TORSO_HALF = 5.0
OUTLINE = (14, 8, 14)
WHITE = (240, 244, 255)
EYE = (250, 252, 255)
VARIANT = 'classic'
CLASSIC_RED, CLASSIC_BLUE = RED[:], BLUE[:]


def set_variant(variant):
    global VARIANT, RED, BLUE, WEB, EYE
    if variant not in ('classic', 'noir', '2099'):
        raise ValueError(variant)
    VARIANT = variant
    RED, BLUE, WEB, EYE = CLASSIC_RED[:], CLASSIC_BLUE[:], (110, 14, 25), (250, 252, 255)
    if variant == '2099':
        RED = [(5, 9, 30), (9, 20, 65), (14, 35, 104), (20, 51, 145), (30, 73, 188), (68, 111, 218)]
        BLUE = RED[:]
        WEB, EYE = (35, 61, 130), (255, 58, 65)


def emblem(opx, w, h, cx, cy):
    color = (12, 10, 18) if VARIANT == 'classic' else (235, 32, 48)
    for dy in range(-2, 3):
        for dx in range(-3, 4):
            if dx == 0 or (abs(dx) in (2, 3) and abs(dy) in (1, 2) and (dx + dy) % 2 == 0):
                x, y = round(cx + dx), round(cy + dy)
                if 0 <= x < w and 0 <= y < h and opx[x, y][3]:
                    opx[x, y] = (*color, opx[x, y][3])


def ramp(r, t):
    t = min(max(t, 0.0), 1.0)
    return r[min(int(t * len(r)), len(r) - 1)]


def lum(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def classify(r, g, b):
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if b > r + 40 and g > r + 30 and s > 0.45:
        return 'cyan'
    if r > 120 and r > g > b and 0.12 < s < 0.5 and v > 0.55:
        return 'skin'
    if r > 90 and r > g > b and s >= 0.3 and (g - b) > 20 and r - g < 60:
        return 'gold'
    if r > g * 1.6 and r >= 40:
        return 'lining' if r < 70 else 'scarf'
    if b > r + 8:
        return 'metal'
    L = lum(r, g, b)
    if L < 36:
        return 'pants'
    if L < 75:
        return 'coat'
    return 'light'


def web_line(x, y):
    return (x - y) % 9 == 0 or (y % 8 == 0 and x % 2 == 0)


# ---------------------------------------------------------------- cara

def find_face(src):
    px = src.load()
    bb = src.getbbox()
    if not bb:
        return None
    x0, y0, x1, y1 = bb
    limit = y0 + (y1 - y0) * 0.45
    skin = [(x, y) for y in range(y0, y1) for x in range(x0, x1)
            if px[x, y][3] > 0 and y < limit and classify(*px[x, y][:3]) == 'skin']
    if not skin:
        # En vistas de espalda no hay piel: el panel rojo del sombrero permite
        # anclar la máscara al propio frame, sin copiar la posición de otra pose.
        hat = {(x, y) for y in range(y0, int(limit)) for x in range(x0, x1)
               if px[x, y][3] and classify(*px[x, y][:3]) == 'scarf'}
        candidates = [c for c in components(hat)
                      if 3 <= len(c) <= 45 and max(x for x, _ in c)-min(x for x, _ in c) <= 10
                      and max(y for _, y in c)-min(y for _, y in c) <= 6]
        if not candidates:
            return None
        cap = min(candidates, key=lambda c: min(y for _, y in c))
        fcx = sum(x for x, _ in cap)/len(cap)
        return {'y0': min(y for _, y in cap)+3, 'cx': fcx, 'right': fcx >= (x0+x1)/2-1}
    top = min(y for _, y in skin)
    face = [(x, y) for x, y in skin if y <= top + 3]
    xs = [x for x, _ in face]
    ys = [y for _, y in face]
    fcx = sum(xs) / len(xs)
    return {'y0': min(ys), 'cx': fcx, 'right': fcx >= (x0 + x1) / 2 - 1}


def frame_key(name):
    m = re.match(r'(.*?)\s*(?:\(lp\))?\s*\((\d+)\)$', name)
    return (m.group(1), int(m.group(2))) if m else (name, 0)


def faces_for(frames):
    """frames: {nombre: imagen}. Devuelve {nombre: cara}, copiando la de un frame vecino si falta."""
    faces = {n: find_face(im) for n, im in frames.items()}
    by_anim = {}
    for n in frames:
        anim, idx = frame_key(n)
        by_anim.setdefault(anim, []).append((idx, n))
    for anim, items in by_anim.items():
        with_face = [(i, n) for i, n in items if faces[n]]
        if not with_face:
            continue
        for idx, n in items:
            if faces[n]:
                continue
            ri, rn = min(with_face, key=lambda t: abs(t[0] - idx))
            if abs(ri - idx) > 3:
                continue
            rb, b = frames[rn].getbbox(), frames[n].getbbox()
            if not rb or not b:
                continue
            ref = faces[rn]
            faces[n] = {'y0': b[1] + (ref['y0'] - rb[1]), 'cx': b[0] + (ref['cx'] - rb[0]), 'right': ref['right']}
    return faces


# ---------------------------------------------------------------- cuerpo

def body(src, face):
    w, h = src.size
    spx = src.load()
    bb = src.getbbox()
    out = Image.new('RGBA', src.size)
    if not bb:
        return out
    opx = out.load()
    x0, y0, x1, y1 = bb
    if face:
        head_top = face['y0'] - 3
        waist = int(head_top + 0.47 * (y1 - head_top))
    else:
        waist = int(y0 + 0.55 * (y1 - y0))

    cls = {}
    for y in range(h):
        for x in range(w):
            p = spx[x, y]
            if p[3]:
                cls[(x, y)] = classify(*p[:3])

    # eje del cuerpo: de la cara a la cadera (asi funciona tambien cuando se inclina)
    hip_px = [x for (x, y), c in cls.items() if c == 'pants' and waist <= y < waist + 5]
    hip_x = sum(hip_px) / len(hip_px) if hip_px else None
    if face:
        top_x = face['cx'] + (-1.5 if face['right'] else 1.5)
        top_y = face['y0']
        bottom_x = hip_x if hip_x is not None else top_x

        def axis_at(y):
            t = min(max((y - top_y) / max(1, waist - top_y), 0), 1)
            return top_x + (bottom_x - top_x) * t
    elif hip_x is not None:
        def axis_at(y):
            return hip_x
    else:
        axis_at = None

    legs = set()
    for (x, y), c in cls.items():
        r, g, b, a = spx[x, y]
        L = lum(r, g, b)
        if c in ('metal', 'cyan') and L > 65 and axis_at is not None and abs(x-axis_at(y)) > 4:
            continue  # piezas mecánicas integradas en poses de diálogo/agachado
        if y < waist:
            # el abrigo que vuela lejos del cuerpo se borra; brazos (manga oscura) y manos se quedan
            if axis_at is not None and c in ('coat', 'lining', 'scarf', 'light') and abs(x - axis_at(y)) > TORSO_HALF:
                continue
            if c == 'lining' and face and y > face['y0'] + 4:
                continue
            if not face and axis_at is not None and y < waist - 14:
                continue  # sin cara: lo que queda muy arriba es sombrero o abrigo al viento
            if c == 'cyan':
                col = WHITE
            elif c in ('pants',) and L < 18:
                col = OUTLINE
            else:
                col = ramp(RED, 0.35 + L / 160)
                if web_line(x, y) and c != 'skin':
                    col = WEB
                if VARIANT == 'classic' and face and y > face['y0'] + 7 and abs(x-axis_at(y)) > 3:
                    col = ramp(BLUE, 0.35 + L/160)
            opx[x, y] = (*col, a)
        else:
            if c in ('pants', 'metal', 'skin'):
                if c == 'skin':
                    opx[x, y] = (*ramp(RED, 0.6), a)  # manos: guantes rojos
                elif L < 16:
                    opx[x, y] = (*OUTLINE, a)
                else:
                    opx[x, y] = (*ramp(BLUE, 0.25 + L / 70), a)
                    legs.add((x, y))
            # abrigo, forro y bufanda por debajo de la cintura: se borran

    # cadera: el ancho del torso justo arriba de la cintura
    row = [x for x in range(w) if opx[x, waist - 1][3] > 0] if 0 < waist <= h else []
    if row:
        hip_c = (min(row) + max(row)) / 2
        hips = [(hip_c - 1.5, waist), (hip_c + 1.5, waist)]
        for comp in components(legs):
            top = min(comp, key=lambda p: p[1])
            if top[1] <= waist + 2:
                continue
            hip = min(hips, key=lambda hp: abs(hp[0] - top[0]))
            thick_line(opx, w, h, hip, top, ramp(BLUE, 0.55))
        # botas: las 3 filas de abajo de cada pierna
        for comp in components(set((x, y) for y in range(waist, h) for x in range(w)
                                   if opx[x, y][3] > 0 and opx[x, y][:3] != OUTLINE)):
            bottom = max(p[1] for p in comp)
            if bottom - min(p[1] for p in comp) < 3:
                continue
            for (x, y) in comp:
                if y >= bottom - 4:
                    opx[x, y] = (*ramp(RED, 0.55 + (y - bottom + 2) * 0.1), 255)

    outline(opx, w, h)
    if face:
        draw_head(opx, w, h, face)
        chest_y = min(waist - 3, face['y0'] + 9)
        if chest_y > face['y0'] + 4:
            emblem(opx, w, h, axis_at(chest_y), chest_y)
    return out


def components(points):
    points = set(points)
    seen = set()
    for p in points:
        if p in seen:
            continue
        stack, comp = [p], []
        seen.add(p)
        while stack:
            x, y = stack.pop()
            comp.append((x, y))
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    q = (x + dx, y + dy)
                    if q in points and q not in seen:
                        seen.add(q)
                        stack.append(q)
        if len(comp) >= 3:
            yield comp


def thick_line(opx, w, h, a, b, col):
    steps = int(max(abs(b[0] - a[0]), abs(b[1] - a[1]))) + 1
    for i in range(steps + 1):
        t = i / steps
        cx = a[0] + (b[0] - a[0]) * t
        cy = a[1] + (b[1] - a[1]) * t
        for dx in (-1, 0, 1):
            x, y = int(round(cx + dx)), int(round(cy))
            if 0 <= x < w and 0 <= y < h and opx[x, y][3] == 0:
                opx[x, y] = (*(col if dx == 0 else ramp(BLUE, 0.35)), 255)


def outline(opx, w, h):
    edge = []
    for y in range(h):
        for x in range(w):
            if opx[x, y][3] == 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < w and 0 <= ny < h) or opx[nx, ny][3] == 0:
                    edge.append((x, y))
                    break
    for (x, y) in edge:
        r, g, b, a = opx[x, y]
        if (r, g, b) not in (WHITE, EYE):
            opx[x, y] = (max(0, r // 3), max(0, g // 3), max(0, b // 3), a)


def draw_head(opx, w, h, face):
    cx = face['cx'] + (-0.5 if face['right'] else 0.5)
    cy = face['y0'] + 0.5
    rx, ry = 2.8, 3.2
    for y in range(0, min(h, face['y0'] + 1)):
        for x in range(max(0, int(cx - 11)), min(w, int(cx + 12))):
            if y < face['y0'] or abs(x - cx) > rx + 0.5:
                opx[x, y] = (0, 0, 0, 0)
    head = set()
    for y in range(int(cy - ry - 1), int(cy + ry + 2)):
        for x in range(int(cx - rx - 1), int(cx + rx + 2)):
            if 0 <= x < w and 0 <= y < h and ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                head.add((x, y))
    back = (lambda x: x < cx - 1.2) if face['right'] else (lambda x: x > cx + 1.2)
    for (x, y) in head:
        col = RED[3] if back(x) else RED[4]
        if web_line(x, y):
            col = WEB
        opx[x, y] = (*col, 255)
    for (x, y) in head:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if (nx, ny) not in head and 0 <= nx < w and 0 <= ny < h and opx[nx, ny][3] == 0:
                opx[nx, ny] = (*OUTLINE, 255)
    # Lentes angulares compactos, separados por un puente rojo. El perfil no
    # muestra dos óvalos enormes que ocupen toda la máscara.
    s = 1 if face['right'] else -1
    ey = int(round(cy - 1))
    fx = int(round(cx + s))
    bx = int(round(cx - s))
    eyes = [(fx, ey), (fx + s, ey - 1), (fx, ey + 1), (bx, ey)]
    eyes = [p for p in eyes if p in head]
    for (x, y) in eyes:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if True:
                q = (x + dx, y + dy)
                if q in head and q not in eyes:
                    opx[q[0], q[1]] = (*OUTLINE, 255)
    for (x, y) in eyes:
        opx[x, y] = (*EYE, 255)


# ---------------------------------------------------------------- brazo, capa, garra

def arm(src, anchor=None):
    """Redraw the mechanical arm as a narrow articulated sleeve and glove.

    Keep the original canvas and animation envelope but discard gears, metal
    claws and bulky shoulder circles. Long dash streaks become white web trails.
    """
    bb = src.getbbox()
    out = Image.new('RGBA', src.size)
    if not bb:
        return out
    x0,y0,x1,y1 = bb
    bw,bh = x1-x0,y1-y0
    px = src.load()
    horizontal = bw > bh
    points = []
    lo,hi = (x0,x1) if horizontal else (y0,y1)
    for along in range(lo,hi):
        cross = [v for v in range(y0,y1) if px[along,v][3] > 64] if horizontal else [v for v in range(x0,x1) if px[v,along][3] > 64]
        if cross:
            cross.sort()
            points.append((along,cross[len(cross)//2]) if horizontal else (cross[len(cross)//2],along))
    if len(points) < 2:
        return out
    points = points[1:-1] if len(points) > 6 else points
    smooth = []
    for i,p in enumerate(points):
        nearby = points[max(0,i-1):i+2]
        smooth.append((p[0],round(sum(q[1] for q in nearby)/len(nearby))) if horizontal else (round(sum(q[0] for q in nearby)/len(nearby)),p[1]))
    draw = ImageDraw.Draw(out)
    if max(bw,bh) > 4*max(1,min(bw,bh)) and max(bw,bh)>35:
        draw.line(smooth, fill=(*WHITE,220), width=1)
        return out
    # Anchor the new shoulder to the torso, rather than tracing the outside of
    # the metal mechanism (which produced a detached vertical protrusion).
    pivot=(src.width/2,src.height/2)
    if math.dist(smooth[-1],pivot)<math.dist(smooth[0],pivot):smooth.reverse()
    start=smooth[0]
    shoulder=anchor or pivot
    length=math.dist(smooth[-1],start)
    scale=min(0.7,15/max(1,length))
    smooth=[(round(shoulder[0]+(p[0]-start[0])*scale),round(shoulder[1]+(p[1]-start[1])*scale)) for p in smooth]
    sleeve = (38,40,47) if VARIANT == 'noir' else (28,55,139) if VARIANT == '2099' else (203,32,44)
    light = (78,83,92) if VARIANT == 'noir' else (68,100,197) if VARIANT == '2099' else (244,70,72)
    draw.line(smooth,fill=(*OUTLINE,255),width=4)
    draw.line(smooth,fill=(*sleeve,255),width=2)
    draw.line(smooth,fill=(*light,255),width=1)
    # Human-sized glove, with a compact web shooter at the wrist.
    hx,hy=smooth[-1]
    glove=(38,40,47) if VARIANT=='noir' else CLASSIC_RED[3]
    draw.rectangle((hx-1,hy-1,hx+1,hy+1),fill=(*glove,255))
    if VARIANT=='2099':
        draw.point(smooth[len(smooth)//2],fill=(*CLASSIC_RED[4],255))
    return out


def old_arm(src):
    out = Image.new('RGBA', src.size)
    spx, opx = src.load(), out.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = spx[x, y]
            if not a:
                continue
            c = classify(r, g, b)
            L = lum(r, g, b)
            if c == 'cyan':
                col = WHITE
            elif L < 18:
                col = OUTLINE
            elif c == 'gold':
                col = (30, 30, 40)
            elif L > 215:
                col = RED[5]
            else:
                col = ramp(RED, 0.2 + L / 230)
                if web_line(x, y) and L > 60:
                    col = WEB
            opx[x, y] = (*col, a)
            if VARIANT == '2099' and L > 45 and (x-y) % 8 < 2:
                opx[x, y] = (*ramp(CLASSIC_RED, 0.55 + L/250), a)
    return out


def web_ball(src):
    """La garra se convierte en una bola de telarana redonda."""
    bb = src.getbbox()
    out = Image.new('RGBA', src.size)
    if not bb:
        return out
    spx, opx = src.load(), out.load()
    x0, y0, x1, y1 = bb
    bw, bh = x1 - x0, y1 - y0
    if bh > 2.2 * bw and bh > 10:
        for y in range(y0, y1):
            for x in range(x0, x1):
                a = spx[x, y][3]
                if a:
                    opx[x, y] = (*WHITE, int(a * (0.25 + 0.6 * (1 - (y - y0) / bh))))
        rad = 1.5
        cx, cy = (x0 + x1 - 1) / 2, y0 + rad + 1
    else:
        cx, cy = (x0 + x1 - 1) / 2, (y0 + y1 - 1) / 2
        rad = max(1.0, min(2.0, min(bw, bh) / 3))
    w, h = src.size
    for y in range(int(cy - rad - 2), int(cy + rad + 3)):
        for x in range(int(cx - rad - 2), int(cx + rad + 3)):
            if not (0 <= x < w and 0 <= y < h):
                continue
            d = math.hypot(x - cx, y - cy)
            if d > rad + 0.5:
                continue
            ang = math.degrees(math.atan2(y - cy, x - cx)) % 45
            if d > rad - 0.5:
                col = (178, 184, 200)
            elif ang < 9 or ang > 36 or abs(d - rad * 0.55) < 0.5:
                col = (206, 212, 226)
            else:
                col = WHITE
            opx[x, y] = (*col, 255)
    return out


def recolor_image(img, layer, face=None, arm_anchor=None):
    src = img.convert('RGBA')
    if layer == 'grab':
        return web_ball(src)
    if layer == 'cape':
        if VARIANT == '2099':
            out = Image.new('RGBA', src.size)
            for y in range(src.height):
                for x in range(src.width):
                    r, g, b, a = src.getpixel((x, y))
                    if a:
                        out.putpixel((x, y), (*ramp(CLASSIC_RED, 0.2 + lum(r, g, b)/220), a))
            return out
        return Image.new('RGBA', src.size)
    if layer == 'arm':
        return arm(src,arm_anchor)
    if VARIANT == 'noir':
        return noir(src, layer, face if face is not None else find_face(src))
    return body(src, face if face is not None else find_face(src))


def noir(src, layer, face):
    # Noir conserva el sombrero y la gabardina: máscara negra y lentes blancos.
    out = Image.new('RGBA', src.size)
    shades = [(15, 17, 21), (31, 34, 42), (51, 55, 65), (84, 90, 104), (126, 132, 145)]
    for y in range(src.height):
        for x in range(src.width):
            r, g, b, a = src.getpixel((x, y))
            if a:
                out.putpixel((x, y), (*ramp(shades, 0.1+lum(r, g, b)/200), a))
    if layer == 'body' and face:
        px = out.load()
        cx, cy = round(face['cx']), face['y0'] + 1
        for dy in range(-1, 4):
            for dx in range(-3, 4):
                x, y = cx+dx, cy+dy
                if 0 <= x < out.width and 0 <= y < out.height and px[x,y][3]:
                    px[x,y] = (16, 17, 22, px[x,y][3])
        for dx in (-2, 1):
            for ox in (0, 1):
                x, y = cx+dx+ox, cy
                if 0 <= x < out.width and 0 <= y < out.height and px[x,y][3]:
                    px[x,y] = (*EYE, 255)
    return out
