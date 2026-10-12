using CommunityToolkit.Mvvm.ComponentModel;
using SanabiGestor.Core;

namespace SanabiGestor.ViewModels;

public partial class MainViewModel
{
    public IReadOnlyList<PreferenceChoice> ModCategories { get; } = [new("all", "Todas las categorías"), new("skins", "Skins"), new("personajes", "Personajes"), new("habilidades", "Habilidades"), new("visual", "Entorno y efectos"), new("audio", "Audio"), new("traduccion", "Traduccion"), new("jugabilidad", "Jugabilidad"), new("dificultad", "Dificultad"), new("herramienta", "Herramienta"), new("otros", "Otros")];
    [ObservableProperty] PreferenceChoice? selectedCategory;
    public string CategoryHelp => SelectedCategory?.Code is { } c && c != "all" ? Fmt.CategoryDescription(c)
        : "Skins cambian la apariencia; habilidades cambian ataques y movimiento; entorno y efectos cambia escenarios e interfaz.";
    public bool HasExperimentalMods => ExperimentalMods.Count > 0;
    public string UploadCategoryHelp => Fmt.CategoryDescription(UploadCategory);
    partial void OnUploadCategoryChanged(string value) => OnPropertyChanged(nameof(UploadCategoryHelp));
    partial void OnSelectedCategoryChanged(PreferenceChoice? value) { FilterCards(); OnPropertyChanged(nameof(CategoryHelp)); }
    bool MatchesCategory(ModCardViewModel card) => SelectedCategory == null || SelectedCategory.Code == "all"
        || (card.Entry?.Category is { } c ? c == "personaje" ? "skins" : c : "otros") == SelectedCategory.Code;
}
