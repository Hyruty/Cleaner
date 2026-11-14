using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cleaner
{
    /// <summary>
    /// Simple line based logger. One file per run, pruned by age, never throws.
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _file;

        public static string CurrentFile
        {
            get
            {
                lock (Gate)
                {
                    return _file;
                }
            }
        }

        public static void Start()
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_file))
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(AppInfo.LogFolder);
                    _file = Path.Combine(AppInfo.LogFolder, string.Format(CultureInfo.InvariantCulture, "clean-{0:yyyyMMdd-HHmmss}.log", DateTime.Now));
                    Info("==== {0} {1} started (pid {2}) ====", AppInfo.Product, AppInfo.Version, System.Diagnostics.Process.GetCurrentProcess().Id);
                }
                catch
                {
                    _file = null;
                }
            }
        }

        public static void Info(string message, params object[] args)
        {
            Write("INFO", message, args);
        }

        public static void Warn(string message, params object[] args)
        {
            Write("WARN", message, args);
        }

        public static void Error(string message, params object[] args)
        {
            Write("ERROR", message, args);
        }

        public static void Exception(string context, Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            Write("ERROR", "{0}: {1}", context, ex.GetType().Name + ": " + ex.Message);
            try
            {
                Write("ERROR", ex.ToString());
            }
            catch
            {
            }
        }

        public static void Write(string level, string message, params object[] args)
        {
            lock (Gate)
            {
                if (string.IsNullOrEmpty(_file))
                {
                    return;
                }

                try
                {
                    string text = message ?? string.Empty;
                    if (args != null && args.Length > 0)
                    {
                        try
                        {
                            text = string.Format(CultureInfo.InvariantCulture, text, args);
                        }
                        catch (FormatException)
                        {
                        }
                    }

                    string line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}",
                        DateTime.Now,
                        level,
                        text);

                    File.AppendAllText(_file, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }
    }
}
