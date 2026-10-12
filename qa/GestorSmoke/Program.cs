using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SanabiGestor;
using SanabiGestor.Core;
using SanabiGestor.ViewModels;
using SanabiGestor.Views;
using System.Text.Json.Nodes;
using System.Net;
using System.Text;

var root = Path.GetFullPath(args[0]);
Environment.SetEnvironmentVariable("SANABI_GESTOR_REPO", root);
if(args.Length==3 && args[1]=="--updater-helper") { UpdateChecks.RunHelper(root,args[2]); return; }
UpdateChecks.Run(root);
GitHubChecks.Run(root);
var protectedCredential = LocalCredentials.Protect("qa-local-credential");
Require(LocalCredentials.Unprotect(protectedCredential) == "qa-local-credential", "Local credentials round-trip for the OS user");
if (OperatingSystem.IsWindows()) Require(protectedCredential != "qa-local-credential", "Windows credentials are encrypted before persistence");
if (args.Length > 2 && args[1] == "--update-installed-2099")
{
    var gameToUpdate = GameInstall.Open(args[2]) ?? throw new InvalidOperationException("SANABI installation not found");
    if (GameInstall.IsRunning()) throw new InvalidOperationException("Close SANABI before updating");
    var realStore = ModStore.Load(gameToUpdate);
    var installed2099 = realStore.State.Mods.GetValueOrDefault("spider-2099") ?? throw new InvalidOperationException("2099 is not installed");
    if (!installed2099.Enabled) throw new InvalidOperationException("The current 2099 mod is not active");
    var backupHashes = installed2099.Files.ToDictionary(f => f, f => Downloader.Sha256(Path.Combine(gameToUpdate.Root, "SanabiGestor", "backup", f)));
    foreach (var f in installed2099.Files)
    {
        if (realStore.State.Files[f].Owners[^1] != "spider-2099") throw new InvalidOperationException("Another mod is active on " + f);
        if (Downloader.Sha256(gameToUpdate.Abs(f)) != Downloader.Sha256(Path.Combine(realStore.StagedPath("spider-2099"), f)))
            throw new InvalidOperationException("Installed game files changed outside the manager: " + f);
    }
    var entry = CatalogClient.LocalOrCached()!.Mods.Single(e => e.Id == "spider-2099");
    if (entry.Version != "1.2.0") throw new InvalidOperationException("Unexpected update version");
    var client = new CatalogClient(Downloader.CreateClient());
    var staged = await client.DownloadAsync(entry, Console.WriteLine, new Progress<double>(), CancellationToken.None);
    if (GameInstall.IsRunning()) throw new InvalidOperationException("SANABI started during staging");
    realStore.Install(staged, Console.WriteLine);
    foreach (var f in entry.Files)
    {
        if (Downloader.Sha256(gameToUpdate.Abs(f.Dst)) != f.Sha256) throw new InvalidOperationException("Installed hash mismatch");
        if (Downloader.Sha256(Path.Combine(gameToUpdate.Root, "SanabiGestor", "backup", f.Dst)) != backupHashes[f.Dst])
            throw new InvalidOperationException("Original backup changed");
    }
    Console.WriteLine("PASS: active 2099 updated to 1.2.0; original backups preserved.");
    return;
}
AppBuilder.Configure<App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = new MainWindow();
var vm = new MainViewModel(new TestHost(),new HttpClient(new OfflineHandler()));
window.DataContext = vm;
window.Show();
vm.ShowInstalledDetails(new InstalledModViewModel(vm, new InstalledMod { Id = "spider-2099", Name = "Spider-Man 2099", Source = "catalogo" }));
Require(vm.SelectedMod?.Entry?.Id == "spider-2099", "Installed mod resolves full local metadata before catalog navigation");
vm.CloseModDetailsCommand.Execute(null);
vm.Section = 1;
var expectedCount = CatalogClient.LocalOrCached()!.Mods.Count(e => e.Verification != "experimental");
for (var i = 0; i < 500 && (vm.CheckedMods.Count < expectedCount || vm.ModsLoading); i++)
{
    Thread.Sleep(10);
    Dispatcher.UIThread.RunJobs();
}
Require(vm.CheckedMods.Count == expectedCount, "Local catalog loads without remote GitHub");
foreach (var card in vm.CheckedMods)
{
    if (card.Entry?.Preview is { } path) card.Preview = new Bitmap(Path.Combine(root, path));
    Require(card.FileRows.Any(), card.Name + " has files");
    Require(card.InstallationInstructions.Length > 30, card.Name + " has instructions");
}
Dispatcher.UIThread.RunJobs();
var outDir = Path.Combine(root, "mods", "_qa");
Directory.CreateDirectory(outDir);
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "gestor-catalogo.png"));
Dispatcher.UIThread.RunJobs();
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "gestor-catalogo.png"));
Require(window.GetVisualDescendants().OfType<Image>().Count(i => i.Source != null) >= vm.CheckedMods.Count, "Catalog preview images are bound");
var minigun = vm.ExperimentalMods.Single(c => c.Id == "brazo-minigun");
var minigunButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Command == minigun.OpenDetailsCommand && b.IsEffectivelyVisible);
var minigunPoint = minigunButton.TranslatePoint(new Point(12, 8), window)!.Value;
Require(minigunPoint.Y > 0 && minigunPoint.Y < window.Bounds.Height && minigunPoint.X > 0 && minigunPoint.X < window.Bounds.Width, "Minigun is visible immediately in Mods without scrolling");
window.MouseDown(minigunPoint, MouseButton.Left);
window.MouseUp(minigunPoint, MouseButton.Left);
Dispatcher.UIThread.RunJobs();
Require(vm.SelectedMod == minigun && vm.IsModDetailsSection && minigun.FileRows.Any(), "Visible minigun opens installation details");
vm.CloseModDetailsCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
var first = vm.CheckedMods.First(c => c.Id == "capa-blanca");
var titleButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Command == first.OpenDetailsCommand);
titleButton.BringIntoView();
Dispatcher.UIThread.RunJobs();
var point = titleButton.TranslatePoint(new Point(12, 8), window)!.Value;
window.MouseDown(point, MouseButton.Left);
window.MouseUp(point, MouseButton.Left);
Dispatcher.UIThread.RunJobs();
Require(vm.SelectedMod == first && vm.IsModDetailsSection && !vm.IsModsSection, "Card click opens detail subsection");
Require(window.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text == first.InstallationInstructions), "Instructions bound in view");
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "gestor-ficha.png"));
vm.CloseModDetailsCommand.Execute(null);
Dispatcher.UIThread.RunJobs();
Require(vm.IsModsSection && vm.SelectedMod == null, "Back returns to catalog");
var preview = window.GetVisualDescendants().OfType<Image>().First(i => i.DataContext == first);
var imagePoint = preview.TranslatePoint(new Point(100, 70), window)!.Value;
window.MouseDown(imagePoint, MouseButton.Left);
window.MouseUp(imagePoint, MouseButton.Left);
Require(vm.SelectedMod == first, "Clicking the cover opens details");
vm.CloseModDetailsCommand.Execute(null);
first.OpenDetailsCommand.Execute(null);
vm.Section = 4;
Require(vm.SelectedMod == null && vm.IsSettingsSection, "Sidebar clears detail state");

var translation = vm.CheckedMods.Single(c => c.IsTranslation);
vm.Section = 1;
Require(translation.Variants.Count == 5, "Five translation editions are selectable");
translation.Refresh(null, "1.3.53");
Require(!translation.Compatible, "Historical bundles warn on the modern DLC game");
translation.SelectedVariant = translation.Variants.Single(v => v.Version == "2025.4.26");
Require(translation.Version == "v2025.4.26" && translation.EffectiveEntry!.Files.All(f => f.Src.Contains("26-04-2025")), "Selector changes the version and actual download files");
vm.ShowModDetails(translation);
for (var i = 0; i < 200 && translation.ReadmeLoading; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
Require(translation.ReadmeText.Contains("No hay traducción en tiempo real"), "Local README explains dated bundle installation");
Dispatcher.UIThread.RunJobs();
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "gestor-traduccion.png"));
Require(window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.SelectedItem == translation.SelectedVariant), "Selected edition is bound in the detail UI");
vm.CloseModDetailsCommand.Execute(null);
vm.Section = 1;
Dispatcher.UIThread.RunJobs();
var holo = window.GetVisualDescendants().OfType<HolographicCard>().First(c => c.DataContext == minigun && c.IsEffectivelyVisible);
holo.BringIntoView();
Dispatcher.UIThread.RunJobs();
var holoPoint = holo.TranslatePoint(new Point(250, 70), window)!.Value;
window.MouseMove(holoPoint);
using (var pump = new CancellationTokenSource(250)) Dispatcher.UIThread.MainLoop(pump.Token);
var transform = (Avalonia.Media.TransformGroup)holo.RenderTransform!;
Require(((Avalonia.Media.SkewTransform)transform.Children[0]).AngleY != 0, "Pointer movement tilts holographic cards");
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "gestor-hover.png"));
window.MouseMove(new Point(15, 200));
using (var pump = new CancellationTokenSource(1200)) Dispatcher.UIThread.MainLoop(pump.Token);
Require(Math.Abs(((Avalonia.Media.SkewTransform)transform.Children[0]).AngleY) < .01, $"Card returns smoothly to rest (angle={((Avalonia.Media.SkewTransform)transform.Children[0]).AngleY}, over={holo.IsPointerOver})");

var uninstall = vm.UninstallModAsync(new InstalledModViewModel(vm, new InstalledMod { Id = "dialog-only", Name = "Nombre destacado" }));
Dispatcher.UIThread.RunJobs();
window.CaptureRenderedFrame();
Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Nombre destacado" && t.FontSize == 20), "Uninstall dialog shows the mod name as a distinct colored label");
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "gestor-desinstalar.png"));
vm.Dialog!.Close("Cancelar");
Dispatcher.UIThread.RunJobs();
Require(uninstall.IsCompleted, "Canceling uninstall makes no game changes");

// Exercise preferences in the actual visual tree; switching back must update existing labels.
Console.WriteLine("QA: preferences");
var oldTheme=vm.SelectedTheme; var oldLanguage=vm.SelectedLanguage;
vm.Section=4; vm.SelectedTheme=vm.Themes.Single(x=>x.Code=="light"); vm.SelectedLanguage=vm.Languages.Single(x=>x.Code=="en");
Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame();
Require(Application.Current!.RequestedThemeVariant==Avalonia.Styling.ThemeVariant.Light,"Light theme applies immediately");
Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Appearance and language"),"English labels update without restart");
Require(Locale.T("Busqueda en GitHub: network") == "GitHub search: network","Variable status messages preserve the technical detail and translate their label");
Require(Locale.T("visual")=="Environment and effects" && Locale.T("habilidades")=="Abilities","Upload category codes have meaningful translated labels");
Require(window.GetVisualDescendants().OfType<ActionLabel>().Any(a=>a.Text=="Ajustes"),"Sidebar uses vector action icons");
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir,"gestor-claro-ingles.png"));
foreach(var lang in new[]{"zh","ru"})
{
    vm.SelectedLanguage=vm.Languages.Single(x=>x.Code==lang); Dispatcher.UIThread.RunJobs();
    Require(Locale.T("Ajustes") != "Ajustes",lang+" language pack loaded");
    if(lang=="zh")Require(vm.UiFontFamily.Name.Contains("Noto Sans CJK SC"),"Chinese selects the bundled CJK font on every platform");
    window.CaptureRenderedFrame()!.Save(Path.Combine(outDir,"gestor-"+lang+".png"));
}
vm.SelectedLanguage=vm.Languages[0]; vm.SelectedTheme=vm.Themes[0]; Dispatcher.UIThread.RunJobs();
Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Apariencia e idioma"),"Switching back restores Spanish labels");
vm.SelectedLanguage=oldLanguage;vm.SelectedTheme=oldTheme;

var gun=new SanabiMinigun.GunLogic();gun.Toggle(0);
Console.WriteLine("QA: gun timing");
Require(!gun.Tick(.2f,.02f,true,true,12),"Mode change requires trigger release");
gun.Tick(.21f,.02f,false,true,12);Require(gun.Tick(.3f,.02f,true,true,12),"Released trigger enables firing");
Require(!gun.Tick(.32f,.02f,true,true,12),"Rate limits repeated rounds");
var now=.4f;
for(var i=0;i<100 && !gun.Overheated;i++,now+=.1f)gun.Tick(now,.1f,true,true,12);
Require(gun.Overheated && !gun.Tick(now,.1f,true,true,12),"Heat blocks firing");
var heat=gun.Heat;Require(!gun.Tick(now+20,.1f,true,false,12) && gun.Heat==heat,"Pause neither shoots nor cools");
for(var i=0;i<40;i++,now+=.1f)gun.Tick(now,.1f,false,true,12);
Require(!gun.Overheated && gun.Heat<.25f,"Cooling re-enables the gun");
gun.Toggle(now);Require(!gun.Equipped && !gun.Tick(now+1,.1f,true,true,12),"Switching back disables rounds");

Require(!vm.ExperimentalMods.Single(c=>c.Id=="brazo-minigun").IsChecked,"Experimental gun never shows a verified check");
var ledger=new SanabiMinigun.FractionalDamage();
for(var i=0;i<24;i++)Require(ledger.Hit(1,i/12f,.04f)==0,"Tiny bullets cannot round up to 1 HP");
Require(ledger.Hit(1,2,.04f)==1 && ledger.Hit(1,2.1f,.04f)==0,"Exactly 25 impacts apply 1 HP, remainder preserved");
Require(ledger.Hit(2,2,.04f)==0,"Fractional damage is isolated per enemy");
for(var i=0;i<20;i++)ledger.Hit(3,3+i/12f,.04f);
Require(ledger.Hit(3,12,.04f)==0,"Stale partial damage expires after six seconds");
ledger.Clear();Require(ledger.Hit(1,13,.04f)==0,"Scene reset clears accumulated damage");
vm.SelectedCategory=vm.ModCategories.Single(c=>c.Code=="habilidades");
Require(vm.CheckedMods.Count==0 && vm.ExperimentalMods.Count==1,"Ability filter separates actual gameplay plugins from skins");
vm.SelectedCategory=vm.ModCategories.Single(c=>c.Code=="personajes");
Require(vm.ExperimentalMods.Count==2 && vm.ExperimentalMods.All(c=>c.Entry?.Files.All(f=>f.Dst.EndsWith(".dll"))==true),"Real characters are native-controller plugins, separate from appearance bundles");
vm.SelectedCategory=vm.ModCategories.Single(c=>c.Code=="skins");
Require(vm.CheckedMods.All(c=>c.Entry?.Category=="skins") && vm.ExperimentalMods.Count==0 && vm.CategoryHelp.Contains("No añaden"),"Skins explain appearance-only scope");
Require(!window.GetVisualDescendants().OfType<ListBoxItem>().Any(i => i.Content as string == "Experimentales"), "Experimental mods have no separate sidebar page");
vm.CloseModDetailsCommand.Execute(null);
vm.Section=1;
vm.SelectedCategory=vm.ModCategories[0];
Dispatcher.UIThread.RunJobs();
Require(vm.IsModsSection && vm.ExperimentalMods.Count == 3 && vm.HasExperimentalMods, "All experimental mods are grouped inside Mods");
Require(vm.ExperimentalMods.All(c => c.CanInstall && c.InstallCommand.CanExecute(null)), "Experimental cards retain installation actions inside Mods");
var previousSettings=Settings.Load();var previousRead=previousSettings.ReadReleaseVersion;
vm.SetAvailableRelease(new ManagerRelease("99.0.0","# Cambios\n\nNueva versión de prueba.",AppInfo.RepoUrl+"/releases/tag/v99.0.0",AppInfo.RepoUrl+"/releases/download/v99.0.0/SanabiGestor-windows-x64.exe",new string('a',64),100));
Require(vm.HasUnreadNotification && vm.NotificationLabel.EndsWith("1"),"New release produces notification badge");
vm.ShowNotificationsCommand.Execute(null);Dispatcher.UIThread.RunJobs();
Require(vm.IsNotificationsSection && !vm.HasUnreadNotification && vm.HasManagerUpdate,"Reading release clears badge but keeps update available");
window.CaptureRenderedFrame()!.Save(Path.Combine(outDir,"gestor-notificaciones.png"));
vm.SetAvailableRelease(new ManagerRelease("0.1.0","old","",null,null,0));
Require(!vm.HasManagerUpdate,"Older release cannot downgrade manager");
previousSettings.ReadReleaseVersion=previousRead;previousSettings.Save();vm.Section=1;

var convertRoot = Path.Combine(outDir, "translation-test-" + Guid.NewGuid().ToString("N"));
var jsonFolder = Path.Combine(convertRoot, "original");
Directory.CreateDirectory(jsonFolder);
var csv = Path.Combine(convertRoot, "text.csv");
File.WriteAllText(csv, "English;Aa Spanish\n\"Hello, {name}\";\"Hola, {name}\"\n\"Two\nlines\";\"Dos\nlíneas\"\n\"Missing {id}\";\"Falta {otro}\"\n");
var sourceJson = """{"textData":{"values":{"Array":["Hello, {name}","Two\nlines","Missing {id}","Unlisted",42] }},"metadata":{"id":7,"untouched":true}}""";
var sourceFile = Path.Combine(jsonFolder, "story.json");
File.WriteAllText(sourceFile, sourceJson);
File.WriteAllText(Path.Combine(jsonFolder, "invalid.json"), "broken json");
var result = TranslationCompiler.Compile(csv, jsonFolder, convertRoot);
var translated = JsonNode.Parse(File.ReadAllText(Path.Combine(result.Output, "story.json")))!;
Require(result.Replaced == 2 && result.Pending == 2 && result.Invalid == 1, "CSV quoting, multiline, pending tokens and invalid JSON are reported");
Require(translated["textData"]!["values"]!["Array"]![0]!.GetValue<string>() == "Hola, {name}" && translated["metadata"]!["id"]!.GetValue<int>() == 7, "Translation replaces strings and preserves unrelated data");
Require(File.ReadAllText(sourceFile) == sourceJson, "Converter never overwrites source JSON");
File.WriteAllText(csv, "English,Spanish\nHello,Hola\nHello,Chau\n");
var duplicateRejected = false;
try { TranslationCompiler.ReadCsv(csv); } catch (InvalidDataException) { duplicateRejected = true; }
Require(duplicateRejected, "Conflicting duplicate translations are rejected");

var readmeHttp = new HttpClient(new ReadmeHandler());
var readmeClient = new GitHubClient(readmeHttp);
Require(readmeClient.ReadmeAsync("owner/mod").GetAwaiter().GetResult() == "# Mod\n\nCómo instalar.", "GitHub README decoding supports Unicode and repository README endpoint");

// Exercise install/disable/restore on an isolated miniature game, not the Steam copy.
var fixture = Path.Combine(outDir, "store-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(fixture, "SNB_Data"));
File.WriteAllBytes(Path.Combine(fixture, "SNB.exe"), []);
var rel = "SNB_Data/test.bundle";
File.WriteAllText(Path.Combine(fixture, rel), "original");
var game = GameInstall.Open(fixture)!;
var store = ModStore.Load(game);
var stage = Path.Combine(outDir, "staged-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(stage, "SNB_Data"));
File.WriteAllText(Path.Combine(stage, rel), "modded");
store.Install(new StagedMod { Manifest = new ModManifest { Id = "fixture", Name = "Fixture" }, Dir = stage, PreviewPath = Path.Combine(root,"mods/capa-blanca/preview.png") });
Require(File.Exists(Path.Combine(store.StagedPath("fixture"),".gestor-preview.png")) && store.State.Mods["fixture"].Files.All(f=>!f.Contains("preview")), "Installed preview is cached outside the game payload and remains available offline");
store.Enable("fixture");
Require(File.ReadAllText(Path.Combine(fixture, rel)) == "modded", "Installation activates replacement");
store.Disable("fixture");
Require(File.ReadAllText(Path.Combine(fixture, rel)) == "original", "Disable restores original");
vm.Shutdown();
window.Close();
Console.WriteLine("PASS: catalog, details/README, dated bundles, theme es/en/zh/ru, vector icons, GitHub community discovery, minigun timing, holographic cards, isolated install/restore.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ReadmeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath != "/repos/owner/mod/readme") throw new InvalidOperationException("Wrong README route");
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes("# Mod\n\nCómo instalar."));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"encoding\":\"base64\",\"content\":\"{content}\"}}") });
    }
}

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) {Content=new StringContent("Offline test")});
}

sealed class TestHost : IUiHost
{
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> PickFileAsync(string title, string[] patterns) => Task.FromResult<string?>(null);
    public Task<string?> SaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
    public Task CopyAsync(string text) => Task.CompletedTask;
    public void BringToFront() { }
}
