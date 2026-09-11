using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Serilog;
using SharpCompress;

namespace OpenUtau.App.Views {
    public partial class MessageBox : Window {
        public enum MessageBoxButtons { Ok, OkCancel, YesNo, YesNoCancel, OkCopy, DropProjectOpenImportCancel }
        public enum MessageBoxResult { Ok, Cancel, Yes, No, OpenPackageManager, OpenUrl }

        public MessageBox() {
            InitializeComponent();
        }

        public void SetText(string text) {
            Dispatcher.UIThread.Post(() => {
                Text.Text = text;
            });
        }

        public static Task<MessageBoxResult> ShowError(Window parent, Exception? e, string message = "", bool fromNotif = false) {
            string text = message;
            string title = ThemeManager.GetString("errors.caption");
            if (fromNotif) {
                IReadOnlyList<Window> dialogs = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
                foreach (var dialog in dialogs) {
                    if (dialog.IsActive) {
                        parent = dialog;
                        break;
                    }
                }
            }

            var builder = new StringBuilder();
            if (e != null) {
                if (e is AggregateException ae && ae.Flatten().InnerExceptions.Count == 1) {
                    e = ae.InnerExceptions.First();
                }

                if (e is MessageCustomizableException mce) {
                    text = Translate(mce);
                    if (mce.SuggestPackageManager || !string.IsNullOrWhiteSpace(mce.SuggestDownloadUrl)) {
                        return ShowInstallPrompt(parent, text, title, new InstallPromptOptions {
                            OfferPackageManager = mce.SuggestPackageManager,
                            DownloadUrl = mce.SuggestDownloadUrl,
                        });
                    }
                    builder.AppendLine(mce.SubstanceException.Message);
                    builder.AppendLine();
                    builder.Append(mce.SubstanceException.ToString());
                    if (!mce.ShowStackTrace) {
                        return Show(parent, text, title, MessageBoxButtons.Ok);
                    }
                } else if (e is AggregateException nestedAe) {
                    foreach (var ie in nestedAe.Flatten().InnerExceptions) {
                        if (!string.IsNullOrWhiteSpace(text)) {
                            text += "\n";
                        }
                        if (ie is MessageCustomizableException innnerMce) {
                            text += Translate(innnerMce);
                            builder.AppendLine(innnerMce.SubstanceException.Message);
                            builder.AppendLine();
                            builder.Append(innnerMce.SubstanceException.ToString());
                        } else {
                            text += ie.Message;
                            builder.AppendLine(ie.Message);
                            builder.AppendLine();
                            builder.AppendLine(ie.ToString());
                        }
                        builder.AppendLine();
                    }
                } else {
                    builder.AppendLine(e.Message);
                    builder.AppendLine();
                    builder.Append(e.ToString());
                    if (string.IsNullOrEmpty(text)) {
                        text = e.Message;
                    }
                }
            }
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown Version");

            return Show(parent, text, title, MessageBoxButtons.OkCopy, builder.ToString());

            string Translate(MessageCustomizableException mce) {
                string text;
                if (string.IsNullOrWhiteSpace(mce.TranslatableMessage)) {
                    text = mce.Message;
                } else {
                    text = mce.TranslatableMessage;
                    try {
                        var matches = Regex.Matches(mce.TranslatableMessage, "<translate:(.*?)>");
                        foreach (Match match in matches) {
                            if (ThemeManager.TryGetString(match.Groups[1].Value, out string translated)) {
                                text = text.Replace(match.Value, translated);
                            } else {
                                text = mce.Message;
                                break;
                            }
                        }
                    } catch {
                        text = mce.Message;
                    }
                }

                if (mce.Replaces != null && mce.Replaces.Length > 0) {
                    return string.Format(text, mce.Replaces);
                } else {
                    return text;
                }
            }
        }

        public static Task<MessageBoxResult> Show(Window parent, string text, string title, MessageBoxButtons buttons, string? stackTrace = null) {
            var msgbox = new MessageBox() {
                Title = title
            };
            msgbox.Text.IsVisible = false;
            var linkBrush = GetAccentBrush(parent);
            msgbox.SetTextWithLink(text, msgbox.TextPanel, linkBrush);
            if (stackTrace != null) {
                var stackTracePanel = new StackPanel();
                var expander = new Expander() { Header = ThemeManager.GetString("errors.details"), Content = stackTracePanel };
                msgbox.TextPanel.Children.Add(expander);
                msgbox.SetTextWithLink(stackTrace, stackTracePanel, null);
            }

            var res = MessageBoxResult.Ok;

            void AddButton(string caption, MessageBoxResult r, bool def = false) {
                var btn = new Button { Content = caption };
                btn.Click += (_, __) => {
                    res = r;
                    msgbox.Close();
                };
                msgbox.Buttons.Children.Add(btn);
                if (def)
                    res = r;
            }

            if (buttons == MessageBoxButtons.Ok || buttons == MessageBoxButtons.OkCancel || buttons == MessageBoxButtons.OkCopy)
                AddButton(ThemeManager.GetString("button.ok"), MessageBoxResult.Ok, true);
            if (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.YesNoCancel) {
                AddButton(ThemeManager.GetString("button.yes"), MessageBoxResult.Yes);
                AddButton(ThemeManager.GetString("button.no"), MessageBoxResult.No, true);
            }
            if (buttons == MessageBoxButtons.DropProjectOpenImportCancel) {
                AddButton(ThemeManager.GetString("dialogs.dropproject.open"), MessageBoxResult.Yes);
                AddButton(ThemeManager.GetString("dialogs.dropproject.import"), MessageBoxResult.No);
                AddButton(ThemeManager.GetString("dialogs.messagebox.cancel"), MessageBoxResult.Cancel, true);
            }

            if (buttons == MessageBoxButtons.OkCancel || buttons == MessageBoxButtons.YesNoCancel)
                AddButton(ThemeManager.GetString("button.cancel"), MessageBoxResult.Cancel, true);
            if (buttons == MessageBoxButtons.OkCopy) {
                var btn = new Button { Content = ThemeManager.GetString("dialogs.messagebox.copy") };
                btn.Click += (_, __) => {
                    try {
                        GetTopLevel(parent)?.Clipboard?.SetTextAsync(text + "\n" + stackTrace);
                    } catch { }
                };
                msgbox.Buttons.Children.Add(btn);
            }

            var tcs = new TaskCompletionSource<MessageBoxResult>();
            msgbox.Closed += delegate { tcs.TrySetResult(res); };
            if (parent != null)
                msgbox.ShowDialog(parent);
            else msgbox.Show();
            return tcs.Task;
        }

        public static Task<MessageBoxResult> ShowInstallPrompt(
                Window parent,
                string text,
                string title,
                InstallPromptOptions options) {
            var msgbox = new MessageBox() {
                Title = title,
            };
            msgbox.Text.IsVisible = false;
            SetPlainText(text, msgbox.TextPanel);

            var res = MessageBoxResult.Cancel;
            void AddActionButton(string caption, MessageBoxResult result, bool isDefault = false) {
                var btn = new Button { Content = caption, IsDefault = isDefault };
                btn.Click += (_, __) => {
                    res = result;
                    msgbox.Close();
                };
                msgbox.Buttons.Children.Add(btn);
            }

            if (options.OfferPackageManager) {
                AddActionButton(
                    ThemeManager.GetString("dialogs.messagebox.openpackages"),
                    MessageBoxResult.OpenPackageManager,
                    isDefault: true);
            }
            if (!string.IsNullOrWhiteSpace(options.DownloadUrl)) {
                AddActionButton(
                    ThemeManager.GetString("dialogs.messagebox.openlink"),
                    MessageBoxResult.OpenUrl);
            }
            AddActionButton(ThemeManager.GetString("button.cancel"), MessageBoxResult.Cancel);
            if (msgbox.Buttons.Children[msgbox.Buttons.Children.Count - 1] is Button cancelButton) {
                cancelButton.IsCancel = true;
            }

            var tcs = new TaskCompletionSource<MessageBoxResult>();
            msgbox.Closed += (_, __) => {
                try {
                    if (res == MessageBoxResult.OpenPackageManager) {
                        OpenPackageManager();
                    } else if (res == MessageBoxResult.OpenUrl && !string.IsNullOrWhiteSpace(options.DownloadUrl)) {
                        OS.OpenWeb(options.DownloadUrl);
                    }
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to run install prompt action.");
                }
                tcs.TrySetResult(res);
            };
            if (parent != null) {
                msgbox.ShowDialog(parent);
            } else {
                msgbox.Show();
            }
            return tcs.Task;
        }

        static void OpenPackageManager() {
            var dialog = new PackageManagerDialog() { DataContext = new PackageManagerViewModel() };
            dialog.Show();
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        static void SetPlainText(string text, StackPanel textPanel) {
            textPanel.Children.Add(new TextBlock {
                Text = text,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        public static MessageBox ShowModal(Window parent, string text, string title) {
            var msgbox = new MessageBox() {
                Title = title
            };
            msgbox.Text.Text = text;
            msgbox.ShowDialog(parent);
            return msgbox;
        }

        /// <summary>
        /// Displays a processing message box with a specified text and title, and executes a given action asynchronously.
        /// </summary>
        /// <param name="parent">The parent window to which the message box belongs.</param>
        /// <param name="text">The text to display in the message box.</param>
        /// <param name="title">The title of the message box.</param>
        /// <param name="action">The action to execute asynchronously. This action takes the message box instance and a cancellation token as parameters, so it can show progress on the message box.</param>
        /// <param name="onFinished">An optional action to execute when the asynchronous operation is completed. Takes the task representing the operation as a parameter. Usually it should check if the task is faulted and handle the error thrown during the task, such as showing an error dialog.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is a <see cref="MessageBoxResult"/> indicating the user's response.</returns>
        /// <remarks>
        /// The method initializes a message box with the specified title and text, and runs the provided action in a separate task.
        /// It supports cancellation through the provided cancellation token. The optional onFinished action allows for additional
        /// operations to be performed once the asynchronous action completes.
        /// </remarks>
        public static Task<MessageBoxResult> ShowProcessing(
                Window parent, 
                string text, 
                string title, 
                Action<MessageBox, 
                CancellationToken> action,
                Action<Task>? onFinished= null) {
            var msgbox = new MessageBox() {
                Title = title
            };
            msgbox.Text.Text = text;
            var res = MessageBoxResult.Ok;
            var tokenSource = new CancellationTokenSource();

            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var task = Task.Run(() => {
                action.Invoke(msgbox, tokenSource.Token);
                return res;
            }, tokenSource.Token);
            task.ContinueWith(t => {
                msgbox.Close();
                if (onFinished != null) {
                    onFinished(task);
                }
            }, scheduler);

            var btn = new Button { Content = ThemeManager.GetString("button.cancel") };
            btn.Click += (_, __) => {
                msgbox.Close();
            };
            msgbox.Buttons.Children.Add(btn);
            msgbox.Closed += delegate {
                if (task.IsCompleted) return;
                res = MessageBoxResult.Cancel;
                tokenSource.Cancel();
            };
            msgbox.ShowDialog(parent);

            return task;
        }

        private static IBrush? GetAccentBrush(Window? parent) {
            if (parent != null && parent.TryFindResource("AccentBrush1", out var res) && res is IBrush brush)
                return brush;
            if (Application.Current?.TryFindResource("AccentBrush1", out var appRes) == true && appRes is IBrush appBrush)
                return appBrush;
            return ThemeManager.AccentBrush1;
        }

        private static string TrimTrailingUrlPunctuation(string url) {
            return url.TrimEnd('.', ',', ';', '!', '?', ')', ']', '>', '"', '\'');
        }

        private void SetTextWithLink(string text, StackPanel textPanel, IBrush? linkBrush) {
            var regex = new Regex(@"https?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = regex.Match(text);
            if (match.Success) {
                textPanel.Children.Add(new TextBlock { Text = text.Substring(0, match.Index), TextAlignment = Avalonia.Media.TextAlignment.Center });
                var rawUrl = match.Value;
                var url = TrimTrailingUrlPunctuation(rawUrl);
                var hyperlink = new Button {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Background = Brushes.Transparent,
                    BorderThickness = new Avalonia.Thickness(0),
                    Padding = new Avalonia.Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Content = new TextBlock {
                        Text = url,
                        Foreground = linkBrush ?? ThemeManager.AccentBrush1,
                        TextDecorations = TextDecorations.Underline
                    },
                    Tag = url
                };
                hyperlink.Click += OnUrlClick;
                textPanel.Children.Add(hyperlink);

                var trailing = rawUrl.Substring(url.Length) + text.Substring(match.Index + match.Length);
                SetTextWithLink(trailing, textPanel, linkBrush);
            } else {
                if (!string.IsNullOrEmpty(text)) {
                    textPanel.Children.Add(new TextBlock { Text = text, TextAlignment = Avalonia.Media.TextAlignment.Center });
                }
            }
        }
        private void OnUrlClick(object? sender, RoutedEventArgs e) {
            try {
                if (sender is Button button && button.Tag is string url) {
                    OS.OpenWeb(url);
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to open url");
            }
        }
    }
}
