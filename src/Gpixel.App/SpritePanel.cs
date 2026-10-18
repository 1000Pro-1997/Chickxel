using System.Globalization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 오른쪽 독: 프로젝트의 Unity 설정(모드·PPU·Read/Write·메시·피벗·9-slice)과 내보내기 설정
/// (저장 시 내보내기, 배치, 열 수, 간격, 테두리 자르기, 메타 쓰기, 폴더·이름) + 만들어질 시트의 읽기 전용 미리보기.
/// 아틀라스 프로젝트에서는 피벗·9-slice가 고른 스프라이트 대상이 되고, 배치 옵션 대신 아틀라스 배치 옵션
/// (최대 가로, 간격, 2의 거듭제곱, 순서, 원래 위치 유지)과 이름이 표시된 배치 결과 미리보기를 보여 준다.
/// </summary>
public sealed class SpritePanel : UserControl
{
    private static readonly string[] PresetArrows = ["↖", "↑", "↗", "←", "●", "→", "↙", "↓", "↘"];
    private static readonly PivotPreset[] PresetOrder =
    [
        PivotPreset.TopLeft, PivotPreset.TopCenter, PivotPreset.TopRight,
        PivotPreset.LeftCenter, PivotPreset.Center, PivotPreset.RightCenter,
        PivotPreset.BottomLeft, PivotPreset.BottomCenter, PivotPreset.BottomRight,
    ];

    private readonly EditorContext _ctx;
    private readonly ComboBox _mode, _layout, _meta, _atlasOrder;
    private readonly NumericUpDown _ppu, _columns, _spacing, _singleFrame, _atlasMaxWidth;
    private readonly CheckBox _readable, _fullRect, _exportOnSave, _trim, _framePivot, _atlasPot, _keepRects;
    private readonly Control[] _animationOnly, _atlasOnly;
    private readonly GroupBox _pivotBox, _borderBox;
    private readonly Button[] _presetButtons = new Button[9];
    private readonly NumericUpDown _pivotX, _pivotY;
    private readonly Label _pivotTarget, _exportPath;
    private readonly NumericUpDown _bL, _bT, _bR, _bB;
    private readonly TextBox _folder, _fileName;
    private readonly SheetPreview _sheet;
    private readonly ListView _entries;
    private readonly System.Windows.Forms.Timer _rebuild = new() { Interval = 300 };
    private bool _updating;

    /// <summary>PPU를 사용자가 바꿈(마지막 PPU 기억용).</summary>
    public event EventHandler<float>? PpuChanged;
    /// <summary>"지금 내보내기".</summary>
    public event EventHandler? ExportRequested;

    public SpritePanel(EditorContext ctx)
    {
        _ctx = ctx;
        BackColor = Theme.PanelColor;

        // ── Unity
        _mode = Ui.Combo(90, "Single", "Multiple");
        _mode.SelectedIndexChanged += (_, _) =>
        {
            var mode = _mode.SelectedIndex == 1 ? SpriteMode.Multiple : SpriteMode.Single;
            EditSettings("스프라이트 모드", p => p.Sprite.Mode != mode, p => p.Sprite.Mode = mode);
        };
        _ppu = Ui.Num(1, 10000, 16, 64);
        _ppu.ValueChanged += (_, _) =>
        {
            float v = (float)_ppu.Value;
            if (EditSettings("PPU", p => Math.Abs(p.Sprite.PixelsPerUnit - v) > 0.0001f, p => p.Sprite.PixelsPerUnit = v)) PpuChanged?.Invoke(this, v);
        };
        _readable = Ui.Check("Read/Write");
        _readable.CheckedChanged += (_, _) => { bool v = _readable.Checked; EditSettings("Read/Write", p => p.Sprite.Readable != v, p => p.Sprite.Readable = v); };
        _fullRect = Ui.Check("메시 Full Rect", true);
        _fullRect.CheckedChanged += (_, _) => { bool v = _fullRect.Checked; EditSettings("메시 형식", p => p.Sprite.FullRectMesh != v, p => p.Sprite.FullRectMesh = v); };
        var fixedInfo = Ui.Label("필터 Point · 압축 없음 · 밉맵 끔(고정)");
        fixedInfo.ForeColor = Theme.DimTextColor;

        // ── 피벗
        var presetGrid = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
        for (int i = 0; i < 9; i++)
        {
            var preset = PresetOrder[i];
            var b = Ui.Button(PresetArrows[i], (_, _) => ApplyPivot(Pivot.FromPreset(preset)), 28);
            b.Height = 24;
            b.Margin = new Padding(1);
            new ToolTip().SetToolTip(b, preset.ToString());
            _presetButtons[i] = b;
            presetGrid.Controls.Add(b, i % 3, i / 3);
        }
        _pivotX = Ui.Num(0, 1, 0.5m, 60, 3, 0.05m);
        _pivotY = Ui.Num(0, 1, 0.5m, 60, 3, 0.05m);
        _pivotX.ValueChanged += (_, _) => { if (!_updating) ApplyPivot(new Pivot((float)_pivotX.Value, (float)_pivotY.Value)); };
        _pivotY.ValueChanged += (_, _) => { if (!_updating) ApplyPivot(new Pivot((float)_pivotX.Value, (float)_pivotY.Value)); };
        _framePivot = Ui.Check("현재 프레임만 따로");
        _framePivot.CheckedChanged += (_, _) =>
        {
            if (_updating || _ctx.Project is not { } p) return;
            int f = _ctx.CurrentFrame;
            _ctx.Structural(() => p.SetFramePivot(f, _framePivot.Checked ? p.GetFramePivot(f) : null));
        };
        _pivotTarget = Ui.Label("");
        _pivotTarget.ForeColor = Theme.DimTextColor;
        _pivotTarget.MaximumSize = new Size(270, 0);
        _pivotBox = Ui.Group("피벗(포커스) — 피벗 도구(K)로 끌기", Ui.Stack(
            Ui.Row(presetGrid, Ui.Stack(Ui.Row(Ui.Label("X"), _pivotX), Ui.Row(Ui.Label("Y"), _pivotY))),
            _framePivot,
            _pivotTarget));

        // ── 테두리
        _bL = Ui.Num(0, PixelImage.MaxSize, 0, 40);
        _bT = Ui.Num(0, PixelImage.MaxSize, 0, 40);
        _bR = Ui.Num(0, PixelImage.MaxSize, 0, 40);
        _bB = Ui.Num(0, PixelImage.MaxSize, 0, 40);
        foreach (var n in new[] { _bL, _bT, _bR, _bB })
            n.ValueChanged += (_, _) =>
            {
                if (_updating || _ctx.Project is not { } p) return;
                var border = new SpriteBorder((int)_bL.Value, (int)_bT.Value, (int)_bR.Value, (int)_bB.Value);
                if (_ctx.CurrentAtlasSprite is { } s)
                {
                    int index = _ctx.CurrentSprite;
                    if (s.Border != border) _ctx.Structural(() => p.SetSpriteBorder(index, border));
                }
                else EditSettings("9-slice 테두리", pr => pr.Sprite.Border != border, pr => pr.Sprite.Border = border);
            };
        _borderBox = Ui.Group("9-slice 테두리(px, 모든 칸)", Ui.Row(Ui.Label("L"), _bL, Ui.Label("T"), _bT, Ui.Label("R"), _bR, Ui.Label("B"), _bB));

        // ── 내보내기
        _exportOnSave = Ui.Check("저장할 때 PNG 시트 + .meta 내보내기", true);
        _exportOnSave.CheckedChanged += (_, _) => { bool v = _exportOnSave.Checked; EditSettings("내보내기 켜기", p => p.Export.ExportOnSave != v, p => p.Export.ExportOnSave = v); };
        _layout = Ui.Combo(150, "가로 N칸(프레임 순)", "태그마다 한 줄");
        _layout.SelectedIndexChanged += (_, _) =>
        {
            var v = _layout.SelectedIndex == 1 ? SheetLayout.RowPerTag : SheetLayout.Columns;
            EditSettings("시트 배치", p => p.Export.Layout != v, p => p.Export.Layout = v);
        };
        _columns = Ui.Num(0, 1024, 0, 54);
        new ToolTip().SetToolTip(_columns, "0 = 한 줄");
        _columns.ValueChanged += (_, _) => { int v = (int)_columns.Value; EditSettings("열 수", p => p.Export.Columns != v, p => p.Export.Columns = v); };
        _spacing = Ui.Num(0, 64, 0, 54);
        _spacing.ValueChanged += (_, _) => { int v = (int)_spacing.Value; EditSettings("칸 간격", p => p.Export.Spacing != v, p => p.Export.Spacing = v); };
        _trim = Ui.Check("빈 테두리 잘라내기(피벗 보정)");
        _trim.CheckedChanged += (_, _) => { bool v = _trim.Checked; EditSettings("테두리 잘라내기", p => p.Export.Trim != v, p => p.Export.Trim = v); };
        _singleFrame = Ui.Num(1, 9999, 1, 54);
        _singleFrame.ValueChanged += (_, _) => { int v = (int)_singleFrame.Value - 1; EditSettings("Single 프레임", p => p.Export.SingleFrameIndex != v, p => p.Export.SingleFrameIndex = v); };
        _meta = Ui.Combo(150, "자동(Unity 안이면)", "항상 쓰기", "쓰지 않기");
        _meta.SelectedIndexChanged += (_, _) =>
        {
            bool? v = _meta.SelectedIndex switch { 1 => true, 2 => false, _ => null };
            EditSettings("메타 쓰기", p => p.Export.WriteMeta != v, p => p.Export.WriteMeta = v);
        };
        _folder = Ui.Text("", 150);
        _folder.PlaceholderText = "(프로젝트 폴더)";
        _folder.Leave += (_, _) => { var v = NullIfEmpty(_folder.Text); EditSettings("내보낼 폴더", p => p.Export.Folder != v, p => p.Export.Folder = v); };
        var browse = Ui.Button("…", (_, _) => BrowseFolder(), 26);
        _fileName = Ui.Text("", 150);
        _fileName.PlaceholderText = "(프로젝트 이름)";
        _fileName.Leave += (_, _) => { var v = NullIfEmpty(_fileName.Text); EditSettings("내보낼 이름", p => p.Export.FileName != v, p => p.Export.FileName = v); };
        _exportPath = Ui.Label("");
        _exportPath.ForeColor = Theme.DimTextColor;
        _exportPath.MaximumSize = new Size(270, 0);

        // ── 아틀라스 배치
        _atlasMaxWidth = Ui.Num(0, PixelImage.MaxSize, 0, 64);
        new ToolTip().SetToolTip(_atlasMaxWidth, "0 = 자동(전체 넓이의 제곱근 정도, 가장 넓은 스프라이트 이상)");
        _atlasMaxWidth.ValueChanged += (_, _) => { int v = (int)_atlasMaxWidth.Value; EditSettings("아틀라스 최대 가로", p => p.Export.AtlasMaxWidth != v, p => p.Export.AtlasMaxWidth = v); };
        _atlasPot = Ui.Check("2의 거듭제곱 크기");
        _atlasPot.CheckedChanged += (_, _) => { bool v = _atlasPot.Checked; EditSettings("2의 거듭제곱", p => p.Export.AtlasPowerOfTwo != v, p => p.Export.AtlasPowerOfTwo = v); };
        _atlasOrder = Ui.Combo(120, "목록 순서대로", "큰 것부터");
        _atlasOrder.SelectedIndexChanged += (_, _) =>
        {
            var v = _atlasOrder.SelectedIndex == 1 ? AtlasPackOrder.LargestFirst : AtlasPackOrder.KeepOrder;
            EditSettings("아틀라스 순서", p => p.Export.AtlasOrder != v, p => p.Export.AtlasOrder = v);
        };
        _keepRects = Ui.Check("원래 위치 유지(가져온 칸 그대로)");
        new ToolTip().SetToolTip(_keepRects, "가져온 PNG의 칸 안에 아직 들어가는 스프라이트는 원래 자리에 두고, 새로 생겼거나 커진 것만 빈 곳에 배치");
        _keepRects.CheckedChanged += (_, _) => { bool v = _keepRects.Checked; EditSettings("원래 위치 유지", p => p.Export.KeepImportedRects != v, p => p.Export.KeepImportedRects = v); };
        var layoutRow = Ui.Row(Ui.Label("배치"), _layout);
        var columnsLabel = Ui.Label("열 수");
        var singleRow = Ui.Row(Ui.Label("Single 프레임"), _singleFrame);
        var maxWidthRow = Ui.Row(Ui.Label("최대 가로(px)"), _atlasMaxWidth);
        var orderRow = Ui.Row(Ui.Label("순서"), _atlasOrder);
        _animationOnly = [layoutRow, columnsLabel, _columns, _trim, singleRow];
        _atlasOnly = [maxWidthRow, orderRow, _atlasPot, _keepRects];

        var exportBox = Ui.Group("내보내기(시트 PNG + Unity .meta)", Ui.Stack(
            _exportOnSave,
            layoutRow,
            maxWidthRow,
            Ui.Row(columnsLabel, _columns, Ui.Label("간격"), _spacing),
            orderRow,
            _atlasPot,
            _keepRects,
            _trim,
            singleRow,
            Ui.Row(Ui.Label("메타"), _meta),
            Ui.Row(Ui.Label("폴더"), _folder, browse),
            Ui.Row(Ui.Label("이름"), _fileName),
            _exportPath,
            Ui.Button("지금 내보내기", (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty))));

        // ── 미리보기(읽기 전용)
        _sheet = new SheetPreview { Size = new Size(266, 160), Margin = new Padding(3) };
        _entries = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            Width = 266,
            Height = 150,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            BorderStyle = BorderStyle.None,
        };
        _entries.Columns.Add("칸 이름", 110);
        _entries.Columns.Add("번호", 46);
        _entries.Columns.Add("시트 위치", 90);
        _entries.SelectedIndexChanged += (_, _) =>
        {
            if (_entries.SelectedItems.Count == 1 && _entries.SelectedItems[0].Tag is SheetEntry e)
            {
                _sheet.Highlight = e.Rect;
                _ctx.SetCurrentColumn(e.FrameIndex);
            }
        };

        var stack = Ui.Stack(
            Ui.Row(Ui.Label("모드"), _mode, Ui.Label("PPU"), _ppu),
            Ui.Row(_readable, _fullRect),
            fixedInfo,
            _pivotBox,
            _borderBox,
            exportBox,
            Ui.Label("만들어질 시트(읽기 전용)", bold: true),
            _sheet,
            _entries);
        Controls.Add(Ui.ScrollHost(stack));
        Controls.Add(Ui.Header("Unity · 내보내기"));

        _rebuild.Tick += (_, _) => { _rebuild.Stop(); RebuildPreview(); };
        ctx.ProjectReplaced += (_, _) => { RefreshAll(); ScheduleRebuild(); };
        ctx.ProjectChanged += (_, e) =>
        {
            RefreshAll();
            if (!(e.Change == ProjectChange.Pixels && _ctx.Project!.IsStrokeActive)) ScheduleRebuild();
        };
        ctx.CurrentChanged += (_, _) => { RefreshPivot(); HighlightCurrent(); };
        RefreshAll();
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>설정을 바꾸는 실행취소 단위 편집. 값이 같거나 화면 갱신 중이면 하지 않는다.</summary>
    private bool EditSettings(string label, Func<SpriteProject, bool> differs, Action<SpriteProject> apply)
    {
        if (_updating || _ctx.Project is not { } p || !differs(p)) return false;
        _ctx.Structural(() => p.Edit(label, ProjectChange.Settings, () => apply(p)));
        return true;
    }

    private void BrowseFolder()
    {
        if (_ctx.Project is not { } p) return;
        using var dlg = new FolderBrowserDialog { Description = "시트 PNG를 내보낼 폴더", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var folder = dlg.SelectedPath;
        if (p.Path is { } path)
        {
            var rel = Path.GetRelativePath(Path.GetDirectoryName(path)!, folder);
            if (!rel.StartsWith("..", StringComparison.Ordinal) || rel.Count(c => c == Path.DirectorySeparatorChar) < 3) folder = rel;
        }
        _folder.Text = folder == "." ? "" : folder;
        var v = NullIfEmpty(_folder.Text);
        EditSettings("내보낼 폴더", pr => pr.Export.Folder != v, pr => pr.Export.Folder = v);
    }

    // ───────────── 프로젝트 → 화면 ─────────────

    private void RefreshAll()
    {
        _updating = true;
        try
        {
            bool has = _ctx.HasProject;
            foreach (Control c in Controls) c.Enabled = has;
            if (_ctx.Project is not { } p)
            {
                _entries.Items.Clear();
                _sheet.SetSheet(null);
                return;
            }
            bool atlas = p.IsAtlas;
            // 아틀라스는 항상 Multiple(스프라이트마다 칸 하나).
            _mode.SelectedIndex = atlas || p.Sprite.Mode == SpriteMode.Multiple ? 1 : 0;
            _mode.Enabled = !atlas;
            _ppu.Value = (decimal)Math.Clamp(p.Sprite.PixelsPerUnit, 1, 10000);
            _readable.Checked = p.Sprite.Readable;
            _fullRect.Checked = p.Sprite.FullRectMesh;
            foreach (var c in _animationOnly) c.Visible = !atlas;
            foreach (var c in _atlasOnly) c.Visible = atlas;
            var e = p.Export;
            _exportOnSave.Checked = e.ExportOnSave;
            _layout.SelectedIndex = e.Layout == SheetLayout.RowPerTag ? 1 : 0;
            _columns.Value = Math.Clamp(e.Columns, 0, 1024);
            _spacing.Value = Math.Clamp(e.Spacing, 0, 64);
            _trim.Checked = e.Trim;
            _singleFrame.Maximum = Math.Max(1, p.Frames.Count);
            _singleFrame.Value = Math.Clamp(e.SingleFrameIndex + 1, 1, Math.Max(1, p.Frames.Count));
            _singleFrame.Enabled = p.Sprite.Mode == SpriteMode.Single;
            _atlasMaxWidth.Value = Math.Clamp(e.AtlasMaxWidth, 0, PixelImage.MaxSize);
            _atlasPot.Checked = e.AtlasPowerOfTwo;
            _atlasOrder.SelectedIndex = e.AtlasOrder == AtlasPackOrder.LargestFirst ? 1 : 0;
            _keepRects.Checked = e.KeepImportedRects;
            _meta.SelectedIndex = e.WriteMeta switch { true => 1, false => 2, _ => 0 };
            if (!_folder.Focused) _folder.Text = e.Folder ?? "";
            if (!_fileName.Focused) _fileName.Text = e.FileName ?? "";
            if (p.Path is null) _exportPath.Text = "저장하면 프로젝트 옆에 시트를 내보냅니다.";
            else
            {
                var png = ProjectExport.GetExportPngPath(p);
                _exportPath.Text = $"→ {png}" + (ProjectExport.ShouldWriteMeta(p, png) ? " (+ .meta)" : " (메타 없음)");
            }
            RefreshPivot();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>피벗·9-slice 대상(아틀라스 = 현재 스프라이트, 애니메이션 = 현재 프레임/기본값)을 화면에 보인다.</summary>
    private void RefreshPivot()
    {
        if (_ctx.Project is not { } p) return;
        bool wasUpdating = _updating;
        _updating = true;
        Pivot pivot;
        SpriteBorder border;
        if (_ctx.CurrentAtlasSprite is { } s)
        {
            pivot = s.Pivot;
            border = s.Border;
            _framePivot.Visible = false;
            _pivotTarget.Text = $"대상: 스프라이트 \"{s.Name}\" ({s.Width}×{s.Height})";
            _borderBox.Text = $"9-slice 테두리(px, \"{s.Name}\")";
        }
        else
        {
            int f = _ctx.CurrentFrame;
            pivot = p.GetFramePivot(f);
            border = p.Sprite.Border;
            bool own = p.Frames[f].Pivot is not null;
            _framePivot.Visible = true;
            _framePivot.Checked = own;
            _pivotTarget.Text = own ? $"대상: 프레임 {f + 1}만" : "대상: 프로젝트 기본 피벗(모든 프레임)";
            _borderBox.Text = "9-slice 테두리(px, 모든 칸)";
        }
        _pivotX.Value = (decimal)Math.Clamp(pivot.X, 0, 1);
        _pivotY.Value = (decimal)Math.Clamp(pivot.Y, 0, 1);
        _bL.Value = Math.Clamp(border.Left, 0, PixelImage.MaxSize);
        _bT.Value = Math.Clamp(border.Top, 0, PixelImage.MaxSize);
        _bR.Value = Math.Clamp(border.Right, 0, PixelImage.MaxSize);
        _bB.Value = Math.Clamp(border.Bottom, 0, PixelImage.MaxSize);
        var preset = pivot.Preset;
        for (int i = 0; i < 9; i++)
            _presetButtons[i].BackColor = PresetOrder[i] == preset ? Theme.AccentColor : Theme.InputColor;
        _updating = wasUpdating;
    }

    private void ApplyPivot(Pivot pivot)
    {
        if (_updating || _ctx.Project is not { } p) return;
        if (_ctx.CurrentAtlasSprite is { } s)
        {
            int index = _ctx.CurrentSprite;
            if (s.Pivot != pivot) _ctx.Structural(() => p.SetSpritePivot(index, pivot));
            return;
        }
        int f = _ctx.CurrentFrame;
        if (p.Frames[f].Pivot is not null) _ctx.Structural(() => p.SetFramePivot(f, pivot));
        else EditSettings("기본 피벗", pr => pr.Sprite.DefaultPivot != pivot, pr => pr.Sprite.DefaultPivot = pivot);
    }

    // ───────────── 시트 미리보기 ─────────────

    private void ScheduleRebuild()
    {
        _rebuild.Stop();
        _rebuild.Start();
    }

    private void RebuildPreview()
    {
        if (_ctx.Project is not { } p)
        {
            _sheet.SetSheet(null);
            _entries.Items.Clear();
            return;
        }
        var baseName = p.Path is null ? p.BaseName : Path.GetFileNameWithoutExtension(ProjectExport.GetExportPngPath(p));
        var sheet = ProjectExport.BuildSheet(p, baseName);
        _sheet.SetSheet(sheet, showNames: p.IsAtlas);
        _entries.BeginUpdate();
        _entries.Items.Clear();
        foreach (var e in sheet.Entries)
            _entries.Items.Add(new ListViewItem([e.Name, (e.FrameIndex + 1).ToString(CultureInfo.InvariantCulture),
                $"{e.Rect.X},{e.Rect.Y} {e.Rect.Width}×{e.Rect.Height}"]) { Tag = e });
        _entries.EndUpdate();
        HighlightCurrent();
    }

    /// <summary>시트 미리보기에서 현재 프레임/스프라이트 칸을 강조한다.</summary>
    private void HighlightCurrent()
    {
        if (_sheet.Sheet is not { } sheet) return;
        int column = _ctx.CurrentColumn;
        _sheet.Highlight = sheet.Entries.FirstOrDefault(e => e.FrameIndex == column)?.Rect;
    }

    /// <summary>만들어질 시트와 칸 테두리(아틀라스면 스프라이트 이름도)를 보여 준다.</summary>
    private sealed class SheetPreview : Control
    {
        private Bitmap? _bmp;
        private SheetResult? _sheet;
        private Rectangle? _highlight;
        private bool _showNames;

        public SheetPreview()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.InputColor;
        }

        public SheetResult? Sheet => _sheet;

        public Rectangle? Highlight { get => _highlight; set { _highlight = value; Invalidate(); } }

        public void SetSheet(SheetResult? sheet, bool showNames = false)
        {
            _bmp?.Dispose();
            _sheet = sheet;
            _showNames = showNames;
            _bmp = sheet is null ? null : ImageUtil.ToBitmap(sheet.Image);
            _highlight = null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.InputColor);
            if (_bmp is null || _sheet is null) return;
            var dest = ImageUtil.DrawFitted(g, _bmp, new Rectangle(0, 0, _bmp.Width, _bmp.Height), Rectangle.Inflate(ClientRectangle, -4, -14), checker: true);
            float scale = dest.Width / (float)_bmp.Width;
            using var pen = new Pen(Color.FromArgb(200, 230, 200, 90));
            using var hl = new Pen(Theme.AccentColor, 2);
            using var labelBack = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            foreach (var entry in _sheet.Entries)
            {
                var r = entry.Rect;
                var sr = new RectangleF(dest.X + r.X * scale, dest.Y + r.Y * scale, r.Width * scale, r.Height * scale);
                g.DrawRectangle(entry.Rect == _highlight ? hl : pen, sr.X, sr.Y, sr.Width, sr.Height);
                if (!_showNames) continue;
                var size = TextRenderer.MeasureText(entry.Name, Theme.SmallFont);
                var lr = new Rectangle((int)sr.X + 1, (int)sr.Y + 1, Math.Min(size.Width, Math.Max(12, (int)sr.Width - 2)), size.Height);
                g.FillRectangle(labelBack, lr);
                TextRenderer.DrawText(g, entry.Name, Theme.SmallFont, lr, entry.Rect == _highlight ? Theme.AccentColor : Color.White,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            TextRenderer.DrawText(g, $"{_bmp.Width}×{_bmp.Height} · 칸 {_sheet.Entries.Count}개", Theme.SmallFont,
                new Rectangle(2, Height - 14, Width - 4, 14), Theme.DimTextColor, TextFormatFlags.Left);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bmp?.Dispose();
            base.Dispose(disposing);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rebuild.Dispose();
        base.Dispose(disposing);
    }
}
