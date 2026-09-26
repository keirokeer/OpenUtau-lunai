using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.App.Views;
using OpenUtau.App;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public sealed class SingerFlyoutSeparatorViewModel { }

    public partial class SingerTypeFilterViewModel : ViewModelBase {
        public USingerType? Type { get; }
        public string Label { get; }
        [Reactive] public partial bool IsSelected { get; set; }

        public SingerTypeFilterViewModel(USingerType? type, string label, bool isSelected) {
            Type = type;
            Label = label;
            IsSelected = isSelected;
        }
    }

    public partial class SingerTileViewModel : ViewModelBase {
        public USinger Singer { get; }
        public string Name => Singer.LocalizedName;
        public string? Location => Singer.Location;
        public IReadOnlyList<string> SearchTerms { get; }
        public string Tip { get; }
        public bool IsCurrent { get; }
        public bool IsMissing => !Singer.Found;
        public bool IsFavourite {
            get => Singer.IsFavourite;
            set {
                if (Singer.IsFavourite != value) {
                    Singer.IsFavourite = value;
                    this.RaisePropertyChanged();
                }
            }
        }
        [Reactive] public partial Bitmap? Avatar { get; set; }

        public SingerTileViewModel(USinger singer, bool isCurrent) {
            Singer = singer;
            IsCurrent = isCurrent;
            SearchTerms = BuildSearchTerms(singer);
            var apply = ThemeManager.GetString("tracks.singer.applytoalltracks.tooltip");
            var mod = OS.IsMacOS() ? "⌘" : "Ctrl";
            var hint = $"{mod}+Click: {apply}";
            // Leave out what the tile and the path already show.
            var extra = SearchTerms
                .Where(term => term != Name &&
                    (Location == null || !Location.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var locationBlock = string.IsNullOrEmpty(Location)
                ? string.Empty
                : extra.Count == 0
                    ? Location
                    : $"{Location}\n{ThemeManager.GetString("tracks.searchterms")}: {string.Join(", ", extra)}";
            Tip = string.IsNullOrEmpty(locationBlock) ? hint : $"{locationBlock}\n{hint}";
            Avatar = SingerAvatarCache.Get(singer, bitmap => Avatar = bitmap);
        }

        /// <summary>
        /// The displayed and original names, all localized names, the id and folder name, which are often
        /// romanized, then the extra terms in the voicebank config.
        /// </summary>
        public static IReadOnlyList<string> BuildSearchTerms(USinger singer) {
            var terms = new List<string> { singer.LocalizedName, singer.Name };
            if (singer.LocalizedNames != null) {
                terms.AddRange(singer.LocalizedNames.Values);
            }
            terms.Add(singer.Id);
            if (!string.IsNullOrEmpty(singer.Location)) {
                terms.Add(Path.GetFileName(singer.Location));
            }
            terms.AddRange(singer.SearchTerms ?? Array.Empty<string>());
            return terms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Distinct()
                .ToArray();
        }

        const CompareOptions SearchOptions =
            CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

        /// <summary>
        /// Whether any search term contains the query, ignoring case, width and hiragana/katakana.
        /// An empty query matches everything.
        /// </summary>
        public bool Matches(string query) => Matches(SearchTerms, query);

        public static bool Matches(IEnumerable<string> terms, string query) {
            return string.IsNullOrEmpty(query) || terms.Any(term =>
                !string.IsNullOrEmpty(term) &&
                CultureInfo.InvariantCulture.CompareInfo.IndexOf(term, query, SearchOptions) >= 0);
        }
    }

    public partial class SingerFlyoutViewModel : ViewModelBase, ICmdSubscriber {
        /// <summary>How many recent singers to show above the separator.</summary>
        public const int MaxRecentDisplayed = 5;

        static readonly USingerType[] TypeFilterOrder = {
            USingerType.Classic,
            USingerType.DiffSinger,
            USingerType.Enunu,
            USingerType.Vogen,
            USingerType.Voicevox,
        };

        [Reactive] public partial IReadOnlyList<object> Items { get; set; } = Array.Empty<object>();
        [Reactive] public partial IReadOnlyList<SingerTypeFilterViewModel> TypeFilters { get; set; }
            = Array.Empty<SingerTypeFilterViewModel>();
        [Reactive] public partial bool IsEmpty { get; set; }
        [Reactive] public partial string SearchText { get; set; } = string.Empty;
        public bool HasAdditionalSingersFolder =>
            !string.IsNullOrWhiteSpace(PathManager.Inst.AdditionalSingersPath) &&
            Directory.Exists(PathManager.Inst.AdditionalSingersPath);

        /// <summary>Raised when the flyout should close, e.g. after a singer is selected.</summary>
        public event Action? CloseRequested;

        private readonly Func<USinger?> getCurrentSinger;
        private readonly ICommand selectSingerCommand;
        private readonly ICommand? allSetSingerCommand;
        private List<USinger> allSingers = new();
        private List<USinger> recentSingers = new();
        private List<USinger> restSingers = new();
        private USingerType? selectedType;

        public SingerFlyoutViewModel(
                Func<USinger?> getCurrentSinger,
                ICommand selectSingerCommand,
                ICommand? allSetSingerCommand = null) {
            this.getCurrentSinger = getCurrentSinger;
            this.selectSingerCommand = selectSingerCommand;
            this.allSetSingerCommand = allSetSingerCommand;
            this.WhenAnyValue(x => x.SearchText)
                .Subscribe(_ => PublishItems());
            Rebuild();
        }

        public void Rebuild() {
            var current = getCurrentSinger();
            allSingers = SingerManager.Inst.Singers.Values
                .Where(s => s != null)
                .ToList();
            if (current != null && !current.Found && !string.IsNullOrEmpty(current.Name) &&
                allSingers.All(s => !s.Equals(current))) {
                allSingers.Insert(0, current);
            }
            (recentSingers, restSingers) = OrderSingers(allSingers, Preferences.Default.RecentSingers);
            RebuildTypeFilters();
            PublishItems();
            this.RaisePropertyChanged(nameof(HasAdditionalSingersFolder));
        }

        void RebuildTypeFilters() {
            var present = allSingers.Select(s => s.SingerType).ToHashSet();
            if (selectedType != null && !present.Contains(selectedType.Value)) {
                selectedType = null;
            }
            var filters = new List<SingerTypeFilterViewModel> {
                new(null, ThemeManager.GetString("lunai.tab.all"), selectedType == null),
            };
            foreach (var type in TypeFilterOrder) {
                if (!present.Contains(type)) {
                    continue;
                }
                filters.Add(new SingerTypeFilterViewModel(type, DisplayName(type), selectedType == type));
            }
            TypeFilters = filters;
        }

        public static string DisplayName(USingerType type) => type switch {
            USingerType.Classic => "Classic",
            USingerType.DiffSinger => "DiffSinger",
            USingerType.Enunu => "ENUNU",
            USingerType.Vogen => "Vogen",
            USingerType.Voicevox => "VOICEVOX",
            _ => type.ToString(),
        };

        public void SelectTypeFilter(SingerTypeFilterViewModel filter) {
            selectedType = filter.Type;
            foreach (var f in TypeFilters) {
                f.IsSelected = ReferenceEquals(f, filter);
            }
            PublishItems();
        }

        void PublishItems() {
            var current = getCurrentSinger();
            var query = SearchText?.Trim() ?? string.Empty;
            bool nameFiltering = query.Length > 0;
            bool typeFiltering = selectedType != null;

            IEnumerable<USinger> Match(IEnumerable<USinger> source) {
                IEnumerable<USinger> result = source;
                if (typeFiltering) {
                    result = result.Where(s => s.SingerType == selectedType);
                }
                if (nameFiltering) {
                    result = result.Where(s => SingerTileViewModel.Matches(
                        SingerTileViewModel.BuildSearchTerms(s), query));
                }
                return result;
            }

            var list = new List<object>();
            if (nameFiltering || typeFiltering) {
                foreach (var singer in Match(allSingers).LocalizedOrderBy(s => s.LocalizedName)) {
                    list.Add(new SingerTileViewModel(singer, current != null && singer.Equals(current)));
                }
            } else {
                var recent = Match(recentSingers).ToList();
                var rest = Match(restSingers).ToList();
                foreach (var singer in recent) {
                    list.Add(new SingerTileViewModel(singer, current != null && singer.Equals(current)));
                }
                if (recent.Count > 0 && rest.Count > 0) {
                    list.Add(new SingerFlyoutSeparatorViewModel());
                }
                foreach (var singer in rest) {
                    list.Add(new SingerTileViewModel(singer, current != null && singer.Equals(current)));
                }
            }
            Items = list;
            IsEmpty = !Items.OfType<SingerTileViewModel>().Any();
        }

        /// <summary>
        /// Recent singers first (most recent first, capped), then the rest A–Z by localized name.
        /// </summary>
        public static (List<USinger> Recent, List<USinger> Others) OrderSingers(
                IEnumerable<USinger> singers,
                IEnumerable<string> recentIds) {
            var byId = singers
                .Where(s => s != null && !string.IsNullOrEmpty(s.Id))
                .GroupBy(s => s.Id)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var recent = new List<USinger>();
            var added = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in recentIds) {
                if (recent.Count >= MaxRecentDisplayed) {
                    break;
                }
                if (string.IsNullOrWhiteSpace(id) || !byId.TryGetValue(id, out var singer)) {
                    continue;
                }
                if (added.Add(singer.Id)) {
                    recent.Add(singer);
                }
            }
            var others = byId.Values
                .Where(s => !added.Contains(s.Id))
                .LocalizedOrderBy(s => s.LocalizedName)
                .ToList();
            return (recent, others);
        }

        // Flat order for unit tests.
        public static List<USinger> OrderSingersFlat(
                IReadOnlyDictionary<string, USinger> singers,
                IReadOnlyDictionary<USingerType, List<USinger>> groups,
                IEnumerable<string> recentIds,
                IEnumerable<string> favoriteIds) {
            _ = favoriteIds;
            _ = groups;
            var (recent, others) = OrderSingers(singers.Values, recentIds);
            return recent.Concat(others).ToList();
        }

        public void Select(SingerTileViewModel tile, bool applyToAll = false) {
            CloseRequested?.Invoke();
            if (applyToAll && allSetSingerCommand != null) {
                allSetSingerCommand.Execute(tile.Singer);
            } else {
                selectSingerCommand.Execute(tile.Singer);
            }
        }

        public bool IsRecent(SingerTileViewModel tile) {
            return Preferences.Default.RecentSingers.Contains(tile.Singer.Id);
        }

        public void RemoveFromRecent(SingerTileViewModel tile) {
            Preferences.Default.RecentSingers.Remove(tile.Singer.Id);
            Preferences.Save();
            Rebuild();
        }

        public void OpenLocation(SingerTileViewModel tile) {
            CloseRequested?.Invoke();
            SingersViewModel.OpenSingerLocation(tile.Singer);
        }

        public void EditSearchTerms(SingerTileViewModel tile) {
            CloseRequested?.Invoke();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
            if (mainWindow == null) {
                return;
            }
            var singer = tile.Singer;
            SingersDialog.ShowSearchTermsDialog(mainWindow, singer, text => SingersViewModel.SetSearchTerms(singer, text));
        }

        public async void InstallSinger() {
            CloseRequested?.Invoke();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow as MainWindow;
            if (mainWindow == null) {
                return;
            }
            var file = await FilePicker.OpenFileAboutSinger(
                mainWindow, "menu.tools.singer.install", FilePicker.ArchiveFiles);
            if (file == null) {
                return;
            }
            try {
                if (file.EndsWith(Core.Vogen.VogenSingerInstaller.FileExt)) {
                    Core.Vogen.VogenSingerInstaller.Install(file);
                    return;
                }
                if (file.EndsWith(PackageManager.OudepExt)) {
                    await PackageManager.Inst.InstallFromFileAsync(file);
                    return;
                }

                var setup = new SingerSetupDialog() {
                    DataContext = new SingerSetupViewModel() {
                        ArchiveFilePath = file,
                    },
                };
                _ = setup.ShowDialog(mainWindow);
                if (setup.Position.Y < 0) {
                    setup.Position = setup.Position.WithY(0);
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to install singer {file}");
                _ = await MessageBox.ShowError(mainWindow, new MessageCustomizableException($"Failed to install singer {file}", $"<translate:errors.failed.installsinger>: {file}", e));
            }
        }

        public void OpenSingersFolder() {
            OpenFolder(PathManager.Inst.SingersPath);
        }

        public void OpenAdditionalSingersFolder() {
            OpenFolder(PathManager.Inst.AdditionalSingersPath);
        }

        void OpenFolder(string path) {
            CloseRequested?.Invoke();
            try {
                OS.OpenFolder(path);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void RefreshSingers() {
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), true, "singer"));
            SingerManager.Inst.SearchAllSingers();
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), false, "singer"));
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SingersChangedNotification ||
                cmd is SingersRefreshedNotification { singer: null }) {
                Dispatcher.UIThread.Post(Rebuild);
            }
        }
    }
}
