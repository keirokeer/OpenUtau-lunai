using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class UpdaterDialog : Window {
        public readonly UpdaterViewModel ViewModel;
        public UpdaterDialog() {
            InitializeComponent();
            DataContext = ViewModel = new UpdaterViewModel();
        }

        void OnClosing(object sender, WindowClosingEventArgs e) {
            ViewModel.OnClosing();
        }

        /// <summary>Whether CheckForUpdate runs. UI tests turn it off to stay offline.</summary>
        public static bool CheckForUpdateEnabled { get; set; } = true;

        public static void CheckForUpdate(Action<Window> showDialog, Action closeApplication, TaskScheduler scheduler) {
            if (!CheckForUpdateEnabled) {
                return;
            }
            Task.Run(async () => await UpdaterViewModel.IsUpdateAvailableQuietlyAsync())
                .ContinueWith(t => {
                    if (t.IsCompletedSuccessfully && t.Result) {
                        var dialog = new UpdaterDialog();
                        dialog.ViewModel.CloseApplication = closeApplication;
                        showDialog.Invoke(dialog);
                    }
                    if (t.IsFaulted) {
                        Log.Error(t.Exception, "Failed to check for update");
                    }
                }, scheduler).ContinueWith((t2, _) => {
                    if (t2.IsFaulted) {
                        Log.Error(t2.Exception, "Failed to check for update");
                    }
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
