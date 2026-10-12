using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SanabiGestor.Core;

public static class Downloader
{
    public static HttpClient CreateClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SanabiGestor", AppInfo.Version));
        return http;
    }

    public static async Task<string> DownloadAsync(HttpClient http, string url, string destination,
        IProgress<double>? progress = null, CancellationToken ct = default, HttpRequestMessage? request = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(new Uri(url).LocalPath, destination, true);
            progress?.Report(1);
            return destination;
        }
        using var req = request ?? new HttpRequestMessage(HttpMethod.Get, url);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(30));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Error {(int)resp.StatusCode} al descargar {url}");
        var total = resp.Content.Headers.ContentLength ?? -1;
        var tmp = destination + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(cts.Token))
        await using (var dst = File.Create(tmp))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, cts.Token)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        File.Move(tmp, destination, true);
        progress?.Report(1);
        return destination;
    }

    public static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct = default) =>
        url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? await File.ReadAllTextAsync(new Uri(url).LocalPath, ct)
            : await http.GetStringAsync(url, ct);

    public static async Task<byte[]> GetBytesAsync(HttpClient http, string url, CancellationToken ct = default)
    {
        const int max = 8 * 1024 * 1024;
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var path = new Uri(url).LocalPath;
            if (new FileInfo(path).Length > max) throw new InvalidDataException("Miniatura demasiado grande.");
            return await File.ReadAllBytesAsync(path, ct);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Miniatura demasiado grande.");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await input.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + read > max) throw new InvalidDataException("Miniatura demasiado grande.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public static string Sha256(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    public static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Settings.CacheDir, "tmp", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 40)]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void CleanTemp()
    {
        try
        {
            var dir = Path.Combine(Settings.CacheDir, "tmp");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch
        {
            // Si algun archivo esta en uso, se borra la proxima vez.
        }
    }
}
