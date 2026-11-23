using Chickxel.Core.Assets;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 효과 목록 패널(도구 막대의 효과 단추로 맨 왼쪽 칸에 엶 — 파일 브라우저와 함께면 그 아래, <see cref="MainForm"/>): 효과 창 왼쪽 칸과 같은 효과·프리셋 상자(<see cref="EffectTileCatalog"/>)를 한 줄씩 보인다.
/// 상자를 타임라인 칸(레이어 × 프레임)으로 끌어 놓으면 효과 창을 띄우지 않고 기본 설정으로 그 레이어에 넣고(<see cref="TimelinePanel.DropEffects"/> — 실행취소 한 단계),
/// 두 번 누르면 지금 레이어의 지금 칸(고른 칸 묶음이면 그 프레임 범위)에 넣는다. 넣은 효과의 범위는 타임라인 효과 줄 끝을 끌어 바꾸고,
/// 마지막 프레임 너머로 끌면 마지막 프레임과 링크된 프레임이 늘어난다. 미리보기는 효과가 정한 미리보기 병아리에 입힌 모습이다(<see cref="EffectTile"/> — 지금 그림과 상관없어 다시 그리지 않음).
/// 맨 위 검색 칸은 두 탭을 이름·설명·만든 사람으로 바로 거르고, 우클릭 > 즐겨찾기에 추가한 상자는 탭 맨 위 "즐겨찾기" 묶음에 별을 달고 보인다
/// (<see cref="AppSettings.EffectFavorites"/> — 바로 저장, 다음 실행에도 남음).
/// </summary>
public sealed class EffectListPanel : UserControl
{
    private const int RowHeight = 44, ThumbHold = 4;

    private readonly TimelinePanel _timeline;
    private readonly AppSettings _settings;
    private readonly TextBox _search;
    private readonly StackPanel _effectRows = new() { Spacing = 2 };
    private readonly StackPanel _presetRows = new() { Spacing = 2 };
    private readonly TextBlock _error = Ui.Label("");
    private readonly TextBlock _effectEmpty = Ui.Label("");
    private readonly TextBlock _presetEmpty = Ui.Label("");
    private readonly List<EffectTile> _tiles = new();
    /// <summary>탭마다 상자(카탈로그 차례 그대로 — 보이는 차례는 <see cref="Arrange"/>가 정함).</summary>
    private readonly List<EffectTile> _effectTiles = new(), _presetTiles = new();
    private readonly DispatcherTimer _thumbTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _thumbTick;
    /// <summary>에셋이 바뀌어 상자를 다시 만들어야 함(숨겨 둔 동안은 미뤘다가 보일 때).</summary>
    private bool _stale = true;
    private EffectTile? _pressed;
    private Avalonia.Point _pressPoint;
    private bool _dragging;

    public EffectListPanel(TimelinePanel timeline, AppSettings settings)
    {
        _timeline = timeline;
        _settings = settings;
        Background = AppTheme.PanelBrush;

        var header = Ui.Header(UiStrings.EffectList_Title);
        var hint = Ui.Label(UiStrings.EffectList_Hint);
        hint.Foreground = AppTheme.DimTextBrush;
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        var hintRow = new DockPanel { Margin = new Thickness(2, 2, 2, 0) };
        var badge = Ui.InfoBadge(UiStrings.EffectList_Help);
        DockPanel.SetDock(badge, Dock.Right);
        hintRow.Children.Add(badge);
        hintRow.Children.Add(hint);
        DockPanel.SetDock(hintRow, Dock.Top);

        _search = new TextBox
        {
            Watermark = UiStrings.EffectList_Search,
            Margin = new Thickness(4, 4, 4, 2),
            InnerLeftContent = new IconView(VectorIcon.Search) { Width = 14, Height = 14, Margin = new Thickness(6, 0, 0, 0), Foreground = AppTheme.DimTextBrush },
        };
        _search.Classes.Add("clearButton");
        _search.Tip(UiStrings.EffectList_SearchTip);
        _search.TextChanged += (_, _) => ArrangeAll();
        DockPanel.SetDock(_search, Dock.Top);

        foreach (var label in new[] { _error, _effectEmpty, _presetEmpty }) label.TextWrapping = TextWrapping.Wrap;
        _effectEmpty.Foreground = AppTheme.DimTextBrush;
        _error.Foreground = AppTheme.WarningBrush;
        _error.IsVisible = false;
        DockPanel.SetDock(_error, Dock.Top);

        ScrollViewer Scroll(Control content) => new()
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 12, 0), // Fluent 스크롤바가 줄 끝의 물음표를 가리지 않게
        };
        var effects = new DockPanel { Children = { _effectEmpty, Scroll(_effectRows) } };
        var presets = new DockPanel { Children = { _presetEmpty, Scroll(_presetRows) } };
        DockPanel.SetDock(_effectEmpty, Dock.Top);
        DockPanel.SetDock(_presetEmpty, Dock.Top);
        // 좁은 패널이라 탭 머리는 본문 글자 크기로(Fluent 기본 탭 머리는 큰 글자)
        TabItem Tab(string header, Control content) => new()
        {
            Header = header, Content = content, FontSize = AppTheme.FontSize + 1, MinHeight = 0, Padding = new Thickness(8, 4),
        };
        var tabs = new TabControl { Margin = new Thickness(2, 0, 2, 0) };
        tabs.Items.Add(Tab(UiStrings.EffectPreset_EffectsTab, effects).HelpAt("screen-effect-list"));
        tabs.Items.Add(Tab(UiStrings.EffectPreset_PresetsTab, presets).HelpAt("screen-effect-recipes", anchor: "Presets tab"));

        // 맨 아래 색 단추 둘(왼쪽·오른쪽 반씩, 서로 다른 색): 추가 다운로드(파랑) = 홈페이지 상점(패키지 목록)을 브라우저로, 효과 만들기(초록) = 효과 만들기 창
        static Button Accent(VectorIcon icon, string text, string tip, IBrush fill, Action onClick)
        {
            var b = Ui.Button(icon, text, (_, _) => onClick());
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.Padding = new Thickness(4, b.Padding.Top, 4, b.Padding.Bottom);
            b.Background = fill;
            b.Foreground = Brushes.White;
            if (b.Content is StackPanel { Children: [IconView view, ..] }) view.Foreground = Brushes.White;
            Ui.KeepFillOnHover(b);
            ToolTip.SetTip(b, tip);
            return b;
        }
        var store = Accent(VectorIcon.Download, UiStrings.EffectList_MoreDownloads,
            string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectList_MoreDownloadsTip, AppInfo.StoreUrl),
            AppTheme.AccentBrush, () => Updater.OpenUrl(this, AppInfo.StoreUrl)).HelpAt("screen-asset-manager");
        var maker = Accent(VectorIcon.Sparkles, UiStrings.EffectMaker_Title, UiStrings.EffectList_MakerTip, AppTheme.SavedBrush, () =>
        {
            if (TopLevel.GetTopLevel(this) is MainForm main) EffectMakerWindow.Open(main, ((Ai.IAiHost)main).Context);
        }).HelpAt("screen-effect-maker");
        Grid.SetColumn(maker, 2);
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,4,*"),
            Margin = new Thickness(4, 4, 4, 6),
            Children = { store, maker },
        };
        DockPanel.SetDock(actions, Dock.Bottom);

        Content = new DockPanel { Children = { header, hintRow, _search, _error, actions, tabs } };

        _thumbTimer.Tick += (_, _) =>
        {
            // 움직이는 효과: 0 → 끝까지 돌리고 끝 모습을 잠깐 보여 준 뒤 처음부터(효과 창 상자와 같음).
            _thumbTick = (_thumbTick + 1) % (AssetPreview.Steps + ThumbHold);
            int step = Math.Min(_thumbTick, AssetPreview.Steps - 1);
            foreach (var tile in _tiles)
                if (tile.Animated && tile.IsEffectivelyVisible) tile.Show(step);
        };
        // 에셋 관리자에서 켜고 끄거나 지우면 상자를 다시 만든다(보이지 않으면 보일 때).
        AppAssets.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            _stale = true;
            if (IsVisible) Reload();
        });
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (IsVisible) Shown();
            else
            {
                _thumbTimer.Stop();
                EndDrag();
            }
        };
        this.HelpAt("screen-effect-list");
    }

    private void Shown()
    {
        if (_stale) Reload();
        _thumbTimer.Start();
    }

    /// <summary>효과·프리셋 상자를 새로 만든다(<see cref="EffectTileCatalog"/> 차례, 즐겨찾기 표시는 설정에서).</summary>
    private void Reload()
    {
        _stale = false;
        foreach (var tile in _tiles) tile.Dispose();
        _tiles.Clear();
        _effectTiles.Clear();
        _presetTiles.Clear();
        var favorites = _settings.EffectFavorites.ToHashSet(StringComparer.Ordinal);
        var (effects, presets) = EffectTileCatalog.Create(null, compact: true);
        foreach (var tile in effects) AddTile(tile, _effectTiles, favorites);
        foreach (var tile in presets) AddTile(tile, _presetTiles, favorites);
        string? error = EffectTileCatalog.LoadError;
        _error.Text = error ?? "";
        _error.IsVisible = error is not null;
        ArrangeAll();
    }

    private void AddTile(EffectTile tile, List<EffectTile> list, HashSet<string> favorites)
    {
        tile.Height = RowHeight;
        tile.Favorite = favorites.Contains(tile.Key);
        tile.Show(0);
        AttachInput(tile);
        PopupMenus.Attach(tile, () => Menu(tile));
        _tiles.Add(tile);
        list.Add(tile);
    }

    // ───────────── 검색·즐겨찾기 ─────────────

    private void ArrangeAll()
    {
        string query = (_search.Text ?? "").Trim();
        Arrange(_effectRows, _effectTiles, _effectEmpty, query, null);
        Arrange(_presetRows, _presetTiles, _presetEmpty, query, EffectTileCatalog.LoadError ?? UiStrings.EffectPreset_Empty);
    }

    /// <summary>
    /// 탭 하나의 줄을 다시 놓는다: 검색어에 맞는 상자만, 즐겨찾기가 있으면 "즐겨찾기" 묶음 → "나머지" 묶음 차례(묶음 안은 카탈로그 차례).
    /// 맞는 상자가 없으면 위 안내 줄에 까닭(검색 결과 없음, 프리셋이 아예 없으면 <paramref name="emptyText"/>).
    /// </summary>
    private static void Arrange(StackPanel rows, List<EffectTile> tiles, TextBlock empty, string query, string? emptyText)
    {
        rows.Children.Clear();
        var shown = tiles.Where(tile => tile.Matches(query)).ToList();
        var favorites = shown.Where(tile => tile.Favorite).ToList();
        if (favorites.Count > 0)
        {
            rows.Children.Add(Section(VectorIcon.Star, UiStrings.EffectList_Favorites, AppTheme.FavoriteBrush));
            rows.Children.AddRange(favorites);
            if (favorites.Count < shown.Count) rows.Children.Add(Section(null, UiStrings.EffectList_Others, AppTheme.DimTextBrush));
        }
        rows.Children.AddRange(shown.Where(tile => !tile.Favorite));
        string? text = tiles.Count == 0 ? emptyText
            : shown.Count == 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectList_NoMatch, query)
            : null;
        empty.Text = text ?? "";
        empty.IsVisible = text is not null;
    }

    /// <summary>묶음 머리 줄(즐겨찾기·나머지): 작은 흐린 글씨, 즐겨찾기는 별 아이콘.</summary>
    private static Control Section(VectorIcon? icon, string text, IBrush iconBrush)
    {
        var label = new TextBlock { Text = text, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(2, 4, 0, 1) };
        if (icon is { } i) row.Children.Add(new IconView(i) { Width = 12, Height = 12, Foreground = iconBrush });
        row.Children.Add(label);
        return row;
    }

    /// <summary>상자 우클릭 메뉴: 즐겨찾기에 추가/빼기, 에셋 상자면 그 아래 에셋 메뉴(<see cref="EffectTileCatalog.Menu"/>).</summary>
    private IEnumerable<object> Menu(EffectTile tile)
    {
        var favorite = new MenuItem
        {
            Header = tile.Favorite ? UiStrings.EffectList_Unfavorite : UiStrings.EffectList_Favorite,
            Icon = Ui.Icon(VectorIcon.Star, 14),
        };
        favorite.Click += (_, _) => Dispatcher.UIThread.Post(() => ToggleFavorite(tile));
        var items = new List<object> { favorite };
        if (tile.Entry is { } entry && TopLevel.GetTopLevel(this) is Window owner)
        {
            items.Add(new Separator());
            items.AddRange(EffectTileCatalog.Menu(owner, entry));
        }
        return items;
    }

    /// <summary>즐겨찾기를 켜고 끈다: 설정에 바로 저장하고 그 탭 줄을 다시 놓는다.</summary>
    private void ToggleFavorite(EffectTile tile)
    {
        tile.Favorite = !tile.Favorite;
        _settings.EffectFavorites.RemoveAll(key => key == tile.Key);
        if (tile.Favorite) _settings.EffectFavorites.Add(tile.Key);
        _settings.Save();
        ArrangeAll();
    }

    // ───────────── 두 번 누르기·타임라인으로 끌어 놓기 ─────────────

    private void AttachInput(EffectTile tile)
    {
        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
            // 물음표: 넣지 않고 설명만 띄운다(두 번째 누르기도 넣기로 넘기지 않음)
            if (tile.HelpHit(e.GetPosition(tile)))
            {
                e.Handled = true;
                EffectTileCatalog.ShowHelp(tile);
                return;
            }
            if (e.ClickCount == 2)
            {
                _timeline.AddEffectsAtCurrent(tile.Effects, tile.Template);
                return;
            }
            _pressed = tile;
            _pressPoint = e.GetPosition(this);
            _dragging = false;
            e.Pointer.Capture(tile);
        };
        tile.PointerMoved += (_, e) =>
        {
            if (_pressed != tile) return;
            if (!_dragging && ReorderCore.Exceeded(_pressPoint, e.GetPosition(this))) _dragging = true;
            if (!_dragging) return;
            bool over = _timeline.ShowEffectDrop(e);
            tile.Cursor = new Cursor(over ? StandardCursorType.DragCopy : StandardCursorType.No);
        };
        tile.PointerReleased += (_, e) =>
        {
            if (_pressed != tile) return;
            bool drop = _dragging;
            EndDrag();
            e.Pointer.Capture(null);
            if (drop) _timeline.DropEffects(e, tile.Effects, tile.Template);
        };
        tile.PointerCaptureLost += (_, _) =>
        {
            if (_pressed == tile) EndDrag();
        };
    }

    private void EndDrag()
    {
        if (_pressed is { } tile) tile.Cursor = new Cursor(StandardCursorType.Hand);
        bool dragging = _dragging;
        _pressed = null;
        _dragging = false;
        if (dragging) _timeline.HideEffectDrop();
    }
}
