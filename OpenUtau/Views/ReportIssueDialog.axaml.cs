using Avalonia.Input.Platform;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class ReportIssueDialog : Window {
        public ReportIssueDialog() {
            InitializeComponent();
        }

        void OnOpenDiscord(object? sender, RoutedEventArgs e) {
            try {
                OS.OpenWeb(UpdaterViewModel.LunaiDiscordInviteUrl);
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OnWriteEmail(object? sender, RoutedEventArgs e) {
            try {
                OS.OpenWeb($"mailto:{UpdaterViewModel.LunaiSupportEmail}");
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        async void OnCopyEmail(object? sender, RoutedEventArgs e) {
            try {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null) {
                    await clipboard.SetTextAsync(UpdaterViewModel.LunaiSupportEmail);
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to copy support email");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OnOpenLogs(object? sender, RoutedEventArgs e) {
            try {
                OS.OpenFolder(PathManager.Inst.LogsPath);
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OnClose(object? sender, RoutedEventArgs e) => Close();
    }
}
