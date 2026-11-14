using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Cleaner
{
    internal sealed class Options
    {
        public bool Help;
        public bool Version;
        public bool SelfTest;
        public bool DryRun;
        public bool UiSmoke;
        public bool Scheduled;
        public bool UninstallAuto;
        public bool NoElevate;

        /// <summary>Set by the elevated relaunch: wait for the previous instance to exit.</summary>
        public bool Handoff;
        public string ScreenshotPath;
        public string ScreenshotResultPath;
        public int ScreenshotWait;
        public string DataRoot;
        public string Categories;
        public string Language;
        public string TargetUserSid;
        public string TargetLocalAppData;
        public string TargetTemp;
        public string CleanHeadless;
        public string[] OriginalArgs = new string[0];

        public bool IsConsoleMode
        {
            get
            {
                return Help || Version || SelfTest || DryRun || Scheduled || UninstallAuto ||
                       !string.IsNullOrEmpty(CleanHeadless);
            }
        }

        public static Options Parse(string[] args, out string error)
        {
            error = null;
            Options options = new Options { OriginalArgs = args ?? new string[0] };

            for (int i = 0; i < options.OriginalArgs.Length; i++)
            {
                string argument = options.OriginalArgs[i] ?? string.Empty;
                string name = argument;
                string value = null;

                int equals = argument.IndexOf('=');
                if (equals > 0)
                {
                    name = argument.Substring(0, equals);
                    value = argument.Substring(equals + 1);
                }

                switch (name.ToLowerInvariant())
                {
                    case "-h":
                    case "/?":
                    case "--help":
                        options.Help = true;
                        break;
                    case "--version":
                        options.Version = true;
                        break;
                    case "--selftest":
                        options.SelfTest = true;
                        break;
                    case "--dry-run":
                        options.DryRun = true;
                        break;
                    case "--ui-smoke":
                        options.UiSmoke = true;
                        break;
                    case "--scheduled":
                        options.Scheduled = true;
                        break;
                    case "--uninstall-auto":
                        options.UninstallAuto = true;
                        break;
                    case "--no-elevate":
                        options.NoElevate = true;
                        break;
                    case "--handoff":
                        options.Handoff = true;
                        break;
                    case "--screenshot":
                        options.ScreenshotPath = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--screenshot-result":
                        options.ScreenshotResultPath = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--screenshot-wait":
                    {
                        string wait = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        int seconds;
                        if (!string.IsNullOrEmpty(wait) && int.TryParse(wait, out seconds) && seconds >= 0 && seconds <= 300)
                        {
                            options.ScreenshotWait = seconds;
                        }
                        else if (string.IsNullOrEmpty(error))
                        {
                            error = "Invalid value for --screenshot-wait.";
                        }

                        break;
                    }
                    case "--data-root":
                        options.DataRoot = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--categories":
                        options.Categories = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--lang":
                    case "--language":
                        options.Language = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--target-user-sid":
                        options.TargetUserSid = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--target-localappdata":
                        options.TargetLocalAppData = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--target-temp":
                        options.TargetTemp = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--clean":
                        options.CleanHeadless = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    default:
                        if (name.StartsWith("-", StringComparison.Ordinal))
                        {
                            error = "Unknown option: " + argument;
                            return null;
                        }

                        break;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    return null;
                }
            }

            return options;
        }

        private static string NextValue(string[] args, ref int index, out string error)
        {
            error = null;
            if (index + 1 >= args.Length)
            {
                error = "Missing value for " + args[index] + ".";
                return null;
            }

            index++;
            return args[index];
        }

        public static string Usage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(AppInfo.Product + " " + AppInfo.Version + " — " + AppInfo.DisplayName);
            sb.AppendLine();
            sb.AppendLine("用法 / Usage:");
            sb.AppendLine("  Cleaner.exe                          启动图形界面 / start the GUI");
            sb.AppendLine("  Cleaner.exe --selftest               运行自检 / run built-in tests");
            sb.AppendLine("  Cleaner.exe --dry-run                只统计不删除 / scan only, delete nothing");
            sb.AppendLine("  Cleaner.exe --clean <ids>            命令行清理 / clean the given category ids");
            sb.AppendLine("  Cleaner.exe --scheduled              执行关机自动清理 / run scheduled cleanup");
            sb.AppendLine("  Cleaner.exe --uninstall-auto         删除自动清理任务 / remove the scheduled task");
            sb.AppendLine("  Cleaner.exe --screenshot <out.png>   导出界面截图 / render the UI to a PNG");
            sb.AppendLine("  Cleaner.exe --screenshot-result <png> 导出结果对话框 / render the result dialog");
            sb.AppendLine("  Cleaner.exe --ui-smoke               启动界面后自动退出 / open the UI briefly");            sb.AppendLine();
            sb.AppendLine("通用参数 / Common options:");
            sb.AppendLine("  --categories <ids>   逗号分隔的类别 / comma separated category ids");
            sb.AppendLine("  --data-root <dir>    覆盖数据目录 / override the data folder");
            sb.AppendLine("  --lang <zh-CN|en-US> 界面语言 / UI language");
            sb.AppendLine("  --version            显示版本 / print the version");
            return sb.ToString();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string parseError;
            Options options = Options.Parse(args, out parseError);

            if (options == null)
            {
                Native.TryAttachConsole();
                Console.Error.WriteLine(parseError);
                Console.Error.WriteLine(Options.Usage());
                return 2;
            }

            if (options.IsConsoleMode)
            {
                Native.TryAttachConsole();
            }

            if (!string.IsNullOrEmpty(options.DataRoot))
            {
                try
                {
                    AppInfo.DataRoot = options.DataRoot;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Invalid --data-root: " + ex.Message);
                    return 2;
                }
            }

            Settings settings = LoadSettings();
            Loc.Initialize(!string.IsNullOrEmpty(options.Language) ? options.Language : settings.Language);

            try
            {
                AppInfo.EnsureDataFolders(settings.LogRetentionDays);
                Log.Start();
                Log.Info("Command line: {0}", string.Join(" ", args ?? new string[0]));
            }
            catch
            {
            }

            if (options.Help)
            {
                Console.WriteLine(Options.Usage());
                return 0;
            }

            if (options.Version)
            {
                Console.WriteLine(AppInfo.Product + " " + AppInfo.Version);
                return 0;
            }

            if (options.SelfTest)
            {
                return SelfTests.Run();
            }

            if (options.DryRun)
            {
                return Commands.DryRun(options, settings);
            }

            if (options.Scheduled)
            {
                return Commands.ScheduledCleanup(options, settings);
            }

            if (options.UninstallAuto)
            {
                return Commands.UninstallAuto(settings);
            }

            if (!string.IsNullOrEmpty(options.CleanHeadless))
            {
                return Commands.HeadlessClean(options, settings);
            }

            if (!string.IsNullOrEmpty(options.ScreenshotResultPath))
            {
                return RunResultScreenshot(options);
            }

            return RunUi(options, settings);
        }

        /// <summary>Renders the result dialog with sample data (documentation / UI checks).</summary>
        private static int RunResultScreenshot(Options options)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                CleanupResult sample = new CleanupResult
                {
                    Bytes = 1354300000L,
                    Files = 311869,
                    Folders = 4312,
                    Skipped = 29,
                    Errors = 2,
                    Duration = TimeSpan.FromSeconds(106)
                };
                sample.SkippedCategories.Add(CleanupCatalog.ById("browserCache").Name + " (" + Loc.T("result.reasonBrowser") + ")");

                using (ResultDialog dialog = new ResultDialog(sample))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new System.Drawing.Point(-4000, -4000);
                    dialog.ShowInTaskbar = false;
                    dialog.Show();
                    Application.DoEvents();
                    Thread.Sleep(200);
                    Application.DoEvents();

                    string directory = Path.GetDirectoryName(Path.GetFullPath(options.ScreenshotResultPath));
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    dialog.CaptureScreenshot(options.ScreenshotResultPath);
                    Console.WriteLine("SCREENSHOT_RESULT_OK " + options.ScreenshotResultPath);

                    try
                    {
                        dialog.Close();
                    }
                    catch
                    {
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log.Exception("Result screenshot failed", ex);
                Console.Error.WriteLine("SCREENSHOT_RESULT_FAILED " + ex.Message);
                return 1;
            }
        }

        private static Settings LoadSettings()
        {
            try
            {
                return Settings.Load();
            }
            catch
            {
                return Settings.CreateDefault();
            }
        }

        private static int RunUi(Options options, Settings settings)
        {
            Native.SetAppUserModelId();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportUnhandled(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportUnhandled(e.ExceptionObject as Exception);

            bool isAdmin = Native.IsAdministrator;
            Log.Info("Starting UI (admin={0})", isAdmin);

            if (!string.IsNullOrEmpty(options.ScreenshotPath) || options.UiSmoke)
            {
                return RunUiAutomation(options, settings, isAdmin);
            }

            using (SingleInstance instance = new SingleInstance())
            {
                // The elevated relaunch is meant to replace the current window:
                // wait until that window has released the mutex instead of
                // treating the new process as a duplicate launch.
                bool handoff = options.Handoff;

                if (!instance.Acquire())
                {
                    if (handoff)
                    {
                        Log.Info("Waiting for the previous instance to finish before taking over.");
                        bool takenOver = false;
                        for (int attempt = 0; attempt < 80 && !takenOver; attempt++)
                        {
                            if (attempt == 0)
                            {
                                instance.SignalExisting();
                            }

                            takenOver = instance.TryTakeOwnership(150);
                            if (!takenOver)
                            {
                                System.Threading.Thread.Sleep(100);
                            }
                        }

                        if (!takenOver)
                        {
                            Log.Warn("Hand-off timed out: the previous instance is still running.");
                            return 0;
                        }

                        Log.Info("Hand-off complete: this instance now runs the UI.");
                    }
                    else
                    {
                        instance.SignalExisting();
                        Log.Info("Another instance is already running; activation signal sent.");
                        return 0;
                    }
                }

                try
                {
                    using (MainForm form = new MainForm(settings, isAdmin, options))
                    {
                        // Release ownership the moment the elevated process has
                        // been started so it does not have to wait for shutdown.
                        form.Elevating += (sender, e) =>
                        {
                            instance.Release();
                            Log.Info("Released the single-instance mutex for the elevated hand-off.");
                        };

                        using (System.Windows.Forms.Timer activationTimer = new System.Windows.Forms.Timer { Interval = 400 })
                        {
                            activationTimer.Tick += (sender, e) =>
                            {
                                if (instance.WaitForActivation())
                                {
                                    if (form.WindowState == FormWindowState.Minimized)
                                    {
                                        form.WindowState = FormWindowState.Normal;
                                    }

                                    form.Activate();
                                    form.BringToFront();
                                }
                            };
                            activationTimer.Start();
                            Application.Run(form);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ReportUnhandled(ex);
                    return 1;
                }
            }

            return 0;
        }

        private static int RunUiAutomation(Options options, Settings settings, bool isAdmin)
        {
            try
            {
                using (MainForm form = new MainForm(settings, isAdmin, options))
                {
                    form.SkipInitialScan = options.ScreenshotWait <= 0;

                    if (!string.IsNullOrEmpty(options.ScreenshotPath))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(-4000, -4000);
                        form.ShowInTaskbar = false;
                        form.Show();
                        Application.DoEvents();

                        if (options.ScreenshotWait > 0)
                        {
                            DateTime deadline = DateTime.Now.AddSeconds(options.ScreenshotWait);
                            while (DateTime.Now < deadline)
                            {
                                Application.DoEvents();
                                Thread.Sleep(80);
                            }
                        }
                        else
                        {
                            Thread.Sleep(150);
                            Application.DoEvents();
                        }

                        string directory = Path.GetDirectoryName(Path.GetFullPath(options.ScreenshotPath));
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        form.CaptureScreenshot(options.ScreenshotPath);
                        form.Close();
                        Console.WriteLine("SCREENSHOT_OK " + options.ScreenshotPath);
                        return 0;
                    }

                    // --ui-smoke: open the window, let the message loop run, then close.
                    bool shown = false;
                    form.Shown += (sender, e) => { shown = true; };

                    using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1200 })
                    {
                        timer.Tick += (sender, e) =>
                        {
                            timer.Stop();
                            form.Close();
                        };
                        timer.Start();
                        Application.Run(form);
                    }

                    if (!shown)
                    {
                        Console.WriteLine("UI_SMOKE_FAILED window-not-shown");
                        return 1;
                    }

                    Console.WriteLine("UI_SMOKE_OK");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("UI automation failed", ex);
                Console.Error.WriteLine("UI_AUTOMATION_FAILED " + ex.Message);
                return 1;
            }
        }

        private static void ReportUnhandled(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Log.Exception("Unhandled exception", exception);
            try
            {
                MessageBox.Show(
                    Loc.T("error.unexpected", exception.Message) + Loc.T("error.logHint", Log.CurrentFile ?? string.Empty),
                    Loc.T("error.title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }
}
