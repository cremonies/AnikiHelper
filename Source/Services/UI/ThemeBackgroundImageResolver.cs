using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnikiHelper.Services.UI
{
    /// <summary>
    /// Shared file-lookup logic for the theme's named-background folders
    /// (Icons/FilterBackground and its legacy predecessor). Used by both the
    /// active-filter and active-platform background services since they only
    /// differ in which name they look up.
    /// </summary>
    internal static class ThemeBackgroundImageResolver
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" },
            StringComparer.OrdinalIgnoreCase);

        /// <summary>Priority: exact user image -> exact theme image -> legacy theme image -> user Default -> theme Default -> legacy Default.</summary>
        public static string ResolveBackgroundPath(string name, string customFolder, string themeFolder, string legacyThemeFolder)
        {
            var path = FindImage(customFolder, name);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = FindImage(themeFolder, name);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = FindImage(legacyThemeFolder, name);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = FindImage(customFolder, "Default");
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = FindImage(themeFolder, "Default");
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = FindImage(legacyThemeFolder, "Default");
            return path ?? string.Empty;
        }

        public static string FindImage(string folder, string fileNameWithoutExtension)
        {
            if (string.IsNullOrWhiteSpace(folder) ||
                string.IsNullOrWhiteSpace(fileNameWithoutExtension) ||
                !Directory.Exists(folder))
            {
                return string.Empty;
            }

            try
            {
                var match = Directory
                    .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                    .FirstOrDefault(path => string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        fileNameWithoutExtension,
                        StringComparison.OrdinalIgnoreCase));

                return string.IsNullOrWhiteSpace(match)
                    ? string.Empty
                    : Path.GetFullPath(match).Replace("\\", "/");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
