using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    /// <summary>
    /// Queues an explicit DiffSinger acoustic retake and records prior mel/sample
    /// state so Ctrl+Z can restore the previous take.
    /// </summary>
    public class AcousticRetakeCommand : UCommand {
        readonly UVoicePart part;
        readonly (int position, int end)[] phraseBounds;
        readonly ulong[] phraseHashes;
        readonly HashSet<int> selectedAbsoluteNotePositions;
        readonly uint noiseNonce;
        readonly DiffSingerAcousticRetake.PhraseStateSnapshot[] beforeStates;
        readonly bool applied;

        public AcousticRetakeCommand(
            UVoicePart part,
            IReadOnlyList<RenderPhrase> phrases,
            IEnumerable<int> selectedAbsoluteNotePositions,
            uint noiseNonce) {
            this.part = part;
            phraseBounds = phrases.Select(p => (p.position, p.end)).ToArray();
            phraseHashes = phrases.Select(p => p.hash).ToArray();
            this.selectedAbsoluteNotePositions = selectedAbsoluteNotePositions.ToHashSet();
            this.noiseNonce = noiseNonce == 0 ? 1u : noiseNonce;
            beforeStates = DiffSingerAcousticRetake.SnapshotPhraseStates(phraseBounds);
            applied = phraseBounds.Length > 0 && this.selectedAbsoluteNotePositions.Count > 0;
        }

        public override ValidateOptions ValidateOptions => new() {
            SkipTiming = true,
            SkipPhonemizer = true,
            SkipPhoneme = true,
        };

        public override Pipeline.ImpactSet Impact => Pipeline.ImpactSet.PartOf(part);

        public override void Execute() {
            if (!applied) {
                return;
            }
            DiffSingerAcousticRetake.QueueForceRetake(
                phraseBounds, selectedAbsoluteNotePositions, noiseNonce);
            InvalidatePhrases();
        }

        public override void Unexecute() {
            if (!applied) {
                return;
            }
            DiffSingerAcousticRetake.ClearPendingForceRetake(phraseBounds);
            DiffSingerAcousticRetake.RestorePhraseStates(beforeStates);
            InvalidatePhrases();
        }

        void InvalidatePhrases() {
            var planner = PlaybackManager.Inst.MixPlanner;
            double holePadMs = DiffSingerAcousticRetake.PadMs
                + DiffSingerAcousticRetake.SampleCrossfadeMs;
            for (int i = 0; i < phraseBounds.Length; i++) {
                var (position, end) = phraseBounds[i];
                var phrase = part.renderPhrases.FirstOrDefault(
                    p => p.position == position && p.end == end);
                ulong hash = phrase?.hash ?? (i < phraseHashes.Length ? phraseHashes[i] : 0);
                phrase?.DeleteCacheFiles();
                if (phrase != null) {
                    var selectedInPhrase = phrase.notes
                        .Where(n => selectedAbsoluteNotePositions.Contains(phrase.position + n.position))
                        .ToArray();
                    var layout = phrase.renderer.Layout(phrase);
                    double phraseStartMs = layout.positionMs - layout.leadingMs;
                    double phraseEndMs = phraseStartMs + layout.estimatedLengthMs;
                    if (selectedInPhrase.Length >= phrase.notes.Length || selectedInPhrase.Length == 0) {
                        PhraseWaveformCache.ClearDisplayAll(hash);
                        PhraseWaveformCache.SetRendering(
                            part.trackNo, hash, new[] { (phraseStartMs, phraseEndMs) });
                    } else {
                        var holes = selectedInPhrase
                            .Select(n => (n.positionMs - holePadMs, n.endMs + holePadMs))
                            .ToArray();
                        PhraseWaveformCache.ClearDisplayRanges(hash, holes);
                        PhraseWaveformCache.SetRendering(part.trackNo, hash, holes);
                    }
                } else {
                    PhraseWaveformCache.ClearDisplayAll(hash);
                }
                planner.MarkFailed(part, hash);
            }
            part.SetRenderMixComplete(false);
            planner.InvalidatePartCompleteness(part);
        }

        public override string ToString() => "Acoustic retake";
    }
}
