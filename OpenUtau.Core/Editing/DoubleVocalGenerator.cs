using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Editing {
    /// <summary>User options for DiffSinger double-vocal creation.</summary>
    public sealed class DoubleVocalOptions {
        public bool RandomizePhonemeTimings { get; set; } = true;
        /// <summary>Max |offset| in ticks for phoneme timing randomization.</summary>
        public int MaxPhonemeOffsetTick { get; set; } = DoubleVocalGenerator.DefaultMaxPhonemeOffsetTick;
        public bool WarpPitchCurve { get; set; } = true;
        public bool GeneratePitch { get; set; } = true;
        /// <summary>PEXP used when generating pitch (0–100).</summary>
        public int PitchExpressionPercent { get; set; } = DoubleVocalGenerator.DefaultPitchExpressionPercent;
        /// <summary>How many double tracks to create (1–<see cref="DoubleVocalGenerator.MaxDoubleCount"/>).</summary>
        public int Count { get; set; } = 1;

        public bool NeedsPhonemes => RandomizePhonemeTimings || WarpPitchCurve || GeneratePitch;
        public bool NeedsPhrases => GeneratePitch;
    }

    /// <summary>
    /// Creates a DiffSinger "double vocal" track: clone part, optional phoneme
    /// offset randomize, optional PITD warp, optional pitch generate at PEXP and add.
    /// </summary>
    public static class DoubleVocalGenerator {
        public const int DefaultPitchExpressionPercent = 45;
        public const int DefaultMaxPhonemeOffsetTick = 15;
        public const int MaxDoubleCount = 8;

        public static bool CanCreateDoubleVocal(UProject project, UPart? part) {
            if (part is not UVoicePart voicePart) {
                return false;
            }
            if (voicePart.trackNo < 0 || voicePart.trackNo >= project.tracks.Count) {
                return false;
            }
            var track = project.tracks[voicePart.trackNo];
            return track.Singer != null && track.Singer.Found
                && track.Singer.SingerType == USingerType.DiffSinger;
        }

        /// <summary>
        /// Clone track + part only. Does not phonemize / randomize / generate pitch.
        /// Must run on the UI/doc thread inside an open undo group.
        /// </summary>
        /// <param name="insertAt">Track index to insert at; default is directly under the source track.</param>
        public static UVoicePart CloneTrackAndPart(
            UProject project,
            UVoicePart sourcePart,
            int? insertAt = null) {
            if (!CanCreateDoubleVocal(project, sourcePart)) {
                throw new InvalidOperationException("Double vocal requires a DiffSinger voice part.");
            }
            var sourceTrack = project.tracks[sourcePart.trackNo];
            int at = insertAt ?? sourcePart.trackNo + 1;
            at = Math.Clamp(at, 0, project.tracks.Count);
            var track = new UTrack(AllocateDoubleTrackName(project, sourceTrack.TrackName)) {
                TrackNo = at,
                Singer = sourceTrack.Singer,
                Phonemizer = sourceTrack.Phonemizer,
                RendererSettings = sourceTrack.RendererSettings.Clone(),
                Mute = sourceTrack.Mute,
                Muted = sourceTrack.Muted,
                Solo = false,
                Volume = sourceTrack.Volume,
                Pan = sourceTrack.Pan,
                TrackColor = sourceTrack.TrackColor,
                TrackExpressions = sourceTrack.TrackExpressions.Select(exp => exp.Clone()).ToList(),
                ExpressionDefaultOverrides = ExpressionDefaultResolver.CloneOverrides(sourceTrack),
            };
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
            track.RendererSettings.Validate(track);

            var part = (UVoicePart)sourcePart.Clone();
            part.name = sourcePart.name;
            part.trackNo = track.TrackNo;
            DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            return part;
        }

        /// <summary>
        /// Unique track name: "Name (Double)", then "Name (Double 2)", "Name (Double 3)", …
        /// </summary>
        public static string AllocateDoubleTrackName(UProject project, string sourceTrackName) {
            sourceTrackName ??= string.Empty;
            string first = $"{sourceTrackName} (Double)";
            if (!IsTrackNameTaken(project, first)) {
                return first;
            }
            for (int n = 2; n < 10000; n++) {
                string candidate = $"{sourceTrackName} (Double {n})";
                if (!IsTrackNameTaken(project, candidate)) {
                    return candidate;
                }
            }
            return $"{sourceTrackName} (Double {Guid.NewGuid():N})";
        }

        static bool IsTrackNameTaken(UProject project, string name) {
            return project.tracks.Any(t => string.Equals(t.TrackName, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Push a phonemizer request for the cloned part (UI thread).
        /// </summary>
        public static void RequestPhonemes(UProject project, UVoicePart part) {
            project.Validate(new ValidateOptions {
                SkipTiming = true,
                Part = part,
            });
        }

        /// <summary>
        /// Wait until phonemes are applied. Safe to call from an async UI method
        /// (yields so PhonemizerRunner can post results onto the main scheduler).
        /// </summary>
        public static async Task WaitForPhonemesAsync(
            UVoicePart part,
            CancellationToken cancellationToken,
            TimeSpan timeout) {
            var sw = Stopwatch.StartNew();
            while (!part.PhonemesUpToDate || part.phonemes.Count == 0) {
                cancellationToken.ThrowIfCancellationRequested();
                if (sw.Elapsed > timeout) {
                    throw new TimeoutException("Timed out waiting for phonemizer on double-vocal part.");
                }
                // Let the UI/main scheduler apply PhonemizerRunner.SendResponse.
                await Task.Delay(50, cancellationToken).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Optional: randomize phoneme offsets, warp PITD, set flat PEXP.
        /// Requires phonemes when randomize/warp are enabled.
        /// UI/doc thread, open undo group.
        /// </summary>
        public static void ApplyTimingAndExpression(
            UProject project,
            UVoicePart part,
            DoubleVocalOptions options) {
            options ??= new DoubleVocalOptions();
            if (options.RandomizePhonemeTimings || options.WarpPitchCurve) {
                if (!part.PhonemesUpToDate || part.phonemes.Count == 0) {
                    throw new InvalidOperationException("Double vocal part has no phonemes yet.");
                }
            }
            var track = project.tracks[part.trackNo];

            var oldBoundaries = GetPhonemeBoundaryTicks(part);
            var pitdBefore = part.curves.FirstOrDefault(c => c.abbr == Format.Ustx.PITD);
            int[]? oldXs = pitdBefore?.xs.ToArray();
            int[]? oldYs = pitdBefore?.ys.ToArray();
            int[]? oldBreaks = pitdBefore?.breaks?.ToArray();

            if (options.RandomizePhonemeTimings) {
                ApplyPhonemeOffsetRandomize(
                    project, part, Math.Clamp(options.MaxPhonemeOffsetTick, 1, 480));
                project.Validate(new ValidateOptions {
                    SkipTiming = true,
                    Part = part,
                    SkipPhonemizer = true,
                });
            }

            if (options.WarpPitchCurve
                && oldXs != null && oldYs != null && oldXs.Length > 0
                && oldBoundaries.Count >= 2) {
                var newBoundaries = GetPhonemeBoundaryTicks(part);
                if (newBoundaries.Count == oldBoundaries.Count) {
                    var (newXs, newYs) = WarpCurvePoints(oldBoundaries, newBoundaries, oldXs, oldYs);
                    DocManager.Inst.ExecuteCmd(new MergedSetCurveCommand(
                        project, part, Format.Ustx.PITD,
                        oldXs, oldYs, oldBreaks,
                        newXs, newYs, oldBreaks));
                }
            }

            if (options.GeneratePitch) {
                ApplyFlatPitchExpression(
                    project, part, track,
                    Math.Clamp(options.PitchExpressionPercent, 0, 100));
            }

            if (options.RandomizePhonemeTimings || options.WarpPitchCurve || options.GeneratePitch) {
                project.Validate(new ValidateOptions {
                    SkipTiming = true,
                    Part = part,
                    SkipPhonemizer = true,
                });
            }
        }

        /// <summary>Legacy name kept for callers; prefer <see cref="ApplyTimingAndExpression"/>.</summary>
        public static void RandomizeWarpAndSetExpr(UProject project, UVoicePart part) {
            ApplyTimingAndExpression(project, part, new DoubleVocalOptions());
        }

        /// <summary>
        /// Wait until render phrases for the latest validate have landed.
        /// </summary>
        public static async Task WaitForPhrasesAsync(
            UVoicePart part,
            CancellationToken cancellationToken,
            TimeSpan timeout) {
            await Task.Run(() => {
                cancellationToken.ThrowIfCancellationRequested();
                if (!part.WaitPhraseSource(timeout)) {
                    throw new TimeoutException("Timed out waiting for render phrases on double-vocal part.");
                }
            }, cancellationToken).ConfigureAwait(true);
        }

        /// <summary>
        /// Background-safe: run DiffSinger pitch and return part-relative (x, y) deviations.
        /// Caller must ensure <see cref="UVoicePart.renderPhrases"/> are ready.
        /// </summary>
        public static List<(int x, int y)> CollectGeneratedPitchPoints(
            UProject project,
            UVoicePart part,
            Action<int, int> setProgressCallback,
            CancellationToken cancellationToken) {
            var points = new List<(int x, int y)>();
            if (part.trackNo < 0 || part.trackNo >= project.tracks.Count) {
                return points;
            }
            var track = project.tracks[part.trackNo];
            track.RendererSettings.Validate(track);
            var renderer = track.RendererSettings.Renderer;
            if (renderer == null || !renderer.SupportsRenderPitch) {
                return points;
            }

            var notes = part.notes.ToList();
            var positions = notes.Select(n => n.position + part.position).ToHashSet();
            var phrases = part.renderPhrases
                .Where(phrase => phrase.notes.Any(n => positions.Contains(phrase.position + n.position)))
                .ToArray();
            float minPitD = -1200;
            if (project.expressions.TryGetValue(Format.Ustx.PITD, out var descriptor)) {
                minPitD = descriptor.min;
            }

            int finished = 0;
            setProgressCallback(0, Math.Max(1, phrases.Length));
            if (phrases.Length == 0) {
                return points;
            }
            for (int ph_i = phrases.Length - 1; ph_i >= 0; ph_i--) {
                if (cancellationToken.IsCancellationRequested) {
                    break;
                }
                var phrase = phrases[ph_i];
                RenderPitchResult? result = renderer.LoadRenderedPitch(phrase, positions);
                if (result == null) {
                    finished += 1;
                    setProgressCallback(finished, phrases.Length);
                    continue;
                }
                for (int i = 0; i < result.tones.Length; i++) {
                    if (result.tones[i] < 0) {
                        continue;
                    }
                    if (result.retakeMask != null && i < result.retakeMask.Length && !result.retakeMask[i]) {
                        continue;
                    }
                    if (result.voiced != null && i < result.voiced.Length && !result.voiced[i]) {
                        continue;
                    }
                    int x = phrase.position - part.position + (int)result.ticks[i];
                    if (result.ticks[i] < 0) {
                        if (i + 1 < result.ticks.Length && result.ticks[i + 1] > 0) { } else {
                            continue;
                        }
                    }
                    if (x >= phrase.position + phrase.duration) {
                        i = result.tones.Length - 1;
                    }
                    if (phrase.pitchesBeforeDeviation == null || phrase.pitchesBeforeDeviation.Length == 0) {
                        continue;
                    }
                    int pitchIndex = Math.Clamp(
                        (x - (phrase.position - part.position - phrase.leading)) / 5,
                        0, phrase.pitchesBeforeDeviation.Length - 1);
                    float basePitch = phrase.pitchesBeforeDeviation[pitchIndex];
                    int y = (int)(result.tones[i] * 100 - basePitch);
                    if (y > minPitD) {
                        points.Add((x, y));
                    }
                }
                finished += 1;
                setProgressCallback(finished, phrases.Length);
            }
            return points;
        }

        /// <summary>
        /// Apply generated pitch as a deviation onto the existing (e.g. warped) PITD:
        /// result[x] = sample(existing, x) + gen[x]. UI/doc thread, open undo group.
        /// </summary>
        public static void ApplyAdditivePitch(
            UProject project,
            UVoicePart part,
            IReadOnlyList<(int x, int y)> generated) {
            if (generated == null || generated.Count == 0) {
                return;
            }
            if (!project.expressions.TryGetValue(Format.Ustx.PITD, out var descriptor)) {
                return;
            }
            var curve = part.curves.FirstOrDefault(c => c.abbr == Format.Ustx.PITD);
            int empty = ExpressionDefaultResolver.GetEffectiveDefaultInt(
                project, project.tracks[part.trackNo], Format.Ustx.PITD);
            int[] oldXs = curve?.xs.ToArray() ?? Array.Empty<int>();
            int[] oldYs = curve?.ys.ToArray() ?? Array.Empty<int>();
            int[]? oldBreaks = curve?.breaks?.ToArray();

            var (newXs, newYs) = MergeAdditivePitchDeviation(
                oldXs, oldYs, generated, empty, descriptor.min, descriptor.max);

            DocManager.Inst.ExecuteCmd(new MergedSetCurveCommand(
                project, part, Format.Ustx.PITD,
                oldXs, oldYs, oldBreaks,
                newXs, newYs, oldBreaks));
        }

        /// <summary>
        /// Pointwise: existing_curve(x) + generated_deviation(x).
        /// Inside the generated X range, only deviation-adjusted samples are kept
        /// (stale original knots are dropped so they cannot create pitch jumps).
        /// Outside that range, original knots are preserved.
        /// </summary>
        public static (int[] xs, int[] ys) MergeAdditivePitchDeviation(
            IReadOnlyList<int> oldXs,
            IReadOnlyList<int> oldYs,
            IReadOnlyList<(int x, int y)> generated,
            int emptyValue,
            float min,
            float max) {
            oldXs ??= Array.Empty<int>();
            oldYs ??= Array.Empty<int>();
            if (generated == null || generated.Count == 0) {
                return (oldXs.ToArray(), oldYs.ToArray());
            }

            var snappedGen = new SortedDictionary<int, int>();
            foreach (var (xRaw, yGen) in generated) {
                int x = (int)Math.Round((float)xRaw / UCurve.interval) * UCurve.interval;
                snappedGen[x] = yGen; // last write wins; never stack on a prior add
            }
            if (snappedGen.Count == 0) {
                return (oldXs.ToArray(), oldYs.ToArray());
            }

            var sampler = new UCurve {
                xs = oldXs.ToList(),
                ys = oldYs.ToList(),
            };
            int rangeMin = snappedGen.Keys.First();
            int rangeMax = snappedGen.Keys.Last();
            var byX = new SortedDictionary<int, int>();

            for (int i = 0; i < oldXs.Count && i < oldYs.Count; i++) {
                int x = oldXs[i];
                if (x < rangeMin || x > rangeMax) {
                    byX[x] = oldYs[i];
                }
            }

            foreach (var (x, yGen) in snappedGen) {
                int oldY = sampler.Sample(x, emptyValue);
                byX[x] = AddPitchDeviation(oldY, yGen, min, max);
            }

            return (byX.Keys.ToArray(), byX.Values.ToArray());
        }

        public static bool SupportsPitchGeneration(UProject project, UVoicePart part) {
            if (part.trackNo < 0 || part.trackNo >= project.tracks.Count) {
                return false;
            }
            var track = project.tracks[part.trackNo];
            track.RendererSettings.Validate(track);
            var renderer = track.RendererSettings.Renderer;
            return renderer != null && renderer.SupportsRenderPitch;
        }

        public static (int[] xs, int[] ys) WarpCurvePoints(
            IReadOnlyList<int> oldBoundaries,
            IReadOnlyList<int> newBoundaries,
            IReadOnlyList<int> xs,
            IReadOnlyList<int> ys) {
            if (oldBoundaries.Count != newBoundaries.Count || oldBoundaries.Count < 2
                || xs.Count != ys.Count || xs.Count == 0) {
                return (xs.ToArray(), ys.ToArray());
            }
            var byX = new SortedDictionary<int, int>();
            for (int i = 0; i < xs.Count; i++) {
                int mapped = MapTick(xs[i], oldBoundaries, newBoundaries);
                mapped = (int)Math.Round((float)mapped / UCurve.interval) * UCurve.interval;
                byX[mapped] = ys[i];
            }
            return (byX.Keys.ToArray(), byX.Values.ToArray());
        }

        public static int MapTick(
            int x,
            IReadOnlyList<int> oldBoundaries,
            IReadOnlyList<int> newBoundaries) {
            if (oldBoundaries.Count != newBoundaries.Count || oldBoundaries.Count < 2) {
                return x;
            }
            if (x <= oldBoundaries[0]) {
                return newBoundaries[0] + (x - oldBoundaries[0]);
            }
            if (x >= oldBoundaries[oldBoundaries.Count - 1]) {
                int last = oldBoundaries.Count - 1;
                return newBoundaries[last] + (x - oldBoundaries[last]);
            }
            for (int i = 0; i < oldBoundaries.Count - 1; i++) {
                int o0 = oldBoundaries[i];
                int o1 = oldBoundaries[i + 1];
                if (x >= o0 && x <= o1) {
                    if (o1 == o0) {
                        return newBoundaries[i];
                    }
                    double t = (double)(x - o0) / (o1 - o0);
                    return (int)Math.Round(newBoundaries[i] + t * (newBoundaries[i + 1] - newBoundaries[i]));
                }
            }
            return x;
        }

        public static int AddPitchDeviation(int oldY, int generatedY, float min, float max) {
            return (int)Math.Clamp(oldY + generatedY, min, max);
        }

        static List<int> GetPhonemeBoundaryTicks(UVoicePart part) {
            var ticks = part.phonemes
                .Select(p => p.position)
                .OrderBy(t => t)
                .Distinct()
                .ToList();
            if (part.phonemes.Count > 0) {
                int end = part.phonemes.Max(p => p.End);
                if (ticks.Count == 0 || ticks[^1] != end) {
                    ticks.Add(end);
                }
            } else if (part.Duration > 0) {
                ticks.Add(0);
                ticks.Add(part.Duration);
            }
            return ticks;
        }

        static void ApplyPhonemeOffsetRandomize(UProject project, UVoicePart part, int maxPhonemeOffsetTick) {
            var notes = part.notes.ToList();
            if (notes.Count == 0 || part.phonemes.Count == 0) {
                return;
            }
            var random = new Random();
            foreach (var note in notes) {
                for (int i = 0; i < part.phonemes.Count; i++) {
                    UPhoneme phoneme = part.phonemes[i];
                    if (phoneme.Parent != note) {
                        continue;
                    }
                    int existing = note.GetPhonemeOverride(phoneme.index).offset ?? 0;
                    if (random.Next(2) == 0) {
                        var tempo = project.timeAxis.GetBpmAtTick(phoneme.position);
                        var max = Math.Min(
                            maxPhonemeOffsetTick,
                            (int)Math.Round(MusicMath.TempoTickToMs(tempo, phoneme.Duration) / 4));
                        if (max < 1) {
                            continue;
                        }
                        int lo = Math.Max(1, max / 4);
                        int delta = random.Next(lo, max + 1);
                        DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(
                            part, note, phoneme.index, existing + delta));
                    } else {
                        var max = maxPhonemeOffsetTick;
                        if (phoneme.Prev != null && phoneme.Prev.End == phoneme.position) {
                            var tempo = project.timeAxis.GetBpmAtTick(part.phonemes[i - 1].position);
                            max = Math.Min(
                                maxPhonemeOffsetTick,
                                (int)Math.Round(MusicMath.TempoTickToMs(tempo, part.phonemes[i - 1].Duration) / 4));
                        }
                        if (max < 1) {
                            continue;
                        }
                        int lo = Math.Max(1, max / 4);
                        int delta = random.Next(lo, max + 1);
                        DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(
                            part, note, phoneme.index, existing - delta));
                    }
                }
            }
        }

        static void ApplyFlatPitchExpression(
            UProject project, UVoicePart part, UTrack track, int pitchExpressionPercent) {
            if (track.Singer is not DiffSingerSinger dsSinger || !dsSinger.PitchPredictorUsesExpr) {
                return;
            }
            if (!project.expressions.TryGetValue(DiffSingerUtils.PEXP, out var descriptor)) {
                return;
            }
            var curve = part.curves.FirstOrDefault(c => c.abbr == DiffSingerUtils.PEXP);
            int[] oldXs = curve?.xs.ToArray() ?? Array.Empty<int>();
            int[] oldYs = curve?.ys.ToArray() ?? Array.Empty<int>();
            int[]? oldBreaks = curve?.breaks?.ToArray();
            int y = (int)Math.Clamp(pitchExpressionPercent, descriptor.min, descriptor.max);
            int duration = Math.Max(part.Duration, UCurve.interval);
            int[] newXs = { 0, duration };
            int[] newYs = { y, y };
            DocManager.Inst.ExecuteCmd(new MergedSetCurveCommand(
                project, part, DiffSingerUtils.PEXP,
                oldXs, oldYs, oldBreaks,
                newXs, newYs, null));
        }
    }
}
