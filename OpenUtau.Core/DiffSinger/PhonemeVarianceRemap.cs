using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.DiffSinger {
    sealed class PhonemeVarianceRemapConfig {
        public OpenMouthConfig? open_mouth;
        public TensionConfig? tension;

        public sealed class OpenMouthConfig {
            public int default_stop = 0;
            public int default_nasal = 0;
            public int default_vowel = 100;
            public float crossfade_ms = 20f;
            public Dictionary<string, int>? stop_exceptions;
            public Dictionary<string, int>? symbols;
        }

        public sealed class TensionConfig {
            public int vowel_scale = 100;
            public int nasal_scale = 100;
            public int stop_scale = 50;
            public float crossfade_ms = 40f;
            public Dictionary<string, int>? stop_exceptions;
        }
    }

    public static class PhonemeVarianceRemap {
        const string ResourceName = "OpenUtau.Core.DiffSinger.Data.lunai-phoneme-variance-remap.yaml";

        static readonly Lazy<PhonemeVarianceRemapConfig> Config = new(LoadConfig);

        static PhonemeVarianceRemapConfig LoadConfig() {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new InvalidDataException($"Missing embedded resource {ResourceName}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Yaml.DefaultDeserializer.Deserialize<PhonemeVarianceRemapConfig>(reader.ReadToEnd())
                ?? new PhonemeVarianceRemapConfig();
        }

        public static bool ShouldApply(RenderPhrase phrase, string abbr) {
            if (!Preferences.Default.DiffSingerPhonemeVarianceRemap) {
                return false;
            }
            if (phrase.singer is not DiffSingerSinger singer || !singer.Found) {
                return false;
            }
            if (abbr != Format.Ustx.OPEC && abbr != Format.Ustx.TENC) {
                return false;
            }
            return DiffSingerUtils.IsExpressionAvailable(singer, abbr);
        }

        static bool IsPassthroughPhoneme(string symbol) => symbol == "SP";

        public static float GetScale(string symbol, string abbr, PhonemeTypeLookup? types) {
            if (IsPassthroughPhoneme(symbol)) {
                return 100f;
            }
            if (abbr == Format.Ustx.OPEC) {
                return GetOpenMouthScale(symbol, types);
            }
            if (abbr == Format.Ustx.TENC) {
                return GetTensionScale(symbol, types);
            }
            return 100f;
        }

        static float GetOpenMouthScale(string symbol, PhonemeTypeLookup? types) {
            var config = Config.Value.open_mouth ?? new PhonemeVarianceRemapConfig.OpenMouthConfig();
            if (config.symbols != null && config.symbols.TryGetValue(symbol, out int tableScale)) {
                return tableScale;
            }
            if (config.stop_exceptions != null && config.stop_exceptions.TryGetValue(symbol, out int stopException)) {
                return stopException;
            }
            var type = types?.GetType(symbol);
            if (type == "vowel") {
                return config.default_vowel;
            }
            if (type == "nasal") {
                return config.default_nasal;
            }
            if (type == "stop") {
                return config.default_stop;
            }
            if (types?.IsVowel(symbol) == true) {
                return config.default_vowel;
            }
            return config.default_stop;
        }

        static float GetTensionScale(string symbol, PhonemeTypeLookup? types) {
            var config = Config.Value.tension ?? new PhonemeVarianceRemapConfig.TensionConfig();
            if (config.stop_exceptions != null && config.stop_exceptions.TryGetValue(symbol, out int stopException)) {
                return stopException;
            }
            var type = types?.GetType(symbol);
            if (type == "vowel" || type == "nasal") {
                return type == "nasal" ? config.nasal_scale : config.vowel_scale;
            }
            if (type == "stop") {
                return config.stop_scale;
            }
            if (types?.IsVowel(symbol) == true) {
                return config.vowel_scale;
            }
            if (types?.IsNasal(symbol) == true) {
                return config.nasal_scale;
            }
            return config.stop_scale;
        }

        public static float[] BuildFrameScales(
            RenderPhrase phrase,
            string abbr,
            float frameMs,
            int headFrames,
            int tailFrames,
            PhonemeTypeLookup? types) {
            var phonemes = phrase.phones.Select(p => p.phoneme).ToArray();
            var durations = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            return BuildFrameScales(phonemes, durations, abbr, frameMs, types);
        }

        internal static float[] BuildFrameScales(
            IReadOnlyList<string> phonemes,
            IReadOnlyList<int> durations,
            string abbr,
            float frameMs,
            PhonemeTypeLookup? types) {
            int totalFrames = durations.Sum();
            var scales = new float[totalFrames];
            if (phonemes.Count == 0 || durations.Count < phonemes.Count + 2 || totalFrames == 0) {
                return scales;
            }
            var phoneScales = phonemes.Select(p => GetScale(p, abbr, types)).ToArray();
            int frame = durations[0];
            for (int phoneIndex = 0; phoneIndex < phonemes.Count; ++phoneIndex) {
                int start = frame;
                frame += durations[phoneIndex + 1];
                float scale = phoneScales[phoneIndex];
                for (int i = start; i < frame && i < totalFrames; ++i) {
                    scales[i] = scale;
                }
            }
            int crossfadeFrames = Math.Clamp(
                (int)Math.Round(GetCrossfadeMs(abbr) / frameMs), 1, 20);
            frame = durations[0];
            for (int phoneIndex = 0; phoneIndex < phonemes.Count - 1; ++phoneIndex) {
                int boundary = frame + durations[phoneIndex + 1];
                frame = boundary;
                float left = phoneScales[phoneIndex];
                float right = phoneScales[phoneIndex + 1];
                if (Math.Abs(left - right) < 0.001f) {
                    continue;
                }
                int fadeStart = Math.Max(0, boundary - crossfadeFrames);
                int fadeEnd = Math.Min(totalFrames, boundary + crossfadeFrames);
                int fadeLength = fadeEnd - fadeStart;
                if (fadeLength <= 1) {
                    continue;
                }
                for (int i = fadeStart; i < fadeEnd; ++i) {
                    float t = SmoothStep((i - fadeStart) / (float)(fadeLength - 1));
                    scales[i] = left + (right - left) * t;
                }
            }
            return scales;
        }

        static float GetCrossfadeMs(string abbr) {
            if (abbr == Format.Ustx.TENC) {
                return Config.Value.tension?.crossfade_ms ?? 40f;
            }
            return Config.Value.open_mouth?.crossfade_ms ?? 20f;
        }

        public static float RemapBaseline(string abbr) {
            return abbr == Format.Ustx.OPEC ? 50f : 0f;
        }

        public static float RemapSample(float user, float scale, string abbr) {
            float baseline = RemapBaseline(abbr);
            if (user <= baseline) {
                return user;
            }
            return baseline + (user - baseline) * scale / 100f;
        }

        public static void ApplyToUserSamples(float[] userSamples, float[] scales, string abbr) {
            int length = Math.Min(userSamples.Length, scales.Length);
            for (int i = 0; i < length; ++i) {
                userSamples[i] = RemapSample(userSamples[i], scales[i], abbr);
            }
        }

        public static float[] ApplyIfEnabled(
            RenderPhrase phrase,
            float[] userSamples,
            string abbr,
            float frameMs,
            int headFrames,
            int tailFrames,
            PhonemeTypeLookup? types = null) {
            if (!ShouldApply(phrase, abbr)) {
                return userSamples;
            }
            types ??= PhonemeTypeLookup.TryFromSinger(phrase.singer);
            var scales = BuildFrameScales(phrase, abbr, frameMs, headFrames, tailFrames, types);
            var result = userSamples.ToArray();
            ApplyToUserSamples(result, scales, abbr);
            return result;
        }

        static float SmoothStep(float t) => t * t * (3f - 2f * t);
    }
}
