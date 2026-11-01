using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 게시 창의 "언어 설정" 창: 패키지 이름·짧은 설명을 언어마다 한 묶음(언어 코드, 이름, 짧은 설명)으로 적는다(manifest의 <c>translations</c>).
/// 언어 고르기는 효과 만들기의 언어 설정 창과 같은 칸(<see cref="LanguagePicker"/>, 글이 있는 언어는 초록)이고, 비워 둔 칸은 기본 이름·설명으로 보인다.
/// </summary>
public sealed class PackageTranslationsDialog : DialogBase
{
    private const int FieldWidth = 360;

    private readonly string _name, _summary;
    private readonly StackPanel _sections = new() { Spacing = 8 };
    private readonly TextBlock _empty;
    private readonly List<Section> _list = [];

    private PackageTranslationsDialog(string name, string summary, IEnumerable<KeyValuePair<string, AssetTranslation>> translations)
        : base(UiStrings.ScriptLanguages_Title)
    {
        _name = name;
        _summary = summary;
        _empty = new TextBlock { Text = UiStrings.ScriptLanguages_Empty, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(3, 6) };
        Body.Children.Add(new TextBlock
        {
            Text = UiStrings.PublishLanguages_Hint, Width = FieldWidth + 160, TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(3, 0, 3, 6),
        });
        Body.Children.Add(new ScrollViewer { Content = Ui.Stack(_empty, _sections), MaxHeight = 520, Width = FieldWidth + 190 });
        Body.Children.Add(Ui.Button(VectorIcon.Plus, UiStrings.ScriptLanguages_Add, (_, _) => AddSection(NextLanguage(), null)));
        foreach (var (code, translation) in translations) AddSection(code, translation);
        UpdateEmpty();
        OkButton.Click += (_, _) =>
        {
            if (Problem() is { } problem)
            {
                Ui.Warn(this, problem, UiStrings.ScriptLanguages_Title);
                DialogResult = DialogResult.None;
            }
        };
    }

    /// <summary>
    /// 언어 설정 창을 연다. <paramref name="name"/>·<paramref name="summary"/>는 기본 이름·설명(빈 칸의 흐린 글). 확인하면 언어 차례대로의 번역
    /// (모두 비운 언어는 뺌), 취소하면 null.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, AssetTranslation>>? Ask(Visual? owner, string name, string summary,
        IEnumerable<KeyValuePair<string, AssetTranslation>> translations)
    {
        var dialog = new PackageTranslationsDialog(name, summary, translations);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    /// <summary>지금 칸들의 번역(언어 차례, 빈 칸은 뺌, 모두 빈 언어도 뺌).</summary>
    public IReadOnlyList<KeyValuePair<string, AssetTranslation>> Result
        => _list.Where(section => section.HasContent).Select(section => new KeyValuePair<string, AssetTranslation>(section.Code, section.Translation())).ToList();

    /// <summary>확인할 수 없는 까닭(틀린 코드·겹친 언어), 없으면 null.</summary>
    private string? Problem()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in _list)
        {
            if (!ScriptTranslations.IsLanguageCode(section.Code)) return string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptLanguages_BadCode, section.Code);
            if (!seen.Add(section.Code)) return string.Format(CultureInfo.CurrentCulture, UiStrings.ScriptLanguages_DuplicateCode, section.Code);
        }
        return null;
    }

    /// <summary>아직 없는 흔한 언어 중 첫째(모두 있으면 빈 코드 — 직접 적음).</summary>
    private string NextLanguage()
        => LanguagePicker.CommonLanguages.Select(l => l.Code).FirstOrDefault(code => !_list.Any(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase))) ?? "";

    private void AddSection(string code, AssetTranslation? translation)
    {
        var section = new Section(this, code, translation);
        _list.Add(section);
        _sections.Children.Add(section.View);
        UpdateEmpty();
        UpdateFilled();
    }

    private void RemoveSection(Section section)
    {
        _list.Remove(section);
        _sections.Children.Remove(section.View);
        UpdateEmpty();
        UpdateFilled();
    }

    private void UpdateEmpty() => _empty.IsVisible = _list.Count == 0;

    /// <summary>글이 있는 언어를 모든 언어 목록에 초록으로 보인다.</summary>
    private void UpdateFilled()
    {
        var filled = _list.Where(s => s.HasContent).Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var section in _list) section.Picker.SetFilled(filled);
    }

    /// <summary>언어 하나의 칸들(언어, 이름, 짧은 설명).</summary>
    private sealed class Section
    {
        private readonly TextBox _name, _summary;

        public Border View { get; }

        public LanguagePicker Picker { get; }

        public Section(PackageTranslationsDialog owner, string code, AssetTranslation? translation)
        {
            Picker = new LanguagePicker(code);
            Picker.CodeChanged += (_, _) => owner.UpdateFilled();
            var remove = Ui.IconButton(VectorIcon.Trash, UiStrings.ScriptLanguages_Remove, (_, _) => owner.RemoveSection(this));
            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(remove, Dock.Right);
            header.Children.Add(remove);
            header.Children.Add(Ui.Row(Ui.Label(UiStrings.ScriptLanguages_Language, bold: true), Picker.View));

            _name = new TextBox { Width = FieldWidth, Margin = new Thickness(3), MaxLength = AssetManifest.MaxNameLength, Watermark = owner._name, Text = translation?.Name ?? "" };
            _summary = new TextBox
            {
                Width = FieldWidth, Height = 52, Margin = new Thickness(3), TextWrapping = TextWrapping.Wrap, MaxLength = RemoteCatalogFormat.MaxSummaryLength,
                Watermark = owner._summary, Text = translation?.Summary ?? "",
            };
            _name.TextChanged += (_, _) => owner.UpdateFilled();
            _summary.TextChanged += (_, _) => owner.UpdateFilled();
            View = new Border
            {
                Child = Ui.Stack(header, Row(UiStrings.Publish_Name, _name), Row(UiStrings.Publish_Summary, _summary)),
                BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6),
            };
        }

        public string Code => Picker.Code;

        public bool HasContent => Text(_name) is not null || Text(_summary) is not null;

        public AssetTranslation Translation() => new(Text(_name), Text(_summary));

        private static string? Text(TextBox box) => box.Text?.Trim() is { Length: > 0 } text ? text : null;

        private static Control Row(string label, Control field)
        {
            var text = Ui.Label(label);
            text.Width = 140;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            return Ui.Row(text, field);
        }
    }
}
