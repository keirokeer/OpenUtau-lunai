using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {

    public class PathManager : SingletonBase<PathManager> {
        public const string AppDataFolderName = "OpenUtau-Lunai";
        public const string LegacyDataFolderName = "OpenUtau";

        public PathManager() {
            RootPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (OS.IsMacOS()) {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                DataPath = Path.Combine(userHome, "Library", AppDataFolderName);
                LegacyDataPath = Path.Combine(userHome, "Library", LegacyDataFolderName);
                CachePath = Path.Combine(userHome, "Library", "Caches", AppDataFolderName);
                ShareLegacySingers = true;
                HomePathIsAscii = true;
                try {
                    // Deletes old cache nested under data path.
                    string oldCache = Path.Combine(DataPath, "Cache");
                    if (Directory.Exists(oldCache)) {
                        Directory.Delete(oldCache, true);
                    }
                } catch { }
            } else if (OS.IsLinux()) {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(dataHome)) {
                    dataHome = Path.Combine(userHome, ".local", "share");
                }
                DataPath = Path.Combine(dataHome, AppDataFolderName);
                LegacyDataPath = Path.Combine(dataHome, LegacyDataFolderName);
                string cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cacheHome)) {
                    cacheHome = Path.Combine(userHome, ".cache");
                }
                CachePath = Path.Combine(cacheHome, AppDataFolderName);
                ShareLegacySingers = true;
                HomePathIsAscii = true;
            } else {
                string exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                // NSIS marker and/or Velopack layout (%LocalAppData%\OpenUtau.Lunai\current + Update.exe).
                IsInstalled = IsWindowsInstalledLayout(exePath);
                string dataHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                LegacyDataPath = Path.Combine(dataHome, LegacyDataFolderName);
                if (!IsInstalled) {
                    DataPath = exePath;
                    ShareLegacySingers = false;
                } else {
                    // Never store prefs under Velopack's current\ (replaced on every update).
                    DataPath = Path.Combine(dataHome, AppDataFolderName);
                    ShareLegacySingers = true;
                }
                CachePath = Path.Combine(DataPath, "Cache");
                HomePathIsAscii = true;
                var etor = StringInfo.GetTextElementEnumerator(DataPath);
                while (etor.MoveNext()) {
                    string s = etor.GetTextElement();
                    if (s.Length != 1 || s[0] >= 128) {
                        HomePathIsAscii = false;
                        break;
                    }
                }
            }
        }

        public string RootPath { get; private set; }
        public string DataPath { get; private set; }
        /// <summary>Stock OpenUtau data folder used for shared singers (and one-time prefs migration).</summary>
        public string LegacyDataPath { get; private set; }
        public string CachePath { get; private set; }
        public bool HomePathIsAscii { get; private set; }
        public bool IsInstalled { get; private set; }
        /// <summary>When true, singers load from LegacyDataPath instead of Lunai DataPath.</summary>
        public bool ShareLegacySingers { get; private set; }

        /// <summary>
        /// True for NSIS installs (installed.txt) and Velopack installs (Update.exe next to current\).
        /// </summary>
        public static bool IsWindowsInstalledLayout(string? exePath) {
            if (string.IsNullOrEmpty(exePath)) {
                return false;
            }
            if (File.Exists(Path.Combine(exePath, "installed.txt"))) {
                return true;
            }
            if (File.Exists(Path.Combine(exePath, "Update.exe"))) {
                return true;
            }
            var parent = Directory.GetParent(exePath)?.FullName;
            return parent != null && File.Exists(Path.Combine(parent, "Update.exe"));
        }

        string SingersRoot => ShareLegacySingers ? LegacyDataPath : DataPath;
        public string SingersPathOld => Path.Combine(SingersRoot, "Content", "Singers");
        public string SingersPath => Path.Combine(SingersRoot, "Singers");
        public string AdditionalSingersPath => Preferences.Default.AdditionalSingerPath;
        public string SingersInstallPath => Preferences.Default.InstallToAdditionalSingersPath
            && !string.IsNullOrEmpty(Preferences.Default.AdditionalSingerPath)
                ? AdditionalSingersPath
                : SingersPath;
        public string ResamplersPath => Path.Combine(DataPath, "Resamplers");
        public string WavtoolsPath => Path.Combine(DataPath, "Wavtools");
        public string DependencyPath => Path.Combine(DataPath, "Dependencies");
        public string PluginsPath => Path.Combine(DataPath, "Plugins");
        public string DictionariesPath => Path.Combine(DataPath, "Dictionaries");
        public string TemplatesPath => Path.Combine(DataPath, "Templates");
        public string LogsPath => Path.Combine(DataPath, "Logs");
        public string LogFilePath => Path.Combine(DataPath, "Logs", "log.txt");
        public string PrefsFilePath => Path.Combine(DataPath, "prefs.json");
        public string ThemesPath => Path.Combine(DataPath, "Themes");
        public string TrackColorsPath => Path.Combine(DataPath, "TrackColors");
        public string ExpressionStylesPath => Path.Combine(DataPath, "ExpressionStyles");
        public string NotePresetsFilePath => Path.Combine(DataPath, "notepresets.json");
        public string BackupsPath => Path.Combine(DataPath, "Backups");

        public List<string> SingersPaths {
            get {
                var list = new List<string> { SingersPath };
                if (Directory.Exists(SingersPathOld)) {
                    list.Add(SingersPathOld);
                }
                if (Directory.Exists(AdditionalSingersPath)) {
                    list.Add(AdditionalSingersPath);
                }
                return list.Distinct().ToList();
            }
        }

        /// <summary>
        /// One-time copy of prefs/themes from stock OpenUtau into the Lunai data folder.
        /// Does not move singers. Skips files that already exist in the destination.
        /// </summary>
        public void TryMigrateFromLegacyOpenUtau() {
            try {
                if (string.IsNullOrEmpty(LegacyDataPath) || !Directory.Exists(LegacyDataPath)) {
                    return;
                }
                if (string.Equals(DataPath, LegacyDataPath, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
                if (File.Exists(PrefsFilePath)) {
                    return;
                }
                string legacyPrefs = Path.Combine(LegacyDataPath, "prefs.json");
                if (!File.Exists(legacyPrefs)) {
                    return;
                }

                Directory.CreateDirectory(DataPath);
                CopyFileIfMissing(legacyPrefs, PrefsFilePath);
                CopyFileIfMissing(
                    Path.Combine(LegacyDataPath, "notepresets.json"),
                    NotePresetsFilePath);
                CopyDirectoryContentsIfMissing(
                    Path.Combine(LegacyDataPath, "Themes"),
                    ThemesPath);
                CopyDirectoryContentsIfMissing(
                    Path.Combine(LegacyDataPath, "TrackColors"),
                    TrackColorsPath);
                Log.Information($"Migrated prefs/themes from {LegacyDataPath} to {DataPath}");
            } catch (Exception e) {
                Log.Error(e, "Failed to migrate prefs from legacy OpenUtau data folder.");
            }
        }

        static void CopyFileIfMissing(string source, string dest) {
            if (!File.Exists(source) || File.Exists(dest)) {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest);
        }

        static void CopyDirectoryContentsIfMissing(string sourceDir, string destDir) {
            if (!Directory.Exists(sourceDir)) {
                return;
            }
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(destDir, relative);
                if (File.Exists(dest)) {
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
            }
        }

        Regex invalid = new Regex("[\\x00-\\x1f<>:\"/\\\\|?*]|^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9]|CLOCK\\$)(\\.|$)|[\\.]$", RegexOptions.IgnoreCase);

        public string GetPartSavePath(string exportPath, string partName, int partNo) {
            var dir = Path.GetDirectoryName(exportPath);
            Directory.CreateDirectory(dir);
            var filename = Path.GetFileNameWithoutExtension(exportPath);
            var name = invalid.Replace(partName, "_");
            if (DocManager.Inst.Project.parts.FindAll(p => p is UVoicePart).Count(p => p.DisplayName == partName) > 1) {
                name += $"_{partNo:D2}";
            }
            return Path.Combine(dir, $"{filename}_{name}.ust");
        }

        public string GetExportPath(string exportPath, UTrack track) {
            var dir = Path.GetDirectoryName(exportPath);
            Directory.CreateDirectory(dir);
            var filename = Path.GetFileNameWithoutExtension(exportPath);
            var trackName = invalid.Replace(track.TrackName, "_");
            if (DocManager.Inst.Project.tracks.Count(t => t.TrackName == track.TrackName) > 1) {
                trackName += $"_{track.TrackNo:D2}";
            }
            return Path.Combine(dir, $"{filename}_{trackName}.wav");
        }

        public void ClearCache() {
            var files = Directory.GetFiles(CachePath);
            foreach (var file in files) {
                try {
                    File.Delete(file);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to delete file {file}");
                }
            }
            var dirs = Directory.GetDirectories(CachePath);
            foreach (var dir in dirs) {
                try {
                    Directory.Delete(dir, true);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to delete dir {dir}");
                }
            }
        }

        readonly static string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
        public string GetCacheSize() {
            if (!Directory.Exists(CachePath)) {
                return "0B";
            }
            var dir = new DirectoryInfo(CachePath);
            double size = dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            int order = 0;
            while (size >= 1024 && order < sizes.Length - 1) {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##}{sizes[order]}";
        }
    }
}
