using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
#if DEBUG
using Avalonia.Diagnostics;
#endif
using OpenUtau.App.Views;
using OpenUtau.Colors;
using Serilog;

namespace OpenUtau.App {
    public class App : Application {
        public override void Initialize() {
            Log.Information("Initializing application.");
            AvaloniaXamlLoader.Load(this);
#if DEBUG
            this.AttachDevTools();
#endif
            InitializeCulture();
            InitializeTheme();
            Log.Information("Initialized application.");
        }

        public override void OnFrameworkInitializationCompleted() {
            Log.Information("Framework initialization completed.");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new SplashWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void InitializeCulture() {
            Log.Information("Initializing culture.");
            string sysLang = CultureInfo.InstalledUICulture.Name;
            string prefLang = Core.Util.Preferences.Default.Language;
            var languages = GetLanguages();
            if (languages.ContainsKey(prefLang)) {
                SetLanguage(prefLang);
            } else if (languages.ContainsKey(sysLang)) {
                SetLanguage(sysLang);
                Core.Util.Preferences.Default.Language = sysLang;
                Core.Util.Preferences.Save();
            } else {
                SetLanguage("en-US");
            }

            // Force using InvariantCulture to prevent issues caused by culture dependent string conversion, especially for floating point numbers.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Log.Information("Initialized culture.");
        }

        public static Dictionary<string, IResourceProvider> GetLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("strings-") &&
                    !key.StartsWith("lunai-strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("strings-", ""), rp);
                }
            }
            return result;
        }

        static Dictionary<string, IResourceProvider> GetLunaiLanguages() {
            if (Current == null) {
                return new();
            }
            var result = new Dictionary<string, IResourceProvider>();
            foreach (string key in Current.Resources.Keys.OfType<string>()) {
                if (key.StartsWith("lunai-strings-") &&
                    Current.Resources.TryGetResource(key, ThemeVariant.Default, out var res) &&
                    res is IResourceProvider rp) {
                    result.Add(key.Replace("lunai-strings-", ""), rp);
                }
            }
            return result;
        }

        public static void SetLanguage(string language) {
            if (Current == null) {
                return;
            }
            var languages = GetLanguages();
            var lunaiLanguages = GetLunaiLanguages();
            foreach (var res in languages.Values.Concat(lunaiLanguages.Values)) {
                Current.Resources.MergedDictionaries.Remove(res);
            }
            // Upstream: en-US fallback, then selected locale.
            if (language != "en-US") {
                Current.Resources.MergedDictionaries.Add(languages["en-US"]);
            }
            if (languages.TryGetValue(language, out var res1)) {
                Current.Resources.MergedDictionaries.Add(res1);
            }
            // Lunai overlay: English Lunai keys, then locale Lunai overrides.
            if (lunaiLanguages.TryGetValue("en-US", out var lunaiEn)) {
                Current.Resources.MergedDictionaries.Add(lunaiEn);
            }
            if (language != "en-US" && lunaiLanguages.TryGetValue(language, out var lunaiLocale)) {
                Current.Resources.MergedDictionaries.Add(lunaiLocale);
            }
        }

        static async void InitializeTheme() {
            Log.Information("Initializing theme.");
            try {
                CustomTheme.ListThemes();
                await OudepLoaderRegistry.LoadAllAsync();
            } catch (Exception e) {
                Log.Error(e, "Failed to load themes from packages.");
            }
            SetTheme();
            Log.Information("Initialized theme.");
        }

        public static void SetTheme() {
            if (Current == null) {
                return;
            }
            var light = (IResourceDictionary) Current.Resources["themes-light"]!;
            var dark = (IResourceDictionary) Current.Resources["themes-dark"]!;
            switch (Core.Util.Preferences.Default.ThemeName) {
                case BuiltInThemeLoader.LightThemeName:
                    ApplyTheme(light);
                    Current.RequestedThemeVariant = ThemeVariant.Light;
                    OpenUtau.Colors.ThemeTemperature.ApplyToCurrentResources(
                        Core.Util.Preferences.Default.ThemeTemperature);
                    OpenUtau.Colors.ThemeTint.ApplyToCurrentResources(
                        Core.Util.Preferences.Default.ThemeTintAmount,
                        Core.Util.Preferences.Default.ThemeTintColor);
                    break;
                case BuiltInThemeLoader.DarkThemeName:
                    ApplyTheme(dark);
                    Current.RequestedThemeVariant = ThemeVariant.Dark;
                    OpenUtau.Colors.ThemeTemperature.ApplyToCurrentResources(
                        Core.Util.Preferences.Default.ThemeTemperature);
                    OpenUtau.Colors.ThemeTint.ApplyToCurrentResources(
                        Core.Util.Preferences.Default.ThemeTintAmount,
                        Core.Util.Preferences.Default.ThemeTintColor);
                    break;
                default:
                    if (BuiltInThemeLoader.TryCreateThemeByName(Core.Util.Preferences.Default.ThemeName, out var builtInTheme)) {
                        ThemeApplicator.Apply(builtInTheme);
                        Current.RequestedThemeVariant = builtInTheme.IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
                    } else {
                        CustomTheme.ApplyTheme(Core.Util.Preferences.Default.ThemeName);
                    }
                    break;
            }
            ThemeManager.LoadTheme();
        }

        private static void ApplyTheme(IResourceDictionary resDict) { 
            var res = Current?.Resources;
            foreach (var item in resDict) {
                res![item.Key] = item.Value;
            }
        }
    }
}
