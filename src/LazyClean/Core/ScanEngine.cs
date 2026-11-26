using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cleaner
{
    public static class ScanEngine
    {
        public static long LastWalked;

        public static List<Category> ScanRegular(CancellationToken ct, IProgress<ScanProgress> progress)
        {
            var cats = new ConcurrentDictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            var seen = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            long count = 0;

            var po = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 };
            Parallel.ForEach(Targets.Regular(), po, t =>
            {
                Category cat = cats.GetOrAdd(t.Category, delegate(string n) { return new Category { Name = n }; });
                foreach (string raw in Targets.Expand(t.Path))
                {
                    if (ct.IsCancellationRequested) return;

                    if (Directory.Exists(raw))
                    {
                        foreach (FileSystemInfo fi in WalkFiles(new DirectoryInfo(raw), ct))
                        {
                            string fp = fi.FullName;
                            if (Targets.IsProtected(fp)) continue;
                            if (!seen.TryAdd(fp, true)) continue;
                            long len = 0;
                            try { len = ((FileInfo)fi).Length; } catch { }
                            cat.Add(new ScanItem
                            {
                                Path = fp,
                                Size = len,
                                Checked = t.DefaultOn,
                                Note = t.Note
                            }.Prepared());
                            long c = Interlocked.Increment(ref count);
                            if (c % 300 == 0 && progress != null)
                                progress.Report(new ScanProgress { Files = c, Current = fp });
                        }
                    }
                    else if (File.Exists(raw) && !Targets.IsProtected(raw))
                    {
                        if (!seen.TryAdd(raw, true)) continue;
                        long len = 0;
                        try { len = new FileInfo(raw).Length; } catch { }
                        cat.Add(new ScanItem
                        {
                            Path = raw,
                            Size = len,
                            Checked = t.DefaultOn,
                            Note = t.Note
                        }.Prepared());
                    }
                }
            });

            var result = new List<Category>();
            foreach (Category c in cats.Values)
            {
                if (c.Items.Count == 0) continue;
                c.Items.Sort(delegate(ScanItem a, ScanItem b) { return b.Size.CompareTo(a.Size); });
                for (int i = 0; i < c.Items.Count; i++)
                {
                    c.Items[i].Category = c.Name;
                    c.Items[i].Prepare();
                }
                result.Add(c);
            }
            result.Sort(delegate(Category a, Category b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return result;
        }

        private static IEnumerable<FileSystemInfo> WalkFiles(DirectoryInfo root, CancellationToken ct)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) yield break;
                DirectoryInfo dir = stack.Pop();
                FileSystemInfo[] entries;
                try { entries = dir.GetFileSystemInfos(); }
                catch { continue; }

                for (int i = 0; i < entries.Length; i++)
                {
                    FileSystemInfo e = entries[i];
                    bool isReparse = true;
                    try { isReparse = (e.Attributes & FileAttributes.ReparsePoint) != 0; }
                    catch { }
                    if (isReparse) continue;

                    DirectoryInfo d = e as DirectoryInfo;
                    if (d != null)
                    {
                        if (!Targets.IsProtected(d.FullName)) stack.Push(d);
                    }
                    else
                    {
                        yield return e;
                    }
                }
            }
        }

        public static List<Category> ScanDeep(ScanOptions opt, CancellationToken ct, IProgress<ScanProgress> progress)
        {
            string root = string.IsNullOrEmpty(opt.Root) ? Path.GetPathRoot(Environment.SystemDirectory) : opt.Root;
            var rootDir = new DirectoryInfo(root);
            var records = new ConcurrentBag<FileRec>();
            var emptyDirs = new ConcurrentBag<string>();
            var ctx = new DeepCtx { Ct = ct, Progress = progress };

            // files sitting directly in the drive root
            FileInfo[] rootFiles;
            try { rootFiles = rootDir.GetFiles(); }
            catch { rootFiles = new FileInfo[0]; }
            for (int i = 0; i < rootFiles.Length; i++)
            {
                FileInfo f = rootFiles[i];
                if (Targets.IsProtected(f.FullName)) continue;
                try
                {
                    if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    records.Add(new FileRec { Path = f.FullName, Size = f.Length });
                }
                catch { }
            }

            DirectoryInfo[] tops;
            try { tops = rootDir.GetDirectories(); }
            catch { tops = new DirectoryInfo[0]; }

            var po = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2)
            };
            Parallel.ForEach(tops, po, d =>
            {
                if (ct.IsCancellationRequested) return;
                if (Targets.IsDeepSkipped(d.FullName)) return;
                try { if ((d.Attributes & FileAttributes.ReparsePoint) != 0) return; }
                catch { return; }

                var files = new List<FileRec>();
                var empties = new List<string>();
                WalkDeep(d, ctx, files, empties);
                for (int i = 0; i < files.Count; i++) records.Add(files[i]);
                for (int i = 0; i < empties.Count; i++) emptyDirs.Add(empties[i]);
            });

            ct.ThrowIfCancellationRequested();

            LastWalked = ctx.Count;

            var result = new List<Category>();
            var all = records.ToList();

            if (opt.Junk)
            {
                // Only extensions that are junk wherever they appear. .log, .dmp, .old and .bak are
                // deliberately absent: Chromium keeps live LevelDB data in *.log files, and updaters
                // keep .bak binaries next to chrome.exe, so those extensions need a location check,
                // not a global sweep.
                var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".tmp", ".temp", ".part", ".crdownload"
                };
                var junk = new List<ScanItem>();
                for (int i = 0; i < all.Count; i++)
                {
                    FileRec r = all[i];
                    if (Targets.IsProtected(r.Path) || Targets.IsUserData(r.Path)) continue;
                    if (Targets.IsAppOwned(r.Path)) continue;
                    if (Targets.IsProgramBinary(r.Path)) continue;
                    if (!exts.Contains(Path.GetExtension(r.Path))) continue;
                    junk.Add(new ScanItem { Path = r.Path, Size = r.Size, Checked = true, Note = "Junk file" });
                }
                AddCategory(result, "Junk files", junk);
            }

            if (opt.EmptyFolders)
            {
                var empties = new List<ScanItem>();
                foreach (string d in emptyDirs)
                {
                    if (Targets.IsProtected(d) || Targets.IsUserData(d)) continue;
                    if (Targets.IsAppOwned(d)) continue;
                    if (d.TrimEnd('\\').Length <= 3) continue;
                    empties.Add(new ScanItem { Path = d, Size = 0, IsDir = true, Checked = true, Note = "Empty folder" });
                }
                empties.Sort(delegate(ScanItem a, ScanItem b) { return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); });
                AddCategory(result, "Empty folders", empties);
            }

            if (opt.LargeFiles)
            {
                var large = new List<ScanItem>();
                for (int i = 0; i < all.Count; i++)
                {
                    FileRec r = all[i];
                    if (r.Size < opt.LargeFileThreshold) continue;
                    if (Targets.IsProtected(r.Path)) continue;
                    large.Add(new ScanItem
                    {
                        Path = r.Path,
                        Size = r.Size,
                        Checked = false,
                        Note = "Check this yourself"
                    });
                }
                large.Sort(delegate(ScanItem a, ScanItem b) { return b.Size.CompareTo(a.Size); });
                AddCategory(result, "Large files", large);
            }

            if (opt.Dupes)
            {
                var dups = FindDuplicates(all, ct, progress);
                AddCategory(result, "Duplicates", dups);
            }

            return result;
        }

        private static void AddCategory(List<Category> result, string name, List<ScanItem> items)
        {
            if (items.Count == 0) return;
            var c = new Category { Name = name };
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Category = name;
                items[i].Prepare();
            }
            c.Items.AddRange(items);
            result.Add(c);
        }

        private static List<ScanItem> FindDuplicates(List<FileRec> all, CancellationToken ct, IProgress<ScanProgress> progress)
        {
            var result = new List<ScanItem>();
            var candidates = new List<FileRec>();
            for (int i = 0; i < all.Count; i++)
            {
                // A second copy of a DLL inside an app folder is not spare space, it is how the app
                // is laid out. Offering it for deletion would break the install, so skip those.
                if (all[i].Size < 1024 * 1024) continue;
                if (Targets.IsAppOwned(all[i].Path)) continue;
                if (Targets.IsProgramBinary(all[i].Path)) continue;
                candidates.Add(all[i]);
            }
            if (candidates.Count < 2) return result;

            var bySize = candidates.GroupBy(delegate(FileRec r) { return r.Size; })
                                   .Where(delegate(IGrouping<long, FileRec> g) { return g.Count() > 1; })
                                   .ToList();

            int groupNo = 0;
            long hashed = 0;
            foreach (var sizeGroup in bySize)
            {
                ct.ThrowIfCancellationRequested();

                var quick = sizeGroup.GroupBy(delegate(FileRec r)
                {
                    return QuickHash(r.Path);
                }).Where(delegate(IGrouping<string, FileRec> g) { return g.Count() > 1; });

                foreach (var qg in quick)
                {
                    ct.ThrowIfCancellationRequested();
                    var full = qg.GroupBy(delegate(FileRec r)
                    {
                        return FullHash(r.Path, ct);
                    }).Where(delegate(IGrouping<string, FileRec> g) { return g.Count() > 1; });

                    foreach (var fg in full)
                    {
                        groupNo++;
                        var list = fg.OrderBy(delegate(FileRec r) { return r.Path; }, StringComparer.OrdinalIgnoreCase).ToList();
                        for (int i = 0; i < list.Count; i++)
                        {
                            result.Add(new ScanItem
                            {
                                Path = list[i].Path,
                                Size = list[i].Size,
                                Checked = i > 0,
                                Keep = i == 0,
                                Note = i == 0 ? "Copy kept" : ("Duplicate #" + groupNo)
                            });
                        }
                    }
                    long hashedCount = Interlocked.Increment(ref hashed);
                    if (hashedCount % 20 == 0 && progress != null)
                        progress.Report(new ScanProgress { Files = hashedCount, Current = "Hashing duplicates..." });
                }
            }
            result.Sort(delegate(ScanItem a, ScanItem b) { return b.Size.CompareTo(a.Size); });
            return result;
        }

        private static string QuickHash(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024))
                {
                    var buf = new byte[64 * 1024];
                    int n = fs.Read(buf, 0, buf.Length);
                    using (var sha = SHA256.Create())
                        return Convert.ToBase64String(sha.ComputeHash(buf, 0, n));
                }
            }
            catch
            {
                return "ERR:" + path;
            }
        }

        private static string FullHash(string path, CancellationToken ct)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024))
                using (var sha = SHA256.Create())
                {
                    var buf = new byte[1024 * 1024];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                        sha.TransformBlock(buf, 0, n, buf, 0);
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    return Convert.ToBase64String(sha.Hash);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return "ERR:" + path;
            }
        }

        private static void WalkDeep(DirectoryInfo start, DeepCtx ctx, List<FileRec> files, List<string> emptyDirs)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                if (ctx.Ct.IsCancellationRequested) return;
                DirectoryInfo dir = stack.Pop();
                FileSystemInfo[] entries;
                try { entries = dir.GetFileSystemInfos(); }
                catch { continue; }

                if (entries.Length == 0)
                {
                    // Only truly empty folders are caught here. A folder holding nothing but empty
                    // subfolders shows up on a second scan, after the first pass removed them.
                    emptyDirs.Add(dir.FullName);
                    continue;
                }

                for (int i = 0; i < entries.Length; i++)
                {
                    FileSystemInfo e = entries[i];
                    try
                    {
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        DirectoryInfo d = e as DirectoryInfo;
                        if (d != null)
                        {
                            if (Targets.SkipDirName(d.Name)) continue;
                            if (Targets.IsProtected(d.FullName)) continue;
                            stack.Push(d);
                        }
                        else
                        {
                            long len;
                            try { len = ((FileInfo)e).Length; } catch { continue; }
                            files.Add(new FileRec { Path = e.FullName, Size = len });
                            long c = Interlocked.Increment(ref ctx.Count);
                            if (c % 500 == 0 && ctx.Progress != null)
                                ctx.Progress.Report(new ScanProgress { Files = c, Current = e.FullName });
                        }
                    }
                    catch { }
                }
            }
        }

        public static DeleteResult Delete(IList<ScanItem> items, bool permanent, IProgress<int> progress, CancellationToken ct)
        {
            var res = new DeleteResult();
            var targets = new List<ScanItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                ScanItem it = items[i];
                if (it.Keep) continue;
                if (Targets.IsProtected(it.Path))
                {
                    res.Failed++;
                    if (res.Errors.Count < 20) res.Errors.Add("Skipped, protected path: " + it.Path);
                    continue;
                }
                // Final guard: a selection can outlive the state it was made in, so re-check who owns
                // the path at delete time as well as at scan time.
                if (Targets.IsAppOwned(it.Path) || Targets.IsProgramBinary(it.Path))
                {
                    res.Failed++;
                    if (res.Errors.Count < 20) res.Errors.Add("Skipped, belongs to an installed program: " + it.Path);
                    continue;
                }

                // Never delete or traverse through a reparse point (symlink, mount point, junction).
                try
                {
                    if (File.Exists(it.Path) || Directory.Exists(it.Path))
                    {
                        FileAttributes attr = File.GetAttributes(it.Path);
                        if ((attr & FileAttributes.ReparsePoint) != 0)
                        {
                            res.Failed++;
                            if (res.Errors.Count < 20) res.Errors.Add("Skipped, reparse point: " + it.Path);
                            continue;
                        }
                    }
                }
                catch { }

                // Directory targets must stay empty at delete time.
                if (it.IsDir)
                {
                    try
                    {
                        if (Directory.Exists(it.Path) && Directory.GetFileSystemEntries(it.Path).Length > 0)
                        {
                            res.Failed++;
                            if (res.Errors.Count < 20) res.Errors.Add("Skipped, directory not empty: " + it.Path);
                            continue;
                        }
                    }
                    catch { }
                }

                if (!seenPaths.Add(it.Path)) continue;
                targets.Add(it);
            }

            if (permanent) DeletePermanent(targets, res, progress, ct);
            else DeleteRecycle(targets, res, progress, ct);
            return res;
        }

        private static void DeletePermanent(List<ScanItem> items, DeleteResult res, IProgress<int> progress, CancellationToken ct)
        {
            int done = 0;
            var po = new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct };
            try
            {
                Parallel.ForEach(items, po, it =>
                {
                    bool ok = false;
                    try
                    {
                        if (it.IsDir)
                        {
                            Directory.Delete(PathLong(it.Path), false);
                        }
                        else
                        {
                            try { File.SetAttributes(it.Path, FileAttributes.Normal); } catch { }
                            File.Delete(PathLong(it.Path));
                        }
                        ok = true;
                    }
                    catch (Exception ex)
                    {
                        lock (res.Errors)
                        {
                            res.Failed++;
                            if (res.Errors.Count < 20) res.Errors.Add(SafeName(it.Path) + " - " + ex.Message);
                        }
                    }

                    if (ok)
                    {
                        lock (res.Removed) res.Removed.Add(it);
                        Interlocked.Increment(ref res.Deleted);
                        Interlocked.Add(ref res.Freed, it.Size);
                    }

                    if (progress != null)
                    {
                        int n = Interlocked.Increment(ref done);
                        if (n % 25 == 0 || n == items.Count) progress.Report(n);
                    }
                });
            }
            catch (OperationCanceledException) { }
        }

        private static void DeleteRecycle(List<ScanItem> items, DeleteResult res, IProgress<int> progress, CancellationToken ct)
        {
            const int chunkSize = 200;
            int done = 0;
            for (int i = 0; i < items.Count; i += chunkSize)
            {
                if (ct.IsCancellationRequested) return;

                var chunk = new List<ScanItem>();
                for (int j = i; j < items.Count && j < i + chunkSize; j++) chunk.Add(items[j]);

                var sb = new StringBuilder();
                for (int j = 0; j < chunk.Count; j++) sb.Append(chunk[j].Path).Append('\0');
                sb.Append('\0');

                var op = new SHFILEOPSTRUCT();
                op.wFunc = FO_DELETE;
                op.pFrom = sb.ToString();
                op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR);
                try { SHFileOperation(ref op); }
                catch { }

                for (int j = 0; j < chunk.Count; j++)
                {
                    ScanItem it = chunk[j];
                    bool gone;
                    try { gone = !File.Exists(it.Path) && !Directory.Exists(it.Path); }
                    catch { gone = false; }

                    if (gone)
                    {
                        res.Removed.Add(it);
                        res.Deleted++;
                        res.Freed += it.Size;
                    }
                    else
                    {
                        res.Failed++;
                        if (res.Errors.Count < 20) res.Errors.Add("Could not reach the Recycle Bin: " + SafeName(it.Path));
                    }
                    done++;
                }
                if (progress != null) progress.Report(done);
            }
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path.TrimEnd('\\')); }
            catch { return path; }
        }

        private static string PathLong(string p)
        {
            if (p.Length < 240) return p;
            if (p.StartsWith(@"\\?\")) return p;
            if (p.StartsWith(@"\\")) return p;
            return @"\\?\" + p;
        }

        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
        private const ushort FOF_NOERRORUI = 0x0400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
