using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cleaner.Setup
{
    /// <summary>Minimal two-language string table for the installer.</summary>
    internal static class S
    {
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _chinese;

        static S()
        {
            Add("setup.title", "安装 Cleaner", "Install Cleaner");
            Add("setup.subtitle", "C 盘清理助手 {0}", "C Drive Cleaner {0}");
            Add("uninstall.title", "卸载 Cleaner", "Uninstall Cleaner");
            Add("uninstall.subtitle", "C 盘清理助手 {0}", "C Drive Cleaner {0}");

            Add("welcome.heading", "欢迎安装 C 盘清理助手", "Welcome to C Drive Cleaner");
            Add("welcome.body", "安装程序会把程序文件写入下面的目录，并创建开始菜单项；桌面快捷方式可选。\n\n程序只清理白名单中的缓存目录，不会触碰桌面、文档、下载等个人文件。", "Setup copies the application files to the folder below and creates a Start menu entry; a desktop shortcut is optional.\n\nOnly allow-listed cache folders are ever cleaned — Desktop, Documents and Downloads are never touched.");
            Add("welcome.location", "安装位置", "Install location");
            Add("welcome.browse", "浏览…", "Browse…");
            Add("welcome.desktop", "创建桌面快捷方式", "Create a desktop shortcut");
            Add("welcome.startmenu", "创建开始菜单项", "Create a Start menu entry");
            Add("welcome.launch", "安装完成后启动", "Launch after installation");
            Add("welcome.scopeMachine", "安装范围：所有用户（需要管理员权限）", "Scope: all users (requires administrator rights)");
            Add("welcome.scopeUser", "安装范围：仅当前用户", "Scope: current user only");
            Add("welcome.upgrade", "检测到已安装版本 {0}，将执行升级安装。", "Version {0} is already installed; setup will upgrade it.");
            Add("welcome.freeSpace", "所需空间约 {0}", "About {0} required");
            Add("welcome.hint", "提示：安装后如需清理系统级项目，请在程序中点击“以管理员身份重启”。", "Tip: to clean system-wide items later, use “Restart as admin” inside the app.");

            Add("uninstall.heading", "卸载 C 盘清理助手", "Remove C Drive Cleaner");
            Add("uninstall.body", "将从下面的位置删除程序文件、开始菜单项、桌面快捷方式和注册表项。", "Setup will remove the program files, Start menu entry, desktop shortcut and registry keys from the location below.");
            Add("uninstall.keepData", "同时删除设置与日志（{0}）", "Also delete settings and logs ({0})");
            Add("uninstall.notInstalled", "没有找到已安装的 Cleaner。", "Cleaner does not appear to be installed.");
            Add("uninstall.taskNote", "关机自动清理计划任务也会被移除。", "The shutdown cleanup scheduled task will be removed as well.");

            Add("btn.install", "安装", "Install");
            Add("btn.uninstall", "卸载", "Uninstall");
            Add("btn.cancel", "取消", "Cancel");
            Add("btn.finish", "完成", "Finish");
            Add("btn.close", "关闭", "Close");

            Add("progress.preparing", "正在准备…", "Preparing…");
            Add("progress.stopping", "正在关闭正在运行的 Cleaner…", "Closing the running Cleaner…");
            Add("progress.extracting", "正在写入程序文件…", "Writing application files…");
            Add("progress.uninstaller", "正在准备卸载程序…", "Preparing the uninstaller…");
            Add("progress.shortcuts", "正在创建快捷方式…", "Creating shortcuts…");
            Add("progress.registry", "正在写入注册信息…", "Writing registry entries…");
            Add("progress.task", "正在清理计划任务…", "Removing the scheduled task…");
            Add("progress.files", "正在删除程序文件…", "Deleting application files…");
            Add("progress.done", "完成", "Done");

            Add("done.heading", "安装完成", "Installation complete");
            Add("done.body", "C 盘清理助手 {0} 已安装到：\n{1}\n\n双击桌面上的图标或开始菜单项即可启动。", "C Drive Cleaner {0} was installed to:\n{1}\n\nStart it from the desktop icon or the Start menu entry.");
            Add("done.launch", "立即启动 C 盘清理助手", "Launch C Drive Cleaner now");
            Add("uninstallDone.heading", "卸载完成", "Uninstall complete");
            Add("uninstallDone.body", "C 盘清理助手已从这台计算机移除。", "C Drive Cleaner has been removed from this computer.");

            Add("error.title", "安装程序", "Setup");
            Add("error.failed", "操作失败：{0}", "The operation failed: {0}");
            Add("error.dir", "请选择一个有效的安装目录。", "Please choose a valid installation folder.");
            Add("error.admin", "需要管理员权限才能为所有用户安装。", "Administrator rights are required to install for all users.");
            Add("error.running", "无法关闭正在运行的 Cleaner，请手动退出后重试。", "Could not close the running Cleaner. Please exit it and try again.");
        }

        private static void Add(string key, string zh, string en)
        {
            Zh[key] = zh;
            En[key] = en;
        }

        public static void Initialize(string language)
        {
            string wanted = language;
            if (string.IsNullOrEmpty(wanted) || string.Equals(wanted, "auto", StringComparison.OrdinalIgnoreCase))
            {
                wanted = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }

            _chinese = wanted.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsChinese
        {
            get { return _chinese; }
        }

        public static string T(string key)
        {
            string value;
            if ((_chinese ? Zh : En).TryGetValue(key, out value))
            {
                return value;
            }

            if (En.TryGetValue(key, out value))
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

        public static string ShortcutName
        {
            get { return _chinese ? "C 盘清理助手" : "C Drive Cleaner"; }
        }

        public static string UninstallShortcutName
        {
            get { return _chinese ? "卸载 C 盘清理助手" : "Uninstall C Drive Cleaner"; }
        }
    }
}
