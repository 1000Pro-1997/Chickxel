using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls.Documents;
using Gpixel.App.Localization;
using Gpixel.Core.Help;

namespace Gpixel.App;

/// <summary>
/// 사용 가이드 창(F1, 모드리스, 하나만). 홈페이지 사용 가이드(docs/guide.html·docs/en/guide.html — 원본은 docs/i18n)를
/// <c>tools/build-docs.py</c>(→ <c>tools/build-help.py</c>)로 미리 만든 쪽 데이터(내장 리소스 help.&lt;언어&gt;.json)로 보여 준다.
/// 왼쪽 = 검색 칸 + 목차(검색 중이면 결과 목록), 오른쪽 = 고른 쪽. 검색은 <see cref="HelpSearch"/>(로컬 BM25)이고
/// 검색 중에는 보이는 쪽의 찾은 말을 모두 노랗게 칠하고 첫 곳으로 스크롤한다. 마지막에 본 쪽은 앱을 켜 둔 동안만 기억한다.
/// </summary>
public sealed class HelpWindow : Window
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly FontFamily MonoFont = new("Consolas, Menlo, DejaVu Sans Mono, monospace");
    // 홈페이지(style.css)와 같은 색: 금색 제목, 노란 강조, 파란 링크, 노란 빛 알림 상자.
    private static readonly IBrush Gold = AppTheme.Brush(Color.FromRgb(0xE8, 0xB9, 0x3A));
    private static readonly IBrush Yellow = AppTheme.Brush(Color.FromRgb(0xFF, 0xD0, 0x2F));
    private static readonly IBrush OnYellow = AppTheme.Brush(Color.FromRgb(0x17, 0x14, 0x0A));
    private static readonly IBrush LinkBrush = AppTheme.Brush(Color.FromRgb(0x8A, 0xA0, 0xFF));
    private static readonly IBrush BodyBrush = AppTheme.Brush(Color.FromRgb(0xC9, 0xCC, 0xD4));
    private static readonly IBrush SoftBrush = AppTheme.Brush(Color.FromRgb(0x24, 0x25, 0x2B));
    private static readonly IBrush CodeBrush = AppTheme.Brush(Color.FromRgb(0x3A, 0x3B, 0x44));
    private static readonly IBrush NoteBack = AppTheme.Brush(Color.FromArgb(0x1E, 0xFF, 0xD0, 0x2F));
    private static readonly IBrush NoteLine = AppTheme.Brush(Color.FromArgb(0x50, 0xFF, 0xD0, 0x2F));
    private static readonly IBrush NoteText = AppTheme.Brush(Color.FromRgb(0xEF, 0xDF, 0xAE));
    private static readonly IBrush SelectedBack = AppTheme.Brush(Color.FromArgb(0x26, 0xFF, 0xD0, 0x2F));
    private const double BodySize = 13.5, BodyLineHeight = 22;

    private static HelpWindow? _instance;
    /// <summary>마지막에 본 쪽(앱을 켜 둔 동안만).</summary>
    private static string? _lastPageId;

    private readonly bool _korean;
    private readonly HelpBook _book;
    private readonly List<HelpPage> _pages = new();
    private readonly Dictionary<string, HelpPage> _byId = new();
    private readonly HelpSearch _search;

    private readonly TextBox _query;
    private readonly Button _clear;
    private readonly ScrollViewer _sideScroll;
    private readonly StackPanel _toc = new() { Margin = new Thickness(8, 4, 8, 12) };
    private readonly StackPanel _results = new() { Margin = new Thickness(8, 4, 8, 12), Spacing = 2 };
    private readonly ScrollViewer _pageScroll;
    /// <summary>쪽 내용(창이 넓으면 읽기 좋은 폭으로 가운데).</summary>
    private readonly StackPanel _body = new() { Margin = new Thickness(32, 20, 32, 28), MaxWidth = 800 };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly HashSet<string> _expanded = new();

    private HelpPage _page;
    private IReadOnlyList<HelpSearchResult> _found = [];
    private int _selected = -1;
    /// <summary>지금 쪽에서 칠할 말(검색 중이 아니면 비어 있음).</summary>
    private IReadOnlyList<string> _terms = [];
    /// <summary>지금 쪽을 그리며 처음 칠한 글 줄(그리로 스크롤).</summary>
    private Control? _firstHit;
    private Control? _currentTocRow;

    /// <summary>
    /// 앱 명령 id(예: "편집/실행취소")의 지금 단축키(사용자가 바꾼 것 우선, 지웠으면 빈 목록, 모르는 명령이면 null).
    /// 메인 창이 정하고, 가이드의 명령 단축키 칸을 이것으로 그린다.
    /// </summary>
    public static Func<string, IReadOnlyList<KeyChord>?>? Shortcuts { get; set; }

    /// <summary>단축키가 바뀌었으면 열려 있는 가이드의 지금 쪽을 다시 그린다.</summary>
    public static void RefreshShortcuts() => _instance?.RenderPage();

    /// <summary>가이드 창을 연다(이미 열려 있으면 앞으로).</summary>
    public static HelpWindow Open(Window? owner)
    {
        if (_instance is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w._query.Focus();
            return w;
        }
        w = new HelpWindow();
        _instance = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_instance, w)) _instance = null; };
        if (owner is null)
        {
            w.Show();
            return w;
        }
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.Show(owner);
        return w;
    }

    private HelpWindow()
    {
        _korean = UiLanguage.Current == UiLanguage.Korean;
        _book = LoadBook(_korean ? "ko" : "en");
        foreach (var chapter in _book.Groups.SelectMany(g => g.Chapters))
        {
            foreach (var page in chapter.Pages)
            {
                page.Chapter = chapter;
                page.Index = _pages.Count;
                _pages.Add(page);
                _byId[page.Id] = page;
            }
            // 장 id(#장 링크·ShowPage)는 그 장의 첫 쪽으로 연다.
            _byId.TryAdd(chapter.Id, chapter.Pages[0]);
        }
        _search = new HelpSearch(_pages.Select(p => new HelpDocument(p.Id, p.Title, p.Text)));
        _page = _lastPageId is { } last && _byId.TryGetValue(last, out var lp) ? lp : _pages[0];

        Title = UiStrings.Help_Title;
        Width = 1000;
        Height = 700;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = AppTheme.BackBrush;

        // 왼쪽: 검색 칸 + 목차/결과
        _clear = Ui.IconButton(VectorIcon.X, UiStrings.Help_ClearSearch, (_, _) => { _query!.Text = ""; _query.Focus(); }, size: 12);
        _clear.Background = Brushes.Transparent;
        _clear.BorderThickness = new Thickness(0);
        _clear.Padding = new Thickness(4, 2);
        _clear.Margin = new Thickness(0, 0, 2, 0);
        _clear.IsVisible = false;
        _query = new TextBox
        {
            Watermark = UiStrings.Help_SearchWatermark,
            InnerRightContent = _clear,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 10, 10, 6),
        };
        _query.TextChanged += (_, _) =>
        {
            _clear.IsVisible = !string.IsNullOrEmpty(_query.Text);
            _debounce.Stop();
            _debounce.Start();
        };
        _query.AddHandler(KeyDownEvent, OnQueryKey, RoutingStrategies.Tunnel);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RunSearch();
        };
        _sideScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _toc,
        };
        var side = new DockPanel { Background = AppTheme.PanelBrush };
        DockPanel.SetDock(_query, Dock.Top);
        side.Children.Add(_query);
        side.Children.Add(_sideScroll);

        // 오른쪽: 위 막대(홈페이지에서 열기) + 쪽
        var website = Ui.Button(VectorIcon.ExternalLink, UiStrings.Help_OpenWebsite, (_, _) => Updater.OpenUrl(this, PageUrl(_page.Id)));
        website.Tip(UiStrings.Help_OpenWebsite);
        var support = Ui.Button(VectorIcon.Heart, UiStrings.Help_Support, (_, _) => Updater.OpenUrl(this, AppInfo.SupportUrl));
        support.Tip(UiStrings.Help_Support);
        var header = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { support, website } },
        };
        _pageScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _body,
        };
        var main = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        main.Children.Add(header);
        main.Children.Add(_pageScroll);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("280,Auto,*"), Background = AppTheme.BackBrush };
        var splitter = new GridSplitter { Width = 4, Background = AppTheme.BorderBrush, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(side, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(main, 2);
        root.Children.Add(side);
        root.Children.Add(splitter);
        root.Children.Add(main);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            if (!string.IsNullOrEmpty(_query.Text)) _query.Text = "";
            else Close();
        };
        Opened += (_, _) => _query.Focus();
        Closed += (_, _) => _debounce.Stop();

        Show(_page);
    }

    internal static HelpBook LoadBook(string lang)
    {
        var name = $"Gpixel.Help.help.{lang}.json";
        using var stream = typeof(HelpWindow).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name);
        return JsonSerializer.Deserialize<HelpBook>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException(name);
    }

    private string PageUrl(string pageId) => $"{AppInfo.WebsiteUrl}{(_korean ? "" : "en/")}guide.html#{pageId}";

    // ───────────── 검색 ─────────────

    /// <summary>검색 칸의 글자.</summary>
    private string Query => _query.Text ?? "";

    /// <summary>검색 칸 글자로 결과 목록과 지금 쪽의 칠하기를 다시 만든다(입력 뒤 150ms에 저절로 불림).</summary>
    private void RunSearch()
    {
        var q = Query;
        _found = _search.Search(q);
        _selected = -1;
        if (string.IsNullOrWhiteSpace(q))
        {
            _sideScroll.Content = _toc;
            RenderPage();
            return;
        }
        _sideScroll.Content = _results;
        _sideScroll.Offset = default;
        RenderResults();
        RenderPage();
    }

    private void OnQueryKey(object? sender, KeyEventArgs e)
    {
        if (_found.Count == 0 || string.IsNullOrWhiteSpace(Query)) return;
        switch (e.Key)
        {
            case Key.Down:
                Select(Math.Min(_found.Count - 1, _selected + 1));
                e.Handled = true;
                break;
            case Key.Up:
                Select(Math.Max(0, _selected - 1));
                e.Handled = true;
                break;
            case Key.Enter:
                Select(_selected < 0 ? 0 : _selected);
                e.Handled = true;
                break;
        }
    }

    private void Select(int index)
    {
        _selected = index;
        RenderResults();
        Show(_byId[_found[index].PageId]);
    }

    private void RenderResults()
    {
        _results.Children.Clear();
        if (_found.Count == 0)
        {
            _results.Children.Add(new TextBlock { Text = UiStrings.Help_NoResults, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(6, 8) });
            return;
        }
        var words = HelpSearch.HighlightTerms(Query);
        Control? selectedRow = null;
        for (int i = 0; i < _found.Count; i++)
        {
            var r = _found[i];
            var page = _byId[r.PageId];
            var terms = HelpSearch.ResolveTerms(page.Title + "\n" + page.Text, words);
            var title = new TextBlock { FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap };
            AddHighlighted(NewInlines(title), page.Title, terms, null);
            var chapter = new TextBlock { Text = page.Chapter.Title, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextTrimming = TextTrimming.CharacterEllipsis };
            var snippet = new TextBlock { Foreground = BodyBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            AddHighlighted(NewInlines(snippet), r.Snippet, terms, null);
            var index = i;
            var row = ClickRow(new StackPanel { Children = { title, chapter, snippet } }, i == _selected || (_selected < 0 && page == _page), () => Select(index), new Thickness(8, 6));
            if (i == _selected) selectedRow = row;
            _results.Children.Add(row);
        }
        if (selectedRow is not null) Dispatcher.UIThread.Post(() => selectedRow.BringIntoView(), DispatcherPriority.Loaded);
    }

    // ───────────── 목차 ─────────────

    private void RenderToc()
    {
        _toc.Children.Clear();
        _currentTocRow = null;
        bool first = true;
        foreach (var group in _book.Groups)
        {
            if (group.Label.Length > 0)
            {
                _toc.Children.Add(new TextBlock
                {
                    Text = group.Label,
                    Foreground = Gold,
                    FontSize = AppTheme.SmallFontSize,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(8, first ? 4 : 16, 0, 6),
                });
            }
            first = false;
            foreach (var chapter in group.Chapters) AddChapterRows(chapter);
        }
        if (_currentTocRow is { } current) Dispatcher.UIThread.Post(() => current.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void AddChapterRows(HelpChapter chapter)
    {
        bool mine = _page.Chapter == chapter;
        bool open = _expanded.Contains(chapter.Id);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*") };
        if (chapter.HasSubPages)
        {
            var chevron = new Border
            {
                Background = Brushes.Transparent,
                Cursor = HandCursor,
                Child = new IconView(open ? VectorIcon.ChevronDown : VectorIcon.ChevronRight, AppTheme.DimTextBrush) { Width = 13, Height = 13 },
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            }.Tip(UiStrings.Help_ToggleChapter);
            chevron.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (!_expanded.Remove(chapter.Id)) _expanded.Add(chapter.Id);
                RenderToc();
            };
            grid.Children.Add(chevron);
        }
        var title = new TextBlock
        {
            Text = chapter.Title,
            Foreground = mine ? AppTheme.TextBrush : BodyBrush,
            FontWeight = mine ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        // 장 이름: 첫 쪽을 열고 펼친다. 세부 쪽을 보는 동안에도 장 줄을 칠해 둔다.
        var first = chapter.Pages[0];
        var row = ClickRow(grid, mine, () => Show(first), new Thickness(4, 4, 8, 4));
        if (mine && !chapter.HasSubPages) _currentTocRow = row;
        _toc.Children.Add(row);
        if (!open || !chapter.HasSubPages) return;
        foreach (var page in chapter.Pages)
        {
            var p = page;
            var text = new TextBlock
            {
                Text = page.Title,
                Foreground = page == _page ? Yellow : AppTheme.DimTextBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            var sub = ClickRow(text, page == _page, () => Show(p), new Thickness(28, 3, 8, 3));
            if (page == _page) _currentTocRow = sub;
            _toc.Children.Add(sub);
        }
    }

    /// <summary>누르면 <paramref name="click"/>하는 목록 줄(올리면 밝게, <paramref name="current"/>면 노란 빛 바탕).</summary>
    private static Border ClickRow(Control content, bool current, Action click, Thickness padding)
    {
        var idle = current ? SelectedBack : Brushes.Transparent;
        var row = new Border
        {
            Child = content,
            Padding = padding,
            CornerRadius = new CornerRadius(5),
            Background = idle,
            Cursor = HandCursor,
        };
        row.PointerEntered += (_, _) => { if (!current) row.Background = AppTheme.HoverBrush; };
        row.PointerExited += (_, _) => row.Background = idle;
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            click();
        };
        return row;
    }

    // ───────────── 쪽 ─────────────

    /// <summary>쪽을 연다(모르는 id면 그대로).</summary>
    public void ShowPage(string pageId)
    {
        if (_byId.TryGetValue(pageId, out var page)) Show(page);
    }

    private void Show(HelpPage page)
    {
        _page = page;
        _lastPageId = page.Id;
        _expanded.Clear();
        _expanded.Add(page.Chapter.Id);
        RenderToc();
        RenderPage();
    }

    private void RenderPage()
    {
        var page = _page;
        _terms = string.IsNullOrWhiteSpace(Query) ? [] : HelpSearch.ResolveTerms(page.Title + "\n" + page.Text, HelpSearch.HighlightTerms(Query));
        _firstHit = null;
        _body.Children.Clear();

        if (page.Chapter.HasSubPages)
        {
            var crumb = Text(new[] { new HelpRun { T = page.Chapter.Title } }, Gold, AppTheme.SmallFontSize, bold: true);
            crumb.Cursor = HandCursor;
            crumb.PointerPressed += (_, e) => { e.Handled = true; Show(page.Chapter.Pages[0]); };
            crumb.Margin = new Thickness(0, 0, 0, 4);
            _body.Children.Add(crumb);
        }
        // 장 머리말 글은 첫 세부 쪽 맨 위에(홈페이지와 같음).
        if (page.Intro is { Length: > 0 } intro)
        {
            var stack = new StackPanel();
            foreach (var block in intro) stack.Children.Add(Block(block, BodyBrush));
            _body.Children.Add(new Border
            {
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 10),
                Margin = new Thickness(0, 0, 0, 18),
                Child = stack,
            });
        }
        var title = Text(new[] { new HelpRun { T = page.Title } }, AppTheme.TextBrush, 24, bold: true);
        title.Margin = new Thickness(0, 0, 0, 14);
        _body.Children.Add(title);

        foreach (var block in page.Blocks) _body.Children.Add(Block(block, BodyBrush));

        _body.Children.Add(Pager(page));

        var hit = _firstHit;
        Dispatcher.UIThread.Post(() =>
        {
            if (hit is not null && hit.TranslatePoint(default, _body) is { } at)
                _pageScroll.Offset = new Vector(0, Math.Max(0, at.Y + _body.Margin.Top - 60));
            else
                _pageScroll.Offset = default;
        }, DispatcherPriority.Loaded);
    }

    private Control Block(HelpBlock b, IBrush fg)
    {
        switch (b.Type)
        {
            case "p":
            {
                var t = Text(b.Runs ?? [], fg);
                t.Margin = new Thickness(0, 0, 0, 10);
                return t;
            }
            case "h4":
            {
                var t = Text(b.Runs ?? [], AppTheme.TextBrush, 15.5, bold: true);
                t.Margin = new Thickness(0, 10, 0, 6);
                return t;
            }
            case "ul":
            case "ol":
                return ListBlock(b, fg);
            case "steps":
                return Steps(b);
            case "table":
                return Table(b);
            case "note":
            {
                var stack = new StackPanel();
                foreach (var inner in b.Blocks ?? []) stack.Children.Add(Block(inner, NoteText));
                if (stack.Children.LastOrDefault() is Control last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
                return new Border
                {
                    Background = NoteBack,
                    BorderBrush = NoteLine,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12),
                    Margin = new Thickness(0, 4, 0, 14),
                    Child = stack,
                };
            }
            case "pre":
            {
                var t = new SelectableTextBlock
                {
                    FontFamily = MonoFont,
                    FontSize = 12,
                    Foreground = AppTheme.TextBrush,
                    TextWrapping = TextWrapping.Wrap,
                };
                AddHighlighted(NewInlines(t), b.Text ?? "", _terms, t);
                return new Border
                {
                    Background = SoftBrush,
                    BorderBrush = AppTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10),
                    Margin = new Thickness(0, 0, 0, 12),
                    Child = t,
                };
            }
            case "media":
                return Media(b);
            case "shot":
                return Shot(b);
            default:
                return new Control();
        }
    }

    private Control ListBlock(HelpBlock b, IBrush fg)
    {
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };
        int n = 0;
        foreach (var item in b.Items ?? [])
        {
            n++;
            // 그림 번호(①…)로 시작하는 목록(marks)은 따로 머리표를 그리지 않는다.
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(b.Marks ? "0,*" : "22,*") };
            Control marker = b.Type == "ol"
                ? new TextBlock { Text = $"{n}.", Foreground = AppTheme.DimTextBrush, FontSize = BodySize, LineHeight = BodyLineHeight }
                : new IconView(VectorIcon.Dot, Gold) { Width = 8, Height = 8, Filled = true, Margin = new Thickness(4, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            if (!b.Marks) grid.Children.Add(marker);
            var content = new StackPanel();
            content.Children.Add(Text(item.Runs ?? [], fg));
            foreach (var sub in item.Sub ?? [])
            {
                var nested = ListBlock(sub, fg);
                nested.Margin = new Thickness(0, 4, 0, 0);
                content.Children.Add(nested);
            }
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            stack.Children.Add(grid);
        }
        return stack;
    }

    private Control Steps(HelpBlock b)
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 2, 0, 14) };
        int n = 0;
        foreach (var item in b.Items ?? [])
        {
            n++;
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = Yellow,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture), Foreground = OnYellow, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var title = Text(item.Title ?? [], AppTheme.TextBrush, 14.5, bold: true);
            var text = Text(item.Runs ?? [], BodyBrush);
            text.Margin = new Thickness(0, 2, 0, 0);
            var content = new StackPanel { Children = { title, text } };
            Grid.SetColumn(content, 1);
            stack.Children.Add(new Border
            {
                Background = SoftBrush,
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12),
                Child = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), Children = { badge, content } },
            });
        }
        return stack;
    }

    private Control Table(HelpBlock b)
    {
        var rows = new List<(HelpRun[][] Cells, bool Head)>();
        if (b.Head is { Length: > 0 } head) rows.Add((head, true));
        rows.AddRange((b.Rows ?? []).Select(r => (r, false)));
        int cols = rows.Count == 0 ? 0 : rows.Max(r => r.Cells.Length);
        var grid = new Grid();
        // 열 폭 = 그 열의 가장 긴 글자 수에 비례(너무 좁거나 넓지 않게).
        for (int c = 0; c < cols; c++)
        {
            int longest = rows.Max(r => c < r.Cells.Length ? r.Cells[c].Sum(x => x.T.Length) : 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Clamp(longest, 8, 48), GridUnitType.Star));
        }
        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < cols; c++)
            {
                var runs = c < rows[r].Cells.Length ? rows[r].Cells[c] : [];
                var cell = new Border
                {
                    BorderBrush = AppTheme.BorderBrush,
                    BorderThickness = new Thickness(0, 0, c < cols - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    Background = rows[r].Head ? SoftBrush : Brushes.Transparent,
                    Padding = new Thickness(10, 6),
                    Child = Text(runs, rows[r].Head ? AppTheme.TextBrush : BodyBrush, 13, bold: rows[r].Head),
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Margin = new Thickness(0, 2, 0, 14),
            Child = grid,
        };
    }

    /// <summary>움짤(내장 WebP, 없으면 홈페이지 영상 링크만). 대체 글은 풍선 도움말·접근성 이름이고, 찾은 말이 있으면 노란 테두리로 칠하고 그리로 스크롤한다.</summary>
    private Control Media(HelpBlock b)
    {
        var url = PageUrl(b.Page ?? _page.Id);
        var altText = b.Alt ?? "";
        var anim = string.IsNullOrEmpty(b.Src) ? null : AnimatedImage.TryCreate(MediaOpener(Path.GetFileNameWithoutExtension(b.Src)));
        if (anim is null) return MediaLink(url, altText);

        anim.Tip(altText);
        AutomationProperties.SetName(anim, altText);
        bool hit = _terms.Count > 0 && HelpSearch.Matches(altText, _terms).Count > 0;
        var frame = new Border
        {
            BorderBrush = hit ? Yellow : AppTheme.BorderBrush,
            BorderThickness = new Thickness(hit ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = anim,
        };
        if (hit) _firstHit ??= frame;
        var link = new TextBlock
        {
            Text = UiStrings.Help_WatchLarger,
            Foreground = LinkBrush,
            FontSize = AppTheme.SmallFontSize,
            Cursor = HandCursor,
            HorizontalAlignment = HorizontalAlignment.Left,
        }.Tip(url);
        link.PointerPressed += (_, e) => { e.Handled = true; Updater.OpenUrl(this, url); };
        return new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 14), Children = { frame, link } };
    }

    /// <summary>내장 움짤 <c>Gpixel.Help.media.&lt;이름&gt;.webp</c>를 여는 함수(붙을 때마다 새로 연다).</summary>
    private static Func<Stream?> MediaOpener(string name) =>
        () => typeof(HelpWindow).Assembly.GetManifestResourceStream($"Gpixel.Help.media.{name}.webp");

    /// <summary>
    /// 화면 그림(내장 <c>Gpixel.Help.shots.&lt;이름&gt;.webp</c>): 원래 크기까지만 보이고 창이 좁으면 줄어든다. 아래에 빨간 번호 설명(Runs)을 단다.
    /// 대체 글·번호 설명에 찾은 말이 있으면 노란 테두리로 칠하고 그리로 스크롤한다.
    /// </summary>
    private Control Shot(HelpBlock b)
    {
        var altText = b.Alt ?? "";
        using var stream = typeof(HelpWindow).Assembly.GetManifestResourceStream($"Gpixel.Help.shots.{b.Src}.webp");
        if (stream is null) return Text(new[] { new HelpRun { T = altText } }, AppTheme.DimTextBrush, 12.5);
        var bitmap = new Bitmap(stream);
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = bitmap.Size.Width,
        }.Tip(altText);
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        image.DetachedFromVisualTree += (_, _) => bitmap.Dispose();
        AutomationProperties.SetName(image, altText);
        var caption = b.Runs ?? [];
        bool hit = _terms.Count > 0 && HelpSearch.Matches(altText + "\n" + string.Concat(caption.Select(r => r.T)), _terms).Count > 0;
        var frame = new Border
        {
            BorderBrush = hit ? Yellow : AppTheme.BorderBrush,
            BorderThickness = new Thickness(hit ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = image,
        };
        if (hit) _firstHit ??= frame;
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 16), Children = { frame } };
        if (caption.Length > 0) stack.Children.Add(Text(caption, AppTheme.DimTextBrush, 12.5));
        return stack;
    }

    /// <summary>움짤이 없을 때: 홈페이지 영상 링크 + 대체 글.</summary>
    private Control MediaLink(string url, string altText)
    {
        var link = new TextBlock
        {
            Text = UiStrings.Help_WatchVideo,
            Foreground = LinkBrush,
            FontWeight = FontWeight.Bold,
            Cursor = HandCursor,
        }.Tip(url);
        link.PointerPressed += (_, e) => { e.Handled = true; Updater.OpenUrl(this, url); };
        var alt = Text(new[] { new HelpRun { T = altText } }, AppTheme.DimTextBrush, 12.5);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*") };
        grid.Children.Add(new IconView(VectorIcon.Play, Gold) { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) });
        var stack = new StackPanel { Spacing = 2, Children = { link, alt } };
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        return new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 2, 0, 14),
            Child = grid,
        };
    }

    private Control Pager(HelpPage page)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 24, 0, 0) };
        if (page.Index > 0) grid.Children.Add(PagerCard(_pages[page.Index - 1], next: false));
        if (page.Index + 1 < _pages.Count)
        {
            var card = PagerCard(_pages[page.Index + 1], next: true);
            Grid.SetColumn(card, 2);
            grid.Children.Add(card);
        }
        return grid;
    }

    private Border PagerCard(HelpPage target, bool next)
    {
        var align = next ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var label = Ui.IconLabel(next ? VectorIcon.ArrowRight : VectorIcon.ArrowLeft, next ? UiStrings.Help_Next : UiStrings.Help_Previous, 12, iconAfter: next);
        label.HorizontalAlignment = align;
        foreach (var c in label.Children)
        {
            if (c is TextBlock tb) { tb.Foreground = AppTheme.DimTextBrush; tb.FontSize = AppTheme.SmallFontSize; }
            else if (c is IconView iv) iv.Foreground = AppTheme.DimTextBrush;
        }
        var title = new TextBlock { Text = target.Title, Foreground = AppTheme.TextBrush, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = align, TextAlignment = next ? TextAlignment.Right : TextAlignment.Left };
        var card = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            Background = Brushes.Transparent,
            Cursor = HandCursor,
            Child = new StackPanel { Spacing = 2, Children = { label, title } },
        };
        card.PointerEntered += (_, _) => card.BorderBrush = Gold;
        card.PointerExited += (_, _) => card.BorderBrush = AppTheme.BorderBrush;
        card.PointerPressed += (_, e) => { e.Handled = true; Show(target); };
        return card;
    }

    // ───────────── 글 줄 ─────────────

    /// <summary>
    /// 글 조각들로 줄바꿈되는 글 줄 하나(굵게·코드·키·링크, 검색 중이면 찾은 말을 노랗게).
    /// 링크도 Run이라 다른 글자처럼 줄바꿈되고, 누른 곳의 글자 위치로 어느 링크인지 찾는다.
    /// </summary>
    private TextBlock Text(IEnumerable<HelpRun> runs, IBrush fg, double size = BodySize, bool bold = false)
    {
        var tb = new TextBlock
        {
            Foreground = fg,
            FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            LineHeight = size >= 20 ? double.NaN : Math.Round(size * 1.6),
            TextWrapping = TextWrapping.Wrap,
        };
        var inlines = NewInlines(tb);
        List<(int Start, int End, string Href)>? links = null;
        int pos = 0;
        var list = runs as IReadOnlyList<HelpRun> ?? runs.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            int start = pos;
            // 앱 명령의 단축키(가이드의 {{key:명령}}): 사용자가 바꾼 키를 보인다(같은 명령 조각들을 한 번에).
            if (r.Cmd is { } cmd && Shortcuts?.Invoke(cmd) is { } keys)
            {
                while (i + 1 < list.Count && list[i + 1].Cmd == cmd) i++;
                pos += AddShortcut(inlines, keys, tb);
                continue;
            }
            if (r.Kbd) pos += AddKey(inlines, r.Keep ? r.T : OsKey(r.T), tb);
            else pos += AddHighlighted(inlines, r.T, _terms, tb, r.B, r.Code, r.Href is null ? null : LinkBrush);
            if (r.Href is { } href) (links ??= new()).Add((start, pos, href));
        }
        if (links is not null) AttachLinks(tb, links);
        return tb;
    }

    /// <summary>
    /// <paramref name="text"/>를 Run으로 더하고 글자 수를 돌려준다. <paramref name="terms"/>가 들어 있는 곳은 노란 바탕·어두운 글자로 칠하고,
    /// 처음 칠한 글 줄을 <see cref="_firstHit"/>에 남긴다(<paramref name="owner"/>가 있을 때).
    /// </summary>
    private int AddHighlighted(InlineCollection inlines, string text, IReadOnlyList<string> terms, Control? owner, bool bold = false, bool code = false, IBrush? fg = null)
    {
        int pos = 0;
        var matches = terms.Count == 0 ? Array.Empty<(int Start, int Length)>() : HelpSearch.Matches(text, terms);
        foreach (var (start, length) in matches)
        {
            if (start > pos) inlines.Add(MakeRun(text[pos..start], bold, code, false, fg));
            inlines.Add(MakeRun(text.Substring(start, length), bold, code, true, fg));
            if (owner is not null) _firstHit ??= owner;
            pos = start + length;
        }
        if (pos < text.Length) inlines.Add(MakeRun(text[pos..], bold, code, false, fg));
        return text.Length;
    }

    private static InlineCollection NewInlines(TextBlock tb)
    {
        var inlines = new InlineCollection();
        tb.Inlines = inlines;
        return inlines;
    }

    private static Run MakeRun(string text, bool bold, bool code, bool hit, IBrush? fg = null)
    {
        var run = new Run(text);
        if (bold) run.FontWeight = FontWeight.Bold;
        if (code)
        {
            run.FontFamily = MonoFont;
            run.Background = CodeBrush;
            run.Foreground = AppTheme.TextBrush;
        }
        if (fg is not null) run.Foreground = fg;
        if (hit)
        {
            run.Background = Yellow;
            run.Foreground = OnYellow;
        }
        return run;
    }

    /// <summary>키 표시: 코드처럼 고정폭·어두운 바탕에 양옆을 조금 띄운다(글 줄 높이를 바꾸지 않게 Run으로). 글자 수를 돌려준다.</summary>
    private int AddKey(InlineCollection inlines, string text, Control owner)
    {
        inlines.Add(MakeRun(KeyPad, false, true, false));
        AddHighlighted(inlines, text, _terms, owner, code: true);
        inlines.Add(MakeRun(KeyPad, false, true, false));
        return text.Length + 2 * KeyPad.Length;
    }

    /// <summary>
    /// 명령 단축키를 이 OS 표기로: Windows·Linux는 수정자마다 키 칸(<c>Ctrl</c>+<c>Z</c>), macOS는 메뉴처럼 한 칸(<c>⌘Z</c>).
    /// 단축키를 모두 지웠으면 "단축키 없음". 글자 수를 돌려준다.
    /// </summary>
    private int AddShortcut(InlineCollection inlines, IReadOnlyList<KeyChord> keys, Control owner)
    {
        if (keys.Count == 0) return AddHighlighted(inlines, UiStrings.Help_NoShortcut, _terms, owner);
        var text = Gpixel.App.Shortcuts.Format(keys[0]);
        if (Platform.IsMac) return AddKey(inlines, Ui.Shortcut(text), owner);
        // 키 이름에 '+'가 들어갈 수 있으므로(Num+) 알려진 수정자만 앞에서 떼어 낸다.
        var parts = new List<string>();
        string[] modifiers = ["Ctrl", "Control", "Meta", "Alt", "Shift"];
        int plus;
        while ((plus = text.IndexOf('+')) > 0 && plus < text.Length - 1 && modifiers.Contains(text[..plus]))
        {
            parts.Add(text[..plus]);
            text = text[(plus + 1)..];
        }
        parts.Add(text);
        int length = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) length += AddHighlighted(inlines, "+", _terms, owner);
            length += AddKey(inlines, parts[i], owner);
        }
        return length;
    }

    /// <summary>키 하나(가이드의 &lt;kbd&gt;)를 이 OS 표기로: macOS는 Ctrl·Alt·Shift를 ⌘·⌥·⇧로.</summary>
    private static string OsKey(string key) => !Platform.IsMac ? key : key switch
    {
        "Ctrl" => "⌘",
        "Alt" => "⌥",
        "Shift" => "⇧",
        _ => key,
    };

    /// <summary>키 글자 양옆 여백(좁은 빈칸).</summary>
    private const string KeyPad = "\u2009";

    /// <summary>링크 글자 위에서 손 모양, 누르면 <c>#쪽id</c>는 이 창에서 그 쪽을 열고 바깥 주소는 브라우저로 연다.</summary>
    private void AttachLinks(TextBlock tb, List<(int Start, int End, string Href)> links)
    {
        string? HrefAt(PointerEventArgs e)
        {
            // IsInside는 여러 줄에서 믿을 수 없어, 그 자리 글자의 칸에 점이 실제로 들어가는지 본다.
            var at = e.GetPosition(tb);
            int index = tb.TextLayout.HitTestPoint(at).CharacterHit.FirstCharacterIndex;
            if (!tb.TextLayout.HitTestTextRange(index, 1).Any(r => r.Inflate(1).Contains(at))) return null;
            foreach (var (start, end, href) in links)
                if (index >= start && index < end) return href;
            return null;
        }
        tb.PointerMoved += (_, e) => tb.Cursor = HrefAt(e) is null ? null : HandCursor;
        tb.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tb).Properties.IsLeftButtonPressed || HrefAt(e) is not { } href) return;
            e.Handled = true;
            if (href.StartsWith('#')) ShowPage(href[1..]);
            else Updater.OpenUrl(this, href);
        };
    }
}

// ───────────── 쪽 데이터(help.<언어>.json, tools/build-help.py가 씀) ─────────────

internal sealed class HelpBook
{
    public HelpGroup[] Groups { get; set; } = [];
}

internal sealed class HelpGroup
{
    public string Label { get; set; } = "";
    public HelpChapter[] Chapters { get; set; } = [];
}

internal sealed class HelpChapter
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<HelpPage> Pages { get; set; } = new();
    /// <summary>세부 항목(h3) 쪽들이 있는 장인지(없으면 장 하나가 장 id의 한 쪽).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool HasSubPages => Pages[0].Id != Id;
}

internal sealed class HelpPage
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public HelpBlock[] Blocks { get; set; } = [];
    /// <summary>장 머리말 글(그 장 첫 세부 쪽에만, 제목 위에 보임).</summary>
    public HelpBlock[]? Intro { get; set; }
    /// <summary>검색용 본문 글자.</summary>
    public string Text { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public HelpChapter Chapter { get; set; } = null!;
    [System.Text.Json.Serialization.JsonIgnore] public int Index { get; set; }
}

/// <summary>블록: p·h4(Runs), ul·ol·steps(Items, Marks = 칸이 그림 번호로 시작해 머리표 없음), table(Head·Rows), note(Blocks), pre(Text), media(Alt·Page·Src), shot(Src·Alt, Runs = 번호 설명).</summary>
internal sealed class HelpBlock
{
    public string Type { get; set; } = "";
    public HelpRun[]? Runs { get; set; }
    public HelpItem[]? Items { get; set; }
    public bool Marks { get; set; }
    public HelpRun[][]? Head { get; set; }
    public HelpRun[][][]? Rows { get; set; }
    public HelpBlock[]? Blocks { get; set; }
    public string? Text { get; set; }
    public string? Alt { get; set; }
    public string? Page { get; set; }
    public string? Src { get; set; }
}

/// <summary>목록 항목(Runs + 안쪽 목록 Sub) 또는 단계(Title + Runs).</summary>
internal sealed class HelpItem
{
    public HelpRun[]? Title { get; set; }
    public HelpRun[]? Runs { get; set; }
    public HelpBlock[]? Sub { get; set; }
}

/// <summary>
/// 글 조각: T = 글자, B = 굵게, Code·Kbd = 코드·키, Href = <c>#쪽id</c> 또는 바깥 주소,
/// Cmd = 앱 명령 id(그 명령의 지금 단축키로 바꿔 그림), Keep = macOS에서도 키 이름을 그대로(⌘로 바꾸지 않음).
/// </summary>
internal sealed class HelpRun
{
    public string T { get; set; } = "";
    public bool B { get; set; }
    public bool Code { get; set; }
    public bool Kbd { get; set; }
    public string? Cmd { get; set; }
    public bool Keep { get; set; }
    public string? Href { get; set; }
}
