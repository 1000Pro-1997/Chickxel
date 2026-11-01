using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 효과 만들기의 "언어 설정" 창: 스크립트 효과 선언의 <c>translations</c>를 언어마다 한 묶음(언어 코드, 제목, 설명, 설정·고르기 항목 이름)으로 고친다.
/// 비워 둔 칸은 원문에 적은 값으로 보이므로 쓰지 않는다. 확인하면 <see cref="Result"/>를 효과 만들기가 <see cref="ScriptDeclarationEdit.SetTranslations"/>로 원문에 쓴다.
/// 글이 있는 언어는 언어 목록에 초록으로 보인다(<see cref="LanguagePicker"/>).
/// </summary>
public sealed class ScriptTranslationsDialog : DialogBase
{
    private const int FieldWidth = 360;

    private readonly ScriptEffectProgram _program;
    private readonly StackPanel _sections = new() { Spacing = 8 };
    private readonly TextBlock _empty;
    private readonly List<Section> _list = [];

    private ScriptTranslationsDialog(ScriptEffectProgram program) : base(UiStrings.ScriptLanguages_Title)
    {
        _program = program;
        _empty = new TextBlock { Text = UiStrings.ScriptLanguages_Empty, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(3, 6) };
        Body.Children.Add(new TextBlock
        {
            Text = UiStrings.ScriptLanguages_Hint, Width = FieldWidth + 160, TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(3, 0, 3, 6),
        });
        Body.Children.Add(new ScrollViewer { Content = Ui.Stack(_empty, _sections), MaxHeight = 520, Width = FieldWidth + 190 });
        Body.Children.Add(Ui.Button(VectorIcon.Plus, UiStrings.ScriptLanguages_Add, (_, _) => AddSection(NextLanguage(), null)));
        foreach (var (code, translation) in program.Translations) AddSection(code, translation);
        UpdateEmpty();
        UpdateFilled();
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
    /// 언어 설정 창을 연다. 확인하면 고친 번역(언어 차례대로 — 모두 비운 언어는 뺌), 취소하면 null.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, ScriptTranslation>>? Ask(Visual? owner, ScriptEffectProgram program)
    {
        var dialog = new ScriptTranslationsDialog(program);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    /// <summary>지금 칸들의 번역(언어 차례, 빈 칸은 뺌, 모두 빈 언어도 뺌).</summary>
    public IReadOnlyList<KeyValuePair<string, ScriptTranslation>> Result
        => _list.Select(section => new KeyValuePair<string, ScriptTranslation>(section.Code, section.Translation()))
            .Where(pair => pair.Value.Name is not null || pair.Value.Description is not null || pair.Value.Labels.Count > 0)
            .ToList();

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

    private void AddSection(string code, ScriptTranslation? translation)
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

    /// <summary>언어 하나의 칸들.</summary>
    private sealed class Section
    {
        private readonly TextBox _name, _description;
        private readonly List<(string Key, TextBox Box)> _labels = [];

        public Border View { get; }

        public LanguagePicker Picker { get; }

        public Section(ScriptTranslationsDialog owner, string code, ScriptTranslation? translation)
        {
            var program = owner._program;
            Picker = new LanguagePicker(code);
            Picker.CodeChanged += (_, _) => owner.UpdateFilled();
            var remove = Ui.IconButton(VectorIcon.Trash, UiStrings.ScriptLanguages_Remove, (_, _) => owner.RemoveSection(this));
            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(remove, Dock.Right);
            header.Children.Add(remove);
            header.Children.Add(Ui.Row(Ui.Label(UiStrings.ScriptLanguages_Language, bold: true), Picker.View));

            _name = new TextBox { Width = FieldWidth, Margin = new Thickness(3), MaxLength = AssetManifest.MaxNameLength, Watermark = program.Name, Text = translation?.Name ?? "" };
            _description = new TextBox
            {
                Width = FieldWidth, Height = 52, Margin = new Thickness(3), TextWrapping = TextWrapping.Wrap, MaxLength = ScriptEffectProgram.MaxDescriptionLength,
                Watermark = program.Description ?? "", Text = translation?.Description ?? "",
            };
            var body = Ui.Stack(header, Row(UiStrings.ScriptLanguages_Name, _name), Row(UiStrings.ScriptLanguages_Description, _description));
            if (program.Settings.Count > 0)
            {
                body.Children.Add(Ui.Label(UiStrings.ScriptLanguages_Labels, bold: true));
                foreach (var setting in program.Settings)
                {
                    body.Children.Add(Row(setting.Label, Label(setting.Key, setting.Label, translation)));
                    foreach (var choice in setting.Choices)
                    {
                        string key = ScriptTranslation.ChoiceKey(setting.Key, choice.Value);
                        body.Children.Add(Row("    " + choice.Label, Label(key, choice.Label, translation)));
                    }
                }
            }
            foreach (var box in new[] { _name, _description }.Concat(_labels.Select(l => l.Box)))
                box.TextChanged += (_, _) => owner.UpdateFilled();
            View = new Border
            {
                Child = body, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6),
            };
        }

        public string Code => Picker.Code;

        /// <summary>이 언어에 적은 글이 하나라도 있는지.</summary>
        public bool HasContent
        {
            get
            {
                var t = Translation();
                return t.Name is not null || t.Description is not null || t.Labels.Count > 0;
            }
        }

        public ScriptTranslation Translation()
        {
            static string? Text(TextBox box) => box.Text?.Trim() is { Length: > 0 } text ? text : null;
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, box) in _labels)
                if (Text(box) is { } label) labels[key] = label;
            return new ScriptTranslation(Text(_name), Text(_description), labels);
        }

        private TextBox Label(string key, string declared, ScriptTranslation? translation)
        {
            var box = new TextBox
            {
                Width = FieldWidth, Margin = new Thickness(3), MaxLength = ScriptTranslations.MaxLabelLength, Watermark = declared,
                Text = translation?.Labels.GetValueOrDefault(key) ?? "",
            };
            _labels.Add((key, box));
            return box;
        }

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
