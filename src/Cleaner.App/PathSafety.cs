using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Cleaner
{
    /// <summary>
    /// Guard rails around every path that may be deleted. The catalog only
    /// produces paths below well known cache folders, and every one of them is
    /// re-validated here before a single file is touched.
    /// </summary>
    internal static class PathSafety
    {
        private static string _systemDrive;

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }

            // Drop a possible extended-length prefix again so paths compare and
            // display consistently; ForIo() re-adds it for file system calls.
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                full = full.Substring(4);
                if (full.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    full = @"\\" + full.Substring(4);
                }
            }

            if (full.Length > 3)
            {
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }

        /// <summary>Adds the extended-length prefix so deep paths keep working.</summary>
        public static string ForIo(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return path;
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + path.Substring(2);
            }

            return @"\\?\" + path;
        }

        public static bool IsDriveRoot(string path)
        {
            string normalized = Normalize(path);
            if (normalized == null)
            {
                return false;
            }

            string root = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            return string.Equals(
                normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnderOrEqual(string candidate, string root)
        {
            string c = Normalize(candidate);
            string r = Normalize(root);
            if (c == null || r == null)
            {
                return false;
            }

            if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // A volume root already ends with a separator ("C:\"), so avoid
            // building a doubled one when comparing prefixes.
            string prefix = r.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? r
                : r + Path.DirectorySeparatorChar;

            return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string SystemDriveRoot
        {
            get
            {
                if (_systemDrive == null)
                {
                    string drive = Path.GetPathRoot(Environment.SystemDirectory);
                    _systemDrive = string.IsNullOrEmpty(drive) ? @"C:\" : drive;
                }

                return _systemDrive;
            }
        }

        /// <summary>Paths that must never be deleted, even partially.</summary>
        public static List<string> ProtectedPaths()
        {
            List<string> paths = new List<string>();

            Action<Environment.SpecialFolder> add = folder =>
            {
                try
                {
                    string path = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(Normalize(path));
                    }
                }
                catch
                {
                }
            };

            add(Environment.SpecialFolder.UserProfile);
            add(Environment.SpecialFolder.Desktop);
            add(Environment.SpecialFolder.MyDocuments);
            add(Environment.SpecialFolder.MyPictures);
            add(Environment.SpecialFolder.MyMusic);
            add(Environment.SpecialFolder.MyVideos);
            add(Environment.SpecialFolder.LocalApplicationData);
            add(Environment.SpecialFolder.ApplicationData);
            add(Environment.SpecialFolder.CommonApplicationData);
            add(Environment.SpecialFolder.ProgramFiles);
            add(Environment.SpecialFolder.ProgramFilesX86);
            add(Environment.SpecialFolder.System);
            add(Environment.SpecialFolder.Windows);

            string systemDrive = SystemDriveRoot;
            paths.Add(Normalize(systemDrive));
            if (!string.IsNullOrEmpty(Environment.SystemDirectory))
            {
                paths.Add(Normalize(Environment.SystemDirectory));
            }

            paths.RemoveAll(p => string.IsNullOrEmpty(p));
            return paths;
        }

        public static bool IsProtected(string path)
        {
            string normalized = Normalize(path);
            if (normalized == null)
            {
                return true;
            }

            if (IsDriveRoot(normalized))
            {
                return true;
            }

            foreach (string guarded in ProtectedPaths())
            {
                if (string.Equals(normalized, guarded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Throws when <paramref name="path"/> is not a deletion candidate that
        /// the catalog is allowed to produce.
        /// </summary>
        public static string EnsureSafeTarget(string path, string allowedRoot)
        {
            string normalized = Normalize(path);
            if (normalized == null)
            {
                throw new InvalidOperationException("Empty or invalid cleanup path.");
            }

            if (IsProtected(normalized))
            {
                throw new InvalidOperationException("Refusing to clean a protected path: " + normalized);
            }

            string root = Normalize(allowedRoot);
            if (!string.IsNullOrEmpty(root) && !IsUnderOrEqual(normalized, root))
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture,
                        "Path '{0}' is outside the expected root '{1}'.", normalized, root));
            }

            // Never allow the root of a volume, of a user profile or of Windows
            // to be reached through a parent-directory hop.
            if (normalized.IndexOf(@"..", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("Refusing relative path traversal: " + normalized);
            }

            if (normalized.Length > 240 && !normalized.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return normalized;
            }

            return normalized;
        }

        public static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(ForIo(path));
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        public static bool DirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                return Directory.Exists(ForIo(path));
            }
            catch
            {
                return false;
            }
        }

        public static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                return File.Exists(ForIo(path));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Removes ReadOnly/Hidden/System so a delete can succeed.</summary>
        public static void ClearBlockingAttributes(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(ForIo(path));
                FileAttributes wanted = attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
                if (wanted != attributes)
                {
                    File.SetAttributes(ForIo(path), wanted);
                }
            }
            catch
            {
            }
        }
    }
}
