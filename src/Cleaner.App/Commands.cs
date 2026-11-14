using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Cleaner
{
    /// <summary>Command line modes: dry run, scheduled cleanup and headless clean.</summary>
    internal static class Commands
    {
        public static int DryRun(Options options, Settings settings)
        {
            UserContext user = ResolveUser(options);
            CleanupEngine engine = new CleanupEngine(user, Native.IsAdministrator);
            List<CleanupCategory> categories = SelectCategories(options, settings, allWhenEmpty: true);

            Console.WriteLine("{0} {1} dry run", AppInfo.Product, AppInfo.Version);
            Console.WriteLine("user: {0}", engine.DescribeUserContext());
            Console.WriteLine();

            long total = 0;
            foreach (CleanupCategory category in categories)
            {
                foreach (string line in engine.DescribeTargets(category.Id))
                {
                    Console.WriteLine(line);
                }

                CategoryEstimate estimate = engine.Estimate(category, CancellationToken.None);
                if (estimate.State == CategoryState.Ok)
                {
                    total += estimate.Bytes;
                }
            }

            Console.WriteLine();
            Console.WriteLine("DRY-RUN total {0} ({1} bytes) — nothing was deleted.", Fmt.Bytes(total), total);
            return 0;
        }

        public static int ScheduledCleanup(Options options, Settings settings)
        {
            if (!settings.AutoCleanEnabled)
            {
                Log.Info("Scheduled cleanup skipped: automatic cleanup is disabled.");
                Console.WriteLine("Skipped: automatic cleanup is disabled.");
                return 0;
            }

            using (Mutex mutex = new Mutex(false, @"Local\Cleaner.AutoClean"))
            {
                bool hasLock = false;
                try
                {
                    try
                    {
                        hasLock = mutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        hasLock = true;
                    }

                    if (!hasLock)
                    {
                        Log.Warn("Scheduled cleanup skipped: another cleanup is running.");
                        Console.WriteLine("Skipped: another cleanup is already running.");
                        return 0;
                    }

                    UserContext user = ResolveUser(options);
                    CleanupEngine engine = new CleanupEngine(user, Native.IsAdministrator);
                    List<CleanupCategory> categories = SelectCategories(options, settings, allWhenEmpty: false);

                    List<string> ids = categories.Where(c => settings.IsEnabled(c.Id) || options != null && !string.IsNullOrEmpty(options.Categories))
                        .Select(c => c.Id)
                        .ToList();

                    if (ids.Count == 0)
                    {
                        Log.Info("Scheduled cleanup skipped: no categories are enabled.");
                        Console.WriteLine("Skipped: no cleanup categories are enabled.");
                        return 0;
                    }

                    Log.Info("Scheduled cleanup starting with categories: {0}", string.Join(", ", ids));
                    CleanupResult result = engine.Clean(ids, null, CancellationToken.None);
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "Scheduled cleanup done: freed={0}; files={1}; folders={2}; skipped={3}; errors={4}",
                        Fmt.Bytes(result.Bytes), result.Files, result.Folders, result.Skipped, result.Errors));
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Exception("Scheduled cleanup failed", ex);
                    Console.Error.WriteLine("Scheduled cleanup failed: " + ex.Message);
                    return 1;
                }
                finally
                {
                    if (hasLock)
                    {
                        try
                        {
                            mutex.ReleaseMutex();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        public static int UninstallAuto(Settings settings)
        {
            string error;
            if (!AutoCleanTask.Uninstall(out error))
            {
                Console.Error.WriteLine("Failed to remove the scheduled task: " + (error ?? string.Empty));
                return 1;
            }

            settings.AutoCleanEnabled = false;
            settings.Save();
            Console.WriteLine("Automatic cleanup task removed.");
            return 0;
        }

        public static int HeadlessClean(Options options, Settings settings)
        {
            UserContext user = ResolveUser(options);
            CleanupEngine engine = new CleanupEngine(user, Native.IsAdministrator);

            List<string> ids = (options.CleanHeadless ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .ToList();

            if (ids.Count == 0)
            {
                ids = CleanupCatalog.All().Where(c => settings.IsEnabled(c.Id)).Select(c => c.Id).ToList();
            }

            if (ids.Count == 0)
            {
                Console.WriteLine("No categories selected.");
                return 2;
            }

            Console.WriteLine("{0} {1} cleaning: {2}", AppInfo.Product, AppInfo.Version, string.Join(", ", ids));
            CleanupResult result = engine.Clean(ids, null, CancellationToken.None);

            Console.WriteLine();
            Console.WriteLine("Freed:   {0}", Fmt.Bytes(result.Bytes));
            Console.WriteLine("Files:   {0}", result.Files);
            Console.WriteLine("Folders: {0}", result.Folders);
            Console.WriteLine("Skipped: {0}", result.Skipped);
            Console.WriteLine("Errors:  {0}", result.Errors);
            Console.WriteLine("Log:     {0}", Log.CurrentFile ?? "(none)");

            foreach (KeyValuePair<string, long> entry in result.PerCategory)
            {
                Console.WriteLine("  {0,-28} {1}", entry.Key, Fmt.Bytes(entry.Value));
            }

            if (result.SkippedCategories.Count > 0)
            {
                Console.WriteLine("Skipped categories: {0}", string.Join(", ", result.SkippedCategories));
            }

            return 0;
        }

        private static UserContext ResolveUser(Options options)
        {
            return UserContext.Current(
                Native.IsAdministrator,
                options == null ? null : options.TargetUserSid,
                options == null ? null : options.TargetLocalAppData,
                options == null ? null : options.TargetTemp);
        }

        private static List<CleanupCategory> SelectCategories(Options options, Settings settings, bool allWhenEmpty)
        {
            if (options != null && !string.IsNullOrWhiteSpace(options.Categories))
            {
                HashSet<string> wanted = new HashSet<string>(
                    options.Categories.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                return CleanupCatalog.All().Where(c => wanted.Contains(c.Id)).ToList();
            }

            if (allWhenEmpty)
            {
                return CleanupCatalog.All();
            }

            return CleanupCatalog.All().Where(c => settings.IsEnabled(c.Id)).ToList();
        }
    }
}
