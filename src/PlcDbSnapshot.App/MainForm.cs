using PlcDbSnapshot.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PlcDbSnapshot.App
{
    public sealed class MainForm : Form
    {
        private readonly PlcReader _plc = new PlcReader();
        private List<PlcBlock> _blocks = new List<PlcBlock>();
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _busy;
        private bool _suppressCheckEvents;

        // Compare state
        private Snapshot _snapA;
        private Snapshot _snapB;
        private CompareResult _result;
        private List<DiffRow> _shownRows = new List<DiffRow>();

        // Connection bar
        private readonly TextBox _ip = new TextBox { Width = 140 };
        private readonly TextBox _password = new TextBox { Width = 110, UseSystemPasswordChar = true };
        private readonly Button _connect = new Button { Text = "접속", AutoSize = true };
        private readonly Button _disconnect = new Button { Text = "해제", AutoSize = true, Enabled = false };
        private readonly Label _connState = new Label { Text = "접속 안 됨", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 6, 0, 0) };

        // Snapshot tab
        private readonly TextBox _blockFilter = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckedListBox _blockList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly Button _checkAll = new Button { Text = "보이는 것 전체 선택", AutoSize = true };
        private readonly Button _checkNone = new Button { Text = "전체 해제", AutoSize = true };
        private readonly Label _checkedInfo = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
        private readonly TextBox _note = new TextBox { Width = 320 };
        private readonly Button _save = new Button { Text = "선택한 DB 현재값 저장", AutoSize = true, Enabled = false };

        // Compare tab
        private readonly TextBox _pathA = new TextBox { Width = 520, ReadOnly = true };
        private readonly Button _openA = new Button { Text = "A 파일 열기", AutoSize = true };
        private readonly RadioButton _bFile = new RadioButton { Text = "B = 저장된 파일", AutoSize = true, Checked = true };
        private readonly RadioButton _bLive = new RadioButton { Text = "B = 지금 PLC 값 (A와 같은 태그)", AutoSize = true };
        private readonly TextBox _pathB = new TextBox { Width = 520, ReadOnly = true };
        private readonly Button _openB = new Button { Text = "B 파일 열기", AutoSize = true };
        private readonly TextBox _ignore = new TextBox { Width = 260 };
        private readonly NumericUpDown _tolerance = new NumericUpDown { Width = 90, DecimalPlaces = 4, Increment = 0.001m, Maximum = 1000000m };
        private readonly CheckBox _showSame = new CheckBox { Text = "같은 값도 표시", AutoSize = true };
        private readonly Button _compare = new Button { Text = "비교", AutoSize = true };
        private readonly Button _exportDiff = new Button { Text = "결과 CSV 저장", AutoSize = true, Enabled = false };
        private readonly Button _saveLiveB = new Button { Text = "B(PLC 값)를 스냅샷으로 저장", AutoSize = true, Enabled = false };
        private readonly TextBox _gridFilter = new TextBox { Width = 260 };
        private readonly Label _summary = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        private readonly DataGridView _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };

        // Status bar
        private readonly ToolStripProgressBar _progress = new ToolStripProgressBar { Width = 220 };
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel { Text = "준비", Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        public MainForm()
        {
            Text = "PLC DB Snapshot (읽기 전용)";
            Width = 1200;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9f);

            Controls.Add(BuildTabs());
            Controls.Add(BuildConnectionBar());
            var strip = new StatusStrip();
            strip.Items.Add(_progress);
            strip.Items.Add(_status);
            Controls.Add(strip);

            _ip.Text = Settings.Load("ip", "192.168.0.1");

            _connect.Click += async (s, e) => await ConnectAsync();
            _disconnect.Click += (s, e) => DoDisconnect();
            _blockFilter.TextChanged += (s, e) => RefreshBlockList();
            _blockList.ItemCheck += OnBlockCheck;
            _checkAll.Click += (s, e) => SetVisibleChecked(true);
            _checkNone.Click += (s, e) => { _checked.Clear(); RefreshBlockList(); };
            _save.Click += async (s, e) => await SaveSnapshotAsync();
            _openA.Click += (s, e) => OpenSnapshot(true);
            _openB.Click += (s, e) => OpenSnapshot(false);
            _bFile.CheckedChanged += (s, e) => UpdateButtons();
            _compare.Click += async (s, e) => await CompareAsync();
            _exportDiff.Click += (s, e) => ExportDiff();
            _saveLiveB.Click += (s, e) => SaveLiveB();
            _gridFilter.TextChanged += (s, e) => ApplyGridFilter();
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.RowPrePaint += OnRowPrePaint;
            FormClosing += (s, e) => _plc.Dispose();

            BuildGridColumns();
            UpdateButtons();
        }

        // ---------------------------------------------------------------- layout

        private Control BuildConnectionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6), WrapContents = false };
            bar.Controls.Add(new Label { Text = "PLC IP", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            bar.Controls.Add(_ip);
            bar.Controls.Add(new Label { Text = "비밀번호(있으면)", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
            bar.Controls.Add(_password);
            bar.Controls.Add(_connect);
            bar.Controls.Add(_disconnect);
            bar.Controls.Add(_connState);
            return bar;
        }

        private Control BuildTabs()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var t1 = new TabPage("1. 현재값 저장 (스냅샷)");
            var t2 = new TabPage("2. 비교");
            t1.Controls.Add(BuildSnapshotTab());
            t2.Controls.Add(BuildCompareTab());
            tabs.TabPages.Add(t1);
            tabs.TabPages.Add(t2);
            return tabs;
        }

        private Control BuildSnapshotTab()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterRow.Controls.Add(new Label { Text = "DB 검색 (이름/번호)", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
            filterRow.Controls.Add(_blockFilter, 1, 0);
            layout.Controls.Add(filterRow, 0, 0);

            layout.Controls.Add(_blockList, 0, 1);

            var checkRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            checkRow.Controls.Add(_checkAll);
            checkRow.Controls.Add(_checkNone);
            checkRow.Controls.Add(_checkedInfo);
            layout.Controls.Add(checkRow, 0, 2);

            var saveRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            saveRow.Controls.Add(new Label { Text = "메모", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            saveRow.Controls.Add(_note);
            saveRow.Controls.Add(_save);
            layout.Controls.Add(saveRow, 0, 3);
            return layout;
        }

        private Control BuildCompareTab()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(6) };
            for (var i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var rowA = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            rowA.Controls.Add(new Label { Text = "A (기준)", AutoSize = true, Width = 70, Padding = new Padding(0, 6, 0, 0) });
            rowA.Controls.Add(_pathA);
            rowA.Controls.Add(_openA);
            layout.Controls.Add(rowA, 0, 0);

            var rowB = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            rowB.Controls.Add(_bFile);
            rowB.Controls.Add(_pathB);
            rowB.Controls.Add(_openB);
            rowB.Controls.Add(_bLive);
            layout.Controls.Add(rowB, 0, 1);

            var rowOpt = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            rowOpt.Controls.Add(new Label { Text = "제외할 태그 (정규식)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            rowOpt.Controls.Add(_ignore);
            rowOpt.Controls.Add(new Label { Text = "Real 허용 오차", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
            rowOpt.Controls.Add(_tolerance);
            rowOpt.Controls.Add(_showSame);
            rowOpt.Controls.Add(_compare);
            rowOpt.Controls.Add(_exportDiff);
            rowOpt.Controls.Add(_saveLiveB);
            layout.Controls.Add(rowOpt, 0, 2);

            var rowFilter = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            rowFilter.Controls.Add(new Label { Text = "결과 검색", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            rowFilter.Controls.Add(_gridFilter);
            layout.Controls.Add(rowFilter, 0, 3);

            layout.Controls.Add(_summary, 0, 4);
            layout.Controls.Add(_grid, 0, 5);
            return layout;
        }

        private void BuildGridColumns()
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "결과", Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Block", HeaderText = "DB", Width = 180 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tag", HeaderText = "태그", Width = 430 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "형식", Width = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "A", HeaderText = "A 값", Width = 170 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "B", HeaderText = "B 값", Width = 170 });
        }

        // ---------------------------------------------------------------- state helpers

        private void UpdateButtons()
        {
            var connected = _plc.IsConnected;
            _connect.Enabled = !_busy && !connected;
            _disconnect.Enabled = !_busy && connected;
            _ip.Enabled = _password.Enabled = !_busy && !connected;
            _save.Enabled = !_busy && connected && _checked.Count > 0;
            _checkAll.Enabled = _checkNone.Enabled = !_busy;
            _openA.Enabled = !_busy;
            _openB.Enabled = !_busy && _bFile.Checked;
            _pathB.Enabled = _bFile.Checked;
            _compare.Enabled = !_busy && _snapA != null && (_bFile.Checked ? _snapB != null : connected);
            _exportDiff.Enabled = !_busy && _result != null;
            _saveLiveB.Enabled = !_busy && _result != null && _snapB != null && _snapB.Get("Source") == "PLC";
            _connState.Text = connected ? "접속됨: " + _plc.Address : "접속 안 됨";
            _connState.ForeColor = connected ? Color.ForestGreen : Color.DimGray;
            var checkedTags = _blocks.Where(b => _checked.Contains(b.AccessKey)).Sum(b => b.TagCount);
            _checkedInfo.Text = "선택 " + _checked.Count + "개 블록, 태그 " + checkedTags.ToString("N0", CultureInfo.InvariantCulture) + "개";
        }

        private async Task RunBusy(string what, Func<IProgress<(int done, int total)>, Task> work)
        {
            _busy = true;
            UseWaitCursor = true;
            _status.Text = what;
            _progress.Style = ProgressBarStyle.Marquee;
            UpdateButtons();
            var progress = new Progress<(int done, int total)>(p =>
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = Math.Max(1, p.total);
                _progress.Value = Math.Min(p.done, _progress.Maximum);
                _status.Text = what + " " + p.done.ToString("N0", CultureInfo.InvariantCulture) + " / " + p.total.ToString("N0", CultureInfo.InvariantCulture);
            });
            try
            {
                await work(progress);
            }
            catch (Exception ex)
            {
                _status.Text = "오류: " + ex.Message;
                MessageBox.Show(this, ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
                UseWaitCursor = false;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
                UpdateButtons();
            }
        }

        // ---------------------------------------------------------------- connect / blocks

        private async Task ConnectAsync()
        {
            var ip = _ip.Text.Trim();
            var pw = _password.Text;
            if (ip.Length == 0) return;
            Settings.Save("ip", ip);
            await RunBusy("접속 및 DB 목록 읽는 중... (프로그램이 크면 1~2분 걸릴 수 있음)", async _ =>
            {
                var blocks = await Task.Run(() =>
                {
                    _plc.Connect(ip, pw);
                    try { return _plc.LoadBlocks(); }
                    catch { _plc.Disconnect(); throw; }
                });
                _blocks = blocks;
                _checked.Clear();
                RefreshBlockList();
                _status.Text = "접속됨. 블록 " + _blocks.Count + "개, 태그 " + _blocks.Sum(b => b.TagCount).ToString("N0", CultureInfo.InvariantCulture) + "개";
            });
        }

        private void DoDisconnect()
        {
            _plc.Disconnect();
            _blocks = new List<PlcBlock>();
            _checked.Clear();
            RefreshBlockList();
            _status.Text = "접속 해제";
            UpdateButtons();
        }

        private IEnumerable<PlcBlock> VisibleBlocks()
        {
            var f = _blockFilter.Text.Trim();
            if (f.Length == 0) return _blocks;
            return _blocks.Where(b => b.Display.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RefreshBlockList()
        {
            _suppressCheckEvents = true;
            _blockList.BeginUpdate();
            _blockList.Items.Clear();
            foreach (var b in VisibleBlocks())
            {
                _blockList.Items.Add(new BlockItem(b), _checked.Contains(b.AccessKey));
            }
            _blockList.EndUpdate();
            _suppressCheckEvents = false;
            UpdateButtons();
        }

        private void OnBlockCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressCheckEvents) return;
            var item = (BlockItem)_blockList.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _checked.Add(item.Block.AccessKey);
            else _checked.Remove(item.Block.AccessKey);
            BeginInvoke(new Action(UpdateButtons));
        }

        private void SetVisibleChecked(bool on)
        {
            foreach (var b in VisibleBlocks())
            {
                if (on) _checked.Add(b.AccessKey); else _checked.Remove(b.AccessKey);
            }
            RefreshBlockList();
        }

        private sealed class BlockItem
        {
            public BlockItem(PlcBlock b) { Block = b; }
            public PlcBlock Block { get; }
            public override string ToString() => Block.Display;
        }

        // ---------------------------------------------------------------- snapshot

        private async Task SaveSnapshotAsync()
        {
            var selected = _blocks.Where(b => _checked.Contains(b.AccessKey)).ToList();
            if (selected.Count == 0) return;

            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "스냅샷 CSV (*.csv)|*.csv",
                InitialDirectory = Settings.SnapshotFolder(),
                FileName = SafeFile(_plc.Address) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + SafeNote(_note.Text) + ".csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            var note = _note.Text.Trim();
            await RunBusy("현재값 읽는 중", async progress =>
            {
                var started = DateTimeOffset.Now;
                var entries = await Task.Run(() => _plc.ReadBlocks(selected, progress));
                var snap = NewSnapshot(started, note, "PLC");
                snap.Metadata[Snapshot.KeyBlocks] = string.Join(";", selected.Select(b => b.Name));
                snap.Entries.AddRange(entries);
                SnapshotCsv.Write(snap, path);
                var errors = entries.Count(x => !x.IsOk);
                _status.Text = "저장 완료: " + Path.GetFileName(path) + " (태그 " + entries.Count.ToString("N0", CultureInfo.InvariantCulture) + "개" + (errors > 0 ? ", 읽기 오류 " + errors + "개" : "") + ")";
            });
        }

        private Snapshot NewSnapshot(DateTimeOffset time, string note, string source)
        {
            var snap = new Snapshot();
            snap.Metadata[Snapshot.KeyPlc] = _plc.Address;
            snap.Metadata[Snapshot.KeyTime] = time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            snap.Metadata["Source"] = source;
            if (!string.IsNullOrEmpty(note)) snap.Metadata[Snapshot.KeyNote] = note;
            snap.Metadata["Tool"] = "PlcDbSnapshot " + typeof(MainForm).Assembly.GetName().Version;
            return snap;
        }

        private static string SafeFile(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace('.', '-');
        }

        private static string SafeNote(string note)
        {
            note = (note ?? "").Trim();
            if (note.Length == 0) return "";
            if (note.Length > 30) note = note.Substring(0, 30);
            return "_" + SafeFile(note).Replace(' ', '_');
        }

        // ---------------------------------------------------------------- compare

        private void OpenSnapshot(bool isA)
        {
            using (var dlg = new OpenFileDialog { Filter = "스냅샷 CSV (*.csv)|*.csv|모든 파일|*.*", InitialDirectory = Settings.SnapshotFolder() })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var snap = SnapshotCsv.Read(dlg.FileName);
                    if (isA) { _snapA = snap; _pathA.Text = dlg.FileName; }
                    else { _snapB = snap; _pathB.Text = dlg.FileName; }
                    _status.Text = (isA ? "A" : "B") + " 열기: 태그 " + snap.Entries.Count.ToString("N0", CultureInfo.InvariantCulture) + "개 (" + SnapshotComparer.Describe(snap) + ")";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "파일 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            UpdateButtons();
        }

        private async Task CompareAsync()
        {
            if (_snapA == null) return;
            var options = new PlcDbSnapshot.Core.CompareOptions
            {
                IgnoreTagRegex = _ignore.Text.Trim(),
                RealTolerance = (double)_tolerance.Value,
                IncludeSame = _showSame.Checked
            };
            try
            {
                if (options.IgnoreTagRegex.Length > 0) new System.Text.RegularExpressions.Regex(options.IgnoreTagRegex);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, "제외 정규식이 잘못되었습니다: " + ex.Message, "비교", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var live = _bLive.Checked;
            var a = _snapA;
            await RunBusy(live ? "PLC 현재값 읽는 중" : "비교 중", async progress =>
            {
                if (live)
                {
                    var started = DateTimeOffset.Now;
                    var names = a.Entries.Select(x => x.Tag).ToList();
                    var entries = await Task.Run(() => _plc.ReadTagsByName(names, progress));
                    var b = NewSnapshot(started, "", "PLC");
                    b.Entries.AddRange(entries);
                    _snapB = b;
                    _pathB.Text = "";
                }
                var snapB = _snapB;
                _result = await Task.Run(() => SnapshotComparer.Compare(a, snapB, options));
                _summary.Text = _result.Summary() + "    A: " + SnapshotComparer.Describe(a) + "    B: " + SnapshotComparer.Describe(snapB);
                ApplyGridFilter();
                _status.Text = "비교 완료";
            });
        }

        private void ApplyGridFilter()
        {
            var rows = _result?.Rows ?? new List<DiffRow>();
            var f = _gridFilter.Text.Trim();
            _shownRows = f.Length == 0
                ? rows
                : rows.Where(r => r.Tag.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                               || DiffRow.KindText(r.Kind).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            _grid.RowCount = 0;
            _grid.RowCount = _shownRows.Count;
            _grid.Invalidate();
        }

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _shownRows.Count) return;
            var r = _shownRows[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = DiffRow.KindText(r.Kind); break;
                case 1: e.Value = r.Block; break;
                case 2: e.Value = r.Tag; break;
                case 3: e.Value = r.DataType; break;
                case 4: e.Value = r.ValueA; break;
                case 5: e.Value = r.ValueB; break;
            }
        }

        private void OnRowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _shownRows.Count) return;
            Color c;
            switch (_shownRows[e.RowIndex].Kind)
            {
                case DiffKind.Changed: c = Color.FromArgb(255, 243, 205); break;
                case DiffKind.OnlyInA: c = Color.FromArgb(248, 215, 218); break;
                case DiffKind.OnlyInB: c = Color.FromArgb(209, 231, 221); break;
                case DiffKind.ReadError: c = Color.FromArgb(226, 227, 229); break;
                default: c = Color.White; break;
            }
            _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = c;
        }

        private void ExportDiff()
        {
            if (_result == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                InitialDirectory = Settings.SnapshotFolder(),
                FileName = "비교_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SnapshotComparer.WriteCsv(_result, _snapA, _snapB, dlg.FileName);
                _status.Text = "비교 결과 저장: " + Path.GetFileName(dlg.FileName);
            }
        }

        private void SaveLiveB()
        {
            if (_snapB == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "스냅샷 CSV (*.csv)|*.csv",
                InitialDirectory = Settings.SnapshotFolder(),
                FileName = SafeFile(_plc.Address) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SnapshotCsv.Write(_snapB, dlg.FileName);
                _status.Text = "B 저장: " + Path.GetFileName(dlg.FileName);
            }
        }
    }
}
