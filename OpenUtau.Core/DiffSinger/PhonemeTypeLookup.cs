using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenUtau.Api;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>Symbol types from a singer dsdict.yaml (vowel, stop, nasal, …).</summary>
    public sealed class PhonemeTypeLookup {
        readonly Dictionary<string, string> symbolTypes;

        PhonemeTypeLookup(Dictionary<string, string> symbolTypes) {
            this.symbolTypes = symbolTypes;
        }

        public static PhonemeTypeLookup? TryFromSinger(OpenUtau.Core.Ustx.USinger? singer) {
            if (singer is not DiffSingerSinger dsSinger) {
                return null;
            }
            string? path = TryFindDsdictPath(dsSinger);
            return path != null ? TryLoad(path) : null;
        }

        public static PhonemeTypeLookup? TryLoad(string path) {
            if (!File.Exists(path)) {
                return null;
            }
            try {
                var data = Yaml.DefaultDeserializer.Deserialize<G2pDictionaryData>(
                    File.ReadAllText(path, Encoding.UTF8));
                var types = new Dictionary<string, string>(StringComparer.Ordinal);
                if (data.symbols != null) {
                    foreach (var symbol in data.symbols) {
                        if (!string.IsNullOrEmpty(symbol.symbol) && !string.IsNullOrEmpty(symbol.type)) {
                            types[symbol.symbol] = symbol.type;
                        }
                    }
                }
                return types.Count > 0 ? new PhonemeTypeLookup(types) : null;
            } catch {
                return null;
            }
        }

        static string? TryFindDsdictPath(DiffSingerSinger singer) {
            foreach (var sub in new[] { "dsvariance", "dsdur", "dspitch" }) {
                var path = Path.Combine(singer.Location, sub, "dsdict.yaml");
                if (File.Exists(path)) {
                    return path;
                }
            }
            var root = Path.Combine(singer.Location, "dsdict.yaml");
            return File.Exists(root) ? root : null;
        }

        public string? GetType(string symbol) {
            return symbolTypes.TryGetValue(symbol, out var type) ? type : null;
        }

        public bool IsVowel(string symbol) => GetType(symbol) == "vowel";

        public bool IsNasal(string symbol) => GetType(symbol) == "nasal";

        public bool IsStop(string symbol) => GetType(symbol) == "stop";
    }
}
