using System.Text.Json;

namespace SanabiGestor.Core;

// Catalogo oficial: catalog.json en la raiz del repo, generado a partir de mods/*/mod.json.
public sealed class CatalogClient(HttpClient http)
{
    public async Task<Catalog> FetchAsync(CancellationToken ct = default)
    {
        var json = await Downloader.GetStringAsync(http, Fresh(AppInfo.RawUrl("catalog.json")), ct);
        var catalog = JsonSerializer.Deserialize(json, JsonCtx.Default.Catalog) ?? new Catalog();
        File.WriteAllText(Path.Combine(Settings.CacheDir, "catalog.json"), json);
        return catalog;
    }

    public async Task<CommunityList> FetchCommunityAsync(CancellationToken ct = default)
    {
        var cache = Path.Combine(Settings.CacheDir, "github-mods.json");
        try
        {
            var json = await Downloader.GetStringAsync(http, Fresh(AppInfo.RawUrl("github-mods.json")), ct);
            var list = JsonSerializer.Deserialize(json, JsonCtx.Default.CommunityList) ?? new CommunityList();
            File.WriteAllText(cache, json);
            return list;
        }
        catch when (File.Exists(cache))
        {
            return JsonSerializer.Deserialize(File.ReadAllText(cache), JsonCtx.Default.CommunityList) ?? new CommunityList();
        }
    }

    // Evita la cache de raw.githubusercontent.com (dura unos minutos).
    static string Fresh(string url) => url.StartsWith("file:") ? url : url + $"?t={DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute}";

    public static Catalog? Cached()
    {
        try
        {
            var path = Path.Combine(Settings.CacheDir, "catalog.json");
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.Catalog) : null;
        }
        catch
        {
            return null;
        }
    }

    public static Catalog? LocalOrCached()
    {
        try
        {
            if (AppInfo.LocalRepo is { } repo)
            {
                var path = Path.Combine(repo, "catalog.json");
                if (File.Exists(path)) return JsonSerializer.Deserialize(File.ReadAllText(path), JsonCtx.Default.Catalog);
            }
        }
        catch { }
        return Cached();
    }

    public async Task<StagedMod> DownloadAsync(CatalogEntry entry, Action<string> log, IProgress<double> progress,
        CancellationToken ct = default)
    {
        var staging = Downloader.NewTempDir("c");
        var total = Math.Max(1, entry.Files.Sum(f => f.Size));
        long done = 0;
        foreach (var file in entry.Files)
        {
            if (!Paths.IsSafeTarget(file.Dst)) throw new InvalidOperationException($"Ruta no permitida en el catalogo: {file.Dst}");
            var dest = Path.Combine(staging, file.Dst.Replace('/', Path.DirectorySeparatorChar));
            log($"Descargando {Path.GetFileName(file.Dst)}...");
            var baseDone = done;
            await Downloader.DownloadAsync(http, entry.AssetUrl(file.Src), dest,
                new Progress<double>(p => progress.Report((baseDone + p * file.Size) / total)), ct);
            if (!Downloader.Sha256(dest).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"El archivo {file.Dst} llego corrupto (hash distinto). Proba de nuevo.");
            done += file.Size;
            progress.Report((double)done / total);
        }

        var manifest = new ModManifest
        {
            Id = entry.Id,
            Name = entry.Name,
            Version = entry.Version,
            Author = entry.Author,
            Description = entry.Description,
            Category = entry.Category,
            Tags = entry.Tags,
            Requires = entry.Requires.ToList(),
            GameVersion = entry.GameVersion,
            Homepage = entry.Homepage,
        };
        string? previewPath = null;
        if (entry.Preview is { } preview)
        {
            try
            {
                var bytes = await Downloader.GetBytesAsync(http, entry.AssetUrl(preview), ct);
                if (bytes.Length <= 8 * 1024 * 1024)
                {
                    previewPath = Path.Combine(Downloader.NewTempDir("p"), "preview.png");
                    await File.WriteAllBytesAsync(previewPath, bytes, ct);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* A missing cover must not prevent installation. */ }
        }
        return new StagedMod { Manifest = manifest, Dir = staging, Source = "catalogo", SourceRef = entry.Version, PreviewPath = previewPath };
    }
}
