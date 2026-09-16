using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AnikiHelper.Services.UI
{
    /// <summary>
    /// Temporary diagnostic: logs a managed call stack the first few times any code decodes an
    /// image under a theme's "Themes Option" folder, so repeated/uncached decodes of the same
    /// file can be traced back to the exact calling method without an external profiler.
    /// Only logs when AnikiHelper's "Enable Debug Logs" setting is on.
    /// </summary>
    internal static class ImageLoadDiagnostics
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly object sync = new object();
        private static readonly Dictionary<string, int> callCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static void LogIfThemesOptionAccess(string path, string source)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.IndexOf("Themes Option", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            int count;
            lock (sync)
            {
                callCounts.TryGetValue(path, out count);
                count++;
                callCounts[path] = count;
            }

            // Log on the 1st, 5th, 25th and every 100th call after that for this exact path,
            // so we see it happening (repeatedly) without flooding the log.
            if (count != 1 && count != 5 && count != 25 && count % 100 != 0)
            {
                return;
            }

            try
            {
                var stack = new StackTrace(2, true).ToString();
                global::AnikiHelper.AnikiLog.Debug(
                    logger,
                    $"[AnikiHelper][ImageLoadDiag] source={source} count={count} path={path}\n{stack}");
            }
            catch
            {
            }
        }
    }
}
