using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Detects leftover nested binary installs (e.g. OpenUtau-win-x64\OpenUtau\) that can
    /// shadow-load an older OpenUtau.Core and cause MissingFieldException on API fields.
    /// Never touches Documents data paths.
    /// </summary>
    public static class InstallLayoutCleanup {
        const int BakRetentionDays = 7;
        const string NestedFolderName = "OpenUtau";
        const string BakPrefix = "OpenUtau.bak-stale-";

        public static void Run() {
            try {
                Run(GetExeDirectory());
            } catch (Exception e) {
                Log.Warning(e, "Install layout cleanup failed.");
            }
        }

        /// <summary>Testable entry: clean a specific application directory.</summary>
        public static void Run(string? exeDir) {
            try {
                if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir)) {
                    return;
                }

                QuarantineNestedShadow(exeDir);
                PurgeOldBackups(exeDir);
                WarnAboutLegacyRootBinaries(exeDir);
            } catch (Exception e) {
                Log.Warning(e, "Install layout cleanup failed.");
            }
        }

        static string? GetExeDirectory() {
            try {
                string? path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) {
                    return Path.GetDirectoryName(path);
                }
            } catch { }
            return PathManager.Inst.RootPath;
        }

        static void QuarantineNestedShadow(string exeDir) {
            string nested = Path.Combine(exeDir, NestedFolderName);
            if (!Directory.Exists(nested)) {
                return;
            }
            if (!IsBinaryShadow(nested)) {
                Log.Information("Nested OpenUtau folder present but not a binary shadow; leaving untouched: {Path}", nested);
                return;
            }
            if (IsProtectedUserDataPath(nested)) {
                Log.Warning("Refusing to quarantine nested OpenUtau that matches a data path: {Path}", nested);
                return;
            }

            string bak = Path.Combine(exeDir, BakPrefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            try {
                Directory.Move(nested, bak);
                Log.Warning(
                    "Quarantined leftover nested OpenUtau install to {Bak} (prevents stale OpenUtau.Core from loading).",
                    bak);
            } catch (Exception e) {
                Log.Warning(e, "Failed to quarantine nested OpenUtau at {Path}", nested);
            }
        }

        static bool IsBinaryShadow(string nestedDir) {
            if (!File.Exists(Path.Combine(nestedDir, "OpenUtau.Core.dll"))) {
                return false;
            }
            return File.Exists(Path.Combine(nestedDir, "OpenUtau.exe"))
                || File.Exists(Path.Combine(nestedDir, "OpenUtau-Lunai.exe"))
                || File.Exists(Path.Combine(nestedDir, "OpenUtau.Plugin.Builtin.dll"));
        }

        static bool IsProtectedUserDataPath(string path) {
            var pm = PathManager.Inst;
            return PathsEqual(path, pm.DataPath)
                || PathsEqual(path, pm.LegacyDataPath)
                || PathsEqual(path, pm.SingersPath)
                || PathsEqual(path, pm.CachePath);
        }

        static bool PathsEqual(string a, string? b) {
            if (string.IsNullOrEmpty(b)) {
                return false;
            }
            try {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            } catch {
                return false;
            }
        }

        static void PurgeOldBackups(string exeDir) {
            var cutoff = DateTime.UtcNow.AddDays(-BakRetentionDays);
            foreach (var dir in Directory.EnumerateDirectories(exeDir, BakPrefix + "*")) {
                try {
                    if (Directory.GetLastWriteTimeUtc(dir) > cutoff) {
                        continue;
                    }
                    Directory.Delete(dir, recursive: true);
                    Log.Information("Removed old install-layout backup {Path}", dir);
                } catch (Exception e) {
                    Log.Debug(e, "Could not remove old backup {Path}", dir);
                }
            }
        }

        static void WarnAboutLegacyRootBinaries(string exeDir) {
            bool hasLunai = File.Exists(Path.Combine(exeDir, "OpenUtau-Lunai.exe"))
                || File.Exists(Path.Combine(exeDir, "OpenUtau-Lunai.dll"));
            if (!hasLunai) {
                return;
            }
            if (File.Exists(Path.Combine(exeDir, "OpenUtau.exe"))
                || File.Exists(Path.Combine(exeDir, "OpenUtau.dll"))) {
                Log.Warning(
                    "Legacy OpenUtau.exe/OpenUtau.dll found next to Lunai in {Dir}. Prefer launching OpenUtau-Lunai.exe only.",
                    exeDir);
            }
        }
    }
}
