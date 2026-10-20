using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 파일 브라우저에서 고른 파일 합치기: 순서(끌어서 바꾸기), 결과 이름, 방식(레이어/그룹/스킨/프레임), 그룹·스킨이면 빈 첫 항목,
/// 캔버스 크기·기준점, 원본 삭제, 미리보기.
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
    private readonly CheckBox _deleteOriginals, _emptyFirst;
    private readonly DragReorder _drag = new();
    private readonly MergePreview _preview = new();
    private readonly TimelinePreview _timeline = new();
    private readonly TextBlock _info, _warning;
    private bool _ready;

    /// <param name="items">합칠 파일(고른 순서)과 읽은 내용.</param>
    /// <param name="folder">결과 <c>.gpixel</c>을 만들 폴더.</param>
    public MergeDialog(IReadOnlyList<(string Path, MergeSource Source)> items, string folder) : base($"파일 {items.Count}개 합치기", "합치기")
    {
        _items = items.ToList();
        _folder = folder;

        _order = new ListBox { Width = 220, Height = 150, Background = AppTheme.InputBrush, Foreground = AppTheme.TextBrush }
            .Tip("줄을 끌어 순서를 바꿉니다(위 = 먼저).");
        _drag.Moved += (from, to) => MoveItem(from, to);
        var up = Ui.Button("▲", (_, _) => MoveSelected(-1), 32).Tip("위로(먼저)");
        var down = Ui.Button("▼", (_, _) => MoveSelected(1), 32).Tip("아래로(나중)");
        _emptyFirst = Ui.Check("첫 항목을 빈 칸으로");
        _emptyFirst.IsVisible = false;
        RefreshOrder(0);

        var maxW = items.Max(i => i.Source.Size.Width);
        var maxH = items.Max(i => i.Source.Size.Height);
        _layers = Radio("레이어로", "파일마다 레이어 1개(첫 파일이 맨 위)", true);
        _group = Radio("그룹으로", "그룹 하나에 파일마다 부위 1개(파일 크기 그대로)", false);
        _skins = Radio("스킨으로", "그룹 하나에 부위 1개, 파일마다 스킨 1개(스킨 이름 = 파일 이름, 첫 파일 = 기본 스킨, 부위 크기 = 가장 큰 파일)", false);
        _frames = Radio("프레임으로", "파일마다 프레임(차례로 이어 붙임, 칸 이름 = 파일 이름)", false);
        _name = Ui.Text(UniqueName(ProjectMerge.SuggestName(items.Select(i => i.Source.Name).ToList())), 200);
        _w = Ui.Num(1, PixelImage.MaxSize, maxW, 70);
        _h = Ui.Num(1, PixelImage.MaxSize, maxH, 70);
        var fit = Ui.Button("가장 큰 크기", (_, _) => { _w.Value = maxW; _h.Value = maxH; });
        _deleteOriginals = Ui.Check("원본 삭제(앱 휴지통에 보관, Ctrl+Z로 되살림) — 끄면 원본을 그대로 둡니다");
        _info = Ui.Label("");
        _warning = Ui.Label("");
        _warning.Foreground = Brushes.OrangeRed;
        _warning.TextWrapping = TextWrapping.Wrap;
        _warning.MaxWidth = 300;

        var left = Ui.Stack(
            Ui.Label("순서(위 = 먼저)", bold: true),
            Ui.Row(_order, Ui.Stack(up, down)),
            Ui.Row(Ui.Label("이름"), _name, Ui.Label(SpriteProject.FileExtension)),
            Ui.Label("합치는 방식", bold: true),
            _layers, _group, _skins, _frames,
            _emptyFirst,
            Ui.Label("캔버스", bold: true),
            Ui.Row(Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px"), fit),
            Ui.Label("기준점(파일 그림이 놓일 위치)"),
            _anchor,
            _deleteOriginals,
            _warning);
        var right = Ui.Stack(Ui.Label("미리보기", bold: true), _preview, _info);
        Body.Children.Add(Ui.Row(left, right));
        // 합친 결과가 타임라인에서 어떻게 보이는지(행 = 레이어·그룹·부위, 열 = 프레임) — 방식을 바꾸면 바로 바뀐다.
        Body.Children.Add(Ui.Label("타임라인에서는 이렇게 보입니다", bold: true));
        Body.Children.Add(new ScrollViewer
        {
            Content = _timeline,
            MaxWidth = 700,
            MaxHeight = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        foreach (var r in new[] { _layers, _group, _skins, _frames }) r.IsCheckedChanged += (_, _) => { UpdateEmptyFirst(); UpdatePreview(); };
        _emptyFirst.IsCheckedChanged += (_, _) => { RefreshOrder(Selected); UpdatePreview(); };
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
    /// <summary>빈 첫 항목을 넣는지(그룹·스킨 방식에서 켰을 때만).</summary>
    private bool EmptyFirst => _emptyFirst.On() && Mode is MergeMode.Group or MergeMode.Skins;
    public MergeOptions Options => new(Mode, (int)_w.NumValue(), (int)_h.NumValue(), _anchor.Value, ResultName, EmptyFirst);

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

    private string UniqueName(string name)
    {
        if (!TargetPaths(name).Any(File.Exists)) return name;
        for (int i = 2; ; i++)
            if (!TargetPaths($"{name}_{i}").Any(File.Exists)) return $"{name}_{i}";
    }

    /// <summary>순서 목록 맨 위의 빈 항목 줄 수(지금 목록에 빈 첫 항목 줄이 있으면 1 — 고정, 끌 수 없음).</summary>
    private int _lead;

    /// <summary>고른 파일 번호(빈 항목 줄 뺀 번호, 없으면 -1).</summary>
    private int Selected => Math.Max(-1, _order.SelectedIndex - _lead);

    private void UpdateEmptyFirst()
    {
        bool group = _group.On();
        _emptyFirst.IsVisible = group || _skins.On();
        _emptyFirst.Content = group
            ? $"첫 항목을 빈 칸으로(맨 위에 빈 부위 \"{ProjectMerge.EmptyName}\")"
            : $"첫 항목을 빈 칸으로(기본 스킨 = 빈 스킨 \"{ProjectMerge.EmptyName}\")";
        _emptyFirst.Tip(group
            ? "그룹 맨 위에 그림 없는 부위를 넣습니다 — 합친 파일 부위들은 그 아래 대안이 됩니다."
            : "첫(기본·지금) 스킨을 그림 없는 스킨으로 둡니다 — 합친 파일 스킨들은 그 뒤에 옷 갈아입기 대안이 됩니다.");
        RefreshOrder(Selected);
    }

    private void RefreshOrder(int select)
    {
        _drag.Clear();
        _order.Items.Clear();
        _lead = EmptyFirst ? 1 : 0;
        if (_lead > 0)
            _order.Items.Add(new ListBoxItem
            {
                Content = $"∅ {ProjectMerge.EmptyName} ({(_group.On() ? "빈 부위" : "빈 스킨")}, 고정)",
                IsEnabled = false,
                Foreground = AppTheme.DimTextBrush,
            });
        foreach (var (path, s) in _items)
        {
            var item = new ListBoxItem { Content = $"{Path.GetFileName(path)}  ({s.Size.Width}×{s.Size.Height}{(s.Frames.Count > 1 ? $", {s.Frames.Count}프레임" : "")})" };
            _order.Items.Add(item);
            _drag.Attach(item);
        }
        _order.SelectedIndex = select < 0 ? -1 : Math.Min(select, _items.Count - 1) + _lead;
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
            return "이름이 비었거나 쓸 수 없는 문자가 있습니다.";
        return Conflicts(name) is { Count: > 0 } c ? $"이미 같은 이름의 파일이 있습니다: {string.Join(", ", c.Select(Path.GetFileName))}" : null;
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
            ? "쓸 수 없는 이름입니다."
            : Conflicts(name) is { Count: > 0 } c
                ? $"이미 있는 파일: {string.Join(", ", c.Select(Path.GetFileName))} — 이름을 바꾸세요."
                : "";
    }

    private void UpdatePreview()
    {
        if (!_ready) return;
        UpdateWarning();
        var p = ProjectMerge.Merge(_items.Select(i => i.Source).ToList(), Options);
        _preview.SetProject(p);
        _timeline.SetProject(p);
        int parts = p.Layers.Count(l => l.IsPart);
        int skins = p.Layers.Where(l => l.IsGroup).Sum(l => l.Skins.Count);
        _info.Text = $"{p.Width}×{p.Height} · 레이어 {p.Layers.Count(l => !l.IsPart)}개" + (parts > 0 ? $"(부위 {parts}개)" : "")
                     + (_skins.On() ? $" · 스킨 {skins}개(미리보기 = 첫 스킨{(EmptyFirst ? $" \"{ProjectMerge.EmptyName}\" — 비어 보임" : "")})" : "")
                     + $" · 프레임 {p.Frames.Count}개" + (p.Tags.Count > 0 ? $" · 태그 {p.Tags.Count}개" : "");
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
    /// 스킨이 여럿인 그룹의 부위 아래에는 스킨마다 흐린 행(타임라인에서는 착장을 바꿔 보는 그림)을 덧붙인다.
    /// 빈 칸은 비워 둔다. 행 순서는 타임라인과 같이 맨 위 레이어부터.
    /// </summary>
    private sealed class TimelinePreview : Control
    {
        private const double NameW = 150, CellW = 44, RowH = 34, Thumb = 28, HeadH = 30, TagH = 16;
        private SpriteProject? _project;
        private readonly List<(string Name, bool Group, bool Part, bool Skin, WriteableBitmap?[] Thumbs)> _rows = new();

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
                string name = layer.IsGroup ? $"▼ {layer.Name}" : layer.IsPart ? $"    └ {layer.Name}  {layer.PartSize.Width}×{layer.PartSize.Height}" : layer.Name;
                int index = li;
                _rows.Add((name, layer.IsGroup, layer.IsPart, false,
                    Thumbs(f => layer.IsGroup ? p.FlattenGroup(layer, f) : p.GetCel(index, f))));
                if (layer is { IsPart: true, Parent: { IsGroup: true } g } && g.Skins.Count > 1)
                    foreach (var skin in g.Skins)
                        _rows.Add(($"        ◇ {skin.Name}", false, false, true, Thumbs(f => p.GetSkinCel(layer, p.Frames[f], skin))));
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
            AppTheme.DrawTextIn(g, "레이어 ＼ 프레임", new Rect(4, y0, NameW - 4, HeadH), AppTheme.DimTextBrush, small);
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
                var (name, isGroup, isPart, isSkin, thumbs) = _rows[row];
                double y = top + row * RowH;
                if (isGroup) g.FillRectangle(AppTheme.HoverBrush, new Rect(0, y, Bounds.Width, RowH));
                AppTheme.DrawTextIn(g, name, new Rect(6, y, NameW - 8, RowH), isSkin ? AppTheme.DimTextBrush : AppTheme.TextBrush, small, bold: !isPart && !isSkin, padLeft: 0);
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
