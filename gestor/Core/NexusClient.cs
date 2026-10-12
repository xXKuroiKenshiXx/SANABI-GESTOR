using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SanabiGestor.Core;

public sealed record NexusMod(int ModId, string Name, string Summary, string Author, string Version,
    int Downloads, int Endorsements, string? PictureUrl, DateTimeOffset UpdatedAt, bool Adult)
{
    public string Url => $"https://www.nexusmods.com/{AppInfo.NexusGameDomain}/mods/{ModId}";
}

public sealed record NexusFile(int FileId, string Name, string Version, string Category, long SizeBytes,
    DateTimeOffset Date, string Description, string FileName);

public sealed record NexusUser(string Name, bool IsPremium);

public sealed record NxmLink(string Game, int ModId, int FileId, string? Key, string? Expires)
{
    public static NxmLink? Parse(string url)
    {
        var m = Regex.Match(url, @"^nxm://([^/]+)/mods/(\d+)/files/(\d+)(\?.*)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var query = m.Groups[4].Value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.OrdinalIgnoreCase);
        return new NxmLink(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
            query.GetValueOrDefault("key"), query.GetValueOrDefault("expires"));
    }
}

// API publica de Nexus Mods: GraphQL v2 (sin login) para listar y REST v1 (con API key) para descargar.
public sealed class NexusClient(HttpClient http)
{
    const string GraphQl = "https://api.nexusmods.com/v2/graphql";
    const string Rest = "https://api.nexusmods.com/v1";

    public string? ApiKey { get; set; }

    static NexusClient()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    async Task<JsonNode> QueryAsync(string query, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, GraphQl)
        {
            Content = new StringContent(new JsonObject { ["query"] = query }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddAppHeaders(req);
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Nexus respondio {(int)resp.StatusCode}");
        var node = JsonNode.Parse(body)!;
        if (node["errors"] is JsonArray { Count: > 0 } errors && node["data"] is null)
            throw new HttpRequestException("Nexus: " + errors[0]?["message"]);
        return node["data"]!;
    }

    void AddAppHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("Application-Name", "SanabiGestor");
        req.Headers.TryAddWithoutValidation("Application-Version", AppInfo.Version);
        if (!string.IsNullOrEmpty(ApiKey)) req.Headers.TryAddWithoutValidation("apikey", ApiKey);
    }

    public async Task<List<NexusMod>> ListModsAsync(CancellationToken ct = default)
    {
        var data = await QueryAsync($$"""
            query {
              mods(filter: { gameDomainName: { value: "{{AppInfo.NexusGameDomain}}", op: EQUALS } }, count: 100) {
                nodes { modId name summary author version downloads endorsements pictureUrl updatedAt adultContent }
              }
            }
            """, ct);
        var result = new List<NexusMod>();
        foreach (var n in data["mods"]?["nodes"]?.AsArray() ?? [])
        {
            if (n is null) continue;
            result.Add(new NexusMod(
                n["modId"]!.GetValue<int>(),
                FixText(n["name"]?.GetValue<string>()),
                FixText(n["summary"]?.GetValue<string>()),
                FixText(n["author"]?.GetValue<string>()),
                n["version"]?.GetValue<string>() ?? "",
                n["downloads"]?.GetValue<int>() ?? 0,
                n["endorsements"]?.GetValue<int>() ?? 0,
                n["pictureUrl"]?.GetValue<string>(),
                DateTimeOffset.TryParse(n["updatedAt"]?.GetValue<string>(), out var d) ? d : DateTimeOffset.MinValue,
                n["adultContent"]?.GetValue<bool>() ?? false));
        }
        return result.OrderByDescending(m => m.Downloads).ToList();
    }

    public async Task<List<NexusFile>> ListFilesAsync(int modId, CancellationToken ct = default)
    {
        var data = await QueryAsync($$"""
            query { modFiles(modId: {{modId}}, gameId: {{AppInfo.NexusGameId}}) {
              fileId name version category sizeInBytes date description uri } }
            """, ct);
        var files = new List<NexusFile>();
        foreach (var n in data["modFiles"]?.AsArray() ?? [])
        {
            if (n is null) continue;
            var category = n["category"]?.GetValue<string>() ?? "";
            if (category is "ARCHIVED" or "DELETED" or "REMOVED") continue;
            files.Add(new NexusFile(
                n["fileId"]!.GetValue<int>(),
                FixText(n["name"]?.GetValue<string>()),
                n["version"]?.GetValue<string>() ?? "",
                category,
                long.TryParse(n["sizeInBytes"]?.ToString(), out var size) ? size : 0,
                DateTimeOffset.FromUnixTimeSeconds(n["date"]?.GetValue<long>() ?? 0),
                StripHtml(FixText(n["description"]?.GetValue<string>())),
                n["uri"]?.GetValue<string>() ?? $"nexus-{modId}-{n["fileId"]}.zip"));
        }
        return files.OrderBy(f => f.Category == "MAIN" ? 0 : 1).ThenByDescending(f => f.Date).ToList();
    }

    public async Task<NexusUser> ValidateAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Rest}/users/validate.json");
        AddAppHeaders(req);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException("La API key de Nexus no es valida.");
        var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
        return new NexusUser(node["name"]?.GetValue<string>() ?? "?", node["is_premium"]?.GetValue<bool>() ?? false);
    }

    // Sin Premium, Nexus solo entrega el enlace si viene la clave del boton "Mod Manager Download" (nxm://).
    public async Task<string> DownloadFileAsync(int modId, int fileId, string fileName, NxmLink? nxm,
        IProgress<double> progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ApiKey)) throw new InvalidOperationException("Falta la API key de Nexus (Ajustes).");
        var url = $"{Rest}/games/{AppInfo.NexusGameDomain}/mods/{modId}/files/{fileId}/download_link.json";
        if (nxm?.Key != null) url += $"?key={Uri.EscapeDataString(nxm.Key)}&expires={Uri.EscapeDataString(nxm.Expires ?? "")}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAppHeaders(req);
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = JsonNode.Parse(body)?["message"]?.GetValue<string>() ?? $"error {(int)resp.StatusCode}";
            throw new HttpRequestException($"Nexus no dio el enlace de descarga: {msg}");
        }
        var links = JsonNode.Parse(body)!.AsArray();
        var uri = links.FirstOrDefault()?["URI"]?.GetValue<string>() ?? throw new HttpRequestException("Nexus no devolvio enlaces.");
        var safeName = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));
        return await Downloader.DownloadAsync(http, uri, Path.Combine(Settings.CacheDir, "nexus", safeName), progress, ct);
    }

    public static string FilesPageUrl(int modId, int fileId) =>
        $"https://www.nexusmods.com/{AppInfo.NexusGameDomain}/mods/{modId}?tab=files&file_id={fileId}&nmm=1";

    public const string ApiKeyPage = "https://next.nexusmods.com/settings/api-keys";

    // Login sin copiar claves (requiere que la app este registrada en Nexus: AppInfo.NexusAppSlug).
    public static async Task<string> SsoLoginAsync(Action<string> openBrowser, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(AppInfo.NexusAppSlug)) throw new InvalidOperationException("SSO no configurado.");
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri("wss://sso.nexusmods.com"), ct);
        var id = Guid.NewGuid().ToString();
        var hello = new JsonObject { ["id"] = id, ["token"] = null, ["protocol"] = 2 }.ToJsonString();
        await ws.SendAsync(Encoding.UTF8.GetBytes(hello), WebSocketMessageType.Text, true, ct);
        openBrowser($"https://www.nexusmods.com/sso?id={id}&application={AppInfo.NexusAppSlug}");
        var buffer = new byte[8192];
        while (ws.State == WebSocketState.Open)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buffer, ct);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            } while (!r.EndOfMessage);
            var msg = JsonNode.Parse(sb.ToString());
            if (msg?["success"]?.GetValue<bool>() != true) continue;
            var key = msg["data"]?["api_key"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(key)) return key;
        }
        throw new OperationCanceledException();
    }

    // Algunos textos de Nexus llegan con UTF-8 leido como Windows-1252 ("ì‚°ë‚˜ë¹„").
    public static string FixText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!Regex.IsMatch(text, "[ÃÂâìëêíîïð][\u0080-ÿŒ-Ÿ‘-›]")) return text;
        try
        {
            var bytes = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback).GetBytes(text);
            // Algunos bytes ya se perdieron como '?', asi que se decodifica sin ser estricto.
            return Encoding.UTF8.GetString(bytes).Replace("�", "");
        }
        catch
        {
            return text;
        }
    }

    static string StripHtml(string text) =>
        Regex.Replace(Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", "").Trim();
}
