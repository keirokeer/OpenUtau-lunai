using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace OpenUtau.App.ViewModels {
    public class UpdaterViewModel : ViewModelBase {
        public const string LunaiRepository = "keirokeer/OpenUtau-lunai";
        public const string LunaiReleasesUrl = "https://github.com/keirokeer/OpenUtau-lunai/releases";
        public const string LunaiRepoUrl = "https://github.com/keirokeer/OpenUtau-lunai";
        public const string LunaiDiscordInviteUrl = "https://discord.gg/GKSxrSd7mB";
        public const string LunaiSupportEmail = "lunaiproject@gmail.com";
        public const string VelopackPackId = "OpenUtau.Lunai";

        public string AppVersion => $"v{Assembly.GetEntryAssembly()?.GetName().Version}";
        public bool IsDarkMode => ThemeManager.IsDarkMode;
        [Reactive] public string UpdaterStatus { get; set; }
        [Reactive] public bool UpdateAvailable { get; set; }
        [Reactive] public FontWeight UpdateButtonFontWeight { get; set; }
        /// <summary>When true, Update button opens GitHub instead of applying a Velopack package.</summary>
        [Reactive] public bool OpenGitHubOnUpdate { get; set; }
        public Action? CloseApplication { get; set; }

        UpdateManager? updateManager;
        UpdateInfo? updateInfo;
        bool updateAccepted;

        public UpdaterViewModel() {
            UpdaterStatus = string.Empty;
            UpdateAvailable = false;
            UpdateButtonFontWeight = FontWeight.Normal;
            OpenGitHubOnUpdate = false;
            _ = InitAsync();
        }

        public static string GetVelopackChannel() {
            string rid = OS.GetUpdaterRid();
            return Preferences.Default.Beta ? $"{rid}-beta" : rid;
        }

        public static UpdateManager? TryCreateUpdateManager() {
            if (!OS.IsWindows()) {
                return null;
            }
            var source = new GithubSource(LunaiRepoUrl, accessToken: null, prerelease: Preferences.Default.Beta);
            var options = new UpdateOptions {
                ExplicitChannel = GetVelopackChannel(),
                AllowVersionDowngrade = true,
            };
            return new UpdateManager(source, options);
        }

        /// <summary>Quiet check for startup prompt. Windows+Velopack only.</summary>
        public static async Task<bool> IsUpdateAvailableQuietlyAsync() {
            try {
                var mgr = TryCreateUpdateManager();
                if (mgr == null || !mgr.IsInstalled) {
                    return false;
                }
                var info = await mgr.CheckForUpdatesAsync();
                if (info?.TargetFullRelease == null) {
                    return false;
                }
                string ver = info.TargetFullRelease.Version.ToString();
                if (ver == Preferences.Default.SkipUpdate) {
                    return false;
                }
                return true;
            } catch (NotInstalledException) {
                return false;
            } catch (Exception e) {
                Log.Warning(e, "Quiet update check failed.");
                return false;
            }
        }

        async Task InitAsync() {
            UpdaterStatus = ThemeManager.GetString("updater.status.checking");
            if (!OS.IsWindows()) {
                UpdaterStatus = ThemeManager.GetString("updater.status.manual");
                OpenGitHubOnUpdate = true;
                UpdateAvailable = true;
                UpdateButtonFontWeight = FontWeight.Bold;
                return;
            }

            try {
                updateManager = TryCreateUpdateManager();
                if (updateManager == null) {
                    UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                    return;
                }
                if (!updateManager.IsInstalled) {
                    UpdaterStatus = ThemeManager.GetString("updater.status.notinstalled");
                    OpenGitHubOnUpdate = true;
                    UpdateAvailable = true;
                    UpdateButtonFontWeight = FontWeight.Bold;
                    return;
                }

                updateInfo = await updateManager.CheckForUpdatesAsync();
                if (updateInfo?.TargetFullRelease == null) {
                    UpdaterStatus = ThemeManager.GetString("updater.status.notavailable");
                    return;
                }

                UpdaterStatus = string.Format(
                    ThemeManager.GetString("updater.status.available"),
                    updateInfo.TargetFullRelease.Version);
                UpdateAvailable = true;
                UpdateButtonFontWeight = FontWeight.Bold;
            } catch (NotInstalledException) {
                UpdaterStatus = ThemeManager.GetString("updater.status.notinstalled");
                OpenGitHubOnUpdate = true;
                UpdateAvailable = true;
                UpdateButtonFontWeight = FontWeight.Bold;
            } catch (Exception e) {
                Log.Error(e, "Failed to check for Velopack updates.");
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
            }
        }

        public void OnGithub() {
            try {
                OS.OpenWeb(LunaiReleasesUrl);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async void OnUpdate() {
            if (OpenGitHubOnUpdate || !OS.IsWindows()) {
                OnGithub();
                return;
            }
            if (updateManager == null || updateInfo?.TargetFullRelease == null) {
                return;
            }

            UpdateAvailable = false;
            updateAccepted = true;
            try {
                await updateManager.DownloadUpdatesAsync(updateInfo, progress => {
                    Dispatcher.UIThread.Post(() => {
                        UpdaterStatus = $"{progress}%";
                    });
                });
                UpdaterStatus = ThemeManager.GetString("updater.status.installing");
                // ApplyUpdatesAndRestart exits the process; CloseApplication is best-effort cleanup.
                CloseApplication?.Invoke();
                updateManager.ApplyUpdatesAndRestart(updateInfo);
            } catch (Exception e) {
                Log.Error(e, "Velopack update failed.");
                UpdateAvailable = true;
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void OnClosing() {
            if (!updateAccepted && updateInfo?.TargetFullRelease != null) {
                string ver = updateInfo.TargetFullRelease.Version.ToString();
                Log.Information($"Skipping update {ver}");
                Preferences.Default.SkipUpdate = ver;
                Preferences.Save();
            }
        }
    }
}
