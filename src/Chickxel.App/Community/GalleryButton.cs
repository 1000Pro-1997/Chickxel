using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media.Immutable;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 메뉴 줄 오른쪽, 계정 단추 바로 왼쪽의 갤러리 단추(가게 아이콘): 누르면 파일 > 갤러리에 올리기와 같다(문서가 없으면 꺼짐).
/// 그림이 있는 문서를 손으로 저장하면(자동 저장은 아님) 약 6초 동안 반짝여 공유를 권한다 — 아이콘 둘레에 강조색 빛이 숨 쉬듯 퍼지고
/// 작은 별 모양이 돌아가며 반짝인다. 누르면 바로 멈추고, 한 문서는 이번 실행에서 한 번만 반짝인다(갤러리에 올린 뒤 다시 저장하면 또).
/// 반짝이지 않을 때는 타이머가 돌지 않는다.
/// </summary>
internal sealed class GalleryButton : Button
{
    private static readonly TimeSpan SparkleTime = TimeSpan.FromSeconds(6);

    private readonly SparkleIcon _icon = new() { Width = 32, Height = 22 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    /// <summary>이번 실행에서 이미 반짝여 권한 문서(올리면 빠짐).</summary>
    private readonly ConditionalWeakTable<SpriteProject, object> _invited = new();

    public GalleryButton(Action upload)
    {
        Content = _icon;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(4, 1);
        Margin = new Thickness(0, 0, 2, 0);
        MinHeight = 0;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        this.Tip(UiStrings.Gallery_ButtonTip);
        _timer.Tick += (_, _) => Tick();
        Click += (_, _) =>
        {
            Stop();
            upload();
        };
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>반짝이는 중인지(가이드 그림·시험용).</summary>
    public bool IsSparkling => _timer.IsEnabled;

    /// <summary>
    /// 문서 <paramref name="project"/>를 손으로 저장했다: 지금 문서이고 그림이 있으면, 이번 실행에서 그 문서로 처음일 때(올린 뒤 다시 저장했으면 또) 반짝인다.
    /// </summary>
    public void Saved(SpriteProject project, bool current)
    {
        if (!current || !IsEnabled || _invited.TryGetValue(project, out _) || !HasPicture(project)) return;
        _invited.AddOrUpdate(project, this);
        Start();
    }

    /// <summary>문서 <paramref name="project"/>를 갤러리에 올렸다: 다음에 저장하면 다시 권할 수 있다.</summary>
    public void Uploaded(SpriteProject project) => _invited.Remove(project);

    /// <summary>반짝임을 바로 멈춘다(문서를 닫았을 때 등).</summary>
    public void Stop()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _clock.Reset();
        _icon.Phase = null;
        this.Tip(UiStrings.Gallery_ButtonTip);
    }

    private void Start()
    {
        _clock.Restart();
        _icon.Phase = 0;
        _timer.Start();
        this.Tip(UiStrings.Gallery_ButtonSparkleTip);
    }

    private void Tick()
    {
        var elapsed = _clock.Elapsed;
        if (elapsed >= SparkleTime || !IsEffectivelyVisible) Stop();
        else _icon.Phase = elapsed.TotalSeconds;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty && !IsEnabled) Stop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>보이는 칸 하나라도 그림이 있다(빈 새 문서는 권하지 않음 — 처음 투명이 아닌 픽셀에서 멈춤).</summary>
    private static bool HasPicture(SpriteProject project)
        => project.StoredCels.Any(c => !c.Cel.IsRegionEmpty(c.Cel.Bounds));

    /// <summary>
    /// 가게 아이콘과 반짝임. <see cref="Phase"/>(반짝인 지 초, null = 쉼)가 바뀔 때만 다시 그린다.
    /// 반짝이는 동안: 가운데 강조색 빛(1.2초마다 숨 쉬듯, 처음·끝은 서서히), 아이콘이 강조색으로 물듦, 둘레 네 곳의 작은 네 갈래 별이 번갈아 커졌다 작아짐.
    /// </summary>
    private sealed class SparkleIcon : Control
    {
        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<SparkleIcon>();

        /// <summary>별 자리(칸 크기에 대한 비율)와 위상(0~1)·색 번호.</summary>
        private static readonly (double X, double Y, double Phase, int Color)[] Stars =
        [
            (0.13, 0.22, 0.0, 0), (0.88, 0.18, 0.35, 1), (0.84, 0.84, 0.6, 0), (0.15, 0.80, 0.82, 1),
        ];

        private static readonly Color[] StarColors = [Color.FromRgb(255, 233, 150), Color.FromRgb(190, 225, 255)];

        /// <summary>네 갈래 별(가운데 0, 바깥 반지름 1, 오목한 변).</summary>
        private static readonly Geometry Star = Geometry.Parse("M0,-1 Q0.12,-0.12 1,0 Q0.12,0.12 0,1 Q-0.12,0.12 -1,0 Q-0.12,-0.12 0,-1 Z");

        private double? _phase;

        static SparkleIcon() => AffectsRender<SparkleIcon>(ForegroundProperty);

        public IBrush? Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public double? Phase
        {
            get => _phase;
            set
            {
                _phase = value;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(Brushes.Transparent, bounds);
            var brush = Foreground ?? AppTheme.TextBrush;
            if (_phase is not { } t)
            {
                VectorIcons.Draw(g, VectorIcon.Store, bounds, 16, brush, 2.2);
                return;
            }

            // 처음 0.3초에 켜지고 마지막 0.8초에 꺼진다.
            double total = SparkleTime.TotalSeconds;
            double envelope = Math.Clamp(Math.Min(t / 0.3, (total - t) / 0.8), 0, 1);
            double breath = 0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 1.2 - Math.PI / 2);
            var center = bounds.Center;
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
            g.DrawEllipse(glow, null, center, bounds.Width / 2 * grow, bounds.Height / 2 * grow);

            var tint = brush is ISolidColorBrush solid ? Mix(solid.Color, Color.FromRgb(150, 205, 255), envelope * (0.4 + 0.6 * breath)) : accent;
            VectorIcons.Draw(g, VectorIcon.Store, bounds, 16, new ImmutableSolidColorBrush(tint), 2.2);

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

        private static Color Mix(Color a, Color b, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
        }
    }
}
