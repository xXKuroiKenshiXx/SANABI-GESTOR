# Español LATAM · Traducción fan

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
