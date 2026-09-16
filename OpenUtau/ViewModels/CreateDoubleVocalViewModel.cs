using OpenUtau.Core.Editing;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class CreateDoubleVocalViewModel : ViewModelBase {
        [Reactive] public partial double Count { get; set; } = 1;
        [Reactive] public partial bool RandomizePhonemeTimings { get; set; } = true;
        [Reactive] public partial double MaxPhonemeOffsetTick { get; set; } =
            DoubleVocalGenerator.DefaultMaxPhonemeOffsetTick;
        [Reactive] public partial bool WarpPitchCurve { get; set; } = true;
        [Reactive] public partial bool GeneratePitch { get; set; } = true;
        [Reactive] public partial double PitchExpressionPercent { get; set; } =
            DoubleVocalGenerator.DefaultPitchExpressionPercent;

        public DoubleVocalOptions ToOptions() {
            return new DoubleVocalOptions {
                Count = (int)Count,
                RandomizePhonemeTimings = RandomizePhonemeTimings,
                MaxPhonemeOffsetTick = (int)MaxPhonemeOffsetTick,
                WarpPitchCurve = WarpPitchCurve,
                GeneratePitch = GeneratePitch,
                PitchExpressionPercent = (int)PitchExpressionPercent,
            };
        }
    }
}
