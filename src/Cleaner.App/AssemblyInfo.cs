using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// The compiler does not emit this automatically; without it the CLR runs in
// legacy path-handling mode and rejects extended-length ("\\?\") paths.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

[assembly: AssemblyTitle("C 盘清理助手 (Cleaner)")]
[assembly: AssemblyDescription("安全清理 C 盘缓存、临时文件与系统垃圾 / Safely cleans caches, temp files and system junk")]
[assembly: AssemblyProduct("Cleaner")]
[assembly: AssemblyCompany("Cleaner Project")]
[assembly: AssemblyCopyright("Based on Hyruty/Cleaner (MIT-style attribution) - rewritten 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("2.0.2.0")]
[assembly: AssemblyFileVersion("2.0.2.0")]
[assembly: AssemblyInformationalVersion("2.0.2")]
[assembly: ComVisible(false)]
[assembly: Guid("6f2e6c8b-5f0a-4d47-9d1b-6a1f2f0d7a11")]
