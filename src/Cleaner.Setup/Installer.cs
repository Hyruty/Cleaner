using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace Cleaner.Setup
{
    internal sealed class InstallRequest
    {
        public string InstallDir;
        public bool MachineScope;
        public bool DesktopShortcut = true;
        public bool StartMenuShortcut = true;
        public bool LaunchAfter;
        public bool DeleteUserData;
    }

    internal sealed class InstallState
    {
        public bool Installed;
        public string InstallDir;
        public string Version;
        public bool MachineScope;
    }

    /// <summary>
    /// Copying files, shortcuts, registry entries and the uninstall hand-off.
    /// Everything here is synchronous and reports progress for the UI thread.
    /// </summary>
    internal static class Installer
    {
        public static bool IsAdmin
        {
            get
            {
                try
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string DefaultInstallDir(bool machineScope)
        {
            return machineScope ? SetupInfo.ProgramFilesCleaner : SetupInfo.UserInstallDir;
        }

        public static InstallState Query()
        {
            InstallState state = QueryFromHive(Registry.CurrentUser);
            if (state.Installed)
            {
                return state;
            }

            return QueryFromHive(Registry.LocalMachine);
        }

        private static InstallState QueryFromHive(RegistryKey hive)
        {
            InstallState state = new InstallState();
            try
            {
                using (RegistryKey key = hive.OpenSubKey(SetupInfo.AppKeyPath))
                {
                    if (key != null)
                    {
                        state.InstallDir = key.GetValue("InstallDir") as string;
                        state.Version = key.GetValue("Version") as string;
                        object machine = key.GetValue("Machine");
                        state.MachineScope = machine != null && Convert.ToInt32(machine, CultureInfo.InvariantCulture) == 1;
                        state.Installed = !string.IsNullOrEmpty(state.InstallDir) && Directory.Exists(state.InstallDir);
                        return state;
                    }
                }

                using (RegistryKey key = hive.OpenSubKey(SetupInfo.UninstallKeyPath))
                {
                    if (key != null)
                    {
                        state.InstallDir = key.GetValue("InstallLocation") as string;
                        state.Version = key.GetValue("DisplayVersion") as string;
                        state.MachineScope = ReferenceEquals(hive, Registry.LocalMachine);
                        state.Installed = !string.IsNullOrEmpty(state.InstallDir);
                        return state;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Reading the install state failed: {0}", ex.Message);
            }

            return state;
        }

        // -------------------------------------------------------------- install
        public static bool Install(InstallRequest request, IProgress<InstallProgress> progress, out string error)
        {
            error = null;

            try
            {
                string directory = string.IsNullOrWhiteSpace(request.InstallDir)
                    ? DefaultInstallDir(request.MachineScope)
                    : Path.GetFullPath(request.InstallDir.Trim().Trim('"'));

                if (!Path.IsPathRooted(directory))
                {
                    error = S.T("error.dir");
                    return false;
                }

                request.InstallDir = directory;

                Report(progress, 3, S.T("progress.preparing"));
                Directory.CreateDirectory(directory);

                Report(progress, 6, S.T("progress.stopping"));
                string stopError;
                if (!StopRunningApplication(out stopError))
                {
                    Log.Warn("Stopping the running application failed: {0}", stopError);
                }

                Report(progress, 10, S.T("progress.extracting"));
                Payload.ExtractTo(directory, progress);

                Report(progress, 58, S.T("progress.uninstaller"));
                string uninstaller = Path.Combine(directory, SetupInfo.UninstallerName);
                if (!string.Equals(Path.GetFullPath(SetupInfo.ExePath), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(SetupInfo.ExePath, uninstaller, true);
                }

                string appExe = Path.Combine(directory, SetupInfo.ExeName);
                if (!File.Exists(appExe))
                {
                    error = "The application executable was not installed.";
                    return false;
                }

                Report(progress, 66, S.T("progress.shortcuts"));
                CreateShortcuts(request, appExe, uninstaller, directory);

                Report(progress, 82, S.T("progress.registry"));
                WriteRegistry(request, appExe, uninstaller, directory);

                Report(progress, 96, S.T("progress.done"));
                Report(progress, 100, S.T("progress.done"));
                Log.Info("Installed {0} {1} to {2} (machine={3})", SetupInfo.Product, SetupInfo.Version, directory, request.MachineScope);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                Log.Error("Install failed with an access error: {0}", ex);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Install failed: {0}", ex);
                return false;
            }
        }

        private static void CreateShortcuts(InstallRequest request, string appExe, string uninstaller, string directory)
        {
            if (request.DesktopShortcut)
            {
                Shortcuts.Create(
                    Shortcuts.DesktopLink(request.MachineScope),
                    appExe,
                    directory,
                    S.ShortcutName,
                    appExe);
            }

            if (request.StartMenuShortcut)
            {
                Shortcuts.Create(
                    Shortcuts.StartMenuLink(request.MachineScope),
                    appExe,
                    directory,
                    S.ShortcutName,
                    appExe);

                Shortcuts.Create(
                    Shortcuts.StartMenuUninstallLink(request.MachineScope),
                    uninstaller,
                    directory,
                    S.UninstallShortcutName,
                    appExe);
            }
        }

        private static void WriteRegistry(InstallRequest request, string appExe, string uninstaller, string directory)
        {
            RegistryKey root = request.MachineScope ? Registry.LocalMachine : Registry.CurrentUser;
            long sizeKb = Math.Max(1, DirectorySize(directory) / 1024);

            using (RegistryKey key = root.CreateSubKey(SetupInfo.UninstallKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Could not create the uninstall registry key.");
                }

                key.SetValue("DisplayName", S.IsChinese ? "C 盘清理助手 (Cleaner)" : "Cleaner — C Drive Cleaner");
                key.SetValue("DisplayVersion", SetupInfo.Version);
                key.SetValue("DisplayIcon", appExe + ",0");
                key.SetValue("Publisher", SetupInfo.Publisher);
                key.SetValue("InstallLocation", directory);
                key.SetValue("InstallSource", SetupInfo.ExeFolder);
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /S /uninstall");
                key.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                key.SetValue("Comments", S.IsChinese
                    ? "安全清理 C 盘缓存、临时文件与系统垃圾。"
                    : "Safely cleans caches, temp files and system junk on drive C.");
            }

            using (RegistryKey key = root.CreateSubKey(SetupInfo.AppKeyPath))
            {
                if (key != null)
                {
                    key.SetValue("InstallDir", directory);
                    key.SetValue("Version", SetupInfo.Version);
                    key.SetValue("Machine", request.MachineScope ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("Scope", request.MachineScope ? "machine" : "user");
                    key.SetValue("Installed", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                }
            }

            using (RegistryKey key = root.CreateSubKey(SetupInfo.AppPathsKeyPath))
            {
                if (key != null)
                {
                    key.SetValue(string.Empty, appExe);
                    key.SetValue("Path", directory);
                }
            }
        }

        // ------------------------------------------------------------ uninstall
        public static bool Uninstall(InstallRequest request, IProgress<InstallProgress> progress, out string error)
        {
            error = null;

            try
            {
                InstallState state = Query();
                string directory = request.InstallDir;
                if (string.IsNullOrEmpty(directory) && state.Installed)
                {
                    directory = state.InstallDir;
                }

                if (string.IsNullOrEmpty(directory))
                {
                    directory = SetupInfo.ExeFolder;
                }

                directory = Path.GetFullPath(directory);
                bool machineScope = state.Installed ? state.MachineScope : request.MachineScope;
                Report(progress, 5, S.T("progress.preparing"));

                Report(progress, 12, S.T("progress.stopping"));
                string stopError;
                if (!StopRunningApplication(out stopError))
                {
                    Log.Warn("Stopping the running application failed: {0}", stopError);
                }

                Report(progress, 25, S.T("progress.task"));
                RemoveScheduledTask(directory);

                Report(progress, 40, S.T("progress.shortcuts"));
                Shortcuts.Delete(Shortcuts.DesktopLink(true));
                Shortcuts.Delete(Shortcuts.DesktopLink(false));
                Shortcuts.DeleteFolder(Shortcuts.ProgramGroupFolder(true));
                Shortcuts.DeleteFolder(Shortcuts.ProgramGroupFolder(false));

                Report(progress, 52, S.T("progress.registry"));
                DeleteRegistry(Registry.CurrentUser);
                DeleteRegistry(Registry.LocalMachine);

                if (request.DeleteUserData)
                {
                    Report(progress, 62, S.T("progress.files"));
                    DeleteUserData();
                }

                Report(progress, 70, S.T("progress.files"));
                DeleteProgramFiles(directory);

                Report(progress, 92, S.T("progress.done"));
                ScheduleSelfRemoval(directory);

                Report(progress, 100, S.T("progress.done"));
                Log.Info("Uninstalled {0} from {1} (machine={2})", SetupInfo.Product, directory, machineScope);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Uninstall failed: {0}", ex);
                return false;
            }
        }

        private static void RemoveScheduledTask(string directory)
        {
            try
            {
                string exe = Path.Combine(directory, SetupInfo.ExeName);
                if (!File.Exists(exe))
                {
                    return;
                }

                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--uninstall-auto",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = directory
                };

                using (Process process = Process.Start(info))
                {
                    if (process != null)
                    {
                        process.WaitForExit(15000);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Removing the scheduled task failed: {0}", ex.Message);
            }
        }

        private static void DeleteRegistry(RegistryKey hive)
        {
            foreach (string path in new[] { SetupInfo.UninstallKeyPath, SetupInfo.AppKeyPath, SetupInfo.AppPathsKeyPath })
            {
                try
                {
                    hive.DeleteSubKeyTree(path, false);
                }
                catch (Exception ex)
                {
                    Log.Warn("Deleting '{0}' from {1} failed: {2}", path, hive.Name, ex.Message);
                }
            }
        }

        private static void DeleteUserData()
        {
            try
            {
                string data = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    SetupInfo.Product);

                if (Directory.Exists(data))
                {
                    DeleteTreeRobust(data);
                    Log.Info("Removed user data from {0}", data);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Removing user data failed: {0}", ex.Message);
            }
        }

        private static void DeleteProgramFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(file), SetupInfo.UninstallerName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Warn("Deleting '{0}' failed: {1}", file, ex.Message);
                }
            }

            foreach (string sub in Directory.GetDirectories(directory))
            {
                try
                {
                    DeleteTreeRobust(sub);
                }
                catch (Exception ex)
                {
                    Log.Warn("Deleting '{0}' failed: {1}", sub, ex.Message);
                }
            }
        }

        public static bool StopRunningApplication(out string error)
        {
            error = null;
            List<Process> targets = new List<Process>();

            try
            {
                foreach (Process process in Process.GetProcessesByName(SetupInfo.Product))
                {
                    if (process.Id == Process.GetCurrentProcess().Id)
                    {
                        process.Dispose();
                        continue;
                    }

                    bool ours = true;
                    try
                    {
                        string path = process.MainModule == null ? null : process.MainModule.FileName;
                        ours = path == null || path.IndexOf(SetupInfo.Product, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    catch
                    {
                        // Another user's process: do not touch it.
                        ours = false;
                    }

                    if (ours)
                    {
                        targets.Add(process);
                    }
                    else
                    {
                        process.Dispose();
                    }
                }

                foreach (Process process in targets)
                {
                    try
                    {
                        if (!process.CloseMainWindow())
                        {
                            process.Kill();
                        }
                        else if (!process.WaitForExit(3000))
                        {
                            process.Kill();
                        }

                        process.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Warn("Enumerating running processes failed: {0}", ex.Message);
            }

            return string.IsNullOrEmpty(error);
        }

        public static long DirectorySize(string directory)
        {
            try
            {
                long total = 0;
                if (!Directory.Exists(directory))
                {
                    return 0;
                }

                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                    }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }

        public static void DeleteTreeRobust(string path)
        {
            if (!Directory.Exists(path))
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                return;
            }

            foreach (string file in Directory.GetFiles(path))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Warn("Deleting '{0}' failed: {1}", file, ex.Message);
                }
            }

            foreach (string sub in Directory.GetDirectories(path))
            {
                DeleteTreeRobust(sub);
            }

            try
            {
                Directory.Delete(path, false);
            }
            catch (Exception ex)
            {
                Log.Warn("Deleting '{0}' failed: {1}", path, ex.Message);
            }
        }

        /// <summary>Starts a copy of this executable from %TEMP% that deletes the install folder.</summary>
        public static void ScheduleSelfRemoval(string directory)
        {
            try
            {
                string temp = Path.Combine(
                    Path.GetTempPath(),
                    "cleaner-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");

                File.Copy(SetupInfo.ExePath, temp, true);

                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = temp,
                    Arguments = "--cleanup \"" + directory + "\" " + Process.GetCurrentProcess().Id,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };

                Process.Start(info);
                Log.Info("Scheduled the removal of '{0}' through '{1}'.", directory, temp);
            }
            catch (Exception ex)
            {
                Log.Warn("Scheduling the folder removal failed: {0}", ex.Message);
            }
        }

        /// <summary>Runs in the %TEMP% copy: waits for the uninstaller to exit, then deletes everything.</summary>
        public static int CleanupAfterExit(string directory, int pid)
        {
            try
            {
                if (pid > 0)
                {
                    try
                    {
                        using (Process process = Process.GetProcessById(pid))
                        {
                            process.WaitForExit(30000);
                        }
                    }
                    catch
                    {
                    }
                }

                for (int attempt = 0; attempt < 40; attempt++)
                {
                    if (!Directory.Exists(directory) || Directory.GetFileSystemEntries(directory).Length == 0)
                    {
                        break;
                    }

                    DeleteTreeRobust(directory);

                    if (!Directory.Exists(directory))
                    {
                        break;
                    }

                    Thread.Sleep(250);
                }

                try
                {
                    if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                    {
                        Directory.Delete(directory, false);
                    }
                }
                catch
                {
                }

                Log.Info("Cleanup finished for '{0}'; exists={1}", directory, Directory.Exists(directory));
            }
            catch (Exception ex)
            {
                Log.Error("Cleanup failed: {0}", ex);
            }

            // Remove this helper executable once it has exited.
            try
            {
                string command = "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + SetupInfo.ExePath + "\"";
                ProcessStartInfo info = new ProcessStartInfo("cmd.exe", command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(info);
            }
            catch
            {
            }

            return 0;
        }

        private static void Report(IProgress<InstallProgress> progress, int percent, string message)
        {
            if (progress != null)
            {
                progress.Report(new InstallProgress { Percent = percent, Message = message });
            }
        }
    }
}
