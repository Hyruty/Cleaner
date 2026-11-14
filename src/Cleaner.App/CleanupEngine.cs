using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cleaner
{
    internal enum CategoryState
    {
        Ok,
        NeedsAdmin,
        BrowserRunning,
        NoTargets,
        Error
    }

    internal sealed class CategoryEstimate
    {
        public string Id;
        public long Bytes;
        public long Files;
        public long Folders;
        public int Errors;
        public long Items = -1;
        public bool NeedsRestart;
        public CategoryState State = CategoryState.Ok;
        public string Detail;
        public TimeSpan Elapsed;
        public int TargetCount;
    }

    internal sealed class ScanProgress
    {
        public int Completed;
        public int Total;
        public string CurrentId;
        public CategoryEstimate Estimate;
    }

    internal sealed class CleanupProgress
    {
        public int Completed;
        public int Total;
        public string CurrentId;
    }

    internal sealed class CleanupResult
    {
        public long Bytes;
        public long Files;
        public long Folders;
        public long Skipped;
        public long Errors;
        public bool RecycleBinCleared;
        public bool RecycleBinVerified;
        public long RecycleBefore = -1;
        public long RecycleAfter = -1;
        public string RecycleError;
        public TimeSpan Duration;
        public readonly List<string> SkippedCategories = new List<string>();
        public readonly List<string> SkipSamples = new List<string>();
        public readonly List<KeyValuePair<string, long>> PerCategory = new List<KeyValuePair<string, long>>();
    }

    /// <summary>
    /// Orchestrates scanning and cleaning. Scanning runs categories in parallel
    /// on worker threads; cleaning runs strictly one category at a time.
    /// </summary>
    internal sealed class CleanupEngine
    {
        private readonly UserContext _user;
        private readonly bool _isAdmin;

        public CleanupEngine(UserContext user, bool isAdmin)
        {
            _user = user;
            _isAdmin = isAdmin;
        }

        public static List<CleanupCategory> Order(IEnumerable<string> ids)
        {
            HashSet<string> wanted = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return CleanupCatalog.All().Where(c => wanted.Contains(c.Id)).ToList();
        }

        public string DescribeUserContext()
        {
            return string.Join("; ", _user.Describe());
        }

        // ---------------------------------------------------------------- scan
        public async Task<List<CategoryEstimate>> ScanAsync(IEnumerable<string> ids, IProgress<ScanProgress> progress, CancellationToken cancellationToken)
        {
            List<CleanupCategory> categories = Order(ids);
            CategoryEstimate[] results = new CategoryEstimate[categories.Count];
            int completed = 0;
            int total = categories.Count;

            using (SemaphoreSlim gate = new SemaphoreSlim(Math.Max(1, Math.Min(4, Environment.ProcessorCount))))
            {
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < categories.Count; i++)
                {
                    int index = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            CategoryEstimate estimate = Estimate(categories[index], cancellationToken);
                            results[index] = estimate;

                            int done = Interlocked.Increment(ref completed);
                            if (progress != null)
                            {
                                progress.Report(new ScanProgress
                                {
                                    Completed = done,
                                    Total = total,
                                    CurrentId = categories[index].Id,
                                    Estimate = estimate
                                });
                            }
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return results.Where(r => r != null).ToList();
        }

        public CategoryEstimate Estimate(CleanupCategory category, CancellationToken cancellationToken)
        {
            Stopwatch watch = Stopwatch.StartNew();
            CategoryEstimate estimate = new CategoryEstimate { Id = category.Id };

            if (category.IsRecycleBin)
            {
                RecycleBinStatus status = RecycleBin.Query();
                estimate.Items = status.Known ? status.Items : -1;
                estimate.Bytes = status.Bytes;
                estimate.State = CategoryState.Ok;
                if (!status.Known)
                {
                    estimate.Detail = "unknown";
                }

                estimate.Elapsed = watch.Elapsed;
                return estimate;
            }

            if (category.RequiresAdmin && !_isAdmin)
            {
                estimate.State = CategoryState.NeedsAdmin;
                estimate.Elapsed = watch.Elapsed;
                return estimate;
            }

            if (string.Equals(category.Id, "browserCache", StringComparison.OrdinalIgnoreCase))
            {
                string[] running = CleanupCatalog.RunningBrowsers();
                if (running.Length > 0)
                {
                    estimate.State = CategoryState.BrowserRunning;
                    estimate.Detail = string.Join(", ", running);
                    estimate.Elapsed = watch.Elapsed;
                    return estimate;
                }
            }

            List<CleanTarget> targets = CleanupCatalog.ResolveTargets(category, _user);
            estimate.TargetCount = targets.Count;

            if (targets.Count == 0)
            {
                estimate.State = CategoryState.NoTargets;
                estimate.Elapsed = watch.Elapsed;
                return estimate;
            }

            foreach (CleanTarget target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanStats stats = FileScanner.Measure(target, cancellationToken);
                estimate.Bytes += stats.Bytes;
                estimate.Files += stats.Files;
                estimate.Folders += stats.Folders;
                estimate.Errors += stats.Errors;
                estimate.NeedsRestart |= target.NeedsRestart;
            }

            if (estimate.Errors > 0 && estimate.Files == 0 && estimate.Bytes == 0)
            {
                estimate.State = CategoryState.Error;
                estimate.Detail = string.Format("unreadable ({0})", estimate.Errors);
            }

            estimate.Elapsed = watch.Elapsed;
            return estimate;
        }

        // --------------------------------------------------------------- clean
        public Task<CleanupResult> CleanAsync(IEnumerable<string> ids, IProgress<CleanupProgress> progress, CancellationToken cancellationToken)
        {
            return Task.Run(() => Clean(ids, progress, cancellationToken), cancellationToken);
        }

        public CleanupResult Clean(IEnumerable<string> ids, IProgress<CleanupProgress> progress, CancellationToken cancellationToken)
        {
            Stopwatch watch = Stopwatch.StartNew();
            CleanupResult result = new CleanupResult();
            List<CleanupCategory> categories = Order(ids);
            int total = categories.Count;
            int completed = 0;

            foreach (CleanupCategory category in categories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (progress != null)
                {
                    progress.Report(new CleanupProgress { Completed = completed, Total = total, CurrentId = category.Id });
                }

                if (category.IsRecycleBin)
                {
                    RecycleBinResult bin = RecycleBin.Empty(TimeSpan.FromSeconds(8), _user, _isAdmin);
                    result.RecycleBinCleared = bin.Cleared;
                    result.RecycleBinVerified = bin.Verified;
                    result.RecycleBefore = bin.Before;
                    result.RecycleAfter = bin.After;
                    result.RecycleError = bin.Error;
                    if (!bin.Cleared)
                    {
                        result.SkippedCategories.Add(category.Name + " (" + Loc.T("result.reasonError") + ")");
                    }

                    completed++;
                    continue;
                }

                if (category.RequiresAdmin && !_isAdmin)
                {
                    result.SkippedCategories.Add(category.Name + " (" + Loc.T("result.reasonAdmin") + ")");
                    completed++;
                    continue;
                }

                if (string.Equals(category.Id, "browserCache", StringComparison.OrdinalIgnoreCase))
                {
                    string[] running = CleanupCatalog.RunningBrowsers();
                    if (running.Length > 0)
                    {
                        result.SkippedCategories.Add(category.Name + " (" + Loc.T("result.reasonBrowser") + ")");
                        completed++;
                        continue;
                    }
                }

                List<CleanTarget> targets = CleanupCatalog.ResolveTargets(category, _user);
                DeleteStats stats = new DeleteStats();

                foreach (CleanTarget target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        Deleter.Delete(target, stats, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        stats.Fail(target.Path, ex.Message);
                        Log.Exception("Cleanup target failed: " + target.Path, ex);
                    }
                }

                result.Bytes += stats.Bytes;
                result.Files += stats.Files;
                result.Folders += stats.Folders;
                result.Skipped += stats.Skipped;
                result.Errors += stats.Errors;
                result.PerCategory.Add(new KeyValuePair<string, long>(category.Name, stats.Bytes));

                if (stats.Samples.Count > 0)
                {
                    foreach (string sample in stats.Samples)
                    {
                        if (result.SkipSamples.Count >= 200)
                        {
                            break;
                        }

                        result.SkipSamples.Add(sample);
                    }
                }

                Log.Info("Category {0}: bytes={1}; files={2}; folders={3}; skipped={4}; errors={5}; targets={6}",
                    category.Id, stats.Bytes, stats.Files, stats.Folders, stats.Skipped, stats.Errors, targets.Count);

                completed++;
            }

            if (progress != null)
            {
                progress.Report(new CleanupProgress { Completed = total, Total = total, CurrentId = null });
            }

            result.Duration = watch.Elapsed;

            Log.Info("Cleanup finished: bytes={0}; files={1}; folders={2}; skipped={3}; errors={4}; categories={5}; elapsed={6}",
                result.Bytes, result.Files, result.Folders, result.Skipped, result.Errors, total, result.Duration);

            if (result.SkipSamples.Count > 0)
            {
                Log.Warn("Skipped samples: {0}", string.Join(" | ", result.SkipSamples.Take(50)));
            }

            return result;
        }

        /// <summary>Human readable target list used by --dry-run and self-tests.</summary>
        public List<string> DescribeTargets(string categoryId)
        {
            List<string> lines = new List<string>();
            CleanupCategory category = CleanupCatalog.ById(categoryId);
            if (category == null)
            {
                return lines;
            }

            CategoryEstimate estimate = Estimate(category, CancellationToken.None);
            List<CleanTarget> targets = CleanupCatalog.ResolveTargets(category, _user);

            lines.Add(string.Format("[{0}] {1} state={2} targets={3} bytes={4} files={5}",
                category.Id,
                category.Name,
                estimate.State,
                targets.Count,
                estimate.Bytes,
                estimate.Files));

            foreach (CleanTarget target in targets)
            {
                lines.Add(string.Format("    {0,-20} {1}", target.Kind, target.Path));
            }

            return lines;
        }
    }
}
