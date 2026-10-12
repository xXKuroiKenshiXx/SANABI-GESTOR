using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SanabiGestor.Core;

namespace SanabiGestor.Views;

// One small vector icon per action, independent of installed emoji fonts.
public sealed class ActionLabel : StackPanel
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ActionLabel, string?>(nameof(Text));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    readonly Avalonia.Controls.Shapes.Path icon = new() { Width = 17, Height = 17, Stretch = Stretch.Uniform, StrokeThickness = 1.6 };
    readonly TextBlock label = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    public ActionLabel()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal; Spacing = 8;
        Children.Add(icon); Children.Add(label);
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, label.GetObservable(TextBlock.ForegroundProperty));
        AttachedToVisualTree += (_, _) => { Locale.Current.PropertyChanged += LanguageChanged; Update(); };
        DetachedFromVisualTree += (_, _) => Locale.Current.PropertyChanged -= LanguageChanged;
    }
    void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Update();
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == TextProperty) Update(); }
    void Update()
    {
        if (label == null) return;
        var text = Text ?? ""; label.Text = Locale.T(text).Replace("▶  ", "").Replace("← ", "");
        icon.Data = Geometry.Parse(GeometryFor(text));
    }
    static string GeometryFor(string text)
    {
        if (text.Contains("Jugar")) return "M4,2 L14,8 L4,14 Z";
        if (text.Contains("Notificaciones")) return "M3,11 H13 L12,8 V5 A4,4 0 0 0 4,5 V8 Z M6,14 Q8,16 10,14 M8,0 V1";
        if (text.Contains("Volver")) return "M14,8 H2 M7,3 L2,8 L7,13";
        if (text.Contains("Desinstalar") || text.Contains("Quitar")) return "M2,4 H14 M5,4 V2 H11 V4 M4,4 L5,14 H11 L12,4 M7,7 V11 M9,7 V11";
        if (text.Contains("Buscar")) return "M11,6 A5,5 0 1 1 1,6 A5,5 0 1 1 11,6 M10,10 L15,15";
        if (text.Contains("Actualiz") || text.Contains("actualizaciones") || text.Contains("actualización") || text.Contains("Reaplicar") || text.Contains("Restaur")) return "M13,5 A6,6 0 1 0 14,10 M13,1 V5 H9";
        if (text.Contains("Instalar") || text.Contains("Descargar") || text.Contains("Ver archivos")) return "M8,1 V11 M4,7 L8,11 L12,7 M2,11 V15 H14 V11";
        if (text.Contains("Subir") || text.Contains("Enviar") || text.Contains("Exportar")) return "M8,11 V1 M4,5 L8,1 L12,5 M2,11 V15 H14 V11";
        if (text.Contains("Guardar") || text.Contains("Copiar")) return "M5,1 H14 V11 H11 M2,5 H10 V15 H2 Z";
        if (text.Contains("Ajustes") || text.Contains("Preferencias")) return "M1,4 H15 M1,12 H15 M5,1 V7 M11,9 V15";
        if (text.Contains("Discord") || text.Contains("Comunidad")) return "M2,2 H14 V11 H7 L3,15 V11 H2 Z M5,6 H6 M10,6 H11";
        if (text.Contains("sesion") || text.Contains("Conectar") || text.Contains("Desconectar") || text.Contains("token") || text.Contains("key")) return "M11,4 A3,3 0 1 1 5,4 A3,3 0 1 1 11,4 M2,15 V12 Q8,6 14,12 V15";
        if (text.Contains("Consola")) return "M2,3 L7,8 L2,13 M9,13 H15";
        if (text.Contains("Mods") || text.Contains("mods")) return "M8,1 L15,5 V12 L8,16 L1,12 V5 Z M1,5 L8,9 L15,5 M8,9 V16";
        if (text.Contains("Abrir en") || text.Contains("release") || text.Contains("Repositorio") || text.Contains("GitHub") || text.Contains("fuente") || text.Contains("reglas")) return "M9,1 H15 V7 M15,1 L6,10 M6,2 H2 V14 H14 V10";
        return "M1,4 H6 L8,6 H15 V14 H1 Z M1,4 V2 H6 L8,4 H14 V6";
    }
}
