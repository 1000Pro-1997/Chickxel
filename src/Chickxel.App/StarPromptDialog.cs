using Chickxel.App.Ai;
using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// GitHub 별 부탁 창(한 번만 — <see cref="AppSettings.StarPromptShown"/>): 반짝이는 큰 별, 부탁 글, 노란 'GitHub에 별 주기' 단추와 닫기.
/// 단추를 누르면 이 컴퓨터의 GitHub CLI(<c>gh</c>)가 로그인돼 있으면 그것으로 바로 별을 찍고(<see cref="GitHubStar"/>), 안 되면 저장소 페이지를 브라우저로 연다.
/// 뜨는 조건은 <see cref="MainForm"/>이 정한다(AI 그림 픽셀로 정리 3번 또는 30분 넘게 쓴 뒤 직접 저장했을 때 — 닫으면서 저장할 때는 아님).
/// </summary>
public sealed class StarPromptDialog : DialogBase
{
    private readonly Button _star;
    private readonly TextBlock _note;
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _tilt = new();
    /// <summary>단추를 누른 때 — 그 뒤로 단추는 '감사합니다!'로 바뀌어 통통 튀고 흔들리며, 다시 누를 수 없다(별 취소 없음).</summary>
    private DateTime? _thankedAt;

    public static void Ask(Window owner) => new StarPromptDialog().ShowDialog(owner);

    private StarPromptDialog() : base(UiStrings.StarPrompt_Title)
    {
        CloseOnly();
        var sparkle = new StarSparkleView { Width = 150, Height = 110, HorizontalAlignment = HorizontalAlignment.Center };
        var heading = new TextBlock
        {
            Text = UiStrings.StarPrompt_Heading, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360,
        };
        var body = new TextBlock
        {
            Text = UiStrings.StarPrompt_Body, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Center,
        };
        _star = Ui.Button(VectorIcon.Star, UiStrings.StarPrompt_Button, (_, _) => GiveStar());
        var ink = AppTheme.Brush(Color.FromRgb(0x23, 0x1A, 0x0E));
        _star.Background = AppTheme.Brush(Color.FromRgb(0xFF, 0xD2, 0x3F));
        _star.Foreground = ink;
        _star.FontWeight = FontWeight.Bold;
        _star.FontSize = 15;
        _star.Padding = new Thickness(18, 8);
        _star.BorderThickness = new Thickness(2);
        _star.HorizontalAlignment = HorizontalAlignment.Center;
        _star.RenderTransformOrigin = RelativePoint.Center;
        _star.RenderTransform = new TransformGroup { Children = { _scale, _tilt } };
        if (_star.Content is StackPanel { Children: [IconView old, ..] } row)
            row.Children[0] = new IconView(VectorIcon.Star, ink) { Filled = true, Width = 16, Height = 16, Margin = old.Margin, VerticalAlignment = VerticalAlignment.Center };
        Ui.KeepFillOnHover(_star);
        _note = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false,
        };
        Body.Children.Add(new StackPanel { Spacing = 10, Margin = new Thickness(10, 4, 10, 6), Children = { sparkle, heading, body, _star, _note } });
        // 단추 테두리가 숨 쉬듯 희게 빛난다(누르고 싶게). 누른 뒤에는 한 번 크게 튀어 오른 뒤 좌우로 살랑인다.
        _pulse.Tick += (_, _) =>
        {
            double t = (DateTime.UtcNow - _start).TotalSeconds;
            byte a = (byte)(110 + 145 * (0.5 + 0.5 * Math.Sin(t * 4)));
            _star.BorderBrush = new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xF6, 0xC8));
            if (_thankedAt is not { } at) return;
            double e = (DateTime.UtcNow - at).TotalSeconds;
            // 처음 0.8초: 줄어드는 진동으로 통통(1 → 1.18 → 0.95 → … → 1), 그 뒤: 작게 숨 쉬며 흔들림.
            double bounce = e < 0.8 ? 0.18 * Math.Exp(-e * 5) * Math.Sin(e * 22) : 0;
            _scale.ScaleX = _scale.ScaleY = 1 + bounce + 0.03 * Math.Sin(e * 5);
            _tilt.Angle = 4 * Math.Sin(e * 7) * Math.Min(1, e * 2);
        };
        Opened += (_, _) => _pulse.Start();
        Closed += (_, _) => _pulse.Stop();
    }

    private async void GiveStar()
    {
        if (_thankedAt is not null) return;
        _thankedAt = DateTime.UtcNow;
        // 다시 눌러 되돌릴 수 없게 누름을 막는다(흐리게 하지 않고 노란 '감사합니다!' 그대로).
        _star.IsHitTestVisible = false;
        _star.Focusable = false;
        if (_star.Content is StackPanel { Children: [IconView icon, TextBlock label, ..] } row)
        {
            row.Children[0] = new IconView(VectorIcon.Heart, icon.Foreground) { Filled = true, Width = icon.Width, Height = icon.Height, Margin = icon.Margin, VerticalAlignment = icon.VerticalAlignment };
            label.Text = UiStrings.StarPrompt_ThankButton;
        }
        _note.IsVisible = true;
        _note.Text = UiStrings.StarPrompt_Working;
        bool starred = await GitHubStar.TryWithCliAsync();
        if (!IsVisible) return;
        if (starred)
        {
            _note.Text = UiStrings.StarPrompt_Thanks;
            _note.Foreground = AppTheme.SavedBrush;
            return;
        }
        // gh가 없거나 로그인하지 않았으면 저장소 페이지에서 직접 누르게.
        _note.Text = UiStrings.StarPrompt_OpenedPage;
        Updater.OpenUrl(this, AppUpdate.RepoUrl);
    }

    /// <summary>가운데 큰 노란 별(벡터)과 둘레에서 차례로 반짝이는 작은 네 갈래 빛.</summary>
    private sealed class StarSparkleView : Control
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly DateTime _start = DateTime.UtcNow;
        private static readonly (double X, double Y, double Size, double Phase)[] Sparkles =
        [
            (0.12, 0.25, 9, 0.0), (0.86, 0.18, 11, 0.35), (0.92, 0.70, 8, 0.7), (0.08, 0.78, 10, 0.15), (0.70, 0.92, 7, 0.55), (0.30, 0.06, 7, 0.85),
        ];

        public StarSparkleView()
        {
            _timer.Tick += (_, _) => InvalidateVisual();
            AttachedToVisualTree += (_, _) => _timer.Start();
            DetachedFromVisualTree += (_, _) => _timer.Stop();
        }

        public override void Render(DrawingContext g)
        {
            double t = (DateTime.UtcNow - _start).TotalSeconds;
            double w = Bounds.Width, h = Bounds.Height;
            // 큰 별: 천천히 숨 쉬듯 커졌다 작아지고, 둘레에 옅은 빛.
            double s = Math.Min(w, h) * (0.62 + 0.04 * Math.Sin(t * 2.2));
            var c = new Avalonia.Point(w / 2, h / 2);
            var glow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new Avalonia.Media.GradientStop(Color.FromArgb(90, 0xFF, 0xD2, 0x3F), 0),
                    new Avalonia.Media.GradientStop(Color.FromArgb(0, 0xFF, 0xD2, 0x3F), 1),
                },
            };
            g.DrawEllipse(glow, null, c, s * 0.8, s * 0.8);
            using (g.PushTransform(Matrix.CreateTranslation(c.X - s / 2, c.Y - s / 2)))
                VectorIcons.Draw(g, VectorIcon.Star, new Rect(0, 0, s, s), s, AppTheme.Brush(Color.FromRgb(0xFF, 0xD2, 0x3F)), 2.2, filled: true);
            // 작은 반짝이: 자리마다 시차를 두고 커졌다 사라진다.
            foreach (var (x, y, size, phase) in Sparkles)
            {
                double k = Math.Sin(((t * 0.8 + phase) % 1.0) * Math.PI);
                if (k <= 0.05) continue;
                double r = size * k;
                var p = new Avalonia.Point(x * w, y * h);
                var geo = Geometry.Parse(FormattableString.Invariant(
                    $"M{p.X},{p.Y - r} Q{p.X + r * 0.18},{p.Y - r * 0.18} {p.X + r},{p.Y} Q{p.X + r * 0.18},{p.Y + r * 0.18} {p.X},{p.Y + r} Q{p.X - r * 0.18},{p.Y + r * 0.18} {p.X - r},{p.Y} Q{p.X - r * 0.18},{p.Y - r * 0.18} {p.X},{p.Y - r} Z"));
                g.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(255 * k), 0xFF, 0xF6, 0xC8)), null, geo);
            }
        }
    }
}

/// <summary>GitHub CLI(<c>gh</c>)로 Chickxel 저장소에 별을 찍는다(설치·로그인돼 있을 때만).</summary>
internal static class GitHubStar
{
    /// <summary>gh가 있고 로그인돼 있으면 <c>gh api -X PUT /user/starred/{owner}/{repo}</c>로 별을 찍고 true. 없거나 실패하면 false.</summary>
    public static async Task<bool> TryWithCliAsync()
    {
        if (AiCli.Locate("gh", null, []) is not { } gh) return false;
        var auth = await AiCli.RunAsync(gh, ["auth", "status"], CancellationToken.None, timeoutSeconds: 10);
        if (auth.ExitCode != 0) return false;
        var put = await AiCli.RunAsync(gh, ["api", "-X", "PUT", $"/user/starred/{AppUpdate.Owner}/{AppUpdate.Repository}"], CancellationToken.None, timeoutSeconds: 15);
        return put.ExitCode == 0;
    }
}
