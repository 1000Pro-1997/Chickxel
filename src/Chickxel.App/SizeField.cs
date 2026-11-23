using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 가로 × 세로 크기 입력(공용 — 크기를 받는 곳은 모두 이것을 쓴다): [가로] × [세로] (단위) [사슬].
/// 사슬(비율 고정)을 켜면 한쪽을 고칠 때 다른 쪽이 같은 비율로 따라간다(반올림해 범위 안으로). 비율은 켤 때의 가로 ÷ 세로이고
/// (<see cref="Size"/>로 값을 통째로 바꾸면 그 값으로 다시 잡음), <see cref="RatioSource"/>를 주면 그 비율(예: 원본 그림 비율)을 쓴다.
/// 숫자 상자는 <see cref="WidthBox"/>·<see cref="HeightBox"/>로 열어 두어 우클릭 예시·최근 값·풍선 도움말·대상 도움말을 그대로 붙인다.
/// </summary>
public sealed class SizeField : StackPanel
{
    private readonly IconView _linkIcon = new(VectorIcon.Unlink) { Width = 14, Height = 14, Margin = new Thickness(0, 1) };
    /// <summary>사슬을 켤 때(또는 <see cref="Size"/>를 바꿀 때) 잡은 가로 ÷ 세로.</summary>
    private double _ratio = 1;
    /// <summary>사슬 때문에 반대쪽을 고치는 중(그 변경은 따로 알리지 않음).</summary>
    private bool _following;

    /// <param name="min">두 상자의 최솟값.</param>
    /// <param name="max">가로 최댓값(세로도 같음, <paramref name="maxHeight"/>를 주면 세로만 그것).</param>
    /// <param name="unit">상자 메뉴 단위이자 세로 상자 뒤에 붙는 글(null = 없음).</param>
    /// <param name="link">사슬 단추를 둘지.</param>
    public SizeField(decimal min, decimal max, int width, int height, int boxWidth = 64, string? unit = "px", bool linked = false,
        bool link = true, decimal? maxHeight = null)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        WidthBox = Ui.Num(min, max, width, boxWidth).Tip(UiStrings.SizeField_Width);
        HeightBox = Ui.Num(min, maxHeight ?? max, height, boxWidth).Tip(UiStrings.SizeField_Height);
        if (unit is not null)
        {
            WidthBox.Unit(unit);
            HeightBox.Unit(unit);
        }
        Children.Add(WidthBox);
        Children.Add(Ui.Label("×"));
        Children.Add(HeightBox);
        if (unit is not null) Children.Add(Ui.Label(unit));
        LinkButton = new ToggleButton
        {
            Content = _linkIcon,
            Margin = new Thickness(2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false,
            IsVisible = link,
        }.Tip(UiStrings.SizeField_LinkTip);
        Children.Add(LinkButton);
        WidthBox.ValueChanged += (_, _) => Edited(width: true);
        HeightBox.ValueChanged += (_, _) => Edited(width: false);
        LinkButton.IsCheckedChanged += (_, _) =>
        {
            _linkIcon.Icon = LinkButton.On() ? VectorIcon.Link : VectorIcon.Unlink;
            if (LinkButton.On()) Capture();
            LinkChanged?.Invoke(this);
        };
        Linked = linked;
    }

    public NumericUpDown WidthBox { get; }
    public NumericUpDown HeightBox { get; }
    /// <summary>사슬(비율 고정) 단추. 켜짐 = 사슬 아이콘, 꺼짐 = 끊긴 사슬.</summary>
    public ToggleButton LinkButton { get; }

    /// <summary>
    /// 지금 크기(빈 상자는 최솟값). 넣으면 두 상자를 그대로 바꾸고(사슬로 따라 바꾸지 않음) 사슬 비율도 이 값으로 다시 잡는다.
    /// <see cref="Changed"/>는 바뀐 상자마다 알린다.
    /// </summary>
    public PixelSize Size
    {
        get => new((int)WidthBox.NumValue(), (int)HeightBox.NumValue());
        set
        {
            _following = true;
            try
            {
                WidthBox.Value = Math.Clamp(value.Width, WidthBox.Minimum, WidthBox.Maximum);
                HeightBox.Value = Math.Clamp(value.Height, HeightBox.Minimum, HeightBox.Maximum);
            }
            finally
            {
                _following = false;
            }
            Capture();
            Changed?.Invoke(this, true);
        }
    }

    /// <summary>사슬(비율 고정)을 켰는지.</summary>
    public bool Linked
    {
        get => LinkButton.On();
        set => LinkButton.IsChecked = value;
    }

    /// <summary>사슬이 켜졌을 때 지킬 가로 ÷ 세로를 주는 함수(null이면 켤 때의 비율, 함수가 null을 주면 따라 바꾸지 않음).</summary>
    public Func<double?>? RatioSource { get; set; }

    /// <summary>값이 바뀜(true = 가로를 고침). 사슬로 반대쪽이 따라 바뀐 것은 고친 쪽 알림 하나로 끝난다.</summary>
    public event Action<SizeField, bool>? Changed;

    /// <summary>사슬을 켜거나 끔.</summary>
    public event Action<SizeField>? LinkChanged;

    /// <summary>두 상자의 우클릭 예시 값.</summary>
    public SizeField Presets(params decimal[] values)
    {
        WidthBox.Presets(values);
        HeightBox.Presets(values);
        return this;
    }

    /// <summary>두 상자가 함께 쓰는 최근 값 묶음 이름.</summary>
    public SizeField RecentKey(string key)
    {
        WidthBox.RecentKey(key);
        HeightBox.RecentKey(key);
        return this;
    }

    /// <summary>사슬 단추 풍선 도움말을 바꾼다(무엇의 비율을 지키는지 말할 때).</summary>
    public SizeField LinkTip(string tip)
    {
        LinkButton.Tip(tip);
        return this;
    }

    private void Capture()
    {
        if (WidthBox.Value is { } w && HeightBox.Value is { } h && w > 0 && h > 0) _ratio = (double)w / (double)h;
    }

    private void Edited(bool width)
    {
        if (_following) return;
        var (edited, other) = width ? (WidthBox, HeightBox) : (HeightBox, WidthBox);
        if (Linked && edited.Value is { } v && (RatioSource is { } source ? source() : _ratio) is { } ratio && ratio > 0)
        {
            double follow = width ? (double)v / ratio : (double)v * ratio;
            _following = true;
            try
            {
                other.Value = Math.Clamp((decimal)Math.Max(1, Math.Round(follow)), other.Minimum, other.Maximum);
            }
            finally
            {
                _following = false;
            }
        }
        Changed?.Invoke(this, width);
    }
}
