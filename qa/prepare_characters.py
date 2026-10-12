import json,shutil,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'mods/traje-aracnido/fuente'))
from pack_mods import pack
for slug,source,assembly,old,skin,title,controller,scope in [
    ('habilidades-major-son','major-son-personaje-campana','SanabiNativeMajor','SanabiMajorAbilities','major-son-en-campana','Major Song · Personaje real en campaña','DlcPlayer','campaña principal'),
    ('habilidades-heroe-dlc','heroe-personaje-dlc','SanabiNativeHero','SanabiHeroAbilities','heroe-en-dlc','Sargento · Personaje real en DLC','Player','DLC'),
]:
    folder=ROOT/'mods'/slug;files=folder/'files/BepInEx/plugins';files.mkdir(parents=True,exist_ok=True)
    legacy=files/(old+'.dll')
    if legacy.exists():legacy.unlink()
    shutil.copyfile(folder/'fuente/native/bin/Release/netstandard2.1'/f'{assembly}.dll',files/(assembly+'.dll'))
    shutil.copyfile(ROOT/'mods'/skin/'preview.png',folder/'preview.png')
    description=f'Reemplaza al personaje jugable en {scope} cargando su escena y controlador original {controller}, con sus animaciones y habilidades nativas. No recolorea sprites ni simula ataques externos. Intercambio experimental entre campaña y DLC.'
    instructions='Cerrá SANABI. Desactivá los intercambios de skin Major Son en campaña/Héroe en DLC y sus plugins anteriores. Instalá este mod desde la ficha; requiere BepInEx 6 IL2CPP pre.1. Reiniciá el juego y cargá el capítulo correspondiente. Usa los controles originales del personaje. En Proton: WINEDLLOVERRIDES="winhttp=n,b" %command%.'
    notes='SANABI 1.3.53 con los recursos del DLC instalados. La DLL se compiló contra las interfaces del juego. Pendiente validar dentro de SANABI: carga del actor, HUD/vida, checkpoints, cinemáticas y scripts que esperan al personaje original. Las campañas incluyen mecánicas específicas del protagonista; no se garantiza completar todos sus capítulos con el otro controlador. No cambia el modo del capítulo ni archivos de partida. Se recomienda probar en selección de capítulos.'
    manifest=dict(id=slug,name=title,version='0.2.0',author='Kuro Kenshi',description=description,category='personajes',requires=['bepinex'],gameVersion='1.3.53',tags=['personaje real','controlador nativo','experimental'],preview='preview.png',verification='experimental',installInstructions=instructions,compatibilityNotes=notes)
    (folder/'mod.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (folder/'README.md').write_text(f'# {title}\n\n{description}\n\n## Qué se reemplaza\n\nEl plugin cambia la referencia a la escena del jugador en SceneTransporter antes de cargar/continuar/seleccionar/reiniciar un capítulo. PlayerBase.Instance devuelve el actor alternativo real. El capítulo, su GameSceneSO y su GameModeType permanecen originales; no se editan archivos de guardado. Activa el conjunto nativo de habilidades después de cargar al actor. Usa animadores, físicas, efectos, audio y controles originales de {controller}.\n\nNo es el plugin anterior de ataques J/K/L/R ni el gancho adaptado E. Aquella implementación queda como fuente histórica y ya no se instala. Desactivá plugins antiguos si los copiaste manualmente. Los dos intercambios restauran referencias antes de cada transición para evitar copiar entre sí referencias ya modificadas.\n\n## Instalación y controles\n\n{instructions}\n\n## Compatibilidad y verificación\n\n{notes}\n\nCompilación y empaquetado comprobados. La integración con scripts, salud e interfaz de cada campaña requiere prueba real en el juego: no se presenta como verificado. Código del controlador de carga en mods/{source}/fuente. Desactivar y reiniciar restaura la carga original.\n',encoding='utf-8')
    readme=folder/'README.md';readme.write_text(readme.read_text(encoding='utf-8').replace(f'mods/{source}/fuente',f'mods/{slug}/fuente/native'),encoding='utf-8')
    pack(folder)
print('Packaged two real native-controller swaps; replaced previous partial ability plugins.')
