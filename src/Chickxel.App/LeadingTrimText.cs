using System.Globalization;

namespace Chickxel.App;

/// <summary>
/// 한 줄 글을 칸에 안 들어가면 앞을 줄임표로 잘라 끝이 보이게 그린다(폴더 경로 등). Avalonia의 <see cref="TextTrimming.LeadingCharacterEllipsis"/>는
/// 한글과 라틴 글자가 섞인 글(예: <c>C:\Users\…\Desktop\#GPT#DOT#열로</c>)에서 폭에 따라 ArgumentOutOfRangeException("length must be greater than zero")을
/// 내므로 직접 자른다 — 글자 단위(서로게이트·결합 문자를 나누지 않음)로 줄임표 + 뒷부분이 들어가는 가장 긴 것.
/// </summary>
internal sealed class LeadingTrimText : Control
{
    private const string Ellipsis = "…";
    private string _text = "";
    private IBrush _foreground = AppTheme.TextBrush;

    public string? Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            _text = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public IBrush Foreground
    {
        get => _foreground;
        set
        {
            _foreground = value;
            InvalidateVisual();
        }
    }

    private FormattedText Format(string text) => AppTheme.Text(text, _foreground);

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        var ft = Format(_text.Length == 0 ? " " : _text);
        double width = _text.Length == 0 ? 0 : ft.WidthIncludingTrailingWhitespace;
        return new Avalonia.Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), ft.Height);
    }

    public override void Render(DrawingContext g)
    {
        if (_text.Length == 0) return;
        double width = Bounds.Width;
        var full = Format(_text);
        var shown = full;
        if (full.WidthIncludingTrailingWhitespace > width + 0.5)
        {
            // 앞에서부터 글자를 하나씩 덜어 줄임표 + 남은 뒷부분이 들어가는 첫 자리(모두 덜어도 안 들어가면 줄임표만).
            shown = Format(Ellipsis);
            int[] starts = StringInfo.ParseCombiningCharacters(_text);
            for (int i = 1; i < starts.Length; i++)
            {
                var candidate = Format(Ellipsis + _text[starts[i]..]);
                if (candidate.WidthIncludingTrailingWhitespace <= width + 0.5)
                {
                    shown = candidate;
                    break;
                }
            }
        }
        using (g.PushClip(new Rect(Bounds.Size)))
            g.DrawText(shown, new Avalonia.Point(0, (Bounds.Height - shown.Height) / 2));
    }
}
