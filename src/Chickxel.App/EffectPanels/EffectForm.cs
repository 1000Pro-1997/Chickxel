namespace Chickxel.App;

/// <summary>
/// 효과 설정 칸: 줄마다 왼쪽 이름 열(모든 줄이 같은 폭, 왼쪽 맞춤 — <see cref="LabelMaxWidth"/>까지) + 오른쪽 값 열, 좁은 칸이면 이름 줄 아래 값 줄.
/// 효과 창의 설정 칸과 오른쪽 독의 효과 설정(<see cref="ToolOptionsPanel"/>, 좁은 칸)이 함께 쓴다.
/// </summary>
internal sealed class EffectForm
{
    /// <summary>설명 글(<see cref="LayerEffectsDialog"/>의 Hint)의 표시: 칸에 늘어놓지 않고 <see cref="TakeHints"/>로 모아 정보 표시(i) 풍선에 넣는다.</summary>
    public const string HintClass = "effect-hint";
    /// <summary>이름 열의 크기 공유 묶음 — 모든 줄의 이름 열이 가장 긴 이름에 맞춰 같은 폭이 된다(<see cref="LabelMaxWidth"/>까지).</summary>
    private const string LabelColumn = "EffectSettingsLabel";
    /// <summary>이름 열의 최대 폭. 긴 이름(스크립트 설정 이름 등)은 줄을 바꿔, 값 열(슬라이더·숫자 칸)이 칸 밖으로 밀려 잘리지 않게 한다.</summary>
    private const double LabelMaxWidth = 84;

    /// <param name="panel">줄을 쌓을 칸(이름 열 폭 공유 범위가 됨).</param>
    /// <param name="owner">글꼴 더하기 등 파일 창의 주인.</param>
    /// <param name="stacked">좁은 칸(오른쪽 독)용: 이름을 값 위 줄에 두어 값이 칸 폭을 다 쓴다.</param>
    public EffectForm(StackPanel panel, Visual owner, bool stacked = false)
    {
        Panel = panel;
        Owner = owner;
        _stacked = stacked;
        Grid.SetIsSharedSizeScope(panel, true);
    }

    private readonly bool _stacked;

    public StackPanel Panel { get; }

    public Visual Owner { get; }

    /// <summary>이름 열 없이 칸 전체 폭으로 넣는다.</summary>
    public void Add(Control control) => Panel.Children.Add(control);

    /// <summary>
    /// 한 줄: 이름 열에 <paramref name="label"/>, 값 열에 컨트롤들(모든 줄이 같은 x에서 시작). 이름이 ""면 값 열만. 값 열 줄을 돌려준다.
    /// 좁은 칸이면 값 열이 칸 폭을 넘을 때 다음 줄로 넘긴다.
    /// </summary>
    public Panel Row(string label, params Control[] controls)
    {
        TextBlock? head = null;
        if (label.Length > 0)
        {
            head = Ui.Label(label);
            head.VerticalAlignment = VerticalAlignment.Center;
            head.Margin = new Thickness(head.Margin.Left, head.Margin.Top, head.Margin.Right + 6, head.Margin.Bottom);
        }
        Panel row;
        if (_stacked)
        {
            row = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in controls)
            {
                c.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(c);
            }
        }
        else row = Ui.Row(controls);
        AddRow(head, row);
        return row;
    }

    /// <summary>값 열에 한 덩어리(줄 바꿈 패널 등 — 열 폭에 맞춰 늘어남). 이름은 첫 줄 높이에 맞춘다.</summary>
    public void Block(string label, Control body)
    {
        var head = Ui.Label(label);
        head.VerticalAlignment = VerticalAlignment.Top;
        head.Margin = new Thickness(3, 6, 9, 3);
        AddRow(head, body);
    }

    /// <summary>스크립트 설정 줄 하나(<see cref="LayerEffectsDialog.AddScriptSettingRows"/>의 줄 넣기): 덩어리면 <see cref="Block"/>, 아니면 <see cref="Row"/>.</summary>
    public void ScriptRow(string label, Control editor, bool block)
    {
        if (block) Block(label, editor);
        else Row(label, editor);
    }

    /// <summary>칸에 넣은 설명 글을 빼서 그 글들을 돌려준다(넣은 차례대로).</summary>
    public List<string> TakeHints()
    {
        var hints = Panel.Children.OfType<TextBlock>().Where(t => t.Classes.Contains(HintClass)).ToList();
        foreach (var h in hints) Panel.Children.Remove(h);
        return [.. hints.Select(h => h.Text ?? "")];
    }

    private void AddRow(TextBlock? head, Control body)
    {
        if (_stacked)
        {
            if (head is not null)
            {
                head.Margin = new Thickness(3, 4, 3, 0);
                head.TextWrapping = TextWrapping.Wrap;
                head.HorizontalAlignment = HorizontalAlignment.Left;
                Panel.Children.Add(head);
            }
            body.Margin = new Thickness(body.Margin.Left + 6, body.Margin.Top, body.Margin.Right, body.Margin.Bottom);
            Panel.Children.Add(body);
            return;
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = LabelColumn });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (head is not null)
        {
            head.HorizontalAlignment = HorizontalAlignment.Left;
            head.TextWrapping = TextWrapping.Wrap;
            head.MaxWidth = LabelMaxWidth;
            grid.Children.Add(head);
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        Panel.Children.Add(grid);
    }
}
