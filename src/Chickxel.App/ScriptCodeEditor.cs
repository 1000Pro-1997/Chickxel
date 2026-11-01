using System.ComponentModel;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using Chickxel.App.Localization;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 스크립트 효과 코드 편집기(설계 4장): AvaloniaEdit에 JavaScript 문법 색칠(어두운 화면용 색), 줄 번호, 자동 완성(<see cref="ScriptCompletion.Suggest"/> —
/// 점·글자·따옴표를 치거나 Ctrl+Space), 매개변수 안내(<see cref="ScriptCompletion.Signature"/>), 이름 위 설명 풍선과 오류 밑줄을 붙인다.
/// </summary>
internal sealed class ScriptCodeEditor : UserControl
{
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");
    private static IHighlightingDefinition? s_highlighting;

    private readonly TextEditor _editor;
    private readonly ErrorUnderlines _errors = new();
    private CompletionWindow? _completion;
    private OverloadInsightWindow? _insight;
    private SignatureProvider? _signature;

    public ScriptCodeEditor()
    {
        _editor = new TextEditor
        {
            FontFamily = MonoFont,
            FontSize = 13,
            ShowLineNumbers = true,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
            LineNumbersForeground = AppTheme.DimTextBrush,
            SyntaxHighlighting = Highlighting(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4),
        };
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.HighlightCurrentLine = true;
        _editor.TextArea.TextView.CurrentLineBackground = AppTheme.Brush(Color.FromArgb(28, 255, 255, 255));
        _editor.TextArea.TextView.CurrentLineBorder = null;
        _editor.TextArea.SelectionBrush = AppTheme.Brush(Color.FromArgb(90, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
        _editor.TextArea.TextView.BackgroundRenderers.Add(_errors);

        _editor.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateSignature();
        _editor.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _editor.PointerHover += OnHover;
        _editor.PointerHoverStopped += (_, _) => ToolTip.SetIsOpen(_editor, false);
        Content = new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _editor };
    }

    /// <summary>원문이 바뀜(사용자 입력과 <see cref="Text"/> 설정 모두).</summary>
    public event EventHandler? TextChanged;

    public string Text
    {
        get => _editor.Text;
        set
        {
            if (_editor.Text == value) return;
            _editor.Document = new TextDocument(value); // 다른 원문을 열면 실행취소 기록도 새로
            _editor.TextArea.Caret.Offset = 0;
        }
    }

    /// <summary>오류 자리(줄·칸, 1부터)와 글. 위치가 없는 오류(0)는 밑줄을 긋지 않는다.</summary>
    public void SetErrors(IEnumerable<ScriptError> errors)
    {
        _errors.Errors = errors.Where(e => e.Line > 0).ToList();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>커서를 줄·칸(1부터)으로 옮기고 보이게 한다.</summary>
    public void GoTo(int line, int column)
    {
        if (line < 1 || line > _editor.Document.LineCount) return;
        var docLine = _editor.Document.GetLineByNumber(line);
        _editor.TextArea.Caret.Offset = docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
        _editor.ScrollTo(line, column);
        _editor.TextArea.Focus();
    }

    /// <summary>커서 자리에 글을 넣는다(선택이 있으면 바꾼다).</summary>
    public void Insert(string text)
    {
        _editor.TextArea.Selection.ReplaceSelectionWithText(text);
        _editor.TextArea.Focus();
    }

    /// <summary>
    /// 원문을 <paramref name="text"/>로 바꾸되 앞뒤가 같은 부분은 두고 달라진 가운데만 고친다(실행취소 한 단계, 커서·스크롤 유지 —
    /// 정보 칸에서 선언의 이름·설명·병아리를 고칠 때). 새 원문을 열 때는 <see cref="Text"/>.
    /// </summary>
    public void Replace(string text)
    {
        var doc = _editor.Document;
        string old = doc.Text;
        if (old == text) return;
        int start = 0, max = Math.Min(old.Length, text.Length);
        while (start < max && old[start] == text[start]) start++;
        int end = 0;
        while (end < max - start && old[old.Length - 1 - end] == text[text.Length - 1 - end]) end++;
        doc.Replace(start, old.Length - start - end, text.Substring(start, text.Length - start - end));
    }

    // ───────────── 문법 색칠 ─────────────

    /// <summary>내장 JavaScript 정의의 색을 어두운 배경에 맞게 바꾼 것(한 번만).</summary>
    private static IHighlightingDefinition? Highlighting()
    {
        if (s_highlighting is not null) return s_highlighting;
        var definition = HighlightingManager.Instance.GetDefinition("JavaScript");
        if (definition is null) return null;
        foreach (var color in definition.NamedHighlightingColors)
        {
            string name = color.Name ?? "";
            Color? fore = name switch
            {
                _ when name.Contains("Comment", StringComparison.OrdinalIgnoreCase) => Color.FromRgb(118, 150, 104),
                _ when name.Contains("String", StringComparison.OrdinalIgnoreCase) || name.Contains("Char", StringComparison.OrdinalIgnoreCase)
                    => Color.FromRgb(214, 157, 112),
                _ when name.Contains("Regex", StringComparison.OrdinalIgnoreCase) => Color.FromRgb(214, 157, 112),
                _ when name.Contains("Digit", StringComparison.OrdinalIgnoreCase) => Color.FromRgb(181, 206, 168),
                _ when name.Contains("Literal", StringComparison.OrdinalIgnoreCase) => Color.FromRgb(86, 156, 214),
                _ when name.Contains("KeyWord", StringComparison.OrdinalIgnoreCase) => Color.FromRgb(197, 134, 192),
                _ when name.Contains("Intrinsic", StringComparison.OrdinalIgnoreCase) || name.Contains("Global", StringComparison.OrdinalIgnoreCase)
                    => Color.FromRgb(78, 201, 176),
                _ => null,
            };
            if (fore is { } c)
            {
                color.Foreground = new SimpleHighlightingBrush(c);
                color.Background = null;
            }
        }
        return s_highlighting = definition;
    }

    // ───────────── 자동 완성 ─────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            ShowCompletion(explicitRequest: true);
        }
    }

    // 후보 창이 열려 있을 때 이름이 아닌 글자를 치면 고른 후보를 먼저 넣는다(점·괄호를 이어 칠 수 있게)
    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completion is null || string.IsNullOrEmpty(e.Text)) return;
        char c = e.Text[0];
        if (!char.IsLetterOrDigit(c) && c is not '_' and not '$') _completion.CompletionList.RequestInsertion(e);
    }

    // 붙여넣기·한꺼번에 들어온 글자(입력기)도 마지막 글자로 판단한다
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        char c = e.Text[^1];
        if (c is '.' or '"' or '\'') ShowCompletion(explicitRequest: false);
        else if ((char.IsLetter(c) || c is '_' or '$') && _completion is null && WordLength() == 1) ShowCompletion(explicitRequest: false);
        if (c is '(' or ',' or ')') UpdateSignature();
    }

    /// <summary>커서 앞 이름 글자 수.</summary>
    private int WordLength()
    {
        int offset = _editor.CaretOffset, start = offset;
        var doc = _editor.Document;
        while (start > 0 && (char.IsLetterOrDigit(doc.GetCharAt(start - 1)) || doc.GetCharAt(start - 1) is '_' or '$')) start--;
        return offset - start;
    }

    private void ShowCompletion(bool explicitRequest)
    {
        if (_completion is not null) return;
        int caret = _editor.CaretOffset;
        var result = ScriptCompletion.Suggest(_editor.Text, caret);
        if (result.Items.Count == 0) return;
        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = result.Start,
            EndOffset = caret,
            CloseWhenCaretAtBeginning = !explicitRequest,
            MinWidth = 260,
        };
        foreach (var item in result.Items) window.CompletionList.CompletionData.Add(new CompletionItem(item));
        if (caret > result.Start) window.CompletionList.SelectItem(_editor.Document.GetText(result.Start, caret - result.Start));
        window.Closed += (_, _) => _completion = null;
        _completion = window;
        window.Show();
    }

    /// <summary>자동 완성 후보 한 줄: 이름(종류마다 다른 색)과 설명(모양 + 한 줄 설명).</summary>
    private sealed class CompletionItem(ScriptCompletionItem item) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => item.Name;
        public object Content => new TextBlock
        {
            Text = item.Name,
            Foreground = item.Kind switch
            {
                ScriptApiKind.Function => AppTheme.Brush(Color.FromRgb(220, 220, 170)),
                ScriptApiKind.Object => AppTheme.Brush(Color.FromRgb(78, 201, 176)),
                ScriptApiKind.Key => AppTheme.Brush(Color.FromRgb(156, 220, 254)),
                ScriptApiKind.Literal => AppTheme.Brush(Color.FromRgb(214, 157, 112)),
                ScriptApiKind.Keyword => AppTheme.Brush(Color.FromRgb(197, 134, 192)),
                _ => AppTheme.TextBrush,
            },
        };
        public object Description => item.Summary.Length > 0 ? item.Detail + "\n" + item.Summary : item.Detail;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => textArea.Document.Replace(completionSegment, Text);
    }

    // ───────────── 매개변수 안내 ─────────────

    private void UpdateSignature()
    {
        var help = ScriptCompletion.Signature(_editor.Text, _editor.CaretOffset);
        if (help is null)
        {
            _insight?.Close();
            return;
        }
        if (_insight is not null && _signature is not null && _signature.Same(help))
        {
            _signature.Update(help);
            return;
        }
        _insight?.Close();
        _signature = new SignatureProvider(help);
        var window = new OverloadInsightWindow(_editor.TextArea) { Provider = _signature, StartOffset = help.OpenParen + 1 };
        window.Closed += (_, _) =>
        {
            if (_insight == window) _insight = null;
        };
        _insight = window;
        window.Show();
    }

    /// <summary>매개변수 안내 내용: 함수 모양(지금 인자는 굵게)과 한 줄 설명.</summary>
    private sealed class SignatureProvider(ScriptSignatureHelp help) : IOverloadProvider
    {
        private ScriptSignatureHelp _help = help;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Same(ScriptSignatureHelp other) => other.OpenParen == _help.OpenParen && other.Member == _help.Member;

        public void Update(ScriptSignatureHelp help)
        {
            if (help.Parameter == _help.Parameter) return;
            _help = help;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
        }

        public int SelectedIndex { get => 0; set { } }
        public int Count => 1;
        public string? CurrentIndexText => null;

        public object CurrentHeader
        {
            get
            {
                // "lerp(a, b, t)": 괄호 안 지금 인자만 굵게
                string signature = _help.Member.Signature;
                var block = new TextBlock { FontFamily = MonoFont, Foreground = AppTheme.TextBrush };
                int open = signature.IndexOf('('), close = signature.IndexOf(')', Math.Max(0, open));
                if (open < 0 || close < 0)
                {
                    block.Text = signature;
                    return block;
                }
                block.Inlines!.Add(new Avalonia.Controls.Documents.Run(signature[..(open + 1)]));
                var parts = signature[(open + 1)..close].Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) block.Inlines.Add(new Avalonia.Controls.Documents.Run(","));
                    var run = new Avalonia.Controls.Documents.Run(parts[i]);
                    if (i == _help.Parameter) run.FontWeight = FontWeight.Bold;
                    block.Inlines.Add(run);
                }
                block.Inlines.Add(new Avalonia.Controls.Documents.Run(signature[close..]));
                return block;
            }
        }

        public object CurrentContent => new TextBlock
        {
            Text = _help.Member.Summary, MaxWidth = 420, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush,
        };
    }

    // ───────────── 설명 풍선 ─────────────

    private void OnHover(object? sender, PointerEventArgs e)
    {
        if (_editor.GetPositionFromPoint(e.GetPosition(_editor)) is not { } position) return;
        int offset = _editor.Document.GetOffset(position.Line, position.Column);
        string? tip = _errors.Errors.FirstOrDefault(error => error.Line == position.Line) is { } lineError
            ? ScriptStatusText.Error(lineError)
            : ScriptCompletion.Describe(_editor.Text, offset) is { } member ? member.Signature + "\n" + member.Summary : null;
        if (tip is null) return;
        ToolTip.SetTip(_editor, tip);
        ToolTip.SetIsOpen(_editor, true);
        e.Handled = true;
    }

    // ───────────── 오류 밑줄 ─────────────

    /// <summary>오류 자리 아래 빨간 물결선(그 자리의 이름 끝까지, 이름이 아니면 한 글자).</summary>
    private sealed class ErrorUnderlines : IBackgroundRenderer
    {
        private static readonly IPen Pen = new Pen(AppTheme.UnsavedBrush, 1.2);

        public IReadOnlyList<ScriptError> Errors { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var doc = textView.Document;
            if (doc is null || Errors.Count == 0) return;
            foreach (var error in Errors)
            {
                if (error.Line > doc.LineCount) continue;
                var line = doc.GetLineByNumber(error.Line);
                int start = line.Offset + Math.Clamp(error.Column - 1, 0, line.Length);
                int end = start;
                while (end < line.EndOffset && (char.IsLetterOrDigit(doc.GetCharAt(end)) || doc.GetCharAt(end) is '_' or '$')) end++;
                if (end == start) end = Math.Min(line.EndOffset, start + 1);
                if (end == start && start > line.Offset) start--;
                if (end == start) continue;
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = start, Length = end - start }))
                {
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        double y = rect.Bottom - 1, x = rect.Left;
                        ctx.BeginFigure(new Avalonia.Point(x, y), false);
                        bool up = true;
                        for (x += 2; x <= rect.Right; x += 2, up = !up) ctx.LineTo(new Avalonia.Point(x, up ? y - 2 : y));
                    }
                    drawingContext.DrawGeometry(null, Pen, geometry);
                }
            }
        }
    }
}
