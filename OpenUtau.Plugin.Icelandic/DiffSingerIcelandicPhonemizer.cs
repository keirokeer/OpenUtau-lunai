using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;

namespace OpenUtau.Plugin.Icelandic;

[Phonemizer("DiffSinger Icelandic Phonemizer", "Icelandic", language: "DiffSinger", author: "xiao")]
public sealed class DiffSingerIcelandicPhonemizer : DiffSingerBasePhonemizer {
    // Úttakstákn hafa nú þegar tungumálaforskeyti. Búðu aldrei til is/*
    // tákn eða tungumálakóða sem hljóðlíkanið var ekki þjálfað með.
    public override string GetLangCode() => string.Empty;

    IcelandicG2p? bankG2p;
    protected override IG2p LoadG2p(string rootPath, bool useLangId = false) => bankG2p = LoadForBank(rootPath);

    public static IcelandicG2p LoadForBank(string durationDirectory) {
        var configs = Path.GetFullPath(Path.Combine(durationDirectory, ".."));
        var paths = new[] { Path.Combine(configs, "dsconfig.yaml"),
            Path.Combine(durationDirectory, "dsconfig.yaml"),
            Path.Combine(configs, "dspitch", "dsconfig.yaml"),
            Path.Combine(configs, "dsvariance", "dsconfig.yaml") };
        HashSet<string>? supported = null;
        foreach (var path in paths) {
            if (!File.Exists(path)) {
                if (path == paths[0] || path == paths[1]) { throw new FileNotFoundException("Configuração DiffSinger ausente", path); }
                continue;
            }
            var config = Yaml.DefaultDeserializer.Deserialize<DsConfig>(File.ReadAllText(path));
            var directory = Path.GetDirectoryName(path)!;
            var tokens = DiffSingerUtils.LoadPhonemes(Path.GetFullPath(Path.Combine(directory, config.phonemes)));
            var valid = new HashSet<string>(tokens.Keys, StringComparer.Ordinal);
            if (config.use_lang_id) {
                var languages = DiffSingerUtils.LoadLanguageIds(Path.GetFullPath(Path.Combine(directory, config.languages)));
                valid.RemoveWhere(token => token.Contains('/') && !languages.ContainsKey(token.Split('/')[0]));
            }
            if (supported == null) { supported = valid; } else { supported.IntersectWith(valid); }
        }
        var builder = G2pDictionary.NewBuilder();
        builder.Load(File.ReadAllText(Path.Combine(durationDirectory, "dsdict.yaml")));
        builder.AddSymbol("SP", true);
        builder.AddSymbol("AP", true);
        var mapping = IcelandicG2p.DefaultMapping();
        var mapPath = Path.Combine(durationDirectory, "icelandic-mapping.json");
        if (File.Exists(mapPath)) {
            foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(mapPath))!) {
                mapping[pair.Key] = pair.Value;
            }
        }
        var wordsPath = Path.Combine(durationDirectory, "icelandic-words.json");
        var words = File.Exists(wordsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(wordsPath)) : null;
        return new IcelandicG2p(builder.Build(), supported!, mapping, words);
    }
}
