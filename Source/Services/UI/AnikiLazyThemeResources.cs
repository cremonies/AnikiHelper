using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace AnikiHelper.Services
{
    /// <summary>
    /// Loads the theme's per-overlay ResourceDictionary files (Steam Store, Friends, Achievements,
    /// Video Player, etc.) on first use instead of Main.xaml merging all of them eagerly at startup.
    /// Main.xaml previously merged ~30 of these files (several hundred KB each) before the Fullscreen
    /// window could render at all; this defers each one until its window is actually opened.
    /// </summary>
    internal static class AnikiLazyThemeResources
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static readonly Dictionary<string, string> KeyToThemeFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScreenShotsThumbsWindowStyle"] = @"Views\AdditionalViews\ScreenshotView.xaml",
            ["FriendsStyleProfil"] = @"Views\AdditionalViews\FriendProfil.xaml",
            ["FriendsStyle"] = @"Views\AdditionalViews\FriendView.xaml",
            ["SteamStoreStyle"] = @"Views\AdditionalViews\SteamStore.xaml",
            ["AchievementsWindow"] = @"Views\AdditionalViews\PlayniteAchievementMainView.xaml",
            ["AchievementsOptionsWindow"] = @"Views\AdditionalViews\PlayniteAchievementMainView.xaml",
            ["PlayerProfileWindowStyle"] = @"Views\AdditionalViews\ProfilView.xaml",
            ["ControlCenterWindowStyle"] = @"Views\AdditionalViews\QuickOptionsView.xaml",
            ["TopBarManagerWindowStyle"] = @"Views\AdditionalViews\TopBarManagerView.xaml",
            ["LastUpdatesWindowStyle"] = @"Views\AdditionalViews\NewsView.xaml",
            ["GameNewsWindowStyle"] = @"Views\AdditionalViews\GameNewsView.xaml",
            ["QuickAccessWindowStyle"] = @"Views\AdditionalViews\QuickAccessMenu.xaml",
            ["NotificationMenuWindowStyle"] = @"Views\AdditionalViews\MenuNotification.xaml",
            ["HelpWindowStyle"] = @"Views\AdditionalViews\HelpMenu.xaml",
            ["WhatsNewWindowStyle"] = @"Views\AdditionalViews\WhatsNewView.xaml",
            ["FirstSetupWindowStyle"] = @"Views\AdditionalViews\FirstSetupView.xaml",
            ["QuickAccessNotificationWindowStyle"] = @"Views\AdditionalViews\QuickAccessNotification.xaml",
            ["SteamStatusMenuWindowStyle"] = @"Views\AdditionalViews\QuickAccessSteamStatut.xaml",
            ["MediaGalleryGamesWindowStyle"] = @"Views\AdditionalViews\ScreenshotGlobalView.xaml",
            ["AudioSwitcherWindowStyle"] = @"Views\AdditionalViews\AudioSwitcherView.xaml",
            ["FriendsActivityWindow"] = @"Views\AdditionalViews\FriendsActivityView.xaml",
            ["FriendsActivityOptionsWindow"] = @"Views\AdditionalViews\FriendsActivityView.xaml",
            ["FriendGameAchievementsWindow"] = @"Views\AdditionalViews\FriendsActivityView.xaml",
            ["FriendGameAchievementsOptionsWindow"] = @"Views\AdditionalViews\FriendsActivityView.xaml",
            ["MusicPlayerWindowStyle"] = @"Views\AdditionalViews\MusicPlayerView.xaml",
            ["VideoPlayerWindowStyle"] = @"Views\AdditionalViews\VideoPlayerView.xaml",
            ["UniPlaySongWindowStyle"] = @"Views\AdditionalViews\UniPlaySongView.xaml",
            ["FriendsWindowStyle"] = @"Views\AdditionalViews\FriendsViewOverlay.xaml",
            ["LastCapturesWindowStyle"] = @"Views\AdditionalViews\LastCapturesViewOverlay.xaml",
            ["AppsWindowStyle"] = @"Views\AdditionalViews\AppsViewOverlay.xaml",
            ["AchievementsWindowStyle"] = @"Views\AdditionalViews\AchievementsViewOverlay.xaml",
            ["ControllerManagerWindowStyle"] = @"Views\AdditionalViews\ControllerManagerView.xaml",
            ["ControllerManagerWindowStyleLeft"] = @"Views\AdditionalViews\ControllerManagerView.xaml",
            ["GamepadTesterWindowStyle"] = @"Views\AdditionalViews\GamepadTesterView.xaml",
        };

        private static readonly HashSet<string> loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object sync = new object();
        private static string cachedThemePath;

        /// <summary>
        /// Call this immediately before any Application.Current.TryFindResource(styleKey) /
        /// FindThemeResource(styleKey) lookup that opens one of the theme's overlay windows.
        /// No-ops instantly once the file has been loaded once, and no-ops entirely for any
        /// key this table doesn't manage (e.g. resources Main.xaml still merges eagerly).
        /// </summary>
        public static void EnsureStyleLoaded(IPlayniteAPI playniteApi, string styleKeyOrParameter)
        {
            if (playniteApi == null || Application.Current == null || string.IsNullOrWhiteSpace(styleKeyOrParameter))
            {
                return;
            }

            var pipeIndex = styleKeyOrParameter.IndexOf('|');
            var styleKey = (pipeIndex >= 0 ? styleKeyOrParameter.Substring(0, pipeIndex) : styleKeyOrParameter).Trim();

            if (string.IsNullOrEmpty(styleKey) || !KeyToThemeFile.TryGetValue(styleKey, out var relativePath))
            {
                return;
            }

            if (loadedFiles.Contains(relativePath))
            {
                return;
            }

            try
            {
                if (Application.Current.TryFindResource(styleKey) != null)
                {
                    // Already available (e.g. Main.xaml still merges it eagerly). Nothing to load.
                    loadedFiles.Add(relativePath);
                    return;
                }
            }
            catch
            {
            }

            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => LoadFile(playniteApi, relativePath));
                return;
            }

            LoadFile(playniteApi, relativePath);
        }

        private static void LoadFile(IPlayniteAPI playniteApi, string relativePath)
        {
            lock (sync)
            {
                if (loadedFiles.Contains(relativePath))
                {
                    return;
                }

                try
                {
                    var themePath = GetCurrentThemePath(playniteApi);
                    if (string.IsNullOrWhiteSpace(themePath))
                    {
                        return;
                    }

                    var filePath = Path.Combine(themePath, relativePath);
                    if (!File.Exists(filePath))
                    {
                        logger.Warn($"[AnikiHelper][LazyTheme] Theme file not found for lazy load: {filePath}");
                        return;
                    }

                    var fileUri = new Uri(filePath, UriKind.Absolute);

                    using (var stream = File.OpenRead(filePath))
                    {
                        var parserContext = new ParserContext { BaseUri = fileUri };
                        var dictionary = (ResourceDictionary)XamlReader.Load(stream, parserContext);
                        dictionary.Source = fileUri;
                        Application.Current.Resources.MergedDictionaries.Add(dictionary);
                    }

                    loadedFiles.Add(relativePath);
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"[AnikiHelper][LazyTheme] Failed to lazily load theme resource file: {relativePath}");
                }
            }
        }

        private static string GetCurrentThemePath(IPlayniteAPI playniteApi)
        {
            if (cachedThemePath != null)
            {
                return cachedThemePath;
            }

            try
            {
                var themeId = playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? playniteApi.ApplicationSettings.FullscreenTheme
                    : playniteApi.ApplicationSettings.DesktopTheme;

                if (string.IsNullOrWhiteSpace(themeId))
                {
                    return null;
                }

                var roots = new List<string>();

                if (!playniteApi.ApplicationInfo.IsPortable)
                {
                    roots.Add(playniteApi.Paths.ConfigurationPath);
                }

                roots.Add(playniteApi.Paths.ApplicationPath);

                var modeFolder = playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                    ? "Fullscreen"
                    : "Desktop";

                foreach (var root in roots)
                {
                    var themesFolder = Path.Combine(root, "Themes", modeFolder);

                    if (!Directory.Exists(themesFolder))
                    {
                        continue;
                    }

                    foreach (var themeDir in Directory.EnumerateDirectories(themesFolder))
                    {
                        var themeFile = Path.Combine(themeDir, "theme.yaml");

                        if (!File.Exists(themeFile))
                        {
                            continue;
                        }

                        try
                        {
                            var data = Serialization.FromYamlFile<Dictionary<string, object>>(themeFile);

                            if (data != null &&
                                data.TryGetValue("Id", out var idValue) &&
                                string.Equals(idValue?.ToString(), themeId, StringComparison.OrdinalIgnoreCase))
                            {
                                cachedThemePath = themeDir;
                                return themeDir;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "[AnikiHelper][LazyTheme] Failed to detect current theme path.");
            }

            return null;
        }
    }
}
