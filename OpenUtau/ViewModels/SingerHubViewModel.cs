using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.SingerHub;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {

    public sealed partial class VersionStatusSegment {
        public string Text { get; init; } = string.Empty;
        public bool Bold { get; init; }
        public FontWeight SegmentFontWeight => Bold ? FontWeight.Bold : FontWeight.Normal;
    }

    public partial class SingerHubRowViewModel : ViewModelBase {
        public const string SingersBaseUrl = "https://lunaiproject.github.io/singers/";
        public const string UfrVoicebankBaseUrl = "https://utaufrance.com/voicebank/";

        public SingerHubEntry? Registry { get; }
        public string Name { get; }
        public string Version { get; }
        /// <summary>"lunai", "ufr", or "" for installed-only.</summary>
        public string Host { get; }
        public bool IsLunai { get; }
        public string IconUrl { get; }
        /// <summary>LUNAI: falls back to icon.png when iconou.png is unavailable.</summary>
        public string IconFallbackUrl { get; }
        public string PageUrl { get; }
        public string Company { get; }
        /// <summary>Terms of use URL. Empty if none. LUNAI default: https://lunaiproject.github.io/termsofuse</summary>
        public string TosUrl { get; }
        /// <summary>Display type from registry: "DiffSinger" or "UTAU". Default DiffSinger for LUNAI.</summary>
        public string TypeDisplay { get; }
        [Reactive] public partial Bitmap? IconBitmap { get; set; }
        [Reactive] public partial bool IsInstalled { get; set; }
        [Reactive] public partial string InstalledVersion { get; set; } = string.Empty;
        [Reactive] public partial string FolderPath { get; set; } = string.Empty;

        public bool HasPageLink => !string.IsNullOrEmpty(PageUrl);
        public bool HasCompany => !string.IsNullOrEmpty(Company);
        public bool HasRegistry => Registry != null;
        /// <summary>Install when not installed or when installed version is older than latest, and download is available.</summary>
        public bool CanInstall => HasRegistry && HasDownloadUrl && (!IsInstalled || SingerHubClient.CompareVersions(InstalledVersion, Version) < 0);
        bool HasDownloadUrl {
            get {
                if (Registry == null) return false;
                var url = (Registry.DownloadUrl ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(url) || url.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
                // Only allow HTTPS downloads for safety.
                return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            }
        }
        public bool IsNewVersionAvailable => IsInstalled && HasRegistry && !string.IsNullOrEmpty(InstalledVersion) && !string.IsNullOrEmpty(Version) && SingerHubClient.CompareVersions(InstalledVersion, Version) < 0;

        static string Ver(string? v) => string.IsNullOrEmpty(v) ? ThemeManager.GetString("lunai.unknownversion") : "v" + v;

        public IEnumerable<VersionStatusSegment> VersionStatusSegments {
            get {
                var list = new List<VersionStatusSegment>();
                if (HasRegistry) {
                    var latestVer = Ver(Version);
                    if (!IsInstalled) {
                        list.Add(new VersionStatusSegment { Text = ThemeManager.GetString("lunai.notinstalled"), Bold = false });
                    } else if (IsNewVersionAvailable) {
                        var instVer = Ver(InstalledVersion);
                        list.Add(new VersionStatusSegment { Text = ThemeManager.GetString("lunai.outdated") + " (", Bold = false });
                        list.Add(new VersionStatusSegment { Text = instVer, Bold = true });
                        list.Add(new VersionStatusSegment { Text = "), " + ThemeManager.GetString("lunai.newversionavailable") + " (", Bold = false });
                        list.Add(new VersionStatusSegment { Text = latestVer, Bold = true });
                        list.Add(new VersionStatusSegment { Text = ")", Bold = false });
                    } else {
                        list.Add(new VersionStatusSegment { Text = ThemeManager.GetString("lunai.uptodate") + " (", Bold = false });
                        list.Add(new VersionStatusSegment { Text = latestVer, Bold = true });
                        list.Add(new VersionStatusSegment { Text = ")", Bold = false });
                    }
                } else if (IsInstalled) {
                    list.Add(new VersionStatusSegment { Text = ThemeManager.GetString("lunai.installed") + ": ", Bold = false });
                    list.Add(new VersionStatusSegment { Text = Ver(InstalledVersion), Bold = true });
                }
                return list;
            }
        }

        public SingerHubRowViewModel(SingerHubEntry entry) {
            Registry = entry;
            Name = entry.Name?.Trim() ?? string.Empty;
            Version = entry.Version?.Trim() ?? string.Empty;
            Host = (entry.Host ?? "lunai").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(Host)) Host = "lunai";
            IsLunai = Host == "lunai";
            var code = (entry.Code ?? string.Empty).Trim();
            var codeLower = code.ToLowerInvariant();
            var icon = (entry.Icon ?? string.Empty).Trim();

            if (Host == "ufr") {
                PageUrl = string.IsNullOrEmpty(code) ? string.Empty : (UfrVoicebankBaseUrl + codeLower + "/");
                IconUrl = icon;
                IconFallbackUrl = string.Empty;
            } else if (Host == "brapa") {
                // BRAPA: icon из JSON, страница — tos или сайт команды.
                IconUrl = icon;
                IconFallbackUrl = string.Empty;
                var page = (entry.Tos ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(page) || page.Equals("none", StringComparison.OrdinalIgnoreCase)) {
                    PageUrl = "https://www.teambrapa.com.br/";
                } else {
                    PageUrl = page;
                }
            } else {
                if (!string.IsNullOrEmpty(codeLower)) {
                    var singerBase = SingersBaseUrl + codeLower + "/";
                    IconUrl = singerBase + "iconou.png";
                    IconFallbackUrl = singerBase + "icon.png";
                    PageUrl = singerBase;
                } else {
                    IconUrl = string.Empty;
                    IconFallbackUrl = string.Empty;
                    PageUrl = string.Empty;
                }
            }

            Company = string.IsNullOrWhiteSpace(entry.Company)
                ? (Host == "ufr"
                    ? "Utau French Resources"
                    : Host == "brapa"
                        ? "Team Brapa"
                        : "LUNAI Project")
                : entry.Company.Trim();
            var tos = (entry.Tos ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(tos) || tos.Equals("none", StringComparison.OrdinalIgnoreCase)) {
                TosUrl = Host == "lunai" ? "https://lunaiproject.github.io/termsofuse" : string.Empty;
            } else {
                TosUrl = tos;
            }
            var rawType = (entry.Type ?? string.Empty).Trim();
            TypeDisplay = rawType.Equals("utau", StringComparison.OrdinalIgnoreCase) ? "UTAU" : "DiffSinger";
            this.WhenAnyValue(x => x.IsInstalled, x => x.InstalledVersion, x => x.Version)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(CanInstall));
                    this.RaisePropertyChanged(nameof(IsNewVersionAvailable));
                    this.RaisePropertyChanged(nameof(VersionStatusSegments));
                });
        }

        public SingerHubRowViewModel(string name, string version, string folderPath, bool isLunai) {
            Registry = null;
            Name = name;
            Version = version;
            Host = string.Empty;
            IsLunai = isLunai;
            IconUrl = string.Empty;
            IconFallbackUrl = string.Empty;
            PageUrl = string.Empty;
            Company = string.Empty;
            TosUrl = isLunai ? "https://lunaiproject.github.io/termsofuse" : string.Empty;
            TypeDisplay = isLunai ? "DiffSinger" : "UTAU";
            FolderPath = folderPath;
            IsInstalled = true;
            InstalledVersion = version;
        }

        public void SetInstalled(string installedVersion, string folderPath) {
            IsInstalled = true;
            InstalledVersion = installedVersion;
            FolderPath = folderPath;
            this.RaisePropertyChanged(nameof(CanInstall));
            this.RaisePropertyChanged(nameof(IsNewVersionAvailable));
            this.RaisePropertyChanged(nameof(VersionStatusSegments));
        }
    }

    /// <summary>Section header and its singer rows (e.g. "Updates available", "Installed", "Not installed").</summary>
    public sealed partial class SingerHubSectionViewModel {
        public string Header { get; init; } = string.Empty;
        public List<SingerHubRowViewModel> Rows { get; init; } = new List<SingerHubRowViewModel>();
    }

    public partial class SingerHubViewModel : ViewModelBase {
        readonly SingerHubClient client = new SingerHubClient();
        readonly ConcurrentDictionary<string, Bitmap> iconCache = new ConcurrentDictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<SingerHubRowViewModel> Rows { get; } = new ObservableCollection<SingerHubRowViewModel>();
        public ObservableCollection<SingerHubRowViewModel> FilteredRows { get; } = new ObservableCollection<SingerHubRowViewModel>();
        /// <summary>Grouped list for UI: Updates available, Installed, Not installed.</summary>
        public ObservableCollection<SingerHubSectionViewModel> Sections { get; } = new ObservableCollection<SingerHubSectionViewModel>();
        [Reactive] public partial string Status { get; set; } = string.Empty;
        [Reactive] public partial string SearchText { get; set; } = string.Empty;
        [Reactive] public partial int SelectedTabIndex { get; set; }
        public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }
        public ReactiveCommand<SingerHubRowViewModel, RxVoid> InstallOrUpdateCommand { get; }
        public ReactiveCommand<SingerHubRowViewModel, RxVoid> UninstallCommand { get; }

        public SingerHubViewModel() {
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
            InstallOrUpdateCommand = ReactiveCommand.CreateFromTask<SingerHubRowViewModel>(InstallOrUpdateAsync);
            UninstallCommand = ReactiveCommand.CreateFromTask<SingerHubRowViewModel>(UninstallAsync);
            this.WhenAnyValue(x => x.SearchText, x => x.SelectedTabIndex).Subscribe(_ => ApplyFilter());
            _ = RefreshAsync();
        }

        void ApplyFilter() {
            var q = Rows.AsEnumerable();
            // ALL: только исполнители из реестров (любой host).
            if (SelectedTabIndex == 0) q = q.Where(r => r.HasRegistry);
            // LUNAI: все с host == "lunai".
            else if (SelectedTabIndex == 1) q = q.Where(r => r.Host == "lunai");
            // UFR: все с host == "ufr".
            else if (SelectedTabIndex == 2) q = q.Where(r => r.Host == "ufr");
            // BRAPA: все с host == "brapa".
            else if (SelectedTabIndex == 3) q = q.Where(r => r.Host == "brapa");
            if (!string.IsNullOrWhiteSpace(SearchText))
                q = q.Where(r => (r.Name ?? string.Empty).Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
            var list = q.ToList();

            FilteredRows.Clear();
            foreach (var r in list) FilteredRows.Add(r);

            var updates = list.Where(r => r.IsNewVersionAvailable).ToList();
            var installed = list.Where(r => r.IsInstalled && !r.IsNewVersionAvailable).ToList();
            var notInstalled = list.Where(r => !r.IsInstalled).ToList();

            Sections.Clear();
            if (updates.Count > 0)
                Sections.Add(new SingerHubSectionViewModel { Header = ThemeManager.GetString("lunai.section.updatesavailable"), Rows = updates });
            if (installed.Count > 0)
                Sections.Add(new SingerHubSectionViewModel { Header = ThemeManager.GetString("lunai.section.installed"), Rows = installed });
            if (notInstalled.Count > 0)
                Sections.Add(new SingerHubSectionViewModel { Header = ThemeManager.GetString("lunai.section.notinstalled"), Rows = notInstalled });
        }

        static string NormalizeName(string s) => (s ?? string.Empty).Trim();

        async Task LoadIconsAsync(IEnumerable<SingerHubRowViewModel> rows) {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "OpenUtau-LUNAI");
            foreach (var row in rows.Where(r => !string.IsNullOrEmpty(r.IconUrl))) {
                try {
                    Bitmap? cachedBitmap = null;
                    foreach (var url in IconUrlsFor(row)) {
                        if (iconCache.TryGetValue(url, out var cached)) {
                            cachedBitmap = cached;
                            break;
                        }
                    }
                    if (cachedBitmap != null) {
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            row.IconBitmap = cachedBitmap;
                        });
                        continue;
                    }

                    byte[]? bytes = null;
                    string? loadedUrl = null;
                    foreach (var url in IconUrlsFor(row)) {
                        try {
                            using var response = await http.GetAsync(url);
                            if (!response.IsSuccessStatusCode) {
                                continue;
                            }
                            bytes = await response.Content.ReadAsByteArrayAsync();
                            loadedUrl = url;
                            break;
                        } catch {
                            /* try next URL */
                        }
                    }
                    if (bytes == null || loadedUrl == null) {
                        continue;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => {
                        try {
                            using var ms = new MemoryStream(bytes);
                            var bmp = new Bitmap(ms);
                            row.IconBitmap = bmp;
                            iconCache[loadedUrl] = bmp;
                        } catch { /* ignore decode errors */ }
                    });
                } catch { /* ignore load errors */ }
            }
        }

        static IEnumerable<string> IconUrlsFor(SingerHubRowViewModel row) {
            if (!string.IsNullOrEmpty(row.IconUrl)) {
                yield return row.IconUrl;
            }
            if (!string.IsNullOrEmpty(row.IconFallbackUrl)
                && !string.Equals(row.IconFallbackUrl, row.IconUrl, StringComparison.OrdinalIgnoreCase)) {
                yield return row.IconFallbackUrl;
            }
        }

        async Task<List<SingerHubEntry>> FetchRegistrySafeAsync(string? registryUrl = null) {
            try {
                return await client.FetchRegistryAsync(registryUrl);
            } catch (Exception e) {
                Serilog.Log.Warning(e, "Failed to fetch Singer Hub registry from {Url}", registryUrl ?? SingerHubClient.DefaultRegistryUrl);
                return new List<SingerHubEntry>();
            }
        }

        public async Task RefreshAsync() {
            try {
                Status = ThemeManager.GetString("lunai.status.fetching");
                var lunaiList = await FetchRegistrySafeAsync();

                // UFR registry: remote JSON only; on error just skip.
                var ufrList = await FetchRegistrySafeAsync("https://utaufrance.com/ufr-pack/singers.json");

                // BRAPA registry: remote JSON only (errors are ignored).
                var brapaList = await FetchRegistrySafeAsync("https://www.teambrapa.com.br/singers.json");

                var registry = lunaiList.Concat(ufrList).Concat(brapaList).ToList();
                Status = ThemeManager.GetString("lunai.status.listing");
                var installed = await client.GetInstalledAsync(registry);

                // Group by name; if same singer in multiple folders, keep the one with highest version
                var installedByName = installed
                    .GroupBy(i => NormalizeName(i.DisplayName), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Version, Comparer<string>.Create((a, b) => SingerHubClient.CompareVersions(a, b))).First(), StringComparer.OrdinalIgnoreCase);
                var rows = new System.Collections.Generic.List<SingerHubRowViewModel>();

                foreach (var entry in registry) {
                    var row = new SingerHubRowViewModel(entry);
                    var key = NormalizeName(entry.Name);
                    if (installedByName.TryGetValue(key, out var info)) {
                        row.SetInstalled(info.Version, info.FolderPath);
                        installedByName.Remove(key);
                    }
                    rows.Add(row);
                }

                foreach (var info in installedByName.Values) {
                    var row = new SingerHubRowViewModel(info.DisplayName, info.Version, info.FolderPath, info.IsLunai);
                    rows.Add(row);
                }

                var ordered = rows
                    .OrderBy(r =>
                    {
                        if (r.IsInstalled && r.IsNewVersionAvailable) return 0; // installed, update available
                        if (r.IsInstalled) return 1;                            // installed, up to date or no registry
                        return 2;                                              // not installed
                    })
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Rows.Clear();
                foreach (var r in ordered) Rows.Add(r);
                ApplyFilter();
                _ = LoadIconsAsync(ordered);
                var hadRegistry = registry.Count > 0;
                Status = hadRegistry
                    ? ThemeManager.GetString("lunai.status.ready")
                    : ThemeManager.GetString("lunai.status.offline");
            } catch (Exception e) {
                Status = ThemeManager.GetString("lunai.status.error");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async Task InstallOrUpdateAsync(SingerHubRowViewModel row) {
            if (row.Registry == null) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException("No registry entry.")));
                return;
            }
            if (!row.CanInstall) return;
            try {
                var baseStatus = string.Format(ThemeManager.GetString("lunai.status.installing"), row.Name);
                Status = baseStatus;
                var progress = new Progress<int>(p => Status = $"{baseStatus} ({p}%)");
                var existingPath = row.IsInstalled && !string.IsNullOrEmpty(row.FolderPath) ? row.FolderPath : null;
                await client.InstallOrUpdateAsync(row.Registry.DownloadUrl, existingPath, row.Host, progress);
                await Dispatcher.UIThread.InvokeAsync(() => SingerManager.Inst.SearchAllSingers());
                await RefreshAsync();
                Status = ThemeManager.GetString("lunai.status.installfinished");
            } catch (Exception e) {
                Status = ThemeManager.GetString("lunai.status.installfailed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async Task UninstallAsync(SingerHubRowViewModel row) {
            if (!row.IsInstalled) return;
            try {
                Status = string.Format(ThemeManager.GetString("lunai.status.uninstalling"), row.Name);
                await client.UninstallAsync(row.FolderPath);
                await Dispatcher.UIThread.InvokeAsync(() => SingerManager.Inst.SearchAllSingers());
                await RefreshAsync();
                Status = ThemeManager.GetString("lunai.status.uninstallfinished");
            } catch (Exception e) {
                Status = ThemeManager.GetString("lunai.status.uninstallfailed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
    }
}

