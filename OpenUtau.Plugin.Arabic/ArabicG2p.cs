using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenUtau.Api;

namespace OpenUtau.Plugin.Arabic;

public sealed class ArabicG2p : IG2p {
    readonly IG2p symbols;
    readonly HashSet<string> supported;
    readonly Dictionary<string, string[]> mapping;
    readonly Dictionary<string, string[]> overrides;

    public ArabicG2p(IG2p symbols, IEnumerable<string> supported,
        Dictionary<string, string[]>? mapping = null, Dictionary<string, string[]>? overrides = null) {
        this.symbols = symbols;
        this.supported = new HashSet<string>(supported, StringComparer.Ordinal);
        this.mapping = mapping ?? DefaultMapping();
        ValidateTargets(["SP", "AP"], "pausas essenciais");
        this.overrides = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var pair in overrides ?? new Dictionary<string, string[]>()) {
            this.overrides[Normalize(pair.Key)] = pair.Value;
        }
        foreach (var (phone, targets) in this.mapping) {
            ValidateTargets(targets, $"mapeamento fonético para {phone}");
        }
        foreach (var (word, targets) in this.overrides) { ValidateTargets(targets, word); }
    }

    static Stream Resource(string name) => typeof(ArabicG2p).Assembly
        .GetManifestResourceStream("OpenUtau.Plugin.Arabic.Data." + name)
        ?? throw new FileNotFoundException(name);

    public static Dictionary<string, string[]> DefaultMapping() {
        using var stream = Resource("arabic-mapping.json");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
    }

    static string Normalize(string text) => text.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    void ValidateTargets(string[] targets, string context) {
        if (targets == null || targets.Length == 0) { throw new InvalidDataException($"Mapeamento vazio: {context}"); }
        foreach (var target in targets) {
            if (!IsValidSymbol(target)) {
                throw new InvalidDataException($"{context}: o símbolo '{target}' não está presente no voicebank ou modelo.");
            }
        }
    }

    public bool IsValidSymbol(string symbol) => supported.Contains(symbol) && symbols.IsValidSymbol(symbol);
    public bool IsVowel(string symbol) => IsValidSymbol(symbol) && symbols.IsVowel(symbol);
    public bool IsGlide(string symbol) => IsValidSymbol(symbol) && symbols.IsGlide(symbol);

    public string[] UnpackHint(string hint, char separator = ' ') {
        var targets = hint.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        ValidateTargets(targets, "transcrição manual");
        return targets;
    }

    public string[] MapSampa(IEnumerable<string> phones) => phones.SelectMany(phone =>
        mapping.TryGetValue(phone, out var targets) ? targets
            : (mapping.TryGetValue(phone.TrimEnd(':'), out var fallbackTargets) ? fallbackTargets
            : throw new InvalidDataException($"Símbolo fonético árabe sem mapeamento: {phone}"))).ToArray();

    public string[] Query(string grapheme) {
        if (string.IsNullOrWhiteSpace(grapheme)) { return null!; }
        if (grapheme.Equals("br", StringComparison.OrdinalIgnoreCase)) { return ["AP"]; }
        if (grapheme is "SP" or "AP" or "R" or "-") { return [grapheme == "AP" ? "AP" : "SP"]; }
        
        // Suporte a hints manuais com prefixo de idioma (ex: en/th fr/ou)
        if (grapheme.Contains('/')) { return UnpackHint(grapheme); }
        if (grapheme.StartsWith("sampa:", StringComparison.OrdinalIgnoreCase)) {
            return MapSampa(grapheme[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var words = grapheme.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var raw in words) {
            var word = Normalize(raw.Trim('!', '?', '.', ',', ';', ':', '"', '“', '”', '„', '(', ')'));
            if (word.Length == 0) { continue; }
            if (overrides.TryGetValue(word, out var manual)) { 
                result.AddRange(manual); 
            } else {
                var rulePhones = ArabicRuleG2p.TranscribeToSampa(word);
                if (rulePhones.Length > 0) {
                    result.AddRange(MapSampa(rulePhones));
                } else {
                    throw new InvalidDataException($"Palavra árabe sem transcrição: '{word}'. Use transcrição manual (ex: en/th fr/ou), sampa:, ou arabic-words.json na pasta dsdur.");
                }
            }
        }
        return result.Count == 0 ? null! : result.ToArray();
    }
}
