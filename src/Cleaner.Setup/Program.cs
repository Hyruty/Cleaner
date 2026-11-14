using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Cleaner.Setup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string parseError;
            SetupOptions options = SetupOptions.Parse(args, out parseError);
            if (options == null)
            {
                MessageBox.Show(parseError + Environment.NewLine + Environment.NewLine + SetupOptions.Usage(),
                    "CleanerSetup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            S.Initialize(options.Language);

            if (options.Cleanup)
            {
                return Installer.CleanupAfterExit(options.CleanupDirectory, options.CleanupPid);
            }

            if (options.Help)
            {
                MessageBox.Show(SetupOptions.Usage(), "CleanerSetup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            if (options.SelfTest)
            {
                return SelfTests();
            }

            bool isAdmin = Installer.IsAdmin;
            InstallState state = Installer.Query();

            bool machineScope = options.MachineScope ?? isAdmin;
            bool needsAdmin = (machineScope || (state.Installed && state.MachineScope)) && !isAdmin;

            if (needsAdmin && !options.Elevated && !options.NoElevate)
            {
                if (TryElevate(args))
                {
                    return 0;
                }

                // The user declined the UAC prompt: keep working with a per-user
                // install instead of failing outright.
                Log.Info("Elevation was not granted; falling back to a per-user installation.");
                machineScope = false;
            }

            if (options.Quiet)
            {
                return RunQuiet(options, machineScope);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (sender, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportError(e.ExceptionObject as Exception);

            try
            {
                using (SetupForm form = new SetupForm(options, isAdmin, machineScope))
                {
                    if (!string.IsNullOrEmpty(options.ScreenshotPath))
                    {
                        form.SuppressDialogs = true;
                        return CaptureScreenshot(form, options.ScreenshotPath);
                    }

                    Application.Run(form);
                }

                return 0;
            }
            catch (Exception ex)
            {
                ReportError(ex);
                return 1;
            }
        }

        private static int CaptureScreenshot(SetupForm form, string path)
        {
            try
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();

                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                form.CaptureScreenshot(path);
                form.Close();
                Console.WriteLine("SETUP_SCREENSHOT_OK " + path);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("Capturing the setup screenshot failed: {0}", ex);
                return 1;
            }
        }

        private static void ReportError(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Log.Error("Unhandled setup error: {0}", exception);
            try
            {
                MessageBox.Show(S.T("error.failed", exception.Message), S.T("error.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private static bool TryElevate(string[] args)
        {
            try
            {
                StringBuilder commandLine = new StringBuilder();
                foreach (string argument in args ?? new string[0])
                {
                    if (string.IsNullOrEmpty(argument))
                    {
                        continue;
                    }

                    if (commandLine.Length > 0)
                    {
                        commandLine.Append(' ');
                    }

                    commandLine.Append(argument.IndexOf(' ') >= 0 ? "\"" + argument + "\"" : argument);
                }

                if (commandLine.Length > 0)
                {
                    commandLine.Append(' ');
                }

                commandLine.Append("--elevated");

                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = SetupInfo.ExePath,
                    Arguments = commandLine.ToString(),
                    WorkingDirectory = SetupInfo.ExeFolder,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using (Process process = Process.Start(info))
                {
                    return process != null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Elevation failed: {0}", ex.Message);
                return false;
            }
        }

        private static int RunQuiet(SetupOptions options, bool machineScope)
        {
            InstallRequest request = new InstallRequest
            {
                InstallDir = options.InstallDir,
                MachineScope = machineScope,
                DesktopShortcut = options.DesktopShortcut ?? true,
                StartMenuShortcut = options.StartMenuShortcut ?? true,
                LaunchAfter = options.LaunchAfter ?? false,
                DeleteUserData = options.DeleteUserData ?? false
            };

            string error;
            bool ok = options.Uninstall
                ? Installer.Uninstall(request, null, out error)
                : Installer.Install(request, null, out error);

            if (ok)
            {
                Log.Info("Quiet {0} completed (machine={1}).", options.Uninstall ? "uninstall" : "install", machineScope);
            }
            else
            {
                Log.Error("Quiet {0} failed: {1}", options.Uninstall ? "uninstall" : "install", error);
            }

            return ok ? 0 : 1;
        }

        /// <summary>Verifies the payload and the setup plumbing without changing the system.</summary>
        private static int SelfTests()
        {
            StringBuilder log = new StringBuilder();
            int passed = 0;
            int failed = 0;

            Action<string, Action> check = (name, body) =>
            {
                try
                {
                    body();
                    passed++;
                    log.AppendLine("[PASS] " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    log.AppendLine("[FAIL] " + name + " : " + ex.Message);
                }
            };

            Action<bool, string> assert = (condition, message) =>
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            };

            log.AppendLine("CleanerSetup self-test " + SetupInfo.Version + " (admin=" + Installer.IsAdmin + ")");

            check("payload.present", () =>
            {
                string error;
                assert(Payload.Verify(out error), "payload invalid: " + error);
                assert(Payload.Size > 0, "payload size is zero");
            });

            check("payload.entries", () =>
            {
                List<string> entries = Payload.ListEntries();
                assert(entries.Count > 0, "no entries");
                assert(entries.Any(e => string.Equals(e, SetupInfo.ExeName, StringComparison.OrdinalIgnoreCase)),
                    "Cleaner.exe missing from payload");
            });

            check("payload.extract", () =>
            {
                string temp = Path.Combine(Path.GetTempPath(), "cleaner-payload-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    Payload.ExtractTo(temp, null);
                    string exe = Path.Combine(temp, SetupInfo.ExeName);
                    assert(File.Exists(exe), "Cleaner.exe was not extracted");
                    FileInfo info = new FileInfo(exe);
                    assert(info.Length > 64 * 1024, "Cleaner.exe looks truncated: " + info.Length + " bytes");
                    assert(info.Length < 32 * 1024 * 1024, "Cleaner.exe is unexpectedly large");
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(temp))
                        {
                            Directory.Delete(temp, true);
                        }
                    }
                    catch
                    {
                    }
                }
            });

            check("options.parse", () =>
            {
                string error;
                SetupOptions parsed = SetupOptions.Parse(new[] { "/S", "/D=C:\\Temp\\Cleaner", "/desktop=0", "/scope=user" }, out error);
                assert(parsed != null, "parse failed: " + error);
                assert(parsed.Quiet, "quiet flag missing");
                assert(parsed.InstallDir == "C:\\Temp\\Cleaner", "install dir not parsed");
                assert(parsed.DesktopShortcut == false, "desktop flag not parsed");
                assert(parsed.MachineScope == false, "scope flag not parsed");

                SetupOptions uninstall = SetupOptions.Parse(new[] { "/S", "/uninstall" }, out error);
                assert(uninstall != null && uninstall.Uninstall, "uninstall flag not parsed");
            });

            check("paths.defaults", () =>
            {
                assert(Installer.DefaultInstallDir(true).EndsWith("Cleaner", StringComparison.OrdinalIgnoreCase), "machine default dir unexpected");
                assert(Installer.DefaultInstallDir(false).IndexOf("Programs", StringComparison.OrdinalIgnoreCase) >= 0, "user default dir unexpected");
                assert(Shortcuts.DesktopLink(true).EndsWith(".lnk", StringComparison.OrdinalIgnoreCase), "desktop link unexpected");
                assert(Shortcuts.StartMenuLink(true).IndexOf("Cleaner", StringComparison.OrdinalIgnoreCase) >= 0, "start menu link unexpected");
            });

            check("registry.paths", () =>
            {
                assert(!string.IsNullOrEmpty(SetupInfo.UninstallKeyPath), "uninstall key path empty");
                assert(!string.IsNullOrEmpty(SetupInfo.AppKeyPath), "app key path empty");
                assert(SetupInfo.UninstallKeyPath.IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0, "uninstall key path looks wrong");
            });

            log.AppendLine();
            log.AppendLine(failed > 0
                ? "SETUP_SELF_TEST_FAILED passed=" + passed + " failed=" + failed
                : "SETUP_SELF_TEST_OK passed=" + passed);

            string report = Path.Combine(Path.GetTempPath(), "cleaner-setup-selftest.log");
            try
            {
                File.WriteAllText(report, log.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }

            Console.WriteLine(log.ToString());
            return failed > 0 ? 1 : 0;
        }
    }
}
