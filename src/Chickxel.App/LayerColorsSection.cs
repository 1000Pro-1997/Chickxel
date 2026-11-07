using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 색 패널 맨 아래 "레이어 색": 지금 고른 레이어의 모든 프레임에 쓰인 색(투명 빼고, 많이 쓰인 순 — 일반 레이어는 캔버스 밖 그림까지, 부위는 보이는 스킨 셀,
/// 그룹은 그 부위들). 머리글을 누르면 접고 펼치며, 색이 많으면 몇 줄 높이 안에서 스크롤한다. 칸 왼쪽 클릭 = 기본색, 오른쪽 = 보조색.
/// 레이어를 바꾸거나 그림이 바뀌면 잠깐 뒤 작업 스레드에서 다시 센다(접혀 있으면 세지 않고, 펼칠 때 센다).
/// </summary>
internal sealed class LayerColorsSection : StackPanel
{
    /// <summary>보일 색 수 상한(많이 쓰인 순으로 앞에서부터).</summary>
    public const int MaxColors = 1024;

    /// <summary>펼쳤을 때 색 칸 영역의 가장 큰 높이(넘치면 스크롤) — 10px 칸 5줄.</summary>
    private const double MaxGridHeight = 5 * 12 + 2;

    private static readonly TimeSpan RecountDelay = TimeSpan.FromMilliseconds(150);

    private readonly EditorContext _ctx;
    private readonly SwatchGrid _grid;
    private readonly ScrollViewer _scroll;
    private readonly IconView _arrow;
    private readonly TextBlock _count;
    private readonly UiDebouncer _recount;
    private bool _collapsed;
    /// <summary>센 결과가 지금 상태와 맞는지 가리는 번호(다시 셀 때마다 늘어남 — 늦게 끝난 옛 결과는 버림).</summary>
    private int _version;

    /// <summary>칸을 누름(왼쪽 = 기본색, 오른쪽 = 보조색).</summary>
    public event EventHandler<(Color32 Color, MouseButton Button)>? ColorClicked;

    /// <summary>접힘이 바뀜(설정 저장용).</summary>
    public event EventHandler? CollapsedChanged;

    public LayerColorsSection(EditorContext ctx)
    {
        _ctx = ctx;
        Orientation = Orientation.Vertical;
        _grid = new SwatchGrid { Cell = 10, ShowMarks = true, FillHeight = true, MinHeight = 16 };
        _grid.ColorClicked += (_, e) => ColorClicked?.Invoke(this, e);
        _scroll = new ScrollViewer
        {
            Content = _grid,
            MaxHeight = MaxGridHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _arrow = new IconView(VectorIcon.ChevronDown, AppTheme.DimTextBrush) { Width = 12, Height = 12, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var title = Ui.Label(UiStrings.LayerColors_Title);
        title.VerticalAlignment = VerticalAlignment.Center;
        _count = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var header = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(0, 2),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _arrow, title, _count } },
        };
        ToolTip.SetTip(header, UiStrings.LayerColors_Tip);
        header.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Collapsed = !Collapsed;
            CollapsedChanged?.Invoke(this, EventArgs.Empty);
        };
        Children.Add(header);
        Children.Add(_scroll);

        _recount = new UiDebouncer(RecountDelay, Recount);
        ctx.ProjectReplaced += (_, _) => _recount.Restart();
        ctx.CurrentChanged += (_, _) => _recount.Restart();
        ctx.ProjectChanged += (_, e) =>
        {
            if ((e.Change & (ProjectChange.Pixels | ProjectChange.Layers | ProjectChange.Frames | ProjectChange.Canvas)) != 0) _recount.Restart();
        };
        ctx.ColorsChanged += (_, _) => _grid.SetMarks(_ctx.Primary, _ctx.Secondary);
        _grid.SetMarks(_ctx.Primary, _ctx.Secondary);
        Refresh();
    }

    /// <summary>접혀 있는지(접으면 색을 세지 않는다).</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            Refresh();
            if (!value) Recount();
        }
    }

    private void Refresh()
    {
        _arrow.Icon = _collapsed ? VectorIcon.ChevronRight : VectorIcon.ChevronDown;
        _scroll.IsVisible = !_collapsed;
    }

    /// <summary>지금 레이어의 셀 그림을 모아(UI 스레드) 작업 스레드에서 색을 센다. 접혀 있으면 지난 결과를 버리기만 한다.</summary>
    private void Recount()
    {
        int version = ++_version;
        if (_collapsed) return;
        var images = CelImages();
        if (images.Count == 0)
        {
            Show([], 0);
            return;
        }
        _ = Task.Run(() =>
        {
            var (colors, total) = CountColors(images);
            Dispatcher.UIThread.Post(() =>
            {
                if (version == _version) Show(colors, total);
            });
        });
    }

    private void Show(IReadOnlyList<Color32> colors, int total)
    {
        _grid.SetColors(colors);
        _count.Text = total > colors.Count
            ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.LayerColors_CountCapped, colors.Count, total)
            : total.ToString(global::System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 지금 레이어의 모든 프레임 셀(같은 그림은 한 번): 일반 레이어는 셀과 캔버스 밖 그림, 부위는 보이는 스킨 셀, 그룹은 그 부위들의 셀.
    /// 그림은 참조만 담는다(세는 동안 그리던 셀이 바뀌어도 다음 변경 때 다시 셈).
    /// </summary>
    private List<PixelImage> CelImages()
    {
        var list = new List<PixelImage>();
        if (_ctx.Project is not { } p || _ctx.CurrentLayerObject is not { } layer) return list;
        var seen = new HashSet<PixelImage>(ReferenceEqualityComparer.Instance);
        void Add(PixelImage? image)
        {
            if (image is not null && seen.Add(image)) list.Add(image);
        }
        var layers = layer.IsGroup ? p.ChildrenOf(layer) : [layer];
        foreach (var l in layers)
        {
            int index = p.IndexOf(l);
            if (index < 0) continue;
            for (int f = 0; f < p.Frames.Count; f++)
            {
                Add(p.GetCel(index, f));
                if (!l.IsPart && p.GetCelOverflow(index, f) is { } overflow) Add(overflow.Image);
            }
        }
        return list;
    }

    /// <summary>투명이 아닌 픽셀의 서로 다른 색(RGBA)을 많이 쓰인 순으로 최대 <see cref="MaxColors"/>개와 전체 색 수.</summary>
    private static (IReadOnlyList<Color32> Colors, int Total) CountColors(List<PixelImage> images)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var image in images)
            foreach (var c in image.Pixels)
                if (c.A != 0) counts[c.Argb] = counts.GetValueOrDefault(c.Argb) + 1;
        var top = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(MaxColors).Select(kv => new Color32(kv.Key)).ToList();
        return (top, counts.Count);
    }
}
