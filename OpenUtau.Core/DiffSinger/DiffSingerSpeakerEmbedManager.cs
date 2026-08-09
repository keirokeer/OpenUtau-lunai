using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime.Tensors;
using NumSharp;
using Serilog;

using OpenUtau.Core.Render;

namespace OpenUtau.Core.DiffSinger
{
    public class DiffSingerSpeakerEmbedManager
    {
        DsConfig dsConfig;
        string rootPath;
        public NDArray speakerEmbeds = null;
        const string VoiceColorHeader = DiffSingerUtils.VoiceColorHeader;

        public DiffSingerSpeakerEmbedManager(DsConfig dsConfig, string rootPath) {
            this.dsConfig = dsConfig;
            this.rootPath = rootPath;
        }
        public NDArray loadSpeakerEmbed(string speaker) {
            string path = Path.Join(rootPath, speaker + ".emb");
            if(File.Exists(path)) {
                using var reader = new BinaryReader(File.OpenRead(path));
                return np.array<float>(Enumerable.Range(0, dsConfig.hiddenSize)
                    .Select(i => reader.ReadSingle()));
            } else {
                throw new Exception($"Speaker embed file {path} not found");
            }
        }

        public NDArray getSpeakerEmbeds() {
            if(speakerEmbeds == null) {
                if(dsConfig.speakers == null) {
                    return null;
                } else {
                    var embeds = np.zeros<float>(dsConfig.hiddenSize, dsConfig.speakers.Count);
                    foreach(var spkId in Enumerable.Range(0, dsConfig.speakers.Count)) {
                        embeds[":", spkId] = loadSpeakerEmbed(dsConfig.speakers[spkId]);
                    }
                    speakerEmbeds = embeds;
                }
            }
            return speakerEmbeds;
        }

        public bool IsVoiceColorCurve(string abbr, out int subBankId) {
            subBankId = 0;
            if (abbr.StartsWith(VoiceColorHeader) && int.TryParse(abbr.Substring(2), out subBankId)) {;
                subBankId -= 1;
                return true;
            } else {
                return false;
            }
        }

        static readonly HashSet<string> warnedMissingSpeakerSuffixes = new();

        public int getSpeakerIndexBySuffix(string suffix) {
            var speakerIndex = dsConfig.speakers.IndexOf(suffix);
            if (speakerIndex >= 0) {
                return speakerIndex;
            }
            speakerIndex = dsConfig.speakers.FindIndex(s => {
                var spSegs = s.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var sfSegs = suffix.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return sfSegs.Length <= spSegs.Length
                    && spSegs[^sfSegs.Length..].SequenceEqual(sfSegs);
            });
            if (speakerIndex >= 0) {
                return speakerIndex;
            }
            if (dsConfig.speakers == null || dsConfig.speakers.Count == 0) {
                throw new InvalidOperationException(
                    "Subbanks are defined in character.yaml but \"speakers\" is empty in dsconfig.yaml.");
            }
            var fallback = dsConfig.speakers[0];
            var warnKey = $"{rootPath}|{suffix}|{fallback}";
            lock (warnedMissingSpeakerSuffixes) {
                if (warnedMissingSpeakerSuffixes.Add(warnKey)) {
                    Log.Warning(
                        "Speaker suffix \"{Suffix}\" not found in dsConfig.speakers ({Candidates}). Falling back to \"{Fallback}\".",
                        suffix,
                        string.Join(", ", dsConfig.speakers),
                        fallback);
                }
            }
            return 0;
        }

        //used by phonemizer (duration model)
        public Tensor<float> PhraseSpeakerEmbedByPhone(string[] speakerByPhone){
            var hiddenSize = dsConfig.hiddenSize;
            var speakerEmbeds = getSpeakerEmbeds();
            var totalPhones = speakerByPhone.Length;
            NDArray spkCurves = np.zeros<float>(totalPhones, dsConfig.speakers.Count);
            foreach(int phoneId in Enumerable.Range(0,totalPhones)) {
                var spkId = getSpeakerIndexBySuffix(speakerByPhone[phoneId]);
                spkCurves[phoneId, spkId] = 1;
            }
            var spkEmbedResult = np.dot(spkCurves, speakerEmbeds.T);
            var spkEmbedTensor = new DenseTensor<float>(spkEmbedResult.ToArray<float>(), 
                new int[] { totalPhones, hiddenSize })
                .Reshape(new int[] { 1, totalPhones, hiddenSize });
            return spkEmbedTensor;
        }

        //used by variance, pitch and acoustic
        public Tensor<float> PhraseSpeakerEmbedByFrame(RenderPhrase phrase, IList<int> durations, float frameMs, int totalFrames, int headFrames, int tailFrames){
            var singer = phrase.singer;
            var hiddenSize = dsConfig.hiddenSize;
            var speakerEmbeds = getSpeakerEmbeds();
            // Per-frame mix origin follows CLR / phoneme suffix (same as pre–voice-color-max).
            // Voice-color curves then deviate from that base with linear mix (supports >100%).
            var headDefaultSpk = getSpeakerIndexBySuffix(phrase.phones[0].suffix);
            var tailDefaultSpk = getSpeakerIndexBySuffix(phrase.phones[^1].suffix);
            var defaultSpkByFrame = Enumerable.Repeat(headDefaultSpk, headFrames).ToList();
            defaultSpkByFrame.AddRange(Enumerable.Range(0, phrase.phones.Length)
                .SelectMany(phIndex => Enumerable.Repeat(
                    getSpeakerIndexBySuffix(phrase.phones[phIndex].suffix),
                    durations[phIndex + 1])));
            defaultSpkByFrame.AddRange(Enumerable.Repeat(tailDefaultSpk, tailFrames));
            //get speaker curves
            NDArray spkCurves = np.zeros<float>(totalFrames, dsConfig.speakers.Count);
            foreach(var curve in phrase.curves) {
                if(IsVoiceColorCurve(curve.Item1,out int subBankId) && subBankId < singer.Subbanks.Count) {
                    var spkId = getSpeakerIndexBySuffix(singer.Subbanks[subBankId].Suffix);
                    spkCurves[":", spkId] += DiffSingerUtils.SampleCurve(phrase, curve.Item2, 0, 
                        frameMs, totalFrames, headFrames, tailFrames, x => x * 0.01f)
                        .Select(f => (float)f).ToArray();
                }
            }

            // Linear embed mix: dest = base(CLR) + Σ amount_i * (spk_i − base).
            // Supports voice-color amounts above 100% (amount > 1) without normalizing them away.
            var result = new float[totalFrames * hiddenSize];
            for (int frameId = 0; frameId < totalFrames; frameId++) {
                int baseSpkId = defaultSpkByFrame[frameId];
                var baseEmbed = speakerEmbeds[":", baseSpkId].ToArray<float>();
                var dest = result.AsSpan(frameId * hiddenSize, hiddenSize);
                baseEmbed.CopyTo(dest);
                for (int spk = 0; spk < dsConfig.speakers.Count; spk++) {
                    float amount = (float)spkCurves[frameId, spk];
                    if (Math.Abs(amount) < 1e-8f) {
                        continue;
                    }
                    var target = speakerEmbeds[":", spk].ToArray<float>();
                    for (int j = 0; j < dest.Length; j++) {
                        dest[j] += amount * (target[j] - baseEmbed[j]);
                    }
                }
            }
            return new DenseTensor<float>(result, new int[] { totalFrames, hiddenSize })
                .Reshape(new int[] { 1, totalFrames, hiddenSize });
        }
    }
}
