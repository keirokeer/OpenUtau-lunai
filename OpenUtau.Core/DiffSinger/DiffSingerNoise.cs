using System;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Deterministic Gaussian noise for diffusion ONNX inputs (Box–Muller via SplitMix64).
    /// Same seed ⇒ bit-identical tensors; stage salts keep acoustic/pitch/variance independent.
    /// </summary>
    public static class DiffSingerNoise {
        public const ulong StageAcoustic = 0xA0;
        public const ulong StagePitch = 0xB0;
        public const ulong StageVariance = 0xC0;

        public static DenseTensor<float> Create(
            ulong stage,
            uint seed,
            int feats,
            int outDims,
            int nFrames) {
            if (feats <= 0 || outDims <= 0 || nFrames <= 0) {
                throw new ArgumentOutOfRangeException(nameof(nFrames), "noise dims must be positive.");
            }
            var data = new float[feats * outDims * nFrames];
            int idx = 0;
            for (int c = 0; c < feats; c++) {
                for (int m = 0; m < outDims; m++) {
                    for (int t = 0; t < nFrames; t++) {
                        data[idx++] = Gaussian(stage, seed, (ulong)c, (ulong)m, (ulong)t);
                    }
                }
            }
            return new DenseTensor<float>(data, new[] { 1, feats, outDims, nFrames });
        }

        public static float Gaussian(ulong stage, ulong seed, ulong c, ulong m, ulong t) {
            ulong k = Mix(Mix(Mix(Mix(stage, seed), c), m), t);
            double u1 = Uniform(SplitMix64(k));
            double u2 = Uniform(SplitMix64(k ^ 0xD1B54A32D192ED03UL));
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            return (float)(r * Math.Cos(2.0 * Math.PI * u2));
        }

        static ulong Mix(ulong h, ulong x) =>
            SplitMix64(h ^ (x + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2)));

        static ulong SplitMix64(ulong x) {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        static double Uniform(ulong h) =>
            ((h >> 11) + 0.5) * (1.0 / 9007199254740992.0);
    }
}
