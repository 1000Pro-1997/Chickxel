using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>
/// 효과 만들기의 "미리보기 움직임" 창(원문 선언의 <c>previewMotion</c>): 이름(없음·오른쪽으로·왼쪽으로·위로·아래로)이나 직접을 고르고,
/// 미리보기 칸마다 병아리 자리(X·Y, ±<see cref="PreviewMotion.MaxOffset"/>)를 표로 보인다. 이름을 고르면 표는 그 자리로 채워져 흐리게 보이고,
/// 표의 값을 고치면 직접으로 바뀐다(직접은 지금 보이는 자리에서 시작). 확인하면 <see cref="PreviewMotion.Simplest"/>로 줄인 움직임을 돌려준다.
/// </summary>
public sealed class PreviewMotionDialog : DialogBase
{
    /// <summary>고르기 상자의 직접 항목 번호(<see cref="PreviewMotion.Presets"/> 다음).</summary>
    private static readonly int CustomIndex = PreviewMotion.Presets.Count;

    private readonly int _steps;
    private readonly ComboBox _choice;
    private readonly List<(NumericUpDown X, NumericUpDown Y)> _rows = [];
    private bool _syncing;

    private PreviewMotionDialog(PreviewMotion motion, int steps) : base(UiStrings.EffectMaker_PreviewMotion)
    {
        _steps = Math.Max(1, steps);
        // 대상 도움말(F1): 창 = "효과 만들기"의 미리보기 움직임 그림, 고르기 줄 = ⑯, 칸마다 자리 표 = ⑰(같은 문단)
        this.HelpAt("screen-effect-maker", shot: "screen-effect-maker-3");
        _choice = Ui.Combo(140, [.. PreviewMotion.Presets.Select(Describe), UiStrings.EffectMaker_PreviewMotionCustom]);
        var choiceLabel = Ui.Label(UiStrings.EffectMaker_PreviewMotionChoice);
        choiceLabel.VerticalAlignment = VerticalAlignment.Center;
        var choiceRow = Ui.Row(choiceLabel, _choice).HelpAt("screen-effect-maker", anchor: "⑯ None, Right, Left");

        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        void Add(Control cell, int row, int column)
        {
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            table.Children.Add(cell);
        }
        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] headers = [UiStrings.EffectMaker_PreviewMotionFrame, "X", "Y"];
        for (int c = 0; c < headers.Length; c++)
        {
            var header = Ui.Label(headers[c], bold: true);
            header.HorizontalAlignment = HorizontalAlignment.Center;
            Add(header, 0, c);
        }
        for (int k = 0; k < _steps; k++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var number = Ui.Label((k + 1).ToString(CultureInfo.CurrentCulture));
            number.VerticalAlignment = VerticalAlignment.Center;
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.Margin = new Thickness(8, 0);
            var offset = motion.OffsetAt(k, _steps);
            var x = Ui.Num(-PreviewMotion.MaxOffset, PreviewMotion.MaxOffset, offset.X, 64);
            var y = Ui.Num(-PreviewMotion.MaxOffset, PreviewMotion.MaxOffset, offset.Y, 64);
            x.ValueChanged += (_, _) => OffsetEdited();
            y.ValueChanged += (_, _) => OffsetEdited();
            Add(number, k + 1, 0);
            Add(x, k + 1, 1);
            Add(y, k + 1, 2);
            _rows.Add((x, y));
        }
        var scroll = new ScrollViewer { Content = table, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            .HelpAt("screen-effect-maker", anchor: "⑰ the table");

        Body.Children.Add(choiceRow);
        Body.Children.Add(scroll);
        Body.Children.Add(new TextBlock
        {
            Text = UiStrings.EffectMaker_PreviewMotionHint, Width = 300, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize,
        });

        int index = motion.Preset is { } name ? Math.Max(0, PreviewMotion.Presets.ToList().IndexOf(name)) : CustomIndex;
        _choice.SelectedIndex = index;
        ShowChoice();
        _choice.SelectionChanged += (_, _) => ShowChoice();
    }

    /// <summary>움직임 이름(창의 고르기 항목·효과 만들기 단추 풍선): 이름이면 그 이름, 목록이면 "직접".</summary>
    public static string Describe(PreviewMotion motion) => motion.Preset is { } name ? Describe(name) : UiStrings.EffectMaker_PreviewMotionCustom;

    private static string Describe(string preset) => preset switch
    {
        "right" => UiStrings.EffectMaker_PreviewMotionRight,
        "left" => UiStrings.EffectMaker_PreviewMotionLeft,
        "up" => UiStrings.EffectMaker_PreviewMotionUp,
        "down" => UiStrings.EffectMaker_PreviewMotionDown,
        _ => UiStrings.EffectMaker_PreviewMotionNone,
    };

    /// <summary>고른 이름의 자리로 표를 채우고 흐리게(직접이면 지금 값 그대로 또렷하게) 보인다.</summary>
    private void ShowChoice()
    {
        bool custom = _choice.SelectedIndex >= CustomIndex || _choice.SelectedIndex < 0;
        if (!custom && PreviewMotion.FromPreset(PreviewMotion.Presets[_choice.SelectedIndex]) is { } preset)
        {
            _syncing = true;
            for (int k = 0; k < _rows.Count; k++)
            {
                var offset = preset.OffsetAt(k, _steps);
                _rows[k].X.Value = offset.X;
                _rows[k].Y.Value = offset.Y;
            }
            _syncing = false;
        }
        foreach (var (x, y) in _rows) x.Opacity = y.Opacity = custom ? 1 : 0.55;
    }

    /// <summary>표의 값을 고치면 직접으로 바꾼다(값은 그대로).</summary>
    private void OffsetEdited()
    {
        if (_syncing || _choice.SelectedIndex == CustomIndex) return;
        _choice.SelectedIndex = CustomIndex;
    }

    /// <summary>고른 움직임: 이름이면 그 이름, 직접이면 표의 자리를 줄인 것(<see cref="PreviewMotion.Simplest"/> — 이름과 같으면 이름, 모두 0이면 없음).</summary>
    private PreviewMotion Result
        => _choice.SelectedIndex is var i && i >= 0 && i < CustomIndex
            ? PreviewMotion.FromPreset(PreviewMotion.Presets[i])!
            : PreviewMotion.Simplest([.. _rows.Select(r => new Point((int)r.X.NumValue(), (int)r.Y.NumValue()))], _steps);

    /// <summary>미리보기 움직임 창을 연다(<paramref name="steps"/> = 미리보기 칸 수). 확인하면 고른 움직임, 취소하면 null.</summary>
    public static PreviewMotion? Ask(Visual? owner, PreviewMotion motion, int steps)
    {
        var dialog = new PreviewMotionDialog(motion, steps);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }
}
