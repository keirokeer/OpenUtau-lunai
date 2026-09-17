using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OpenUtau.App.Views;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App {
    /// <summary>
    /// Last-resort handlers so managed failures surface as an error dialog instead of a silent exit.
    /// Native/process-corrupting faults (e.g. AccessViolation inside OnnxRuntime) still terminate the
    /// process without a managed exception — those cannot be caught here. We flush logs and try a
    /// blocking OS dialog when any fatal path does reach managed code.
    /// </summary>
    static class UnhandledExceptionGuard {
        static int installedEarly;
        static int installedUi;
        static int dialogOpen;

        public static void InstallEarly() {
            if (Interlocked.Exchange(ref installedEarly, 1) != 0) {
                return;
            }
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        public static void InstallUiThreadHandlers() {
            if (Interlocked.Exchange(ref installedUi, 1) != 0) {
                return;
            }
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        }

        public static void ReportFatal(Exception ex) {
            CrashReport.Write(ex, "report-fatal");
            ShowNativeFatalDialog(ex);
        }

        static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e) {
            Log.Error(e.Exception, "Unhandled UI thread exception");
            bool fatal = IsNonRecoverable(e.Exception);
            if (fatal) {
                CrashReport.Write(e.Exception, "ui-nonrecoverable");
                ShowNativeFatalDialog(e.Exception);
                return;
            }
            ShowUserDialog(e.Exception);
            e.Handled = true;
        }

        static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
            ShowUserDialog(e.Exception);
        }

        static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) {
            var ex = e.ExceptionObject as Exception
                ?? new Exception($"Non-Exception unhandled: {e.ExceptionObject}");
            Log.Fatal(ex, "Unhandled domain exception (IsTerminating={IsTerminating})", e.IsTerminating);
            CrashReport.Write(
                ex,
                e.IsTerminating ? "domain-terminating" : "domain-nonterminating",
                writeDump: e.IsTerminating || IsNonRecoverable(ex));
            if (e.IsTerminating || IsNonRecoverable(ex)) {
                ShowNativeFatalDialog(ex);
            } else {
                ShowUserDialog(ex);
            }
        }

        static bool IsNonRecoverable(Exception ex) {
            for (Exception? cur = ex; cur != null; cur = cur.InnerException) {
                if (cur is AccessViolationException
                    or SEHException
                    or StackOverflowException
                    or OutOfMemoryException
                    or BadImageFormatException) {
                    return true;
                }
            }
            return false;
        }

        static void ShowUserDialog(Exception ex) {
            if (Interlocked.CompareExchange(ref dialogOpen, 1, 0) != 0) {
                return;
            }
            void Show() {
                try {
                    string message = ThemeManager.GetString("errors.unhandled.message");
                    if (string.IsNullOrWhiteSpace(message) || message.StartsWith("errors.", StringComparison.Ordinal)) {
                        message = "An unexpected error occurred. The application will try to continue. Save your work if you can.";
                    }
                    var parent = TryGetActiveWindow();
                    if (parent != null) {
                        _ = MessageBox.ShowError(parent, ex, message).ContinueWith(
                            _ => Interlocked.Exchange(ref dialogOpen, 0));
                        return;
                    }
                    ShowNativeFatalDialog(ex, message);
                } catch (Exception showEx) {
                    Log.Error(showEx, "Failed to show unhandled-exception dialog");
                    ShowNativeFatalDialog(ex);
                }
                Interlocked.Exchange(ref dialogOpen, 0);
            }

            try {
                if (Dispatcher.UIThread.CheckAccess()) {
                    Show();
                } else {
                    Dispatcher.UIThread.Post(Show);
                }
            } catch {
                Interlocked.Exchange(ref dialogOpen, 0);
                ShowNativeFatalDialog(ex);
            }
        }

        static Window? TryGetActiveWindow() {
            try {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                    return desktop.Windows.FirstOrDefault(w => w.IsActive)
                        ?? desktop.MainWindow
                        ?? desktop.Windows.FirstOrDefault();
                }
            } catch {
                // Application may already be tearing down.
            }
            return null;
        }

        static void ShowNativeFatalDialog(Exception ex, string? preface = null) {
            try {
                string title = "OpenUtau Lunai";
                try {
                    string caption = ThemeManager.GetString("errors.caption");
                    if (!string.IsNullOrWhiteSpace(caption) && !caption.StartsWith("errors.", StringComparison.Ordinal)) {
                        title = caption;
                    }
                } catch {
                    // ThemeManager may be unavailable during teardown.
                }
                string body = preface ?? ThemeManagerSafeFatalMessage();
                body += "\n\n" + ex.GetType().Name + ": " + ex.Message;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    MessageBoxW(IntPtr.Zero, Truncate(body, 1500), title, 0x00000010u);
                }
            } catch {
                // Last resort — nothing else we can do.
            }
        }

        static string ThemeManagerSafeFatalMessage() {
            try {
                string s = ThemeManager.GetString("errors.unhandled.fatal");
                if (!string.IsNullOrWhiteSpace(s) && !s.StartsWith("errors.", StringComparison.Ordinal)) {
                    return s;
                }
            } catch {
            }
            return "A fatal error occurred and OpenUtau Lunai must close. Details were written to the log.";
        }

        static string Truncate(string text, int max) {
            if (string.IsNullOrEmpty(text) || text.Length <= max) {
                return text;
            }
            return text.Substring(0, max - 3) + "...";
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
    }
}
