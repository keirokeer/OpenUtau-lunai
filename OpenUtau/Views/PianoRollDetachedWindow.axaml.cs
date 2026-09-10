using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    public partial class PianoRollDetachedWindow : Window {
        private const double TikTokAspectRatio = 9.0 / 16.0;

        private readonly PianoRoll pianoRoll;
        private bool forceClose;
        private bool tikTokMode;
        private bool skipSaveSize;
        private bool inTikTokResizeLock;
        private WindowNotificationManager notificationManager;

        public PianoRollDetachedWindow(PianoRoll pianoRoll) {
            InitializeComponent();
            this.pianoRoll = pianoRoll;
            DataContext = pianoRoll.DataContext;

            PianoRollContainer.Content = pianoRoll;
            Opened += (_, _) => pianoRoll.NotifyDetachedLayoutChanged();

            if (Preferences.Default.PianorollWindowSize.TryGetPosition(out int x, out int y)) {
                Position = new PixelPoint(x, y);
            }
            var ws = Preferences.Default.PianorollWindowSize;
            Width = ws.Width;
            Height = ws.Height;
            WindowState = (WindowState)ws.State;

            notificationManager = new WindowNotificationManager(this) {
                Position = NotificationPosition.BottomCenter,
                MaxItems = 3
            };
        }

        public void WindowGotFocus(object sender, GotFocusEventArgs e) {
            if (e.Source is PianoRollDetachedWindow) {
                pianoRoll.Focus();
            }
        }

        public void WindowClosing(object? sender, WindowClosingEventArgs e) {
            if (!skipSaveSize && WindowState != WindowState.Maximized) {
                Preferences.Default.PianorollWindowSize.Set(Width, Height, Position.X, Position.Y, (int)WindowState);
                Preferences.Save();
            }
            Hide();
            e.Cancel = !forceClose;
        }

        public void WindowDeactivated(object sender, EventArgs args) {
            pianoRoll.LyricBox?.EndEdit();
        }

        public void SetTikTokMode(bool enable) {
            tikTokMode = enable;
            skipSaveSize = enable;
            if (enable) {
                inTikTokResizeLock = true;
                Width = 640;
                Height = 1138;
                inTikTokResizeLock = false;
                Resized += OnTikTokResized;
            } else {
                Resized -= OnTikTokResized;
                var ws = Preferences.Default.PianorollWindowSize;
                inTikTokResizeLock = true;
                Width = ws.Width;
                Height = ws.Height;
                if (ws.TryGetPosition(out int x, out int y)) {
                    Position = new PixelPoint(x, y);
                }
                WindowState = (WindowState)ws.State;
                inTikTokResizeLock = false;
            }
        }

        private void OnTikTokResized(object? sender, WindowResizedEventArgs e) {
            if (!tikTokMode || inTikTokResizeLock) return;
            var cs = e.ClientSize;
            double w = cs.Width;
            double h = cs.Height;
            double targetH = w / TikTokAspectRatio;
            double targetW = h * TikTokAspectRatio;
            if (Math.Abs(h - targetH) > Math.Abs(w - targetW)) {
                w = targetW;
            } else {
                h = targetH;
            }
            if (Math.Abs(w - cs.Width) > 0.5 || Math.Abs(h - cs.Height) > 0.5) {
                inTikTokResizeLock = true;
                ClientSize = new Avalonia.Size(w, h);
                inTikTokResizeLock = false;
            }
        }

        public void ForceClose() {
            PianoRollContainer.Content = null;
            forceClose = true;
            Close();
        }

        public bool Toast(ToastNotification toast) {
            if (this.IsActive) {
                notificationManager.Show(ToastControl.GetNotification(toast, this));
                return true;
            }
            return false;
        }
    }
}
