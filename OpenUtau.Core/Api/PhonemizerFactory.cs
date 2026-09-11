using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Api {
    public class PhonemizerFactory {
        public const string DiffSingerLanguage = "DiffSinger";
        public const string VoicevoxLanguage = "VOICEVOX";
        public const string EnunuLanguage = "ENUNU";
        public const string VogenLanguage = "VOGEN";

        public Type type;
        public string name;
        public string tag;
        public string author;
        public string language;

        public Phonemizer Create() {
            var phonemizer = Activator.CreateInstance(type) as Phonemizer;
            phonemizer.Name = name;
            phonemizer.Tag = tag;
            phonemizer.Language = language;
            return phonemizer;
        }

        public override string ToString() => string.IsNullOrEmpty(author)
            ? $"[{tag}] {name}"
            : $"[{tag}] {name} (Contributed by {author})";

        private static readonly object factoriesLock = new object();
        private static Dictionary<Type, PhonemizerFactory> factories = new Dictionary<Type, PhonemizerFactory>();
        private static PhonemizerFactory[] orderedFactories = [];
        public static PhonemizerFactory Get(Type type) {
            lock (factoriesLock) {
                if (!factories.TryGetValue(type, out var factory)) {
                    var attr = type.GetCustomAttribute<PhonemizerAttribute>();
                    if (attr == null || string.IsNullOrEmpty(attr.Name) || string.IsNullOrEmpty(attr.Tag)) {
                        return null;
                    }
                    factory = new PhonemizerFactory() {
                        type = type,
                        name = attr.Name,
                        tag = attr.Tag,
                        author = attr.Author,
                        language = attr.Language,
                    };
                    factories[type] = factory;
                }
                return factory;
            }
        }

        public static PhonemizerFactory? Get(string typeFullName) {
            lock (factoriesLock) {
                foreach (var factory in factories.Values) {
                    if (factory.type.FullName == typeFullName) {
                        return factory;
                    }
                }
                return null;
            }
        }

        public static void BuildList() {
            lock (factoriesLock) {
                orderedFactories = factories.Values.OrderBy(f => f.tag).ToArray();
            }
        }

        public static PhonemizerFactory[] GetAll() => orderedFactories;

        static bool LanguageEquals(string? language, string expected) {
            return string.Equals(language, expected, StringComparison.OrdinalIgnoreCase);
        }

        static bool ContainsIgnoreCase(string? text, string value) {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool TagStartsWithDiffs(string? tag) {
            if (string.IsNullOrWhiteSpace(tag)) {
                return false;
            }
            tag = tag.Trim();
            return tag.StartsWith("DIFFS", StringComparison.OrdinalIgnoreCase)
                && (tag.Length == 5 || !char.IsLetterOrDigit(tag[5]));
        }

        static bool TypeDerivesFrom(Type type, params string[] baseTypeNames) {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType) {
                foreach (var baseName in baseTypeNames) {
                    if (string.Equals(t.Name, baseName, StringComparison.Ordinal)) {
                        return true;
                    }
                    if (t.FullName != null
                        && t.FullName.EndsWith("." + baseName, StringComparison.Ordinal)) {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsDiffSingerPhonemizer(PhonemizerFactory factory) {
            if (factory == null) {
                return false;
            }
            if (LanguageEquals(factory.language, DiffSingerLanguage)) {
                return true;
            }
            if (TypeDerivesFrom(factory.type,
                    "DiffSingerBasePhonemizer",
                    "DiffSingerG2pPhonemizer",
                    "DiffSingerRefinedPhonemizer")) {
                return true;
            }
            if (ContainsIgnoreCase(factory.name, "DiffSinger")) {
                return true;
            }
            return TagStartsWithDiffs(factory.tag);
        }

        public static bool IsVoicevoxPhonemizer(PhonemizerFactory factory) {
            if (factory == null || IsDiffSingerPhonemizer(factory)) {
                return false;
            }
            if (LanguageEquals(factory.language, VoicevoxLanguage)) {
                return true;
            }
            if (TypeDerivesFrom(factory.type, "VoicevoxPhonemizer")) {
                return true;
            }
            if (ContainsIgnoreCase(factory.name, "ENtoJA")
                || TagStartsWithIgnoreCase(factory.tag, "S-VOICEVOX")) {
                return false;
            }
            return ContainsIgnoreCase(factory.name, "Voicevox")
                || ContainsIgnoreCase(factory.tag, "VOICEVOX");
        }

        public static bool IsEnunuPhonemizer(PhonemizerFactory factory) {
            if (factory == null
                || IsDiffSingerPhonemizer(factory)
                || IsVoicevoxPhonemizer(factory)) {
                return false;
            }
            if (LanguageEquals(factory.language, EnunuLanguage)) {
                return true;
            }
            if (TypeDerivesFrom(factory.type, "EnunuPhonemizer")) {
                return true;
            }
            return ContainsIgnoreCase(factory.name, "Enunu")
                || ContainsIgnoreCase(factory.tag, "ENUNU");
        }

        public static bool IsVogenPhonemizer(PhonemizerFactory factory) {
            if (factory == null
                || IsDiffSingerPhonemizer(factory)
                || IsVoicevoxPhonemizer(factory)
                || IsEnunuPhonemizer(factory)) {
                return false;
            }
            if (LanguageEquals(factory.language, VogenLanguage)) {
                return true;
            }
            if (TypeDerivesFrom(factory.type, "VogenBasePhonemizer")) {
                return true;
            }
            return ContainsIgnoreCase(factory.name, "Vogen")
                || ContainsIgnoreCase(factory.tag, "VOGEN");
        }

        public static bool IsUtauPhonemizer(PhonemizerFactory factory) {
            return !IsDiffSingerPhonemizer(factory)
                && !IsVoicevoxPhonemizer(factory)
                && !IsEnunuPhonemizer(factory)
                && !IsVogenPhonemizer(factory);
        }

        static bool TagStartsWithIgnoreCase(string? tag, string prefix) {
            return !string.IsNullOrEmpty(tag)
                && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesFlatPhonemizerMenu(USinger? singer) {
            if (singer == null || !singer.Found) {
                return false;
            }
            return singer.SingerType is USingerType.DiffSinger
                or USingerType.Voicevox
                or USingerType.Enunu
                or USingerType.Vogen;
        }

        public static IEnumerable<PhonemizerFactory> EnumerateForSinger(USinger? singer) {
            var all = GetAll();
            if (singer == null || !singer.Found) {
                return all.Where(IsUtauPhonemizer);
            }
            return singer.SingerType switch {
                USingerType.DiffSinger => all.Where(IsDiffSingerPhonemizer),
                USingerType.Voicevox => all.Where(IsVoicevoxPhonemizer),
                USingerType.Enunu => all.Where(IsEnunuPhonemizer),
                USingerType.Vogen => all.Where(IsVogenPhonemizer),
                _ => all.Where(IsUtauPhonemizer),
            };
        }
    }
}
