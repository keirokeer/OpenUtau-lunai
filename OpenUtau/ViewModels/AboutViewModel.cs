using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.App.Models;
using OpenUtau.App.Services;
using OpenUtau.Core.Util;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class AboutViewModel : ViewModelBase {
        public const string LunaiRepository = "keirokeer/OpenUtau-lunai";

        public string AppVersion => $"v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}";
        public bool IsDarkMode => ThemeManager.IsDarkMode;
        [Reactive] public partial string SectionTitle { get; private set; } = string.Empty;
        [Reactive] public partial string StatusText { get; private set; } = string.Empty;
        public ObservableCollectionExtended<ContributorEntry> Contributors { get; } = new();

        public AboutViewModel() {
            ApplyDocument(ContributorsCatalog.LoadEmbedded());
            _ = LoadContributorsAsync();
        }

        async Task LoadContributorsAsync() {
            if (Contributors.Count == 0) {
                StatusText = ThemeManager.GetString("dialogs.about.contributors.loading");
            }
            var document = await ContributorsCatalog.LoadAsync();
            await Dispatcher.UIThread.InvokeAsync(() => {
                ApplyDocument(document);
                StatusText = document.FetchedLive
                    ? ThemeManager.GetString("dialogs.about.contributors.updated")
                    : Contributors.Count > 0
                        ? ThemeManager.GetString("dialogs.about.contributors.embedded")
                        : ThemeManager.GetString("dialogs.about.contributors.unavailable");
            });
        }

        void ApplyDocument(ContributorsDocument document) {
            Contributors.Clear();
            foreach (var contributor in document.Contributors) {
                Contributors.Add(contributor);
            }
            var repository = string.IsNullOrWhiteSpace(document.Repository)
                ? ContributorsCatalog.Repository
                : document.Repository;
            SectionTitle = string.Format(
                ThemeManager.GetString("dialogs.about.contributors.upstream"),
                repository);
        }

        public void OpenRepository() {
            OS.OpenWeb($"https://github.com/{ContributorsCatalog.Repository}");
        }

        public void OpenLunaiRepository() {
            OS.OpenWeb($"https://github.com/{LunaiRepository}");
        }
    }
}
