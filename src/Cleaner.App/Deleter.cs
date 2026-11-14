using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Cleaner
{
    internal sealed class DeleteStats
    {
        public long Bytes;
        public long Files;
        public long Folders;
        public long Skipped;
        public long Errors;
        public readonly List<string> Samples = new List<string>();
        public readonly List<string> TouchedFolders = new List<string>();

        public void Skip(string path, string reason)
        {
            Skipped++;
            if (Samples.Count < 200)
            {
                Samples.Add(path + " :: " + reason);
            }
        }

        public void Fail(string path, string reason)
        {
            Errors++;
            if (Samples.Count < 200)
            {
                Samples.Add(path + " :: " + reason);
            }
        }
    }

    /// <summary>
    /// Deletes cleanup targets with a conservative strategy: reparse points are
    /// never followed, protected roots are refused, read-only attributes are
    /// cleared, and locked files are retried once before being reported.
    /// </summary>
    internal static class Deleter
    {
        public static void Delete(CleanTarget target, DeleteStats stats, CancellationToken cancellationToken)
        {
            if (target == null || stats == null)
            {
                return;
            }

            string path = PathSafety.EnsureSafeTarget(target.Path, target.Root);
            cancellationToken.ThrowIfCancellationRequested();

            switch (target.Kind)
            {
                case TargetKind.File:
                    DeleteFile(path, stats);
                    break;

                case TargetKind.Tree:
                    DeleteTree(path, stats, cancellationToken, 0);
                    break;

                default:
                    DeleteChildren(path, stats, cancellationToken);
                    break;
            }
        }

        public static void DeleteChildren(string folder, DeleteStats stats, CancellationToken cancellationToken)
        {
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(PathSafety.ForIo(folder));
            }
            catch (Exception ex)
            {
                stats.Fail(folder, "enumerate: " + ex.Message);
                return;
            }

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteTree(entry, stats, cancellationToken, 1);
            }

            stats.TouchedFolders.Add(folder);
        }

        public static void DeleteTree(string path, DeleteStats stats, CancellationToken cancellationToken, int depth)
        {
            if (depth > 64)
            {
                stats.Skip(path, "nesting-too-deep");
                return;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(PathSafety.ForIo(path));
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex)
            {
                stats.Skip(path, "attributes: " + ex.Message);
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                stats.Skip(path, "reparse-point");
                return;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                DeleteFile(path, stats);
                return;
            }

            string[] children;
            try
            {
                children = Directory.GetFileSystemEntries(PathSafety.ForIo(path));
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex)
            {
                stats.Skip(path, "enumerate: " + ex.Message);
                return;
            }

            foreach (string child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteTree(child, stats, cancellationToken, depth + 1);
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Directory.Delete(PathSafety.ForIo(path), false);
                    stats.Folders++;
                    stats.TouchedFolders.Add(path);
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                    {
                        Thread.Sleep(120);
                        PathSafety.ClearBlockingAttributes(path);
                        continue;
                    }

                    stats.Skip(path, "delete-folder: " + ex.Message);
                }
            }
        }

        public static void DeleteFile(string path, DeleteStats stats)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                long size = 0;
                try
                {
                    size = new FileInfo(PathSafety.ForIo(path)).Length;
                }
                catch
                {
                }

                try
                {
                    if (attempt == 0)
                    {
                        PathSafety.ClearBlockingAttributes(path);
                    }

                    File.Delete(PathSafety.ForIo(path));
                    stats.Files++;
                    stats.Bytes += size;
                    return;
                }
                catch (FileNotFoundException)
                {
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                    {
                        Thread.Sleep(120);
                        continue;
                    }

                    stats.Skip(path, "delete-file: " + ex.Message);
                }
            }
        }
    }
}
