using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AnikiHelper.Services.UI
{
    // A real, separate, topmost WPF window shown while ANY game is running in
    // Fullscreen mode — native/installed games, emulators, everything Playnite
    // itself launches (via OnGameStarted's StartedProcessId), not just NAS Connector
    // games. Modeled directly on RetroBat/EmulationStation: the frontend gets out of
    // the way entirely while a game runs (Playnite's own window is minimized the
    // moment this is created) instead of staying interactive underneath it.
    //
    // Deliberately a separate top-level window rather than a themed in-app layer —
    // testing the existing "NowPlaying" plugin's own in-theme overlay (which sets
    // WPF's KeyboardNavigation to "Contained") showed that approach does NOT reliably
    // stop a controller from still navigating Playnite's game list underneath it. A
    // genuinely separate, activated window doesn't have that problem: a controller's
    // button-to-keypress translation targets whichever window Windows considers the
    // real foreground one, not whatever a single window's WPF visual tree considers
    // "contained."
    //
    // The window itself is built and shown once, immediately, and stays alive
    // (non-topmost, unfocused) for the entire session rather than only being built
    // reactively when Playnite regains focus — see the constructor and
    // PromoteToForeground. That's what actually closes the launch/closing transition
    // gap: with Playnite minimized and nothing else in place, the instant the game's
    // own window is destroyed on exit, Windows would otherwise have nothing to reveal
    // except the real desktop for however long it then takes OnGameStopped to run and
    // react — long enough to see. A window that's already there and already painted
    // gets promoted to the front with zero extra latency; nothing shown reactively
    // afterwards can undo a frame the OS already presented.
    internal sealed class NativePlaySessionOverlay : IDisposable
    {
        private readonly System.Diagnostics.Process process;
        private readonly string gameName;
        private readonly Action stopRequested;
        private readonly Window playniteWindow;
        private readonly DateTime startedUtc = DateTime.UtcNow;

        private Window overlayWindow;
        private System.Windows.Threading.DispatcherTimer sessionTimer;
        private TextBlock sessionLengthText;
        private bool disposed;

        public NativePlaySessionOverlay(System.Diagnostics.Process process, string gameName, Action stopRequested)
        {
            this.process = process;
            this.gameName = gameName;
            this.stopRequested = stopRequested;
            playniteWindow = Application.Current?.MainWindow;

            if (playniteWindow != null)
            {
                playniteWindow.Activated += OnPlayniteWindowActivated;
                playniteWindow.WindowState = WindowState.Minimized;
            }

            // ShowActivated=false so building this doesn't itself steal focus from
            // Playnite/the not-yet-appeared game.
            overlayWindow = BuildWindow();
            overlayWindow.ShowActivated = false;
            overlayWindow.Show();

            sessionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            sessionTimer.Tick += (s, e) => UpdateSessionLengthText();
            sessionTimer.Start();
            UpdateSessionLengthText();
        }

        private void OnPlayniteWindowActivated(object sender, EventArgs e)
        {
            // Playnite regained the foreground while the game is still running —
            // most likely a task switch (Alt+Tab, taskbar, the Windows/Xbox Guide
            // button). Show this screen instead of leaving Playnite's normal,
            // fully-navigable UI reachable underneath.
            PromoteToForeground();
        }

        // Brings the already-existing curtain window to the front — used both when
        // Playnite is reactivated mid-session (above) and when the game exits (see
        // Dispose), so the restore happens hidden underneath the same cover either
        // way. Never rebuilds the window; it's alive for the whole session.
        private void PromoteToForeground()
        {
            if (disposed || overlayWindow == null)
                return;

            overlayWindow.Topmost = true;
            overlayWindow.Activate();

            // Nothing should be reachable behind this screen — send Playnite back
            // down immediately in case it briefly flashed up before this took effect.
            if (playniteWindow != null)
                playniteWindow.WindowState = WindowState.Minimized;
        }

        private void UpdateSessionLengthText()
        {
            if (sessionLengthText == null)
                return;

            var elapsed = DateTime.UtcNow - startedUtc;
            sessionLengthText.Text = elapsed.Hours > 0
                ? $"{elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s"
                : $"{elapsed.Minutes}m {elapsed.Seconds}s";
        }

        private Window BuildWindow()
        {
            var returnButton = new Button
            {
                Content = "Return to Game",
                Width = 320,
                Height = 64,
                FontSize = 20,
                Margin = new Thickness(10, 0, 10, 0)
            };
            returnButton.Click += (s, e) => ReturnToGame();

            var stopButton = new Button
            {
                Content = "Stop Game",
                Width = 320,
                Height = 64,
                FontSize = 20,
                Margin = new Thickness(10, 0, 10, 0),
                Foreground = Brushes.OrangeRed
            };
            stopButton.Click += (s, e) =>
            {
                // stopRequested shows its own confirmation dialog — a Topmost window
                // like this one would otherwise render on top of it, making that
                // dialog impossible to see or click.
                if (overlayWindow != null)
                    overlayWindow.Topmost = false;

                stopRequested?.Invoke();

                if (overlayWindow != null)
                    overlayWindow.Topmost = true;
            };

            sessionLengthText = new TextBlock
            {
                FontSize = 22,
                Foreground = Brushes.Gainsboro,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 40)
            };

            var titleText = new TextBlock
            {
                Text = gameName,
                FontSize = 48,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(40, 0, 40, 12)
            };

            var subtitleText = new TextBlock
            {
                Text = "NOW PLAYING",
                FontSize = 20,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 24)
            };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttonRow.Children.Add(returnButton);
            buttonRow.Children.Add(stopButton);

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(subtitleText);
            stack.Children.Add(titleText);
            stack.Children.Add(sessionLengthText);
            stack.Children.Add(buttonRow);

            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(12, 12, 16)) };
            root.Children.Add(stack);

            var window = new Window
            {
                Content = root,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowState = WindowState.Maximized,
                // Starts NOT topmost: this window is alive for the whole session (see
                // the constructor) sitting passively behind the game's own window, and
                // Topmost=true from the start would put it in front of the game before
                // it even launches. PromoteToForeground (Alt+Tab back to Playnite, or
                // the game exiting) is what raises it when it actually needs to block.
                Topmost = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            window.Loaded += (s, e) => Keyboard.Focus(returnButton);
            window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    ReturnToGame();
            };

            return window;
        }

        // Demotes rather than closes: the window stays alive, passively behind the
        // game again, ready to be promoted the next time it's actually needed (another
        // Alt+Tab back, or the game finally exiting).
        public void ReturnToGame()
        {
            BringProcessToForeground(process);
            if (overlayWindow != null)
                overlayWindow.Topmost = false;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            if (playniteWindow != null)
                playniteWindow.Activated -= OnPlayniteWindowActivated;

            sessionTimer?.Stop();
            sessionTimer = null;

            // Bring the curtain to the front FIRST, so the Playnite restore below
            // happens hidden underneath it, then only close the curtain once Playnite
            // is already normal and active underneath — never the other way around,
            // or the moment between restoring Playnite and closing this window would
            // itself be a visible flash of exactly the kind this class exists to avoid.
            if (overlayWindow != null)
            {
                overlayWindow.Topmost = true;
                overlayWindow.Activate();
            }

            if (playniteWindow != null)
            {
                playniteWindow.WindowState = WindowState.Normal;
                playniteWindow.Activate();
            }

            // Only now — Playnite is already restored and active underneath — does
            // taking the curtain away actually reveal it instead of the desktop.
            overlayWindow?.Close();
            overlayWindow = null;
        }

        private static void BringProcessToForeground(System.Diagnostics.Process process)
        {
            try
            {
                if (process.HasExited)
                    return;

                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                    return;

                if (NativeMethods.IsIconic(handle))
                    NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(handle);
            }
            catch
            {
                // Best effort — nothing useful to do if the process already exited or
                // never had its own window.
            }
        }

        private static class NativeMethods
        {
            public const int SW_RESTORE = 9;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool IsIconic(IntPtr hWnd);
        }
    }
}
