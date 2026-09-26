using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
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

        [Theory]
        [InlineData("Kasane Teto", "teto", true)]
        [InlineData("Kasane Teto", "TETO", true)]
        [InlineData("Kasane Teto", "miku", false)]
        // Hiragana matches katakana, and half width matches full width.
        [InlineData("重音テト", "てと", true)]
        [InlineData("ＵＴＡＵ", "utau", true)]
        public void SearchMatches(string term, string query, bool expected) {
            Assert.Equal(expected, SingerTileViewModel.Matches(new[] { term }, query));
        }

        class NamedSinger : USinger {
            public NamedSinger() {
                found = true;
            }
            public override string Id => "Teto\\TetoCV";
            public override string Name => "重音テト";
            public override Dictionary<string, string> LocalizedNames { get; } = new() {
                ["en-US"] = "Kasane Teto",
            };
            public override string Location => Path.Combine("Singers", "Teto", "TetoCV");
            public override IList<string> SearchTerms => new[] { "kasane", " teto " };
        }

        [Fact]
        public void SearchTermsIncludeNamesFolderAndAuthorTerms() {
            var terms = SingerTileViewModel.BuildSearchTerms(new NamedSinger());
            Assert.Contains("重音テト", terms);
            Assert.Contains("Kasane Teto", terms);
            Assert.Contains("Teto\\TetoCV", terms);
            Assert.Contains("TetoCV", terms);
            Assert.Contains("kasane", terms);
            Assert.Contains("teto", terms);
            Assert.Equal(terms.Distinct(), terms);
            Assert.True(SingerTileViewModel.Matches(terms, "kasane te"));
        }

        [Fact]
        public void SplitSearchTermsOnAnyComma() {
            Assert.Equal(new[] { "kasane", "teto", "テト", "tet" },
                SingersViewModel.SplitSearchTerms(" kasane, teto，テト、 ;tet;teto "));
        }

        class FolderSinger : USinger {
            readonly string location;
            public FolderSinger(string location) {
                this.location = location;
                found = true;
            }
            public override string Location => location;
            public override IList<string> SearchTerms { get; } = new List<string>();
        }

        [Fact]
        public void SetSearchTermsWritesCharacterYaml() {
            var dir = Directory.CreateTempSubdirectory().FullName;
            try {
                var yamlFile = Path.Combine(dir, "character.yaml");
                File.WriteAllText(yamlFile, "name: Teto\n");
                var singer = new FolderSinger(dir);

                SingersViewModel.SetSearchTerms(singer, "kasane, teto");
                Assert.Equal(new[] { "kasane", "teto" }, singer.SearchTerms);
                using (var stream = File.OpenRead(yamlFile)) {
                    var config = VoicebankConfig.Load(stream);
                    Assert.Equal("Teto", config.Name);
                    Assert.Equal(new[] { "kasane", "teto" }, config.SearchTerms);
                }

                SingersViewModel.SetSearchTerms(singer, " ");
                Assert.Empty(singer.SearchTerms);
                Assert.DoesNotContain("search_terms", File.ReadAllText(yamlFile));
            } finally {
                Directory.Delete(dir, true);
            }
        }
    }
}
