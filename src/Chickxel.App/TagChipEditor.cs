using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>
/// 태그 칩 편집기(게시 창, 효과 만들기): <c>#neon</c>을 적고 Space·Enter·쉼표(또는 <c>#a #b</c> 붙여 넣기)로 칩을 더한다.
/// 칩은 코드 조각처럼 고정폭 글자와 옅은 둥근 바탕으로 <c>#neon</c>을 보이고, X 단추로 뺀다. 빈 입력에서 Backspace는 마지막 칩을 뺀다.
/// 태그는 소문자로, <c>#</c> 없이 둔다(<see cref="AssetPublishCheck.ParseTags"/>). 규칙(개수·길이·글자)에 맞지 않는 칩은 경고색으로 보이고 아래에 까닭을 적는다.
/// </summary>
public sealed class TagChipEditor : UserControl
{
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");
    // 도움말 창의 코드 조각과 같은 바탕.
    private static readonly IBrush CodeBrush = AppTheme.Brush(Color.FromRgb(0x3A, 0x3B, 0x44));
    private static readonly char[] Separators = [' ', ',', '\t', '\n', '\r'];

    private readonly List<string> _tags = [];
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox _input;
    private readonly TextBlock _problem;
    private bool _splitting;

    /// <summary>칩을 더하거나 뺐을 때(코드로 <see cref="SetTags"/>한 때는 빼고).</summary>
    public event EventHandler? TagsChanged;

    public TagChipEditor(double width)
    {
        _input = new TextBox
        {
            MinWidth = 60, Margin = new Thickness(1), Padding = new Thickness(2, 1), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Watermark = UiStrings.TagChips_Hint, MaxLength = 200, VerticalAlignment = VerticalAlignment.Center,
        };
        // 테마의 포커스 테두리·바탕 대신 바깥 상자가 입력 칸처럼 보인다.
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _input.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        _input.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        _input.TextChanged += (_, _) => SplitTyped();
        _input.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        _input.LostFocus += (_, _) => Commit();
        _problem = new TextBlock
        {
            Foreground = AppTheme.WarningBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 2, 2, 0),
        };
        var box = new Border
        {
            Child = _chips, Background = AppTheme.InputBrush, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 2), MinHeight = 28, Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        // 칩 밖 빈 곳을 눌러도 입력 칸으로.
        box.PointerPressed += (_, e) =>
        {
            if (e.Source == box || e.Source == _chips) _input.Focus();
        };
        Width = width;
        Margin = new Thickness(3);
        Content = new StackPanel { Children = { box, _problem } };
        _chips.Children.Add(_input);
        Rebuild();
    }

    /// <summary>지금 태그(소문자, <c>#</c> 없음, 더한 차례). 규칙에 맞지 않는 것도 들어 있다(<see cref="IsValid"/>).</summary>
    public IReadOnlyList<string> Tags => _tags;

    /// <summary>모든 태그가 규칙(개수·길이·글자, <see cref="AssetPublishCheck.IsValidTags"/>)에 맞는지.</summary>
    public bool IsValid => AssetPublishCheck.IsValidTags(_tags);

    /// <summary>태그를 바꾼다(<see cref="TagsChanged"/>는 일어나지 않음, 입력 중이던 글은 그대로).</summary>
    public void SetTags(IEnumerable<string> tags)
    {
        var parsed = AssetPublishCheck.ParseTags(string.Join(' ', tags));
        if (parsed.SequenceEqual(_tags)) return;
        _tags.Clear();
        _tags.AddRange(parsed);
        Rebuild();
    }

    /// <summary>입력 칸에 남은 글을 칩으로 만든다(포커스를 잃을 때, Enter).</summary>
    public void Commit()
    {
        string text = _input.Text ?? "";
        if (text.Trim().Length == 0) return;
        _splitting = true;
        _input.Text = "";
        _splitting = false;
        Add(text);
    }

    private void SplitTyped()
    {
        if (_splitting) return;
        string text = _input.Text ?? "";
        int last = text.LastIndexOfAny(Separators);
        if (last < 0) return;
        _splitting = true;
        _input.Text = text[(last + 1)..];
        _input.CaretIndex = _input.Text.Length;
        _splitting = false;
        Add(text[..last]);
    }

    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (_input.Text ?? "").Trim().Length > 0)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(_input.Text) && _tags.Count > 0)
        {
            Remove(_tags[^1]);
            e.Handled = true;
        }
    }

    private void Add(string text)
    {
        bool changed = false;
        foreach (string tag in AssetPublishCheck.ParseTags(text))
        {
            if (_tags.Contains(tag)) continue;
            _tags.Add(tag);
            changed = true;
        }
        if (!changed) return;
        Rebuild();
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(string tag)
    {
        if (!_tags.Remove(tag)) return;
        Rebuild();
        TagsChanged?.Invoke(this, EventArgs.Empty);
        _input.Focus();
    }

    /// <summary>칩을 다시 만든다. 입력 칸은 빼지 않고 칩만 그 앞에 바꿔 넣는다(치는 중에 포커스를 잃지 않게).</summary>
    private void Rebuild()
    {
        _chips.Children.RemoveAll(_chips.Children.Where(child => child != _input).ToList());
        for (int i = 0; i < _tags.Count; i++)
            _chips.Children.Insert(i, Chip(_tags[i], RemoteCatalogFormat.IsValidTag(_tags[i]) && i < RemoteCatalogFormat.MaxTags));
        _input.Watermark = _tags.Count == 0 ? UiStrings.TagChips_Hint : null;
        string? problem = _tags.FirstOrDefault(tag => !RemoteCatalogFormat.IsValidTag(tag)) is { } bad
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.TagChips_Invalid, bad, RemoteCatalogFormat.MaxTagLength)
            : _tags.Count > RemoteCatalogFormat.MaxTags ? string.Format(CultureInfo.CurrentCulture, UiStrings.TagChips_TooMany, RemoteCatalogFormat.MaxTags)
            : null;
        _problem.Text = problem ?? "";
        _problem.IsVisible = problem is not null;
    }

    /// <summary>칩 하나: 코드 조각 모양의 <c>#태그</c>와 빼기 단추(X 아이콘).</summary>
    private Border Chip(string tag, bool valid)
    {
        IBrush brush = valid ? AppTheme.TextBrush : AppTheme.WarningBrush;
        var remove = new Button
        {
            Content = new IconView(VectorIcon.X, AppTheme.DimTextBrush, 2) { Width = 9, Height = 9 },
            Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(string.Format(CultureInfo.CurrentCulture, UiStrings.TagChips_Remove, tag));
        remove.Click += (_, _) => Remove(tag);
        return new Border
        {
            Background = CodeBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 3, 2), Margin = new Thickness(1, 1, 3, 1),
            BorderBrush = valid ? null : AppTheme.WarningBrush, BorderThickness = new Thickness(valid ? 0 : 1), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new TextBlock { Text = "#" + tag, FontFamily = MonoFont, Foreground = brush, VerticalAlignment = VerticalAlignment.Center }, remove },
            },
        };
    }
}
