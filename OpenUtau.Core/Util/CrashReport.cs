using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Writes text crash reports (+ Windows minidumps) when the process is dying.
    /// Native faults (e.g. DirectML AccessViolation) often skip managed catch blocks;
    /// the OS unhandled-exception filter still gets a chance to dump before exit.
    /// </summary>
    public static class CrashReport {
        const string OnnxAttemptFileName = "onnx-last-attempt.txt";
        const int MiniDumpWithDataSegs = 0x00000001;
        const int MiniDumpWithHandleData = 0x00000004;
        const int MiniDumpWithThreadInfo = 0x00001000;
        const int MiniDumpWithUnloadedModules = 0x00000020;

        static int nativeFilterInstalled;
        static int reporting;
        static UnhandledExceptionFilterDelegate? keepAliveFilter;

        public static void InstallNativeHandlers() {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return;
            }
            if (Interlocked.Exchange(ref nativeFilterInstalled, 1) != 0) {
                return;
            }
            keepAliveFilter = OnUnhandledNativeException;
            SetUnhandledExceptionFilter(keepAliveFilter);
        }

        /// <summary>
        /// If a previous run died mid ONNX session create, promote the breadcrumb
        /// into a crash report and Serilog entry (no preference changes).
        /// </summary>
        public static void CheckPreviousOnnxAttempt() {
            string? breadcrumb = TryGetOnnxAttemptPath();
            if (breadcrumb == null || !File.Exists(breadcrumb)) {
                return;
            }
            string body;
            try {
                body = File.ReadAllText(breadcrumb);
            } catch (Exception e) {
                Log.Warning(e, "Failed to read leftover ONNX attempt breadcrumb");
                return;
            }
            try {
                File.Delete(breadcrumb);
            } catch {
                // keep going — still write a report
            }
            var ex = new Exception(
                "Previous OpenUtau process appears to have terminated while creating an ONNX session " +
                "(often a native AccessViolation inside DirectML/ORT). Preferences were left unchanged.");
            string? report = Write(
                ex,
                "previous-onnx-session-create",
                writeDump: false,
                extra: body);
            Log.Fatal(
                "Recovered evidence of a prior ONNX-related process death. Crash report: {Report}\n{Breadcrumb}",
                report ?? "(failed to write)",
                body);
        }

        public static void MarkOnnxSessionCreateStarting() {
            string? path = TryGetOnnxAttemptPath();
            if (path == null) {
                return;
            }
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var sb = new StringBuilder();
                sb.AppendLine($"utc={DateTime.UtcNow:o}");
                sb.AppendLine($"pid={Environment.ProcessId}");
                sb.AppendLine($"runner={Preferences.Default.OnnxRunner}");
                sb.AppendLine($"onnxGpu={Preferences.Default.OnnxGpu}");
                sb.AppendLine($"os={RuntimeInformation.OSDescription}");
                sb.AppendLine($"rid={RuntimeInformation.RuntimeIdentifier}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Log.Warning(
                    "ONNX InferenceSession create starting (runner={Runner}, gpu={Gpu}). " +
                    "If the process dies here, a crash report will be built on next launch from {Path}",
                    Preferences.Default.OnnxRunner,
                    Preferences.Default.OnnxGpu,
                    path);
            } catch (Exception e) {
                Log.Debug(e, "Failed to write ONNX attempt breadcrumb");
            }
        }

        public static void MarkOnnxSessionCreateFinished() {
            string? path = TryGetOnnxAttemptPath();
            if (path == null) {
                return;
            }
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception e) {
                Log.Debug(e, "Failed to clear ONNX attempt breadcrumb");
            }
        }

        /// <summary>
        /// Writes a text report under CrashReports/ and optionally a .dmp on Windows.
        /// Returns the text report path when successful.
        /// </summary>
        public static string? Write(
            Exception? ex,
            string reason,
            bool writeDump = true,
            string? extra = null,
            IntPtr exceptionPointers = default) {
            if (Interlocked.Exchange(ref reporting, 1) != 0) {
                // Nested fault while already reporting — avoid recursion.
                return null;
            }
            try {
                string dir = PathManager.Inst.CrashReportsPath;
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string baseName = $"crash-{stamp}-{Sanitize(reason)}";
                string textPath = Path.Combine(dir, baseName + ".txt");

                var sb = new StringBuilder(8 * 1024);
                sb.AppendLine("OpenUtau Lunai crash report");
                sb.AppendLine($"utc={DateTime.UtcNow:o}");
                sb.AppendLine($"local={DateTime.Now:o}");
                sb.AppendLine($"reason={reason}");
                sb.AppendLine($"pid={Environment.ProcessId}");
                sb.AppendLine($"version={Assembly.GetEntryAssembly()?.GetName().Version}");
                sb.AppendLine($"os={Environment.OSVersion}");
                sb.AppendLine($"osDescription={RuntimeInformation.OSDescription}");
                sb.AppendLine($"osArch={RuntimeInformation.OSArchitecture}");
                sb.AppendLine($"processArch={RuntimeInformation.ProcessArchitecture}");
                sb.AppendLine($"rid={RuntimeInformation.RuntimeIdentifier}");
                try {
                    sb.AppendLine($"dataPath={PathManager.Inst.DataPath}");
                    sb.AppendLine($"logFile={PathManager.Inst.LogFilePath}");
                    sb.AppendLine($"onnxRunner={Preferences.Default.OnnxRunner}");
                    sb.AppendLine($"onnxGpu={Preferences.Default.OnnxGpu}");
                } catch {
                    // PathManager/prefs may be unavailable during early/native faults.
                }
                if (!string.IsNullOrWhiteSpace(extra)) {
                    sb.AppendLine();
                    sb.AppendLine("--- extra ---");
                    sb.AppendLine(extra);
                }
                if (ex != null) {
                    sb.AppendLine();
                    sb.AppendLine("--- exception ---");
                    sb.AppendLine(ex.ToString());
                }
                File.WriteAllText(textPath, sb.ToString(), Encoding.UTF8);

                try {
                    if (writeDump) {
                        Log.Fatal(ex, "Crash report written to {Path} (reason={Reason})", textPath, reason);
                    } else {
                        Log.Error(ex, "Crash report written to {Path} (reason={Reason})", textPath, reason);
                    }
                } catch {
                    // Serilog may already be torn down.
                }

                if (writeDump && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    string dumpPath = Path.Combine(dir, baseName + ".dmp");
                    TryWriteMiniDump(dumpPath, exceptionPointers);
                }
                if (writeDump) {
                    // Process is likely dying — flush so the log sink hits disk.
                    try { Log.CloseAndFlush(); } catch { }
                }
                return textPath;
            } catch {
                return null;
            } finally {
                Interlocked.Exchange(ref reporting, 0);
            }
        }

        static string? TryGetOnnxAttemptPath() {
            try {
                string logs = PathManager.Inst.LogsPath;
                if (string.IsNullOrWhiteSpace(logs)) {
                    return null;
                }
                return Path.Combine(logs, OnnxAttemptFileName);
            } catch {
                return null;
            }
        }

        static string Sanitize(string reason) {
            foreach (char c in Path.GetInvalidFileNameChars()) {
                reason = reason.Replace(c, '_');
            }
            if (reason.Length > 48) {
                reason = reason.Substring(0, 48);
            }
            return string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
        }

        static long OnUnhandledNativeException(IntPtr exceptionInfo) {
            try {
                uint code = 0;
                if (exceptionInfo != IntPtr.Zero) {
                    var pointers = Marshal.PtrToStructure<ExceptionPointers>(exceptionInfo);
                    if (pointers.ExceptionRecord != IntPtr.Zero) {
                        var record = Marshal.PtrToStructure<ExceptionRecord>(pointers.ExceptionRecord);
                        code = record.ExceptionCode;
                    }
                }
                Write(
                    new Exception($"Native unhandled exception code=0x{code:X8}"),
                    $"native-0x{code:X8}",
                    writeDump: true,
                    extra: "Captured by SetUnhandledExceptionFilter before process termination.",
                    exceptionPointers: exceptionInfo);
            } catch {
                // Best effort only.
            }
            // EXCEPTION_CONTINUE_SEARCH — let the normal crash path proceed after we dumped.
            return 0;
        }

        static void TryWriteMiniDump(string dumpPath, IntPtr exceptionPointers) {
            try {
                using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                var process = Process.GetCurrentProcess();
                var exceptInfo = new MiniDumpExceptionInformation {
                    ThreadId = GetCurrentThreadId(),
                    ExceptionPointers = exceptionPointers,
                    ClientPointers = 0,
                };
                int dumpType = MiniDumpWithDataSegs | MiniDumpWithHandleData |
                    MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules;
                bool ok;
                unsafe {
                    ok = MiniDumpWriteDump(
                        process.Handle,
                        (uint)process.Id,
                        fs.SafeFileHandle,
                        dumpType,
                        exceptionPointers == IntPtr.Zero ? IntPtr.Zero : (IntPtr)(&exceptInfo),
                        IntPtr.Zero,
                        IntPtr.Zero);
                }
                if (!ok) {
                    try { File.Delete(dumpPath); } catch { }
                }
            } catch {
                try { File.Delete(dumpPath); } catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ExceptionPointers {
            public IntPtr ExceptionRecord;
            public IntPtr ContextRecord;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ExceptionRecord {
            public uint ExceptionCode;
            public uint ExceptionFlags;
            public IntPtr ExceptionRecordPtr;
            public IntPtr ExceptionAddress;
            public uint NumberParameters;
            // EXCEPTION_RECORD has ExceptionInformation[15] — unused here.
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct MiniDumpExceptionInformation {
            public uint ThreadId;
            public IntPtr ExceptionPointers;
            public int ClientPointers;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate long UnhandledExceptionFilterDelegate(IntPtr exceptionInfo);

        [DllImport("kernel32.dll")]
        static extern IntPtr SetUnhandledExceptionFilter(UnhandledExceptionFilterDelegate lpTopLevelExceptionFilter);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeFileHandle hFile,
            int dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);
    }
}
