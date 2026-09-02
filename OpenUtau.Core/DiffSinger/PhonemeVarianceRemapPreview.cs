using System;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.DiffSinger {
    public static class PhonemeVarianceRemapPreview {
        public static bool TryBuildEffectiveCurve(
            RenderPhrase phrase,
            int partPosition,
            string abbr,
            float[]? userCurve,
            double defaultValue,
            out float[] ticks,
            out float[] values) {
            ticks = Array.Empty<float>();
            values = Array.Empty<float>();
            if (!PhonemeVarianceRemap.ShouldApply(phrase, abbr)) {
                return false;
            }
            if (phrase.singer is not DiffSingerSinger singer) {
                return false;
            }
            float frameMs = singer.dsConfig.frameMs();
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            var durations = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = durations.Sum();
            if (totalFrames <= 0) {
                return false;
            }
            var userSamples = DiffSingerUtils.SampleCurve(
                phrase, userCurve, defaultValue, frameMs, totalFrames, headFrames, tailFrames,
                x => x).Select(x => (float)x).ToArray();
            var types = PhonemeTypeLookup.TryFromSinger(singer);
            var remapped = PhonemeVarianceRemap.ApplyIfEnabled(
                phrase, userSamples, abbr, frameMs, headFrames, tailFrames, types);
            double startMs = phrase.positionMs - headFrames * frameMs;
            ticks = Enumerable.Range(0, totalFrames)
                .Select(i => (float)phrase.timeAxis.MsPosToTickPos(startMs + i * frameMs) - partPosition)
                .ToArray();
            values = remapped;
            return values.Length >= 2;
        }
    }
}
