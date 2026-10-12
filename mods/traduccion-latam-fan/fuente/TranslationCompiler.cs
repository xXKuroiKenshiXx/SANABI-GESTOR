using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace SanabiGestor.Core;

public sealed record TranslationResult(string Output, int Files, int Replaced, int Pending, int Invalid);

// Based on the English -> Spanish lookup used by the original CSV/JSON tool.
// No Python dependency. Exported JSON is never written over its source.
public static class TranslationCompiler
{
    public static TranslationResult Compile(string csv, string inputFolder, string outputParent)
    {
        var translations = ReadCsv(csv);
        var inputs = Directory.GetFiles(inputFolder, "*.json", System.IO.SearchOption.AllDirectories);
        if (inputs.Length == 0) throw new InvalidDataException("No hay archivos JSON en la carpeta elegida.");
        var output = Path.Combine(outputParent, $"traduccion-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        int changed = 0, pending = 0, invalid = 0, files = 0;
        var issues = new List<object>();
        foreach (var path in inputs)
        {
            var relative = Path.GetRelativePath(inputFolder, path);
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (root?["textData"]?["values"]?["Array"] is not JsonArray lines)
                {
                    issues.Add(new { file = relative, reason = "No contiene textData.values.Array" });
                    invalid++; continue;
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i] is not JsonValue value || !value.TryGetValue<string>(out var english) || string.IsNullOrWhiteSpace(english)) continue;
                    if (translations.TryGetValue(english, out var spanish))
                    {
                        if (!Tokens(english).SequenceEqual(Tokens(spanish)))
                        {
                            issues.Add(new { file = relative, line = i, reason = "Marcadores o etiquetas diferentes", text = english });
                            pending++; continue;
                        }
                        lines[i] = spanish; changed++;
                    }
                    else { pending++; issues.Add(new { file = relative, line = i, reason = "Sin coincidencia exacta en el CSV", text = english }); }
                }
                var dest = Path.Combine(output, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllText(dest, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                files++;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                invalid++; issues.Add(new { file = relative, reason = "JSON inválido: " + e.Message });
            }
        }
        var report = new { files, replaced = changed, pending, invalid, issues };
        File.WriteAllText(Path.Combine(output, "informe-traduccion.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return new TranslationResult(output, files, changed, pending, invalid);
    }

    static string[] Tokens(string text) => Regex.Matches(text, @"\{[^{}]+\}|</?[A-Za-z][^>]*>|%\d*\$?[sdif]")
        .Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();

    public static Dictionary<string, string> ReadCsv(string path)
    {
        // TextFieldParser supports quoted delimiters, multiline fields and BOM.
        var headerLine = File.ReadLines(path, Encoding.UTF8).FirstOrDefault() ?? "";
        var delimiter = headerLine.Count(c => c == ';') > headerLine.Count(c => c == ',') ? ";" : ",";
        using var parser = new TextFieldParser(path, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(delimiter);
        var header = parser.ReadFields() ?? throw new InvalidDataException("CSV vacío.");
        var english = Array.FindIndex(header, h => h.Trim().Equals("English", StringComparison.OrdinalIgnoreCase));
        var spanish = Array.FindIndex(header, h => h.Trim().Equals("Spanish", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("Aa Spanish", StringComparison.OrdinalIgnoreCase));
        if (english < 0 || spanish < 0) throw new InvalidDataException("El CSV necesita las columnas English y Spanish (o Aa Spanish).");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields();
            if (row == null || row.Length <= Math.Max(english, spanish)) throw new InvalidDataException("El CSV contiene una fila incompleta.");
            var from = row[english]; var to = row[spanish];
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
            if (map.TryGetValue(from, out var old) && old != to) throw new InvalidDataException($"Hay dos traducciones diferentes para: {from}");
            map[from] = to;
        }
        if (map.Count == 0) throw new InvalidDataException("El CSV no tiene traducciones utilizables.");
        return map;
    }
}
