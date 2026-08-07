using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;
using UstxFmt = OpenUtau.Core.Format.Ustx;

namespace OpenUtau.Core {
    public class ExpressionDefaultResolverTest {
        [Fact]
        public void EffectiveFallsBackToProjectThenOverride() {
            var (project, track) = CreateProject();
            project.expressions[UstxFmt.TENC].CustomDefaultValue = 20;

            Assert.Equal(20, ExpressionDefaultResolver.GetEffectiveDefault(project, track, UstxFmt.TENC));

            ExpressionDefaultResolver.ApplyTrackOverride(project, track, UstxFmt.TENC, 70);
            Assert.Equal(70, ExpressionDefaultResolver.GetEffectiveDefault(project, track, UstxFmt.TENC));
            Assert.True(ExpressionDefaultResolver.HasTrackOverride(track, UstxFmt.TENC));

            ExpressionDefaultResolver.ApplyTrackOverride(project, track, UstxFmt.TENC, null);
            Assert.Equal(20, ExpressionDefaultResolver.GetEffectiveDefault(project, track, UstxFmt.TENC));
            Assert.False(ExpressionDefaultResolver.HasTrackOverride(track, UstxFmt.TENC));
        }

        [Fact]
        public void TrackValueEqualToProjectRemovesOverride() {
            var (project, track) = CreateProject();
            project.expressions[UstxFmt.TENC].CustomDefaultValue = 15;
            ExpressionDefaultResolver.ApplyTrackOverride(project, track, UstxFmt.TENC, 40);
            Assert.True(ExpressionDefaultResolver.HasTrackOverride(track, UstxFmt.TENC));

            ExpressionDefaultResolver.ApplyTrackOverride(project, track, UstxFmt.TENC, 15);
            Assert.False(ExpressionDefaultResolver.HasTrackOverride(track, UstxFmt.TENC));
            Assert.Empty(track.ExpressionDefaultOverrides);
        }

        [Fact]
        public void PhonemeGetExpressionUsesTrackEffectiveDefault() {
            var (project, track, _, _, phoneme) = CreatePhonemeFixture();
            project.expressions[UstxFmt.VEL].CustomDefaultValue = 100;
            ExpressionDefaultResolver.ApplyTrackOverride(project, track, UstxFmt.VEL, 40);

            var result = phoneme.GetExpression(project, track, UstxFmt.VEL);
            Assert.False(result.Item2);
            Assert.Equal(40, result.Item1);
        }

        [Fact]
        public void CurveSampleEmptyUsesExplicitEmptyValue() {
            var descriptor = new UExpressionDescriptor("tension", UstxFmt.TENC, -100, 100, 0) {
                type = UExpressionType.Curve,
                CustomDefaultValue = 10,
            };
            var curve = new UCurve(descriptor);
            Assert.Equal(10, curve.Sample(100));
            Assert.Equal(55, curve.Sample(100, 55));

            curve.Set(0, 80, 0, 80);
            curve.Set(200, 80, 200, 80);
            curve.Erase(50, 150);
            Assert.Equal(55, curve.Sample(100, 55));
            Assert.Equal(10, curve.Sample(100));
        }

        [Fact]
        public void ProjectClrSyncSkipsTracksWithClrOverride() {
            var project = new UProject();
            UstxFmt.AddDefaultExpressions(project);
            var trackA = new UTrack(project) { TrackNo = 0 };
            var trackB = new UTrack(project) { TrackNo = 1 };
            project.tracks.Add(trackA);
            project.tracks.Add(trackB);
            trackA.VoiceColorExp = new UExpressionDescriptor("voice color", UstxFmt.CLR, 0, 2, 0) {
                options = new[] { "A", "B", "C" },
            };
            trackB.VoiceColorExp = new UExpressionDescriptor("voice color", UstxFmt.CLR, 0, 2, 0) {
                options = new[] { "A", "B", "C" },
            };
            project.expressions[UstxFmt.CLR].min = 0;
            project.expressions[UstxFmt.CLR].max = 2;
            project.expressions[UstxFmt.CLR].CustomDefaultValue = 0;

            ExpressionDefaultResolver.ApplyTrackOverride(project, trackB, UstxFmt.CLR, 2);
            trackB.VoiceColorExp.CustomDefaultValue = 2;

            var cmd = new SetExpressionCustomDefaultCommand(project, UstxFmt.CLR, 1, 0);
            cmd.Execute();

            Assert.Equal(1, project.expressions[UstxFmt.CLR].CustomDefaultValue);
            Assert.Equal(1, trackA.VoiceColorExp.CustomDefaultValue);
            Assert.Equal(2, trackB.VoiceColorExp.CustomDefaultValue);
            Assert.True(ExpressionDefaultResolver.HasTrackOverride(trackB, UstxFmt.CLR));
        }

        [Fact]
        public void PruneMatchingOverridesAfterProjectChange() {
            var (project, track) = CreateProject();
            project.expressions[UstxFmt.TENC].CustomDefaultValue = 10;
            ExpressionDefaultResolver.EnsureOverridesDict(track);
            track.ExpressionDefaultOverrides[UstxFmt.TENC] = 10;
            Assert.True(ExpressionDefaultResolver.HasTrackOverride(track, UstxFmt.TENC));

            ExpressionDefaultResolver.PruneMatchingOverrides(project, new[] { UstxFmt.TENC });
            Assert.False(ExpressionDefaultResolver.HasTrackOverride(track, UstxFmt.TENC));
        }

        [Fact]
        public void CloneOverridesCopiesDictionary() {
            var (project, track) = CreateProject();
            ExpressionDefaultResolver.ApplyTrackOverride(project, track, UstxFmt.TENC, 33);
            var clone = ExpressionDefaultResolver.CloneOverrides(track);
            Assert.Equal(33, clone[UstxFmt.TENC]);
            clone[UstxFmt.TENC] = 1;
            Assert.Equal(33, track.ExpressionDefaultOverrides[UstxFmt.TENC]);
        }

        static (UProject project, UTrack track) CreateProject() {
            var project = new UProject();
            UstxFmt.AddDefaultExpressions(project);
            var track = new UTrack(project) { TrackNo = 0 };
            project.tracks.Add(track);
            return (project, track);
        }

        static (UProject project, UTrack track, UVoicePart part, UNote note, UPhoneme phoneme)
            CreatePhonemeFixture() {
            var (project, track) = CreateProject();
            var part = new UVoicePart { trackNo = 0, position = 0, Duration = 480 };
            var note = project.CreateNote(60, 0, 480);
            note.phonemeIndexes = new[] { 0 };
            var phoneme = new UPhoneme {
                Parent = note,
                index = 0,
                position = 0,
            };
            note.phonemeExpressions = new List<UExpression>();
            note.phonemizerExpressions = new List<UExpression>();
            part.notes.Add(note);
            part.phonemes.Add(phoneme);
            project.parts.Add(part);
            return (project, track, part, note, phoneme);
        }
    }
}
