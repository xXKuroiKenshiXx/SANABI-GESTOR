using Avalonia;
using Avalonia.Styling;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanabiGestor.Core;
using System.Collections.ObjectModel;

namespace SanabiGestor.ViewModels;

public partial class MainViewModel
{
    public IReadOnlyList<PreferenceChoice> Themes { get; } = [new("dark", "Oscuro"), new("light", "Claro")];
    public ObservableCollection<PreferenceChoice> Languages { get; } = [new("es", "Español"), new("en", "English"), new("zh", "简体中文"), new("ru", "Русский")];
    [ObservableProperty] PreferenceChoice? selectedTheme;
    [ObservableProperty] PreferenceChoice? selectedLanguage;
    public const string DiscordInvite = "https://discord.gg/P6yQeuEA8";
    public FontFamily UiFontFamily => new(SelectedLanguage?.Code=="zh" ? "avares://SanabiGestor/Assets/Fonts#Noto Sans CJK SC" : "avares://Avalonia.Fonts.Inter/Assets#Inter");
    bool initializingPreferences;

    void InitializePreferences()
    {
        initializingPreferences=true;
        var languageDir=Path.Combine(Settings.DataDir,"languages");
        if(Directory.Exists(languageDir)) foreach(var path in Directory.EnumerateFiles(languageDir,"*.json"))
            try { var pack=Locale.ReadPack(path); if(Languages.All(x=>x.Code!=pack.Language)) Languages.Add(new(pack.Language,pack.Name)); }catch{ }
        SelectedTheme = Themes.FirstOrDefault(x => x.Code == settings.Theme) ?? Themes[0];
        SelectedLanguage = Languages.FirstOrDefault(x => x.Code == settings.Language) ?? Languages[0];
        initializingPreferences=false;
    }
    static void ApplyTheme(string code)
    { if (Application.Current is { } app) app.RequestedThemeVariant = code == "light" ? ThemeVariant.Light : ThemeVariant.Dark; }
    partial void OnSelectedThemeChanged(PreferenceChoice? value)
    { if (value == null) return; settings.Theme = value.Code; ApplyTheme(value.Code); if(!initializingPreferences) settings.Save(); }
    partial void OnSelectedLanguageChanged(PreferenceChoice? value)
    { if (value == null) return; settings.Language = value.Code; Locale.Current.Select(value.Code); OnPropertyChanged(nameof(UiFontFamily)); if(!initializingPreferences) settings.Save(); }
    [RelayCommand] void OpenDiscord() => Platform.Open(DiscordInvite);
    [RelayCommand] async Task ImportLanguageAsync()
    {
        var path=await ui.PickFileAsync(Locale.T("Importar idioma…"),["*.json"]);if(path==null)return;
        try
        {
            var pack=Locale.ReadPack(path);var dir=Path.Combine(Settings.DataDir,"languages");Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,pack.Language+".json"),System.Text.Json.JsonSerializer.Serialize(pack));
            var choice=Languages.FirstOrDefault(x=>x.Code==pack.Language);
            if(choice==null){choice=new(pack.Language,pack.Name);Languages.Add(choice);}
            SelectedLanguage=choice;Locale.Current.Select(pack.Language);
        }
        catch(Exception e){await Alert(Locale.T("Paquete de idioma inválido"),e.Message);}
    }
}
