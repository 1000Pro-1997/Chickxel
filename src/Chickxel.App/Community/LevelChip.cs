using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 계정 레벨 표시: 이름 옆 픽셀 그림 배지(<c>Lv 7</c> — <see cref="LevelBadgeArt"/>를 정수 배로 키워 또렷하게; 단계가 높을수록 빛·반짝이·흐르는 색이 화려해짐,
/// 1~4 흐린 회색은 멈춤)와 계정 창의 레벨·경험치 막대. 홈페이지 배지와 같은 단계를 쓴다(<see cref="CommunityLevel.Tier"/>).
/// </summary>
public static class LevelChip
{
    private static IBrush Solid(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));

    /// <summary>왼쪽 위 → 오른쪽 아래로 고르게 나눈 그러데이션.</summary>
    private static IBrush Gradient(params string[] colors)
    {
        var stops = new GradientStops();
        for (int i = 0; i < colors.Length; i++) stops.Add(new Avalonia.Media.GradientStop(Color.Parse(colors[i]), colors.Length == 1 ? 0 : i / (double)(colors.Length - 1)));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = stops,
        }.ToImmutable();
    }

    private static readonly IBrush PlainBar = Solid(0x9A, 0x9A, 0xA4), BronzeBar = Solid(0xC9, 0x8A, 0x4F), SilverBar = Solid(0xC4, 0xCC, 0xD6),
        GoldBar = Solid(0xF0, 0xC0, 0x30), CrystalBar = Gradient("#22D3EE", "#8B5CF6"), LegendBar = Gradient("#E8475A", "#E59A2E", "#2FB868", "#2C84D8", "#9050E0");

    /// <summary>경험치 막대 색(배지 단계 색).</summary>
    private static IBrush BarOf(int level) => CommunityLevel.Tier(level) switch
    {
        CommunityLevelTier.Legend => LegendBar,
        CommunityLevelTier.Crystal => CrystalBar,
        CommunityLevelTier.Gold => GoldBar,
        CommunityLevelTier.Silver => SilverBar,
        CommunityLevelTier.Bronze => BronzeBar,
        _ => PlainBar,
    };

    /// <summary>계정의 레벨 배지(레벨을 모르면 — 예전 서버·확인 전 — null).</summary>
    public static Control? For(CommunityUser? user, bool large = false) => user is { Level: > 0 } u ? Create(u.Level, large) : null;

    /// <summary>레벨 배지 하나(풍선: 레벨과 경험치 쌓는 법). <paramref name="large"/>면 그림 한 칸이 3px, 아니면 2px.</summary>
    public static Control Create(int level, bool large = false)
    {
        var badge = new PixelBadge(new LevelBadgeArt(level), large ? 3 : 2) { VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(badge, string.Format(CultureInfo.CurrentCulture, UiStrings.Level_ChipTip, level));
        return badge;
    }

    /// <summary>
    /// 픽셀 배지 컨트롤: 크기는 배지만큼이고, 빛 테두리·반짝이는 둘레 여백(<see cref="LevelBadgeArt.Margin"/>)까지 칸 밖으로 그린다.
    /// 움직이는 배지는 모두 타이머 하나로 같은 프레임을 그리고(화면에 붙어 있고 보이는 것만), 마지막 배지가 떨어지면 타이머가 선다.
    /// </summary>
    private sealed class PixelBadge : Control
    {
        private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(LevelBadgeArt.FrameMs) };
        private static readonly List<PixelBadge> Running = new();
        private static int _frame;

        private readonly LevelBadgeArt _art;
        private readonly int _scale;
        private readonly Color32[] _pixels;
        private readonly int[] _premul;
        private WriteableBitmap? _bitmap;

        static PixelBadge() => Timer.Tick += (_, _) =>
        {
            _frame++;
            foreach (var badge in Running)
                if (badge.IsEffectivelyVisible) badge.Draw();
        };

        public PixelBadge(LevelBadgeArt art, int scale)
        {
            _art = art;
            _scale = scale;
            _pixels = new Color32[art.Width * art.Height];
            _premul = new int[_pixels.Length];
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        }

        protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
            => new(_art.BadgeWidth * _scale, LevelBadgeArt.BadgeHeight * _scale);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _bitmap ??= new WriteableBitmap(new PixelSize(_art.Width, _art.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            Draw();
            if (!_art.Animated) return;
            Running.Add(this);
            Timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Running.Remove(this);
            if (Running.Count == 0) Timer.Stop();
            _bitmap?.Dispose();
            _bitmap = null;
        }

        private void Draw()
        {
            if (_bitmap is null) return;
            _art.Render(_frame, _pixels);
            for (int i = 0; i < _pixels.Length; i++)
            {
                var c = _pixels[i];
                int a = c.A;
                _premul[i] = a << 24 | c.R * a / 255 << 16 | c.G * a / 255 << 8 | c.B * a / 255;
            }
            using (var fb = _bitmap.Lock())
                for (int y = 0; y < _art.Height; y++)
                    Marshal.Copy(_premul, y * _art.Width, fb.Address + y * fb.RowBytes, _art.Width);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (_bitmap is null) return;
            double m = LevelBadgeArt.Margin * _scale;
            context.DrawImage(_bitmap, new Rect(0, 0, _art.Width, _art.Height), new Rect(-m, -m, _art.Width * _scale, _art.Height * _scale));
        }
    }

    /// <summary>
    /// 계정 창의 레벨 줄(칩은 바로 위 프로필 배지에 있음): "레벨 N"·경험치 쌓는 법 정보박스, 다음 레벨까지 막대, 경험치 글.
    /// 경험치를 모르면(레벨만 옴) 막대 없이 레벨 줄만, 레벨도 모르면 null.
    /// </summary>
    public static Control? Progress(CommunityUser user, double width)
    {
        if (user.Level <= 0) return null;
        var title = new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, UiStrings.Level_Title, user.Level), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { title, Ui.InfoBadge(UiStrings.Level_HowTip) } };
        var panel = new StackPanel { Spacing = 3, Children = { head } };
        if (!user.HasXp || user.NextLevelXp is not { } next) return panel;
        panel.Children.Add(new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = CommunityLevel.Progress(user.Xp, user.LevelStartXp, next),
            Height = 6, MinHeight = 6, Width = width, HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = BarOf(user.Level), Background = AppTheme.InputBrush, CornerRadius = new CornerRadius(3),
        });
        panel.Children.Add(new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            Text = string.Format(CultureInfo.CurrentCulture, UiStrings.Level_Progress, user.Xp, Math.Max(0, next - user.Xp)),
        });
        return panel;
    }
}
