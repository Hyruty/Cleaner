using System;
using System.IO;

namespace Cleaner
{
    internal sealed class DiskStatus
    {
        public bool Available;
        public string Root = "C:\\";
        public string VolumeLabel;
        public long Total;
        public long Free;
        public long Used;
        public int UsedPercent;
    }

    internal static class DiskInfo
    {
        public static DiskStatus SystemDrive()
        {
            DiskStatus status = new DiskStatus();
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root))
                {
                    root = "C:\\";
                }

                DriveInfo drive = new DriveInfo(root);
                status.Root = root;
                status.VolumeLabel = SafeLabel(drive);
                status.Total = drive.TotalSize;
                status.Free = drive.AvailableFreeSpace;
                status.Used = status.Total - status.Free;
                status.UsedPercent = status.Total <= 0
                    ? 0
                    : (int)Math.Round(status.Used * 100.0 / status.Total);
                status.UsedPercent = Math.Max(0, Math.Min(100, status.UsedPercent));
                status.Available = status.Total > 0;
            }
            catch (Exception ex)
            {
                Log.Warn("Reading disk status failed: {0}", ex.Message);
            }

            return status;
        }

        private static string SafeLabel(DriveInfo drive)
        {
            try
            {
                return drive.VolumeLabel;
            }
            catch
            {
                return null;
            }
        }
    }
}
