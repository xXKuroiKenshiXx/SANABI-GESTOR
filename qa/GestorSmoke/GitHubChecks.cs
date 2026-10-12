using System.Net;
using System.Text.Json.Nodes;
using SanabiGestor.Core;

static class GitHubChecks
{
    public static void Run(string root)
    {
        var handler = new FakeGitHub();
        var client = new GitHubClient(new HttpClient(handler)) { Token = "test-credential" };
        var repos = client.SearchAsync("", ["known/excluded"]).GetAwaiter().GetResult();
        if (!repos.Any(r => r.FullName == "community/new-sanabi-mod") || repos.Any(r => r.FullName == "minecraft/sanabi-inspired"))
            throw new Exception("GitHub discovery must include new paginated projects and exclude other games.");
        var stage = Path.Combine(root, "mods", "_qa", "github-submit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage, "BepInEx", "plugins"));
        File.WriteAllBytes(Path.Combine(stage, "BepInEx", "plugins", "Test.dll"), [77,90]);
        try
        {
            var url = client.SubmitModAsync(new ModManifest { Id="example-mod", Name="Example", Version="1.0.0", Author="Test" },
                stage, null, "Test", _ => {}, new Progress<double>()).GetAwaiter().GetResult();
            if (url != AppInfo.RepoUrl + "/pull/123" || !handler.UploadedTree || !handler.OpenedPr)
                throw new Exception("Submission must package mods/id/files, manifest, branch and PR to the configured repository.");
        }
        finally { Directory.Delete(stage, true); }
        Console.WriteLine("PASS: paginated discovery of new community mods and automated mod submission (isolated fake API).");
    }

    sealed class FakeGitHub : HttpMessageHandler
    {
        public bool UploadedTree, OpenedPr;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "api.github.com") throw new Exception("Unexpected API origin.");
            var path = uri.AbsolutePath;
            JsonNode result;
            if (path == "/search/repositories")
            {
                var items = new JsonArray();
                if (uri.Query.Contains("page=1"))
                    for (var i=0;i<50;i++) items.Add(new JsonObject { ["full_name"]=$"community/sanabi-plugin-{i}", ["description"]="SANABI mod", ["html_url"]=$"https://github.com/community/sanabi-plugin-{i}" });
                else
                {
                    items.Add(new JsonObject { ["full_name"]="community/new-sanabi-mod", ["description"]="New SANABI mod", ["html_url"]="https://github.com/community/new-sanabi-mod" });
                    items.Add(new JsonObject { ["full_name"]="minecraft/sanabi-inspired", ["description"]="Minecraft mod inspired by SANABI", ["html_url"]="https://github.com/minecraft/sanabi-inspired" });
                }
                result = new JsonObject { ["items"] = items };
            }
            else if (path == "/user") result = new JsonObject { ["login"] = AppInfo.RepoOwner };
            else if (path.EndsWith("/git/ref/heads/main")) result = new JsonObject { ["object"] = new JsonObject { ["sha"]="base" } };
            else if (path.EndsWith("/git/commits/base")) result = new JsonObject { ["tree"] = new JsonObject { ["sha"]="base-tree" } };
            else if (path.Contains("/git/ref/heads/mod/")) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content=new StringContent("{}") };
            else if (path.EndsWith("/git/trees"))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                var paths = body["tree"]!.AsArray().Select(n => n!["path"]!.GetValue<string>()).ToArray();
                UploadedTree = paths.Contains("mods/example-mod/mod.json") && paths.Contains("mods/example-mod/files/BepInEx/plugins/Test.dll") && paths.All(p=>p.StartsWith("mods/example-mod/"));
                result = new JsonObject { ["sha"]="tree" };
            }
            else if (path.EndsWith("/pulls"))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
                OpenedPr = path == $"/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/pulls" && body["base"]!.GetValue<string>() == "main" && body["head"]!.GetValue<string>().StartsWith("mod/");
                result = new JsonObject { ["html_url"]=AppInfo.RepoUrl+"/pull/123" };
            }
            else result = new JsonObject { ["sha"]="object" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(result.ToJsonString()) };
        }
    }
}
