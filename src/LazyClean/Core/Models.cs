using System;
using System.Collections.Generic;
using System.Threading;

namespace Cleaner
{
    public class ScanItem
    {
        public string Path;
        public long Size;
        public bool IsDir;
        public bool Checked;
        public bool Keep;
        public string Note;
        public string Category;
        public string LowerPath;

        public string Name;

        public void Prepare()
        {
            LowerPath = Path.ToLowerInvariant();
            string p = Path.TrimEnd('\\');
            try { Name = System.IO.Path.GetFileName(p); } catch { Name = p; }
            if (string.IsNullOrEmpty(Name)) Name = p;
        }

        public ScanItem Prepared()
        {
            Prepare();
            return this;
        }
    }

    public class Category
    {
        public string Name;
        public readonly List<ScanItem> Items = new List<ScanItem>();

        public void Add(ScanItem item)
        {
            lock (Items) Items.Add(item);
        }

        public long Size
        {
            get
            {
                long s = 0;
                lock (Items) for (int i = 0; i < Items.Count; i++) s += Items[i].Size;
                return s;
            }
        }

        public long SelectedSize
        {
            get
            {
                long s = 0;
                lock (Items) for (int i = 0; i < Items.Count; i++) if (Items[i].Checked && !Items[i].Keep) s += Items[i].Size;
                return s;
            }
        }

        public bool AllChecked
        {
            get
            {
                lock (Items)
                {
                    if (Items.Count == 0) return false;
                    for (int i = 0; i < Items.Count; i++)
                        if (!Items[i].Keep && !Items[i].Checked) return false;
                    return true;
                }
            }
        }

        public bool AnyChecked
        {
            get
            {
                lock (Items)
                {
                    for (int i = 0; i < Items.Count; i++)
                        if (!Items[i].Keep && Items[i].Checked) return true;
                    return false;
                }
            }
        }
    }

    public class ScanProgress
    {
        public long Files;
        public string Current;
    }

    public class ScanOptions
    {
        public bool Junk = true;
        public bool Dupes = true;
        public bool EmptyFolders = true;
        public bool LargeFiles = true;
        public string Root;
        public long LargeFileThreshold = 500L * 1024 * 1024;
    }

    public class DeleteResult
    {
        public int Deleted;
        public int Failed;
        public long Freed;
        public readonly List<ScanItem> Removed = new List<ScanItem>();
        public readonly List<string> Errors = new List<string>();
    }

    public class TargetDef
    {
        public string Category;
        public string Path;
        public bool DefaultOn = true;
        public string Note;
    }

    internal class FileRec
    {
        public string Path;
        public long Size;
    }

    internal class DeepCtx
    {
        public CancellationToken Ct;
        public IProgress<ScanProgress> Progress;
        public long Count;
    }

    public enum SortField
    {
        Name,
        Size,
        Location,
        Note
    }

    public enum FilterMode
    {
        All,
        Checked,
        Unchecked
    }

    public static class Util
    {
        public static string Fmt(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double v = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int i = -1;
            do
            {
                v /= 1024.0;
                i++;
            }
            while (v >= 1024.0 && i < units.Length - 1);
            string fmt = v >= 100 ? "0" : (v >= 10 ? "0.0" : "0.00");
            return v.ToString(fmt) + " " + units[i];
        }
    }
}
