using Playnite.SDK;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace AnikiHelper.Services.UI
{
    /// <summary>Resolves the main-view background for the selected game's platform, from Icons/FilterBackground.</summary>
    public sealed class PlatformBackgroundService : IDisposable
    {
        private readonly IPlayniteAPI api;
        private readonly AnikiHelperSettings settings;
        private readonly ILogger logger;
        private readonly Func<string> themePathProvider;

        private DispatcherTimer refreshTimer;
        private string lastPlatformName = string.Empty;
        private string lastCustomFolder = string.Empty;
        private string lastThemePath = string.Empty;
        private string lastResolvedPath = string.Empty;
        private DateTime nextMissingPathProbeUtc = DateTime.MinValue;
        private bool invalidated = true;

        public PlatformBackgroundService(
            IPlayniteAPI api,
            AnikiHelperSettings settings,
            ILogger logger,
            Func<string> themePathProvider)
        {
            this.api = api;
            this.settings = settings;
            this.logger = logger;
            this.themePathProvider = themePathProvider;
        }

        public void Start()
        {
            try
            {
                var dispatcher = api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                if (!dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.Loaded);
                    return;
                }

                if (refreshTimer == null)
                {
                    refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(750)
                    };
                    refreshTimer.Tick += RefreshTimer_Tick;
                }

                invalidated = true;
                RefreshCore();
                refreshTimer.Start();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][PlatformBackground] Failed to start platform background service.");
            }
        }

        public void Stop()
        {
            try
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Stop();
                    refreshTimer.Tick -= RefreshTimer_Tick;
                    refreshTimer = null;
                }
            }
            catch
            {
            }
        }

        public void Invalidate()
        {
            invalidated = true;
            RefreshNow();
        }

        public void RefreshNow()
        {
            try
            {
                var dispatcher = api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                if (!dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(RefreshCore), DispatcherPriority.Background);
                    return;
                }

                RefreshCore();
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][PlatformBackground] Failed to refresh platform background.");
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshCore();
        }

        private void RefreshCore()
        {
            try
            {
                var platformName = GetSelectedGamePlatformName();
                var customFolder = settings?.CustomFilterBackgroundsFolder ?? string.Empty;
                var themePath = themePathProvider?.Invoke() ?? string.Empty;

                var currentPathMissing = !string.IsNullOrWhiteSpace(lastResolvedPath) && !File.Exists(lastResolvedPath);
                var missingPathProbeDue = string.IsNullOrWhiteSpace(lastResolvedPath) &&
                                          DateTime.UtcNow >= nextMissingPathProbeUtc;
                var shouldResolve = invalidated ||
                                    currentPathMissing ||
                                    missingPathProbeDue ||
                                    !string.Equals(lastPlatformName, platformName, StringComparison.Ordinal) ||
                                    !string.Equals(lastCustomFolder, customFolder, StringComparison.OrdinalIgnoreCase) ||
                                    !string.Equals(lastThemePath, themePath, StringComparison.OrdinalIgnoreCase);

                if (!shouldResolve)
                {
                    return;
                }

                var resolvedPath = ResolveBackgroundPath(platformName, customFolder, themePath);

                lastPlatformName = platformName;
                lastCustomFolder = customFolder;
                lastThemePath = themePath;
                lastResolvedPath = resolvedPath;
                nextMissingPathProbeUtc = string.IsNullOrWhiteSpace(resolvedPath)
                    ? DateTime.UtcNow.AddSeconds(5)
                    : DateTime.MaxValue;
                invalidated = false;

                if (settings != null)
                {
                    settings.ActivePlatformBackgroundPath = resolvedPath;
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "[AnikiHelper][PlatformBackground] Failed to resolve active platform background.");
            }
        }

        private string GetSelectedGamePlatformName()
        {
            try
            {
                var game = api?.MainView?.SelectedGames?.FirstOrDefault();
                return game?.Platforms?.FirstOrDefault()?.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveBackgroundPath(string platformName, string customFolder, string themePath)
        {
            var themeFolder = string.IsNullOrWhiteSpace(themePath)
                ? string.Empty
                : Path.Combine(themePath, "Icons", "FilterBackground");

            var legacyThemeFolder = string.IsNullOrWhiteSpace(themePath)
                ? string.Empty
                : Path.Combine(themePath, "Images", "FilterBackgrounds");

            return ThemeBackgroundImageResolver.ResolveBackgroundPath(platformName, customFolder, themeFolder, legacyThemeFolder);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
