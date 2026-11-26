using System;
using System.Collections.Generic;

namespace Cleaner
{
    public static class ViewLogic
    {
        public static List<ScanItem> Build(List<ScanItem> source, string query, FilterMode mode, SortField field, bool ascending)
        {
            var list = new List<ScanItem>();
            string q = (query ?? "").Trim().ToLowerInvariant();

            for (int i = 0; i < source.Count; i++)
            {
                ScanItem it = source[i];
                if (mode == FilterMode.Checked && (!it.Checked || it.Keep)) continue;
                if (mode == FilterMode.Unchecked && it.Checked) continue;
                if (q.Length > 0 && it.LowerPath.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                list.Add(it);
            }

            Comparison<ScanItem> cmp;
            switch (field)
            {
                case SortField.Size:
                    cmp = delegate(ScanItem a, ScanItem b) { return a.Size.CompareTo(b.Size); };
                    break;
                case SortField.Location:
                    cmp = delegate(ScanItem a, ScanItem b)
                    {
                        return string.Compare(ParentOf(a.Path), ParentOf(b.Path), StringComparison.OrdinalIgnoreCase);
                    };
                    break;
                case SortField.Note:
                    cmp = delegate(ScanItem a, ScanItem b)
                    {
                        return string.Compare(a.Note ?? "", b.Note ?? "", StringComparison.OrdinalIgnoreCase);
                    };
                    break;
                default:
                    cmp = delegate(ScanItem a, ScanItem b)
                    {
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    };
                    break;
            }

            list.Sort(delegate(ScanItem a, ScanItem b)
            {
                int r = cmp(a, b);
                if (!ascending) r = -r;
                if (r != 0) return r;
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static string ParentOf(string path)
        {
            try
            {
                string p = path.TrimEnd('\\');
                string d = System.IO.Path.GetDirectoryName(p);
                return d ?? "";
            }
            catch { return ""; }
        }
    }
}
