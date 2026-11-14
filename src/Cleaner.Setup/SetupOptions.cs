using System;
using System.Globalization;

namespace Cleaner.Setup
{
    internal sealed class SetupOptions
    {
        public bool Quiet;
        public bool Uninstall;
        public bool SelfTest;
        public bool Elevated;
        public bool NoElevate;
        public bool Help;

        public bool Cleanup;
        public string CleanupDirectory;
        public int CleanupPid;

        public string InstallDir;
        public bool? MachineScope;
        public bool? DesktopShortcut;
        public bool? StartMenuShortcut;
        public bool? LaunchAfter;
        public bool? DeleteUserData;
        public string Language;
        public string ScreenshotPath;
        public string[] OriginalArgs = new string[0];

        public static SetupOptions Parse(string[] args, out string error)
        {
            error = null;
            SetupOptions options = new SetupOptions { OriginalArgs = args ?? new string[0] };

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
                    case "/s":
                    case "/silent":
                    case "/quiet":
                    case "--silent":
                        options.Quiet = true;
                        break;
                    case "/uninstall":
                    case "--uninstall":
                        options.Uninstall = true;
                        break;
                    case "--selftest":
                        options.SelfTest = true;
                        break;
                    case "--elevated":
                        options.Elevated = true;
                        break;
                    case "--no-elevate":
                        options.NoElevate = true;
                        break;
                    case "-h":
                    case "/?":
                    case "--help":
                        options.Help = true;
                        break;
                    case "/d":
                    case "/dir":
                    case "/directory":
                    case "--dir":
                        options.InstallDir = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "/scope":
                        if (string.Equals(value, "machine", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
                        {
                            options.MachineScope = true;
                        }
                        else if (string.Equals(value, "user", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(value, "current", StringComparison.OrdinalIgnoreCase))
                        {
                            options.MachineScope = false;
                        }
                        else
                        {
                            error = "Invalid value for /scope: " + (value ?? string.Empty);
                        }

                        break;
                    case "/machine":
                        options.MachineScope = true;
                        break;
                    case "/user":
                    case "/peruser":
                        options.MachineScope = false;
                        break;
                    case "/desktop":
                        options.DesktopShortcut = ParseBool(value);
                        break;
                    case "/startmenu":
                        options.StartMenuShortcut = ParseBool(value);
                        break;
                    case "/launch":
                        options.LaunchAfter = ParseBool(value);
                        break;
                    case "/deletedata":
                    case "/removedata":
                        options.DeleteUserData = ParseBool(value);
                        break;
                    case "/lang":
                    case "--lang":
                        options.Language = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--screenshot":
                        options.ScreenshotPath = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        break;
                    case "--cleanup":
                        options.Cleanup = true;
                        options.CleanupDirectory = value ?? NextValue(options.OriginalArgs, ref i, out error);
                        string pid = NextValue(options.OriginalArgs, ref i, out error);
                        int parsedPid;
                        if (!string.IsNullOrEmpty(pid) && int.TryParse(pid, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedPid))
                        {
                            options.CleanupPid = parsedPid;
                        }

                        break;
                    default:
                        if (name.StartsWith("--", StringComparison.Ordinal))
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

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            return !(value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                     value.Equals("no", StringComparison.OrdinalIgnoreCase));
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
            return
                "CleanerSetup " + SetupInfo.Version + Environment.NewLine +
                "  CleanerSetup.exe                        interactive install" + Environment.NewLine +
                "  CleanerSetup.exe /S [/D=<dir>]          silent install" + Environment.NewLine +
                "  CleanerSetup.exe /uninstall             interactive uninstall" + Environment.NewLine +
                "  CleanerSetup.exe /S /uninstall          silent uninstall" + Environment.NewLine +
                "  /scope=machine|user   installation scope" + Environment.NewLine +
                "  /desktop=0|1 /startmenu=0|1 /launch=0|1" + Environment.NewLine +
                "  /lang=zh-CN|en-US    installer language" + Environment.NewLine;
        }
    }
}
