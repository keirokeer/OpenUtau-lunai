using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Resolves the DiffSinger voicebank configured as the global pitch source (Ctrl+Shift+R).
    /// </summary>
    public static class DiffSingerPitchSource {
        public static IReadOnlyList<PitchSourceSingerOption> ListOptions() {
            var list = new List<PitchSourceSingerOption> {
                new PitchSourceSingerOption(string.Empty, string.Empty),
            };
            foreach (var singer in SingerManager.Inst.Singers.Values
                .OfType<DiffSingerSinger>()
                .Where(s => s.HasPitchPredictor)
                .OrderBy(s => s.LocalizedName, StringComparer.CurrentCultureIgnoreCase)) {
                list.Add(new PitchSourceSingerOption(singer.Id, singer.LocalizedName));
            }
            return list;
        }

        internal static DiffSingerSinger? TryResolveConfigured() {
            string id = Preferences.Default.DiffSingerPitchSourceSingerId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) {
                return null;
            }
            if (!SingerManager.Inst.Singers.TryGetValue(id, out var singer) || singer is not DiffSingerSinger ds) {
                Log.Warning("Configured DiffSinger pitch source singer \"{Id}\" was not found.", id);
                return null;
            }
            if (!ds.HasPitchPredictor) {
                Log.Warning("Configured DiffSinger pitch source singer \"{Id}\" has no pitch model.", id);
                return null;
            }
            try {
                if (!ds.Loaded) {
                    ds.EnsureLoaded();
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load DiffSinger pitch source singer \"{Id}\".", id);
                return null;
            }
            return ds;
        }
    }

    public readonly struct PitchSourceSingerOption {
        public string Id { get; }
        public string DisplayName { get; }

        public PitchSourceSingerOption(string id, string displayName) {
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName)
                ? "(None)"
                : displayName;
        }

        public override string ToString() => DisplayName;
    }
}
