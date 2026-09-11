using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.Views {
    public partial class SingerHubPane : UserControl {
        int scrollStyleApplyGeneration;

        public SingerHubPane() {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => ScheduleApplyScrollStyle();
            DetachedFromVisualTree += (_, _) => scrollStyleApplyGeneration++;
            MessageBus.Current.Listen<ScrollbarsStyleChangedEvent>()
                .Subscribe(_ => ScheduleApplyScrollStyle());
        }

        void ScheduleApplyScrollStyle() {
            if (!WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                return;
            }
            int generation = ++scrollStyleApplyGeneration;
            Dispatcher.UIThread.Post(() => {
                if (generation != scrollStyleApplyGeneration || !WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                    return;
                }
                ApplyScrollStyle();
            }, DispatcherPriority.Loaded);
        }

        void ApplyScrollStyle() {
            if (!WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                return;
            }
            bool classic = WorkspaceScrollbarHelper.UseClassicScrollbars;
            WorkspaceScrollbarHelper.ApplyScrollViewer(ContentScroll, classic);
            ContentScroll.Padding = new Thickness(0);
            if (!classic) {
                foreach (var bar in ContentScroll.GetVisualDescendants().OfType<ScrollBar>()) {
                    if (bar.Orientation == Orientation.Vertical) {
                        bar.Margin = new Thickness(0, 0, 0, 0);
                        bar.HorizontalAlignment = HorizontalAlignment.Right;
                    }
                }
            }
        }

        static Window? GetOwnerWindow(Visual? visual) {
            if (TopLevel.GetTopLevel(visual) is Window w) {
                return w;
            }
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            return lifetime?.MainWindow;
        }

        async void OnPrimaryActionClick(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is SingerHubViewModel vm &&
                    sender is Button b &&
                    b.DataContext is SingerHubRowViewModel row &&
                    row.CanInstall) {
                    var owner = GetOwnerWindow(this);
                    if (owner == null) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException("No owner window for Singer Hub dialog.")));
                        return;
                    }
                    var msg = string.Format(ThemeManager.GetString("lunai.confirm.install.message"), row.Name);
                    if (!string.IsNullOrEmpty(row.TosUrl)) {
                        msg += "\n" + ThemeManager.GetString("lunai.confirm.install.tos") + " " + row.TosUrl;
                    }
                    var caption = ThemeManager.GetString("lunai.confirm.install.caption");
                    var result = await MessageBox.Show(owner, msg, caption, MessageBox.MessageBoxButtons.YesNo);
                    if (result != MessageBox.MessageBoxResult.Yes) return;
                    await vm.InstallOrUpdateAsync(row);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OnOpenWebsiteClick(object? sender, RoutedEventArgs e) {
            OpenHttpUrl((sender as Control)?.DataContext as SingerHubRowViewModel, row => row.PageUrl);
        }

        static void OpenHttpUrl(SingerHubRowViewModel? row, Func<SingerHubRowViewModel, string> urlSelector) {
            if (row == null) {
                return;
            }
            var url = urlSelector(row);
            if (string.IsNullOrEmpty(url)) {
                return;
            }
            try {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                    return;
                }
                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
                OS.OpenWeb(uri.ToString());
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }
    }
}
