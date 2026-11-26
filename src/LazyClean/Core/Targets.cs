using System;
using System.Collections.Generic;
using System.IO;

namespace Cleaner
{
    public static class Targets
    {
        public static readonly string[] Protected =
        {
            @"C:\Windows\System32",
            @"C:\Windows\SysWOW64",
            @"C:\Windows\WinSxS",
            @"C:\Windows\Boot",
            @"C:\Windows\servicing",
            @"C:\Windows\assembly",
            @"C:\Windows\Microsoft.NET",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\Recovery",
            @"C:\pagefile.sys",
            @"C:\hiberfil.sys",
            @"C:\swapfile.sys",
            @"C:\DumpStack.log.tmp",
            @"C:\DumpStack.log",
            @"C:\bootmgr",
            @"C:\Boot"
        };

        private static readonly string[] DriveCriticalRoots =
        {
            "$Recycle.Bin", "System Volume Information", "Recovery",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp",
            "DumpStack.log", "bootmgr", "Boot"
        };

        private static readonly string[] DeepSkip =
        {
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\Recovery",
            @"C:\$WinREAgent",
            @"C:\Config.Msi",
            @"C:\Windows.old",
            @"C:\$SysReset",
            @"C:\PerfLogs",
            @"C:\inetpub",
            @"C:\MSOCache"
        };

        private static readonly string[] UserDataFolders =
        {
            "Documents", "Desktop", "Pictures", "Videos", "Music", "Downloads",
            "OneDrive", "Dropbox", "Google Drive", "iCloudDrive"
        };

        public static List<TargetDef> Regular()
        {
            var list = new List<TargetDef>();
            Add(list, "User temp files", "%TEMP%", true, "Left behind by running apps");
            Add(list, "Windows temp files", "%WINDIR%\\Temp", true, null);
            Add(list, "Windows Update cache", "%WINDIR%\\SoftwareDistribution\\Download", true, "Updates already installed");
            Add(list, "Delivery Optimization", "%WINDIR%\\ServiceProfiles\\NetworkService\\AppData\\Local\\Microsoft\\Windows\\DeliveryOptimization\\Cache", true, null);

            Add(list, "Chrome cache", "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache", true, null);
            Add(list, "Chrome cache", "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Code Cache", true, null);
            Add(list, "Chrome cache", "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\GPUCache", true, null);

            Add(list, "Edge cache", "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Cache", true, null);
            Add(list, "Edge cache", "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\Code Cache", true, null);
            Add(list, "Edge cache", "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\*\\GPUCache", true, null);

            Add(list, "Firefox cache", "%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2", true, null);

            Add(list, "Thumbnail cache", "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer\\thumbcache_*.db", true, "Explorer rebuilds these");
            Add(list, "Icon cache", "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer\\iconcache_*.db", true, "Explorer rebuilds these");

            Add(list, "Crash dumps", "%LOCALAPPDATA%\\CrashDumps", true, null);
            Add(list, "Windows Error Reporting", "%LOCALAPPDATA%\\Microsoft\\Windows\\WER", true, null);
            Add(list, "Windows Error Reporting", "%PROGRAMDATA%\\Microsoft\\Windows\\WER", true, null);

            Add(list, "System logs", "%WINDIR%\\Logs\\CBS", true, null);
            Add(list, "System logs", "%WINDIR%\\Logs\\DISM", true, null);
            Add(list, "System logs", "%WINDIR%\\Logs\\WindowsUpdate", true, null);
            Add(list, "System logs", "%WINDIR%\\Panther", true, null);

            Add(list, "Prefetch", "%WINDIR%\\Prefetch", false, "Startup stays a little slower for a while");
            return list;
        }

        private static void Add(List<TargetDef> list, string cat, string path, bool on, string note)
        {
            list.Add(new TargetDef { Category = cat, Path = path, DefaultOn = on, Note = note });
        }

        // Canonicalizes paths to defeat traversal and alternate-namespace evasion.
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string p = path.Trim();
            if (p.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                p = p.Substring(4);
            if (p.Length == 2 && char.IsLetter(p[0]) && p[1] == ':')
                return p;
            try
            {
                p = Path.GetFullPath(p);
            }
            catch
            {
                p = p.Replace('/', '\\');
            }
            return p.TrimEnd('\\');
        }

        public static bool IsProtected(string path)
        {
            string p = Normalize(path);
            if (p.Length == 0) return true;

            // Never delete an entire filesystem drive root.
            if (p.Length <= 3 && (p.EndsWith(":") || p.IndexOf(':') == p.Length - 1)) return true;

            for (int i = 0; i < Protected.Length; i++)
            {
                string d = Protected[i];
                if (p.Equals(d, StringComparison.OrdinalIgnoreCase)) return true;
                if (p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }

            // Protect the active Windows installation and its core subsystems.
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (p.Equals(win, StringComparison.OrdinalIgnoreCase)) return true;
            if (p.StartsWith(win + "\\System32", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.StartsWith(win + "\\SysWOW64", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.StartsWith(win + "\\WinSxS", StringComparison.OrdinalIgnoreCase)) return true;

            // Protect critical OS files and volume recovery folders on any drive letter.
            string root = Path.GetPathRoot(p);
            if (!string.IsNullOrEmpty(root) && p.Length > root.Length)
            {
                string rel = p.Substring(root.Length).TrimStart('\\');
                string top = rel.Split('\\')[0];
                for (int i = 0; i < DriveCriticalRoots.Length; i++)
                {
                    if (string.Equals(top, DriveCriticalRoots[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static bool IsDeepSkipped(string path)
        {
            string p = Normalize(path);
            for (int i = 0; i < DeepSkip.Length; i++)
            {
                string d = DeepSkip[i];
                if (p.Equals(d, StringComparison.OrdinalIgnoreCase)) return true;
                if (p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsUserData(string path)
        {
            string p = Normalize(path);
            int idx = p.IndexOf(@"\Users\", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            string rest = p.Substring(idx + 7);
            string[] parts = rest.Split('\\');
            // Protect the root user profile folder itself from deletion.
            if (parts.Length < 2) return true;
            string sub = parts[1];
            for (int i = 0; i < UserDataFolders.Length; i++)
                if (string.Equals(sub, UserDataFolders[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Folders that hold a program's own files or its live data. Nothing under these is ever
        // "junk by extension": a .log here can be a database, a .bak can be the previous binary, and
        // a .old can be the file an updater is about to restore. Deleting any of them breaks the app.
        private static readonly string[] AppOwnedMarkers =
        {
            @"\Application\",          // installer layouts (Chromium/Electron put chrome.exe and .pak here)
            @"\Current\",              // version-manager layouts
            @"\app-",                  // Squirrel-style app-1.2.3
            @"\Programs\",
            @"\Program Files",
            @"\bin\",
            @"\lib\",
            @"\resources\",
            @"\locales\",
            @"\User Data\",            // Chromium profile: holds leveldb .log files that ARE the data
            @"\Profiles\",             // Firefox profile
            @"\Default\",
            @"\Local Storage\",
            @"\IndexedDB\",
            @"\leveldb",
            @"\Service Worker\",
            @"\Session Storage\",
            @"\Sync Data\",
            @"\Extension State\",
            @"\Server Key\",           // plugin/DRM key in some installers
            @"\Installer\",
            @"\uninstall",
            @"\.git\"
        };

        // True when a path belongs to an installed program or a live application profile, so it must
        // never be treated as junk just because of its extension.
        public static bool IsAppOwned(string path)
        {
            for (int i = 0; i < AppOwnedMarkers.Length; i++)
                if (path.IndexOf(AppOwnedMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // A sibling .bak-<version> or .old next to the live binary is an updater's backup copy.
            // Deleting it is survivable, but deleting the live file it belongs to is not, and a scan
            // that cannot tell them apart must leave the whole folder alone.
            string name = Path.GetFileName(path);
            if (name.Length > 0)
            {
                string ext = Path.GetExtension(name);
                if (ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".old", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Executables and libraries: never deletable as junk, wherever they live.
        public static bool IsProgramBinary(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".sys", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".node", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".so", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".pak", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".dat", StringComparison.OrdinalIgnoreCase);
        }

        public static bool SkipDirName(string name)
        {
            return name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".vs", StringComparison.OrdinalIgnoreCase)
                || name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase)
                || name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);
        }

        public static IEnumerable<string> Expand(string raw)
        {
            string p = Environment.ExpandEnvironmentVariables(raw);
            if (p.IndexOf('*') < 0 && p.IndexOf('?') < 0)
            {
                yield return p;
                yield break;
            }

            string root = Path.GetPathRoot(p);
            if (string.IsNullOrEmpty(root) || root.Length < 3)
            {
                yield return p;
                yield break;
            }

            var current = new List<string>();
            current.Add(root);
            string[] parts = p.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                var next = new List<string>();
                bool wildcard = part.IndexOf('*') >= 0 || part.IndexOf('?') >= 0;
                for (int j = 0; j < current.Count; j++)
                {
                    string c = current[j];
                    if (!wildcard)
                    {
                        next.Add(Path.Combine(c, part));
                        continue;
                    }
                    try { next.AddRange(Directory.GetDirectories(c, part)); }
                    catch { }
                    try { next.AddRange(Directory.GetFiles(c, part)); }
                    catch { }
                }
                current = next;
                if (current.Count == 0) yield break;
            }
            for (int i = 0; i < current.Count; i++) yield return current[i];
        }
    }
}
