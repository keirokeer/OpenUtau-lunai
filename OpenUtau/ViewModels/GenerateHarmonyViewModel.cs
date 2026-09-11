using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public sealed partial class KeyOptionViewModel {
        public MusicalKey Key { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    public partial class GenerateHarmonyViewModel : ViewModelBase {
        readonly UVoicePart sourcePart;
        readonly UProject project;

        public IReadOnlyList<KeyOptionViewModel> KeyOptions { get; }
        [Reactive] public partial KeyOptionViewModel? SelectedKeyOption { get; set; }
        [Reactive] public partial bool ThirdAbove { get; set; }
        [Reactive] public partial bool ThirdBelow { get; set; }
        [Reactive] public partial bool FifthAbove { get; set; }
        [Reactive] public partial bool FifthBelow { get; set; }

        public string PartName => sourcePart.DisplayName;
        public string TrackName => project.tracks[sourcePart.trackNo].TrackName;

        public GenerateHarmonyViewModel(UVoicePart part) {
            sourcePart = part;
            project = DocManager.Inst.Project;
            ThirdAbove = true;
            var projectKey = MusicalKey.FromProject(project);
            var detected = KeySignatureHelper.DetectKeys(part.notes, 3);
            var detectedSet = detected.ToHashSet();
            var options = new List<KeyOptionViewModel>();
            foreach (var key in detected) {
                options.Add(new KeyOptionViewModel {
                    Key = key,
                    DisplayName = $"★ {KeySignatureHelper.FormatKey(key)}",
                });
            }
            foreach (var key in KeySignatureHelper.AllKeys()) {
                if (detectedSet.Contains(key)) {
                    continue;
                }
                options.Add(new KeyOptionViewModel {
                    Key = key,
                    DisplayName = KeySignatureHelper.FormatKey(key),
                });
            }
            KeyOptions = options;
            SelectedKeyOption = options.FirstOrDefault(option => option.Key == projectKey)
                ?? options.FirstOrDefault(option => detectedSet.Contains(option.Key))
                ?? options.FirstOrDefault();
        }

        public IReadOnlyList<HarmonyInterval> GetSelectedIntervals() {
            var list = new List<HarmonyInterval>();
            if (ThirdAbove) list.Add(HarmonyInterval.ThirdAbove);
            if (ThirdBelow) list.Add(HarmonyInterval.ThirdBelow);
            if (FifthAbove) list.Add(HarmonyInterval.FifthAbove);
            if (FifthBelow) list.Add(HarmonyInterval.FifthBelow);
            return list;
        }
    }
}
