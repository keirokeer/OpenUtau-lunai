using System.Collections.Generic;
using System.Linq;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.App {
    public class SingerFlyoutOrderTest {
        class TestSinger : USinger {
            private readonly string id;
            private readonly string name;
            private readonly USingerType type;
            public TestSinger(string id, USingerType type, string? name = null) {
                this.id = id;
                this.name = name ?? id;
                this.type = type;
                found = true;
            }
            public override string Id => id;
            public override string Name => name;
            public override USingerType SingerType => type;
        }

        static List<string> Order(IEnumerable<string> recents) {
            var singers = new USinger[] {
                new TestSinger("c-classic", USingerType.Classic),
                new TestSinger("a-classic", USingerType.Classic),
                new TestSinger("b-classic", USingerType.Classic),
                new TestSinger("z-enunu", USingerType.Enunu),
                new TestSinger("y-diffsinger", USingerType.DiffSinger),
            };
            var byId = singers.ToDictionary(s => s.Id);
            var groups = singers
                .GroupBy(s => s.SingerType)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name).ToList());
            return SingerFlyoutViewModel.OrderSingersFlat(byId, groups, recents, System.Array.Empty<string>())
                .Select(s => s.Id)
                .ToList();
        }

        [Fact]
        public void RecentThenAlphabeticalRest() {
            var order = Order(new[] { "b-classic", "missing", "y-diffsinger" });
            Assert.Equal(new[] {
                "b-classic",
                "y-diffsinger",
                "a-classic",
                "c-classic",
                "z-enunu",
            }, order);
        }

        [Fact]
        public void EmptyRecentIsAlphabetical() {
            var order = Order(System.Array.Empty<string>());
            Assert.Equal(new[] {
                "a-classic",
                "b-classic",
                "c-classic",
                "y-diffsinger",
                "z-enunu",
            }, order);
        }

        [Fact]
        public void DiffSingerVersionsAreNotMerged() {
            var singers = new USinger[] {
                new TestSinger("llane-170", USingerType.DiffSinger, "Llane Crow v170"),
                new TestSinger("llane-268", USingerType.DiffSinger, "Llane Crow v268"),
                new TestSinger("other", USingerType.Classic, "Other"),
            };
            var (recent, others) = SingerFlyoutViewModel.OrderSingers(
                singers, new[] { "llane-170" });
            Assert.Equal(new[] { "llane-170" }, recent.Select(s => s.Id));
            Assert.Equal(2, others.Count);
            Assert.Contains(others, s => s.Id == "llane-268");
            Assert.Contains(others, s => s.Id == "other");
        }

        [Fact]
        public void RecentSectionIsCapped() {
            var singers = Enumerable.Range(0, 12)
                .Select(i => (USinger)new TestSinger($"s{i}", USingerType.Classic, $"Singer {i:00}"))
                .ToArray();
            var recentIds = singers.Select(s => s.Id).Reverse();
            var (recent, others) = SingerFlyoutViewModel.OrderSingers(singers, recentIds);
            Assert.Equal(SingerFlyoutViewModel.MaxRecentDisplayed, recent.Count);
            Assert.Equal(12 - SingerFlyoutViewModel.MaxRecentDisplayed, others.Count);
        }
    }
}
