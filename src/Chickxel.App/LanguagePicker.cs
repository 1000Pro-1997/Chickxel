using System.Globalization;
using Avalonia.Controls.Templates;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 언어 설정 창(효과 만들기의 <see cref="ScriptTranslationsDialog"/>, 게시 창의 <see cref="PackageTranslationsDialog"/>)의 언어 고르기 칸:
/// 흔한 언어 목록과 "다른 언어 코드"(코드 직접 적기). 이미 글이 있는 언어(<see cref="SetFilled"/>)는 목록과 고른 칸 모두 초록 글자와 체크 아이콘으로 보인다.
/// </summary>
internal sealed class LanguagePicker
{
    /// <summary>고르기 쉬운 언어(코드, 그 언어로 쓴 이름). 이름은 그 언어 자신의 표기라 화면 언어와 상관없이 같다.</summary>
    public static readonly (string Code, string Native)[] CommonLanguages =
    [
        ("en", "English"), ("ja", "日本語"), ("zh-Hans", "简体中文"), ("zh-Hant", "繁體中文"), ("ko", "한국어"), ("es", "Español"), ("fr", "Français"),
        ("de", "Deutsch"), ("pt", "Português"), ("ru", "Русский"), ("id", "Bahasa Indonesia"), ("vi", "Tiếng Việt"), ("th", "ไทย"),
    ];

    private readonly ComboBox _language;
    private readonly TextBox _code;
    private readonly Item[] _items;

    /// <summary>고른 언어 코드가 바뀌었을 때(목록에서 고르거나 코드를 고쳐 적음).</summary>
    public event EventHandler? CodeChanged;

    public LanguagePicker(string code)
    {
        _items = [.. CommonLanguages.Select(l => new Item(l.Code, $"{l.Native} ({l.Code})")), new Item(null, UiStrings.ScriptLanguages_OtherCode)];
        _language = new ComboBox { Width = 200, Margin = new Thickness(3), ItemsSource = _items, ItemTemplate = new FuncDataTemplate<Item>((item, _) => ItemView(item)) };
        _code = new TextBox { Width = 120, Margin = new Thickness(3), Watermark = UiStrings.ScriptLanguages_CodeHint, Text = code, MaxLength = 35 };
        int known = Array.FindIndex(CommonLanguages, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        _language.SelectedIndex = known >= 0 ? known : CommonLanguages.Length;
        _code.IsVisible = known < 0;
        _language.SelectionChanged += (_, _) =>
        {
            bool other = _language.SelectedIndex >= CommonLanguages.Length || _language.SelectedIndex < 0;
            _code.IsVisible = other;
            if (!other) _code.Text = CommonLanguages[_language.SelectedIndex].Code;
            CodeChanged?.Invoke(this, EventArgs.Empty);
        };
        _code.TextChanged += (_, _) => CodeChanged?.Invoke(this, EventArgs.Empty);
        View = Ui.Row(_language, _code);
    }

    public Control View { get; }

    public string Code => (_code.Text ?? "").Trim();

    /// <summary>글이 있는 언어 표시를 바꾼다: <paramref name="filled"/>에 든 언어, "다른 언어 코드"는 지금 적은 (흔하지 않은) 코드가 들어 있으면.</summary>
    public void SetFilled(IReadOnlySet<string> filled)
    {
        bool other = !CommonLanguages.Any(l => string.Equals(l.Code, Code, StringComparison.OrdinalIgnoreCase));
        foreach (var item in _items) item.HasContent = item.Code is { } code ? filled.Contains(code) : other && filled.Contains(Code);
    }

    /// <summary>언어 코드의 그 언어 표기 이름(흔한 언어 목록, 없으면 .NET 문화 이름, 그것도 없으면 코드 그대로).</summary>
    public static string NativeName(string code)
    {
        foreach (var (known, native) in CommonLanguages)
            if (string.Equals(known, code, StringComparison.OrdinalIgnoreCase)) return native;
        try
        {
            return CultureInfo.GetCultureInfo(code).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>목록 항목 하나의 모양: 체크 아이콘(글이 있을 때만 보임) + 이름(글이 있으면 초록).</summary>
    private static Control ItemView(Item? item)
    {
        var check = new IconView(VectorIcon.Check, AppTheme.SavedBrush, 2.4) { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = item?.Text ?? "", VerticalAlignment = VerticalAlignment.Center };
        void Apply()
        {
            bool has = item?.HasContent == true;
            check.Opacity = has ? 1 : 0;
            text.Foreground = has ? AppTheme.SavedBrush : AppTheme.TextBrush;
        }
        Apply();
        if (item is not null) item.Changed += (_, _) => Apply();
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { check, text } };
    }

    /// <summary>목록 항목: 흔한 언어(코드) 또는 "다른 언어 코드"(코드 null), 글이 있는지.</summary>
    private sealed class Item(string? code, string text)
    {
        private bool _hasContent;

        public string? Code { get; } = code;
        public string Text { get; } = text;
        public event EventHandler? Changed;

        public bool HasContent
        {
            get => _hasContent;
            set
            {
                if (_hasContent == value) return;
                _hasContent = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public override string ToString() => Text;
    }
}
