using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Cleaner
{
    /// <summary>
    /// Static information about the application and its per-user data folders.
    /// </summary>
    internal static class AppInfo
    {
        public const string Product = "Cleaner";
        public const string DisplayName = "C 盘清理助手";
        public const string EnglishName = "Cleaner — C Drive Cleaner";
        public const string Version = "2.0.2";
        public const string Publisher = "Cleaner Project";
        public const string Copyright = "Based on Hyruty/Cleaner, rewritten.";
        public const string TaskName = "Cleaner_AutoClean";
        public const string AppUserModelId = "Cleaner.CDriveCleaner";

        private static string _dataRoot;

        /// <summary>Full path of the running executable.</summary>
        public static string ExePath
        {
            get
            {
                try
                {
                    Assembly asm = Assembly.GetEntryAssembly();
                    if (asm != null && !string.IsNullOrEmpty(asm.Location))
                    {
                        return asm.Location;
                    }
                }
                catch
                {
                }

                try
                {
                    using (Process p = Process.GetCurrentProcess())
                    {
                        return p.MainModule.FileName;
                    }
                }
                catch
                {
                    return Path.Combine(Environment.CurrentDirectory, Product + ".exe");
                }
            }
        }

        public static string ExeFolder
        {
            get { return Path.GetDirectoryName(ExePath); }
        }

        /// <summary>Root folder for settings and logs. Can be overridden with --data-root.</summary>
        public static string DataRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_dataRoot))
                {
                    return _dataRoot;
                }

                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                {
                    local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
                }

                return Path.Combine(local, Product);
            }
            set { _dataRoot = string.IsNullOrEmpty(value) ? null : Path.GetFullPath(value); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(DataRoot, "settings.json"); }
        }

        public static string LogFolder
        {
            get { return Path.Combine(DataRoot, "logs"); }
        }

        public static string VersionText
        {
            get { return "v" + Version; }
        }

        public static string UserAgent
        {
            get { return Product + "/" + Version; }
        }

        /// <summary>Creates the data folders on demand and prunes old log files.</summary>
        public static void EnsureDataFolders(int logRetentionDays)
        {
            try
            {
                Directory.CreateDirectory(DataRoot);
                Directory.CreateDirectory(LogFolder);
            }
            catch
            {
                return;
            }

            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-Math.Max(1, logRetentionDays));
                foreach (string file in Directory.GetFiles(LogFolder, "clean-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }
}
