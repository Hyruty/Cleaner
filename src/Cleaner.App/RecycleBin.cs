using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Cleaner
{
    internal sealed class RecycleBinStatus
    {
        public bool Known;
        public long Items = -1;
        public long Bytes;
    }

    internal sealed class RecycleBinResult
    {
        public bool Cleared;
        public bool Verified;
        public long Before = -1;
        public long After = -1;
        public string Method = "none";
        public string Error;
    }

    /// <summary>
    /// Recycle bin access through the shell API, with a filesystem fallback and
    /// a verification pass so the UI never claims success it did not achieve.
    /// </summary>
    internal static class RecycleBin
    {
        public static RecycleBinStatus Query()
        {
            RecycleBinStatus status = new RecycleBinStatus();
            long bytes;
            long items;
            if (Native.TryGetRecycleBinTotals(out bytes, out items))
            {
                status.Known = true;
                status.Items = items;
                status.Bytes = bytes;
            }

            return status;
        }

        public static RecycleBinResult Empty(TimeSpan timeout, UserContext user, bool isAdmin)
        {
            RecycleBinResult result = new RecycleBinResult();
            RecycleBinStatus before = Query();
            result.Before = before.Known ? before.Items : -1;

            int hr = Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
            bool shellApiOk = hr == 0 || hr == 1;
            result.Method = shellApiOk ? "ShellApi" : "ShellApi-0x" + hr.ToString("X8");

            if (!shellApiOk)
            {
                result.Error = "SHEmptyRecycleBin failed with 0x" + hr.ToString("X8") + ".";
                Log.Warn("Recycle bin shell API failed: 0x{0:X8}", hr);
            }

            RecycleBinStatus after = WaitForEmpty(timeout);
            if (!after.Known || after.Items != 0)
            {
                // Fall back to removing the per-user $Recycle.Bin entries directly;
                // this also covers cases where the shell call silently did nothing.
                string sid = user == null ? null : user.Sid;
                int removed = DeleteRecycleBinEntries(sid, isAdmin);
                if (removed >= 0)
                {
                    result.Method = result.Method + "+Filesystem";
                }

                after = WaitForEmpty(TimeSpan.FromSeconds(2));
            }

            result.After = after.Known ? after.Items : -1;
            result.Verified = after.Known && after.Items == 0;
            result.Cleared = result.Verified || (shellApiOk && !after.Known);

            if (!result.Cleared && string.IsNullOrEmpty(result.Error))
            {
                result.Error = result.After >= 0
                    ? "Recycle bin still reports " + result.After + " item(s) after cleanup."
                    : "Recycle bin item count could not be verified.";
            }

            Log.Info("Recycle bin cleanup: before={0}; after={1}; verified={2}; method={3}; error={4}",
                result.Before, result.After, result.Verified, result.Method, result.Error ?? string.Empty);

            return result;
        }

        private static RecycleBinStatus WaitForEmpty(TimeSpan timeout)
        {
            DateTime deadline = DateTime.Now + timeout;
            RecycleBinStatus status = Query();

            while (!status.Known || status.Items != 0)
            {
                if (DateTime.Now >= deadline)
                {
                    break;
                }

                Thread.Sleep(200);
                status = Query();
            }

            return status;
        }

        /// <summary>
        /// Deletes $Recycle.Bin entries for the given SID on every ready drive.
        /// Returns the number of entries removed, or -1 when nothing was touched.
        /// </summary>
        public static int DeleteRecycleBinEntries(string sid, bool isAdmin)
        {
            if (string.IsNullOrEmpty(sid))
            {
                return -1;
            }

            int removed = 0;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    {
                        continue;
                    }

                    string folder = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                    if (!Directory.Exists(PathSafety.ForIo(folder)))
                    {
                        continue;
                    }

                    DeleteStats stats = new DeleteStats();
                    foreach (string entry in Directory.GetFileSystemEntries(PathSafety.ForIo(folder)))
                    {
                        string name = Path.GetFileName(entry);
                        try
                        {
                            if (name.StartsWith("$R", StringComparison.OrdinalIgnoreCase) && Directory.Exists(PathSafety.ForIo(entry)))
                            {
                                Deleter.DeleteTree(entry, stats, CancellationToken.None, 1);
                            }
                            else
                            {
                                Deleter.DeleteFile(entry, stats);
                            }
                        }
                        catch (Exception ex)
                        {
                            stats.Fail(entry, ex.Message);
                        }
                    }

                    removed += (int)(stats.Files + stats.Folders);
                    if (stats.Skipped > 0)
                    {
                        Log.Warn("Recycle bin fallback skipped {0} item(s) in {1}", stats.Skipped, folder);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Recycle bin fallback failed for a drive: {0}", ex.Message);
                }
            }

            return removed;
        }
    }
}
