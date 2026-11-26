using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cleaner
{
    public class MainForm : Form
    {
        private List<Category> cats = new List<Category>();
        private List<Category> categories = new List<Category>();
        private List<ScanItem> current = new List<ScanItem>();
        private List<ScanItem> view = new List<ScanItem>();

        private CancellationTokenSource cts;
        private bool busy;
        private bool toolsBusy;
        private bool syncing;

        private SortField sortField = SortField.Size;
        private bool sortAscending = false;
        private FilterMode filterMode = FilterMode.All;
        private string query = "";

        private Label lblFree;
        private Label lblStatus;
        private Label lblSelected;
        private Label lblFreeSpace;
        private ProgressBar progress;
        private Button btnScanReg;
        private Button btnScanDeep;
        private Button btnCancel;
        private Button btnSelectAll;
        private Button btnSelectNone;
        private Button btnDelete;
        private Button btnWinOld;
        private CheckBox chkPermanent;
        private CheckBox chkDeepJunk;
        private CheckBox chkDeepDup;
        private CheckBox chkDeepEmpty;
        private CheckBox chkDeepLarge;
        private ListView lvCats;
        private DataGridView grid;
        private SplitContainer split;
        private Panel emptyHost;
        private Panel right;
        private Panel bar;
        private Label emptyTitle;
        private Label emptyBody;
        private TextBox txtOutput;
        private TextBox txtSearch;
        private Label lblFilterCount;
        private Button btnRefresh;
        private Button btnPrev;
        private Button btnNext;
        private bool hasScanned;
        private bool lastScanWasDeep;
        private readonly List<Button> filterChips = new List<Button>();
        private readonly List<Button> toolButtons = new List<Button>();

        private int searchTick;

        public MainForm()
        {
            Text = "lazy clean";
            Size = new Size(1240, 820);
            MinimumSize = new Size(1040, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Canvas;
            ForeColor = Theme.Body;
            Font = Theme.UiFont(9F, FontStyle.Regular);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildUi();
            RefreshFree();
            UpdateFooter();
            UpdateChips();
            RefreshView(false);

            Shown += delegate
            {
                try { split.SplitterDistance = 428; }
                catch { }
                RefreshView(false);
                txtSearch.Focus();
                if (Environment.GetEnvironmentVariable("CLEANER_DEMO") == "1") LoadDemoRows();
            };
        }

        private void BuildUi()
        {
            SuspendLayout();

            var header = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Theme.Canvas };

            // The app icon shown at real size, so the window matches the taskbar entry.
            var mark = new PictureBox
            {
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(20, 20),
                BackColor = Color.Transparent
            };
            try { mark.Image = Icon != null ? Icon.ToBitmap() : null; }
            catch { mark.Image = null; }

            var title = new Label
            {
                Text = "lazy clean",
                Font = Theme.UiFont(16F, FontStyle.Bold),
                ForeColor = Theme.Body,
                AutoSize = true,
                Location = new Point(72, 14)
            };
            var subtitle = new Label
            {
                Text = "Finds junk on this PC. You pick what goes. Nothing is deleted until you say so.",
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(74, 46)
            };
            lblFree = new Label
            {
                AutoSize = false,
                Size = new Size(260, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(9F, FontStyle.Regular),
                Location = new Point(header.ClientSize.Width - 280, 24)
            };
            header.Controls.Add(lblFree);
            header.Controls.Add(mark);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Line });
            header.Resize += delegate
            {
                lblFree.Location = new Point(header.ClientSize.Width - 280, 24);
            };

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(18, 8),
                Font = Theme.UiFont(9.5F, FontStyle.Regular),
                DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(132, 30)
            };
            tabs.DrawItem += TabsDrawItem;
            tabs.BackColor = Theme.Canvas;
            var pageClean = new TabPage("Clean") { BackColor = Theme.Canvas, Padding = new Padding(12) };
            var pageTools = new TabPage("System tools") { BackColor = Theme.Canvas, Padding = new Padding(12) };
            tabs.TabPages.Add(pageClean);
            tabs.TabPages.Add(pageTools);

            BuildCleanPage(pageClean);
            BuildToolsPage(pageTools);

            Controls.Add(tabs);
            Controls.Add(header);
            ResumeLayout();
        }

        private void BuildCleanPage(TabPage page)
        {
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Canvas };
            lblSelected = new Label
            {
                AutoSize = true,
                Location = new Point(4, 14),
                Font = Theme.UiFont(11.5F, FontStyle.Bold),
                ForeColor = Theme.Body
            };
            // The freed size is the number people actually came for, so it gets its own line and the
            // accent colour instead of being one more clause inside the count.
            lblFreeSpace = new Label
            {
                AutoSize = true,
                Location = new Point(4, 38),
                Font = Theme.UiFont(9F, FontStyle.Regular),
                ForeColor = Theme.Muted
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(0, 12, 0, 0)
            };
            chkPermanent = new CheckBox
            {
                Text = "Delete permanently (no undo)",
                AutoSize = true,
                ForeColor = Theme.Muted,
                BackColor = Theme.Canvas,
                Margin = new Padding(4, 10, 16, 0)
            };
            btnSelectNone = SecondaryButton("Clear selection", 160);
            btnSelectNone.Click += delegate { SetViewChecked(false); };
            btnSelectAll = SecondaryButton("Select all", 108);
            btnSelectAll.Click += delegate { SetViewChecked(true); };
            btnDelete = PrimaryButton("Delete selected", 156);
            btnDelete.Click += delegate { DeleteSelected(); };
            flow.Controls.Add(chkPermanent);
            flow.Controls.Add(btnSelectNone);
            flow.Controls.Add(btnSelectAll);
            flow.Controls.Add(btnDelete);
            footer.Controls.Add(lblSelected);
            footer.Controls.Add(lblFreeSpace);
            footer.Controls.Add(flow);

            var scanPanel = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Theme.Canvas };
            btnScanReg = PrimaryButton("Quick scan", 132);
            btnScanReg.Location = new Point(2, 4);
            btnScanReg.Click += delegate { StartScan(false); };

            btnScanDeep = SecondaryButton("Full drive scan", 160);
            btnScanDeep.Location = new Point(144, 4);
            btnScanDeep.Click += delegate { StartScan(true); };

            btnCancel = TertiaryButton("Cancel");
            btnCancel.Location = new Point(314, 4);
            btnCancel.Size = new Size(88, 40);
            btnCancel.Enabled = false;
            btnCancel.Click += delegate { if (cts != null) cts.Cancel(); };

            lblStatus = new Label
            {
                AutoSize = false,
                Location = new Point(414, 14),
                Size = new Size(650, 20),
                ForeColor = Theme.Muted,
                AutoEllipsis = true
            };

            progress = new ProgressBar
            {
                Location = new Point(2, 50),
                Size = new Size(1088, 5),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0,
                Visible = false
            };

            var deepLabel = new Label
            {
                Text = "What the full drive scan looks for",
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(8.5F, FontStyle.Bold),
                Location = new Point(2, 66)
            };

            chkDeepJunk = DeepCheck("Junk files", 0, 86);
            chkDeepDup = DeepCheck("Duplicates", 150, 86);
            chkDeepEmpty = DeepCheck("Empty folders", 272, 86);
            chkDeepLarge = DeepCheck("Files over 500 MB", 412, 86);

            scanPanel.Controls.Add(deepLabel);
            scanPanel.Controls.Add(chkDeepJunk);
            scanPanel.Controls.Add(chkDeepDup);
            scanPanel.Controls.Add(chkDeepEmpty);
            scanPanel.Controls.Add(chkDeepLarge);
            scanPanel.Controls.Add(progress);
            scanPanel.Controls.Add(lblStatus);
            scanPanel.Controls.Add(btnCancel);
            scanPanel.Controls.Add(btnScanDeep);
            scanPanel.Controls.Add(btnScanReg);

            scanPanel.Resize += delegate
            {
                int w = scanPanel.ClientSize.Width;
                progress.Width = Math.Max(60, w - 4);
                lblStatus.Width = Math.Max(120, w - 424);
            };

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };

            split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Theme.Canvas
            };

            lvCats = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Card,
                ForeColor = Theme.Body,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = Theme.UiFont(9F, FontStyle.Regular),
                // A little left padding so the accent bar on the active row sits inside the control
                // instead of being clipped by its edge.
                Padding = new Padding(2, 0, 0, 0)
            };
            lvCats.Columns.Add("Category", 168);
            lvCats.Columns.Add("Items", 52, HorizontalAlignment.Right);
            lvCats.Columns.Add("Size", 74, HorizontalAlignment.Right);
            lvCats.Columns.Add("To delete", 84, HorizontalAlignment.Right);
            lvCats.OwnerDraw = true;
            lvCats.DrawColumnHeader += CatDrawHeader;
            lvCats.DrawItem += CatDrawItem;
            lvCats.DrawSubItem += CatDrawSubItem;
            lvCats.ItemChecked += CatsItemChecked;
            lvCats.SelectedIndexChanged += delegate
            {
                ShowCategory(lvCats.SelectedIndices.Count > 0 ? lvCats.SelectedIndices[0] : -1);
                UpdateNav();
            };
            var catHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(8, 4, 4, 4) };
            catHost.Controls.Add(lvCats);
            split.Panel1.Controls.Add(catHost);

            right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };

            // item toolbar: search + filter chips + result count
            bar = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Card, Padding = new Padding(12, 10, 12, 0) };
            var searchLabel = new Label
            {
                Text = "Search this list",
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(8.5F, FontStyle.Bold),
                Location = new Point(12, 10)
            };
            txtSearch = new TextBox
            {
                Location = new Point(12, 30),
                Size = new Size(360, 30),
                BackColor = Theme.Inset,
                ForeColor = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.UiFont(10F, FontStyle.Regular)
            };
            txtSearch.TextChanged += delegate { ScheduleSearch(); };
            txtSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    txtSearch.Clear();
                    e.SuppressKeyPress = true;
                }
            };

            var chipRow = new FlowLayoutPanel
            {
                Location = new Point(384, 28),
                Size = new Size(274, 34),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Card
            };
            AddChip(chipRow, "All", FilterMode.All);
            AddChip(chipRow, "Checked", FilterMode.Checked);
            AddChip(chipRow, "Unchecked", FilterMode.Unchecked);

            lblFilterCount = new Label
            {
                AutoSize = false,
                Location = new Point(12, 64),
                Size = new Size(500, 22),
                ForeColor = Theme.Body,
                Font = Theme.UiFont(9.5F, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            btnRefresh = TertiaryButton("Refresh");
            btnRefresh.Size = new Size(84, 26);
            btnRefresh.ForeColor = Theme.Body;
            btnRefresh.Click += delegate { RefreshAll(); };

            btnPrev = TertiaryButton("Previous");
            btnPrev.Size = new Size(88, 26);
            btnPrev.ForeColor = Theme.Body;
            btnPrev.Click += delegate { StepCategory(-1); };

            btnNext = TertiaryButton("Next");
            btnNext.Size = new Size(72, 26);
            btnNext.ForeColor = Theme.Body;
            btnNext.Click += delegate { StepCategory(1); };

            bar.Controls.Add(searchLabel);
            bar.Controls.Add(txtSearch);
            bar.Controls.Add(chipRow);
            bar.Controls.Add(lblFilterCount);
            bar.Controls.Add(btnRefresh);
            bar.Controls.Add(btnPrev);
            bar.Controls.Add(btnNext);
            bar.Resize += delegate
            {
                int w = bar.ClientSize.Width;
                const int chipsW = 274;
                int searchW = Math.Max(160, Math.Min(360, w - chipsW - 36));
                txtSearch.Width = searchW;
                chipRow.Location = new Point(12 + searchW + 12, 28);
                chipRow.Width = chipsW;
                btnNext.Location = new Point(Math.Max(500, w - 78), 62);
                btnPrev.Location = new Point(Math.Max(406, w - 172), 62);
                btnRefresh.Location = new Point(Math.Max(316, w - 262), 62);
                lblFilterCount.Location = new Point(12, 64);
                lblFilterCount.Width = Math.Max(220, w - 276);
            };

            // grid: virtual mode so six figures of rows still scroll smoothly
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                VirtualMode = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BackgroundColor = Theme.Card,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                EnableHeadersVisualStyles = false,
                GridColor = Theme.Line,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34,
                Font = Theme.UiFont(9F, FontStyle.Regular),
                RowTemplate = { Height = 26 }
            };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Inset;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Inset;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiFont(8.5F, FontStyle.Bold);
            // No header padding: DataGridView subtracts it from the available width, which clipped
            // the "Delete" header. The arrow instead gets its own grab of space in GridCellPainting.
            grid.DefaultCellStyle.BackColor = Theme.Card;
            grid.DefaultCellStyle.ForeColor = Theme.Body;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Card;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Body;

            var colCheck = new DataGridViewCheckBoxColumn
            {
                Width = 52,
                HeaderText = "Delete",
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Frozen = false
            };
            var colName = new DataGridViewTextBoxColumn
            {
                HeaderText = "Name",
                Width = 190,
                MinimumWidth = 120,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                Tag = SortField.Name
            };
            var colSize = new DataGridViewTextBoxColumn
            {
                HeaderText = "Size",
                Width = 88,
                MinimumWidth = 70,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                Tag = SortField.Size
            };
            colSize.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colSize.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            // Location takes the leftover width: it is the only value that is routinely long.
            var colPath = new DataGridViewTextBoxColumn
            {
                HeaderText = "Location",
                Width = 320,
                MinimumWidth = 160,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                Tag = SortField.Location
            };
            var colNote = new DataGridViewTextBoxColumn
            {
                HeaderText = "Note",
                Width = 128,
                MinimumWidth = 100,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                Tag = SortField.Note
            };
            colCheck.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns.AddRange(new DataGridViewColumn[]
            {
                colCheck, colName, colSize, colPath, colNote
            });

            colCheck.DefaultCellStyle.BackColor = Theme.Card;
            colCheck.DefaultCellStyle.SelectionBackColor = Theme.Card;
            grid.CellPainting += GridCellPainting;

            grid.CellValueNeeded += GridCellValueNeeded;
            grid.CellValuePushed += GridCellValuePushed;
            grid.CellValueChanged += GridCellValueChanged;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellDoubleClick += GridDoubleClick;
            grid.ColumnHeaderMouseClick += GridHeaderClick;
            grid.ColumnHeaderMouseDoubleClick += GridHeaderClick;

            emptyHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };

            // Positioned by hand rather than docked or wrapped in a TableLayoutPanel: this panel only
            // exists to say "nothing here", so two labels and an explicit centre are enough.
            emptyTitle = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Body,
                Font = Theme.UiFont(12F, FontStyle.Bold)
            };
            emptyBody = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(9.5F, FontStyle.Regular)
            };
            emptyHost.Controls.Add(emptyTitle);
            emptyHost.Controls.Add(emptyBody);
            emptyHost.Resize += delegate { CenterEmpty(); };

            // Dock order matters: controls are laid out from the last added to the first, so the
            // toolbar is added before the two Fill views to keep its strip reserved.
            right.Controls.Add(grid);
            right.Controls.Add(emptyHost);
            right.Controls.Add(bar);

            split.Panel2.Controls.Add(right);

            body.Controls.Add(split);
            page.Controls.Add(body);
            page.Controls.Add(scanPanel);
            page.Controls.Add(footer);

            SetEmpty("Nothing scanned yet", "Quick scan covers temp files, update cache and logs. Full drive scan walks the whole disk and also finds duplicates, empty folders and files over 500 MB.");
            UpdateSortGlyphs();
        }

        private void TabsDrawItem(object sender, DrawItemEventArgs e)
        {
            var tabs = (TabControl)sender;
            TabPage page = tabs.TabPages[e.Index];
            bool active = tabs.SelectedIndex == e.Index;
            var rect = e.Bounds;

            // Tabs sit on the canvas and the active one lifts to the card colour, with a short
            // accent rule under it. Everything else stays flat so the tab strip reads as a header.
            using (var back = new SolidBrush(Theme.Canvas))
                e.Graphics.FillRectangle(back, rect);
            if (active)
            {
                var body = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height - 2);
                using (var lift = new SolidBrush(Theme.Card))
                    e.Graphics.FillRectangle(lift, body);
                using (var ink = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(ink, body.Left + 12, body.Bottom - 2, body.Width - 24, 2);
            }
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(e.Graphics, page.Text, Theme.UiFont(9.5F, active ? FontStyle.Bold : FontStyle.Regular),
                rect, active ? Theme.Body : Theme.Muted, flags);
        }

        private void CatDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var back = new SolidBrush(Theme.Inset))
                e.Graphics.FillRectangle(back, e.Bounds);
            using (var line = new Pen(Theme.Line))
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            flags |= e.ColumnIndex == 0 ? TextFormatFlags.Left : TextFormatFlags.Right;
            var rect = e.Bounds;
            // Right-aligned headers only need a hair of padding; 8px on each side was clipping
            // the shortest header on the strip.
            rect.Inflate(e.ColumnIndex == 0 ? -8 : -4, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.UiFont(8.5F, FontStyle.Bold), rect, Theme.Muted, flags);
        }

        // Rows are painted by hand for one reason: the row you are looking at has to be obvious.
        // ListView's native selection is a system blue that fights the accent, and it disappears
        // the moment focus moves, which leaves no answer to "which category is on the right".
        private void CatDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool active = lvCats.SelectedIndices.Contains(e.ItemIndex);

            using (var fill = new SolidBrush(active ? Theme.AccentSoft : Theme.Card))
                e.Graphics.FillRectangle(fill, e.Bounds);

            using (var line = new Pen(Theme.Line))
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
        }

        private void CatDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool active = lvCats.SelectedIndices.Contains(e.ItemIndex);
            Color ink = active ? Theme.AccentDeep : (e.ColumnIndex == 0 ? Theme.Body : Theme.Muted);
            Font font = Theme.UiFont(9F, e.ColumnIndex == 0 ? FontStyle.Bold : FontStyle.Regular);

            if (active && e.ColumnIndex == 0)
            {
                // The bar lives inside the first cell, not at the control edge, because the ListView
                // clips anything drawn on its own border and the bar lost its bottom half there.
                using (var accentBar = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(accentBar, e.Bounds.Left + 1, e.Bounds.Top, 3, e.Bounds.Height);
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            flags |= e.ColumnIndex == 0 ? TextFormatFlags.Left : TextFormatFlags.Right;
            var rect = e.Bounds;
            rect.Inflate(e.ColumnIndex == 0 ? -8 : -4, 0);
            // Leave room for the checkbox the ListView draws and for the accent bar.
            if (e.ColumnIndex == 0)
            {
                rect.X += 20;
                rect.Width -= 4;
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, rect, ink, flags);
        }

        private CheckBox DeepCheck(string text, int x, int y)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = true,
                Location = new Point(x, y),
                ForeColor = Theme.Body,
                BackColor = Theme.Canvas
            };
        }

        private void AddChip(FlowLayoutPanel row, string text, FilterMode mode)
        {
            int chipWidth = 64;
            if (mode == FilterMode.Checked) chipWidth = 88;
            else if (mode == FilterMode.Unchecked) chipWidth = 104;

            var b = new Button
            {
                Text = text,
                Size = new Size(chipWidth, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Inset,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(8.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Tag = mode
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += delegate(object s, EventArgs e)
            {
                filterMode = (FilterMode)((Button)s).Tag;
                RefreshView(true);
                UpdateChips();
            };
            filterChips.Add(b);
            row.Controls.Add(b);
        }

        private void UpdateChips()
        {
            for (int i = 0; i < filterChips.Count; i++)
            {
                Button b = filterChips[i];
                bool active = (FilterMode)b.Tag == filterMode;
                b.BackColor = active ? Theme.Accent : Theme.Inset;
                b.ForeColor = active ? Theme.Card : Theme.Muted;
                b.FlatAppearance.MouseOverBackColor = active ? Theme.Accent : Theme.Line;
                b.FlatAppearance.MouseDownBackColor = active ? Theme.Accent : Theme.Line;
            }
        }

        private void BuildToolsPage(TabPage page)
        {
            // Grouped by what the task touches, because eight identical buttons gave no hint which
            // one is routine and which one cannot be undone.
            var top = new Panel { Dock = DockStyle.Top, Height = 246, BackColor = Theme.Canvas };

            int y = 4;
            y = ToolGroup(top, "Windows component store", y, new ToolSpec[]
            {
                new ToolSpec("Analyze first", "Reads the store and reports what can be reclaimed. Changes nothing.",
                    delegate { RunTool("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore", "Analyze component store", null); }),
                new ToolSpec("Clean up", "Removes superseded components. Takes several minutes.",
                    delegate
                    {
                        RunTool("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", "Clean component store",
                            "Windows will remove superseded components from the component store.\nThis can take several minutes. Continue?");
                    }),
                new ToolSpec("ResetBase", "Also drops every rollback copy. Installed updates can never be uninstalled again.",
                    delegate
                    {
                        RunTool("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /ResetBase", "DISM ResetBase",
                            "ResetBase removes every previous version of a component.\nAfter this, installed Windows updates can no longer be uninstalled.\n\nContinue?");
                    })
            });

            y = ToolGroup(top, "System file compression", y, new ToolSpec[]
            {
                new ToolSpec("Check status", "Only reports whether CompactOS is on. Changes nothing.",
                    delegate { RunTool("compact.exe", "/CompactOS:query", "CompactOS query", null); }),
                new ToolSpec("Turn on", "Compresses system files to save disk space. Takes a few minutes.",
                    delegate
                    {
                        RunTool("compact.exe", "/CompactOS:ALWAYS", "CompactOS ALWAYS",
                            "Windows compresses system files the same way the default CompactOS image does.\nTakes a few minutes. Continue?");
                    }),
                new ToolSpec("Turn off", "System files go back to normal size and use more disk space.",
                    delegate
                    {
                        RunTool("compact.exe", "/CompactOS:NEVER", "CompactOS NEVER",
                            "CompactOS compression is turned off and system files use more disk space. Continue?");
                    })
            });

            ToolGroup(top, "One-off cleanups", y, new ToolSpec[]
            {
                new ToolSpec("Empty Recycle Bin", "Permanently removes everything already in the Recycle Bin.",
                    delegate { EmptyRecycleBin(); }),
                new ToolSpec("Turn off hibernation", "Deletes hiberfil.sys and disables fast startup.",
                    delegate
                    {
                        RunTool("powercfg.exe", "/h off", "Turn off hibernation",
                            "Deletes hiberfil.sys and disables hibernation and fast startup. Continue?");
                    }),
                new ToolSpec("Delete Windows.old", "Frees the old install. You lose the ability to roll back.",
                    delegate
                    {
                        RunTool("cmd.exe",
                            "/c takeown /f \"C:\\Windows.old\" /r /skipsl /d y & icacls \"C:\\Windows.old\" /grant *S-1-5-32-544:F /t /c /l & rd /s /q \"C:\\Windows.old\"",
                            "Delete Windows.old",
                            "Removing Windows.old means you cannot roll back to the previous Windows install.\nThis can take a long time. Continue?");
                    },
                    delegate(Button b) { btnWinOld = b; })
            });
            if (btnWinOld != null) btnWinOld.Enabled = Directory.Exists(@"C:\Windows.old");

            var outLabel = new Label
            {
                Text = "Result",
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(8.5F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 22
            };

            txtOutput = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = Theme.MonoFont(9F),
                BackColor = Theme.Card,
                ForeColor = Theme.Body,
                BorderStyle = BorderStyle.None,
                Text = "Pick a task above. Its output shows here."
            };

            var outHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(12) };
            outHost.Controls.Add(txtOutput);

            page.Controls.Add(outHost);
            page.Controls.Add(outLabel);
            page.Controls.Add(top);
        }

        // One task: a plain name, a sentence saying what it does, and the work itself.
        private class ToolSpec
        {
            public readonly string Name;
            public readonly string Note;
            public readonly EventHandler Work;
            public readonly Action<Button> Created;

            public ToolSpec(string name, string note, EventHandler work)
                : this(name, note, work, null)
            {
            }

            public ToolSpec(string name, string note, EventHandler work, Action<Button> created)
            {
                Name = name;
                Note = note;
                Work = work;
                Created = created;
            }
        }

        // A titled row of tasks. Returns the Y to start the next group at.
        private int ToolGroup(Control parent, string title, int y, ToolSpec[] specs)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                Location = new Point(2, y),
                ForeColor = Theme.Body,
                Font = Theme.UiFont(9F, FontStyle.Bold)
            };
            parent.Controls.Add(label);

            var row = new FlowLayoutPanel
            {
                Location = new Point(0, y + 22),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                BackColor = Theme.Canvas,
                Width = 1120
            };
            parent.Resize += delegate
            {
                row.Width = Math.Max(340, parent.ClientSize.Width);
            };
            for (int i = 0; i < specs.Length; i++)
            {
                ToolSpec spec = specs[i];
                var cell = new Panel { Width = 326, Height = 54, BackColor = Theme.Canvas, Margin = new Padding(0, 0, 8, 0) };
                Button b = ToolButton(spec.Name, spec.Work);
                b.Width = 148;
                b.Height = 32;
                b.Location = new Point(0, 0);
                if (spec.Created != null) spec.Created(b);
                var note = new Label
                {
                    Text = spec.Note,
                    AutoSize = false,
                    Size = new Size(172, 46),
                    Location = new Point(154, 2),
                    ForeColor = Theme.Muted,
                    Font = Theme.UiFont(8.5F, FontStyle.Regular)
                };
                cell.Controls.Add(b);
                cell.Controls.Add(note);
                row.Controls.Add(cell);
            }
            parent.Controls.Add(row);
            return y + 22 + 54 + 12;
        }

        private Button ToolButton(string text, EventHandler click)
        {
            var b = SecondaryButton(text, 216);
            b.Height = 34;
            b.Margin = new Padding(0, 0, 8, 8);
            b.Click += click;
            toolButtons.Add(b);
            return b;
        }

        private static Button PrimaryButton(string text, int width)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(width, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Accent,
                ForeColor = Theme.Card,
                Font = Theme.UiFont(9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentDeep;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentDeep;
            return b;
        }

        private static Button SecondaryButton(string text, int width)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(width, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Card,
                ForeColor = Theme.Body,
                Font = Theme.UiFont(9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Theme.Inset;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentSoft;
            return b;
        }

        private static Button TertiaryButton(string text)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(110, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Theme.Muted,
                Font = Theme.UiFont(9F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Inset;
            return b;
        }

        private void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // Sort arrow on whichever column is driving the order. Drawn here, not in the header
            // text, so the arrow has its own space and the header keeps its plain name.
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                DataGridViewColumn col = grid.Columns[e.ColumnIndex];
                if (col.Tag is SortField && (SortField)col.Tag == sortField)
                {
                    e.PaintBackground(e.CellBounds, false);
                    e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground);

                    // The triangle goes into the trailing gutter DataGridView leaves in a header,
                    // so it never overlaps the text and no padding is taken from the column.
                    int midY = e.CellBounds.Top + e.CellBounds.Height / 2;
                    int arrowX = e.CellBounds.Right - 12;
                    Point[] tri = sortAscending
                        ? new Point[] { new Point(arrowX, midY + 2), new Point(arrowX + 7, midY + 2), new Point(arrowX + 3, midY - 3) }
                        : new Point[] { new Point(arrowX, midY - 3), new Point(arrowX + 7, midY - 3), new Point(arrowX + 3, midY + 2) };
                    using (var ink = new SolidBrush(Theme.Accent))
                        e.Graphics.FillPolygon(ink, tri);

                    e.Handled = true;
                    return;
                }
            }

            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= view.Count) return;

            e.PaintBackground(e.CellBounds, false);

            ScanItem it = view[e.RowIndex];
            int size = 14;
            var box = new Rectangle(
                e.CellBounds.Left + (e.CellBounds.Width - size) / 2,
                e.CellBounds.Top + (e.CellBounds.Height - size) / 2,
                size, size);

            using (var fill = new SolidBrush(it.Keep ? Theme.Inset : (it.Checked ? Theme.Accent : Theme.Card)))
                e.Graphics.FillRectangle(fill, box);
            // Border rather than Line: a checkbox edge is a control boundary and has to clear 3:1.
            using (var edge = new Pen(it.Keep ? Theme.Border : (it.Checked ? Theme.Accent : Theme.Border)))
                e.Graphics.DrawRectangle(edge, box);

            if (it.Checked && !it.Keep)
            {
                // Drawn by hand: a white tick on the accent fill, readable at 14px.
                using (var tick = new Pen(Theme.Card, 2F))
                {
                    tick.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    tick.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    e.Graphics.DrawLines(tick, new Point[]
                    {
                        new Point(box.Left + 3, box.Top + 7),
                        new Point(box.Left + 6, box.Top + 10),
                        new Point(box.Left + 11, box.Top + 4)
                    });
                }
            }

            if (it.Keep)
            {
                // "Kept" is shown as a shape, not a colour alone: a short bar that reads as a lock.
                using (var lockInk = new SolidBrush(Theme.Quiet))
                    e.Graphics.FillRectangle(lockInk, box.Left + 4, box.Top + 5, 6, 2);
            }
            e.Handled = true;
        }

        private void GridCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            ScanItem it = view[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0:
                    e.Value = it.Checked;
                    break;
                case 1:
                    e.Value = it.Name;
                    break;
                case 2:
                    e.Value = Util.Fmt(it.Size);
                    break;
                case 3:
                    e.Value = ViewLogic.ParentOf(it.Path);
                    break;
                case 4:
                    e.Value = it.Note ?? "";
                    break;
            }
        }

        private void GridCellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            view[e.RowIndex].Checked = Convert.ToBoolean(e.Value);
        }

        private void GridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            object v = grid.Rows[e.RowIndex].Cells[0].Value;
            view[e.RowIndex].Checked = v is bool && (bool)v;
            RefreshCatStates();
            UpdateFooter();
            UpdateFilterCount();
        }

        private void GridDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            ScanItem it = view[e.RowIndex];
            try
            {
                string safePath = it.Path.Replace("\"", "");
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                if (it.IsDir) Process.Start(explorer, "\"" + safePath + "\"");
                else Process.Start(explorer, "/select,\"" + safePath + "\"");
            }
            catch { }
        }

        private void GridHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            object tag = grid.Columns[e.ColumnIndex].Tag;
            if (!(tag is SortField)) return;

            SortField field = (SortField)tag;
            if (field == sortField) sortAscending = !sortAscending;
            else
            {
                sortField = field;
                sortAscending = field == SortField.Name || field == SortField.Location || field == SortField.Note;
            }
            UpdateSortGlyphs();
            RefreshView(true);
        }

        private void UpdateSortGlyphs()
        {
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn col = grid.Columns[i];
                if (!(col.Tag is SortField)) continue;
                string label;
                switch ((SortField)col.Tag)
                {
                    case SortField.Name:
                        label = "Name";
                        break;
                    case SortField.Size:
                        label = "Size";
                        break;
                    case SortField.Location:
                        label = "Location";
                        break;
                    default:
                        label = "Note";
                        break;
                }
                // The sort direction is painted as an arrow in GridCellPainting rather than
                // appended to the header text, so the header stays the word it names and the
                // arrow cannot collide with it in a narrow column.
                col.HeaderText = label;
            }
        }

        private bool SortActive(SortField want, bool asc)
        {
            return sortField == want && sortAscending == asc;
        }

        // Sample rows for screenshots only, switched on with CLEANER_DEMO=1. The paths are fictional
        // so a screenshot never implies something was found on this machine.
        public void LoadDemoRows()
        {
            var junk = new List<ScanItem>();
            string[] paths =
            {
                @"C:\Users\amy\AppData\Local\Temp\chrome_installer.tmp",
                @"C:\Users\amy\AppData\Local\Temp\setup_log.txt",
                @"C:\Windows\Temp\MpCmdRun.log",
                @"C:\Users\amy\AppData\Local\CrashDumps\code.dmp",
                @"C:\$WinREAgent\Scratch\staging.part"
            };
            long[] sizes = { 24L * 1024 * 1024, 112L * 1024, 3L * 1024 * 1024, 402L * 1024 * 1024, 88L * 1024 };
            for (int i = 0; i < paths.Length; i++)
            {
                junk.Add(new ScanItem
                {
                    Path = paths[i],
                    Size = sizes[i],
                    Checked = i != 3,
                    Note = "Junk file",
                    Category = "Junk files"
                }.Prepared());
            }

            var empties = new List<ScanItem>();
            string[] dirs =
            {
                @"C:\ProgramData\Package Cache\{1f2e3d}\layout",
                @"C:\Users\amy\AppData\Local\Packages\Temp\Sessions"
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                empties.Add(new ScanItem
                {
                    Path = dirs[i],
                    Size = 0,
                    IsDir = true,
                    Checked = true,
                    Note = "Empty folder",
                    Category = "Empty folders"
                }.Prepared());
            }

            var dupes = new List<ScanItem>();
            string[] dupPaths =
            {
                @"C:\Users\amy\Downloads\installer.msi",
                @"C:\Users\amy\Downloads\installer (1).msi",
                @"C:\Users\amy\Desktop\backup\installer.msi"
            };
            for (int i = 0; i < dupPaths.Length; i++)
            {
                dupes.Add(new ScanItem
                {
                    Path = dupPaths[i],
                    Size = 286L * 1024 * 1024,
                    Checked = i > 0,
                    Keep = i == 0,
                    Note = i == 0 ? "Copy kept" : ("Duplicate #" + i),
                    Category = "Duplicates"
                }.Prepared());
            }

            cats = new List<Category>
            {
                new Category { Name = "Junk files" },
                new Category { Name = "Empty folders" },
                new Category { Name = "Duplicates" }
            };
            cats[0].Items.AddRange(junk);
            cats[1].Items.AddRange(empties);
            cats[2].Items.AddRange(dupes);
            RebuildCategories();
        }

        private void RefreshView(bool resetScroll)
        {
            view = ViewLogic.Build(current, query, filterMode, sortField, sortAscending);

            // A re-created grid resets its row count, so only touch it when the size changed.
            if (grid.RowCount != view.Count)
            {
                grid.RowCount = 0;
                grid.RowCount = view.Count;
            }
            if (resetScroll && view.Count > 0 && grid.IsHandleCreated && grid.Height > 0)
            {
                try { grid.FirstDisplayedScrollingRowIndex = 0; }
                catch (InvalidOperationException) { }
            }
            if (view.Count > 0) grid.ClearSelection();

            // The toolbar (search, filter chips, result count) stays visible in every state: when a
            // search or filter returns nothing, that toolbar is the only way back out. Only the grid
            // area swaps for the empty panel.
            grid.Visible = view.Count > 0;
            emptyHost.Visible = view.Count == 0;
            if (view.Count == 0) emptyHost.BringToFront();
            else grid.BringToFront();

            if (view.Count == 0)
            {
                if (current.Count == 0 && categories.Count == 0)
                    SetEmpty("Nothing scanned yet", "Quick scan covers temp files, update cache and logs. Full drive scan walks the whole disk and also finds duplicates, empty folders and files over 500 MB.");
                else if (query.Length > 0)
                    SetEmpty("No match for \"" + query + "\"", "Clear the search box or try a shorter piece of the file name.");
                else if (current.Count > 0)
                    SetEmpty("Nothing matches this filter", "Switch the filter back to All to see every item in this category.");
                else
                    SetEmpty("No items in this view", "Pick a category on the left, or widen the filter.");
            }

            UpdateFilterCount();
            UpdateFooter();
        }

        private void SetEmpty(string title, string body)
        {
            emptyTitle.Text = title;
            emptyBody.Text = body;
            CenterEmpty();
        }

        private void CenterEmpty()
        {
            if (emptyTitle == null || emptyBody == null) return;
            int hostW = emptyHost.ClientSize.Width;
            int hostH = emptyHost.ClientSize.Height;
            int titleH = emptyTitle.PreferredHeight;
            int bodyH = emptyBody.PreferredHeight;

            emptyTitle.Location = new Point(Math.Max(0, (hostW - emptyTitle.Width) / 2),
                Math.Max(0, (hostH - (titleH + 8 + bodyH)) / 2));
            emptyBody.Location = new Point(Math.Max(0, (hostW - emptyBody.Width) / 2),
                emptyTitle.Bottom + 8);
        }

        private void UpdateFilterCount()
        {
            if (lblFilterCount == null) return;

            // Plain sentences, not slash-joined developer notes: this line is the only feedback the
            // user gets about what the search and filter are currently doing.
            if (current.Count == 0 && categories.Count == 0)
            {
                lblFilterCount.Text = "No scan yet.";
                return;
            }

            string shown = string.Format("Showing {0:N0} of {1:N0}", view.Count, current.Count);
            string tail = "";
            if (!string.IsNullOrEmpty(query)) tail = " matching \"" + query + "\"";
            else if (filterMode == FilterMode.Checked) tail = " with a check mark";
            else if (filterMode == FilterMode.Unchecked) tail = " without a check mark";

            if (view.Count == 0) shown = "Nothing to show";
            lblFilterCount.Text = shown + tail + (current.Count == 0 ? "." : " in this category.");
        }

        private void ScheduleSearch()
        {
            query = txtSearch.Text.Trim();
            int tick = ++searchTick;
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                if (tick != searchTick || IsDisposed) return;
                RefreshView(true);
            };
            timer.Start();
        }

        private void ShowCategory(int index)
        {
            if (index < 0 || index >= categories.Count) current = new List<ScanItem>();
            else current = categories[index].Items;

            btnPrev.Enabled = index > 0;
            btnNext.Enabled = index >= 0 && index < categories.Count - 1;
            RefreshView(true);
        }

        private void StepCategory(int delta)
        {
            if (lvCats.SelectedIndices.Count == 0) return;
            int idx = lvCats.SelectedIndices[0] + delta;
            if (idx < 0 || idx >= categories.Count) return;
            lvCats.Items[idx].Selected = true;
            lvCats.Items[idx].Focused = true;
            lvCats.EnsureVisible(idx);
        }

        private void UpdateNav()
        {
            if (lvCats.SelectedIndices.Count == 0)
            {
                btnPrev.Enabled = false;
                btnNext.Enabled = false;
                return;
            }
            int idx = lvCats.SelectedIndices[0];
            btnPrev.Enabled = !busy && idx > 0;
            btnNext.Enabled = !busy && idx < categories.Count - 1;
        }

        private void CatsItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (syncing) return;
            int idx = e.Item.Index;
            if (idx < 0 || idx >= categories.Count) return;

            bool val = e.Item.Checked;
            Category c = categories[idx];
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i].Keep) continue;
                c.Items[i].Checked = val;
            }
            grid.Invalidate();
            RefreshCatStates();
            UpdateFooter();
            UpdateFilterCount();
        }

        private void SetViewChecked(bool value)
        {
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i].Keep) continue;
                view[i].Checked = value;
            }
            grid.Invalidate();
            RefreshCatStates();
            UpdateFooter();
            UpdateFilterCount();
        }

        private void RefreshCatStates()
        {
            syncing = true;
            try
            {
                for (int i = 0; i < categories.Count && i < lvCats.Items.Count; i++)
                {
                    Category c = categories[i];
                    ListViewItem lvi = lvCats.Items[i];
                    lvi.SubItems[3].Text = Util.Fmt(c.SelectedSize);
                    bool all = c.AllChecked;
                    if (lvi.Checked != all) lvi.Checked = all;
                }
            }
            finally
            {
                syncing = false;
            }
        }

        private void UpdateFooter()
        {
            int count = 0;
            long size = 0;
            for (int i = 0; i < cats.Count; i++)
            {
                Category c = cats[i];
                for (int j = 0; j < c.Items.Count; j++)
                {
                    ScanItem it = c.Items[j];
                    if (!it.Checked || it.Keep) continue;
                    count++;
                    size += it.Size;
                }
            }
            // The total covers everything selected, across every category, because that is exactly
            // what Delete will act on. When the current view hides some of those rows (a search or a
            // filter is narrowing it), say so instead of letting the number look wrong.
            if (count == 0)
            {
                lblSelected.Text = "Nothing selected";
                lblFreeSpace.Text = "";
            }
            else
            {
                lblSelected.Text = string.Format("{0:N0} file{1} selected", count, count == 1 ? "" : "s");
                lblFreeSpace.Text = Util.Fmt(size) + " to free" +
                    ((view != null && view.Count != count) ? ", some outside this view" : "");
            }
            lblSelected.ForeColor = count > 0 ? Theme.Body : Theme.Muted;
            lblFreeSpace.ForeColor = count > 0 ? Theme.AccentDeep : Theme.Muted;
            btnDelete.Text = count > 1 ? "Delete " + count.ToString("N0") + " files" : "Delete selected";
            btnDelete.Enabled = !busy && count > 0;
        }

        private void PopulateCats()
        {
            syncing = true;
            try
            {
                lvCats.BeginUpdate();
                lvCats.Items.Clear();
                for (int i = 0; i < categories.Count; i++)
                {
                    Category c = categories[i];
                    var lvi = new ListViewItem(c.Name);
                    lvi.SubItems.Add(c.Items.Count.ToString("N0"));
                    lvi.SubItems.Add(Util.Fmt(c.Size));
                    // "To delete" rather than the raw selected size: the number means nothing until
                    // it says what it is, and it is the only column a user can act on.
                    lvi.SubItems.Add(c.SelectedSize > 0 ? Util.Fmt(c.SelectedSize) : "nothing");
                    lvi.Checked = c.AllChecked;
                    lvCats.Items.Add(lvi);
                }
                lvCats.EndUpdate();
                if (lvCats.Items.Count > 0)
                {
                    lvCats.Items[0].Selected = true;
                    lvCats.Items[0].Focused = true;
                }
            }
            finally
            {
                syncing = false;
            }
            ShowCategory(categories.Count > 0 ? 0 : -1);
        }

        private void RebuildCategories()
        {
            cats.RemoveAll(delegate(Category c) { return c.Items.Count == 0; });

            categories = new List<Category>();
            var all = new Category { Name = "All results" };
            for (int i = 0; i < cats.Count; i++) all.Items.AddRange(cats[i].Items);
            if (all.Items.Count > 0) categories.Add(all);
            categories.AddRange(cats);

            PopulateCats();
        }

        private async void StartScan(bool deep)
        {
            if (busy) return;

            if (deep && !chkDeepJunk.Checked && !chkDeepDup.Checked && !chkDeepEmpty.Checked && !chkDeepLarge.Checked)
            {
                MessageBox.Show(this, "Pick at least one deep scan scope.", "Scan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            busy = true;
            SetScanUi(true, true);
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Visible = true;
            lblStatus.Text = deep ? "Walking every folder on the drive..." : "Reading temp, cache and log locations...";

            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            var prog = new Progress<ScanProgress>(delegate(ScanProgress p)
            {
                lblStatus.Text = string.Format("{0:N0} files  /  {1}", p.Files, Shorten(p.Current, 96));
            });

            var opt = new ScanOptions
            {
                Junk = chkDeepJunk.Checked,
                Dupes = chkDeepDup.Checked,
                EmptyFolders = chkDeepEmpty.Checked,
                LargeFiles = chkDeepLarge.Checked,
                LargeFileThreshold = 500L * 1024 * 1024
            };

            List<Category> result = null;
            try
            {
                result = await Task.Run(delegate
                {
                    return deep ? ScanEngine.ScanDeep(opt, token, prog) : ScanEngine.ScanRegular(token, prog);
                });
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Scan cancelled. Nothing was deleted.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Scan failed.";
                SetEmpty("Scan failed", ex.Message);
                MessageBox.Show(this, "The scan stopped with an error:\n" + ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                progress.MarqueeAnimationSpeed = 0;
                progress.Visible = false;
                SetScanUi(false, false);
                cts = null;
            }

            if (result == null) return;

            cats = result;
            hasScanned = true;
            lastScanWasDeep = deep;
            query = "";
            if (txtSearch.Text.Length > 0) txtSearch.Clear();
            RebuildCategories();

            int total = 0;
            long size = 0;
            for (int i = 0; i < cats.Count; i++)
            {
                total += cats[i].Items.Count;
                size += cats[i].Size;
            }

            lblStatus.Text = total == 0
                ? "Nothing to clean in these locations."
                : string.Format("{0:N0} items, {1}  /  tick what you want gone, then Delete selected.", total, Util.Fmt(size));
        }

        private void SetScanUi(bool running, bool allowCancel)
        {
            btnScanReg.Enabled = !running;
            btnScanDeep.Enabled = !running;
            btnCancel.Enabled = running && allowCancel;
            btnRefresh.Enabled = !running;
            btnSelectAll.Enabled = !running;
            btnSelectNone.Enabled = !running;
            lvCats.Enabled = !running;
            grid.Enabled = !running;
            chkPermanent.Enabled = !running;
            chkDeepJunk.Enabled = !running;
            chkDeepDup.Enabled = !running;
            chkDeepEmpty.Enabled = !running;
            chkDeepLarge.Enabled = !running;
            btnDelete.Enabled = !running;
            txtSearch.Enabled = !running;
            if (running)
            {
                btnPrev.Enabled = false;
                btnNext.Enabled = false;
            }
            else
            {
                UpdateNav();
                UpdateFooter();
            }
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return "..." + s.Substring(s.Length - max);
        }

        private async void DeleteSelected()
        {
            if (busy) return;

            var byPath = new Dictionary<string, ScanItem>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cats.Count; i++)
            {
                Category c = cats[i];
                for (int j = 0; j < c.Items.Count; j++)
                {
                    ScanItem it = c.Items[j];
                    if (it.Checked && !it.Keep) byPath[it.Path] = it;
                }
            }
            if (byPath.Count == 0)
            {
                MessageBox.Show(this, "Nothing is selected yet.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var items = new List<ScanItem>(byPath.Values);
            long size = 0;
            for (int i = 0; i < items.Count; i++) size += items[i].Size;
            bool permanent = chkPermanent.Checked;

            string mode = permanent ? "permanently, with no way back" : "to the Recycle Bin";
            string msg = string.Format("{0:N0} items ({1}) will be deleted {2}.\n\nContinue?", items.Count, Util.Fmt(size), mode);
            if (MessageBox.Show(this, msg, "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            busy = true;
            SetScanUi(true, false);
            progress.Style = ProgressBarStyle.Blocks;
            progress.Minimum = 0;
            progress.Maximum = Math.Max(1, items.Count);
            progress.Value = 0;
            progress.Visible = true;
            lblStatus.Text = string.Format("Deleting 0 of {0:N0}...", items.Count);

            var prog = new Progress<int>(delegate(int n)
            {
                if (n <= progress.Maximum) progress.Value = n;
                lblStatus.Text = string.Format("Deleting {0:N0} of {1:N0}...", n, items.Count);
            });

            DeleteResult res = null;
            try
            {
                res = await Task.Run(delegate
                {
                    return ScanEngine.Delete(items, permanent, prog, CancellationToken.None);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Delete stopped with an error:\n" + ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                progress.Visible = false;
                SetScanUi(false, false);
            }

            if (res == null) return;

            var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ScanItem it in res.Removed) gone.Add(it.Path);
            for (int i = 0; i < cats.Count; i++)
            {
                cats[i].Items.RemoveAll(delegate(ScanItem it) { return gone.Contains(it.Path); });
            }
            RebuildCategories();
            RefreshFree();

            var sb = new StringBuilder();
            sb.AppendFormat("Deleted: {0:N0} items\n", res.Deleted);
            sb.AppendFormat("Freed: {0}\n", Util.Fmt(res.Freed));
            sb.AppendFormat("Failed or skipped: {0:N0} items", res.Failed);
            if (res.Errors.Count > 0)
            {
                sb.Append("\n\nFirst problems:\n");
                int n = Math.Min(8, res.Errors.Count);
                for (int i = 0; i < n; i++) sb.Append("- ").Append(res.Errors[i]).Append('\n');
            }
            lblStatus.Text = string.Format("Deleted {0:N0} items and freed {1}.", res.Deleted, Util.Fmt(res.Freed));
            MessageBox.Show(this, sb.ToString(), "Cleanup result", MessageBoxButtons.OK,
                res.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void RunTool(string exe, string args, string label, string confirmText)
        {
            if (toolsBusy) return;
            if (confirmText != null &&
                MessageBox.Show(this, confirmText, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            toolsBusy = true;
            SetToolsEnabled(false);
            Append("\r\n=== " + label + " ===\r\n");

            Process p = null;
            try
            {
                string fullExe = Path.IsPathRooted(exe) ? exe : Path.Combine(Environment.SystemDirectory, exe);
                var psi = new ProcessStartInfo(fullExe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.SystemDirectory
                };
                p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) Append(e.Data + "\r\n");
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) Append(e.Data + "\r\n");
                };
                p.Exited += delegate(object s, EventArgs e)
                {
                    int code = -1;
                    try { code = p.ExitCode; } catch { }
                    BeginInvokeSafe(delegate
                    {
                        Append("=== finished, exit code " + code + " ===\r\n");
                        toolsBusy = false;
                        SetToolsEnabled(true);
                        RefreshFree();
                    });
                    try { p.Dispose(); } catch { }
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Append("Could not run " + exe + ": " + ex.Message + "\r\n");
                toolsBusy = false;
                SetToolsEnabled(true);
                if (p != null) { try { p.Dispose(); } catch { } }
            }
        }

        private void SetToolsEnabled(bool enabled)
        {
            for (int i = 0; i < toolButtons.Count; i++) toolButtons[i].Enabled = enabled;
            if (enabled && btnWinOld != null) btnWinOld.Enabled = Directory.Exists(@"C:\Windows.old");
        }

        private void EmptyRecycleBin()
        {
            if (MessageBox.Show(this, "Empty the Recycle Bin on every drive?", "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                Append("\r\n=== Empty Recycle Bin ===\r\n");
                Append(hr == 0 ? "Recycle Bin emptied.\r\n" : "Finished with code 0x" + hr.ToString("X8") + "\r\n");
            }
            catch (Exception ex)
            {
                Append("Failed: " + ex.Message + "\r\n");
            }
            RefreshFree();
        }

        private void Append(string text)
        {
            if (txtOutput == null) return;
            if (txtOutput.InvokeRequired)
            {
                try { txtOutput.BeginInvoke(new Action<string>(Append), text); }
                catch { }
                return;
            }
            txtOutput.AppendText(text);
        }

        private void BeginInvokeSafe(Action a)
        {
            try
            {
                if (IsDisposed) return;
                BeginInvoke(a);
            }
            catch { }
        }

        private void RefreshFree()
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                lblFree.Text = string.Format("{0}  {1} free of {2}",
                    d.Name.TrimEnd('\\'), Util.Fmt(d.AvailableFreeSpace), Util.Fmt(d.TotalSize));
            }
            catch
            {
                lblFree.Text = "";
            }
        }

        public void RefreshAll()
        {
            RefreshFree();
            if (btnWinOld != null)
                btnWinOld.Enabled = Directory.Exists(@"C:\Windows.old");

            if (busy) return;

            if (hasScanned)
            {
                StartScan(lastScanWasDeep);
                return;
            }

            if (cats != null && cats.Count > 0)
            {
                for (int i = 0; i < cats.Count; i++)
                {
                    Category c = cats[i];
                    for (int j = c.Items.Count - 1; j >= 0; j--)
                    {
                        ScanItem it = c.Items[j];
                        if (!File.Exists(it.Path) && !Directory.Exists(it.Path))
                        {
                            c.Items.RemoveAt(j);
                        }
                    }
                }
                RebuildCategories();
                RefreshView(true);
                UpdateFooter();
                lblStatus.Text = "Refreshed.";
            }
            else
            {
                StartScan(false);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                RefreshAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
            }
            base.OnFormClosing(e);
        }

        private int smokeFailures;

        public int RunSmoke()
        {
            smokeFailures = 0;

            var items = new List<ScanItem>();
            string[] names = { "zeta.log", "alpha.tmp", "Mid.bak", "beta.log" };
            long[] sizes = { 50, 900, 20, 300 };
            for (int i = 0; i < names.Length; i++)
            {
                items.Add(new ScanItem
                {
                    Path = @"C:\Temp\" + names[i],
                    Size = sizes[i],
                    Checked = i % 2 == 0,
                    Note = i == 2 ? "keep copy" : "",
                    Category = "Junk files"
                }.Prepared());
            }
            var cat = new Category { Name = "Junk files" };
            cat.Items.AddRange(items);
            cats = new List<Category> { cat };
            categories = new List<Category> { cat };
            query = "";
            filterMode = FilterMode.All;
            sortField = SortField.Size;
            sortAscending = false;
            RebuildCategories();
            Application.DoEvents();

            Check(grid.RowCount == 4, "all four rows visible through the virtual grid (got " + grid.RowCount + ")");
            Check(SortActive(SortField.Size, false), "default sort is size, largest first");
            string shownName = grid.Rows[0].Cells[1].Value as string;
            Check(shownName != null && shownName.Length > 0, "the grid hands back real cell values (row 1 = " + shownName + ")");

            var click = new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0);
            GridHeaderClick(null, new DataGridViewCellMouseEventArgs(1, -1, 0, 0, click));
            Application.DoEvents();
            shownName = grid.Rows[0].Cells[1].Value as string;
            Check(SortActive(SortField.Name, true) && shownName == "alpha.tmp", "name ascending after header click");

            GridHeaderClick(null, new DataGridViewCellMouseEventArgs(1, -1, 0, 0, click));
            Application.DoEvents();
            shownName = grid.Rows[0].Cells[1].Value as string;
            Check(SortActive(SortField.Name, false) && shownName == "zeta.log", "name descending on the second click");

            GridHeaderClick(null, new DataGridViewCellMouseEventArgs(2, -1, 0, 0, click));
            Application.DoEvents();
            Check((grid.Rows[0].Cells[2].Value as string) == Util.Fmt(900), "first click on Size puts the largest first");
            GridHeaderClick(null, new DataGridViewCellMouseEventArgs(2, -1, 0, 0, click));
            Application.DoEvents();
            Check((grid.Rows[0].Cells[2].Value as string) == Util.Fmt(20), "second click on Size puts the smallest first");

            GridHeaderClick(null, new DataGridViewCellMouseEventArgs(4, -1, 0, 0, click));
            Application.DoEvents();
            Check(grid.Columns[4].HeaderText.StartsWith("Note"), "clicking Note sorts by note across every row");

            query = "tmp";
            RefreshView(true);
            Application.DoEvents();
            Check(grid.RowCount == 1, "search narrows to one row (got " + grid.RowCount + ")");
            query = "";
            RefreshView(true);

            filterMode = FilterMode.Checked;
            RefreshView(true);
            Application.DoEvents();
            Check(grid.RowCount == 2, "Checked chip shows two rows (got " + grid.RowCount + ")");
            filterMode = FilterMode.Unchecked;
            RefreshView(true);
            Application.DoEvents();
            Check(grid.RowCount == 2, "Unchecked chip shows two rows (got " + grid.RowCount + ")");
            filterMode = FilterMode.All;
            RefreshView(true);

            query = "no-such-file";
            RefreshView(true);
            Application.DoEvents();
            Check(emptyHost.Visible && !grid.Visible, "no-match state swaps in the empty panel");
            Check(emptyTitle.Text.Contains("no-such-file"), "empty state names the query");

            // The ways out of an empty result (search box, chips, category list) must stay on screen.
            Check(txtSearch.Visible && txtSearch.Parent.Visible, "search box stays visible when nothing matches");
            bool chipsShown = true;
            bool chipsFit = true;
            for (int i = 0; i < filterChips.Count; i++)
            {
                Button chip = filterChips[i];
                chipsShown &= chip.Visible && chip.Parent.Visible;
                chipsFit &= chip.Width >= TextRenderer.MeasureText(chip.Text, chip.Font).Width + 12;
            }
            Check(chipsShown, "filter chips stay visible when nothing matches");
            Check(chipsFit, "filter chip labels fit without truncation");
            Check(lblFilterCount.Visible && lblFilterCount.Parent.Visible && lblFilterCount.ForeColor == Theme.Body, "result count stays visible and readable");
            Check(btnRefresh.Visible && btnRefresh.Enabled, "refresh button is visible and enabled");
            Check(right.Visible && bar.Visible, "the toolbar panel itself does not get hidden");

            // A filter with no matches (every row already checked, then filtered to Unchecked) is the
            // other way to reach this screen, and it must behave the same.
            query = "";
            filterMode = FilterMode.Unchecked;
            RefreshView(true);
            Application.DoEvents();
            Check(emptyHost.Visible && emptyTitle.Text.Contains("Nothing matches this filter") || grid.RowCount > 0,
                "filter with no rows shows the filter empty state");
            Check(txtSearch.Visible && bar.Visible, "search and chips survive the filter empty state");
            filterMode = FilterMode.All;
            RefreshView(true);

            SetViewChecked(true);
            Application.DoEvents();
            Check(lblSelected.Text.StartsWith("4 files selected"), "select all updates the footer (got " + lblSelected.Text + ")");
            Check(lblFreeSpace.Text.Contains("to free"), "the footer says how much space that frees (got " + lblFreeSpace.Text + ")");
            Check(btnDelete.Text.Contains("4"), "the delete button states how many it will delete (got " + btnDelete.Text + ")");
            SetViewChecked(false);
            Application.DoEvents();
            Check(lblSelected.Text == "Nothing selected", "clear selection updates the footer (got " + lblSelected.Text + ")");
            Check(lblFreeSpace.Text.Length == 0, "clear selection clears the freed size too");

            var ghost = new Category { Name = "Junk files" };
            ghost.Items.Add(new ScanItem
            {
                Path = @"C:\nonexistent-" + Guid.NewGuid().ToString("N") + ".tmp",
                Size = 128,
                Checked = true,
                Category = "Junk files"
            }.Prepared());
            cats = new List<Category> { ghost };
            RebuildCategories();
            RefreshAll();
            Application.DoEvents();
            Check(grid.RowCount == 0 && emptyHost.Visible && lblStatus.Text == "Refreshed.", "refresh prunes missing files before first scan");

            cats = new List<Category>();
            categories = new List<Category>();
            current = new List<ScanItem>();
            RebuildCategories();
            Application.DoEvents();
            Check(emptyHost.Visible && emptyTitle.Text.Contains("Nothing scanned"), "pristine state shows the first-run empty text");
            Check(emptyHost.Height > 0 && emptyTitle.Width > 0, "the empty text has room to render (h=" + emptyHost.Height + ", w=" + emptyTitle.Width + ")");

            Console.WriteLine(smokeFailures == 0 ? "UI SMOKE PASS" : "UI SMOKE FAIL (" + smokeFailures + ")");
            return smokeFailures == 0 ? 0 : 1;
        }

        private void Check(bool ok, string label)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
            if (!ok) smokeFailures++;
        }

        public void CaptureTo(string path)
        {
            // A single DoEvents after Show() only gets through the first message batch, leaving
            // freshly laid out panels unpainted. Pump a few cycles and force a paint before capturing.
            for (int i = 0; i < 6; i++)
            {
                Invalidate(true);
                Update();
                Application.DoEvents();
            }
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        public void CaptureTab(int index, string path)
        {
            foreach (Control c in Controls)
            {
                var tabs = c as TabControl;
                if (tabs == null) continue;
                tabs.SelectedIndex = index;
                break;
            }
            Application.DoEvents();
            CaptureTo(path);
        }

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);
    }
}
