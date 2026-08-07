using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.DiffSinger {
    internal sealed class AcousticRetakeState {
        public readonly Dictionary<string, float[]> conditions;
        public readonly float[] mel;
        public readonly int[] melDims;
        public readonly int totalFrames;
        public readonly int melBins;
        public readonly float frameMs;
        /// <summary>Pre-dynamics vocoder output for sample HardCompose.</summary>
        public readonly float[]? rawSamples;
        public readonly int sampleRate;
        public readonly int hopSize;

        public AcousticRetakeState(
            Dictionary<string, float[]> conditions,
            float[] mel,
            int[] melDims,
            int totalFrames,
            int melBins,
            float frameMs,
            float[]? rawSamples = null,
            int sampleRate = 0,
            int hopSize = 0) {
            this.conditions = new Dictionary<string, float[]>(conditions, StringComparer.Ordinal);
            this.mel = (float[])mel.Clone();
            this.melDims = (int[])melDims.Clone();
            this.totalFrames = totalFrames;
            this.melBins = melBins;
            this.frameMs = frameMs;
            this.rawSamples = rawSamples == null ? null : (float[])rawSamples.Clone();
            this.sampleRate = sampleRate;
            this.hopSize = hopSize;
        }

        public bool HasCompatibleRawSamples(int expectedLength, int hopSize) {
            return rawSamples != null &&
                rawSamples.Length == expectedLength &&
                this.hopSize == hopSize &&
                hopSize > 0;
        }
    }

    internal sealed class AcousticRetakeStateCache {
        readonly int capacity;
        readonly Dictionary<ulong, LinkedListNode<(ulong key, AcousticRetakeState state)>> entries = new();
        readonly LinkedList<(ulong key, AcousticRetakeState state)> recency = new();

        internal AcousticRetakeStateCache(int capacity) {
            if (capacity <= 0) {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            this.capacity = capacity;
        }

        internal int Count => entries.Count;

        internal bool TryGetValue(ulong key, out AcousticRetakeState state) {
            if (!entries.TryGetValue(key, out var node)) {
                state = null!;
                return false;
            }
            recency.Remove(node);
            recency.AddFirst(node);
            state = node.Value.state;
            return true;
        }

        internal void Set(ulong key, AcousticRetakeState state) {
            if (entries.TryGetValue(key, out var existing)) {
                existing.Value = (key, state);
                recency.Remove(existing);
                recency.AddFirst(existing);
                return;
            }
            var node = recency.AddFirst((key, state));
            entries.Add(key, node);
            if (entries.Count <= capacity) {
                return;
            }
            var oldest = recency.Last!;
            recency.RemoveLast();
            entries.Remove(oldest.Value.key);
        }
    }

    /// <summary>
    /// SynthV-style acoustic local retake: regenerate only frames whose conditions
    /// (pitch/curves/spk) changed, keep the rest via gt_mel + HardCompose (mel and samples).
    /// Also supports explicit note-selection force retake from the piano-roll context menu.
    /// </summary>
    internal static class DiffSingerAcousticRetake {
        public const float ConditionEpsilon = 1e-4f;
        public const float PadMs = 64f;
        public const float FullRetakeFraction = 0.85f;
        /// <summary>Edge crossfade duration for sample HardCompose (equal-power cosine).</summary>
        public const float SampleCrossfadeMs = 60f;
        const int StateCacheCapacity = 32;

        static readonly HashSet<string> StableInputNames = new(StringComparer.Ordinal) {
            "tokens", "durations", "languages", "depth", "steps", "speedup",
        };

        static readonly HashSet<string> ConditionInputNames = new(StringComparer.Ordinal) {
            "f0", "gender", "velocity", "spk_embed",
            "energy", "breathiness", "voicing", "tension", "mouth_opening",
            "shift_mouth_opening",
        };

        static readonly AcousticRetakeStateCache states = new(StateCacheCapacity);
        static readonly object forceLock = new();
        static readonly List<ForceAcousticRetakeRequest> pendingForce = new();

        sealed class ForceAcousticRetakeRequest {
            public int PhrasePosition;
            public int PhraseEnd;
            public HashSet<int> SelectedAbsoluteNotePositions = new();
            public uint NoiseNonce;
        }

        public static bool Supports(InferenceSession session) =>
            session.InputNames.Contains("retake") && session.InputNames.Contains("gt_mel");

        public static bool Supports(InferenceSession session, DsConfig _) =>
            Supports(session);

        /// <summary>True when the track singer is DiffSinger and acoustic ONNX has retake + gt_mel.</summary>
        public static bool Supports(USinger? singer) {
            if (singer is not DiffSingerSinger dsSinger || !dsSinger.Found) {
                return false;
            }
            try {
                return dsSinger.SupportsAcousticRetake;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>
        /// Queue an explicit acoustic retake for phrases overlapping the selected notes.
        /// Consumed on the next acoustic render of each phrase.
        /// </summary>
        public static void QueueForceRetake(
            IEnumerable<(int position, int end)> phraseBounds,
            IEnumerable<int> selectedAbsoluteNotePositions) {
            var notes = selectedAbsoluteNotePositions.ToHashSet();
            if (notes.Count == 0) {
                return;
            }
            uint nonce = (uint)Random.Shared.Next(1, int.MaxValue);
            lock (forceLock) {
                foreach (var (position, end) in phraseBounds) {
                    pendingForce.RemoveAll(r => r.PhrasePosition == position && r.PhraseEnd == end);
                    pendingForce.Add(new ForceAcousticRetakeRequest {
                        PhrasePosition = position,
                        PhraseEnd = end,
                        SelectedAbsoluteNotePositions = new HashSet<int>(notes),
                        NoiseNonce = nonce,
                    });
                }
            }
        }

        /// <summary>
        /// If a force-retake was queued for this phrase, build a padded frame mask and noise nonce.
        /// </summary>
        public static bool TryConsumeForceRetake(
            RenderPhrase phrase,
            IReadOnlyList<int> paddedDurations,
            int totalFrames,
            float frameMs,
            out bool[] mask,
            out uint noiseNonce) {
            mask = Array.Empty<bool>();
            noiseNonce = 0;
            ForceAcousticRetakeRequest? req = null;
            lock (forceLock) {
                int idx = pendingForce.FindIndex(
                    r => r.PhrasePosition == phrase.position && r.PhraseEnd == phrase.end);
                if (idx < 0) {
                    return false;
                }
                req = pendingForce[idx];
                pendingForce.RemoveAt(idx);
            }
            noiseNonce = req.NoiseNonce;
            var noteRelative = new int[phrase.notes.Length];
            for (int i = 0; i < phrase.notes.Length; i++) {
                noteRelative[i] = phrase.notes[i].position;
            }
            var noteIndexes = DiffSingerRetake.MapSelectedPositionsToNoteIndexes(
                phrase.position, noteRelative, req.SelectedAbsoluteNotePositions);
            if (noteIndexes.Count == 0 || noteIndexes.Count >= phrase.notes.Length) {
                mask = Enumerable.Repeat(true, totalFrames).ToArray();
            } else {
                mask = BuildNoteFrameMask(phrase, paddedDurations, noteIndexes, totalFrames);
                int pad = Math.Max(1, (int)Math.Round(PadMs / Math.Max(frameMs, 1e-3f)));
                mask = PadMask(mask, pad);
            }
            return true;
        }

        static bool[] BuildNoteFrameMask(
            RenderPhrase phrase,
            IReadOnlyList<int> paddedDurations,
            ISet<int> selectedNoteIndexes,
            int totalFrames) {
            var mask = new bool[totalFrames];
            if (paddedDurations.Count != phrase.phones.Length + 2) {
                return Enumerable.Repeat(true, totalFrames).ToArray();
            }
            int frame = Math.Max(0, paddedDurations[0]);
            for (int i = 0; i < phrase.phones.Length; i++) {
                int noteIdx = FindNoteIndex(phrase, phrase.phones[i]);
                bool retake = noteIdx >= 0 && selectedNoteIndexes.Contains(noteIdx);
                int dur = Math.Max(0, paddedDurations[i + 1]);
                for (int f = 0; f < dur; f++) {
                    int fi = frame + f;
                    if (fi >= 0 && fi < totalFrames) {
                        mask[fi] = retake;
                    }
                }
                frame += dur;
            }
            return mask;
        }

        static int FindNoteIndex(RenderPhrase phrase, RenderPhone phone) {
            for (int i = 0; i < phrase.notes.Length; i++) {
                var note = phrase.notes[i];
                if (phone.position >= note.position && phone.position < note.end) {
                    return i;
                }
            }
            // Fallback: nearest note by start tick.
            int best = -1;
            int bestDist = int.MaxValue;
            for (int i = 0; i < phrase.notes.Length; i++) {
                int dist = Math.Abs(phrase.notes[i].position - phone.position);
                if (dist < bestDist) {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        public static ulong BuildScopeKey(
            ulong acousticHash,
            IEnumerable<NamedOnnxValue> inputs,
            int phrasePosition,
            int phraseEnd) {
            var stable = inputs
                .Where(v => StableInputNames.Contains(v.Name))
                .ToList();
            var baseHash = new DiffSingerCache(acousticHash, stable).Hash;
            return DiffSingerVariancePatch.BuildStateKey(baseHash, phrasePosition, phraseEnd);
        }

        public static Dictionary<string, float[]> CaptureConditions(IEnumerable<NamedOnnxValue> inputs) {
            var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (var input in inputs) {
                if (!ConditionInputNames.Contains(input.Name)) {
                    continue;
                }
                try {
                    result[input.Name] = input.AsTensor<float>().ToArray();
                } catch (InvalidOperationException) {
                    // Non-float tensor — ignore.
                }
            }
            return result;
        }

        public static bool TryGetState(ulong key, out AcousticRetakeState state) =>
            states.TryGetValue(key, out state);

        public static void SetState(ulong key, AcousticRetakeState state) =>
            states.Set(key, state);

        /// <summary>
        /// Builds a padded frame mask. Returns null when layout is incompatible (caller should full-retake).
        /// Returns all-false when nothing changed (caller may reuse previous mel/samples).
        /// </summary>
        public static bool[]? BuildRetakeMask(
            AcousticRetakeState previous,
            Dictionary<string, float[]> currentConditions,
            int totalFrames,
            float frameMs) {
            if (previous.totalFrames != totalFrames ||
                Math.Abs(previous.frameMs - frameMs) > 1e-4f) {
                return null;
            }
            if (previous.conditions.Count != currentConditions.Count ||
                previous.conditions.Keys.Any(k => !currentConditions.ContainsKey(k)) ||
                currentConditions.Keys.Any(k => !previous.conditions.ContainsKey(k))) {
                return null;
            }

            var mask = new bool[totalFrames];
            foreach (var (name, current) in currentConditions) {
                var prev = previous.conditions[name];
                bool[] part;
                if (prev.Length == totalFrames && current.Length == totalFrames) {
                    part = DiffSingerVariancePatch.BuildChangedFrameMask(prev, current, ConditionEpsilon);
                } else if (prev.Length == current.Length &&
                           current.Length > 0 &&
                           current.Length % totalFrames == 0) {
                    part = DiffSingerVariancePatch.BuildChangedFrameMask(
                        prev, current, totalFrames, ConditionEpsilon);
                } else {
                    return null;
                }
                for (int i = 0; i < totalFrames && i < part.Length; i++) {
                    if (part[i]) {
                        mask[i] = true;
                    }
                }
            }

            if (!mask.Any(x => x)) {
                return mask;
            }

            int pad = Math.Max(1, (int)Math.Round(PadMs / Math.Max(frameMs, 1e-3f)));
            mask = PadMask(mask, pad);

            int changed = mask.Count(x => x);
            if (changed >= (int)Math.Ceiling(totalFrames * FullRetakeFraction)) {
                return Enumerable.Repeat(true, totalFrames).ToArray();
            }
            return mask;
        }

        internal static bool[] PadMask(IReadOnlyList<bool> mask, int pad) {
            if (pad <= 0) {
                return mask.ToArray();
            }
            var result = new bool[mask.Count];
            for (int i = 0; i < mask.Count; i++) {
                if (!mask[i]) {
                    continue;
                }
                int start = Math.Max(0, i - pad);
                int end = Math.Min(mask.Count - 1, i + pad);
                for (int j = start; j <= end; j++) {
                    result[j] = true;
                }
            }
            return result;
        }

        /// <summary>
        /// Merges contiguous retake frames into half-open sample ranges [start, end).
        /// The last frame group extends to <paramref name="sampleCount"/>.
        /// </summary>
        public static List<(int start, int end)> FramesToSampleRanges(
            IReadOnlyList<bool> frameMask,
            int hopSize,
            int sampleCount) {
            var ranges = new List<(int start, int end)>();
            if (hopSize <= 0 || sampleCount <= 0 || frameMask.Count == 0) {
                return ranges;
            }
            int t = 0;
            while (t < frameMask.Count) {
                if (!frameMask[t]) {
                    t++;
                    continue;
                }
                int t0 = t;
                while (t < frameMask.Count && frameMask[t]) {
                    t++;
                }
                int start = Math.Min(t0 * hopSize, sampleCount);
                int end = t >= frameMask.Count
                    ? sampleCount
                    : Math.Min(t * hopSize, sampleCount);
                if (end > start) {
                    ranges.Add((start, end));
                }
            }
            return ranges;
        }

        /// <summary>
        /// Keep previous samples outside the retake mask; use predicted inside,
        /// with an equal-power cosine crossfade centered on range boundaries
        /// (extends into keep so the splice is not a hard cut).
        /// </summary>
        public static float[] HardComposeSamples(
            float[] previous,
            float[] predicted,
            IReadOnlyList<bool> frameMask,
            int hopSize,
            int crossfadeSamples) {
            if (previous.Length != predicted.Length ||
                hopSize <= 0 ||
                frameMask.Count == 0) {
                return (float[])predicted.Clone();
            }
            if (!frameMask.Any(x => x)) {
                return (float[])previous.Clone();
            }
            if (frameMask.All(x => x)) {
                return (float[])predicted.Clone();
            }

            var result = (float[])previous.Clone();
            int fade = Math.Max(0, crossfadeSamples);
            foreach (var (start, end) in FramesToSampleRanges(frameMask, hopSize, predicted.Length)) {
                ApplyPredictedRangeWithCrossfade(result, predicted, start, end, fade);
            }
            return result;
        }

        public static int DefaultCrossfadeSamples(int hopSize, int sampleRate = 44100) {
            int fromMs = Math.Max(1, (int)Math.Round(Math.Max(1, sampleRate) * SampleCrossfadeMs / 1000.0));
            // At least two hops so the fade covers a meaningful spectral window.
            return Math.Max(fromMs, Math.Max(1, hopSize) * 2);
        }

        /// <summary>
        /// Equal-power weight for predicted in [0,1]: sin(π/2 · t).
        /// </summary>
        internal static float EqualPowerIn(float t) {
            t = Math.Clamp(t, 0f, 1f);
            return MathF.Sin(t * (MathF.PI * 0.5f));
        }

        static void ApplyPredictedRangeWithCrossfade(
            float[] dest,
            float[] predicted,
            int start,
            int end,
            int fade) {
            if (end <= start) {
                return;
            }
            int edge = Math.Max(0, fade);
            int blendStart = Math.Max(0, start - edge);
            int blendEnd = Math.Min(dest.Length, end + edge);
            float denom = Math.Max(1f, 2f * edge);

            for (int i = blendStart; i < blendEnd; i++) {
                float left;
                float right;
                if (edge == 0) {
                    left = i >= start ? 1f : 0f;
                    right = i < end ? 1f : 0f;
                } else {
                    // Soft gate: 0 at start-edge / end+edge, ~1 across the interior.
                    left = (i - (start - edge)) / denom;
                    right = ((end + edge) - i) / denom;
                }
                float t = Math.Min(Math.Clamp(left, 0f, 1f), Math.Clamp(right, 0f, 1f));
                float w = edge == 0 ? t : EqualPowerIn(t);
                float wPrev = MathF.Sqrt(Math.Max(0f, 1f - w * w));
                dest[i] = dest[i] * wPrev + predicted[i] * w;
            }
        }

        public static Tensor<float> RestoreMel(AcousticRetakeState state) {
            return new DenseTensor<float>((float[])state.mel.Clone(), state.melDims);
        }

        public static AcousticRetakeState CaptureState(
            Dictionary<string, float[]> conditions,
            Tensor<float> mel,
            int totalFrames,
            int melBins,
            float frameMs,
            float[]? rawSamples = null,
            int sampleRate = 0,
            int hopSize = 0) {
            var dims = mel.Dimensions.ToArray();
            return new AcousticRetakeState(
                conditions,
                mel.ToArray(),
                dims,
                totalFrames,
                melBins,
                frameMs,
                rawSamples,
                sampleRate,
                hopSize);
        }

        /// <summary>
        /// Keep previous mel where retake is false; use predicted where true.
        /// Expected layout: [1, T, mel_bins].
        /// </summary>
        public static Tensor<float> HardComposeMel(
            Tensor<float> previous,
            Tensor<float> predicted,
            IReadOnlyList<bool> frameMask) {
            if (!IsMelLayoutCompatible(previous, predicted, frameMask.Count)) {
                return predicted.Clone();
            }
            int tFrames = previous.Dimensions[1];
            int bins = previous.Dimensions[2];
            var result = previous.Clone();
            for (int t = 0; t < tFrames; t++) {
                if (!frameMask[t]) {
                    continue;
                }
                for (int c = 0; c < bins; c++) {
                    result[0, t, c] = predicted[0, t, c];
                }
            }
            return result;
        }

        public static bool IsMelLayoutCompatible(
            Tensor<float> previous,
            Tensor<float> predicted,
            int frameCount) {
            return previous.Dimensions.Length == 3 &&
                predicted.Dimensions.Length == 3 &&
                previous.Dimensions[0] == 1 &&
                predicted.Dimensions[0] == 1 &&
                previous.Dimensions[1] == frameCount &&
                predicted.Dimensions[1] == frameCount &&
                previous.Dimensions[2] == predicted.Dimensions[2] &&
                previous.Dimensions[2] > 0;
        }

        public static bool IsMelCompatibleWithState(AcousticRetakeState state, Tensor<float> mel) {
            return mel.Dimensions.Length == 3 &&
                mel.Dimensions[0] == 1 &&
                mel.Dimensions[1] == state.totalFrames &&
                mel.Dimensions[2] == state.melBins &&
                state.melDims.Length == 3 &&
                state.melDims[1] == state.totalFrames &&
                state.melDims[2] == state.melBins;
        }
    }

    /// <summary>Public entry points for acoustic retake (UI / batch edits outside this assembly).</summary>
    public static class DiffSingerAcousticRetakeApi {
        public static bool Supports(USinger? singer) =>
            DiffSingerAcousticRetake.Supports(singer);

        public static void QueueForceRetake(
            IEnumerable<(int position, int end)> phraseBounds,
            IEnumerable<int> selectedAbsoluteNotePositions) =>
            DiffSingerAcousticRetake.QueueForceRetake(phraseBounds, selectedAbsoluteNotePositions);
    }
}
