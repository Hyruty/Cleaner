using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Cleaner
{
    /// <summary>
    /// Headless verification of everything that can be checked without touching
    /// real caches: catalog, localisation, JSON, settings, path guards, the
    /// scanner and the deleter.
    /// </summary>
    internal static class SelfTests
    {
        private sealed class Reporter
        {
            private readonly StringBuilder _log = new StringBuilder();
            private readonly List<string> _lines = new List<string>();

            public int Passed;
            public int Failed;

            public void Check(string name, Action action)
            {
                try
                {
                    action();
                    Passed++;
                    Write("[PASS] " + name);
                }
                catch (Exception ex)
                {
                    Failed++;
                    Write("[FAIL] " + name + " : " + ex.Message);
                }
            }

            public void Assert(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            }

            private void Write(string line)
            {
                _lines.Add(line);
                Console.WriteLine(line);
                _log.AppendLine(line);
            }

            public void Save(string path)
            {
                try
                {
                    File.WriteAllText(path, _log.ToString(), new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }

        public static int Run()
        {
            Reporter reporter = new Reporter();
            string reportPath = Path.Combine(AppInfo.DataRoot, "selftest.log");

            Console.WriteLine("Cleaner self-test " + AppInfo.Version + " (admin=" + Native.IsAdministrator + ")");

            reporter.Check("catalog.integrity", () =>
            {
                List<CleanupCategory> all = CleanupCatalog.All();
                reporter.Assert(all.Count >= 11, "expected at least 11 categories, got " + all.Count);
                reporter.Assert(all.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count, "category ids are not unique");
                reporter.Assert(CleanupCatalog.ById("userTemp") != null, "userTemp is missing");
                reporter.Assert(CleanupCatalog.ById("recycleBin").IsRecycleBin, "recycleBin is not flagged");
            });

            reporter.Check("catalog.localized", () =>
            {
                foreach (CleanupCategory category in CleanupCatalog.All())
                {
                    reporter.Assert(!string.IsNullOrWhiteSpace(category.Name), "missing name for " + category.Id);
                    reporter.Assert(!string.IsNullOrWhiteSpace(category.Description), "missing description for " + category.Id);
                    reporter.Assert(category.Name != "category." + category.Id + ".name", "untranslated name for " + category.Id);
                }
            });

            reporter.Check("loc.both-languages", () =>
            {
                string original = Loc.Language;
                try
                {
                    Loc.Initialize("zh-CN");
                    string chinese = CleanupCatalog.ById("userTemp").Name;
                    Loc.Initialize("en-US");
                    string english = CleanupCatalog.ById("userTemp").Name;
                    reporter.Assert(chinese != english, "zh-CN and en-US returned the same text");
                    reporter.Assert(!string.IsNullOrWhiteSpace(english), "english string is empty");
                }
                finally
                {
                    Loc.Initialize(original);
                }
            });

            reporter.Check("json.roundtrip", () =>
            {
                string json = "{ \"name\": \"cleaner\", \"count\": 3, \"flag\": true, \"list\": [1, 2, \"x\"], \"nested\": { \"a\": null } }";
                Dictionary<string, object> root = Json.AsObject(Json.Parse(json));
                reporter.Assert(root != null, "root is not an object");
                reporter.Assert(Json.GetString(root, "name", null) == "cleaner", "string value lost");
                reporter.Assert(Json.GetInt(root, "count", 0) == 3, "int value lost");
                reporter.Assert(Json.GetBool(root, "flag", false), "bool value lost");

                string serialized = Json.Serialize(root);
                Dictionary<string, object> again = Json.AsObject(Json.Parse(serialized));
                reporter.Assert(again != null && Json.GetString(again, "name", null) == "cleaner", "serialize/parse mismatch");
            });

            reporter.Check("settings.roundtrip", () =>
            {
                string originalRoot = AppInfo.DataRoot;
                string tempRoot = CreateTempFolder("settings");
                try
                {
                    AppInfo.DataRoot = tempRoot;
                    Settings settings = Settings.CreateDefault();
                    settings.Language = "en-US";
                    settings.AutoCleanEnabled = true;
                    settings.LogRetentionDays = 7;
                    settings.SetEnabled("userTemp", false);
                    reporter.Assert(settings.Save(), "settings could not be saved");
                    reporter.Assert(File.Exists(AppInfo.SettingsFile), "settings.json was not written");

                    Settings loaded = Settings.Load();
                    reporter.Assert(loaded.Language == "en-US", "language was not persisted");
                    reporter.Assert(loaded.AutoCleanEnabled, "autoCleanEnabled was not persisted");
                    reporter.Assert(loaded.LogRetentionDays == 7, "logRetentionDays was not persisted");
                    reporter.Assert(!loaded.IsEnabled("userTemp"), "category flag was not persisted");
                    reporter.Assert(loaded.IsEnabled("browserCache"), "default category flag was lost");
                }
                finally
                {
                    AppInfo.DataRoot = originalRoot;
                    TryDeleteTree(tempRoot);
                }
            });

            reporter.Check("paths.guards", () =>
            {
                string driveRoot = Path.GetPathRoot(Environment.SystemDirectory);
                reporter.Assert(PathSafety.IsDriveRoot(driveRoot), "drive root not detected");
                reporter.Assert(PathSafety.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "profile not protected");

                string tempRoot = CreateTempFolder("safety");
                try
                {
                    string inside = Path.Combine(tempRoot, "cache");
                    Directory.CreateDirectory(inside);
                    string normalized = PathSafety.EnsureSafeTarget(inside, tempRoot);
                    reporter.Assert(normalized != null && Directory.Exists(normalized), "valid target rejected");

                    ExpectThrow(() => PathSafety.EnsureSafeTarget(driveRoot, driveRoot), "drive root accepted");
                    ExpectThrow(() => PathSafety.EnsureSafeTarget(Path.Combine(tempRoot, ".."), tempRoot), "parent hop accepted");
                    ExpectThrow(() => PathSafety.EnsureSafeTarget(Path.Combine(tempRoot, "x"), Path.Combine(tempRoot, "other")), "path outside root accepted");
                }
                finally
                {
                    TryDeleteTree(tempRoot);
                }
            });

            reporter.Check("scanner.measures", () =>
            {
                string root = CreateTempFolder("scan");
                try
                {
                    Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
                    Directory.CreateDirectory(Path.Combine(root, "empty"));
                    File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
                    File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[250]);
                    File.WriteAllBytes(Path.Combine(root, "sub", "deep", "c.bin"), new byte[50]);

                    ScanStats stats = FileScanner.Measure(
                        new CleanTarget { Path = root, Kind = TargetKind.Tree, Root = Path.GetDirectoryName(root) },
                        CancellationToken.None);

                    reporter.Assert(stats.Files == 3, string.Format(
                        "expected 3 files, got {0} (folders={1}, errors={2}, bytes={3}, root={4})",
                        stats.Files, stats.Folders, stats.Errors, stats.Bytes, root));
                    reporter.Assert(stats.Bytes == 400, "expected 400 bytes, got " + stats.Bytes);
                    reporter.Assert(stats.Folders >= 3, "expected at least 3 folders, got " + stats.Folders);

                    ScanStats contents = FileScanner.Measure(
                        new CleanTarget { Path = Path.Combine(root, "sub"), Kind = TargetKind.DirectoryContents, Root = root },
                        CancellationToken.None);
                    reporter.Assert(contents.Bytes == 300, "expected 300 bytes for directory contents, got " + contents.Bytes);
                    reporter.Assert(contents.Files == 2, "expected 2 files for directory contents, got " + contents.Files);

                    // Several files directly inside one folder must all be counted.
                    string flat = Path.Combine(root, "flat");
                    Directory.CreateDirectory(flat);
                    for (int i = 0; i < 4; i++)
                    {
                        File.WriteAllBytes(Path.Combine(flat, "f" + i + ".bin"), new byte[25]);
                    }

                    ScanStats flatStats = FileScanner.Measure(
                        new CleanTarget { Path = flat, Kind = TargetKind.DirectoryContents, Root = root },
                        CancellationToken.None);
                    reporter.Assert(flatStats.Files == 4, "expected 4 files in a flat folder, got " + flatStats.Files);
                    reporter.Assert(flatStats.Bytes == 100, "expected 100 bytes in a flat folder, got " + flatStats.Bytes);
                }
                finally
                {
                    TryDeleteTree(root);
                }
            });

            reporter.Check("deleter.safe-tree", () =>
            {
                string root = CreateTempFolder("delete");
                try
                {
                    Directory.CreateDirectory(Path.Combine(root, "nested", "deep"));
                    File.WriteAllBytes(Path.Combine(root, "one.tmp"), new byte[10]);
                    File.WriteAllBytes(Path.Combine(root, "nested", "two.tmp"), new byte[20]);
                    File.WriteAllBytes(Path.Combine(root, "nested", "deep", "three.tmp"), new byte[30]);

                    string readonlyFile = Path.Combine(root, "nested", "readonly.tmp");
                    File.WriteAllBytes(readonlyFile, new byte[40]);
                    File.SetAttributes(readonlyFile, FileAttributes.ReadOnly);

                    DeleteStats stats = new DeleteStats();
                    Deleter.Delete(
                        new CleanTarget { Path = root, Kind = TargetKind.Tree, Root = Path.GetDirectoryName(root) },
                        stats,
                        CancellationToken.None);

                    reporter.Assert(!Directory.Exists(root), string.Format(
                        "tree was not removed (files={0}, skipped={1}, errors={2}, samples={3})",
                        stats.Files, stats.Skipped, stats.Errors, string.Join(" | ", stats.Samples.ToArray())));
                    reporter.Assert(stats.Files == 4, "expected 4 files deleted, got " + stats.Files);
                    reporter.Assert(stats.Bytes == 100, "expected 100 bytes, got " + stats.Bytes);
                    reporter.Assert(stats.Skipped == 0, "unexpected skips: " + string.Join(" | ", stats.Samples));
                }
                finally
                {
                    TryDeleteTree(root);
                }
            });

            reporter.Check("deleter.keeps-root", () =>
            {
                string root = CreateTempFolder("contents");
                try
                {
                    File.WriteAllBytes(Path.Combine(root, "x.tmp"), new byte[5]);
                    Directory.CreateDirectory(Path.Combine(root, "sub"));
                    File.WriteAllBytes(Path.Combine(root, "sub", "y.tmp"), new byte[7]);

                    DeleteStats stats = new DeleteStats();
                    Deleter.Delete(
                        new CleanTarget { Path = root, Kind = TargetKind.DirectoryContents, Root = Path.GetDirectoryName(root) },
                        stats,
                        CancellationToken.None);

                    reporter.Assert(Directory.Exists(root), "contents mode removed the folder itself");
                    reporter.Assert(Directory.GetFileSystemEntries(root).Length == 0, "contents mode left entries behind");
                    reporter.Assert(stats.Bytes == 12, "expected 12 bytes, got " + stats.Bytes);
                }
                finally
                {
                    TryDeleteTree(root);
                }
            });

            reporter.Check("deleter.refuses-protected", () =>
            {
                DeleteStats stats = new DeleteStats();
                string driveRoot = Path.GetPathRoot(Environment.SystemDirectory);
                ExpectThrow(
                    () => Deleter.Delete(new CleanTarget { Path = driveRoot, Kind = TargetKind.Tree, Root = driveRoot }, stats, CancellationToken.None),
                    "drive root was not refused");
            });

            reporter.Check("paths.long", () =>
            {
                string root = CreateTempFolder("long");
                try
                {
                    string deep = root;
                    while (deep.Length < 280)
                    {
                        deep = Path.Combine(deep, "segment-0123456789");
                    }

                    Directory.CreateDirectory(PathSafety.ForIo(deep));
                    File.WriteAllBytes(PathSafety.ForIo(Path.Combine(deep, "payload.bin")), new byte[128]);

                    ScanStats stats = FileScanner.Measure(
                        new CleanTarget { Path = root, Kind = TargetKind.Tree, Root = Path.GetDirectoryName(root) },
                        CancellationToken.None);
                    reporter.Assert(stats.Files == 1, "deep path file not counted, got " + stats.Files);
                    reporter.Assert(stats.Bytes == 128, "deep path bytes not counted, got " + stats.Bytes);

                    DeleteStats deleteStats = new DeleteStats();
                    Deleter.Delete(
                        new CleanTarget { Path = root, Kind = TargetKind.Tree, Root = Path.GetDirectoryName(root) },
                        deleteStats,
                        CancellationToken.None);
                    reporter.Assert(!Directory.Exists(PathSafety.ForIo(root)), "deep path tree was not removed");
                }
                finally
                {
                    TryDeleteTree(root);
                }
            });

            reporter.Check("settings.corrupt", () =>
            {
                string originalRoot = AppInfo.DataRoot;
                string tempRoot = CreateTempFolder("corrupt");
                try
                {
                    AppInfo.DataRoot = tempRoot;
                    File.WriteAllText(AppInfo.SettingsFile, "{ this is not valid json", new UTF8Encoding(false));

                    Settings loaded = Settings.Load();
                    reporter.Assert(loaded != null, "loading broken settings returned null");
                    reporter.Assert(!loaded.AutoCleanEnabled, "broken settings did not fall back to defaults");
                    reporter.Assert(loaded.Categories.Count == CleanupCatalog.All().Count, "default categories were not restored");
                }
                finally
                {
                    AppInfo.DataRoot = originalRoot;
                    TryDeleteTree(tempRoot);
                }
            });

            reporter.Check("engine.order", () =>
            {
                List<CleanupCategory> ordered = CleanupEngine.Order(new[] { "recycleBin", "userTemp", "does-not-exist" });
                reporter.Assert(ordered.Count == 2, "order should drop unknown ids, got " + ordered.Count);
                reporter.Assert(ordered[0].Id == "userTemp", "catalog order not preserved");
                reporter.Assert(ordered[1].Id == "recycleBin", "recycle bin should stay last");
            });

            reporter.Check("args.quoting", () =>
            {
                // Golden expectations verified against the .NET Framework
                // command line parser: backslashes are only special in front of
                // a quote, and a trailing backslash must be doubled or the
                // closing quote would be swallowed.
                reporter.Assert(Elevation.Quote("plain-value") == "plain-value", "simple value should not be quoted");
                reporter.Assert(Elevation.Quote(@"C:\Program Files\Cleaner") == "\"C:\\Program Files\\Cleaner\"", "space should force quoting");
                reporter.Assert(Elevation.Quote(@"C:\dir with space\") == "\"C:\\dir with space\\\\\"", "trailing backslash must be doubled");
                reporter.Assert(Elevation.Quote("quote\"inside") == "\"quote\\\"inside\"", "embedded quote must be escaped");
                reporter.Assert(Elevation.Quote(string.Empty) == "\"\"", "empty value must be quoted");

                string built = Elevation.Quote(@"C:\some dir\") + " " + Elevation.Quote("中文 参数");
                reporter.Assert(built.IndexOf("  ", StringComparison.Ordinal) < 0, "quoted arguments must stay separated: " + built);
            });

            reporter.Check("singleinstance.handoff", () =>
            {
                string suffix = ".selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                using (SingleInstance first = new SingleInstance(suffix))
                {
                    reporter.Assert(first.Acquire(), "the first instance could not acquire the mutex");

                    bool taken = false;
                    Exception workerError = null;
                    System.Threading.Thread worker = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            using (SingleInstance second = new SingleInstance(suffix))
                            {
                                if (second.Acquire())
                                {
                                    taken = true;
                                    return;
                                }

                                for (int attempt = 0; attempt < 100 && !taken; attempt++)
                                {
                                    if (second.TryTakeOwnership(50))
                                    {
                                        taken = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            workerError = ex;
                        }
                    });

                    worker.IsBackground = true;
                    worker.Start();
                    System.Threading.Thread.Sleep(400);

                    reporter.Assert(!taken, "the second instance took the mutex while the first one held it");

                    first.Release();
                    worker.Join(8000);

                    reporter.Assert(workerError == null, "the waiting instance failed: " + (workerError == null ? string.Empty : workerError.Message));
                    reporter.Assert(taken, "the hand-off did not complete after the first instance released the mutex");
                }
            });

            reporter.Check("autoclean.task-xml", () =>
            {
                string error;
                reporter.Assert(AutoCleanTask.ValidateXml(out error), "task XML is invalid: " + error);
                string xml = AutoCleanTask.BuildXml();
                reporter.Assert(xml.Contains("--scheduled"), "task action does not pass --scheduled");
                reporter.Assert(xml.Contains("Kernel-General"), "task has no shutdown trigger");
                reporter.Assert(xml.Contains("LogonTrigger"), "task has no logon retry trigger");
            });

            reporter.Check("recyclebin.query", () =>
            {
                RecycleBinStatus status = RecycleBin.Query();
                reporter.Assert(status != null, "query returned null");
                if (status.Known)
                {
                    reporter.Assert(status.Items >= 0, "item count is negative");
                    reporter.Assert(status.Bytes >= 0, "byte count is negative");
                }
            });

            reporter.Check("format.bytes", () =>
            {
                reporter.Assert(Fmt.Bytes(512).EndsWith("B", StringComparison.Ordinal), "byte formatting lost its unit");
                reporter.Assert(Fmt.Bytes(1536).Contains("KB"), "kilobyte formatting failed: " + Fmt.Bytes(1536));
                reporter.Assert(Fmt.Bytes(5L * 1024 * 1024 * 1024).Contains("GB"), "gigabyte formatting failed");
            });

            reporter.Check("catalog.targets-exist", () =>
            {
                UserContext user = UserContext.Current(Native.IsAdministrator, null, null, null);
                foreach (CleanupCategory category in CleanupCatalog.All())
                {
                    if (category.IsRecycleBin)
                    {
                        continue;
                    }

                    List<CleanTarget> targets = CleanupCatalog.ResolveTargets(category, user);
                    foreach (CleanTarget target in targets)
                    {
                        bool exists = target.Kind == TargetKind.File
                            ? PathSafety.FileExists(target.Path)
                            : PathSafety.DirectoryExists(target.Path);
                        reporter.Assert(exists, "resolved target does not exist: " + target.Path);
                        reporter.Assert(!PathSafety.IsProtected(target.Path), "resolved target is protected: " + target.Path);
                        reporter.Assert(PathSafety.IsUnderOrEqual(target.Path, target.Root), "target outside its root: " + target.Path);
                    }
                }
            });

            reporter.Check("engine.scan-shape", () =>
            {
                UserContext user = UserContext.Current(Native.IsAdministrator, null, null, null);
                CleanupEngine engine = new CleanupEngine(user, Native.IsAdministrator);
                CategoryEstimate estimate = engine.Estimate(CleanupCatalog.ById("recycleBin"), CancellationToken.None);
                reporter.Assert(estimate != null, "estimate is null");
                reporter.Assert(estimate.Id == "recycleBin", "estimate id mismatch");
            });

            Console.WriteLine();
            reporter.Save(reportPath);

            if (reporter.Failed > 0)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "SELF_TEST_FAILED passed={0} failed={1}", reporter.Passed, reporter.Failed));
                return 1;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "SELF_TEST_OK passed={0}", reporter.Passed));
            return 0;
        }

        private static void ExpectThrow(Action action, string message)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(message + " (unexpected exception: " + ex.GetType().Name + ")");
            }

            throw new InvalidOperationException(message);
        }

        internal static string CreateTempFolder(string tag)
        {
            string name = "cleaner-selftest-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // %TEMP% is the natural place, but locked down machines can deny
            // creating folders there; fall back to folders the app already owns.
            string[] roots =
            {
                Path.GetTempPath(),
                Path.Combine(AppInfo.DataRoot, "selftest"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cleaner", "selftest")
            };

            Exception lastError = null;
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                try
                {
                    string path = Path.Combine(root, name);
                    Directory.CreateDirectory(path);
                    return path;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                "Could not create a temporary test folder: " + (lastError == null ? "no writable location" : lastError.Message));
        }

        internal static void TryDeleteTree(string path)
        {
            try
            {
                if (Directory.Exists(PathSafety.ForIo(path)))
                {
                    Directory.Delete(PathSafety.ForIo(path), true);
                }
            }
            catch
            {
            }
        }
    }
}
