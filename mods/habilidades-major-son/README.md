# Major Song · Personaje real en campaña

Reemplaza al personaje jugable en campaña principal cargando su escena y controlador original DlcPlayer, con sus animaciones y habilidades nativas. No recolorea sprites ni simula ataques externos. Intercambio experimental entre campaña y DLC.

## Qué se reemplaza

El plugin cambia la referencia a la escena del jugador en SceneTransporter antes de cargar/continuar/seleccionar/reiniciar un capítulo. PlayerBase.Instance devuelve el actor alternativo real. El capítulo, su GameSceneSO y su GameModeType permanecen originales; no se editan archivos de guardado. Activa el conjunto nativo de habilidades después de cargar al actor. Usa animadores, físicas, efectos, audio y controles originales de DlcPlayer.

No es el plugin anterior de ataques J/K/L/R ni el gancho adaptado E. Aquella implementación queda como fuente histórica y ya no se instala. Desactivá plugins antiguos si los copiaste manualmente. Los dos intercambios restauran referencias antes de cada transición para evitar copiar entre sí referencias ya modificadas.

## Instalación y controles

Cerrá SANABI. Desactivá los intercambios de skin Major Son en campaña/Héroe en DLC y sus plugins anteriores. Instalá este mod desde la ficha; requiere BepInEx 6 IL2CPP pre.1. Reiniciá el juego y cargá el capítulo correspondiente. Usa los controles originales del personaje. En Proton: WINEDLLOVERRIDES="winhttp=n,b" %command%.

## Compatibilidad y verificación

SANABI 1.3.53 con los recursos del DLC instalados. La DLL se compiló contra las interfaces del juego. Pendiente validar dentro de SANABI: carga del actor, HUD/vida, checkpoints, cinemáticas y scripts que esperan al personaje original. Las campañas incluyen mecánicas específicas del protagonista; no se garantiza completar todos sus capítulos con el otro controlador. No cambia el modo del capítulo ni archivos de partida. Se recomienda probar en selección de capítulos.

Compilación y empaquetado comprobados. La integración con scripts, salud e interfaz de cada campaña requiere prueba real en el juego: no se presenta como verificado. Código del controlador de carga en mods/habilidades-major-son/fuente/native. Desactivar y reiniciar restaura la carga original.
