"""Package separately compiled ability plugins and migrate appearance labels."""
import json, shutil, sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'mods/traje-aracnido/fuente'))
from pack_mods import pack

for path in (ROOT/'mods').glob('*/mod.json'):
    data=json.loads(path.read_text(encoding='utf-8'))
    if data.get('category')=='personaje' or data['id'] in ('capa-blanca','capa-creeper'):
        data['category']='skins'
    if data['id']=='brazo-minigun':data['category']='habilidades'
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

install='Cerrá SANABI e instalá desde la ficha. Requiere BepInEx 6 IL2CPP pre.1; el gestor ofrece instalarlo. En Linux/Proton usá WINEDLLOVERRIDES="winhttp=n,b" %command%. No necesita la skin; podés combinar ambos mods.'
for slug,assembly,skin,name,description,controls,scope,details in [
    ('habilidades-major-son','SanabiMajorAbilities','major-son-en-campana','Major Son · Habilidades en campaña',
     'Añade combate adaptado de Major Son a la campaña: combo de tres golpes, disparo, dash, ráfaga y salto aéreo extra. Es un plugin independiente de la skin, con daño real a enemigos y energía recargable.',
     'J: combo de tres golpes. K: disparo hacia el cursor. L: dash. R: ráfaga de área. Espacio en el aire: un salto extra; se recarga al aterrizar. Energía máxima 100; recupera 18/s. Disparo 10, dash 18, ráfaga 45.',
     'Solo el protagonista de la campaña principal. No actúa durante pausa, cinemáticas, muerte ni control de clones. Respeta el desbloqueo de salto. El gancho normal se mantiene; el dash lo cancela si estaba conectado.',
     'Es una adaptación programada del conjunto de combate, no una copia del controlador nativo del DLC. Los golpes usan EnemyHitBox y AttackType del juego; un mismo enemigo recibe un solo golpe por ataque. Los disparos se detienen en paredes. Dash usa el controlador de colisiones, sin teletransporte ni invulnerabilidad. No incluye las animaciones de combate nativas de Major ni su HUD de combo/curación.'),
    ('habilidades-heroe-dlc','SanabiHeroAbilities','heroe-en-dlc','Héroe · Gancho en DLC',
     'Añade un gancho funcional al DLC de Major Son: dispara, se engancha a superficies sólidas, atrae al personaje y permite soltarlo con impulso. Plugin independiente de la skin del héroe; mantiene el combate propio del DLC.',
     'Mantené E: disparar el gancho hacia el cursor y atraerte al punto. Soltá E: liberar. Espacio mientras está enganchado: salir con impulso. Alcance 16 unidades, velocidad 18; configurables en BepInEx.',
     'Solo Major Son en el DLC. No actúa durante pausa, cinemáticas ni muerte. No sustituye scripts de misiones ni cambia partidas guardadas.',
     'Es una adaptación del gancho para el controlador del DLC, no el controlador entero de la campaña. La cuerda tiene lanzamiento visible y anclaje relativo a superficies móviles. Respeta colisiones al moverse y cancela el anclaje al cambiar de escena o superar el alcance. No incluye ejecuciones del gancho, rebobinado/ataques cargados del héroe ni la física pendular nativa.'),
]:
    folder=ROOT/'mods'/slug
    existing=folder/'mod.json'
    if existing.exists() and json.loads(existing.read_text(encoding='utf-8')).get('category')=='personajes':continue
    destination=folder/'files/BepInEx/plugins'/f'{assembly}.dll'
    destination.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(folder/'fuente/bin/Release/netstandard2.1'/f'{assembly}.dll',destination)
    shutil.copyfile(ROOT/'mods'/skin/'preview.png',folder/'preview.png')
    notes=f'{scope} Compilado para SANABI Windows 1.3.53 y Proton. Temporizadores y recursos comprobados fuera del juego; prueba de movimiento y combate en SANABI pendiente. No equivale a trasladar todas las habilidades nativas.'
    manifest=dict(id=slug,name=name,version='0.1.0',author='Kuro Kenshi',description=description,category='habilidades',requires=['bepinex'],gameVersion='1.3.53',tags=['habilidades','experimental'],preview='preview.png',verification='experimental',installInstructions=install+' '+controls,compatibilityNotes=notes)
    (folder/'mod.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (folder/'README.md').write_text(f'# {name}\n\n{description}\n\n## Controles\n\n{controls}\n\n## Instalación\n\n{install}\n\n## Alcance real\n\n{details}\n\n## Compatibilidad y pruebas\n\n{notes}\n\nLa apariencia se instala con el mod de skin correspondiente. Desactivar este plugin desde el gestor restaura el comportamiento original. Las DLL usan interfaces del juego generadas localmente; no distribuyen ensamblados del juego.\n',encoding='utf-8')

for slug in ('habilidades-major-son','habilidades-heroe-dlc','brazo-minigun','traje-aracnido','spider-noir','spider-2099','spiderman-modelo-completo','spider-2099-modelo-completo','capa-blanca','capa-creeper','major-son-en-campana','heroe-en-dlc'):
    pack(ROOT/'mods'/slug)
print('Packaged ability plugins independently; appearance categories migrated to skins.')
