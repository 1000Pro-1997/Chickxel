using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 파일 브라우저에서 고른 파일 합치기: 순서(끌어서 바꾸기), 결과 이름, 방식(레이어/그룹/스킨/프레임), 그룹이면 빈 대표 이미지,
/// 캔버스 크기·기준점, 원본 삭제, 미리보기. <c>.chickxel</c>의 그룹·스킨 등 구조는 어느 방식이든 유지하고, 어떻게 옮기는지 안내한다.
/// </summary>
public sealed class MergeDialog : DialogBase
{
    private readonly List<(string Path, MergeSource Source)> _items;
    private readonly string _folder;
    private readonly ListBox _order;
    private readonly TextBox _name;
    private readonly RadioButton _layers, _group, _skins, _frames;
    private readonly NumericUpDown _w, _h;
    private readonly AnchorPicker _anchor = new();
    private readonly CheckBox _deleteOriginals, _blankCover;
    private readonly DragReorder _drag = new();
    private readonly MergePreview _preview = new();
    private readonly TimelinePreview _timeline = new();
    private readonly TextBlock _info, _warning, _notes;
    private bool _ready;

    /// <param name="items">합칠 파일(고른 순서)과 읽은 내용.</param>
    /// <param name="folder">결과 <c>.chickxel</c>을 만들 폴더.</param>
    public MergeDialog(IReadOnlyList<(string Path, MergeSource Source)> items, string folder) : base(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_MergeDialog_B96DE665FA,items.Count), UiStrings.S_MergeDialog_MergeDialog_FB647D2B95)
    {
        _items = items.ToList();
        _folder = folder;

        _order = new ListBox { Width = 220, Height = 150, Background = AppTheme.InputBrush, Foreground = AppTheme.TextBrush }
            .Tip(UiStrings.S_MergeDialog_MergeDialog_34FC6D6063);
        _drag.Moved += (from, to) => MoveItem(from, to);
        var up = Ui.IconButton(VectorIcon.ChevronUp, UiStrings.S_MergeDialog_MergeDialog_23AA50B513, (_, _) => MoveSelected(-1), 32);
        var down = Ui.IconButton(VectorIcon.ChevronDown, UiStrings.S_MergeDialog_MergeDialog_BDD18DAAB9, (_, _) => MoveSelected(1), 32);
        _blankCover = Ui.Check(UiStrings.S_MergeDialog_MergeDialog_40FF1DF2B5);
        _blankCover.IsEnabled = false;
        _blankCover.VerticalAlignment = VerticalAlignment.Center;
        _blankCover.Tip(UiStrings.S_MergeDialog_MergeDialog_7C6B20A094
                        + UiStrings.S_MergeDialog_MergeDialog_BC8C862A07);
        RefreshOrder(0);

        var maxW = items.Max(i => i.Source.Size.Width);
        var maxH = items.Max(i => i.Source.Size.Height);
        _layers = Radio(UiStrings.S_MergeDialog_MergeDialog_B3BA45594D, UiStrings.S_MergeDialog_MergeDialog_9D32BDB124, true);
        _group = Radio(UiStrings.S_MergeDialog_MergeDialog_2A89E3C4B6, UiStrings.S_MergeDialog_MergeDialog_7306C9C995, false);
        _skins = Radio(UiStrings.S_MergeDialog_MergeDialog_0A7DC12A34, UiStrings.S_MergeDialog_MergeDialog_C9D3632FB9
                                  + UiStrings.S_MergeDialog_MergeDialog_A54A1EF2B9, false);
        _frames = Radio(UiStrings.S_MergeDialog_MergeDialog_6233362607, UiStrings.S_MergeDialog_MergeDialog_C112F96D7C, false);
        _name = Ui.Text(UniqueName(ProjectMerge.SuggestName(items.Select(i => i.Source.Name).ToList())), 200);
        _w = Ui.Num(1, PixelImage.MaxSize, maxW, 70).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _h = Ui.Num(1, PixelImage.MaxSize, maxH, 70).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        var fit = Ui.Button(UiStrings.S_MergeDialog_MergeDialog_FAF5E33EF9, (_, _) => { _w.Value = maxW; _h.Value = maxH; });
        _deleteOriginals = Ui.Check(UiStrings.S_MergeDialog_MergeDialog_FBCED3F611);
        _info = Ui.Label("");
        _warning = Ui.Label("");
        _warning.Foreground = Brushes.OrangeRed;
        _warning.TextWrapping = TextWrapping.Wrap;
        _warning.MaxWidth = 300;
        _notes = Ui.Label("");
        _notes.Foreground = AppTheme.DimTextBrush;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.MaxWidth = 300;

        var left = Ui.Stack(
            Ui.Label(UiStrings.S_MergeDialog_MergeDialog_D350916435, bold: true),
            Ui.Row(_order, Ui.Stack(up, down)),
            Ui.Row(Ui.Label(UiStrings.S_MergeDialog_MergeDialog_C6EEBDB85A), _name, Ui.Label(SpriteProject.FileExtension)),
            Ui.Label(UiStrings.S_MergeDialog_MergeDialog_0D1516342C, bold: true),
            _layers, GroupRow(), _skins, _frames,
            Ui.Label(UiStrings.S_MergeDialog_MergeDialog_6DB1865E38, bold: true),
            Ui.Row(Ui.Label(UiStrings.S_MergeDialog_MergeDialog_9EA164CE93), _w, Ui.Label(UiStrings.S_MergeDialog_MergeDialog_B655521669), _h, Ui.Label("px"), fit),
            Ui.Label(UiStrings.S_MergeDialog_MergeDialog_58E4493C15),
            _anchor,
            _deleteOriginals,
            _warning);
        var right = Ui.Stack(Ui.Label(UiStrings.S_MergeDialog_MergeDialog_5A49C66873, bold: true), _preview, _info, _notes);
        Body.Children.Add(Ui.Row(left, right));
        // 합친 결과가 타임라인에서 어떻게 보이는지(행 = 레이어·그룹·부위, 열 = 프레임) — 방식을 바꾸면 바로 바뀐다.
        Body.Children.Add(Ui.Label(UiStrings.S_MergeDialog_MergeDialog_E564B919E5, bold: true));
        Body.Children.Add(new ScrollViewer
        {
            Content = _timeline,
            MaxWidth = 700,
            MaxHeight = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        foreach (var r in new[] { _layers, _group, _skins, _frames }) r.IsCheckedChanged += (_, _) => { _blankCover.IsEnabled = _group.On(); UpdatePreview(); };
        _blankCover.IsCheckedChanged += (_, _) => UpdatePreview();
        _w.ValueChanged += (_, _) => UpdatePreview();
        _h.ValueChanged += (_, _) => UpdatePreview();
        _anchor.Changed += (_, _) => UpdatePreview();
        _name.TextChanged += (_, _) => UpdateWarning();
        _deleteOriginals.IsCheckedChanged += (_, _) => UpdateWarning();
        Closed += (_, _) => { _preview.Clear(); _timeline.Clear(); };
        OkButton.Click += (_, _) =>
        {
            if (Validate() is { } error)
            {
                Ui.Error(this, error);
                DialogResult = DialogResult.None;
            }
        };
        _ready = true;
        UpdatePreview();
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    /// <summary>합칠 파일(정한 순서).</summary>
    public IReadOnlyList<(string Path, MergeSource Source)> Items => _items;
    public string ResultName => _name.Text?.Trim() ?? "";
    public string ProjectPath => Path.Combine(_folder, ResultName + SpriteProject.FileExtension);
    public bool DeleteOriginals => _deleteOriginals.On();
    public MergeMode Mode => _group.On() ? MergeMode.Group : _skins.On() ? MergeMode.Skins : _frames.On() ? MergeMode.Frames : MergeMode.Layers;
    /// <summary>빈 대표 이미지(그룹 방식에서 켰을 때만).</summary>
    private bool BlankCover => _blankCover.On() && Mode == MergeMode.Group;
    public MergeOptions Options => new(Mode, (int)_w.NumValue(), (int)_h.NumValue(), _anchor.Value, ResultName, BlankCover);

    /// <summary>저장하면 생기는 파일: 프로젝트, 기본 내보내기 프레임 시트(그룹·스킨이면 부위 아틀라스도).</summary>
    public IEnumerable<string> TargetPaths(string name)
    {
        yield return Path.Combine(_folder, name + SpriteProject.FileExtension);
        yield return Path.Combine(_folder, name + ".png");
        if (Mode is MergeMode.Group or MergeMode.Skins) yield return Path.Combine(_folder, name + "_parts.png");
    }

    /// <summary>원본을 지울 때 함께 지워지는 파일(원본 + 딸린 파일). 확인할 수 없는 딸린 파일은 뺀다.</summary>
    private List<string> OriginalFiles()
    {
        var list = new List<string>();
        foreach (var (path, _) in _items)
        {
            list.Add(path);
            try { list.AddRange(FileBrowserPanel.CompanionsOf(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return list;
    }

    private static RadioButton Radio(string text, string tip, bool on)
        => new RadioButton { GroupName = "merge-mode", Content = text, IsChecked = on, Foreground = AppTheme.TextBrush }.Tip(tip);

    /// <summary>"그룹으로" 줄: 라디오 오른쪽에 같은 줄로 "빈 대표 이미지"(그룹으로를 고를 때만 켤 수 있음, 아니면 회색).</summary>
    private Grid GroupRow()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,Auto") };
        row.Children.Add(_group);
        Grid.SetColumn(_blankCover, 1);
        row.Children.Add(_blankCover);
        return row;
    }

    private string UniqueName(string name)
    {
        if (!TargetPaths(name).Any(File.Exists)) return name;
        for (int i = 2; ; i++)
            if (!TargetPaths($"{name}_{i}").Any(File.Exists)) return $"{name}_{i}";
    }

    /// <summary>고른 파일 번호(없으면 -1).</summary>
    private int Selected => _order.SelectedIndex;

    private void RefreshOrder(int select)
    {
        _drag.Clear();
        _order.Items.Clear();
        foreach (var (path, s) in _items)
        {
            var item = new ListBoxItem { Content = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_RefreshOrder_39B513567A,Path.GetFileName(path),s.Size.Width,s.Size.Height,(s.FrameCount > 1 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_RefreshOrder_1AAB850EB8,s.FrameCount) : "")) };
            _order.Items.Add(item);
            _drag.Attach(item);
        }
        _order.SelectedIndex = select < 0 ? -1 : Math.Min(select, _items.Count - 1);
    }

    private void MoveSelected(int delta)
    {
        int i = Selected;
        if (i >= 0) MoveItem(i, i + delta);
    }

    /// <summary>파일 <paramref name="from"/>을 <paramref name="to"/>번(옮긴 뒤 번호)으로 옮기고 미리보기를 다시 만든다.</summary>
    private void MoveItem(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to) return;
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        RefreshOrder(to);
        UpdatePreview();
    }

    /// <summary>입력 오류(없으면 null).</summary>
    private string? Validate()
    {
        var name = ResultName;
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return UiStrings.S_MergeDialog_Validate_A78A5A134C;
        return Conflicts(name) is { Count: > 0 } c ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_Validate_71684F8BF0,string.Join(", ", c.Select(Path.GetFileName))) : null;
    }

    /// <summary>덮어쓰게 되는 기존 파일(원본 삭제를 켰으면 지울 원본은 뺌).</summary>
    private List<string> Conflicts(string name)
    {
        var deleted = DeleteOriginals ? OriginalFiles().Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        return TargetPaths(name).Where(p => File.Exists(p) && !deleted.Contains(Path.GetFullPath(p))).ToList();
    }

    private void UpdateWarning()
    {
        var name = ResultName;
        _warning.Text = name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? UiStrings.S_MergeDialog_UpdateWarning_0357CC4FEF
            : Conflicts(name) is { Count: > 0 } c
                ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_UpdateWarning_9F1C6A7600,string.Join(", ", c.Select(Path.GetFileName)))
                : "";
    }

    private void UpdatePreview()
    {
        if (!_ready) return;
        UpdateWarning();
        var notes = new List<string>();
        var p = ProjectMerge.Merge(_items.Select(i => i.Source).ToList(), Options, notes: notes);
        _preview.SetProject(p);
        _timeline.SetProject(p);
        int parts = p.Layers.Count(l => l.IsPart);
        int skins = p.Layers.Where(l => l.IsGroup).Sum(l => l.Skins.Count);
        _info.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_UpdatePreview_D7D7C6307A,p.Width,p.Height,p.Layers.Count(l => !l.IsPart)) + (parts > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_UpdatePreview_190EF86F30,parts) : "")
                     + (_skins.On() ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_UpdatePreview_A64CA4F418,skins) : "")
                     + (BlankCover ? UiStrings.S_MergeDialog_UpdatePreview_140D4F9E33 : "")
                     + string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_UpdatePreview_2981429CE0,p.Frames.Count) + (p.Tags.Count > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_UpdatePreview_6CCB9AF0FE,p.Tags.Count) : "");
        _notes.Text = string.Join("\n", notes.Select(n => "• " + n));
        _notes.IsVisible = notes.Count > 0;
    }

    /// <summary>결과 프레임을 차례로 재생하는 미리보기(체커 배경, 정수 배율).</summary>
    private sealed class MergePreview : Control
    {
        private readonly List<(WriteableBitmap Bitmap, int Ms)> _frames = new();
        private readonly DispatcherTimer _timer = new();
        private int _index;

        public MergePreview()
        {
            Width = 260;
            Height = 260;
            _timer.Tick += (_, _) =>
            {
                _index = (_index + 1) % Math.Max(1, _frames.Count);
                _timer.Interval = TimeSpan.FromMilliseconds(_frames[_index].Ms);
                InvalidateVisual();
            };
        }

        public void SetProject(SpriteProject p)
        {
            Clear();
            for (int f = 0; f < p.Frames.Count; f++)
                _frames.Add((ImageUtil.ToBitmap(p.Flatten(f)), p.Frames[f].DurationMs));
            _index = 0;
            if (_frames.Count > 1)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].Ms);
                _timer.Start();
            }
            InvalidateVisual();
        }

        public void Clear()
        {
            _timer.Stop();
            foreach (var (b, _) in _frames) b.Dispose();
            _frames.Clear();
        }

        public override void Render(DrawingContext g)
        {
            var area = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.InputBrush, area);
            if (_frames.Count == 0) return;
            var bmp = _frames[_index].Bitmap;
            var size = bmp.PixelSize;
            var dest = ImageUtil.DrawFitted(g, bmp, new System.Drawing.Rectangle(0, 0, size.Width, size.Height), area.Deflate(8), checker: true);
            g.DrawRectangle(new Pen(AppTheme.BorderBrush), dest.Inflate(0.5));
            if (_frames.Count > 1)
                g.DrawText(new FormattedText($"{_index + 1}/{_frames.Count}", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 11, AppTheme.DimTextBrush), new Avalonia.Point(4, 2));
        }
    }

    /// <summary>
    /// 합친 결과의 타임라인 모양(읽기 전용): 위쪽 태그 띠, 프레임 번호·길이, 행마다 레이어(그룹 ▼, 부위는 들여쓰기)와 칸 썸네일.
    /// 스킨이 여럿인 부위(그룹 전체 스킨 + 그 부위 전용 스킨) 아래에는 스킨마다 흐린 행(타임라인에서는 착장을 바꿔 보는 그림)을 덧붙인다.
    /// 빈 칸은 비워 둔다. 행 순서는 타임라인과 같이 맨 위 레이어부터.
    /// </summary>
    private sealed class TimelinePreview : Control
    {
        private const double NameW = 150, CellW = 44, RowH = 34, Thumb = 28, HeadH = 30, TagH = 16;
        private SpriteProject? _project;
        private readonly List<(string Name, bool Group, bool Part, bool Skin, bool PartOnly, WriteableBitmap?[] Thumbs)> _rows = new();

        public void SetProject(SpriteProject p)
        {
            Clear();
            _project = p;
            WriteableBitmap?[] Thumbs(Func<int, PixelImage?> cel)
                => Enumerable.Range(0, p.Frames.Count)
                    .Select(f => cel(f) is { } img && !img.IsRegionEmpty(img.Bounds) ? ImageUtil.MakeThumbnail(img, (int)Thumb) : null).ToArray();
            for (int li = p.Layers.Count - 1; li >= 0; li--)
            {
                var layer = p.Layers[li];
                string name = layer.IsPart ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_MergeDialog_SetProject_AB9B2445C1,layer.Name,layer.PartSize.Width,layer.PartSize.Height,(layer.Visible ? "" : UiStrings.S_MergeDialog_SetProject_00D4A9DF3D)) : layer.Name;
                int index = li;
                _rows.Add((name, layer.IsGroup, layer.IsPart, false, false,
                    Thumbs(f => layer.IsGroup ? p.FlattenGroup(layer, f) : p.GetCel(index, f))));
                if (layer is { IsPart: true, SkinChoices.Count: > 1 })
                    foreach (var skin in layer.SkinChoices)
                        _rows.Add((skin.Name, false, false, true, skin.IsPartOnly, Thumbs(f => p.GetSkinCel(layer, p.Frames[f], skin))));
            }
            Width = NameW + p.Frames.Count * CellW + 4;
            Height = Top + _rows.Count * RowH + 2;
            InvalidateMeasure();
            InvalidateVisual();
        }

        private double Top => (_project?.Tags.Count > 0 ? TagH : 0) + HeadH;

        public void Clear()
        {
            foreach (var row in _rows)
                foreach (var b in row.Thumbs) b?.Dispose();
            _rows.Clear();
            _project = null;
        }

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.PanelBrush, new Rect(Bounds.Size));
            if (_project is not { } p) return;
            double small = AppTheme.SmallFontSize;
            var line = AppTheme.Pen(AppTheme.BorderColor);
            double y0 = 0;
            if (p.Tags.Count > 0)
            {
                foreach (var t in p.Tags)
                {
                    var r = new Rect(NameW + t.From * CellW + 1, 1, (t.To - t.From + 1) * CellW - 2, TagH - 3);
                    g.FillRectangle(AppTheme.Brush(t.Color.ToColor()), r);
                    AppTheme.DrawTextIn(g, t.Name, r, Brushes.White, small, padLeft: 3);
                }
                y0 = TagH;
            }
            AppTheme.DrawTextIn(g, UiStrings.S_MergeDialog_Render_B55FF80314, new Rect(4, y0, NameW - 4, HeadH), AppTheme.DimTextBrush, small);
            for (int f = 0; f < p.Frames.Count; f++)
            {
                double x = NameW + f * CellW;
                AppTheme.DrawTextIn(g, (f + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), new Rect(x, y0, CellW, HeadH / 2 + 2), AppTheme.TextBrush, small, bold: true, center: true);
                AppTheme.DrawTextIn(g, $"{p.Frames[f].DurationMs}ms", new Rect(x, y0 + HeadH / 2, CellW, HeadH / 2), AppTheme.DimTextBrush, small - 1, center: true);
            }
            double top = Top;
            g.DrawLine(line, new Avalonia.Point(0, top - 0.5), new Avalonia.Point(Bounds.Width, top - 0.5));
            for (int row = 0; row < _rows.Count; row++)
            {
                var (name, isGroup, isPart, isSkin, partOnly, thumbs) = _rows[row];
                double y = top + row * RowH, cy = y + RowH / 2;
                if (isGroup) g.FillRectangle(AppTheme.HoverBrush, new Rect(0, y, Bounds.Width, RowH));
                // 이름 앞 표시: 그룹 = 펼친 삼각형, 부위 = 나무 가지, 스킨 = 마름모(부위 전용이면 채움)
                double textX = 6;
                if (isGroup)
                {
                    VectorIcons.Draw(g, VectorIcon.ChevronDown, new Rect(4, y, 12, RowH), 11, AppTheme.TextBrush);
                    textX = 20;
                }
                else if (isPart)
                {
                    g.DrawLine(line, new Avalonia.Point(20.5, y), new Avalonia.Point(20.5, cy + 0.5));
                    g.DrawLine(line, new Avalonia.Point(20.5, cy + 0.5), new Avalonia.Point(28, cy + 0.5));
                    textX = 32;
                }
                else if (isSkin)
                {
                    VectorIcons.Draw(g, VectorIcon.Diamond, new Rect(34, y, 10, RowH), 9, AppTheme.DimTextBrush, filled: partOnly);
                    textX = 48;
                }
                AppTheme.DrawTextIn(g, name, new Rect(textX, y, NameW - textX - 2, RowH), isSkin ? AppTheme.DimTextBrush : AppTheme.TextBrush, small, bold: !isPart && !isSkin, padLeft: 0);
                for (int f = 0; f < thumbs.Length; f++)
                {
                    var cell = new Rect(NameW + f * CellW + (CellW - Thumb) / 2, y + (RowH - Thumb) / 2, Thumb, Thumb);
                    g.FillRectangle(AppTheme.InputBrush, cell);
                    if (thumbs[f] is { } bmp)
                        using (ImageUtil.Pixelated(g))
                            g.DrawImage(bmp, cell);
                }
                g.DrawLine(line, new Avalonia.Point(0, y + RowH - 0.5), new Avalonia.Point(Bounds.Width, y + RowH - 0.5));
            }
        }
    }
}
