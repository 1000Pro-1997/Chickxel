using System.Diagnostics;
using Avalonia.Media.Immutable;

namespace Chickxel.App;

/// <summary>
/// 강조 반짝임(공용 모듈): 감싼 내용 뒤에서 강조색 빛이 숨 쉬듯 퍼지고(1.2초마다, 처음 0.3초에 켜지고 끝 0.8초에 꺼짐) 네 귀퉁이의 작은 네 갈래 별이
/// 번갈아 커졌다 작아진다. 눈길을 끌어야 하는 단추에 쓴다 — 단추라면 단추 바탕 위·내용 뒤에 그리도록 단추의 <see cref="ContentControl.Content"/>를 감싼다(<see cref="Wrap"/>).
/// <see cref="Start"/>로 정한 시간 동안(없으면 <see cref="Stop"/>까지) 반짝이고, 보이지 않게 되거나 화면에서 빠지면 멈춘다. 반짝이지 않을 때는 타이머가 돌지 않는다.
/// 내용을 물들이려면 <see cref="Pulsed"/>(지금 세기 0~1, 멈추면 null)를 받는다.
/// </summary>
public sealed class Sparkle : Decorator
{
    /// <summary>시간을 주지 않고 <see cref="Start"/>할 때의 반짝임 시간.</summary>
    public static readonly TimeSpan DefaultTime = TimeSpan.FromSeconds(6);

    /// <summary>별 자리(칸 크기에 대한 비율)와 위상(0~1)·색 번호.</summary>
    private static readonly (double X, double Y, double Phase, int Color)[] Stars =
    [
        (0.13, 0.22, 0.0, 0), (0.88, 0.18, 0.35, 1), (0.84, 0.84, 0.6, 0), (0.15, 0.80, 0.82, 1),
    ];

    private static readonly Color[] StarColors = [Color.FromRgb(255, 233, 150), Color.FromRgb(190, 225, 255)];

    /// <summary>네 갈래 별(가운데 0, 바깥 반지름 1, 오목한 변).</summary>
    private static readonly Geometry Star = Geometry.Parse("M0,-1 Q0.12,-0.12 1,0 Q0.12,0.12 0,1 Q-0.12,0.12 -1,0 Q-0.12,-0.12 0,-1 Z");

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    /// <summary>이번 반짝임 시간(null = 멈출 때까지).</summary>
    private TimeSpan? _length;
    /// <summary>반짝인 지 초(null = 쉼).</summary>
    private double? _phase;

    public Sparkle(Control? content = null)
    {
        Child = content;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>반짝이는 중인지.</summary>
    public bool IsSparkling => _timer.IsEnabled;

    /// <summary>
    /// 물들임 세기가 바뀜(그릴 때마다 0~1 — 켜짐 정도 × (0.4 + 0.6 × 숨 쉬는 밝기), 멈추면 null). 감싼 내용을 강조색 쪽으로 물들일 때 쓴다(갤러리 단추 아이콘 등).
    /// </summary>
    public event Action<double?>? Pulsed;

    /// <summary>반짝임이 끝남(시간이 다 됐거나 <see cref="Stop"/>·안 보이게 됨).</summary>
    public event Action? Stopped;

    /// <summary>단추 등의 내용을 반짝임으로 감싼다(바탕 위·내용 뒤에 그려짐).</summary>
    public static Sparkle Wrap(ContentControl control)
    {
        var content = control.Content as Control;
        control.Content = null;
        var sparkle = new Sparkle(content);
        control.Content = sparkle;
        return sparkle;
    }

    /// <summary><paramref name="length"/> 동안 반짝인다(기본 <see cref="DefaultTime"/>, <see cref="TimeSpan.MaxValue"/> = 멈출 때까지). 반짝이는 중이면 처음부터.</summary>
    public void Start(TimeSpan? length = null)
    {
        _length = length is { } l && l == TimeSpan.MaxValue ? null : length ?? DefaultTime;
        _clock.Restart();
        SetPhase(0);
        _timer.Start();
    }

    /// <summary>바로 멈춘다(반짝이지 않으면 아무것도 안 함).</summary>
    public void Stop()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _clock.Reset();
        SetPhase(null);
        Stopped?.Invoke();
    }

    private void Tick()
    {
        var elapsed = _clock.Elapsed;
        if (elapsed >= _length || !IsEffectivelyVisible) Stop();
        else SetPhase(elapsed.TotalSeconds);
    }

    private void SetPhase(double? phase)
    {
        _phase = phase;
        InvalidateVisual();
        Pulsed?.Invoke(phase is { } t ? Envelope(t) * (0.4 + 0.6 * Breath(t)) : null);
    }

    /// <summary>켜지고 꺼지는 정도: 처음 0.3초에 켜지고 마지막 0.8초에 꺼진다(멈출 때까지면 끄지 않음).</summary>
    private double Envelope(double t)
        => Math.Clamp(Math.Min(t / 0.3, _length is { } l ? (l.TotalSeconds - t) / 0.8 : double.PositiveInfinity), 0, 1);

    /// <summary>1.2초마다 숨 쉬는 밝기(0~1).</summary>
    private static double Breath(double t) => 0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 1.2 - Math.PI / 2);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>반짝이는 동안 내용 뒤에: 가운데 강조색 빛, 귀퉁이 별.</summary>
    public override void Render(DrawingContext g)
    {
        if (_phase is not { } t) return;
        var bounds = new Rect(Bounds.Size);
        double envelope = Envelope(t), breath = Breath(t);
        var accent = AppTheme.AccentColor;
        double grow = 0.9 + 0.35 * breath;
        var glow = new RadialGradientBrush
        {
            GradientStops =
            {
                new Avalonia.Media.GradientStop(Color.FromArgb((byte)(envelope * (160 + 95 * breath)), accent.R, accent.G, accent.B), 0),
                new Avalonia.Media.GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
            },
        };
        g.DrawEllipse(glow, null, bounds.Center, bounds.Width / 2 * grow, bounds.Height / 2 * grow);
        foreach (var (x, y, phase, color) in Stars)
        {
            double s = Math.Sin((t / 0.9 + phase) * 2 * Math.PI);
            if (s <= 0) continue;
            double size = 5.5 * s * envelope;
            if (size < 0.4) continue;
            var c = StarColors[color];
            using (g.PushTransform(Matrix.CreateScale(size, size) * Matrix.CreateTranslation(bounds.Width * x, bounds.Height * y)))
                g.DrawGeometry(new ImmutableSolidColorBrush(Color.FromArgb((byte)(255 * Math.Min(1, s * 1.4) * envelope), c.R, c.G, c.B)), null, Star);
        }
    }
}
