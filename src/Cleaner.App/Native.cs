using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Cleaner
{
    internal static class Native
    {
        public const int AttachParentProcess = -1;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

        public const uint SHERB_NOCONFIRMATION = 0x1;
        public const uint SHERB_NOPROGRESSUI = 0x2;
        public const uint SHERB_NOSOUND = 0x4;

        public const int STD_OUTPUT_HANDLE = -11;
        public const int STD_ERROR_HANDLE = -12;
        private const uint FILE_TYPE_UNKNOWN = 0;
        private const uint FILE_TYPE_DISK = 1;
        private const uint FILE_TYPE_CHAR = 2;
        private const uint FILE_TYPE_PIPE = 3;

        /// <summary>
        /// Attaches to the parent console (if the app was started from cmd.exe)
        /// so command line modes can print. Returns true when output works.
        /// </summary>
        public static bool TryAttachConsole()
        {
            try
            {
                uint type = GetFileType(GetStdHandle(STD_OUTPUT_HANDLE));
                bool redirected = type == FILE_TYPE_DISK || type == FILE_TYPE_PIPE;

                if (!redirected && type != FILE_TYPE_CHAR)
                {
                    if (!AttachConsole(AttachParentProcess))
                    {
                        uint after = GetFileType(GetStdHandle(STD_OUTPUT_HANDLE));
                        if (after != FILE_TYPE_CHAR)
                        {
                            return false;
                        }
                    }
                }

                BindStreams();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void BindStreams()
        {
            try
            {
                uint type = GetFileType(GetStdHandle(STD_OUTPUT_HANDLE));
                StreamWriter writer = type == FILE_TYPE_CHAR
                    ? OpenConsoleWriter()
                    : new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

                if (writer != null)
                {
                    Console.SetOut(writer);
                }
            }
            catch
            {
            }

            try
            {
                uint type = GetFileType(GetStdHandle(STD_ERROR_HANDLE));
                StreamWriter writer = type == FILE_TYPE_CHAR
                    ? OpenConsoleWriter()
                    : new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };

                if (writer != null)
                {
                    Console.SetError(writer);
                }
            }
            catch
            {
            }
        }

        private static StreamWriter OpenConsoleWriter()
        {
            try
            {
                return new StreamWriter(
                    new System.IO.FileStream("CONOUT$", System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite),
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }
            catch
            {
                return null;
            }
        }

        public static void SetAppUserModelId()
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID(AppInfo.AppUserModelId);
            }
            catch
            {
            }
        }

        /// <summary>Queries the recycle bin totals for the whole machine.</summary>
        public static bool TryGetRecycleBinTotals(out long bytes, out long items)
        {
            bytes = 0;
            items = 0;

            try
            {
                SHQUERYRBINFO info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(null, ref info);
                if (hr != 0)
                {
                    return false;
                }

                bytes = info.i64Size;
                items = info.i64NumItems;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetRecycleBinTotalsForDrive(string driveRoot, out long bytes, out long items)
        {
            bytes = 0;
            items = 0;

            try
            {
                SHQUERYRBINFO info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(driveRoot, ref info);
                if (hr != 0)
                {
                    return false;
                }

                bytes = info.i64Size;
                items = info.i64NumItems;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAdministrator
        {
            get
            {
                try
                {
                    using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    {
                        System.Security.Principal.WindowsPrincipal principal =
                            new System.Security.Principal.WindowsPrincipal(identity);
                        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    internal static class Fmt
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        public static string Bytes(long bytes)
        {
            if (bytes < 0)
            {
                return "—";
            }

            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? string.Format(CultureInfo.CurrentCulture, "{0:N0} {1}", value, Units[unit])
                : string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", value, Units[unit]);
        }

        public static string BytesCompact(long bytes)
        {
            return Bytes(bytes);
        }

        public static string Duration(TimeSpan span)
        {
            if (span.TotalSeconds < 1)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0:N0} ms", span.TotalMilliseconds);
            }

            if (span.TotalMinutes < 1)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0:N1} s", span.TotalSeconds);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0:N0}m {1:N0}s", Math.Floor(span.TotalMinutes), span.Seconds);
        }
    }
}
