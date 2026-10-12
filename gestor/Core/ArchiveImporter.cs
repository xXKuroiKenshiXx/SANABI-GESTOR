using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace SanabiGestor.Core;

// Convierte un .zip/.7z/.rar o una carpeta cualquiera en un StagedMod con rutas relativas al juego.
public static class ArchiveImporter
{
    static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".xz"];
    static readonly string[] DocExtensions = [".txt", ".md", ".pdf", ".url", ".html", ".htm", ".nfo", ".jpg", ".jpeg", ".png", ".gif", ".webp"];

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string Extract(string archivePath)
    {
        var dir = Downloader.NewTempDir("x");
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;
            var target = Path.GetFullPath(Path.Combine(dir, entry.Key.Replace('\\', '/')));
            if (!target.StartsWith(root, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.WriteToFile(target, new ExtractionOptions { Overwrite = true });
        }
        return dir;
    }

    public static StagedMod Import(string path, GameInstall game, string fallbackName, string source = "local",
        string? sourceRef = null, ModManifest? hint = null)
    {
        var folder = File.Exists(path) && IsArchive(path) ? Extract(path) : path;
        if (File.Exists(folder))
        {
            // Un archivo suelto (.dll, .bundle): lo tratamos como una carpeta de un solo archivo.
            var single = Downloader.NewTempDir("f");
            File.Copy(folder, Path.Combine(single, Path.GetFileName(folder)));
            folder = single;
        }
        return FromFolder(folder, game, fallbackName, source, sourceRef, hint);
    }

    public static StagedMod FromFolder(string folder, GameInstall game, string fallbackName, string source,
        string? sourceRef, ModManifest? hint)
    {
        var root = Unwrap(folder);
        var manifestPath = Path.Combine(root, Paths.ManifestName);
        ModManifest? manifest = null;
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), JsonCtx.Default.ModManifest);
            }
            catch (Exception e)
            {
                Log.Write($"mod.json invalido en {fallbackName}: {e.Message}");
            }
        }

        manifest ??= hint ?? new ModManifest();
        if (string.IsNullOrWhiteSpace(manifest.Name)) manifest.Name = fallbackName;
        if (string.IsNullOrWhiteSpace(manifest.Id)) manifest.Id = Paths.Slug(manifest.Name);

        var staging = Downloader.NewTempDir("s");
        string? preview = null;
        if (manifest.Preview is { } image && !Path.IsPathRooted(image))
        {
            var candidate = Path.GetFullPath(Path.Combine(root, image));
            if (candidate.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(candidate) && new FileInfo(candidate).Length <= 8 * 1024 * 1024) preview = candidate;
        }
        var staged = new StagedMod { Manifest = manifest, Dir = staging, Source = source, SourceRef = sourceRef, PreviewPath = preview };

        if (manifest.Files is { Count: > 0 })
        {
            var baseDir = string.IsNullOrEmpty(manifest.Base) ? root : Path.Combine(root, manifest.Base);
            foreach (var map in manifest.Files)
                Place(staged, Path.Combine(baseDir, map.Src), map.Dst);
        }
        else if (Directory.Exists(Path.Combine(root, "files")) && File.Exists(manifestPath))
        {
            var filesDir = Path.Combine(root, "files");
            foreach (var file in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
                Place(staged, file, Paths.Normalize(Path.GetRelativePath(filesDir, file)));
        }
        else
        {
            MapByHeuristics(staged, root, game);
        }

        var rels = staged.RelativeFiles();
        if (rels.Any(r => r.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
            && !manifest.Requires.Contains("bepinex"))
            manifest.Requires.Add("bepinex");
        return staged;
    }

    static void Place(StagedMod staged, string sourceFile, string dst)
    {
        dst = Paths.Normalize(dst);
        if (!File.Exists(sourceFile))
        {
            staged.Warnings.Add($"Falta el archivo {Path.GetFileName(sourceFile)}");
            return;
        }
        if (!Paths.IsSafeTarget(dst))
        {
            staged.Warnings.Add($"Ignorado (ruta no permitida): {dst}");
            return;
        }
        Paths.CopyFile(sourceFile, Path.Combine(staged.Dir, dst.Replace('/', Path.DirectorySeparatorChar)));
    }

    // Si el archivo trae una sola carpeta envolviendo todo, entramos en ella.
    static string Unwrap(string dir)
    {
        while (true)
        {
            var subdirs = Directory.GetDirectories(dir);
            var files = Directory.GetFiles(dir).Where(f => !DocExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (subdirs.Length != 1 || files.Any()) return dir;
            var name = Path.GetFileName(subdirs[0]);
            if (name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
                || name.Equals("SNB_Data", StringComparison.OrdinalIgnoreCase)
                || name.Equals("files", StringComparison.OrdinalIgnoreCase)) return dir;
            dir = subdirs[0];
        }
    }

    static void MapByHeuristics(StagedMod staged, string root, GameInstall game)
    {
        var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Abs: f, Rel: Paths.Normalize(Path.GetRelativePath(root, f))))
            .ToList();
        Dictionary<string, List<string>>? bundleIndex = null;
        var dirMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(string Abs, string Rel)>();
        var pluginFolder = $"BepInEx/plugins/{staged.Manifest.Id}";

        foreach (var (abs, rel) in all)
        {
            var parts = rel.Split('/');
            var name = parts[^1];
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var anchor = Array.FindIndex(parts, p =>
                p.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) || p.Equals("SNB_Data", StringComparison.OrdinalIgnoreCase));

            if (anchor >= 0)
            {
                var head = parts[anchor].Equals("bepinex", StringComparison.OrdinalIgnoreCase) ? "BepInEx" : "SNB_Data";
                Place(staged, abs, string.Join('/', new[] { head }.Concat(parts[(anchor + 1)..])));
            }
            else if (parts.Length == 1 && (name.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)
                                           || name.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)))
            {
                Place(staged, abs, name);
            }
            else if (ext == ".dll")
            {
                var pi = Array.FindIndex(parts, p => p.Equals("plugins", StringComparison.OrdinalIgnoreCase) || p.Equals("patchers", StringComparison.OrdinalIgnoreCase));
                string dst = pi >= 0
                    ? $"BepInEx/{parts[pi].ToLowerInvariant()}/{string.Join('/', parts[(pi + 1)..])}"
                    : $"{pluginFolder}/{name}";
                Place(staged, abs, dst);
                var srcDir = Path.GetDirectoryName(rel.Replace('/', Path.DirectorySeparatorChar)) ?? "";
                dirMap.TryAdd(Paths.Normalize(srcDir), Paths.Normalize(Path.GetDirectoryName(dst) ?? ""));
            }
            else if (ext == ".cfg")
            {
                Place(staged, abs, $"BepInEx/config/{name}");
            }
            else if (ext == ".bundle")
            {
                bundleIndex ??= BuildBundleIndex(game);
                var dst = MatchBundle(bundleIndex, parts);
                if (dst != null) Place(staged, abs, dst);
                else staged.Warnings.Add($"No se sabe donde va {rel} (hay varios o ningun archivo con ese nombre en el juego)");
            }
            else
            {
                pending.Add((abs, rel));
            }
        }

        // Recursos que acompanan a una .dll (imagenes, sonidos) van a la misma carpeta del plugin.
        foreach (var (abs, rel) in pending)
        {
            var dir = Paths.Normalize(Path.GetDirectoryName(rel.Replace('/', Path.DirectorySeparatorChar)) ?? "");
            var match = dirMap.Keys.Where(k => k.Length == 0 ? true : dir.Equals(k, StringComparison.OrdinalIgnoreCase)
                                                                  || dir.StartsWith(k + "/", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(k => k.Length).FirstOrDefault();
            var isDoc = DocExtensions.Contains(Path.GetExtension(rel).ToLowerInvariant()) && !rel.Contains('/');
            if (match != null && !isDoc)
            {
                var tail = match.Length == 0 ? rel : rel[(match.Length + 1)..];
                Place(staged, abs, $"{dirMap[match]}/{tail}");
            }
            else if (!isDoc)
            {
                staged.Warnings.Add($"Ignorado: {rel}");
            }
        }
    }

    static Dictionary<string, List<string>> BuildBundleIndex(GameInstall game)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var root = game.Abs("SNB_Data/StreamingAssets");
        if (!Directory.Exists(root)) return index;
        foreach (var file in Directory.EnumerateFiles(root, "*.bundle", SearchOption.AllDirectories))
        {
            var rel = Paths.Normalize(Path.GetRelativePath(game.Root, file));
            var name = Path.GetFileName(file);
            if (!index.TryGetValue(name, out var list)) index[name] = list = new();
            list.Add(rel);
        }
        return index;
    }

    static string? MatchBundle(Dictionary<string, List<string>> index, string[] sourceParts)
    {
        if (!index.TryGetValue(sourceParts[^1], out var candidates)) return null;
        if (candidates.Count == 1) return candidates[0];
        // Varias opciones: gana la que comparte mas carpetas finales con la ruta del archivo del mod.
        var scored = candidates.Select(c =>
        {
            var cp = c.Split('/');
            var score = 0;
            for (int i = 2; i <= Math.Min(cp.Length, sourceParts.Length); i++)
            {
                if (!cp[^i].Equals(sourceParts[^i], StringComparison.OrdinalIgnoreCase)) break;
                score++;
            }
            return (c, score);
        }).OrderByDescending(x => x.score).ToList();
        return scored[0].score > 0 && scored[0].score > scored[1].score ? scored[0].c : null;
    }
}
