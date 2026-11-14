using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Cleaner.Setup
{
    /// <summary>The application files travel inside the installer as a zip resource.</summary>
    internal static class Payload
    {
        public static Stream Open()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream(SetupInfo.PayloadResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("The installer payload is missing.");
            }

            return stream;
        }

        public static long Size
        {
            get
            {
                try
                {
                    using (Stream stream = Open())
                    {
                        return stream.Length;
                    }
                }
                catch
                {
                    return 0;
                }
            }
        }

        public static List<string> ListEntries()
        {
            List<string> names = new List<string>();
            using (Stream stream = Open())
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        names.Add(entry.FullName);
                    }
                }
            }

            return names;
        }

        public static bool Verify(out string error)
        {
            error = null;
            try
            {
                List<string> entries = ListEntries();
                if (entries.Count == 0)
                {
                    error = "The payload archive is empty.";
                    return false;
                }

                bool hasApp = false;
                foreach (string entry in entries)
                {
                    if (string.Equals(entry, SetupInfo.ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        hasApp = true;
                    }
                }

                if (!hasApp)
                {
                    error = "The payload does not contain " + SetupInfo.ExeName + ".";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Extracts every payload entry into <paramref name="directory"/>.</summary>
        public static int ExtractTo(string directory, IProgress<InstallProgress> progress)
        {
            Directory.CreateDirectory(directory);
            int count = 0;

            using (Stream stream = Open())
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                int total = archive.Entries.Count;
                int current = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    current++;
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string target = Path.Combine(directory, relative);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    entry.ExtractToFile(target, true);
                    count++;

                    if (progress != null && total > 0)
                    {
                        progress.Report(new InstallProgress
                        {
                            Percent = 10 + (int)(45.0 * current / total),
                            Message = S.T("progress.extracting")
                        });
                    }
                }
            }

            return count;
        }
    }

    internal sealed class InstallProgress
    {
        public int Percent;
        public string Message;
    }
}
