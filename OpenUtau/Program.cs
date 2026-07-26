using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Serilog;
using Velopack;
using Velopack.Logging;

namespace OpenUtau.App {
    public class Program {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) {
            // Must be first: Velopack install/update/uninstall hooks exit inside Run().
            VelopackApp.Build()
                .SetLogger(new SerilogVelopackLogger())
                .Run();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitLogging();
            string processName = Process.GetCurrentProcess().ProcessName;
            if (processName != "dotnet") {
                var exists = Process.GetProcessesByName(processName).Count() > 1;
                if (exists) {
                    Log.Information($"Process {processName} already open. Exiting.");
                    return;
                }
            }
            Log.Information($"{Environment.OSVersion}");
            Log.Information($"{RuntimeInformation.OSDescription} " +
                $"{RuntimeInformation.OSArchitecture} " +
                $"{RuntimeInformation.ProcessArchitecture}");
            Log.Information($"OpenUtau Lunai v{Assembly.GetEntryAssembly()?.GetName().Version} " +
                $"{RuntimeInformation.RuntimeIdentifier}");
            Log.Information($"Installed = {PathManager.Inst.IsInstalled}");
            Log.Information($"Data path = {PathManager.Inst.DataPath}");
            Log.Information($"Legacy data path = {PathManager.Inst.LegacyDataPath}");
            Log.Information($"Singers path = {PathManager.Inst.SingersPath}");
            Log.Information($"Cache path = {PathManager.Inst.CachePath}");
            Log.Information($"System encoding = {Encoding.GetEncoding(0)?.WebName ?? "null"}");
            OpenUtau.Core.Util.InstallLayoutCleanup.Run();
            try {
                Run(args);
                Log.Information($"Exiting.");
            } finally {
                if (!OS.IsMacOS()) {
                    NetMQ.NetMQConfig.Cleanup(/*block=*/false);
                    // Cleanup() hangs on macOS https://github.com/zeromq/netmq/issues/1018
                }
            }
            Log.Information($"Exited.");
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp() {
            FontManagerOptions fontOptions = new();
            if (OS.IsLinux()) {
                using Process process = Process.Start(new ProcessStartInfo("fc-match")
                {
                    ArgumentList = { "-f", "%{family}" },
                    RedirectStandardOutput = true
                })!;
                process.WaitForExit();

                string fontFamily = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(fontFamily)) {
                    string [] fontFamilies = fontFamily.Split(',');
                    fontOptions.DefaultFamilyName = fontFamilies[0];
                }
            } else if (OS.IsMacOS()) {
                //To avoid text display corruption, specify Hiragino Sans font first.
                //Due to the specification of AvaloniaUI, this only affects when the language is set to Japanese.
                fontOptions.DefaultFamilyName = "Hiragino Sans, Segoe UI, San Francisco, Helvetica Neue";
            }
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI()
                .With(fontOptions)
                .With(new X11PlatformOptions {EnableIme = true});
        }

        public static void Run(string[] args)
            => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(
                    args, ShutdownMode.OnMainWindowClose);

        public static void InitLogging() {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.Information()
                    .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8))
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.ControlledBy(DebugViewModel.Sink.Inst.LevelSwitch)
                    .WriteTo.Sink(DebugViewModel.Sink.Inst))
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                Log.Error((Exception)args.ExceptionObject, "Unhandled exception");
            });
            Log.Information("Logging initialized.");
        }

        /// <summary>Routes Velopack diagnostics to Serilog when available, else Debug.</summary>
        sealed class SerilogVelopackLogger : IVelopackLogger {
            public void Log(VelopackLogLevel level, string? message, Exception? exception) {
                message ??= string.Empty;
                if (Serilog.Log.Logger == null) {
                    System.Diagnostics.Debug.WriteLine($"[Velopack:{level}] {message}");
                    return;
                }
                switch (level) {
                    case VelopackLogLevel.Trace:
                    case VelopackLogLevel.Debug:
                        Serilog.Log.Debug(exception, "[Velopack] {Message}", message);
                        break;
                    case VelopackLogLevel.Information:
                        Serilog.Log.Information(exception, "[Velopack] {Message}", message);
                        break;
                    case VelopackLogLevel.Warning:
                        Serilog.Log.Warning(exception, "[Velopack] {Message}", message);
                        break;
                    case VelopackLogLevel.Error:
                    case VelopackLogLevel.Critical:
                        Serilog.Log.Error(exception, "[Velopack] {Message}", message);
                        break;
                    default:
                        Serilog.Log.Information(exception, "[Velopack] {Message}", message);
                        break;
                }
            }
        }
    }
}
