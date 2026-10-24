using Avalonia.Controls.Documents;
using Gpixel.Core.Text;

namespace Gpixel.App.Ai;

/// <summary>
/// AI 답 글 보기: <see cref="MiniMarkdown"/>이 읽은 블록마다 컨트롤 하나(문단·제목·목록 항목 = 고를 수 있는 글, 코드 블록 = 고정폭 상자,
/// 인용 = 왼쪽 막대). 글은 <see cref="SelectableTextBlock"/>의 Run이라 골라 복사하면 표시 기호 없는 글이 나온다.
/// 링크(http·https)는 눌러서 연다. <see cref="SetText"/>를 다시 부르면 원문이 그대로인 앞쪽 블록은 두고 바뀐 뒤쪽만 새로 그린다(스트리밍).
/// </summary>
internal sealed class MarkdownView : StackPanel
{
    private static readonly double[] HeadingSizes = [AppTheme.FontSize + 4, AppTheme.FontSize + 2, AppTheme.FontSize + 1];
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly FontFamily _mono;
    private readonly Action<string> _openUrl;
    private readonly List<MdBlock> _shown = [];
    private string? _text;

    public MarkdownView(FontFamily mono, Action<string> openUrl)
    {
        _mono = mono;
        _openUrl = openUrl;
        Spacing = 6;
        Margin = new Thickness(2, 0);
    }

    /// <summary>목록 항목을 더 들여 쓸 너비(깊이마다 들여 쓰는 16에 더함). 업데이트 창은 소제목 아래 세부 목록을 들여 쓴다.</summary>
    public double ListIndent { get; init; }

    /// <summary>맨 처음이 아닌 제목 위에 더 띄울 높이(<c>#</c>·<c>##</c>는 두 배). 업데이트 창이 버전·소제목 덩어리를 나눠 보이게 쓴다.</summary>
    public double HeadingGap { get; init; }

    public void SetText(string text)
    {
        if (text == _text) return;
        _text = text;
        var blocks = MiniMarkdown.Parse(text);
        int same = 0;
        while (same < blocks.Count && same < _shown.Count && blocks[same].Kind == _shown[same].Kind && blocks[same].Source == _shown[same].Source) same++;
        _shown.RemoveRange(same, _shown.Count - same);
        Children.RemoveRange(same, Children.Count - same);
        for (int i = same; i < blocks.Count; i++)
        {
            _shown.Add(blocks[i]);
            var control = Build(blocks[i], AppTheme.TextBrush);
            if (i > 0 && HeadingGap > 0 && blocks[i].Kind == MdBlockKind.Heading)
                control.Margin = new Thickness(0, blocks[i].Level <= 2 ? HeadingGap * 2 : HeadingGap, 0, 0);
            Children.Add(control);
        }
    }

    private Control Build(MdBlock block, IBrush foreground)
    {
        switch (block.Kind)
        {
            case MdBlockKind.Heading:
                var heading = Text(block.Spans, foreground);
                heading.FontWeight = FontWeight.Bold;
                heading.FontSize = HeadingSizes[Math.Clamp(block.Level, 1, 3) - 1];
                return heading;
            case MdBlockKind.ListItem:
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(ListIndent + block.Level * 16, 0, 0, 0) };
                Control marker = block.Number is { } number
                    ? new TextBlock { Text = number, Foreground = foreground, Margin = new Thickness(0, 0, 6, 0) }
                    : new IconView(VectorIcon.Dot, AppTheme.AccentBrush) { Filled = true, Width = 8, Height = 8, Margin = new Thickness(2, 5, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                row.Children.Add(marker);
                var item = Text(block.Spans, foreground);
                Grid.SetColumn(item, 1);
                row.Children.Add(item);
                return row;
            case MdBlockKind.Quote:
                var inner = new StackPanel { Spacing = 6 };
                foreach (var child in block.Children) inner.Children.Add(Build(child, AppTheme.DimTextBrush));
                return new Border
                {
                    BorderBrush = AppTheme.BorderBrush,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(8, 2, 0, 2),
                    Child = inner,
                };
            case MdBlockKind.Code:
                return new Border
                {
                    Background = AppTheme.InputBrush,
                    BorderBrush = AppTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 4),
                    Child = new SelectableTextBlock
                    {
                        Text = block.Code,
                        FontFamily = _mono,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = AppTheme.TextBrush,
                    },
                };
            default:
                return Text(block.Spans, foreground);
        }
    }

    /// <summary>꾸민 글 한 덩이. 링크 자리(글 안 위치)를 기억해 누르면 연다.</summary>
    private SelectableTextBlock Text(IReadOnlyList<MdSpan> spans, IBrush foreground)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = foreground };
        var inlines = new InlineCollection();
        List<(int Start, int End, string Url)>? links = null;
        int pos = 0;
        foreach (var span in spans)
        {
            var run = new Run(span.Text);
            if (span.Style.HasFlag(MdStyle.Bold)) run.FontWeight = FontWeight.Bold;
            if (span.Style.HasFlag(MdStyle.Italic)) run.FontStyle = FontStyle.Italic;
            if (span.Style.HasFlag(MdStyle.Code))
            {
                run.FontFamily = _mono;
                run.Background = AppTheme.InputBrush;
            }
            if (span.Url is { } url)
            {
                run.Foreground = AppTheme.AccentBrush;
                run.TextDecorations = TextDecorations.Underline;
                (links ??= []).Add((pos, pos + span.Text.Length, url));
            }
            inlines.Add(run);
            pos += span.Text.Length;
        }
        block.Inlines = inlines;
        if (links is not null) HookLinks(block, links);
        return block;
    }

    private void HookLinks(SelectableTextBlock block, List<(int Start, int End, string Url)> links)
    {
        string? LinkAt(PointerEventArgs e)
        {
            var p = e.GetPosition(block);
            var hit = block.TextLayout.HitTestPoint(new Avalonia.Point(p.X - block.Padding.Left, p.Y - block.Padding.Top));
            if (!hit.IsInside) return null;
            int at = hit.CharacterHit.FirstCharacterIndex;
            foreach (var (start, end, url) in links)
                if (at >= start && at < end) return url;
            return null;
        }

        block.PointerMoved += (_, e) =>
        {
            if (LinkAt(e) is null) block.ClearValue(CursorProperty);
            else block.Cursor = HandCursor;
        };
        // 끌어서 고른 뒤 놓을 때는 열지 않는다(고른 글이 없을 때만).
        block.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && block.SelectionStart == block.SelectionEnd && LinkAt(e) is { } url) _openUrl(url);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
