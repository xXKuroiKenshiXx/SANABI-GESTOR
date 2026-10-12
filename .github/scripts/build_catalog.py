"""Genera catalog.json a partir de mods/*/mod.json y valida cada mod.

Uso:
    python .github/scripts/build_catalog.py           # valida y escribe catalog.json
    python .github/scripts/build_catalog.py --check   # solo valida (para pull requests)
"""
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODS = ROOT / "mods"
OUT = ROOT / "catalog.json"

CATEGORIES = {"personaje", "personajes", "skins", "habilidades", "jugabilidad", "visual", "audio", "dificultad", "traduccion", "herramienta", "otros"}
ALLOWED_ROOTS = ("BepInEx/", "SNB_Data/", "mono/", "dotnet/")
ALLOWED_ROOT_FILES = {"winhttp.dll", "doorstop_config.ini", ".doorstop_version"}
BLOCKED_EXT = {".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".msi", ".lnk", ".sh"}
MAX_FILE = 95 * 1024 * 1024


def safe_target(rel: str) -> bool:
    rel = rel.replace("\\", "/").lstrip("/")
    if not rel or ".." in rel or ":" in rel:
        return False
    if Path(rel).suffix.lower() in BLOCKED_EXT:
        return False
    if rel.lower().startswith("sanabigestor/"):
        return False
    return rel.startswith(ALLOWED_ROOTS) or rel.lower() in ALLOWED_ROOT_FILES


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_mod(folder: Path, errors: list, manifest: dict | None = None) -> dict | None:
    def err(msg):
        errors.append(f"{folder.name}: {msg}")

    manifest_path = folder / "mod.json"
    if not manifest_path.is_file():
        err("falta mod.json")
        return None
    try:
        m = manifest if manifest is not None else json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        err(f"mod.json no es JSON valido: {e}")
        return None

    if m.get("id") != folder.name:
        err(f'"id" tiene que ser igual al nombre de la carpeta ({folder.name})')
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]{1,47}", folder.name):
        err("el nombre de la carpeta solo puede tener minusculas, numeros y guiones")
    for key in ("name", "version", "author", "description"):
        if not str(m.get(key, "")).strip():
            err(f'falta "{key}"')
    if not re.fullmatch(r"\d+(\.\d+){0,3}", str(m.get("version", ""))):
        err('"version" tiene que ser tipo 1.0.0')
    if len(str(m.get("description", ""))) < 10:
        err('"description" es muy corta')
    category = m.get("category", "otros")
    if category not in CATEGORIES:
        err(f'"category" tiene que ser una de: {", ".join(sorted(CATEGORIES))}')

    base = m.get("base")
    src_root = (ROOT / base) if base else folder
    files = []
    if m.get("files"):
        for entry in m["files"]:
            src = src_root / entry["src"]
            files.append((src, entry["dst"]))
    else:
        files_dir = folder / "files"
        if not files_dir.is_dir():
            err('no hay carpeta "files/" ni lista "files" en mod.json')
        else:
            for f in sorted(files_dir.rglob("*")):
                if f.is_file():
                    files.append((f, f.relative_to(files_dir).as_posix()))

    out_files = []
    for src, dst in files:
        if not src.is_file():
            err(f"no existe {src.relative_to(ROOT).as_posix()}")
            continue
        if not safe_target(dst):
            err(f"ruta de destino no permitida: {dst}")
            continue
        size = src.stat().st_size
        if size > MAX_FILE:
            err(f"{dst} pesa mas de 95 MB")
        out_files.append({
            "src": src.relative_to(ROOT).as_posix(),
            "dst": dst,
            "size": size,
            "sha256": sha256(src),
        })
    if not out_files and not any(e.startswith(folder.name) for e in errors):
        err("el mod no tiene archivos")

    preview = m.get("preview")
    if preview and not (folder / preview).is_file():
        err(f"no existe la imagen {preview}")

    entry = {
        "id": m.get("id"),
        "name": m.get("name"),
        "version": m.get("version"),
        "author": m.get("author"),
        "description": m.get("description"),
        "category": category,
        "tags": m.get("tags", []),
        "requires": sorted(set(m.get("requires", [])) | ({"bepinex"} if any(f["dst"].startswith("BepInEx/") for f in out_files) else set())),
        "path": folder.relative_to(ROOT).as_posix(),
        "size": sum(f["size"] for f in out_files),
        "files": out_files,
    }
    for key in ("gameVersion", "homepage", "installInstructions", "compatibilityNotes", "verification"):
        if m.get(key):
            entry[key] = m[key]
    if preview:
        entry["preview"] = f"{folder.relative_to(ROOT).as_posix()}/{preview}"
    if (folder / "README.md").is_file():
        entry["readme"] = f"{folder.relative_to(ROOT).as_posix()}/README.md"
    if m.get("variants"):
        variants = []
        for variant in m["variants"]:
            vm = {**m, **variant}
            vm.pop("variants", None)
            ve = load_mod(folder, errors, vm)
            if ve:
                variants.append({"label": variant["label"], "version": ve["version"],
                    "gameVersion": ve.get("gameVersion"), "compatibilityNotes": ve.get("compatibilityNotes"), "files": ve["files"]})
        entry["variants"] = variants
    return entry


def main() -> int:
    check_only = "--check" in sys.argv
    errors: list[str] = []
    mods = []
    for folder in sorted(p for p in MODS.iterdir() if p.is_dir() and not p.name.startswith("_")):
        entry = load_mod(folder, errors)
        if entry:
            mods.append(entry)

    if errors:
        print("Problemas encontrados:")
        for e in errors:
            print(f"  - {e}")
        return 1

    mods.sort(key=lambda e: e["name"].lower())
    if check_only:
        print(f"OK: {len(mods)} mod(s) validos.")
        return 0

    catalog = {"schema": 1, "mods": mods}
    previous = json.loads(OUT.read_text(encoding="utf-8")) if OUT.is_file() else None
    if previous and {k: v for k, v in previous.items() if k != "generated"} == catalog:
        print("catalog.json sin cambios.")
        return 0
    catalog = {"schema": 1, "generated": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "mods": mods}
    OUT.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"catalog.json actualizado: {len(mods)} mod(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
