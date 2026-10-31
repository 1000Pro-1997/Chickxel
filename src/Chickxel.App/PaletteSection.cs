using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>팔레트와 색 고르기 창에서 함께 쓰는 팔레트 머리글·색 칸.</summary>
internal sealed class PaletteSection : StackPanel
{
    public string PaletteName { get; set; }
    public bool Collapsed { get; set; }
    public SwatchGrid Grid { get; }
    public Border Header { get; }
    public Border ArrowButton { get; }
    public IconView Arrow { get; }
    public TextBlock Title { get; }
    public TextBlock Count { get; }
    public Button MenuButton { get; }
    public StackPanel Root => this;

    public PaletteSection(string name, IEnumerable<Color32> colors, bool collapsed, bool editable)
    {
        PaletteName = name;
        Collapsed = collapsed;
        Orientation = Orientation.Vertical;
        Margin = new Thickness(0, 0, 0, 4);
        Grid = new SwatchGrid { Cell = 14, ShowMarks = true, Margin = new Thickness(0), FillHeight = true, MinHeight = 20 };
        Grid.SetColors(colors);
        Arrow = new IconView(VectorIcon.ChevronRight, AppTheme.DimTextBrush)
        {
            Width = 12,
            Height = 12,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ArrowButton = new Border
        {
            Width = 16,
            Child = Arrow,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(ArrowButton, UiStrings.S_PaletteSection_PaletteSection_5C40417A3E);
        Title = new TextBlock
        {
            Foreground = AppTheme.TextBrush,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 4, 0),
        };
        Count = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
        };
        MenuButton = Ui.IconButton(VectorIcon.Menu, UiStrings.S_PaletteSection_PaletteSection_003DA891E2, null, 26, 13);
        MenuButton.Height = 20;
        MenuButton.Margin = new Thickness(1);
        MenuButton.VerticalAlignment = VerticalAlignment.Center;
        MenuButton.IsVisible = editable;
        var row = new DockPanel();
        DockPanel.SetDock(ArrowButton, Dock.Left);
        DockPanel.SetDock(MenuButton, Dock.Right);
        DockPanel.SetDock(Count, Dock.Right);
        row.Children.Add(ArrowButton);
        row.Children.Add(MenuButton);
        row.Children.Add(Count);
        row.Children.Add(Title);
        Header = new Border
        {
            Height = 24,
            Background = AppTheme.BackBrush,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(2, 0, 0, 0),
            Child = row,
        };
        ToolTip.SetTip(Title, editable ? UiStrings.S_PaletteSection_PaletteSection_B4F1EEF45C : UiStrings.S_PaletteSection_PaletteSection_CF481ADEDA);
        Children.Add(Header);
        Children.Add(Grid);
        Refresh(false);
    }

    public void Refresh(bool active)
    {
        Title.Text = PaletteName;
        Count.Text = Grid.Colors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Arrow.Icon = Collapsed ? VectorIcon.ChevronRight : VectorIcon.ChevronDown;
        Grid.IsVisible = !Collapsed;
        Header.BorderBrush = active ? AppTheme.AccentBrush : AppTheme.BackBrush;
    }
}
