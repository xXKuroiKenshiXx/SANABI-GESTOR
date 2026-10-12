# Idiomas del gestor

Incluidos: `es.json`, `en.json`, `zh.json` (chino simplificado), `ru.json`.
Las fuentes Noto Sans CJK SC regular y negrita están incorporadas para que el chino no dependa de fuentes del sistema en Linux. Su licencia se incluye en `LICENSE-NotoSansSC.txt` junto al ejecutable.
Las claves son textos de interfaz en español; los valores son sus traducciones.
Los mensajes variables usan `{0}`, `{1}`; se deben conservar en el texto traducido.
No se traducen los nombres, descripciones ni README escritos por los autores de mods.
Los errores técnicos provenientes de Steam, Nexus, GitHub o el sistema conservan su detalle original.

Para agregar cualquier otro idioma, exportar un JSON con esta estructura e importarlo desde Ajustes → Importar idioma:

```json
{
  "language": "de",
  "name": "Deutsch",
  "messages": {
    "Ajustes": "Einstellungen",
    "Mis mods": "Meine Mods",
    "Instalar": "Installieren"
  }
}
```

Para un paquete completo, copiar todas las claves de `es.json` a `messages` y traducir sus valores. Las claves que falten conservan el texto español; no se inventan traducciones. Los paquetes se guardan en `SanabiGestor-data/languages/` y persisten entre reinicios. Se puede generar ese JSON con un traductor externo y luego revisar/importar el resultado: la aplicación no envía texto a servicios de traducción ni requiere una API de pago.
