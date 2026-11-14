using System;
using System.Diagnostics;
using System.IO;

namespace Cleaner.Setup
{
    /// <summary>.lnk creation through the shell's WScript.Shell helper.</summary>
    internal static class Shortcuts
    {
        public static bool Create(string linkPath, string target, string workingDirectory, string description, string iconPath)
        {
            try
            {
                string folder = Path.GetDirectoryName(linkPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return false;
                }

                object shell = Activator.CreateInstance(shellType);
                dynamic shellObject = shell;
                dynamic link = shellObject.CreateShortcut(linkPath);
                link.TargetPath = target;
                link.WorkingDirectory = workingDirectory;
                link.Description = description;
                link.IconLocation = (iconPath ?? target) + ",0";
                link.WindowStyle = 1;
                link.Save();

                return File.Exists(linkPath);
            }
            catch (Exception ex)
            {
                Log.Warn("Creating the shortcut '{0}' failed: {1}", linkPath, ex.Message);
                return false;
            }
        }

        public static void Delete(string linkPath)
        {
            try
            {
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Deleting the shortcut '{0}' failed: {1}", linkPath, ex.Message);
            }
        }

        public static void DeleteFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Deleting the folder '{0}' failed: {1}", path, ex.Message);
            }
        }

        public static string DesktopFolder(bool machine)
        {
            return Environment.GetFolderPath(machine
                ? Environment.SpecialFolder.CommonDesktopDirectory
                : Environment.SpecialFolder.DesktopDirectory);
        }

        public static string StartMenuFolder(bool machine)
        {
            return Environment.GetFolderPath(machine
                ? Environment.SpecialFolder.CommonStartMenu
                : Environment.SpecialFolder.Programs);
        }

        public static string ProgramGroupFolder(bool machine)
        {
            return Path.Combine(StartMenuFolder(machine), "Cleaner");
        }

        public static string DesktopLink(bool machine)
        {
            return Path.Combine(DesktopFolder(machine), S.ShortcutName + ".lnk");
        }

        public static string StartMenuLink(bool machine)
        {
            return Path.Combine(ProgramGroupFolder(machine), S.ShortcutName + ".lnk");
        }

        public static string StartMenuUninstallLink(bool machine)
        {
            return Path.Combine(ProgramGroupFolder(machine), S.UninstallShortcutName + ".lnk");
        }
    }

    /// <summary>Very small logger for the installer (kept next to the exe).</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string FilePath
        {
            get
            {
                try
                {
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string folder = Path.Combine(local, "Cleaner", "logs");
                    Directory.CreateDirectory(folder);
                    return Path.Combine(folder, "setup.log");
                }
                catch
                {
                    try
                    {
                        return Path.Combine(Path.GetTempPath(), "cleaner-setup.log");
                    }
                    catch
                    {
                        return null;
                    }
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

        private static void Write(string level, string message, object[] args)
        {
            lock (Gate)
            {
                try
                {
                    string text = message ?? string.Empty;
                    if (args != null && args.Length > 0)
                    {
                        try
                        {
                            text = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
                        }
                        catch (FormatException)
                        {
                        }
                    }

                    string path = FilePath;
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    File.AppendAllText(
                        path,
                        string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}", DateTime.Now, level, text, Environment.NewLine));
                }
                catch
                {
                }
            }
        }
    }
}
