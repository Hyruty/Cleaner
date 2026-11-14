using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Cleaner
{
    internal sealed class ScanStats
    {
        public long Bytes;
        public long Files;
        public long Folders;
        public int Errors;
    }

    /// <summary>
    /// Measures how much space a cleanup target occupies. Pure read-only work
    /// that runs on a worker thread and honours cancellation.
    /// </summary>
    internal static class FileScanner
    {
        public static ScanStats Measure(CleanTarget target, CancellationToken cancellationToken)
        {
            ScanStats stats = new ScanStats();
            if (target == null || string.IsNullOrEmpty(target.Path))
            {
                return stats;
            }

            switch (target.Kind)
            {
                case TargetKind.File:
                    MeasureFile(target.Path, stats);
                    break;

                case TargetKind.Tree:
                    MeasureTree(target.Path, stats, cancellationToken);
                    break;

                default:
                    MeasureChildren(target.Path, stats, cancellationToken);
                    break;
            }

            return stats;
        }

        private static void MeasureFile(string path, ScanStats stats)
        {
            try
            {
                FileInfo info = new FileInfo(PathSafety.ForIo(path));
                if (info.Exists)
                {
                    stats.Files++;
                    stats.Bytes += info.Length;
                }
            }
            catch
            {
                stats.Errors++;
            }
        }

        private static void MeasureChildren(string folder, ScanStats stats, CancellationToken cancellationToken)
        {
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(PathSafety.ForIo(folder));
            }
            catch
            {
                stats.Errors++;
                return;
            }

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(PathSafety.ForIo(entry)))
                {
                    MeasureTree(entry, stats, cancellationToken);
                }
                else
                {
                    MeasureFile(entry, stats);
                }
            }
        }

        private static void MeasureTree(string root, ScanStats stats, CancellationToken cancellationToken)
        {
            Stack<DirectoryInfo> pending = new Stack<DirectoryInfo>();

            try
            {
                DirectoryInfo info = new DirectoryInfo(PathSafety.ForIo(root));
                if (!info.Exists)
                {
                    return;
                }

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }

                pending.Push(info);
            }
            catch
            {
                stats.Errors++;
                return;
            }

            int steps = 0;
            while (pending.Count > 0)
            {
                if ((++steps & 0x3F) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                DirectoryInfo directory = pending.Pop();

                try
                {
                    foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                    {
                        FileAttributes attributes;
                        try
                        {
                            attributes = entry.Attributes;
                        }
                        catch
                        {
                            stats.Errors++;
                            continue;
                        }

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            stats.Folders++;
                            pending.Push((DirectoryInfo)entry);
                        }
                        else
                        {
                            stats.Files++;
                            try
                            {
                                stats.Bytes += ((FileInfo)entry).Length;
                            }
                            catch
                            {
                                stats.Errors++;
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    stats.Errors++;
                }
                catch (IOException)
                {
                    stats.Errors++;
                }
            }
        }
    }
}
