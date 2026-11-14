using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cleaner
{
    /// <summary>
    /// Two built-in languages (Simplified Chinese and English) compiled into the
    /// executable so the program stays a single file. An optional
    /// <c>lang.&lt;culture&gt;.json</c> next to the exe or in the data folder can
    /// override or extend any key.
    /// </summary>
    internal static class Loc
    {
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _language = "zh-CN";

        public static string Language
        {
            get { return _language; }
        }

        public static bool IsChinese
        {
            get { return _language.StartsWith("zh", StringComparison.OrdinalIgnoreCase); }
        }

        static Loc()
        {
            Add(app: "app.title", zh: "清理 C 盘", en: "Clean up drive C");
            Add("app.subtitle", "安全清理缓存、临时文件与系统垃圾", "Safely clear caches, temp files and system junk");
            Add("app.version", "版本 {0}", "Version {0}");
            Add("app.shortName", "C 盘清理助手", "C Drive Cleaner");

            Add("badge.admin", "管理员", "Administrator");
            Add("badge.user", "标准用户", "Standard user");

            Add("disk.title", "系统盘 (C:)", "System drive (C:)");
            Add("disk.free", "可用 {0}", "{0} free");
            Add("disk.usage", "已用 {0} / 共 {1}", "Used {0} of {1}");
            Add("disk.loading", "读取中…", "Reading…");
            Add("disk.unavailable", "无法读取磁盘信息", "Disk information unavailable");
            Add("disk.selected", "已选 {0} 项，预计可释放 {1}", "{0} selected · about {1} reclaimable");
            Add("disk.selectedNone", "尚未选择清理项目", "No cleanup items selected");

            Add("col.item", "清理项目", "Item");
            Add("col.size", "可释放", "Reclaimable");
            Add("col.desc", "说明", "Description");

            Add("list.notScanned", "未扫描", "Not scanned");
            Add("list.scanning", "扫描中…", "Scanning…");
            Add("list.needsAdmin", "需要管理员", "Needs admin");
            Add("list.browser", "请先关闭浏览器", "Close browsers first");
            Add("list.noCache", "未发现缓存", "Nothing to clean");
            Add("list.recycle", "回收站 {0} 项", "{0} item(s)");
            Add("list.recycleUnknown", "无法统计", "Unknown");

            Add("btn.scan", "重新扫描", "Rescan");
            Add("btn.clean", "立即清理", "Clean now");
            Add("btn.cancel", "取消", "Cancel");
            Add("btn.elevate", "以管理员身份重启", "Restart as admin");
            Add("btn.more", "更多", "More");
            Add("btn.close", "关闭", "Close");
            Add("btn.ok", "确定", "OK");
            Add("btn.yes", "继续", "Continue");
            Add("btn.no", "取消", "Cancel");
            Add("btn.openFolder", "打开目录", "Open folder");

            Add("status.ready", "就绪", "Ready");
            Add("status.scanning", "正在扫描 {0}/{1}：{2}", "Scanning {0}/{1}: {2}");
            Add("status.scanDone", "扫描完成，预计可释放 {0}", "Scan complete · about {0} reclaimable");
            Add("status.cleaning", "正在清理：{0}", "Cleaning: {0}");
            Add("status.cleanDone", "清理完成，已释放 {0}", "Done · {0} freed");
            Add("status.cancelling", "正在取消…", "Cancelling…");
            Add("status.failed", "操作失败，详情见日志", "Operation failed — see the log");
            Add("status.elevated", "已以管理员身份运行", "Running with administrator rights");

            Add("auto.label", "关机时自动清理已勾选项目", "Clean checked items automatically at shutdown");
            Add("auto.note", "由计划任务在关机 / 重启时触发；若未完成，下次登录 2 分钟后补做。", "A scheduled task runs at shutdown or restart, and retries 2 minutes after your next sign-in if it was cut short.");
            Add("auto.enableTitle", "启用自动清理", "Enable automatic cleanup");
            Add("auto.enableConfirm", "将创建计划任务“{0}”，在关机时以当前用户身份清理已勾选项目。\n\n确定启用吗？", "A scheduled task named “{0}” will clean the checked items at shutdown, running as the current user.\n\nEnable it now?");
            Add("auto.disableConfirm", "将删除计划任务“{0}”，之后不再自动清理。\n\n确定禁用吗？", "The scheduled task “{0}” will be removed and nothing will be cleaned automatically.\n\nDisable it now?");
            Add("auto.failed", "自动清理设置失败：{0}", "Could not update automatic cleanup: {0}");
            Add("auto.enabled", "已启用关机自动清理", "Automatic cleanup enabled");
            Add("auto.disabled", "已禁用关机自动清理", "Automatic cleanup disabled");

            Add("confirm.title", "确认清理", "Confirm cleanup");
            Add("confirm.body", "将清理 {0} 个项目，预计释放 {1}。\n\n{2}\n\n此操作不可撤销，是否继续？", "{0} item(s) will be cleaned, freeing about {1}.\n\n{2}\n\nThis cannot be undone. Continue?");
            Add("confirm.recycle", "⚠ 回收站中的项目清除后无法恢复。", "⚠ Items in the recycle bin cannot be restored after clearing.");

            Add("result.title", "清理完成", "Cleanup finished");
            Add("result.summary", "释放空间：{0}\n删除文件：{1} 个\n删除文件夹：{2} 个", "Freed: {0}\nFiles removed: {1}\nFolders removed: {2}");
            Add("result.skipped", "跳过 {0} 项（正在使用或受保护），详细记录见日志。", "{0} item(s) skipped (in use or protected) — see the log for details.");
            Add("result.errors", "发生 {0} 个错误。", "{0} error(s) occurred.");
            Add("result.recycle", "回收站已清空：{0} 项", "Recycle bin emptied: {0} item(s)");
            Add("result.recycleAlready", "回收站本来就是空的", "The recycle bin was already empty");
            Add("result.recycleFailed", "回收站未能完全清空", "The recycle bin was not fully emptied");
            Add("result.skippedCategories", "已跳过：{0}", "Skipped: {0}");
            Add("result.reasonAdmin", "需要管理员权限", "needs administrator rights");
            Add("result.reasonBrowser", "浏览器正在运行", "browser is running");
            Add("result.reasonError", "读取或清理失败", "scan or cleanup failed");
            Add("result.failed", "清理失败：{0}", "Cleanup failed: {0}");
            Add("result.nothing", "没有可清理的内容。", "There is nothing to clean.");
            Add("result.openLog", "打开日志目录", "Open log folder");

            Add("menu.logs", "打开日志目录", "Open log folder");
            Add("menu.data", "打开数据目录", "Open data folder");
            Add("menu.readme", "使用说明", "Read me");
            Add("menu.language", "界面语言", "Language");
            Add("menu.about", "关于", "About");
            Add("menu.autoclean", "自动清理设置…", "Automatic cleanup…");

            Add("about.title", "关于 Cleaner", "About Cleaner");
            Add("about.text", "C 盘清理助手 {0}\n\n在原 Hyruty/Cleaner（PowerShell 版）基础上重写为原生应用：\n· 并行扫描、界面不卡顿\n· 白名单 + 路径校验，避免误删\n· 跳过正在使用与符号链接项目\n· 支持关机自动清理与清理报告\n\n数据目录：{1}", "C Drive Cleaner {0}\n\nRewritten as a native app based on Hyruty/Cleaner:\n· parallel scanning, responsive UI\n· allow-list plus path validation\n· skips in-use files and reparse points\n· scheduled shutdown cleanup and reports\n\nData folder: {1}");

            Add("elevate.confirm", "将关闭当前窗口并以管理员身份重新启动，以便清理系统级项目。", "The window will restart with administrator rights so system items can be cleaned.");
            Add("elevate.failed", "提权启动失败：{0}", "Could not restart elevated: {0}");
            Add("elevate.running", "当前已经是管理员权限。", "Already running as administrator.");

            Add("error.title", "错误", "Error");
            Add("error.unexpected", "发生未预期的错误：{0}", "Unexpected error: {0}");
            Add("error.scan", "扫描失败：{0}", "Scan failed: {0}");
            Add("error.logHint", "\n\n详情已写入日志：{0}", "\n\nDetails were written to: {0}");

            Add("category.userTemp.name", "用户临时文件", "User temp files");
            Add("category.userTemp.desc", "Windows 与应用程序产生的临时文件；正在使用的会被跳过", "Temp files left by Windows and apps; files in use are skipped");
            Add("category.browserCache.name", "浏览器缓存", "Browser caches");
            Add("category.browserCache.desc", "Edge / Chrome / Brave / Firefox 的网页缓存，不影响书签、密码与历史记录", "Web caches of Edge, Chrome, Brave and Firefox — bookmarks, passwords and history are kept");
            Add("category.crashDumps.name", "崩溃转储与错误报告", "Crash dumps and reports");
            Add("category.crashDumps.desc", "应用程序崩溃转储与 Windows 错误报告存档", "Application crash dumps and Windows Error Reporting archives");
            Add("category.shaderCache.name", "显卡着色器缓存", "GPU shader caches");
            Add("category.shaderCache.desc", "显卡驱动的着色器缓存；删除后游戏首次运行会重新编译", "Driver shader caches; games recompile them on their next launch");
            Add("category.thumbnails.name", "缩略图与图标缓存", "Thumbnail caches");
            Add("category.thumbnails.desc", "资源管理器缩略图数据库，删除后由系统自动重建", "Explorer thumbnail databases; Windows rebuilds them automatically");
            Add("category.devCaches.name", "开发工具缓存", "Developer caches");
            Add("category.devCaches.desc", "npm、pip、Yarn、Electron 等包管理器的下载缓存", "Download caches of npm, pip, Yarn and Electron");
            Add("category.systemTemp.name", "Windows 临时文件", "Windows temp files");
            Add("category.systemTemp.desc", "系统级临时目录（需要管理员权限）", "System-wide temp folder (needs administrator rights)");
            Add("category.errorReports.name", "系统错误报告与转储", "System reports and dumps");
            Add("category.errorReports.desc", "系统级 WER 存档、内核报告与内存转储（需要管理员权限）", "System WER archives, kernel reports and memory dumps (needs administrator rights)");
            Add("category.windowsUpdateCache.name", "Windows 更新缓存", "Windows Update cache");
            Add("category.windowsUpdateCache.desc", "已下载的更新安装包与传递优化缓存（需要管理员权限）", "Downloaded update packages and Delivery Optimization cache (needs administrator rights)");
            Add("category.upgradeLeftovers.name", "旧系统安装残留", "Previous Windows install");
            Add("category.upgradeLeftovers.desc", "Windows.old 与升级临时目录，通常可释放数 GB（需要管理员权限）", "Windows.old and upgrade staging folders, often several GB (needs administrator rights)");
            Add("category.recycleBin.name", "回收站", "Recycle bin");
            Add("category.recycleBin.desc", "清空回收站；清空后无法恢复", "Empties the recycle bin; deleted items cannot be restored");
        }

        private static void Add(string app, string zh, string en)
        {
            Zh[app] = zh;
            En[app] = en;
        }

        public static void Initialize(string languageSetting)
        {
            string wanted = languageSetting;
            if (string.IsNullOrEmpty(wanted) || string.Equals(wanted, "auto", StringComparison.OrdinalIgnoreCase))
            {
                wanted = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }

            if (wanted.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                _language = "zh-CN";
            }
            else if (wanted.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                _language = "en-US";
            }
            else
            {
                _language = "en-US";
            }

            MergeOverrideFile();
        }

        private static Dictionary<string, string> Active
        {
            get { return IsChinese ? Zh : En; }
        }

        public static string T(string key)
        {
            string value;
            if (Active.TryGetValue(key, out value))
            {
                return value;
            }

            // Fall back to the other language, then to the key itself so a
            // missing translation never produces an empty label.
            if (En.TryGetValue(key, out value))
            {
                return value;
            }

            if (Zh.TryGetValue(key, out value))
            {
                return value;
            }

            return key;
        }

        public static string T(string key, params object[] args)
        {
            string format = T(key);
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        public static bool HasKey(string key)
        {
            return Active.ContainsKey(key);
        }

        public static IEnumerable<string> AllKeys
        {
            get { return Zh.Keys; }
        }

        /// <summary>Loads lang.&lt;culture&gt;.json overrides if present.</summary>
        private static void MergeOverrideFile()
        {
            string[] candidates =
            {
                Path.Combine(AppInfo.DataRoot, "lang." + _language + ".json"),
                Path.Combine(AppInfo.ExeFolder, "lang." + _language + ".json")
            };

            foreach (string file in candidates)
            {
                try
                {
                    if (!File.Exists(file))
                    {
                        continue;
                    }

                    Dictionary<string, object> map = Json.AsObject(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                    if (map == null)
                    {
                        return;
                    }

                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        string text = pair.Value as string;
                        if (text != null && !string.IsNullOrEmpty(pair.Key))
                        {
                            Active[pair.Key] = text;
                        }
                    }

                    return;
                }
                catch
                {
                    // Ignore malformed override files; built-in strings still work.
                }
            }
        }
    }
}
