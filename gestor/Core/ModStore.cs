using System.IO.Compression;
using System.Text.Json;

namespace SanabiGestor.Core;

// Un mod listo para instalar: Dir contiene los archivos ya ordenados con la ruta relativa al juego.
public sealed class StagedMod
{
    public required ModManifest Manifest { get; init; }
    public required string Dir { get; init; }
    public string Source { get; init; } = "local";
    public string? SourceRef { get; init; }
    public string? PreviewPath { get; init; }
    public List<string> Warnings { get; } = new();

    public List<string> RelativeFiles() =>
        Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)
            .Select(f => Paths.Normalize(Path.GetRelativePath(Dir, f)))
            .Where(f => !f.Equals(Paths.ManifestName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class Paths
{
    public const string ManifestName = "mod.json";

    public static string Normalize(string rel) => rel.Replace('\\', '/').TrimStart('/');

    // Solo se permite escribir dentro de estas carpetas del juego (y los dos archivos de arranque de BepInEx).
    static readonly string[] AllowedRoots = ["BepInEx/", "SNB_Data/", "mono/", "dotnet/"];
    static readonly string[] AllowedRootFiles = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version"];
    static readonly string[] BlockedExtensions = [".exe", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".msi", ".lnk", ".sh"];

    public static bool IsSafeTarget(string rel)
    {
        rel = Normalize(rel);
        if (rel.Length == 0 || rel.Contains("..") || Path.IsPathRooted(rel) || rel.Contains(':')) return false;
        if (BlockedExtensions.Contains(Path.GetExtension(rel).ToLowerInvariant())) return false;
        if (rel.StartsWith("SanabiGestor/", StringComparison.OrdinalIgnoreCase)) return false;
        return AllowedRoots.Any(r => rel.StartsWith(r, StringComparison.OrdinalIgnoreCase))
               || AllowedRootFiles.Any(f => rel.Equals(f, StringComparison.OrdinalIgnoreCase));
    }

    public static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, true);
    }

    public static void TryDeleteEmptyParents(string file, string stopAt)
    {
        try
        {
            var dir = Path.GetDirectoryName(file);
            var stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
            while (dir != null && Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar).Length > stop.Length
                   && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
            // Carpetas vacias de mas no son un problema.
        }
    }

    public static string Slug(string text)
    {
        var chars = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "mod" : slug[..Math.Min(slug.Length, 48)];
    }
}

public sealed class ModStore
{
    readonly GameInstall game;
    public GameState State { get; private set; }

    string Home => game.Abs("SanabiGestor");
    string BackupDir => Path.Combine(Home, "backup");
    string ModsDir => Path.Combine(Home, "mods");
    string StatePath => Path.Combine(Home, "state.json");

    ModStore(GameInstall game, GameState state)
    {
        this.game = game;
        State = state;
    }

    public GameInstall Game => game;

    public static ModStore Load(GameInstall game)
    {
        var path = Path.Combine(game.Root, "SanabiGestor", "state.json");
        GameState state = new();
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.GameState);
                if (loaded != null)
                {
                    state = loaded;
                    state.Mods = new(state.Mods, StringComparer.OrdinalIgnoreCase);
                    state.Files = new(state.Files, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception e)
            {
                Log.Write($"state.json corrupto, se respalda: {e.Message}");
                File.Copy(path, path + ".roto", true);
            }
        }
        return new ModStore(game, state);
    }

    public void Save()
    {
        Directory.CreateDirectory(Home);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(State, JsonCtx.Default.GameState));
        File.Move(tmp, StatePath, true);
    }

    public string StagedPath(string modId) => Path.Combine(ModsDir, modId);

    // Archivos que este mod pisaria y que ya estan aplicados por otro mod activo.
    public List<(string File, string Owner)> Conflicts(string modId, IEnumerable<string> files)
    {
        var result = new List<(string, string)>();
        foreach (var f in files)
        {
            if (State.Files.TryGetValue(f, out var rec) && rec.Owners.Count > 0)
            {
                var top = rec.Owners[^1];
                if (!top.Equals(modId, StringComparison.OrdinalIgnoreCase)) result.Add((f, top));
            }
        }
        return result;
    }

    public void Install(StagedMod staged, Action<string>? log = null)
    {
        var id = staged.Manifest.Id;
        if (State.Mods.ContainsKey(id))
        {
            log?.Invoke($"Reemplazando la version instalada de {id}...");
            Uninstall(id);
        }

        var files = staged.RelativeFiles();
        var unsafeFiles = files.Where(f => !Paths.IsSafeTarget(f)).ToList();
        if (unsafeFiles.Count > 0)
            throw new InvalidOperationException("El mod intenta escribir fuera de las carpetas permitidas:\n" +
                                                string.Join("\n", unsafeFiles.Take(10)));
        if (files.Count == 0) throw new InvalidOperationException("El mod no tiene archivos para instalar.");

        var dest = StagedPath(id);
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        Directory.CreateDirectory(ModsDir);
        try
        {
            Directory.Move(staged.Dir, dest);
        }
        catch (IOException)
        {
            // Distinta unidad de disco: hay que copiar.
            CopyTree(staged.Dir, dest);
        }
        if (staged.PreviewPath is { } preview && File.Exists(preview) && new FileInfo(preview).Length <= 8 * 1024 * 1024)
            File.Copy(preview, Path.Combine(dest, ".gestor-preview.png"), true);
        File.WriteAllText(Path.Combine(dest, Paths.ManifestName),
            JsonSerializer.Serialize(staged.Manifest, JsonCtx.Default.ModManifest));

        State.Mods[id] = new InstalledMod
        {
            Id = id,
            Name = staged.Manifest.Name,
            Version = staged.Manifest.Version,
            Author = staged.Manifest.Author,
            Description = staged.Manifest.Description,
            Source = staged.Source,
            SourceRef = staged.SourceRef,
            Files = files,
            Requires = staged.Manifest.Requires.ToList(),
            InstalledAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        Save();
        Enable(id, log);
    }

    static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            Paths.CopyFile(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    public void Enable(string id, Action<string>? log = null)
    {
        var mod = State.Mods[id];
        var staged = StagedPath(id);
        foreach (var rel in mod.Files)
        {
            var target = game.Abs(rel);
            if (!State.Files.TryGetValue(rel, out var rec))
            {
                rec = new FileRecord { OriginalExisted = File.Exists(target) };
                if (rec.OriginalExisted) Paths.CopyFile(target, BackupPath(rel));
                State.Files[rel] = rec;
            }
            rec.Owners.RemoveAll(o => o.Equals(id, StringComparison.OrdinalIgnoreCase));
            rec.Owners.Add(id);
            Paths.CopyFile(Path.Combine(staged, rel), target);
            Stamp(rel, rec);
        }
        mod.Enabled = true;
        mod.NeedsReapply = false;
        Save();
        log?.Invoke($"Activado: {mod.Name} ({mod.Files.Count} archivos)");
    }

    public void Disable(string id, Action<string>? log = null)
    {
        var mod = State.Mods[id];
        foreach (var rel in mod.Files)
        {
            if (!State.Files.TryGetValue(rel, out var rec)) continue;
            var wasTop = rec.Owners.Count > 0 && rec.Owners[^1].Equals(id, StringComparison.OrdinalIgnoreCase);
            rec.Owners.RemoveAll(o => o.Equals(id, StringComparison.OrdinalIgnoreCase));
            var target = game.Abs(rel);
            if (rec.Owners.Count > 0)
            {
                if (wasTop)
                {
                    Paths.CopyFile(Path.Combine(StagedPath(rec.Owners[^1]), rel), target);
                    Stamp(rel, rec);
                }
                continue;
            }

            var backup = BackupPath(rel);
            if (rec.OriginalExisted && File.Exists(backup))
            {
                Paths.CopyFile(backup, target);
                File.Delete(backup);
                Paths.TryDeleteEmptyParents(backup, BackupDir);
            }
            else if (!rec.OriginalExisted && File.Exists(target))
            {
                File.Delete(target);
                Paths.TryDeleteEmptyParents(target, game.Root);
            }
            State.Files.Remove(rel);
        }
        mod.Enabled = false;
        mod.NeedsReapply = false;
        Save();
        log?.Invoke($"Desactivado: {mod.Name}");
    }

    public void Uninstall(string id, Action<string>? log = null)
    {
        if (!State.Mods.TryGetValue(id, out var mod)) return;
        if (mod.Enabled) Disable(id);
        var staged = StagedPath(id);
        if (Directory.Exists(staged)) Directory.Delete(staged, true);
        State.Mods.Remove(id);
        Save();
        log?.Invoke($"Desinstalado: {mod.Name}");
    }

    string BackupPath(string rel) => Path.Combine(BackupDir, rel.Replace('/', Path.DirectorySeparatorChar));

    void Stamp(string rel, FileRecord rec)
    {
        var info = new FileInfo(game.Abs(rel));
        rec.Size = info.Length;
        rec.WriteTicks = info.LastWriteTimeUtc.Ticks;
    }

    // Steam reemplaza los archivos modificados cuando actualiza o verifica el juego.
    // Si eso paso, el archivo actual pasa a ser el nuevo original y el mod queda para reaplicar.
    public int DetectExternalChanges()
    {
        var changed = 0;
        foreach (var (rel, rec) in State.Files)
        {
            if (rec.Owners.Count == 0) continue;
            var info = new FileInfo(game.Abs(rel));
            var same = info.Exists && info.Length == rec.Size && info.LastWriteTimeUtc.Ticks == rec.WriteTicks;
            if (same) continue;
            changed++;
            if (info.Exists)
            {
                Paths.CopyFile(info.FullName, BackupPath(rel));
                rec.OriginalExisted = true;
            }
            if (State.Mods.TryGetValue(rec.Owners[^1], out var top)) top.NeedsReapply = true;
            rec.Size = -1;
        }
        if (State.GameVersion != game.Version) State.GameVersion = game.Version;
        if (changed > 0) Save();
        return changed;
    }

    public void Reapply(Action<string>? log = null)
    {
        foreach (var mod in State.Mods.Values.Where(m => m.Enabled && m.NeedsReapply).ToList())
        {
            foreach (var rel in mod.Files)
            {
                if (!State.Files.TryGetValue(rel, out var rec) || rec.Owners.Count == 0) continue;
                if (!rec.Owners[^1].Equals(mod.Id, StringComparison.OrdinalIgnoreCase)) continue;
                Paths.CopyFile(Path.Combine(StagedPath(mod.Id), rel), game.Abs(rel));
                Stamp(rel, rec);
            }
            mod.NeedsReapply = false;
            log?.Invoke($"Reaplicado: {mod.Name}");
        }
        Save();
    }

    public void DisableAll(Action<string>? log = null)
    {
        foreach (var mod in State.Mods.Values.Where(m => m.Enabled).Reverse().ToList())
            Disable(mod.Id, log);
    }

    // ---- BepInEx (cargador de plugins que usan los mods de Nexus) ----

    public bool BepInExInstalled =>
        File.Exists(game.Abs("winhttp.dll")) && Directory.Exists(game.Abs("BepInEx/core"));

    public bool BepInExPrepared => Directory.Exists(game.Abs("BepInEx/unhollowed"));

    public async Task InstallBepInExAsync(HttpClient http, Action<string>? log = null, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        log?.Invoke($"Descargando BepInEx {AppInfo.BepInExVersion}...");
        var zip = await Downloader.DownloadAsync(http, AppInfo.BepInExUrl,
            Path.Combine(Settings.CacheDir, "BepInEx_UnityIL2CPP_x64_6.0.0-pre.1.zip"), progress, ct);
        var files = new List<string>();
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var rel = Paths.Normalize(entry.FullName);
                if (!Paths.IsSafeTarget(rel)) continue;
                var target = game.Abs(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
                files.Add(rel);
            }
        }
        Directory.CreateDirectory(game.Abs("BepInEx/plugins"));
        State.BepInEx = new BepInExRecord { Version = AppInfo.BepInExVersion, Files = files };
        Save();
        log?.Invoke($"BepInEx instalado ({files.Count} archivos). La primera vez que abras el juego va a tardar un poco mas.");
    }

    public void UninstallBepInEx(Action<string>? log = null)
    {
        foreach (var mod in State.Mods.Values.Where(m => m.Enabled && m.Requires.Contains("bepinex")).ToList())
            Disable(mod.Id, log);
        var files = State.BepInEx?.Files ?? ["winhttp.dll", "doorstop_config.ini"];
        foreach (var rel in files)
        {
            var path = game.Abs(rel);
            if (File.Exists(path)) File.Delete(path);
        }
        foreach (var dir in new[] { "BepInEx/core", "BepInEx/unhollowed", "BepInEx/cache", "BepInEx/unity-libs", "mono", "dotnet" })
        {
            var path = game.Abs(dir);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        State.BepInEx = null;
        Save();
        log?.Invoke("BepInEx desinstalado. Tus configuraciones en BepInEx/config se conservaron.");
    }
}
