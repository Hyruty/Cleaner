using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Cleaner
{
    internal enum TargetKind
    {
        /// <summary>Delete the children of a folder and keep the folder itself.</summary>
        DirectoryContents,

        /// <summary>Delete a single file.</summary>
        File,

        /// <summary>Delete the folder and everything below it.</summary>
        Tree
    }

    internal sealed class CleanTarget
    {
        public string Path;
        public TargetKind Kind;

        /// <summary>Folder the path must stay below; re-validated before deletion.</summary>
        public string Root;

        public bool NeedsRestart;

        public override string ToString()
        {
            return Path;
        }
    }

    internal sealed class CleanupCategory
    {
        public string Id;
        public bool RequiresAdmin;
        public bool DefaultEnabled;
        public bool IsRecycleBin;

        public string Name
        {
            get { return Loc.T("category." + Id + ".name"); }
        }

        public string Description
        {
            get { return Loc.T("category." + Id + ".desc"); }
        }
    }

    /// <summary>
    /// The complete allow-list of cleanup locations. Nothing outside these
    /// definitions can ever be scanned or deleted.
    /// </summary>
    internal static class CleanupCatalog
    {
        private static readonly string[] ChromiumCacheFolders =
        {
            "Cache",
            "Code Cache",
            "GPUCache",
            "DawnCache",
            "DawnGraphiteCache",
            "DawnWebGPUCache",
            "GrShaderCache",
            "ShaderCache"
        };

        private static readonly string[] ChromiumUserDataRoots =
        {
            @"Microsoft\Edge\User Data",
            @"Google\Chrome\User Data",
            @"BraveSoftware\Brave-Browser\User Data",
            @"Vivaldi\User Data",
            @"Chromium\User Data"
        };

        private static readonly string[] OperaCacheRoots =
        {
            @"Opera Software\Opera Stable",
            @"Opera Software\Opera GX Stable"
        };

        private static readonly string[] DevCacheFolders =
        {
            "npm-cache",
            @"pip\Cache",
            @"Yarn\Cache",
            @"electron\Cache",
            @"NuGet\v3-cache",
            @"NuGet\plugins-cache"
        };

        private static List<CleanupCategory> _all;

        public static List<CleanupCategory> All()
        {
            if (_all == null)
            {
                _all = new List<CleanupCategory>
                {
                    Category("userTemp", admin: false, defaultOn: true),
                    Category("browserCache", admin: false, defaultOn: true),
                    Category("crashDumps", admin: false, defaultOn: true),
                    Category("shaderCache", admin: false, defaultOn: true),
                    Category("thumbnails", admin: false, defaultOn: false),
                    Category("devCaches", admin: false, defaultOn: false),
                    Category("systemTemp", admin: true, defaultOn: false),
                    Category("errorReports", admin: true, defaultOn: false),
                    Category("windowsUpdateCache", admin: true, defaultOn: false),
                    Category("upgradeLeftovers", admin: true, defaultOn: false),
                    new CleanupCategory { Id = "recycleBin", RequiresAdmin = false, DefaultEnabled = false, IsRecycleBin = true }
                };
            }

            return _all;
        }

        private static CleanupCategory Category(string id, bool admin, bool defaultOn)
        {
            return new CleanupCategory { Id = id, RequiresAdmin = admin, DefaultEnabled = defaultOn };
        }

        public static CleanupCategory ById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (CleanupCategory category in All())
            {
                if (string.Equals(category.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the existing folders/files that belong to a category. Paths
        /// are only returned when they exist right now.
        /// </summary>
        public static List<CleanTarget> ResolveTargets(CleanupCategory category, UserContext user)
        {
            List<CleanTarget> targets = new List<CleanTarget>();
            if (category == null || user == null || category.IsRecycleBin)
            {
                return targets;
            }

            string localAppData = user.LocalAppData;
            string roaming = user.RoamingAppData;
            string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            switch (category.Id)
            {
                case "userTemp":
                {
                    string root = localAppData;
                    if (!string.IsNullOrEmpty(user.Temp) && !PathSafety.IsUnderOrEqual(user.Temp, localAppData))
                    {
                        root = Path.GetDirectoryName(user.Temp);
                    }

                    AddContents(targets, user.Temp, root);
                    if (!string.IsNullOrEmpty(localAppData))
                    {
                        AddContents(targets, Path.Combine(localAppData, "Temp"), localAppData);
                    }

                    break;
                }

                case "browserCache":
                {
                    if (!string.IsNullOrEmpty(localAppData))
                    {
                        foreach (string relative in ChromiumUserDataRoots)
                        {
                            string userDataRoot = Path.Combine(localAppData, relative);
                            if (!PathSafety.DirectoryExists(userDataRoot))
                            {
                                continue;
                            }

                            foreach (string profile in SafeGetDirectories(userDataRoot))
                            {
                                foreach (string cacheName in ChromiumCacheFolders)
                                {
                                    AddContents(targets, Path.Combine(profile, cacheName), userDataRoot);
                                }
                            }
                        }

                        foreach (string relative in OperaCacheRoots)
                        {
                            string profileRoot = Path.Combine(localAppData, relative);
                            if (!PathSafety.DirectoryExists(profileRoot))
                            {
                                continue;
                            }

                            foreach (string cacheName in ChromiumCacheFolders)
                            {
                                AddContents(targets, Path.Combine(profileRoot, cacheName), profileRoot);
                            }
                        }

                        string mozillaProfiles = Path.Combine(localAppData, @"Mozilla\Firefox\Profiles");
                        foreach (string profile in SafeGetDirectories(mozillaProfiles))
                        {
                            AddContents(targets, Path.Combine(profile, "cache2"), mozillaProfiles);
                            AddContents(targets, Path.Combine(profile, "startupCache"), mozillaProfiles);
                        }
                    }

                    break;
                }

                case "crashDumps":
                {
                    if (!string.IsNullOrEmpty(localAppData))
                    {
                        AddContents(targets, Path.Combine(localAppData, "CrashDumps"), localAppData);
                        AddContents(targets, Path.Combine(localAppData, @"Microsoft\Windows\WER\ReportArchive"), localAppData);
                        AddContents(targets, Path.Combine(localAppData, @"Microsoft\Windows\WER\ReportQueue"), localAppData);
                        AddContents(targets, Path.Combine(localAppData, @"Microsoft\Windows\WER\Temp"), localAppData);

                        if (!string.IsNullOrEmpty(roaming))
                        {
                            AddContents(targets, Path.Combine(roaming, @"Microsoft\Windows\WER\ReportArchive"), roaming);
                            AddContents(targets, Path.Combine(roaming, @"Microsoft\Windows\WER\ReportQueue"), roaming);
                        }
                    }

                    break;
                }

                case "shaderCache":
                {
                    if (!string.IsNullOrEmpty(localAppData))
                    {
                        string[] shaderPaths =
                        {
                            "D3DSCache",
                            @"NVIDIA\DXCache",
                            @"NVIDIA\GLCache",
                            @"NVIDIA\ComputeCache",
                            @"AMD\DxCache",
                            @"AMD\DxcCache",
                            @"AMD\GLCache",
                            @"Intel\ShaderCache"
                        };

                        foreach (string relative in shaderPaths)
                        {
                            AddContents(targets, Path.Combine(localAppData, relative), localAppData);
                        }
                    }

                    break;
                }

                case "thumbnails":
                {
                    if (!string.IsNullOrEmpty(localAppData))
                    {
                        string explorer = Path.Combine(localAppData, @"Microsoft\Windows\Explorer");
                        if (PathSafety.DirectoryExists(explorer))
                        {
                            AddFiles(targets, explorer, "thumbcache_*.db", localAppData, needsRestart: true);
                            AddFiles(targets, explorer, "iconcache_*.db", localAppData, needsRestart: true);
                        }
                    }

                    break;
                }

                case "devCaches":
                {
                    if (!string.IsNullOrEmpty(localAppData))
                    {
                        foreach (string relative in DevCacheFolders)
                        {
                            AddContents(targets, Path.Combine(localAppData, relative), localAppData);
                        }
                    }

                    break;
                }

                case "systemTemp":
                {
                    if (!string.IsNullOrEmpty(systemRoot))
                    {
                        AddContents(targets, Path.Combine(systemRoot, "Temp"), systemRoot);
                        AddContents(targets, Path.Combine(systemRoot, "SystemTemp"), systemRoot);
                    }

                    break;
                }

                case "errorReports":
                {
                    if (!string.IsNullOrEmpty(programData))
                    {
                        AddContents(targets, Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"), programData);
                        AddContents(targets, Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue"), programData);
                        AddContents(targets, Path.Combine(programData, @"Microsoft\Windows\WER\Temp"), programData);
                    }

                    if (!string.IsNullOrEmpty(systemRoot))
                    {
                        AddContents(targets, Path.Combine(systemRoot, "LiveKernelReports"), systemRoot);
                        AddContents(targets, Path.Combine(systemRoot, "Minidump"), systemRoot);
                        AddFile(targets, Path.Combine(systemRoot, "MEMORY.DMP"), systemRoot);
                    }

                    break;
                }

                case "windowsUpdateCache":
                {
                    if (!string.IsNullOrEmpty(systemRoot))
                    {
                        AddContents(targets, Path.Combine(systemRoot, @"SoftwareDistribution\Download"), systemRoot);
                        AddContents(targets, Path.Combine(systemRoot, @"SoftwareDistribution\DeliveryOptimization"), systemRoot);
                    }

                    break;
                }

                case "upgradeLeftovers":
                {
                    string drive = PathSafety.SystemDriveRoot;
                    AddTree(targets, Path.Combine(drive, "Windows.old"), drive);
                    AddTree(targets, Path.Combine(drive, "$Windows.~BT"), drive);
                    AddTree(targets, Path.Combine(drive, "$Windows.~WS"), drive);
                    AddTree(targets, Path.Combine(drive, "$GetCurrent"), drive);
                    break;
                }
            }

            return targets;
        }

        public static string[] RunningBrowsers()
        {
            string[] names = { "msedge", "chrome", "brave", "vivaldi", "opera", "firefox", "chromium" };
            List<string> running = new List<string>();

            foreach (string name in names)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    if (processes.Length > 0)
                    {
                        running.Add(name);
                        foreach (Process process in processes)
                        {
                            try
                            {
                                process.Dispose();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return running.ToArray();
        }

        private static IEnumerable<string> SafeGetDirectories(string path)
        {
            if (string.IsNullOrEmpty(path) || !PathSafety.DirectoryExists(path))
            {
                return new string[0];
            }

            try
            {
                return Directory.GetDirectories(PathSafety.ForIo(path));
            }
            catch
            {
                return new string[0];
            }
        }

        private static void AddContents(List<CleanTarget> targets, string path, string root, bool needsRestart = false)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root) || !PathSafety.DirectoryExists(path))
            {
                return;
            }

            Add(targets, path, TargetKind.DirectoryContents, root, needsRestart);
        }

        private static void AddFile(List<CleanTarget> targets, string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root) || !PathSafety.FileExists(path))
            {
                return;
            }

            Add(targets, path, TargetKind.File, root, false);
        }

        private static void AddTree(List<CleanTarget> targets, string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root) || !PathSafety.DirectoryExists(path))
            {
                return;
            }

            Add(targets, path, TargetKind.Tree, root, false);
        }

        private static void Add(List<CleanTarget> targets, string path, TargetKind kind, string root, bool needsRestart)
        {
            string normalized = PathSafety.Normalize(path);
            if (normalized == null)
            {
                return;
            }

            foreach (CleanTarget existing in targets)
            {
                if (string.Equals(existing.Path, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            targets.Add(new CleanTarget
            {
                Path = normalized,
                Kind = kind,
                Root = PathSafety.Normalize(root),
                NeedsRestart = needsRestart
            });
        }

        private static void AddFiles(List<CleanTarget> targets, string folder, string pattern, string root, bool needsRestart)
        {
            try
            {
                foreach (string file in Directory.GetFiles(PathSafety.ForIo(folder), pattern))
                {
                    Add(targets, file, TargetKind.File, root, needsRestart);
                }
            }
            catch
            {
            }
        }
    }
}
