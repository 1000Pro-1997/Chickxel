using System.Diagnostics;

namespace Chickxel.App;

/// <summary>
/// 접고 펴는 내용 칸(<see cref="Ui.Foldable"/>): 펼친 정도(<see cref="Openness"/>, 0~1)만큼의 높이만 차지하고 넘치는 아래쪽은 잘라 보인다.
/// <see cref="SetOpen"/>에 animate를 주면 <see cref="AnimMs"/> 동안 화면 그리기마다(끝으로 갈수록 느리게) 접고 편다. 다 접히면 내용을 숨겨 Tab 이동에서 빠진다.
/// </summary>
internal sealed class FoldBody : Decorator
{
    private const double AnimMs = 180;

    private readonly Stopwatch _clock = new();
    private double _from, _to = 1;
    /// <summary>다음 화면 그리기에 <see cref="Step"/>을 부탁해 둠.</summary>
    private bool _animating;

    public FoldBody(Control content)
    {
        Child = content;
        ClipToBounds = true;
    }

    /// <summary>펴져 있거나 펴지는 중인지.</summary>
    public bool IsOpen => _to >= 1;

    /// <summary>지금 펼친 정도(0 = 다 접힘, 1 = 다 펴짐).</summary>
    public double Openness { get; private set; } = 1;

    /// <summary><see cref="Openness"/>가 바뀜(화살표 돌리기 등).</summary>
    public event Action? OpennessChanged;

    public void SetOpen(bool open, bool animate)
    {
        _to = open ? 1 : 0;
        if (Child is { } child && open) child.IsVisible = true;
        var top = animate ? TopLevel.GetTopLevel(this) : null;
        if (top is null)
        {
            _animating = false;
            Apply(_to);
            return;
        }
        _from = Openness;
        _clock.Restart();
        if (_animating) return;
        _animating = true;
        top.RequestAnimationFrame(Step);
    }

    private void Step(TimeSpan _)
    {
        if (!_animating) return;
        double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / AnimMs);
        double eased = 1 - Math.Pow(1 - t, 3);
        Apply(_from + (_to - _from) * eased);
        if (t < 1 && TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Step);
        else
        {
            _animating = false;
            if (t < 1) Apply(_to);
        }
    }

    private void Apply(double openness)
    {
        Openness = openness;
        if (Child is { } child && openness <= 0) child.IsVisible = false;
        InvalidateMeasure();
        OpennessChanged?.Invoke();
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        if (Child is not { } child) return default;
        child.Measure(availableSize);
        var size = child.DesiredSize;
        return new Avalonia.Size(size.Width, size.Height * Openness);
    }

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        // 내용은 늘 제 높이로 놓고, 칸 밖(접힌 만큼)은 ClipToBounds로 잘린다.
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, Child.DesiredSize.Height)));
        return finalSize;
    }
}
