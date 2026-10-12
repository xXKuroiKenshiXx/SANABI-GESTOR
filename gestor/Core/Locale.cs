using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace SanabiGestor.Core;

// UI language packs are bundled with the app. Mod authors' text is kept intact.
public sealed class Locale : INotifyPropertyChanged
{
    public static Locale Current { get; } = new();
    Dictionary<string, string> messages = new();
    List<(Regex Pattern,string Translation)> formats = new();
    public string Language { get; private set; } = "es";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string text]
    {
        get
        {
            if(messages.TryGetValue(text,out var literal))return literal;
            foreach(var (pattern,translation) in formats)
            {
                var match=pattern.Match(text);if(!match.Success)continue;
                return Regex.Replace(translation,@"\{(\d+)\}",m=>match.Groups["p"+m.Groups[1].Value].Value);
            }
            return text;
        }
    }
    public static string T(string text) => Current[text];

    public void Select(string language)
    {
        if (!Regex.IsMatch(language,"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$")) language="es";
        var custom=Path.Combine(Settings.DataDir,"languages",language+".json");
        var loaded=false;
        if(File.Exists(custom))try{messages=ReadPack(custom).Messages;loaded=true;}catch{ }
        if(!loaded)
        {
            if (!new[] { "es", "en", "zh", "ru" }.Contains(language)) language = "es";
            using var stream = AssetLoader.Open(new Uri($"avares://SanabiGestor/Assets/Languages/{language}.json"));
            messages = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        formats=messages.Where(x=>Regex.IsMatch(x.Key,@"\{\d+\}"))
            .Select(x=>(new Regex("^"+Regex.Replace(Regex.Escape(x.Key),@"\\\{(\d+)\}",m=>"(?<p"+m.Groups[1].Value+">.*?)")+"$",RegexOptions.Singleline),x.Value)).ToList();
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public static LanguagePack ReadPack(string path)
    {
        if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Paquete de idioma demasiado grande.");
        var pack=JsonSerializer.Deserialize<LanguagePack>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Paquete vacío.");
        if(!Regex.IsMatch(pack.Language,"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$")||string.IsNullOrWhiteSpace(pack.Name)||pack.Name.Length>50||pack.Messages.Count==0||pack.Messages.Count>3000)
            throw new InvalidDataException("El paquete necesita language, name y messages.");
        if(pack.Messages.Any(p=>p.Key.Length>4000||p.Value==null||p.Value.Length>8000))throw new InvalidDataException("Texto de idioma demasiado largo.");
        return pack;
    }
}

public sealed class LanguagePack
{
    public string Language { get; set; }="";
    public string Name { get; set; }="";
    public Dictionary<string,string> Messages { get; set; }=new();
}

public sealed class TrExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider provider) => new Binding(nameof(Locale.Language))
    { Source = Locale.Current, Mode = BindingMode.OneWay, Converter = new LiteralConverter(text) };
    sealed class LiteralConverter(string key) : IValueConverter
    {
        public object Convert(object? value, Type type, object? parameter, CultureInfo culture) => Locale.T(key);
        public object ConvertBack(object? value, Type type, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

public sealed class UiTextExtension(string path) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider provider) => new MultiBinding
    {
        Bindings = [new Binding(path),new Binding(nameof(Locale.Language)){Source=Locale.Current}],
        Converter = new FuncMultiValueConverter<object,string>(values=>Locale.T(values.FirstOrDefault()?.ToString()??""))
    };
}

public sealed record PreferenceChoice(string Code, string Name)
{
    public override string ToString() => Name;
}
