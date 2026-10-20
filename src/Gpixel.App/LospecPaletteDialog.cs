using System.ComponentModel;
using System.Globalization;
using Gpixel.App.Ai;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Avalonia.VisualTree;

namespace Gpixel.App;

/// <summary>
/// lospec.com 팔레트 목록 둘러보기(모달 아님). 정렬·색 수·태그로 거르고, 고른 팔레트를 현재 팔레트로 쓰거나 덧붙이거나
/// .hex로 저장한다. 태그 칸의 글자는 팔레트 이름(슬러그)으로도 찾아 맞으면 맨 위에 고정해 보여 준다.
/// 네트워크는 모두 비동기이고, 조건이 바뀌거나 창을 닫으면 진행 중인 요청을 취소한다.
/// </summary>
public sealed class LospecPaletteDialog : DialogBase
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

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
        Func<IReadOnlyList<Color32>, int> append) : base("팔레트 웹 — Lospec")
    {
        _current = current;
        _replace = replace;
        _append = append;
        CloseOnly();
        CancelButtonControl.IsDefault = false; // Enter는 태그 검색에 쓴다.
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 720;
        Height = 600;
        MinWidth = 520;
        MinHeight = 440;

        // ── 거르기 줄
        _sort = Ui.Combo(100, "인기", "다운로드순", "최신", "이름순");
        _filter = Ui.Combo(84, "전체", "최대", "최소", "정확히");
        _count = Ui.Num(1, 256, 8, 64);
        _count.IsEnabled = false;
        _tag = Ui.Text("", 150);
        _tag.Watermark = "태그 또는 이름";
        ToolTip.SetTip(_tag, "태그(예: gameboy, pico8)로 거릅니다. 팔레트 이름(예: sweetie 16)이 맞으면 맨 위에 보여 줍니다.");
        _tag.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Search(); } };
        _sort.SelectionChanged += (_, _) => Search();
        _filter.SelectionChanged += (_, _) => { _count.IsEnabled = _filter.SelectedIndex > 0; Search(); };
        _count.ValueChanged += (_, _) => { if (_filter.SelectedIndex > 0) { _debounce.Stop(); _debounce.Start(); } };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Search(); };
        var search = Ui.Button("검색", (_, _) => Search(), 60);
        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new Control[] { Ui.Label("정렬"), _sort, Gap(), Ui.Label("색 수"), _filter, _count, Gap(), Ui.Label("태그"), _tag, search })
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            filters.Children.Add(c);
        }

        // ── 상태 줄
        _status = new TextBlock { Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _retry = Ui.Button("다시 시도", (_, _) => Retry());
        _retry.IsVisible = false;
        var statusRow = new DockPanel { Margin = new Thickness(2, 2, 2, 4) };
        DockPanel.SetDock(_retry, Dock.Right);
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
            if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { Tag: LospecPalette p }) UsePalette(p);
        };
        // 끝 가까이 스크롤하면 다음 쪽을 불러온다.
        _list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            if (e.Source is ScrollViewer sv && sv.Extent.Height > 0 && sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 60) LoadMoreIfIdle();
        });
        _more = Ui.Button("더 보기", (_, _) => LoadMoreIfIdle(), 90);
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
        _use = Ui.Button("팔레트로 쓰기", (_, _) => { if (Selected is { } p) UsePalette(p); });
        _add = Ui.Button("현재 팔레트에 추가", (_, _) => { if (Selected is { } p) AppendPalette(p); });
        _save = Ui.Button(".hex로 저장…", (_, _) => { if (Selected is { } p) SavePalette(p); });
        _web = Ui.Button("Lospec에서 보기", (_, _) => { if (Selected is { } p) OpenWeb(p.WebUrl); });
        _note = new TextBlock { Foreground = AppTheme.AccentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new Control[] { _use, _add, _save, _web, _note }) actions.Children.Add(b);
        var hexRow = new DockPanel();
        DockPanel.SetDock(_hoverHex, Dock.Right);
        hexRow.Children.Add(_hoverHex);
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
            Text = "팔레트 출처: Lospec.com (각 팔레트 작성자)",
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
        Opened += (_, _) => { _tag.Focus(); Search(); };
    }

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
        _examples.Clear();
        foreach (var bmp in _exampleCache.Values) bmp?.Dispose();
        _exampleCache.Clear();
        base.OnClosed(e);
    }

    // ───────────── 불러오기 ─────────────

    private LospecQuery ReadQuery() => new(
        (LospecSort)Math.Max(0, _sort.SelectedIndex),
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
        ShowDetail(null);
        var token = _cts.Token;
        _ = LoadPageAsync(token);
        if (LospecPalettes.ToSlug(_query.Tag) is { Length: > 0 } slug) _ = LoadPinnedAsync(slug, token);
    }

    private void Retry()
    {
        if (_nextPage == 0) Search();
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
        SetStatus(_nextPage == 0 ? "불러오는 중…" : $"{_loaded} / {_total} · 더 불러오는 중…", error: false);
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
            SetStatus(ex switch
            {
                OperationCanceledException => $"Lospec이 {RequestTimeout.TotalSeconds:0}초 안에 응답하지 않았습니다. 잠시 뒤 다시 시도하세요.",
                FormatException => "Lospec 응답을 읽지 못했습니다. 사이트 형식이 바뀌었을 수 있습니다.",
                _ => $"Lospec에 연결하지 못했습니다. 인터넷 연결을 확인하세요.\n({ex.Message})",
            }, error: true);
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
            SetStatus(_query.Tag.Length > 0 ? $"'{_query.Tag}' 태그나 이름에 맞는 팔레트가 없습니다." : "조건에 맞는 팔레트가 없습니다.", error: false);
        else if (_loaded == 0)
            SetStatus($"'{_query.Tag}' 태그는 없고, 이름이 맞는 팔레트(★)를 찾았습니다.", error: false);
        else
            SetStatus($"{_loaded} / {_total}" + (_pinned is not null ? " · ★ 이름이 맞는 팔레트를 맨 위에 두었습니다" : ""), error: false);
    }

    /// <summary>단추를 누른 결과(팔레트로 씀·덧붙임·저장)를 단추 옆에 알린다.</summary>
    private void Note(string text) => _note.Text = text;

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = error ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
        _retry.IsVisible = error;
    }

    private static ListBoxItem MakeItem(LospecPalette p, bool pinned)
    {
        var title = new TextBlock
        {
            Text = (pinned ? "★ " : "") + p.Title,
            FontWeight = FontWeight.Bold,
            Foreground = AppTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var info = new TextBlock
        {
            Text = Info(p),
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var head = new DockPanel();
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        head.Children.Add(info);
        var strip = new SwatchStrip(p.Colors) { Height = 16, Margin = new Thickness(0, 3, 0, 0) };
        var item = new ListBoxItem
        {
            Content = new StackPanel { Children = { head, strip } },
            Tag = p,
            Padding = new Thickness(6, 4),
        };
        ToolTip.SetTip(item, p.Tags.Count > 0 ? "태그: " + string.Join(", ", p.Tags) : null);
        return item;
    }

    private static string Info(LospecPalette p)
    {
        var parts = new List<string>();
        if (p.Author.Length > 0) parts.Add(p.Author);
        parts.Add($"{p.Colors.Count}색");
        if (p.Likes > 0) parts.Add($"♥ {p.Likes.ToString("N0", CultureInfo.InvariantCulture)}");
        if (p.Downloads > 0) parts.Add($"↓ {p.Downloads.ToString("N0", CultureInfo.InvariantCulture)}");
        return string.Join(" · ", parts);
    }

    // ───────────── 고른 팔레트 ─────────────

    private void ShowDetail(LospecPalette? p)
    {
        _detailTitle.Text = p?.Title ?? "팔레트를 고르세요";
        _detailInfo.Text = p is null ? "목록을 두 번 누르면 바로 팔레트로 씁니다." : Info(p) + (p.Tags.Count > 0 ? " · " + string.Join(", ", p.Tags) : "");
        _swatches.SetColors(p?.Colors ?? []);
        ShowExamples(p);
        _hoverHex.Text = "";
        _note.Text = "";
        foreach (var b in new[] { _use, _add, _save, _web }) b.IsEnabled = p is not null;
    }

    /// <summary>고른 팔레트의 예시 그림을 오른쪽에 내려받아 보인다(픽셀이 그대로 보이게 정수 배율, 고르는 팔레트가 바뀌면 받던 것은 취소).</summary>
    private async void ShowExamples(LospecPalette? p)
    {
        _examplesCts?.Cancel();
        _examples.Clear();
        if (p is null || p.Examples.Count == 0)
        {
            _examplesNote.Text = p is null ? "" : "이 팔레트에는 예시 그림이 없습니다.";
            return;
        }
        var cts = _examplesCts = new CancellationTokenSource();
        _examplesNote.Text = "예시 그림 불러오는 중…";
        int shown = 0;
        foreach (var url in p.Examples.Take(6))
        {
            if (!_exampleCache.TryGetValue(url, out var bmp))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var bytes = await LospecPalettes.FetchImageAsync(Http, url, timeout.Token);
                    using var ms = new MemoryStream(bytes);
                    bmp = new Bitmap(ms);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException
                                               or IOException or NotSupportedException)
                {
                    bmp = null;
                }
                _exampleCache[url] = bmp;
            }
            if (cts.IsCancellationRequested) return;
            if (bmp is null) continue;
            _examples.Add(bmp, $"{bmp.PixelSize.Width}×{bmp.PixelSize.Height}px · {Path.GetFileName(url.LocalPath)}");
            shown++;
        }
        _examplesNote.Text = shown == 0 ? "예시 그림을 불러오지 못했습니다." : "";
    }

    private void UsePalette(LospecPalette p)
    {
        int n = _current().Count;
        if (n > 0 && Ui.Ask(this, $"지금 팔레트({n}색)를 '{p.Title}'({p.Colors.Count}색)으로 바꿀까요?",
                "팔레트로 쓰기", MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _replace(p.Colors);
        Note($"'{p.Title}'을(를) 팔레트로 썼습니다.");
    }

    private void AppendPalette(LospecPalette p)
    {
        int added = _append(p.Colors);
        Note(added > 0 ? $"{added}색을 덧붙였습니다." : "덧붙일 새 색이 없습니다(모두 이미 있음).");
    }

    private void SavePalette(LospecPalette p)
    {
        var path = Ui.SaveFile(this, "팔레트 저장", null, p.Slug + ".hex", "hex", new FileFilter("HEX 팔레트", "*.hex"));
        if (path is null) return;
        try
        {
            PaletteIO.SaveHex(path, p.Colors);
            Note($"저장했습니다: {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"팔레트를 저장하지 못했습니다.\n{ex.Message}");
        }
    }

    private void OpenWeb(string url)
    {
        try
        {
            ClaudeCli.OpenUrl(url);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Ui.Error(this, $"브라우저를 열지 못했습니다.\n{url}\n{ex.Message}");
        }
    }

    // ───────────── 내부 컨트롤 ─────────────

    /// <summary>목록 한 줄의 색 띠: 모든 색을 너비에 맞춰 늘이거나 줄인다.</summary>
    private sealed class SwatchStrip(IReadOnlyList<Color32> colors) : Control
    {
        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            if (colors.Count == 0 || b.Width <= 0) return;
            double w = b.Width / colors.Count;
            for (int i = 0; i < colors.Count; i++)
            {
                // 칸 사이가 비지 않게 다음 칸 시작점까지 채운다(반올림해 경계를 픽셀에 맞춤).
                double x0 = Math.Round(i * w), x1 = Math.Round((i + 1) * w);
                ImageUtil.DrawSwatch(g, new Rect(x0, 0, Math.Max(1, x1 - x0), b.Height), colors[i]);
            }
            g.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), b.Deflate(0.5));
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
    /// 예시 그림 띠: 너비를 장 수만큼 똑같이 나눠 장마다 그림 가운데를 보여 주고(픽셀 그대로 정수 배율), 마우스를 올린 장이
    /// 부드럽게 넓어지며(나머지는 좁아짐) 그림 전체가 보인다. 마우스가 나가면 다시 똑같이 나뉜다.
    /// </summary>
    private sealed class ExampleStrip : Control
    {
        private const double GapPx = 2, Ease = 0.22;
        private readonly List<(Bitmap Bitmap, string Tip)> _items = new();
        private readonly List<double> _weights = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
        private int _hover = -1;

        public ExampleStrip()
        {
            ClipToBounds = true;
            _timer.Tick += (_, _) => Step();
        }

        public void Clear()
        {
            _items.Clear();
            _weights.Clear();
            _hover = -1;
            _timer.Stop();
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }

        public void Add(Bitmap bitmap, string tip)
        {
            _items.Add((bitmap, tip));
            _weights.Add(1);
            InvalidateVisual();
        }

        /// <summary>그림이 칸 높이에 들어가는 배율(작으면 정수 배로 키움).</summary>
        private double ScaleOf(Bitmap b)
        {
            double h = Math.Max(1, b.PixelSize.Height), max = Math.Max(1, Bounds.Height - 8);
            return h <= max ? Math.Floor(max / h) : max / h;
        }

        /// <summary>마우스를 올린 장의 목표 몫: 그 그림 전체 너비가 들어갈 만큼(나머지 장은 몫 1).</summary>
        private double HoverWeight(int i)
        {
            int n = _items.Count;
            if (n <= 1) return 1;
            double total = Bounds.Width - GapPx * (n - 1);
            double want = _items[i].Bitmap.PixelSize.Width * ScaleOf(_items[i].Bitmap) + 8;
            // 나머지 장은 최소 24px은 남긴다.
            double maxShare = Math.Max(1, total - 24 * (n - 1));
            double share = Math.Clamp(want, total / n, maxShare);
            // share / (share + (n-1)*rest) = weight / (weight + n - 1)
            double restEach = (total - share) / (n - 1);
            return restEach <= 0 ? 1 : share / restEach;
        }

        private void Step()
        {
            bool moving = false;
            for (int i = 0; i < _weights.Count; i++)
            {
                double target = i == _hover ? HoverWeight(i) : 1;
                double next = _weights[i] + (target - _weights[i]) * Ease;
                if (Math.Abs(target - next) < 0.01) next = target;
                else moving = true;
                _weights[i] = next;
            }
            if (!moving) _timer.Stop();
            InvalidateVisual();
        }

        private IEnumerable<(int Index, Rect Rect)> Strips()
        {
            int n = _items.Count;
            double total = Bounds.Width - GapPx * (n - 1), sum = _weights.Sum(), x = 0;
            for (int i = 0; i < n; i++)
            {
                double w = total * _weights[i] / sum;
                yield return (i, new Rect(x, 0, Math.Max(0, w), Bounds.Height));
                x += w + GapPx;
            }
        }

        public override void Render(DrawingContext g)
        {
            if (_items.Count == 0) return;
            foreach (var (i, r) in Strips())
            {
                var bmp = _items[i].Bitmap;
                double scale = ScaleOf(bmp);
                double w = bmp.PixelSize.Width * scale, h = bmp.PixelSize.Height * scale;
                // 칸 가운데에 그림 가운데를 맞춘다(좁으면 양옆이 잘림).
                var dest = new Rect(Math.Round(r.X + (r.Width - w) / 2), Math.Round((r.Height - h) / 2), w, h);
                using (g.PushClip(r))
                using (ImageUtil.Pixelated(g))
                    g.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest);
                if (i == _hover) g.DrawRectangle(null, AppTheme.Pen(AppTheme.AccentColor, 2), r.Deflate(1));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            double x = e.GetPosition(this).X;
            int hover = -1;
            foreach (var (i, r) in Strips())
                if (x >= r.X && x < r.Right + GapPx) { hover = i; break; }
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
            ToolTip.SetTip(this, i >= 0 ? _items[i].Tip : null);
            _timer.Start();
        }
    }
}
