"""Describe and package the preserved translation editions; no translation runs here."""
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
folder = ROOT / 'mods/traduccion-latam-fan'
m = json.loads((folder / 'mod.json').read_text(encoding='utf-8'))
default_maps = m['files']
variants = []
for path in sorted((folder/'versiones').glob('Español LATAM *'), reverse=True):
    date = path.name.replace('Español LATAM ', '').replace(' NEW', '')
    day,month,year = date.split('-')
    version = f'{int(year)}.{int(month)}.{int(day)}'
    base = (path/'BUNDELS').relative_to(ROOT).as_posix()
    variants.append(dict(label=f'{date.replace("-", "/")} · LATAM fan', version=version, base=base,
        gameVersion='<1.3.48', files=default_maps,
        compatibilityNotes='Edición histórica para SANABI anterior a 1.3.48. Windows y Linux mediante Steam/Proton usan los mismos bundles y rutas. No reemplaces datos del DLC moderno con esta edición.'))
variants.sort(key=lambda v: tuple(map(int,v['version'].split('.'))), reverse=True)
m.update(base=variants[0]['base'], variants=variants, name='Español LATAM · Traducción fan',
    installInstructions='Elegí una edición en Versión de la traducción y pulsá Instalar. Cerrá SANABI antes de aplicar cambios. En Linux elegí la carpeta de SANABI de Steam/Proton, la misma que contiene SNB.exe y SNB_Data. No necesita Python, Wine adicional ni BepInEx. Para cambiar de edición, elegí otra y reinstalá; para restaurar el original, desactivá el mod.',
    compatibilityNotes=variants[0]['compatibilityNotes'], verification='package',
    description='Cinco ediciones históricas de la traducción LATAM, con selector, instalación reversible y soporte Windows / Steam-Proton en Linux. Descarga e instala los .bundle del repositorio según la fecha elegida; no traduce en tiempo real. Los bundles históricos son anteriores al español oficial y al DLC.')
(folder/'mod.json').write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(folder/'README.md').write_text('''# Español LATAM · Traducción fan

Las cinco ediciones y los archivos de recuperación están dentro de `versiones/`.
La ficha del gestor permite seleccionar 26/04, 01/05, 03/05, 05/06 o 21/06 de 2025.
Cambiar de edición reinstala el mismo mod; desactivarlo restaura el respaldo original.

## Windows y Linux

En Linux ejecutá el gestor nativo y seleccioná la instalación de SANABI de Steam/Proton.
Conservá los nombres y mayúsculas de los archivos. Los bundles de esta colección pertenecen
al juego Windows ejecutado por Proton; no se renombran a StandaloneLinux64.
No se necesita BepInEx ni configurar winhttp para esta traducción.

## Compatibilidad

Estas ediciones se hicieron para SANABI 1.3.37, anterior a 1.3.48 y al DLC.
No contienen los textos nuevos del DLC. El selector conserva el aviso de incompatibilidad
para evitar reemplazar escenas actuales con versiones antiguas. En juegos modernos usá
el español incorporado o prepará nuevos bundles a partir de los assets actuales.

## Funcionamiento

El gestor busca los archivos .bundle de la edición fechada seleccionada en el repositorio,
verifica sus hashes y los copia a las rutas del juego conservando un respaldo.
No hay traducción en tiempo real ni conversión de CSV/JSON en la instalación.
La fuente TranslationCompiler.cs se conserva como herramienta de referencia archivada;
no se integra en el programa ni se necesita Python para usar el gestor.
''',encoding='utf-8')
out=ROOT/'dist/mods';out.mkdir(parents=True,exist_ok=True)
for v in variants:
    manifest={**m,**v};manifest.pop('variants',None);manifest.pop('base',None);manifest.pop('files',None);manifest.pop('label',None)
    with zipfile.ZipFile(out/f"{m['id']}-{v['version']}.zip",'w',zipfile.ZIP_DEFLATED) as z:
        for f in default_maps:z.write(ROOT/v['base']/f['src'],f['dst'])
        z.writestr('mod.json',json.dumps(manifest,ensure_ascii=False,indent=2))
        z.write(folder/'README.md','README.md');z.write(folder/'preview.png','preview.png')
print('Translation editions:',len(variants))
