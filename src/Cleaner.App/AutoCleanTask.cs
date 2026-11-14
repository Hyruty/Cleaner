using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace Cleaner
{
    /// <summary>
    /// Registers the "clean at shutdown" scheduled task. The task runs the same
    /// executable with --scheduled as the interactive user, so no elevated
    /// rights are needed and user caches are the ones being cleaned.
    /// </summary>
    internal static class AutoCleanTask
    {
        private const string LogonDelay = "PT2M";
        private const string ExecutionLimit = "PT5M";

        public static bool IsRegistered()
        {
            string output;
            int exitCode = RunSchtasks("/Query /TN \"" + AppInfo.TaskName + "\" /FO LIST", out output);
            return exitCode == 0;
        }

        public static bool Install(out string error)
        {
            error = null;

            string xmlPath = Path.Combine(Path.GetTempPath(), "cleaner-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(xmlPath, BuildXml(), new UnicodeEncoding(false, true));

                string output;
                int exitCode = RunSchtasks(
                    "/Create /TN \"" + AppInfo.TaskName + "\" /XML \"" + xmlPath + "\" /F",
                    out output);

                if (exitCode != 0)
                {
                    error = string.IsNullOrWhiteSpace(output) ? "schtasks exited with code " + exitCode : output.Trim();
                    Log.Error("Registering the scheduled task failed: {0}", error);
                    return false;
                }

                Log.Info("Scheduled task '{0}' registered for {1}", AppInfo.TaskName, AppInfo.ExePath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Exception("Registering the scheduled task failed", ex);
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(xmlPath))
                    {
                        File.Delete(xmlPath);
                    }
                }
                catch
                {
                }
            }
        }

        public static bool Uninstall(out string error)
        {
            error = null;
            string output;
            int exitCode = RunSchtasks("/Delete /TN \"" + AppInfo.TaskName + "\" /F", out output);

            if (exitCode != 0 && output.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = string.IsNullOrWhiteSpace(output) ? "schtasks exited with code " + exitCode : output.Trim();
                Log.Error("Removing the scheduled task failed: {0}", error);
                return false;
            }

            Log.Info("Scheduled task '{0}' removed", AppInfo.TaskName);
            return true;
        }

        public static bool RunNow(out string error)
        {
            error = null;
            string output;
            int exitCode = RunSchtasks("/Run /TN \"" + AppInfo.TaskName + "\"", out output);
            if (exitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(output) ? "schtasks exited with code " + exitCode : output.Trim();
                return false;
            }

            return true;
        }

        public static string BuildXml()
        {
            string userId;
            try
            {
                userId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            }
            catch
            {
                userId = Environment.UserDomainName + "\\" + Environment.UserName;
            }

            string exe = AppInfo.ExePath;
            string workingDirectory = AppInfo.ExeFolder;
            const string subscription =
                "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[(Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=13) or (Provider[@Name='User32'] and EventID=1074)]]</Select></Query></QueryList>";

            string safeUser = SecurityElement.Escape(userId);
            string safeExe = SecurityElement.Escape(exe);
            string safeWorkingDirectory = SecurityElement.Escape(workingDirectory);
            string safeSubscription = SecurityElement.Escape(subscription);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Author>Cleaner</Author>");
            sb.AppendLine("    <Description>Cleans the selected cache folders at shutdown and retries after sign-in.</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");
            sb.AppendLine("    <EventTrigger>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            sb.AppendLine("      <Subscription>" + safeSubscription + "</Subscription>");
            sb.AppendLine("    </EventTrigger>");
            sb.AppendLine("    <LogonTrigger>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            sb.AppendLine("      <UserId>" + safeUser + "</UserId>");
            sb.AppendLine("      <Delay>" + LogonDelay + "</Delay>");
            sb.AppendLine("    </LogonTrigger>");
            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendLine("      <UserId>" + safeUser + "</UserId>");
            sb.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            sb.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.AppendLine("    <IdleSettings>");
            sb.AppendLine("      <StopOnIdleEnd>false</StopOnIdleEnd>");
            sb.AppendLine("      <RestartOnIdle>false</RestartOnIdle>");
            sb.AppendLine("    </IdleSettings>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
            sb.AppendLine("    <WakeToRun>false</WakeToRun>");
            sb.AppendLine("    <ExecutionTimeLimit>" + ExecutionLimit + "</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>7</Priority>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine("      <Command>" + safeExe + "</Command>");
            sb.AppendLine("      <Arguments>--scheduled</Arguments>");
            sb.AppendLine("      <WorkingDirectory>" + safeWorkingDirectory + "</WorkingDirectory>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.AppendLine("</Task>");

            return sb.ToString();
        }

        /// <summary>Validates the task XML against an XML parser (used by selftest).</summary>
        public static bool ValidateXml(out string error)
        {
            error = null;
            try
            {
                System.Xml.XmlDocument document = new System.Xml.XmlDocument();
                document.LoadXml(BuildXml());
                if (document.DocumentElement == null || document.DocumentElement.Name != "Task")
                {
                    error = "Root element is not <Task>.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        private static int RunSchtasks(string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return -1;
                    }

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(30000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        output = "schtasks timed out.";
                        return -1;
                    }

                    output = string.Join(Environment.NewLine, new[] { stdout, stderr }).Trim();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                Log.Exception("Running schtasks failed", ex);
                return -1;
            }
        }
    }
}
