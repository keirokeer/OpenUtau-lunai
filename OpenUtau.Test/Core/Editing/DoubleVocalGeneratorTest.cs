using System;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.Editing {
    public class DoubleVocalGeneratorTest {
        [Fact]
        public void MapTickWarpsInsideSegment() {
            var oldB = new[] { 0, 100, 200 };
            var newB = new[] { 0, 150, 200 };
            Assert.Equal(75, DoubleVocalGenerator.MapTick(50, oldB, newB));
            Assert.Equal(150, DoubleVocalGenerator.MapTick(100, oldB, newB));
            Assert.Equal(175, DoubleVocalGenerator.MapTick(150, oldB, newB));
        }

        [Fact]
        public void MapTickPreservesOutsideEndsAsOffset() {
            var oldB = new[] { 100, 200 };
            var newB = new[] { 110, 190 };
            Assert.Equal(90, DoubleVocalGenerator.MapTick(80, oldB, newB));
            Assert.Equal(200, DoubleVocalGenerator.MapTick(210, oldB, newB));
        }

        [Fact]
        public void WarpCurvePointsRemapsXsAndKeepsYs() {
            var oldB = new[] { 0, 100, 200 };
            var newB = new[] { 0, 150, 200 };
            var (xs, ys) = DoubleVocalGenerator.WarpCurvePoints(
                oldB, newB,
                new[] { 0, 50, 100, 200 },
                new[] { 10, 20, 30, 40 });
            Assert.Equal(new[] { 0, 75, 150, 200 }, xs);
            Assert.Equal(new[] { 10, 20, 30, 40 }, ys);
        }

        [Fact]
        public void AddPitchDeviationClampsSum() {
            Assert.Equal(90, DoubleVocalGenerator.AddPitchDeviation(40, 50, -1200, 1200));
            Assert.Equal(1200, DoubleVocalGenerator.AddPitchDeviation(1000, 500, -1200, 1200));
            Assert.Equal(-1200, DoubleVocalGenerator.AddPitchDeviation(-800, -500, -1200, 1200));
        }

        [Fact]
        public void MergeAdditivePitchDeviationAddsPointwiseOntoExisting() {
            // warped: 10 10 12 14, gen deviation: 0 1 -1 2 → 10 11 11 16
            var (xs, ys) = DoubleVocalGenerator.MergeAdditivePitchDeviation(
                new[] { 0, 5, 10, 15 },
                new[] { 10, 10, 12, 14 },
                new (int x, int y)[] { (0, 0), (5, 1), (10, -1), (15, 2) },
                emptyValue: 0,
                min: -1200,
                max: 1200);
            Assert.Equal(new[] { 0, 5, 10, 15 }, xs);
            Assert.Equal(new[] { 10, 11, 11, 16 }, ys);
        }

        [Fact]
        public void MergeAdditivePitchDeviationDropsStaleKnotsInsideGenRange() {
            // Sparse warped knot at x=50 would jump if left unadjusted next to dense gen samples.
            var (xs, ys) = DoubleVocalGenerator.MergeAdditivePitchDeviation(
                new[] { 0, 50, 100 },
                new[] { 10, 100, 10 },
                new (int x, int y)[] { (0, 0), (25, 1), (75, -1), (100, 2) },
                emptyValue: 0,
                min: -1200,
                max: 1200);
            Assert.Equal(new[] { 0, 25, 75, 100 }, xs);
            Assert.Equal(new[] { 10, 56, 54, 12 }, ys);
        }

        [Fact]
        public void MergeAdditivePitchDeviationDoesNotStackSameX() {
            var (xs, ys) = DoubleVocalGenerator.MergeAdditivePitchDeviation(
                new[] { 0 },
                new[] { 10 },
                new (int x, int y)[] { (0, 5), (0, 3) },
                emptyValue: 0,
                min: -1200,
                max: 1200);
            Assert.Equal(new[] { 0 }, xs);
            Assert.Equal(new[] { 13 }, ys);
        }

        [Fact]
        public void AllocateDoubleTrackNameUsesSuffixThenNumbers() {
            var project = new UProject();
            project.tracks.Add(new UTrack("Vox") { TrackNo = 0 });
            Assert.Equal("Vox (Double)", DoubleVocalGenerator.AllocateDoubleTrackName(project, "Vox"));

            project.tracks.Add(new UTrack("Vox (Double)") { TrackNo = 1 });
            Assert.Equal("Vox (Double 2)", DoubleVocalGenerator.AllocateDoubleTrackName(project, "Vox"));

            project.tracks.Add(new UTrack("Vox (Double 2)") { TrackNo = 2 });
            Assert.Equal("Vox (Double 3)", DoubleVocalGenerator.AllocateDoubleTrackName(project, "Vox"));
        }

        [Fact]
        public void CanCreateDoubleVocalRejectsWaveAndMissingSinger() {
            var project = new UProject();
            var track = new UTrack("t") { TrackNo = 0 };
            project.tracks.Add(track);
            var voice = new UVoicePart { trackNo = 0 };
            project.parts.Add(voice);
            Assert.False(DoubleVocalGenerator.CanCreateDoubleVocal(project, voice));
            Assert.False(DoubleVocalGenerator.CanCreateDoubleVocal(project, new UWavePart { trackNo = 0 }));
        }
    }
}
