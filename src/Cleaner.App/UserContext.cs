using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Principal;

namespace Cleaner
{
    /// <summary>
    /// Describes whose cache folders are being cleaned. When the app is elevated
    /// it cleans the caches of the interactive desktop user, not of the
    /// administrator account that got the UAC prompt.
    /// </summary>
    internal sealed class UserContext
    {
        public string Sid { get; private set; }
        public string LocalAppData { get; private set; }
        public string Temp { get; private set; }
        public string Profile { get; private set; }
        public string Source { get; private set; }

        public string RoamingAppData
        {
            get
            {
                if (string.IsNullOrEmpty(Profile))
                {
                    return null;
                }

                return Path.Combine(Profile, "AppData", "Roaming");
            }
        }

        private static UserContext _cached;

        public static UserContext Current(bool isAdmin, string overrideSid, string overrideLocalAppData, string overrideTemp)
        {
            if (_cached != null &&
                string.IsNullOrEmpty(overrideSid) &&
                string.IsNullOrEmpty(overrideLocalAppData) &&
                string.IsNullOrEmpty(overrideTemp))
            {
                return _cached;
            }

            UserContext context = Resolve(isAdmin, overrideSid, overrideLocalAppData, overrideTemp);
            if (string.IsNullOrEmpty(overrideSid) && string.IsNullOrEmpty(overrideLocalAppData))
            {
                _cached = context;
            }

            return context;
        }

        private static UserContext Resolve(bool isAdmin, string overrideSid, string overrideLocalAppData, string overrideTemp)
        {
            string currentSid = GetCurrentSid();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string temp = Path.GetTempPath();
            string sid = currentSid;
            string source = "current-user";

            if (!string.IsNullOrEmpty(overrideLocalAppData))
            {
                localAppData = overrideLocalAppData;
                sid = string.IsNullOrEmpty(overrideSid) ? currentSid : overrideSid;
                temp = string.IsNullOrEmpty(overrideTemp) ? Path.Combine(localAppData, "Temp") : overrideTemp;
                source = "handed-over-by-launcher";
            }
            else if (isAdmin)
            {
                string interactiveSid;
                if (TryGetInteractiveShellSid(out interactiveSid) &&
                    !string.IsNullOrEmpty(interactiveSid) &&
                    !string.Equals(interactiveSid, currentSid, StringComparison.OrdinalIgnoreCase))
                {
                    string interactiveLocalAppData = GetLocalAppDataForSid(interactiveSid);
                    if (!string.IsNullOrEmpty(interactiveLocalAppData))
                    {
                        sid = interactiveSid;
                        localAppData = interactiveLocalAppData;
                        temp = Path.Combine(interactiveLocalAppData, "Temp");
                        source = "interactive-desktop-user";
                    }
                }
            }

            if (string.IsNullOrEmpty(localAppData))
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            if (string.IsNullOrEmpty(temp))
            {
                temp = Path.Combine(localAppData ?? Path.GetTempPath(), "Temp");
            }

            UserContext context = new UserContext
            {
                Sid = sid,
                LocalAppData = Normalize(localAppData),
                Temp = Normalize(temp),
                Profile = Normalize(GuessProfile(localAppData, sid)),
                Source = source
            };

            Log.Info("Cleanup user context: sid={0}; localAppData={1}; temp={2}; source={3}",
                context.Sid, context.LocalAppData, context.Temp, context.Source);

            return context;
        }

        private static string Normalize(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static string GuessProfile(string localAppData, string sid)
        {
            if (!string.IsNullOrEmpty(localAppData))
            {
                string marker = Path.DirectorySeparatorChar + "AppData" + Path.DirectorySeparatorChar + "Local";
                if (localAppData.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return localAppData.Substring(0, localAppData.Length - marker.Length);
                }
            }

            if (!string.IsNullOrEmpty(sid))
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid))
                    {
                        if (key != null)
                        {
                            string profile = key.GetValue("ProfileImagePath") as string;
                            if (!string.IsNullOrEmpty(profile))
                            {
                                return Environment.ExpandEnvironmentVariables(profile);
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static string GetCurrentSid()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return identity.User == null ? string.Empty : identity.User.Value;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>LocalAppData of a user SID, read from the HKEY_USERS hive.</summary>
        public static string GetLocalAppDataForSid(string sid)
        {
            if (string.IsNullOrEmpty(sid))
            {
                return null;
            }

            string[] valueNames = { "Local AppData", "Local AppData" };
            string[] subKeys =
            {
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"
            };

            string profile = null;
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid))
                {
                    if (key != null)
                    {
                        profile = key.GetValue("ProfileImagePath") as string;
                    }
                }
            }
            catch
            {
            }

            foreach (string subKey in subKeys)
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.Users.OpenSubKey(sid + @"\" + subKey))
                    {
                        if (key == null)
                        {
                            continue;
                        }

                        string value = key.GetValue(valueNames[0]) as string;
                        if (string.IsNullOrEmpty(value))
                        {
                            continue;
                        }

                        value = Environment.ExpandEnvironmentVariables(value);
                        if (!string.IsNullOrEmpty(profile))
                        {
                            value = value.Replace("%USERPROFILE%", profile);
                        }

                        return Path.GetFullPath(value);
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(profile))
            {
                return Path.Combine(profile, "AppData", "Local");
            }

            return null;
        }

        /// <summary>
        /// Finds the SID that owns explorer.exe in the current session. Fails
        /// softly (returns false) when WMI is unavailable.
        /// </summary>
        public static bool TryGetInteractiveShellSid(out string sid)
        {
            sid = null;

            try
            {
                int sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, SessionId FROM Win32_Process WHERE Name = 'explorer.exe'"))
                {
                    foreach (ManagementBaseObject raw in searcher.Get())
                    {
                        using (ManagementObject process = (ManagementObject)raw)
                        {
                            object session = process["SessionId"];
                            if (session == null || Convert.ToInt32(session, CultureInfo.InvariantCulture) != sessionId)
                            {
                                continue;
                            }

                            using (ManagementBaseObject owner = process.InvokeMethod("GetOwner", null, null))
                            {
                                if (owner == null)
                                {
                                    continue;
                                }

                                string user = owner["User"] as string;
                                string domain = owner["Domain"] as string;
                                if (string.IsNullOrEmpty(user))
                                {
                                    continue;
                                }

                                string accountName = string.IsNullOrEmpty(domain) ? user : domain + @"\" + user;
                                try
                                {
                                    var account = new NTAccount(accountName);
                                    sid = ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
                                    if (!string.IsNullOrEmpty(sid))
                                    {
                                        return true;
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public IEnumerable<string> Describe()
        {
            yield return "Sid=" + Sid;
            yield return "LocalAppData=" + LocalAppData;
            yield return "Temp=" + Temp;
            yield return "Profile=" + Profile;
            yield return "Source=" + Source;
        }
    }
}
