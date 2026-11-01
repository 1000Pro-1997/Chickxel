using System.Globalization;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>
/// 효과 프리셋(레시피) 저장 창: 이름과 미리보기 병아리(<see cref="PreviewChicks"/>, 번호 = 목록 차례 + 1).
/// 고른 병아리는 레시피에 남아 온라인에 올릴 때 패키지 미리보기(<see cref="AssetPreview.Package"/>)가 된다.
/// </summary>
public sealed class RecipeSaveDialog : DialogBase
{
    private const int ChickScale = 2;

    private readonly AutoCompleteBox _name;
    private readonly ListBox _chicks;

    private RecipeSaveDialog(string name, string chick, IEnumerable<string> names) : base(UiStrings.EffectPreset_SaveTitle)
    {
        _name = new AutoCompleteBox
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinimumPrefixLength = 0,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            ItemsSource = names.ToList(),
            Text = name,
        };
        _chicks = new ListBox
        {
            Width = 420,
            MaxHeight = 360,
            Background = Brushes.Transparent,
            Foreground = AppTheme.TextBrush,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_chicks, ScrollBarVisibility.Disabled);
        for (int i = 0; i < PreviewChicks.Ids.Count; i++) _chicks.Items.Add(ChickItem(PreviewChicks.Ids[i], i + 1));
        _chicks.SelectedIndex = Math.Max(0, PreviewChicks.Ids.ToList().IndexOf(chick));

        Body.Children.Add(Ui.Label(UiStrings.EffectPreset_NamePrompt));
        Body.Children.Add(_name);
        Body.Children.Add(Ui.Label(UiStrings.EffectPreset_PreviewChick, bold: true));
        Body.Children.Add(_chicks);
        Body.Children.Add(new TextBlock
        {
            Text = UiStrings.EffectPreset_PreviewChickHint, Width = 420, TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize,
        });
        Opened += (_, _) =>
        {
            if (_name.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        };
    }

    public string Value => _name.Text?.Trim() ?? "";

    public string Chick => PreviewChicks.Ids[Math.Max(0, _chicks.SelectedIndex)];

    /// <summary>이름과 병아리를 묻는다. 취소하거나 이름이 비었으면 null.</summary>
    public static (string Name, string Chick)? Ask(Visual? owner, string name, string chick, IEnumerable<string> names)
    {
        var dialog = new RecipeSaveDialog(name, chick, names);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Value.Length > 0 ? (dialog.Value, dialog.Chick) : null;
    }

    private static ListBoxItem ChickItem(string id, int number)
    {
        var image = PreviewChicks.Load(id);
        var view = new Image
        {
            Source = ImageUtil.ToBitmap(image), Width = image.Width * ChickScale, Height = image.Height * ChickScale, Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        };
        RenderOptions.SetBitmapInterpolationMode(view, BitmapInterpolationMode.None);
        var item = new ListBoxItem
        {
            Width = 64, Padding = new Thickness(4),
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Border { Height = 20 * ChickScale, Child = view },
                    new TextBlock { Text = number.ToString(CultureInfo.CurrentCulture), HorizontalAlignment = HorizontalAlignment.Center, Foreground = AppTheme.DimTextBrush },
                },
            },
        };
        ToolTip.SetTip(item, string.Format(CultureInfo.CurrentCulture, UiStrings.EffectPreset_PreviewChickTip, number));
        return item;
    }
}
