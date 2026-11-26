using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Cleaner
{
    internal static class SelfTest
    {
        private static int failures;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest")
                return Run();

            if (args.Length > 0 && args[0] == "--scaninfo")
                return ScanInfo();

            if (args.Length > 0 && args[0] == "--deepinfo")
                return DeepInfo();

            if (args.Length > 0 && args[0] == "--uismoke")
                return RunUiSmoke();

            if (args.Length > 0 && args[0] == "--screenshot")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var f = new MainForm())
                {
                    f.Show();
                    Application.DoEvents();
                    string path = args.Length > 1 ? args[1] : "shot.png";
                    if (args.Length > 2 && args[2] == "populated")
                    {
                        UiSmoke(f);
                        // UiSmoke leaves the form pristine on purpose, so put demo rows back before
                        // capturing, otherwise the shot is the empty state again.
                        f.LoadDemoRows();
                        f.CaptureTab(1, path.Replace(".png", "-tools.png"));
                        f.CaptureTab(0, path);
                    }
                    else
                    {
                        Application.DoEvents();
                        f.CaptureTo(path);
                    }
                }
                Console.WriteLine("screenshot written");
                return 0;
            }

            int scanCode = Run();
            int uiCode = RunUiSmoke();
            return (scanCode == 0 && uiCode == 0) ? 0 : 1;
        }

        private static int RunUiSmoke()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var f = new MainForm())
            {
                f.Show();
                Application.DoEvents();
                return UiSmoke(f);
            }
        }

        public static int Run()
        {
            failures = 0;
            string dir = Path.Combine(Path.GetTempPath(), "cleaner-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);

                byte[] data = new byte[2 * 1024 * 1024];
                new Random(42).NextBytes(data);

                string a = Path.Combine(dir, "a.bin");
                string b = Path.Combine(dir, "b.bin");
                File.WriteAllBytes(a, data);
                File.WriteAllBytes(b, data);

                string junk = Path.Combine(dir, "junk.tmp");
                File.WriteAllText(junk, "x");

                string sub = Path.Combine(dir, "sub");
                Directory.CreateDirectory(sub);
                string deepJunk = Path.Combine(sub, "old.tmp");
                File.WriteAllText(deepJunk, "y");

                // A Chromium-style install: the live binary plus the updater's backup, and a profile
                // whose *.log files ARE the database. None of this may ever be flagged as junk.
                string appDir = Path.Combine(dir, "Application");
                string appVer = Path.Combine(appDir, "0.22.4.1");
                Directory.CreateDirectory(appVer);
                string liveExe = Path.Combine(appVer, "chrome.exe");
                File.WriteAllText(liveExe, "MZ");
                string liveDll = Path.Combine(appVer, "chrome.dll");
                File.WriteAllText(liveDll, "MZ");
                string backupExe = Path.Combine(appDir, "chrome.exe.bak-0.21.3.1");
                File.WriteAllText(backupExe, "MZ");
                string oldExe = Path.Combine(appDir, "chrome.exe.old");
                File.WriteAllText(oldExe, "MZ");

                string profile = Path.Combine(dir, "User Data", "Default", "Local Storage", "leveldb");
                Directory.CreateDirectory(profile);
                string leveldbLog = Path.Combine(profile, "000003.log");
                File.WriteAllText(leveldbLog, "db");
                string looseLog = Path.Combine(dir, "application.log");
                File.WriteAllText(looseLog, "not junk either");

                string empty = Path.Combine(dir, "empty-sub");
                Directory.CreateDirectory(empty);

                var opt = new ScanOptions
                {
                    Root = dir,
                    LargeFileThreshold = 1024,
                    Junk = true,
                    Dupes = true,
                    EmptyFolders = true,
                    LargeFiles = true
                };
                List<Category> cats = ScanEngine.ScanDeep(opt, CancellationToken.None, null);

                Category dup = Find(cats, "Duplicates");
                Category junkCat = Find(cats, "Junk files");
                Category emptyCat = Find(cats, "Empty folders");
                Category largeCat = Find(cats, "Large files");

                Check(dup != null && dup.Items.Count == 2, "duplicates detected (2 files)");
                Check(dup != null && dup.Items.Count(delegate(ScanItem i) { return i.Keep; }) == 1, "one duplicate copy kept");
                Check(junkCat != null && junkCat.Items.Any(delegate(ScanItem i) { return i.Path == junk; }), "junk .tmp detected");
                Check(junkCat != null && junkCat.Items.Any(delegate(ScanItem i) { return i.Path == deepJunk; }), "nested junk .tmp detected");
                Check(emptyCat != null && emptyCat.Items.Any(delegate(ScanItem i) { return i.Path == empty; }), "empty folder detected");
                Check(largeCat != null && largeCat.Items.Any(delegate(ScanItem i) { return i.Path == a; }), "large file detected");

                // The regression this suite exists for: a deep scan must never offer an installed
                // program's own files, its updater backups, or a profile database as deletable junk.
                Func<string, bool> flagged = delegate(string p)
                {
                    for (int i = 0; i < cats.Count; i++)
                        for (int j = 0; j < cats[i].Items.Count; j++)
                            if (string.Equals(cats[i].Items[j].Path, p, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                };
                Check(!flagged(liveExe), "chrome.exe in version folder not flagged");
                Check(!flagged(liveDll), "chrome.dll not flagged");
                Check(!flagged(backupExe), "updater .bak-<version> backup not flagged");
                Check(!flagged(oldExe), "chrome.exe.old not flagged");
                Check(!flagged(leveldbLog), "leveldb .log database not flagged");
                Check(!flagged(looseLog), "application .log file not flagged");

                var toDelete = new List<ScanItem>();
                for (int i = 0; i < cats.Count; i++)
                {
                    for (int j = 0; j < cats[i].Items.Count; j++)
                    {
                        ScanItem it = cats[i].Items[j];
                        if (it.Checked && !it.Keep) toDelete.Add(it);
                    }
                }

                DeleteResult res = ScanEngine.Delete(toDelete, true, null, CancellationToken.None);
                Check(res.Failed == 0, "permanent delete completes with zero failures (failed=" + res.Failed + ")");
                Check(!File.Exists(junk), "junk.tmp deleted");
                Check(!File.Exists(deepJunk), "nested old.tmp deleted");
                Check(!Directory.Exists(empty), "empty folder deleted");
                Check(File.Exists(liveExe) && File.Exists(liveDll), "application binaries intact after delete");

                // Second layer: even if a stale selection names a program file, delete must refuse it.
                DeleteResult guard = ScanEngine.Delete(new List<ScanItem>
                {
                    new ScanItem { Path = liveExe, Size = 2 },
                    new ScanItem { Path = backupExe, Size = 2 }
                }, true, null, CancellationToken.None);
                Check(guard.Deleted == 0 && guard.Failed == 2, "delete refuses app-owned files");
                Check(File.Exists(liveExe) && File.Exists(backupExe), "app-owned files protected from forced delete");

                int remain = (File.Exists(a) ? 1 : 0) + (File.Exists(b) ? 1 : 0);
                Check(remain == 1, "exactly one duplicate copy remains (remaining=" + remain + ")");

                DeleteResult res2 = ScanEngine.Delete(
                    new List<ScanItem> { new ScanItem { Path = @"C:\Windows\System32\drivers\etc\hosts", Size = 1 } },
                    true, null, CancellationToken.None);
                Check(res2.Deleted == 0 && res2.Failed == 1, "protected system path rejected");

                Check(Targets.IsProtected(@"C:\"), "drive root C:\\ protected");
                Check(Targets.IsProtected(@"C:"), "drive root C: protected");
                Check(Targets.IsProtected(@"D:"), "drive root D: protected");
                Check(Targets.IsProtected(@"D:\$Recycle.Bin"), "secondary drive Recycle Bin protected");
                Check(Targets.IsProtected(@"E:\System Volume Information\test"), "secondary drive System Volume Information protected");
                Check(Targets.IsProtected(@"\\?\C:\Windows\System32\calc.exe"), "long-path prefix protected");
                Check(Targets.IsProtected(@"C:\Windows\System32\..\System32\calc.exe"), "path traversal protected");

                string notEmptyDir = Path.Combine(dir, "not-empty-dir");
                Directory.CreateDirectory(notEmptyDir);
                File.WriteAllText(Path.Combine(notEmptyDir, "keep.txt"), "stay");
                DeleteResult nonEmpRes = ScanEngine.Delete(
                    new List<ScanItem> { new ScanItem { Path = notEmptyDir, IsDir = true, Size = 0 } },
                    true, null, CancellationToken.None);
                Check(nonEmpRes.Deleted == 0 && nonEmpRes.Failed == 1 && Directory.Exists(notEmptyDir), "non-empty directory rejected on delete");

                string rc1 = Path.Combine(dir, "recycle1.tmp");
                string rc2 = Path.Combine(dir, "recycle2.tmp");
                File.WriteAllText(rc1, "a");
                File.WriteAllText(rc2, "b");
                DeleteResult rcr = ScanEngine.Delete(new List<ScanItem>
                {
                    new ScanItem { Path = rc1, Size = 1 },
                    new ScanItem { Path = rc2, Size = 1 }
                }, false, null, CancellationToken.None);
                Check(rcr.Deleted == 2 && !File.Exists(rc1) && !File.Exists(rc2), "move to Recycle Bin succeeded");
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION: " + ex);
                failures++;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            Console.WriteLine(failures == 0 ? "SELFTEST PASS" : "SELFTEST FAIL (" + failures + " failed)");
            return failures == 0 ? 0 : 1;
        }

        public static int DeepInfo()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var opt = new ScanOptions { Junk = true, Dupes = false, EmptyFolders = true, LargeFiles = true };
            List<Category> cats = ScanEngine.ScanDeep(opt, CancellationToken.None, null);
            sw.Stop();
            Console.WriteLine("Files scanned: " + ScanEngine.LastWalked);
            for (int i = 0; i < cats.Count; i++)
            {
                Category c = cats[i];
                Console.WriteLine(string.Format("{0,-20} {1} items  {2}", c.Name, c.Items.Count, Util.Fmt(c.Size)));
                for (int j = 0; j < Math.Min(3, c.Items.Count); j++)
                    Console.WriteLine("    " + c.Items[j].Path);
            }
            Console.WriteLine("Elapsed: " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
            return 0;
        }

        public static int UiSmoke(MainForm f) { return f.RunSmoke(); }

        public static int ScanInfo()
        {
            List<Category> cats = ScanEngine.ScanRegular(CancellationToken.None, null);
            long total = 0;
            for (int i = 0; i < cats.Count; i++)
            {
                Category c = cats[i];
                Console.WriteLine(string.Format("{0,-28} {1,8:N0} items  {2,10}", c.Name, c.Items.Count, Util.Fmt(c.Size)));
                total += c.Size;
            }
            Console.WriteLine(string.Format("{0,-28} {1,8}       {2,10}", "TOTAL", "", Util.Fmt(total)));
            return 0;
        }

        private static Category Find(List<Category> cats, string name)
        {
            for (int i = 0; i < cats.Count; i++)
                if (cats[i].Name == name) return cats[i];
            return null;
        }

        private static void Check(bool ok, string label)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
            if (!ok) failures++;
        }
    }
}
