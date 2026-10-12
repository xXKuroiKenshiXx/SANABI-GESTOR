using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SanabiGestor.Core;

public sealed record GitHubRepo(string FullName, string Description, string HtmlUrl, int Stars, DateTimeOffset UpdatedAt);

public sealed record GitHubAsset(string Name, string Url, long Size, string Tag);

public sealed class GitHubClient(HttpClient http)
{
    const string Api = "https://api.github.com";
    public const long MaxFileSize = 95L * 1024 * 1024;

    public string? Token { get; set; }

    HttpRequestMessage Request(HttpMethod method, string url, JsonNode? body = null)
    {
        var req = new HttpRequestMessage(method, url.StartsWith("http") ? url : Api + url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(Token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return req;
    }

    async Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonNode? body = null, CancellationToken ct = default,
        bool allowNotFound = false)
    {
        using var req = Request(method, url, body);
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (allowNotFound && resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
        {
            var msg = "";
            try { msg = JsonNode.Parse(text)?["message"]?.GetValue<string>() ?? ""; } catch { }
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden && msg.Contains("rate limit"))
                msg = "GitHub limita las busquedas sin sesion; espera un minuto o inicia sesion.";
            throw new HttpRequestException($"GitHub ({(int)resp.StatusCode}): {msg}");
        }
        return text.Length == 0 ? null : JsonNode.Parse(text);
    }

    // ---- Buscar mods de SANABI publicados en otros repos ----

    public async Task<List<GitHubRepo>> SearchAsync(string text, ICollection<string> exclude, CancellationToken ct = default)
    {
        var queries = new[]
        {
            "topic:sanabi-mod",
            "sanabi mod in:name,description,topics fork:false",
            "sanabi bepinex in:name,description,readme fork:false",
        };
        string[] modWords = ["mod", "bepinex", "plugin", "patch", "harmony"];
        string[] otherGames = ["minecraft", "rimworld", "mindustry", "isaac", "fabric", "forge", "ss14", "terraria"];
        var result = new Dictionary<string, GitHubRepo>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in queries)
        {
            for (var page = 1; page <= 3; page++)
            {
            var node = await SendAsync(HttpMethod.Get, $"/search/repositories?per_page=50&sort=updated&page={page}&q={Uri.EscapeDataString(q)}", ct: ct);
            var items = node?["items"]?.AsArray() ?? [];
            foreach (var item in items)
            {
                var full = item!["full_name"]!.GetValue<string>();
                if (full.Equals($"{AppInfo.RepoOwner}/{AppInfo.RepoName}", StringComparison.OrdinalIgnoreCase)
                    || exclude.Contains(full)) continue;
                var desc = item["description"]?.GetValue<string>() ?? "";
                var topics = string.Join(' ', (item["topics"]?.AsArray() ?? []).Select(t => t?.GetValue<string>()));
                var haystack = $"{full} {desc} {topics}".ToLowerInvariant();
                var tagged = topics.Contains("sanabi-mod", StringComparison.OrdinalIgnoreCase);
                // Solo mods de SANABI: nada de repos que se llaman igual ni mods de otros juegos inspirados en SANABI.
                if (!tagged && (!haystack.Contains("sanabi") || !modWords.Any(haystack.Contains) || otherGames.Any(haystack.Contains))) continue;
                if (!string.IsNullOrWhiteSpace(text) && !haystack.Contains(text.ToLowerInvariant())) continue;
                result[full] = new GitHubRepo(full, desc, item["html_url"]!.GetValue<string>(),
                    item["stargazers_count"]?.GetValue<int>() ?? 0,
                    DateTimeOffset.TryParse(item["updated_at"]?.GetValue<string>(), out var d) ? d : default);
            }
            if (items.Count < 50) break;
            }
        }
        return result.Values.OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Stars).ToList();
    }

    public async Task<List<GitHubAsset>> ReleaseAssetsAsync(string fullName, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, $"/repos/{fullName}/releases?per_page=5", ct: ct);
        foreach (var rel in node?.AsArray() ?? [])
        {
            if (rel!["draft"]?.GetValue<bool>() == true) continue;
            var tag = rel["tag_name"]?.GetValue<string>() ?? "";
            var assets = (rel["assets"]?.AsArray() ?? [])
                .Select(a => new GitHubAsset(a!["name"]!.GetValue<string>(), a["browser_download_url"]!.GetValue<string>(),
                    a["size"]?.GetValue<long>() ?? 0, tag))
                .Where(a => ArchiveImporter.IsArchive(a.Name) || a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (assets.Count > 0) return assets;
        }
        return new();
    }

    public static string RawFileUrl(string fullName, string path) =>
        $"https://raw.githubusercontent.com/{fullName}/HEAD/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public async Task<string?> ReadmeAsync(string fullName, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, $"/repos/{fullName}/readme", ct: ct, allowNotFound: true);
        if (node == null) return null;
        var content = node["content"]?.GetValue<string>();
        if (content == null || node["encoding"]?.GetValue<string>() != "base64") return null;
        if (content.Length > 1_400_000) throw new InvalidDataException("El README supera el límite de lectura (1 MB).");
        return Encoding.UTF8.GetString(Convert.FromBase64String(content));
    }

    // Miniatura: la primera imagen del README (null si no tiene; la tarjeta muestra el nombre).
    public async Task<string?> PreviewUrlAsync(string fullName, CancellationToken ct = default)
    {
        try
        {
            var readme = await http.GetStringAsync(RawFileUrl(fullName, "README.md"), ct);
            var m = System.Text.RegularExpressions.Regex.Match(readme,
                @"!\[[^\]]*\]\(\s*<?([^)\s>]+)|<img[^>]+src=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var src = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value;
                if (!src.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && !src.Contains("shields.io"))
                {
                    if (src.StartsWith("http")) return src.Replace("github.com/" + fullName + "/blob/", "raw.githubusercontent.com/" + fullName + "/");
                    return RawFileUrl(fullName, src.TrimStart('.', '/'));
                }
            }
        }
        catch
        {
            // Sin README: usamos la tarjeta.
        }
        return null;
    }

    // Archivos del repo que se pueden instalar sin compilar: paquetes .zip/.7z o archivos dentro de BepInEx/plugins.
    public async Task<List<string>> InstallableFilesAsync(string fullName, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, $"/repos/{fullName}/git/trees/HEAD?recursive=1", ct: ct);
        var result = new List<string>();
        foreach (var t in node?["tree"]?.AsArray() ?? [])
        {
            if (t?["type"]?.GetValue<string>() != "blob") continue;
            var path = t["path"]!.GetValue<string>();
            var lower = "/" + path.ToLowerInvariant();
            if (lower.Contains("/lib/") || lower.Contains("/obj/") || lower.Contains("/packages/") || lower.Contains("/dlls/")) continue;
            if (ArchiveImporter.IsArchive(path) || lower.Contains("/bepinex/plugins/")) result.Add(path);
        }
        return result;
    }

    // ---- Login (flujo de dispositivo: el usuario pega un codigo corto en github.com) ----

    public static bool DeviceFlowAvailable => !string.IsNullOrEmpty(AppInfo.GitHubClientId);

    public async Task<GitHubDeviceCode> StartDeviceFlowAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = AppInfo.GitHubClientId,
                ["scope"] = "public_repo",
            }),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize(await resp.Content.ReadAsStringAsync(ct), JsonCtx.Default.GitHubDeviceCode)!;
    }

    public async Task<string> PollDeviceFlowAsync(GitHubDeviceCode code, CancellationToken ct)
    {
        var interval = Math.Max(5, code.Interval);
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = AppInfo.GitHubClientId,
                    ["device_code"] = code.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }),
            };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await http.SendAsync(req, ct);
            var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            var token = node?["access_token"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(token)) return token;
            switch (node?["error"]?.GetValue<string>())
            {
                case "slow_down": interval += 5; break;
                case "authorization_pending": break;
                case "expired_token": throw new TimeoutException("El codigo vencio, proba de nuevo.");
                case "access_denied": throw new OperationCanceledException("Se cancelo el inicio de sesion.");
            }
        }
        throw new TimeoutException("El codigo vencio, proba de nuevo.");
    }

    public async Task<string> GetLoginAsync(CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, "/user", ct: ct);
        return node!["login"]!.GetValue<string>();
    }

    // ---- Subir un mod: crea una rama con los archivos y abre un pull request para revision ----

    public async Task<string> SubmitModAsync(ModManifest manifest, string stagedDir, string? previewPng, string notes,
        Action<string> log, IProgress<double> progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Token)) throw new InvalidOperationException("Primero inicia sesion con GitHub.");
        var upstream = $"{AppInfo.RepoOwner}/{AppInfo.RepoName}";
        var login = await GetLoginAsync(ct);
        var isOwner = login.Equals(AppInfo.RepoOwner, StringComparison.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(stagedDir, "*", SearchOption.AllDirectories)
            .Select(f => (Abs: f, Rel: Paths.Normalize(Path.GetRelativePath(stagedDir, f))))
            .Where(f => !f.Rel.Equals(Paths.ManifestName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var tooBig = files.FirstOrDefault(f => new FileInfo(f.Abs).Length > MaxFileSize);
        if (tooBig.Abs != null)
            throw new InvalidOperationException($"{tooBig.Rel} pesa mas de 95 MB; GitHub no acepta archivos tan grandes.");

        string target = upstream;
        if (!isOwner)
        {
            log("Creando tu copia (fork) del repositorio...");
            var fork = await SendAsync(HttpMethod.Post, $"/repos/{upstream}/forks",
                new JsonObject { ["default_branch_only"] = true }, ct);
            target = fork!["full_name"]!.GetValue<string>();
            for (var i = 0; i < 30; i++)
            {
                if (await SendAsync(HttpMethod.Get, $"/repos/{target}/branches/{AppInfo.RepoBranch}", ct: ct, allowNotFound: true) != null) break;
                await Task.Delay(2000, ct);
            }
            try
            {
                await SendAsync(HttpMethod.Post, $"/repos/{target}/merge-upstream",
                    new JsonObject { ["branch"] = AppInfo.RepoBranch }, ct);
            }
            catch (HttpRequestException e)
            {
                Log.Write($"merge-upstream: {e.Message}");
            }
        }

        var head = await SendAsync(HttpMethod.Get, $"/repos/{target}/git/ref/heads/{AppInfo.RepoBranch}", ct: ct);
        var baseSha = head!["object"]!["sha"]!.GetValue<string>();
        var baseCommit = await SendAsync(HttpMethod.Get, $"/repos/{target}/git/commits/{baseSha}", ct: ct);
        var baseTree = baseCommit!["tree"]!["sha"]!.GetValue<string>();

        var folder = $"mods/{manifest.Id}";
        var tree = new JsonArray();
        var clean = new ModManifest
        {
            Id = manifest.Id, Name = manifest.Name, Version = manifest.Version, Author = manifest.Author,
            Description = manifest.Description, Category = manifest.Category, Tags = manifest.Tags,
            Requires = manifest.Requires, GameVersion = manifest.GameVersion, Homepage = manifest.Homepage,
            Preview = previewPng != null ? "preview.png" : null,
        };
        var uploads = files.Select(f => (f.Abs, Path: $"{folder}/files/{f.Rel}")).ToList();
        if (previewPng != null) uploads.Add((previewPng, $"{folder}/preview.png"));

        var total = Math.Max(1, uploads.Sum(u => new FileInfo(u.Abs).Length));
        long done = 0;
        foreach (var (abs, path) in uploads)
        {
            log($"Subiendo {Path.GetFileName(abs)}...");
            var blob = await SendAsync(HttpMethod.Post, $"/repos/{target}/git/blobs", new JsonObject
            {
                ["content"] = Convert.ToBase64String(await File.ReadAllBytesAsync(abs, ct)),
                ["encoding"] = "base64",
            }, ct);
            tree.Add(new JsonObject { ["path"] = path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = blob!["sha"]!.GetValue<string>() });
            done += new FileInfo(abs).Length;
            progress.Report((double)done / total);
        }
        tree.Add(new JsonObject
        {
            ["path"] = $"{folder}/mod.json", ["mode"] = "100644", ["type"] = "blob",
            ["content"] = JsonSerializer.Serialize(clean, JsonCtx.Default.ModManifest) + "\n",
        });

        log("Creando el commit...");
        var newTree = await SendAsync(HttpMethod.Post, $"/repos/{target}/git/trees",
            new JsonObject { ["base_tree"] = baseTree, ["tree"] = tree }, ct);
        var commit = await SendAsync(HttpMethod.Post, $"/repos/{target}/git/commits", new JsonObject
        {
            ["message"] = $"Mod: {manifest.Name} {manifest.Version}",
            ["tree"] = newTree!["sha"]!.GetValue<string>(),
            ["parents"] = new JsonArray(baseSha),
        }, ct);
        var commitSha = commit!["sha"]!.GetValue<string>();
        var branch = $"mod/{manifest.Id}-{manifest.Version}";
        var existing = await SendAsync(HttpMethod.Get, $"/repos/{target}/git/ref/heads/{branch}", ct: ct, allowNotFound: true);
        if (existing == null)
            await SendAsync(HttpMethod.Post, $"/repos/{target}/git/refs",
                new JsonObject { ["ref"] = $"refs/heads/{branch}", ["sha"] = commitSha }, ct);
        else
            await SendAsync(HttpMethod.Patch, $"/repos/{target}/git/refs/heads/{branch}",
                new JsonObject { ["sha"] = commitSha, ["force"] = true }, ct);

        log("Abriendo el pull request...");
        var body = new StringBuilder()
            .AppendLine($"**{manifest.Name}** v{manifest.Version} por {manifest.Author}")
            .AppendLine()
            .AppendLine(manifest.Description)
            .AppendLine()
            .AppendLine($"- Categoria: {manifest.Category}")
            .AppendLine($"- Version del juego: {manifest.GameVersion ?? "sin indicar"}")
            .AppendLine($"- Requiere: {(manifest.Requires.Count > 0 ? string.Join(", ", manifest.Requires) : "nada")}")
            .AppendLine($"- Archivos: {files.Count}")
            .AppendLine();
        if (!string.IsNullOrWhiteSpace(notes)) body.AppendLine(notes).AppendLine();
        body.AppendLine("Enviado desde SANABI Gestor.");

        var headRef = isOwner ? branch : $"{target.Split('/')[0]}:{branch}";
        try
        {
            var pr = await SendAsync(HttpMethod.Post, $"/repos/{upstream}/pulls", new JsonObject
            {
                ["title"] = $"Mod: {manifest.Name} {manifest.Version}",
                ["head"] = headRef,
                ["base"] = AppInfo.RepoBranch,
                ["body"] = body.ToString(),
                ["maintainer_can_modify"] = true,
            }, ct);
            return pr!["html_url"]!.GetValue<string>();
        }
        catch (HttpRequestException e) when (e.Message.Contains("422"))
        {
            // Ya habia un PR abierto para esa rama: se actualizo con el nuevo commit.
            var open = await SendAsync(HttpMethod.Get,
                $"/repos/{upstream}/pulls?state=open&head={Uri.EscapeDataString(headRef.Contains(':') ? headRef : $"{AppInfo.RepoOwner}:{branch}")}", ct: ct);
            return open?.AsArray().FirstOrDefault()?["html_url"]?.GetValue<string>() ?? $"{AppInfo.RepoUrl}/pulls";
        }
    }
}
