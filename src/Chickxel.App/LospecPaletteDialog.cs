using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls.Documents;
using Chickxel.App.Ai;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Avalonia.VisualTree;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// lospec.com 팔레트 목록 둘러보기(모달 아님). 정렬·색 수·태그로 거르고, 고른 팔레트를 현재 팔레트로 쓰거나 덧붙이거나
/// .hex로 저장한다. 태그 칸의 글자는 팔레트 이름(슬러그)으로도 찾아 맞으면 맨 위에 고정해 보여 준다.
/// 별을 누른 팔레트는 즐겨찾기(<see cref="LospecFavoriteStore"/>, 예시 그림까지 내려받아 둠)로 남아, 정렬의 "즐겨찾기"에서 인터넷 없이 보고 쓴다.
/// 네트워크는 모두 비동기이고, 조건이 바뀌거나 창을 닫으면 진행 중인 요청을 취소한다.
/// </summary>
public sealed class LospecPaletteDialog : DialogBase
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    /// <summary>정렬 칸 항목의 설정 이름(<see cref="SortKey"/>, 칸 순서). 마지막이 즐겨찾기.</summary>
    private static readonly string[] SortKeys = ["default", "downloads", "newest", "alphabetical", "favorites"];
    public const string DefaultSortKey = "default";
    private const int FavoritesSortIndex = 4;

    /// <summary>즐겨찾기 폴더(<c>&lt;설정 폴더&gt;/Chickxel/Lospec</c>).</summary>
    public static string FavoritesRoot => Path.Combine(Platform.AppFolder, "Lospec");

    private readonly LospecFavoriteStore _favorites = LospecFavoriteStore.Load(FavoritesRoot);
    /// <summary>창이 닫히면 취소(즐겨찾기 예시 그림 내려받기).</summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>목록 줄마다의 즐겨찾기 별(<see cref="Control.Tag"/> = 슬러그).</summary>
    private readonly List<Button> _rowStars = new();
    private readonly Button _detailStar, _viewFavorites;
    private bool _opened;

    /// <summary>정렬을 바꿈(설정 이름, <see cref="SortKey"/>).</summary>
    public event Action<string>? SortChanged;

    private readonly Func<IReadOnlyList<Color32>> _current;
    private readonly Action<IReadOnlyList<Color32>> _replace;
    private readonly Func<IReadOnlyList<Color32>, int> _append;

    private readonly ComboBox _sort, _filter;
    private readonly NumericUpDown _count;
    private readonly TextBox _tag;
    private readonly TextBlock _status;
    private readonly Button _retry, _more;
    private readonly ListBox _list;
    private readonly TextBlock _detailTitle, _detailInfo, _hoverHex, _note;
    private readonly BigSwatches _swatches;
    private readonly ExampleStrip _examples;
    private readonly TextBlock _examplesNote;
    private CancellationTokenSource? _examplesCts;
    /// <summary>내려받은 예시 그림(창을 닫으면 버림).</summary>
    private readonly Dictionary<Uri, Bitmap?> _exampleCache = new();
    private readonly Button _use, _add, _save, _web;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private CancellationTokenSource? _cts;
    private LospecQuery _query = new();
    private int _nextPage, _loaded, _total;
    private bool _loading, _failed;
    private ListBoxItem? _pinned;

    /// <param name="current">지금 팔레트.</param>
    /// <param name="replace">팔레트를 이 색들로 바꾼다.</param>
    /// <param name="append">이 색들을 덧붙이고(겹치는 색 제외) 덧붙인 수를 돌려준다.</param>
    public LospecPaletteDialog(Func<IReadOnlyList<Color32>> current, Action<IReadOnlyList<Color32>> replace,
        Func<IReadOnlyList<Color32>, int> append) : base(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_BA8E30F0A4)
    {
        _current = current;
        _replace = replace;
        _append = append;
        CloseOnly();
        CancelButtonControl.IsDefault = false; // Enter는 태그 검색에 쓴다.
        // 확인 = 고른 팔레트를 팔레트로 쓰고 닫기(덮어쓰기 확인에서 취소하면 닫지 않음).
        OkButton.IsVisible = true;
        OkButton.IsEnabled = false;
        OkButton.Click += (_, _) =>
        {
            if (Selected is not { } p || !UsePalette(p)) DialogResult = DialogResult.None;
        };
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 720;
        Height = 600;
        MinWidth = 520;
        MinHeight = 440;

        // ── 거르기 줄
        _sort = Ui.Combo(100, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_C6B4CAD5AA, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_02A80D18D3, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_1C3280FE06, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_6859F79425, UiStrings.Lospec_Sort_Favorites);
        _filter = Ui.Combo(84, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_07EB862B40, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_06F3644153, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_E2D53DCDA5, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_A394BB00BB);
        _count = Ui.Num(1, 256, 8, 64).Presets(2, 3, 4, 5, 6, 8, 12, 16, 24, 32, 64, 256).Unit(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_C4911E03B6).RecentKey("palette.count");
        _count.IsEnabled = false;
        _tag = Ui.Text("", 150);
        _tag.Watermark = UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_2308C983F0;
        ToolTip.SetTip(_tag, UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_3CF9E5D276);
        _tag.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Search(); } };
        // 즐겨찾기는 이 컴퓨터에서 거르므로 글자를 칠 때마다 바로 거른다.
        _tag.TextChanged += (_, _) => { if (_opened && FavoritesView) Search(); };
        _sort.SelectionChanged += (_, _) =>
        {
            if (_opened) Search();
            SortChanged?.Invoke(SortKey);
        };
        _filter.SelectionChanged += (_, _) => { _count.IsEnabled = _filter.SelectedIndex > 0; Search(); };
        _count.ValueChanged += (_, _) => { if (_filter.SelectedIndex > 0) { _debounce.Stop(); _debounce.Start(); } };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Search(); };
        var search = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_0D176B4714, (_, _) => Search(), 60);
        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new Control[] { Ui.Label(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_EE68758A30), _sort, Gap(), Ui.Label(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_F66FE6E7DA), _filter, _count, Gap(), Ui.Label(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_ECD11E2F35), _tag, search })
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            filters.Children.Add(c);
        }

        // ── 상태 줄
        _status = new TextBlock { Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _retry = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_530A8326D1, (_, _) => Retry());
        _retry.IsVisible = false;
        // 연결하지 못했을 때: 인터넷 없이 볼 수 있는 즐겨찾기로.
        _viewFavorites = Ui.Button(VectorIcon.Star, UiStrings.Lospec_Favorites_View, (_, _) => _sort.SelectedIndex = FavoritesSortIndex)
            .Tip(UiStrings.Lospec_Favorites_OfflineHint);
        _viewFavorites.IsVisible = false;
        var statusRow = new DockPanel { Margin = new Thickness(2, 2, 2, 4) };
        DockPanel.SetDock(_viewFavorites, Dock.Right);
        DockPanel.SetDock(_retry, Dock.Right);
        statusRow.Children.Add(_viewFavorites);
        statusRow.Children.Add(_retry);
        statusRow.Children.Add(_status);

        // ── 결과 목록
        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => ShowDetail(Selected);
        _list.DoubleTapped += (_, e) =>
        {
            // 별 단추를 빠르게 두 번 눌러도 팔레트를 쓰지 않는다.
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is null
                && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { Tag: LospecPalette p }) UsePalette(p);
        };
        // 끝 가까이 스크롤하면 다음 쪽을 불러온다.
        _list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            if (e.Source is ScrollViewer sv && sv.Extent.Height > 0 && sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 60) LoadMoreIfIdle();
        });
        _more = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_A4B23137E6, (_, _) => LoadMoreIfIdle(), 90);
        _more.HorizontalAlignment = HorizontalAlignment.Center;
        _more.IsVisible = false;

        // ── 고른 팔레트
        _detailTitle = new TextBlock { FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        _detailInfo = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextTrimming = TextTrimming.CharacterEllipsis };
        _hoverHex = new TextBlock { Foreground = AppTheme.TextBrush, FontFamily = new FontFamily("Menlo, Consolas, monospace"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
        _swatches = new BigSwatches();
        _swatches.HoverChanged += (_, c) => _hoverHex.Text = c?.ToHex() ?? "";
        // 왼쪽: 팔레트 칸과 같은 크기로 줄바꿈한 색 칸. 오른쪽: 이 팔레트로 그린 예시 그림.
        var swatchHost = new ScrollViewer
        {
            Content = _swatches,
            Width = BigSwatches.PreferredWidth,
            Height = ExampleHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = AppTheme.InputBrush,
        };
        _examples = new ExampleStrip();
        _examplesNote = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            Margin = new Thickness(6, 4),
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
        };
        // 예시 그림: 칸을 장 수만큼 똑같이 나눠 보이고, 마우스를 올린 장이 부드럽게 넓어진다(Lospec 사이트처럼).
        var examplesHost = new Panel
        {
            Children = { _examples, _examplesNote },
            Height = ExampleHeight,
            Background = AppTheme.InputBrush,
            Margin = new Thickness(6, 0, 0, 0),
            ClipToBounds = true,
        };
        Grid.SetColumn(examplesHost, 1);
        var swatchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { swatchHost, examplesHost } };
        _use = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_41463D2C53, (_, _) => { if (Selected is { } p) UsePalette(p); });
        _add = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_9163206E82, (_, _) => { if (Selected is { } p) AppendPalette(p); });
        _save = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_48D4D57A7C, (_, _) => { if (Selected is { } p) SavePalette(p); });
        _web = Ui.Button(UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_C15363FA9A, (_, _) => { if (Selected is { } p) OpenWeb(p.WebUrl); });
        _note = new TextBlock { Foreground = AppTheme.AccentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new Control[] { _use, _add, _save, _web, _note }) actions.Children.Add(b);
        _detailStar = MakeStar(() => Selected);
        _detailStar.Margin = new Thickness(0, 0, 4, 0);
        var hexRow = new DockPanel();
        DockPanel.SetDock(_hoverHex, Dock.Right);
        DockPanel.SetDock(_detailStar, Dock.Left);
        hexRow.Children.Add(_hoverHex);
        hexRow.Children.Add(_detailStar);
        hexRow.Children.Add(_detailTitle);
        var detail = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 6, 0, 0),
            Child = new StackPanel { Spacing = 3, Children = { hexRow, _detailInfo, swatchRow, actions } },
        };

        var credit = new TextBlock
        {
            Text = UiStrings.S_LospecPaletteDialog_LospecPaletteDialog_E2E07547CF,
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            Margin = new Thickness(2, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto") };
        void Put(Control c, int row) { Grid.SetRow(c, row); grid.Children.Add(c); }
        Put(filters, 0);
        Put(statusRow, 1);
        Put(_list, 2);
        Put(_more, 3);
        Put(detail, 4);
        Put(credit, 5);

        // 대화상자 본문(StackPanel)은 높이를 주지 않으므로 바깥 DockPanel의 채우기 칸에 바로 둔다.
        var root = (DockPanel)Content!;
        root.Children.Remove(Body);
        root.Children.Add(grid);

        ShowDetail(null);
        Opened += (_, _) => { _opened = true; _tag.Focus(); Search(); };
    }

    /// <summary>정렬(설정 이름: default·downloads·newest·alphabetical·favorites). 모르는 이름은 기본 정렬.</summary>
    public string SortKey
    {
        get => SortKeys[Math.Clamp(_sort.SelectedIndex, 0, SortKeys.Length - 1)];
        set
        {
            int i = Array.IndexOf(SortKeys, value);
            _sort.SelectedIndex = i < 0 ? 0 : i;
        }
    }

    /// <summary>정렬이 "즐겨찾기"(이 컴퓨터에 남긴 팔레트만, 네트워크 없음).</summary>
    private bool FavoritesView => _sort.SelectedIndex == FavoritesSortIndex;

    /// <summary>고른 팔레트 칸(색 칸·예시 그림)의 높이.</summary>
    private const double ExampleHeight = 140;

    private static Control Gap() => new Border { Width = 10 };

    private LospecPalette? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as LospecPalette;

    protected override void OnClosed(EventArgs e)
    {
        _debounce.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _examplesCts?.Cancel();
        _lifetime.Cancel();
        _examples.Clear();
        foreach (var bmp in _exampleCache.Values) bmp?.Dispose();
        _exampleCache.Clear();
        base.OnClosed(e);
    }

    // ───────────── 불러오기 ─────────────

    private LospecQuery ReadQuery() => new(
        FavoritesView ? LospecSort.Default : (LospecSort)Math.Max(0, _sort.SelectedIndex),
        (LospecColorFilter)Math.Max(0, _filter.SelectedIndex),
        (int)_count.NumValue(),
        (_tag.Text ?? "").Trim());

    /// <summary>지금 조건으로 처음부터 다시 찾는다(진행 중인 요청은 취소).</summary>
    private void Search()
    {
        _debounce.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _query = ReadQuery();
        _nextPage = 0;
        _loaded = 0;
        _total = 0;
        _pinned = null;
        _list.Items.Clear();
        _rowStars.Clear();
        ShowDetail(null);
        if (FavoritesView)
        {
            ShowFavorites();
            return;
        }
        var token = _cts.Token;
        _ = LoadPageAsync(token);
        if (LospecPalettes.ToSlug(_query.Tag) is { Length: > 0 } slug) _ = LoadPinnedAsync(slug, token);
    }

    /// <summary>즐겨찾기를 목록에 채운다(이 컴퓨터에서 색 수·제목/태그로 거름, 최근에 더한 것부터, 네트워크 없음).</summary>
    private void ShowFavorites()
    {
        foreach (var f in LospecFavoriteStore.Filter(_favorites.Items, _query.ColorFilter, _query.ColorCount, _query.Tag))
            _list.Items.Add(MakeItem(f.ToPalette(), pinned: false));
        _loaded = _total = _list.Items.Count;
        _more.IsVisible = false;
        UpdateFavoritesStatus();
    }

    private void UpdateFavoritesStatus()
    {
        int total = _favorites.Items.Count, shown = _list.Items.Count;
        SetStatus(total == 0 ? UiStrings.Lospec_Favorites_Empty
            : shown == 0 ? UiStrings.Lospec_Favorites_NoMatch
            : UiStrings.FormatLospec_Favorites_Count(shown: shown, total: total), error: false);
    }

    private void Retry()
    {
        if (_nextPage == 0 || FavoritesView) Search();
        else if (_cts is { } cts) _ = LoadPageAsync(cts.Token);
    }

    private void LoadMoreIfIdle()
    {
        if (_loading || _failed || _cts is null || _loaded >= _total) return;
        _ = LoadPageAsync(_cts.Token);
    }

    private async Task LoadPageAsync(CancellationToken token)
    {
        _loading = true;
        _failed = false;
        SetStatus(_nextPage == 0 ? UiStrings.S_LospecPaletteDialog_LoadPageAsync_3756910EA9 : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_LoadPageAsync_CD0A6F99BD,_loaded,_total), error: false);
        _more.IsVisible = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(RequestTimeout);
            var page = await LospecPalettes.FetchPageAsync(Http, _query with { Page = _nextPage }, timeout.Token);
            if (token.IsCancellationRequested) return;
            _nextPage++;
            _total = page.TotalCount;
            foreach (var p in page.Palettes)
            {
                _list.Items.Add(MakeItem(p, pinned: false));
                _loaded++;
            }
            // 쪽이 비었는데 전체 수가 남았다고 하면 더 받을 것이 없는 것으로 본다.
            if (page.Palettes.Count == 0) _total = _loaded;
            _loading = false;
            UpdateCountStatus();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 조건이 바뀌었거나 창을 닫음.
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or FormatException or IOException)
        {
            if (token.IsCancellationRequested) return;
            _loading = false;
            _failed = true;
            // 인터넷이 없어도 즐겨찾기는 볼 수 있다고 알린다(상태 줄의 "즐겨찾기 보기" 단추).
            SetStatus(ex switch
            {
                OperationCanceledException => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_LoadPageAsync_8947DCE5D3,RequestTimeout.TotalSeconds),
                FormatException => UiStrings.S_LospecPaletteDialog_LoadPageAsync_7E7F70A765,
                _ => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_LoadPageAsync_018F29C1E9,ex.Message),
            } + "\n" + UiStrings.Lospec_Favorites_OfflineHint, error: true);
        }
    }

    /// <summary>태그 칸 글자가 팔레트 이름(슬러그)이면 그 팔레트를 맨 위에 고정한다. 실패는 조용히 넘긴다.</summary>
    private async Task LoadPinnedAsync(string slug, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(RequestTimeout);
            var p = await LospecPalettes.FetchBySlugAsync(Http, slug, timeout.Token);
            if (token.IsCancellationRequested || p is null) return;
            _pinned = MakeItem(p, pinned: true);
            _list.Items.Insert(0, _pinned);
            if (!_loading) UpdateCountStatus();
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
        {
        }
    }

    private void UpdateCountStatus()
    {
        bool more = _loaded < _total;
        _more.IsVisible = more;
        if (_loaded == 0 && _pinned is null)
            SetStatus(_query.Tag.Length > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_UpdateCountStatus_89312F3E76,_query.Tag) : UiStrings.S_LospecPaletteDialog_UpdateCountStatus_FACBC1CCC7, error: false);
        else if (_loaded == 0)
            SetStatus(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_UpdateCountStatus_5AA76C9F5F,_query.Tag), error: false);
        else
            SetStatus($"{_loaded} / {_total}" + (_pinned is not null ? UiStrings.S_LospecPaletteDialog_UpdateCountStatus_27B678EA3A : ""), error: false);
    }

    /// <summary>단추를 누른 결과(팔레트로 씀·덧붙임·저장)를 단추 옆에 알린다.</summary>
    private void Note(string text) => _note.Text = text;

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = error ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
        _retry.IsVisible = error;
        _viewFavorites.IsVisible = error && !FavoritesView;
    }

    // ───────────── 즐겨찾기 ─────────────

    /// <summary>즐겨찾기 별 단추(누르면 <paramref name="palette"/>를 켜고 끔). 모양은 <see cref="SetStar"/>.</summary>
    private Button MakeStar(Func<LospecPalette?> palette)
    {
        var b = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        b.Click += (_, e) =>
        {
            e.Handled = true;
            if (palette() is { } p) ToggleFavorite(p);
        };
        return b;
    }

    /// <summary>즐겨찾기면 채운 별(강조색), 아니면 빈 별.</summary>
    private static void SetStar(Button b, bool favorite)
    {
        b.Content = new IconView(VectorIcon.Star, favorite ? AppTheme.FavoriteBrush : AppTheme.DimTextBrush, 1.8) { Filled = favorite, Width = 14, Height = 14 };
        b.Tip(favorite ? UiStrings.Lospec_Favorites_Remove : UiStrings.Lospec_Favorites_Add);
    }

    private void RefreshStars()
    {
        foreach (var b in _rowStars) SetStar(b, b.Tag is string slug && _favorites.Contains(slug));
        if (Selected is { } p) SetStar(_detailStar, _favorites.Contains(p.Slug));
    }

    /// <summary>
    /// 즐겨찾기를 켜고 끈다(고른 팔레트는 그대로). 켜면 예시 그림을 뒤에서 내려받아 두고, 즐겨찾기 보기에서 끄면 목록에서 뺀다.
    /// </summary>
    private void ToggleFavorite(LospecPalette p)
    {
        bool favorite;
        try
        {
            favorite = _favorites.Toggle(p, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, UiStrings.FormatLospec_Favorites_SaveFailed(message: ex.Message));
            return;
        }
        if (favorite) _ = DownloadExamplesAsync(p);
        else if (FavoritesView)
        {
            foreach (var item in _list.Items.OfType<ListBoxItem>().Where(i => i.Tag is LospecPalette q && q.Slug == p.Slug).ToList())
                _list.Items.Remove(item);
            _rowStars.RemoveAll(b => b.Tag as string == p.Slug);
            _loaded = _total = _list.Items.Count;
            UpdateFavoritesStatus();
        }
        RefreshStars();
    }

    /// <summary>
    /// 즐겨찾기한 팔레트의 예시 그림을 아직 없는 것만 내려받아 둔다. 인터넷이 안 되면 그만두고, 나중에 인터넷이 될 때 그 팔레트를 보이면
    /// <see cref="ShowExamples"/>가 채운다.
    /// </summary>
    private async Task DownloadExamplesAsync(LospecPalette p)
    {
        var token = _lifetime.Token;
        foreach (int i in _favorites.MissingExamples(p.Slug).ToList())
        {
            if (i >= p.Examples.Count) continue;
            byte[] bytes;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(RequestTimeout);
                bytes = await LospecPalettes.FetchImageAsync(Http, p.Examples[i], timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
            {
                return;
            }
            if (token.IsCancellationRequested) return;
            _favorites.WriteExample(p.Slug, i, bytes);
        }
    }

    private ListBoxItem MakeItem(LospecPalette p, bool pinned)
    {
        var title = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            Foreground = AppTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (pinned)
            title.Inlines = [Ui.InlineIcon(VectorIcon.Star, AppTheme.TextBrush, 12, filled: true), new Run(" " + p.Title)];
        else
            title.Text = p.Title;
        var info = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        FillInfo(info, p);
        var star = MakeStar(() => p);
        star.Tag = p.Slug;
        SetStar(star, _favorites.Contains(p.Slug));
        _rowStars.Add(star);
        var head = new DockPanel();
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(star, Dock.Right);
        head.Children.Add(star);
        head.Children.Add(title);
        head.Children.Add(info);
        var strip = new SwatchStrip(p.Colors) { Margin = new Thickness(0, 3, 0, 0) };
        var item = new ListBoxItem
        {
            Content = new StackPanel { Children = { head, strip } },
            Tag = p,
            Padding = new Thickness(6, 4),
        };
        ToolTip.SetTip(item, p.Tags.Count > 0 ? UiStrings.S_LospecPaletteDialog_MakeItem_759399DD94 + string.Join(", ", p.Tags) : null);
        return item;
    }

    /// <summary>작성자 · 색 수 · 좋아요(하트) · 내려받기(아래 화살표) 수 + <paramref name="suffix"/>를 글 줄에 채운다(아이콘은 글자색).</summary>
    private static void FillInfo(TextBlock target, LospecPalette p, string suffix = "")
    {
        var head = new List<string>();
        if (p.Author.Length > 0) head.Add(p.Author);
        head.Add(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_FillInfo_8490C7D755,p.Colors.Count));
        var brush = target.Foreground ?? AppTheme.DimTextBrush;
        var inlines = new InlineCollection { new Run(string.Join(" · ", head)) };
        void Stat(VectorIcon icon, int count)
        {
            inlines.Add(new Run(" · "));
            inlines.Add(Ui.InlineIcon(icon, brush, 10));
            inlines.Add(new Run(" " + count.ToString("N0", CultureInfo.InvariantCulture)));
        }
        if (p.Likes > 0) Stat(VectorIcon.Heart, p.Likes);
        if (p.Downloads > 0) Stat(VectorIcon.Download, p.Downloads);
        if (suffix.Length > 0) inlines.Add(new Run(suffix));
        target.Inlines = inlines;
    }

    // ───────────── 고른 팔레트 ─────────────

    private void ShowDetail(LospecPalette? p)
    {
        _detailTitle.Text = p?.Title ?? UiStrings.S_LospecPaletteDialog_ShowDetail_3963CB3400;
        if (p is null) _detailInfo.Inlines = [new Run(UiStrings.S_LospecPaletteDialog_ShowDetail_699C49ACD8)];
        else FillInfo(_detailInfo, p, p.Tags.Count > 0 ? " · " + string.Join(", ", p.Tags) : "");
        _swatches.SetColors(p?.Colors ?? []);
        ShowExamples(p);
        _hoverHex.Text = "";
        _note.Text = "";
        foreach (var b in new[] { _use, _add, _save, _web, OkButton }) b.IsEnabled = p is not null;
        _detailStar.IsVisible = p is not null;
        if (p is not null) SetStar(_detailStar, _favorites.Contains(p.Slug));
    }

    /// <summary>
    /// 고른 팔레트의 예시 그림을 오른쪽에 보인다(픽셀이 그대로 보이게 정수 배율, 고르는 팔레트가 바뀌면 받던 것은 취소).
    /// 즐겨찾기는 남겨 둔 그림을 먼저 쓰고(인터넷 없이도 보임), 없는 그림은 내려받아 남긴다. 내려받지 못한 그림은 다음에 다시 시도한다.
    /// </summary>
    private async void ShowExamples(LospecPalette? p)
    {
        _examplesCts?.Cancel();
        _examples.Clear();
        if (p is null || p.Examples.Count == 0)
        {
            _examplesNote.Text = p is null ? "" : UiStrings.S_LospecPaletteDialog_ShowExamples_5CCC9687E1;
            return;
        }
        var cts = _examplesCts = new CancellationTokenSource();
        _examplesNote.Text = UiStrings.S_LospecPaletteDialog_ShowExamples_05EE4FBC6D;
        int shown = 0;
        for (int i = 0; i < Math.Min(p.Examples.Count, LospecFavoriteStore.MaxExamples); i++)
        {
            var url = p.Examples[i];
            if (!_exampleCache.TryGetValue(url, out var bmp))
            {
                var bytes = _favorites.ReadExample(p.Slug, i);
                if (bytes is null)
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        timeout.CancelAfter(RequestTimeout);
                        bytes = await LospecPalettes.FetchImageAsync(Http, url, timeout.Token);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
                    {
                        continue;
                    }
                    _favorites.WriteExample(p.Slug, i, bytes);
                }
                bmp = DecodeImage(bytes);
                _exampleCache[url] = bmp;
            }
            if (cts.IsCancellationRequested) return;
            if (bmp is null) continue;
            _examples.Add(bmp, $"{bmp.PixelSize.Width}×{bmp.PixelSize.Height}px · {Path.GetFileName(url.LocalPath)}");
            shown++;
        }
        if (cts.IsCancellationRequested) return;
        _examplesNote.Text = shown == 0 ? UiStrings.S_LospecPaletteDialog_ShowExamples_7293A60341 : "";
    }

    /// <summary>그림 바이트를 비트맵으로. 그림이 아니면 null.</summary>
    private static Bitmap? DecodeImage(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>팔레트를 이 색들로 바꾼다. 덮어쓰기 확인에서 취소하면 false.</summary>
    private bool UsePalette(LospecPalette p)
    {
        int n = _current().Count;
        if (n > 0 && Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_UsePalette_61DD05834E,n,p.Title,p.Colors.Count),
                UiStrings.S_LospecPaletteDialog_UsePalette_D1481F9E4E, MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return false;
        _replace(p.Colors);
        Note(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_UsePalette_DDD1C3B449,p.Title));
        return true;
    }

    private void AppendPalette(LospecPalette p)
    {
        int added = _append(p.Colors);
        Note(added > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_AppendPalette_C42B6B0D5F,added) : UiStrings.S_LospecPaletteDialog_AppendPalette_0829A17CC4);
    }

    private void SavePalette(LospecPalette p)
    {
        var path = Ui.SaveFile(this, UiStrings.S_LospecPaletteDialog_SavePalette_E26650B496, null, p.Slug + ".hex", "hex", new FileFilter(UiStrings.S_LospecPaletteDialog_SavePalette_8D2EB5880E, "*.hex"));
        if (path is null) return;
        try
        {
            PaletteIO.SaveHex(path, p.Colors);
            Note(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_SavePalette_5FC897CDCA,Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_SavePalette_4C07BA89FD,ex.Message));
        }
    }

    private void OpenWeb(string url)
    {
        try
        {
            AiCli.OpenUrl(url);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LospecPaletteDialog_OpenWeb_335A7C88C9,url,ex.Message));
        }
    }

    // ───────────── 내부 컨트롤 ─────────────

    /// <summary>목록 한 줄의 색 칸: 고른 팔레트 칸과 같은 크기(14px + 2px 간격)로 늘어놓고 너비가 모자라면 줄을 바꾼다.</summary>
    private sealed class SwatchStrip(IReadOnlyList<Color32> colors) : Control
    {
        private const double Cell = 14, GapPx = 2;

        private static int Columns(double width) => Math.Max(1, (int)((width + GapPx) / (Cell + GapPx)));

        protected override Avalonia.Size MeasureOverride(Avalonia.Size available)
        {
            double width = double.IsInfinity(available.Width) ? colors.Count * (Cell + GapPx) : available.Width;
            int rows = Math.Max(1, (colors.Count + Columns(width) - 1) / Columns(width));
            return new Avalonia.Size(Math.Min(width, colors.Count * (Cell + GapPx)), rows * (Cell + GapPx) - GapPx);
        }

        public override void Render(DrawingContext g)
        {
            int cols = Columns(Bounds.Width);
            var border = AppTheme.Pen(AppTheme.BorderColor);
            for (int i = 0; i < colors.Count; i++)
            {
                var r = new Rect(i % cols * (Cell + GapPx), i / cols * (Cell + GapPx), Cell, Cell);
                ImageUtil.DrawSwatch(g, r, colors[i]);
                g.DrawRectangle(null, border, r.Deflate(0.5));
            }
        }
    }

    /// <summary>고른 팔레트의 큰 색 칸(너비에 맞춰 줄바꿈). 마우스를 올린 색은 풍선 도움말과 <see cref="HoverChanged"/>로 알린다.</summary>
    private sealed class BigSwatches : Control
    {
        /// <summary>편집기 팔레트 칸과 같은 크기(14px + 2px 간격).</summary>
        private const double Cell = 14, GapPx = 2;
        /// <summary>한 줄 12칸 너비(편집기 팔레트 열과 비슷하게).</summary>
        public const double PreferredWidth = GapPx + 12 * (Cell + GapPx) + 14;
        private IReadOnlyList<Color32> _colors = [];
        private int _hover = -1;

        public event EventHandler<Color32?>? HoverChanged;

        public void SetColors(IReadOnlyList<Color32> colors)
        {
            _colors = colors;
            _hover = -1;
            ToolTip.SetTip(this, null);
            InvalidateMeasure();
            InvalidateVisual();
        }

        private int Columns(double width) => Math.Max(1, (int)((width - GapPx) / (Cell + GapPx)));

        protected override Avalonia.Size MeasureOverride(Avalonia.Size available)
        {
            double width = double.IsInfinity(available.Width) ? 600 : available.Width;
            int rows = (_colors.Count + Columns(width) - 1) / Columns(width);
            return new Avalonia.Size(width, Math.Max(Cell + 2 * GapPx, rows * (Cell + GapPx) + GapPx));
        }

        private Rect CellRect(int i, int cols)
            => new(GapPx + i % cols * (Cell + GapPx), GapPx + i / cols * (Cell + GapPx), Cell, Cell);

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.InputBrush, new Rect(Bounds.Size));
            int cols = Columns(Bounds.Width);
            for (int i = 0; i < _colors.Count; i++)
            {
                var r = CellRect(i, cols);
                ImageUtil.DrawSwatch(g, r, _colors[i]);
                g.DrawRectangle(null, i == _hover ? AppTheme.Pen(Colors.White, 2) : AppTheme.Pen(AppTheme.BorderColor), r.Deflate(i == _hover ? 1 : 0.5));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);
            int cols = Columns(Bounds.Width);
            int col = (int)((pos.X - GapPx) / (Cell + GapPx)), row = (int)((pos.Y - GapPx) / (Cell + GapPx));
            int i = pos.X >= GapPx && pos.Y >= GapPx && col < cols ? row * cols + col : -1;
            if (i >= _colors.Count || (i >= 0 && !CellRect(i, cols).Contains(pos))) i = -1;
            SetHover(i);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            SetHover(-1);
        }

        private void SetHover(int i)
        {
            if (i == _hover) return;
            _hover = i;
            Color32? c = i >= 0 ? _colors[i] : null;
            ToolTip.SetTip(this, c?.ToHex());
            HoverChanged?.Invoke(this, c);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 예시 그림 띠: 너비를 장 수만큼 똑같이 나눠 장마다 그림 가운데를 보여 준다(픽셀 그대로 정수 배율). 나눈 칸은 마우스를 올려도 그대로이고,
    /// 칸에 마우스를 올리면 그 그림이 자기 칸에서 띠 전체로 부드럽게 커져 크게 보인다(다른 칸으로 옮기면 그 그림으로 바뀜, 나가면 다시 작아짐).
    /// 아래 작은 막대가 지금 보이는 칸을 표시한다.
    /// </summary>
    private sealed class ExampleStrip : Control
    {
        private const double GapPx = 2, Ease = 0.25, IndicatorH = 3;
        private readonly List<(Bitmap Bitmap, string Tip)> _items = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
        /// <summary>마우스가 올라간 칸(-1 = 없음).</summary>
        private int _hover = -1;
        /// <summary>크게 보이는 그림(작아지는 동안에도 남아 있음).</summary>
        private int _shown = -1;
        /// <summary>크게 보이기 진행(0 = 자기 칸, 1 = 띠 전체).</summary>
        private double _t;

        public ExampleStrip()
        {
            ClipToBounds = true;
            _timer.Tick += (_, _) => Step();
        }

        public void Clear()
        {
            _items.Clear();
            _hover = _shown = -1;
            _t = 0;
            _timer.Stop();
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }

        public void Add(Bitmap bitmap, string tip)
        {
            _items.Add((bitmap, tip));
            InvalidateVisual();
        }

        /// <summary>칸 <paramref name="i"/>(장 수만큼 똑같이 나눈 고정 칸).</summary>
        private Rect Zone(int i)
        {
            int n = Math.Max(1, _items.Count);
            double w = (Bounds.Width - GapPx * (n - 1)) / n;
            return new Rect(Math.Round(i * (w + GapPx)), 0, Math.Max(0, Math.Round(w)), Bounds.Height);
        }

        /// <summary>그림이 <paramref name="area"/>에 들어가는 배율(키울 수 있으면 정수 배).</summary>
        private static double FitScale(Bitmap b, Rect area)
        {
            double s = Math.Min(Math.Max(1, area.Width - 8) / Math.Max(1, b.PixelSize.Width), Math.Max(1, area.Height - 8) / Math.Max(1, b.PixelSize.Height));
            return s >= 1 ? Math.Floor(s) : s;
        }

        /// <summary>칸에 놓을 때 배율: 높이에 맞춤(가로는 잘림).</summary>
        private double StripScale(Bitmap b)
        {
            double h = Math.Max(1, b.PixelSize.Height), max = Math.Max(1, Bounds.Height - 8);
            return h <= max ? Math.Floor(max / h) : max / h;
        }

        private void Step()
        {
            double target = _hover >= 0 ? 1 : 0;
            _t += (target - _t) * Ease;
            if (Math.Abs(target - _t) < 0.01)
            {
                _t = target;
                if (_t == 0) _shown = -1;
                _timer.Stop();
            }
            InvalidateVisual();
        }

        private static void DrawImage(DrawingContext g, Bitmap bmp, Rect area, double scale)
        {
            double w = bmp.PixelSize.Width * scale, h = bmp.PixelSize.Height * scale;
            var dest = new Rect(Math.Round(area.X + (area.Width - w) / 2), Math.Round(area.Y + (area.Height - h) / 2), w, h);
            using (g.PushClip(area))
            using (ImageUtil.Pixelated(g))
                g.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest);
        }

        public override void Render(DrawingContext g)
        {
            int n = _items.Count;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
                DrawImage(g, _items[i].Bitmap, Zone(i), StripScale(_items[i].Bitmap));
            if (_shown >= 0 && _t > 0)
            {
                // 자기 칸 → 띠 전체로 커지는 판(뒤 칸들은 판이 덮음).
                var from = Zone(_shown);
                var full = new Rect(Bounds.Size);
                double e = 1 - Math.Pow(1 - _t, 3);
                var panel = new Rect(from.X + (full.X - from.X) * e, 0, from.Width + (full.Width - from.Width) * e, full.Height);
                g.FillRectangle(AppTheme.InputBrush, panel);
                var bmp = _items[_shown].Bitmap;
                // 배율도 칸 배율 → 전체 맞춤 배율로 함께 바뀐다(끝에서 정수 배율로 딱 맞음).
                double s0 = StripScale(bmp), s1 = FitScale(bmp, full);
                DrawImage(g, bmp, panel, _t >= 1 ? s1 : s0 + (s1 - s0) * e);
                g.DrawRectangle(null, AppTheme.Pen(AppTheme.AccentColor, 2), panel.Deflate(1));
            }
            if (n > 1 && (_hover >= 0 || _t > 0))
            {
                // 아래 칸 표시: 고정 칸마다 짧은 막대, 지금 보이는 칸은 강조색.
                for (int i = 0; i < n; i++)
                {
                    var z = Zone(i);
                    var bar = new Rect(z.X + 4, Bounds.Height - IndicatorH - 3, Math.Max(0, z.Width - 8), IndicatorH);
                    g.FillRectangle(i == _shown ? AppTheme.AccentBrush : AppTheme.Brush(Color.FromArgb(150, 255, 255, 255)), bar);
                }
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            double x = e.GetPosition(this).X;
            int hover = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                var z = Zone(i);
                if (x >= z.X && x < z.Right + GapPx) { hover = i; break; }
            }
            SetHover(hover);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            SetHover(-1);
        }

        private void SetHover(int i)
        {
            if (i == _hover) return;
            _hover = i;
            if (i >= 0) _shown = i; // 커진 상태에서 다른 칸으로 옮기면 바로 그 그림으로
            ToolTip.SetTip(this, i >= 0 ? _items[i].Tip : null);
            _timer.Start();
            InvalidateVisual();
        }
    }
}
