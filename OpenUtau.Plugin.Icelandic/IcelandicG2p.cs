using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenUtau.Api;

namespace OpenUtau.Plugin.Icelandic;

/// Framburður úr orðasafni hefur forgang, næst kemur sértæk hljóðnálgun fyrir raddbankann.
public sealed class IcelandicG2p : IG2p {
    static readonly Lazy<Dictionary<string, string[]>> lexicon = new(LoadLexicon);
    static readonly Lazy<Dictionary<string, string[]>> spellingSuggestions = new(() =>
        lexicon.Value.Keys.GroupBy(RemoveAccents).ToDictionary(g => g.Key, g => g.ToArray()));
    readonly IG2p symbols;
    readonly HashSet<string> supported;
    readonly Dictionary<string, string[]> mapping;
    readonly Dictionary<string, string[]> overrides;

    public static int WordCount => lexicon.Value.Count;
    public static IReadOnlyDictionary<string, string[]> Lexicon =>
        lexicon.Value.ToDictionary(pair => pair.Key, pair => (string[])pair.Value.Clone());

    public IcelandicG2p(IG2p symbols, IEnumerable<string> supported,
        Dictionary<string, string[]>? mapping = null, Dictionary<string, string[]>? overrides = null) {
        this.symbols = symbols;
        this.supported = new HashSet<string>(supported, StringComparer.Ordinal);
        this.mapping = mapping ?? DefaultMapping();
        ValidateTargets(["SP", "AP"], "nauðsynlegar þagnir");
        this.overrides = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var pair in overrides ?? new Dictionary<string, string[]>()) {
            this.overrides[Normalize(pair.Key)] = pair.Value;
        }
        foreach (var (phone, targets) in this.mapping) {
            ValidateTargets(targets, $"hljóðvörpun fyrir {phone}");
        }
        foreach (var (word, targets) in this.overrides) { ValidateTargets(targets, word); }
        // Hætta við fyrir ályktun í stað þess að sleppa óvörpuðu hljóði hljóðlaust.
        foreach (var phone in lexicon.Value.Values.SelectMany(v => v).Distinct()) {
            if (!this.mapping.ContainsKey(phone.TrimEnd(':'))) {
                throw new InvalidDataException($"Íslenskt hljóðritunartákn án vörpunar: {phone}");
            }
        }
    }

    static Stream Resource(string name) => typeof(IcelandicG2p).Assembly
        .GetManifestResourceStream("OpenUtau.Plugin.Icelandic.Data." + name)
        ?? throw new FileNotFoundException(name);

    public static Dictionary<string, string[]> DefaultMapping() {
        using var stream = Resource("icelandic-mapping.json");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
    }

    static Dictionary<string, string[]> LoadLexicon() {
        using var stream = new GZipStream(Resource("iceprondict-standard.tsv.gz"), CompressionMode.Decompress);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        while (reader.ReadLine() is string line) {
            var parts = line.Split('\t');
            if (parts.Length >= 2) { result.TryAdd(Normalize(parts[0]), parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
        }
        return result;
    }

    static string Normalize(string text) => text.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    static string RemoveAccents(string text) => new string(text.Normalize(NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
            System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);

    void ValidateTargets(string[] targets, string context) {
        if (targets == null || targets.Length == 0) { throw new InvalidDataException($"Tóm hljóðvörpun: {context}"); }
        foreach (var target in targets) {
            if (!IsValidSymbol(target)) {
                throw new InvalidDataException($"{context}: táknið '{target}' vantar í raddbanka/líkön eða tegundaskilgreiningu.");
            }
        }
    }

    public bool IsValidSymbol(string symbol) => supported.Contains(symbol) && symbols.IsValidSymbol(symbol);
    public bool IsVowel(string symbol) => IsValidSymbol(symbol) && symbols.IsVowel(symbol);
    public bool IsGlide(string symbol) => IsValidSymbol(symbol) && symbols.IsGlide(symbol);
    public string[] UnpackHint(string hint, char separator = ' ') {
        var targets = hint.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        ValidateTargets(targets, "handvirk hljóðritun");
        return targets;
    }

    public string[] MapSampa(IEnumerable<string> phones) => phones.SelectMany(phone =>
        mapping.TryGetValue(phone.TrimEnd(':', 'ː'), out var targets) ? targets
            : throw new InvalidDataException($"Íslenskt hljóðritunartákn án vörpunar: {phone}")).ToArray();

    public string[] Query(string grapheme) {
        if (string.IsNullOrWhiteSpace(grapheme)) { return null!; }
        if (grapheme.Equals("br", StringComparison.OrdinalIgnoreCase)) { return ["AP"]; }
        if (grapheme is "SP" or "AP" or "R" or "-") { return [grapheme == "AP" ? "AP" : "SP"]; }
        // Fullgilt markhóptákn helst nákvæmt, þar á meðal tákn eins og es/G.
        if (grapheme.Contains('/')) { return UnpackHint(grapheme); }
        if (grapheme.StartsWith("sampa:", StringComparison.OrdinalIgnoreCase)) {
            return MapSampa(grapheme[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        var words = grapheme.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        foreach (var raw in words) {
            var word = Normalize(raw.Trim('!', '?', '.', ',', ';', ':', '"', '“', '”', '„', '(', ')'));
            if (word.Length == 0) { continue; }
            if (overrides.TryGetValue(word, out var manual)) { result.AddRange(manual); }
            else if (lexicon.Value.TryGetValue(word, out var phones)) { result.AddRange(MapSampa(phones)); }
            else {
                var rulePhones = IcelandicRuleG2p.TranscribeToSampa(word);
                if (rulePhones.Length > 0) {
                    result.AddRange(MapSampa(rulePhones));
                } else {
                    var suggestion = spellingSuggestions.Value.TryGetValue(RemoveAccents(word), out var candidates)
                        ? $" Stafsetningartillögur með broddstaf: {string.Join(", ", candidates.Take(5))}." : string.Empty;
                    throw new InvalidDataException($"Íslenskt orð án hljóðritunar: '{word}'.{suggestion} Notaðu handvirka hljóðritun (t.d. en/th fr/ou), sampa:, eða icelandic-words.json í dsdur möppunni.");
                }
            }
        }
        return result.Count == 0 ? null! : result.ToArray();
    }
}
