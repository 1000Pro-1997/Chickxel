using System.Drawing;
using System.Text;
using Chickxel.Core.Model;

namespace Chickxel.Core.Text;

/// <summary>
/// 글씨 효과(<see cref="TextEffect"/>)의 글자 배치. 좌표는 효과 원본 왼쪽 위 기준 px(<see cref="TextEffect.X"/>·<see cref="TextEffect.Y"/>와 같음).
/// 글 상자가 없으면(<see cref="TextEffect.BoxWidth"/> = 0) '\n'에서만 줄을 바꾸고 맞춤에 따라 줄 왼쪽 끝 = X(왼쪽) / X − ⌊너비/2⌋(가운데) / X − 너비(오른쪽).
/// 글 상자면 그 너비를 넘지 않게 낱말 사이(빈칸·탭 뒤)에서 줄을 바꾸고(한 낱말이 너무 길면 글자 사이에서), 상자 안에서 맞춘다 — 줄 끝 빈칸은 너비에 넣지 않는다.
/// i번째 줄 위쪽 끝 = Y + i × (줄 높이 + 줄 간격). 그리기·앱의 글씨 고르기·캔버스 글 편집(커서·선택)이 같이 쓴다.
/// </summary>
public static class TextLayout
{
    /// <summary>탭 한 개 = 빈칸 몇 개.</summary>
    internal const int TabSpaces = 4;

    public static TextLayoutResult Measure(TextEffect e) => new(e, TextFace.For(e));

    /// <summary>모든 글자(타자기 진행과 관계없이)의 잉크 사각형을 합친 사각형. 찍히는 글자가 없으면 <see cref="Rectangle.Empty"/>.</summary>
    public static Rectangle Bounds(TextEffect e) => Measure(e).Ink;

    /// <summary>
    /// 글 전체(상자 너비에서 바뀌는 줄까지)가 높이 <paramref name="height"/> px 안에 드는 가장 큰 글자 크기(1~<see cref="TextEffect.MaxSize"/>, 1에서도 넘치면 1).
    /// 빈 글은 한 줄로 잰다. 캔버스 글씨 도구가 끈 상자 높이로 크기를 정할 때 쓴다.
    /// </summary>
    public static int FitSize(TextEffect e, int height)
    {
        e = e with { BoxHeight = 0 };
        int fit = 1, lo = 2, hi = TextEffect.MaxSize;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var probe = e with { Size = mid };
            if (new TextLayoutResult(probe, TextFace.Uncached(probe)).Frame.Height <= height)
            {
                fit = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return fit;
    }
}

/// <summary>
/// 줄 하나: 글(<see cref="TextEffect.Text"/>)의 UTF-16 위치 <see cref="Start"/>부터 <see cref="Length"/>개('\n'은 빼고, 상자에서 바뀐 줄은 끝 빈칸 포함),
/// 맞춤에 쓰는 너비만큼의 줄 사각형(왼쪽·위·너비·높이).
/// </summary>
public readonly record struct TextLine(int Start, int Length, int Left, int Top, int Width, int Height);

/// <summary>
/// 글씨 효과 하나의 배치 결과(<see cref="TextLayout.Measure"/>): 잉크·글 상자·줄, 커서 자리·누른 자리 → 커서 위치·선택 사각형.
/// 글 위치는 <see cref="TextEffect.Text"/>의 UTF-16 위치(0~길이)이고, 서로게이트 쌍 가운데는 쌍 앞으로 본다.
/// </summary>
public sealed class TextLayoutResult
{
    private readonly string _text;
    private readonly TextLine[] _lines;

    /// <summary>줄마다 위치(줄 시작 + 0~길이)의 커서 X.</summary>
    private readonly int[][] _carets;

    /// <summary>줄마다 상자 너비에서 바뀐 줄인지(끝 위치 = 다음 줄 시작).</summary>
    private readonly bool[] _soft;

    private readonly int _newlineWidth;

    internal TextLayoutResult(TextEffect e, TextFace face)
    {
        _text = e.Text;
        int spacing = e.LetterSpacing, lineHeight = face.LineHeight, step = lineHeight + e.LineSpacing;
        var space = face.Glyph(' ');
        var lines = new List<TextLine>();
        var carets = new List<int[]>();
        var soft = new List<bool>();
        var placed = new List<PlacedGlyph>();
        var items = new List<Item>();
        int order = 0, start = 0;
        while (true)
        {
            int end = _text.IndexOf('\n', start);
            if (end < 0) end = _text.Length;
            items.Clear();
            for (int i = start; i < end;)
            {
                var rune = Rune.GetRuneAt(_text, i);
                int length = rune.Utf16SequenceLength;
                if (rune.Value == '\t')
                    items.Add(space is null ? new Item(i, length, order, null, 0, false, true, 0) : new Item(i, length, order, space, TextLayout.TabSpaces * (space.Advance + spacing), true, true, TextLayout.TabSpaces));
                else if (face.Glyph(rune.Value) is { } g)
                    items.Add(new Item(i, length, order, g, g.Advance + spacing, true, rune.Value == ' ', 1));
                else
                    items.Add(new Item(i, length, order, null, 0, false, false, 0));
                order++;
                i += length;
            }

            // 상자 너비 안으로 줄 나누기(빈칸은 넘쳐도 줄 끝에 남김)
            int first = 0;
            while (true)
            {
                int last = items.Count;
                if (e.BoxWidth > 0)
                {
                    int pen = 0, breakAt = -1;
                    for (int i = first; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (!it.Space && it.Present && i > first && pen + it.Step - spacing > e.BoxWidth)
                        {
                            last = breakAt > first ? breakAt : i;
                            break;
                        }
                        pen += it.Step;
                        if (it.Space) breakAt = i + 1;
                    }
                }
                bool wrapped = last < items.Count;
                int lineStart = first < items.Count ? items[first].Index : end;
                int lineEnd = wrapped ? items[last].Index : end;
                AddLine(first, last, lineStart, lineEnd, wrapped);
                if (!wrapped) break;
                first = last;
            }
            if (end >= _text.Length) break;
            start = end + 1;
        }
        Count = order;
        _lines = [.. lines];
        _carets = [.. carets];
        _soft = [.. soft];
        Placed = placed;
        _newlineWidth = Math.Max(1, (space?.Advance ?? lineHeight / 4) / 2);

        var ink = Rectangle.Empty;
        foreach (var p in placed) ink = ink.IsEmpty ? p.Rect : Rectangle.Union(ink, p.Rect);
        Ink = ink;
        int textHeight = Math.Max(lineHeight, _lines.Length * lineHeight + (_lines.Length - 1) * e.LineSpacing);
        if (e.BoxWidth > 0)
        {
            Frame = new Rectangle(e.X, e.Y, e.BoxWidth, Math.Max(e.BoxHeight, textHeight));
        }
        else
        {
            int left = _lines.Min(l => l.Left), right = _lines.Max(l => l.Left + Math.Max(1, l.Width));
            Frame = new Rectangle(left, e.Y, right - left, textHeight);
        }

        void AddLine(int from, int to, int lineStart, int lineEnd, bool wrapped)
        {
            // 너비: 놓이는 글자마다 폭 + 간격, 끝 글자 뒤 간격은 뺌(상자에서 바뀐 줄은 끝 빈칸도 뺌)
            int total = 0, visible = 0;
            bool any = false, anyVisible = false;
            for (int i = from; i < to; i++)
            {
                var it = items[i];
                total += it.Step;
                any |= it.Present;
                if (it.Present && !it.Space || !wrapped)
                {
                    visible = total;
                    anyVisible |= it.Present;
                }
            }
            int width = anyVisible ? visible - spacing : 0, endPen = any ? total - spacing : 0;
            int left = e.BoxWidth > 0
                ? e.Align switch
                {
                    TextAlign.Center => e.X + (int)Math.Floor((e.BoxWidth - width) / 2.0),
                    TextAlign.Right => e.X + e.BoxWidth - width,
                    _ => e.X,
                }
                : e.Align switch
                {
                    TextAlign.Center => e.X - (int)Math.Floor(width / 2.0),
                    TextAlign.Right => e.X - width,
                    _ => e.X,
                };
            int top = e.Y + lines.Count * step;
            var xs = new int[lineEnd - lineStart + 1];
            int pen = 0;
            for (int i = from; i < to; i++)
            {
                var it = items[i];
                for (int k = 0; k < it.Length; k++) xs[it.Index - lineStart + k] = left + pen;
                if (it.Glyph is { } g)
                    for (int r = 0; r < it.Repeat; r++)
                    {
                        if (g.HasInk)
                            placed.Add(new PlacedGlyph(g, it.Order, new Rectangle(left + pen + g.Left, top + face.Ascent + g.Top, g.Width, g.Height)));
                        pen += g.Advance + spacing;
                    }
            }
            xs[^1] = left + endPen;
            lines.Add(new TextLine(lineStart, lineEnd - lineStart, left, top, width, lineHeight));
            carets.Add(xs);
            soft.Add(wrapped);
        }
    }

    /// <summary>잉크가 있는 글자들의 자리(글 순서).</summary>
    internal IReadOnlyList<PlacedGlyph> Placed { get; }

    /// <summary>타자기가 세는 글자 수(줄바꿈을 뺀 코드 포인트 — 탭·없는 글자도 하나).</summary>
    internal int Count { get; }

    /// <summary>찍히는 픽셀을 모두 덮는 사각형(없으면 <see cref="Rectangle.Empty"/>).</summary>
    public Rectangle Ink { get; }

    /// <summary>
    /// 글 상자: 상자가 있으면 (X, Y, 상자 너비, max(상자 높이, 글 높이)), 없으면 줄들이 차지하는 범위(너비 최소 1px, 높이 최소 한 줄).
    /// </summary>
    public Rectangle Frame { get; }

    public IReadOnlyList<TextLine> Lines => _lines;

    /// <summary>위치 <paramref name="index"/> 앞의 1px 너비 커서(그 줄의 위·높이). 상자에서 바뀐 줄의 끝 위치는 다음 줄 시작에 둔다.</summary>
    public Rectangle CaretRect(int index)
    {
        int line = LineOf(ref index);
        var l = _lines[line];
        return new Rectangle(_carets[line][index - l.Start], l.Top, 1, l.Height);
    }

    /// <summary>점 <paramref name="p"/>에 가장 가까운 커서 위치(가장 가까운 줄에서 가장 가까운 글자 사이).</summary>
    public int HitTest(Point p)
    {
        int best = 0, bestDistance = int.MaxValue, bestCenter = int.MaxValue;
        for (int i = 0; i < _lines.Length; i++)
        {
            var l = _lines[i];
            int distance = p.Y < l.Top ? l.Top - p.Y : p.Y >= l.Top + l.Height ? p.Y - (l.Top + l.Height - 1) : 0;
            int center = Math.Abs(2 * p.Y - (2 * l.Top + l.Height));
            if (distance < bestDistance || distance == bestDistance && center < bestCenter)
                (best, bestDistance, bestCenter) = (i, distance, center);
        }
        var line = _lines[best];
        var xs = _carets[best];
        // 상자에서 바뀐 줄의 끝 위치는 다음 줄 커서이므로 이 줄에서는 고르지 않는다
        int last = _soft[best] && line.Length > 0 ? line.Length - 1 : line.Length;
        int bestIndex = line.Start, bestDx = int.MaxValue;
        for (int k = 0; k <= last; k++)
        {
            int index = line.Start + k;
            if (index < _text.Length && index > 0 && char.IsLowSurrogate(_text[index]) && char.IsHighSurrogate(_text[index - 1])) continue;
            int dx = Math.Abs(xs[k] - p.X);
            if (dx < bestDx) (bestIndex, bestDx) = (index, dx);
        }
        return bestIndex;
    }

    /// <summary>
    /// 위치 [<paramref name="start"/>, <paramref name="end"/>)의 선택을 덮는 사각형(덮인 줄마다 하나, 줄 위·높이). 줄바꿈까지 고르면 그 줄 끝에 조금 더 그린다.
    /// </summary>
    public IReadOnlyList<Rectangle> SelectionRects(int start, int end)
    {
        if (start > end) (start, end) = (end, start);
        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, 0, _text.Length);
        var rects = new List<Rectangle>();
        if (start == end) return rects;
        for (int i = 0; i < _lines.Length; i++)
        {
            var l = _lines[i];
            int lineEnd = l.Start + l.Length;
            int a = Math.Max(start, l.Start), b = Math.Min(end, lineEnd);
            if (b < a) continue;
            bool past = end > lineEnd && start <= lineEnd;
            if (a == b && !past) continue;
            int x0 = _carets[i][a - l.Start], x1 = _carets[i][b - l.Start];
            if (past && !_soft[i]) x1 += _newlineWidth;
            rects.Add(new Rectangle(Math.Min(x0, x1), l.Top, Math.Max(1, Math.Abs(x1 - x0)), l.Height));
        }
        return rects;
    }

    /// <summary>위치가 놓인 줄(위치를 0~길이로 자르고 서로게이트 쌍 가운데면 쌍 앞으로 옮김).</summary>
    private int LineOf(ref int index)
    {
        index = Math.Clamp(index, 0, _text.Length);
        if (index > 0 && index < _text.Length && char.IsLowSurrogate(_text[index]) && char.IsHighSurrogate(_text[index - 1])) index--;
        for (int i = 0; i < _lines.Length; i++)
        {
            var l = _lines[i];
            if (index < l.Start || index > l.Start + l.Length) continue;
            if (index == l.Start + l.Length && _soft[i] && i + 1 < _lines.Length) continue;
            return i;
        }
        return _lines.Length - 1;
    }

    /// <summary>
    /// 줄 안 글자 하나(탭은 빈칸 <see cref="TextLayout.TabSpaces"/>개): 글 위치·길이, 타자기 순서, 그림(없으면 null — 폭 0),
    /// 펜 이동(폭 + 간격, 반복 포함), 놓이는지(간격을 셈), 빈칸·탭인지(줄 바꿈 자리).
    /// </summary>
    private readonly record struct Item(int Index, int Length, int Order, RasterGlyph? Glyph, int Step, bool Present, bool Space, int Repeat);
}

/// <summary>놓인 글자 하나: 그림, 타자기 순서(0부터), 잉크 사각형(효과 원본 기준).</summary>
internal readonly record struct PlacedGlyph(RasterGlyph Glyph, int Order, Rectangle Rect);
