using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Cleaner
{
    /// <summary>Restarts the app through the UAC prompt, handing over the user context.</summary>
    internal static class Elevation
    {
        public static bool TryStartElevated(IEnumerable<string> arguments, out string error)
        {
            error = null;

            StringBuilder commandLine = new StringBuilder();
            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    if (string.IsNullOrEmpty(argument))
                    {
                        continue;
                    }

                    if (commandLine.Length > 0)
                    {
                        commandLine.Append(' ');
                    }

                    commandLine.Append(Quote(argument));
                }
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = AppInfo.ExePath,
                    Arguments = commandLine.ToString(),
                    WorkingDirectory = AppInfo.ExeFolder,
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
                error = ex.Message;
                Log.Exception("Elevation failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Quotes one argument the way CommandLineToArgvW parses it: backslashes
        /// are only special in front of a quote, and a value ending with a
        /// backslash must have that backslash doubled or the closing quote would
        /// be escaped (the classic "C:\path\" bug).
        /// </summary>
        public static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            bool needsQuotes = value.Length == 0 ||
                               value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0 ||
                               value.EndsWith("\\", StringComparison.Ordinal);

            if (!needsQuotes)
            {
                return value;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;

            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }

                sb.Append(c);
            }

            if (backslashes > 0)
            {
                sb.Append('\\', backslashes * 2);
            }

            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Single instance guard. A second launch signals the first window instead
    /// of opening a duplicate; the elevated hand-off waits until the instance
    /// it replaces has released the mutex.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\Cleaner.App.SingleInstance";
        private const string EventName = @"Local\Cleaner.App.Activate";

        private readonly string _mutexName;
        private readonly string _eventName;

        private System.Threading.Mutex _mutex;
        private System.Threading.EventWaitHandle _event;
        private bool _ownsMutex;

        /// <param name="nameSuffix">
        /// Optional suffix used by self-tests so they never collide with a
        /// running instance of the application.
        /// </param>
        public SingleInstance(string nameSuffix = null)
        {
            _mutexName = MutexName + (nameSuffix ?? string.Empty);
            _eventName = EventName + (nameSuffix ?? string.Empty);
        }

        public bool IsFirstInstance { get; private set; }

        public bool Acquire()
        {
            try
            {
                _mutex = new System.Threading.Mutex(true, _mutexName, out _ownsMutex);
                IsFirstInstance = _ownsMutex;

                _event = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, _eventName);

                return IsFirstInstance;
            }
            catch
            {
                IsFirstInstance = true;
                return true;
            }
        }

        /// <summary>
        /// Waits for the instance that currently owns the mutex to let go of it
        /// (the elevated hand-off). Returns true when this instance took over.
        /// </summary>
        public bool TryTakeOwnership(int timeoutMilliseconds)
        {
            if (_mutex == null)
            {
                return false;
            }

            try
            {
                _ownsMutex = _mutex.WaitOne(Math.Max(0, timeoutMilliseconds));
                IsFirstInstance = _ownsMutex;
                return _ownsMutex;
            }
            catch (System.Threading.AbandonedMutexException)
            {
                // The previous instance died while holding the mutex.
                _ownsMutex = true;
                IsFirstInstance = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Gives up ownership without disposing, so a replacement instance can start.</summary>
        public void Release()
        {
            try
            {
                if (_mutex != null && _ownsMutex)
                {
                    _mutex.ReleaseMutex();
                    _ownsMutex = false;
                    IsFirstInstance = false;
                }
            }
            catch
            {
            }
        }

        /// <summary>Tells the already running instance to bring its window to the front.</summary>
        public void SignalExisting()
        {
            try
            {
                if (_event != null)
                {
                    _event.Set();
                }
            }
            catch
            {
            }
        }

        /// <summary>Returns true once when another instance asked to be activated.</summary>
        public bool WaitForActivation()
        {
            try
            {
                return _event != null && _event.WaitOne(0);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_mutex != null)
                {
                    if (_ownsMutex)
                    {
                        try
                        {
                            _mutex.ReleaseMutex();
                        }
                        catch
                        {
                        }
                    }

                    _mutex.Dispose();
                    _mutex = null;
                }

                if (_event != null)
                {
                    _event.Dispose();
                    _event = null;
                }
            }
            catch
            {
            }
        }
    }
}
