using Chickxel.App.Localization;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>
/// 효과 만들기의 "미리보기 자리·피벗" 창(원문 선언의 <c>previewPivot</c>·<c>previewEndOffset</c>·<c>previewEndPivot</c>): 병아리 피벗, 끝 그림 병아리 자리,
/// 끝 그림 병아리 피벗을 X·Y(±<see cref="PreviewPlacement.MaxOffset"/>px)로 보인다. 끝 그림 병아리가 없으면 끝 값 줄은 흐리게 꺼져 있다(값은 그대로 둠).
/// 확인하면 표의 값을 돌려준다.
/// </summary>
public sealed class PreviewPlacementDialog : DialogBase
{
    private readonly (NumericUpDown X, NumericUpDown Y) _pivot, _endOffset, _endPivot;

    private PreviewPlacementDialog(PreviewPlacement placement, bool hasEnd) : base(UiStrings.EffectMaker_PreviewPlacement)
    {
        this.HelpAt("screen-effect-maker");
        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto") };
        void Add(Control cell, int row, int column)
        {
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            table.Children.Add(cell);
        }
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] headers = ["", "X", "Y"];
        for (int c = 0; c < headers.Length; c++)
        {
            var header = Ui.Label(headers[c], bold: true);
            header.HorizontalAlignment = HorizontalAlignment.Center;
            Add(header, 0, c);
        }
        (NumericUpDown X, NumericUpDown Y) Row(string name, Point value, bool enabled)
        {
            int row = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = Ui.Label(name);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 8, 0);
            var x = Ui.Num(-PreviewPlacement.MaxOffset, PreviewPlacement.MaxOffset, value.X, 64);
            var y = Ui.Num(-PreviewPlacement.MaxOffset, PreviewPlacement.MaxOffset, value.Y, 64);
            label.IsEnabled = x.IsEnabled = y.IsEnabled = enabled;
            label.Opacity = enabled ? 1 : 0.55;
            Add(label, row, 0);
            Add(x, row, 1);
            Add(y, row, 2);
            return (x, y);
        }
        _pivot = Row(UiStrings.EffectMaker_PreviewPivot, placement.Pivot, true);
        _endOffset = Row(UiStrings.EffectMaker_PreviewEndOffset, placement.EndOffset, hasEnd);
        _endPivot = Row(UiStrings.EffectMaker_PreviewEndPivot, placement.EndPivot, hasEnd);

        Body.Children.Add(table);
        Body.Children.Add(new TextBlock
        {
            Text = UiStrings.EffectMaker_PreviewPlacementHint, Width = 320, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize,
        });
    }

    /// <summary>자리·피벗 요약(효과 만들기 단추 풍선): 0이 아닌 값마다 "이름 (x, y)", 모두 0이면 "없음".</summary>
    public static string Describe(PreviewPlacement placement)
    {
        var parts = new List<string>();
        void Part(string name, Point value)
        {
            if (!value.IsEmpty) parts.Add(UiStrings.FormatEffectMaker_PreviewPlacementPart(name: name, x: value.X, y: value.Y));
        }
        Part(UiStrings.EffectMaker_PreviewPivot, placement.Pivot);
        Part(UiStrings.EffectMaker_PreviewEndOffset, placement.EndOffset);
        Part(UiStrings.EffectMaker_PreviewEndPivot, placement.EndPivot);
        return parts.Count == 0 ? UiStrings.EffectMaker_PreviewMotionNone : string.Join(", ", parts);
    }

    private static Point Value((NumericUpDown X, NumericUpDown Y) row) => new((int)row.X.NumValue(), (int)row.Y.NumValue());

    private PreviewPlacement Result => new(Value(_endOffset), Value(_pivot), Value(_endPivot));

    /// <summary>
    /// 미리보기 자리·피벗 창을 연다(<paramref name="hasEnd"/> = 끝 그림 병아리를 골랐는지). 확인하면 표의 값(모두 0이면 <see cref="PreviewPlacement.None"/>), 취소하면 null.
    /// </summary>
    public static PreviewPlacement? Ask(Visual? owner, PreviewPlacement placement, bool hasEnd)
    {
        var dialog = new PreviewPlacementDialog(placement, hasEnd);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        var result = dialog.Result;
        return result.IsNone ? PreviewPlacement.None : result;
    }
}
