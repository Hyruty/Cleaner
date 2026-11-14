using System;
using System.Drawing;
using System.IO;

namespace Cleaner.Setup
{
    internal static class SetupInfo
    {
        public const string Product = "Cleaner";
        public const string DisplayName = "C 盘清理助手";
        public const string EnglishName = "Cleaner";
        public const string Version = "2.0.2";
        public const string Publisher = "Cleaner Project";
        public const string ExeName = "Cleaner.exe";
        public const string UninstallerName = "Uninstall.exe";
        public const string AppKeyName = "Cleaner";

        public const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Cleaner";
        public const string AppKeyPath = @"SOFTWARE\Cleaner";
        public const string AppPathsKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Cleaner.exe";
        public const string PayloadResourceName = "Cleaner.Setup.Payload.zip";

        public static string ExePath
        {
            get
            {
                try
                {
                    return System.Reflection.Assembly.GetEntryAssembly().Location;
                }
                catch
                {
                    return Path.Combine(Environment.CurrentDirectory, "CleanerSetup.exe");
                }
            }
        }

        public static string ExeFolder
        {
            get { return Path.GetDirectoryName(ExePath); }
        }

        public static string ProgramFilesCleaner
        {
            get
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(programFiles, "Cleaner");
            }
        }

        public static string UserInstallDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "Programs", "Cleaner");
            }
        }
    }

    internal static class SetupTheme
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#F2F5F7");
        public static readonly Color Surface = Color.White;
        public static readonly Color Text = ColorTranslator.FromHtml("#17222B");
        public static readonly Color Muted = ColorTranslator.FromHtml("#5E6E7C");
        public static readonly Color Border = ColorTranslator.FromHtml("#DFE6EB");
        public static readonly Color Accent = ColorTranslator.FromHtml("#0F766E");
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#0B5F59");
        public static readonly Color HeaderStart = ColorTranslator.FromHtml("#17938C");
        public static readonly Color HeaderEnd = ColorTranslator.FromHtml("#10516A");
        public static readonly Color Danger = ColorTranslator.FromHtml("#B91C1C");

        public static Font Font(float size, FontStyle style)
        {
            try
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
            }
        }

        public static Color Mix(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }
    }
}
