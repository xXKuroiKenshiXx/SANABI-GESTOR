using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SanabiGestor.Core;

public sealed record ManagerRelease(string Version, string Notes, string Url, string? AssetUrl, string? Sha256, long Size)
{
    public bool CanInstall => AssetUrl != null && Sha256 != null && Size > 0 && AppUpdater.IsPortableExecutable;
}

public sealed class UpdateJob
{
    public string Target { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int ParentId { get; set; }
    public long ParentStarted { get; set; }
    public string Version { get; set; } = "";
}

public static class AppUpdater
{
    const long MaxDownload = 350 * 1024 * 1024;
    public static bool IsPortableExecutable => OperatingSystem.IsWindows()
        ? Path.GetFileName(Platform.ExecutablePath).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
          && !Path.GetFileName(Platform.ExecutablePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
          && !File.Exists(Path.Combine(AppContext.BaseDirectory, "SanabiGestor.dll"))
        : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));

    public static async Task<ManagerRelease?> CheckAsync(HttpClient http, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(req, deadline.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        return ParseRelease(doc.RootElement, OperatingSystem.IsWindows());
    }

    public static ManagerRelease? ParseRelease(JsonElement root, bool windows)
    {
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (!Regex.IsMatch(tag, @"^\d+(\.\d+){1,3}$")) throw new InvalidDataException("Versión de release inválida.");
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        var name = windows ? "SanabiGestor-windows-x64.exe" : "SanabiGestor-x86_64.AppImage";
        string? url = null, hash = null; long size = 0;
        if (root.TryGetProperty("assets", out var assets)) foreach (var a in assets.EnumerateArray())
        {
            if (a.GetProperty("name").GetString() != name) continue;
            var candidate = a.GetProperty("browser_download_url").GetString();
            var digest = a.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            size = a.GetProperty("size").GetInt64();
            if (candidate != null && TrustedAsset(candidate) && digest.StartsWith("sha256:", StringComparison.Ordinal)
                && Regex.IsMatch(digest[7..], "^[a-fA-F0-9]{64}$") && size > 0 && size <= MaxDownload)
            { url = candidate; hash = digest[7..].ToLowerInvariant(); }
        }
        return new(tag, notes, AppInfo.RepoUrl + "/releases/tag/v" + tag, url, hash, size);
    }

    static bool TrustedAsset(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == "https" && u.Host == "github.com" && u.UserInfo.Length == 0
        && u.AbsolutePath.StartsWith($"/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/download/", StringComparison.Ordinal);

    public static async Task<string> PrepareAsync(HttpClient http, ManagerRelease release, IProgress<double> progress, CancellationToken ct)
    {
        if (!release.CanInstall || !TrustedAsset(release.AssetUrl!)) throw new InvalidOperationException("Esta release no tiene un archivo verificado para este formato.");
        var target = Path.GetFullPath(Platform.ExecutablePath);
        var stage = Path.Combine(Path.GetDirectoryName(target)!, ".SanabiGestor-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var payload = Path.Combine(stage, "payload");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            using var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var output = File.Create(payload))
            {
                var buffer = new byte[65536]; long done = 0; int count;
                while ((count = await source.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    done += count;
                    if (done > release.Size || done > MaxDownload) throw new InvalidDataException("La descarga supera el tamaño de la release.");
                    await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                    progress.Report((double)done / release.Size);
                }
                if (done != release.Size) throw new InvalidDataException("Descarga incompleta.");
            }
            VerifyPayload(payload, release.Sha256!, OperatingSystem.IsWindows());
            var helper = Path.Combine(stage, OperatingSystem.IsWindows() ? "helper.exe" : "helper.AppImage");
            File.Copy(target, helper);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(helper, File.GetUnixFileMode(target) | UnixFileMode.UserExecute);
            using var parent = Process.GetCurrentProcess();
            var job = new UpdateJob { Target = target, Sha256 = release.Sha256!, ParentId = parent.Id, ParentStarted = parent.StartTime.ToUniversalTime().Ticks, Version = release.Version };
            File.WriteAllText(Path.Combine(stage, "job.json"), JsonSerializer.Serialize(job, JsonCtx.Default.UpdateJob));
            return stage;
        }
        catch
        {
            // Only this freshly created, exact staging directory is removed.
            Directory.Delete(stage, true);
            throw;
        }
    }

    public static void VerifyPayload(string path, string expected, bool windows)
    {
        if (!Regex.IsMatch(expected, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("SHA-256 inválido.");
        using var file = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(file));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La verificación SHA-256 falló. Se conserva el gestor actual.");
        file.Position = 0; Span<byte> header = stackalloc byte[12];
        if (file.Read(header) < 12 || (windows ? header[0] != 'M' || header[1] != 'Z'
            : header[0] != 0x7f || header[1] != 'E' || header[2] != 'L' || header[3] != 'F' || header[8] != 'A' || header[9] != 'I'))
            throw new InvalidDataException("El archivo no es un ejecutable del gestor para esta plataforma.");
    }

    public static void LaunchHelper(string stage)
    {
        var helper = Path.Combine(stage, OperatingSystem.IsWindows() ? "helper.exe" : "helper.AppImage");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(Path.Combine(stage, "job.json"));
        _ = Process.Start(start) ?? throw new IOException("No se pudo iniciar el actualizador.");
    }

    // Runs before Avalonia/settings/IPC are initialized, from a copy of the old executable.
    public static int ApplyJob(string jobPath)
    {
        string? stage = null, installedTarget = null, resumeTarget = null;
        bool replaced = false;
        try
        {
            jobPath = Path.GetFullPath(jobPath); stage = Path.GetDirectoryName(jobPath)!;
            var helper = Path.GetFullPath(Platform.ExecutablePath);
            if (Path.GetDirectoryName(helper) != stage || Path.GetFileName(jobPath) != "job.json"
                || !Path.GetFileName(stage).StartsWith(".SanabiGestor-update-", StringComparison.Ordinal)) throw new InvalidDataException("Ubicación de actualización inválida.");
            var job = JsonSerializer.Deserialize(File.ReadAllText(jobPath), JsonCtx.Default.UpdateJob) ?? throw new InvalidDataException("Trabajo inválido.");
            var target = Path.GetFullPath(job.Target);
            if (Path.GetDirectoryName(target) != Directory.GetParent(stage)!.FullName || target == helper
                || !File.Exists(target) || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0
                || !target.EndsWith(OperatingSystem.IsWindows() ? ".exe" : ".AppImage", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Destino inválido.");
            try
            {
                using var parent = Process.GetProcessById(job.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == job.ParentStarted && !parent.WaitForExit(60000))
                    throw new IOException("El gestor anterior sigue abierto.");
            }
            catch (ArgumentException) { } // Parent has already exited.
            resumeTarget = target;
            var payload = Path.Combine(stage, "payload");
            VerifyPayload(payload, job.Sha256, OperatingSystem.IsWindows());
            ReplaceExecutable(target, payload, Path.Combine(stage, "previous"));
            installedTarget = target; replaced = true;
            File.WriteAllText(Path.Combine(stage, "result.txt"), "OK " + job.Version);
            StartTarget(target);
            return 0;
        }
        catch (Exception e)
        {
            if (stage != null && Directory.Exists(stage) && Path.GetDirectoryName(Path.GetFullPath(Platform.ExecutablePath)) == stage)
                File.WriteAllText(Path.Combine(stage, "result.txt"), "ERROR " + e.Message);
            if (replaced && installedTarget != null && stage != null)
            {
                try
                {
                    File.Move(installedTarget, Path.Combine(stage, "failed"));
                    File.Move(Path.Combine(stage, "previous"), installedTarget);
                }
                catch { } // The previous executable remains in the staging directory for recovery.
            }
            if (resumeTarget != null) try { StartTarget(resumeTarget); } catch { }
            return 1;
        }
    }

    public static void ReplaceExecutable(string target, string payload, string backup)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(payload, File.GetUnixFileMode(target) | UnixFileMode.UserExecute);
        File.Move(target, backup); // Backup and payload stay on the same filesystem.
        try { File.Move(payload, target); }
        catch { File.Move(backup, target); throw; }
    }

    static void StartTarget(string target)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        // A new AppImage must set its own mount/runtime environment.
        foreach (var key in new[] { "APPIMAGE", "APPDIR", "ARGV0", "OWD" }) start.Environment.Remove(key);
        _ = Process.Start(start) ?? throw new IOException("Actualización instalada; abrí el gestor nuevamente.");
    }
}
