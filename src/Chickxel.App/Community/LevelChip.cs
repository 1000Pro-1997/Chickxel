using System.Globalization;
using Avalonia.Media.Immutable;
using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 계정 레벨 표시: 이름 옆 작은 칩(<c>Lv 7</c> — 레벨 단계마다 색: 1~4 흐린 회색, 5~9 동, 10~19 은, 20~29 금, 30~49 청록–보라 크리스털과 은은한 빛,
/// 50 이상 무지개와 빛, <see cref="CommunityLevel.Tier"/>)과 계정 창의 레벨·경험치 막대. 홈페이지 배지와 같은 단계를 쓴다(앱은 움직이지 않는 그러데이션).
/// </summary>
public static class LevelChip
{
    private sealed record Look(IBrush Background, IBrush Foreground, IBrush Border, Color? Glow, IBrush Bar);

    private static readonly Look Plain = new(Solid(0x3C, 0x3C, 0x44), Solid(0xB8, 0xB8, 0xC0), Solid(0x55, 0x55, 0x5F), null, Solid(0x9A, 0x9A, 0xA4));
    private static readonly Look Bronze = new(Gradient("#7A4A22", "#B87333"), Solid(0xFF, 0xE9, 0xD2), Solid(0xC9, 0x8A, 0x4F), null, Solid(0xC9, 0x8A, 0x4F));
    private static readonly Look Silver = new(Gradient("#7E8896", "#D5DCE4"), Solid(0x1F, 0x24, 0x2B), Solid(0xE6, 0xEB, 0xF0), null, Solid(0xC4, 0xCC, 0xD6));
    private static readonly Look Gold = new(Gradient("#B8860B", "#FFD54A"), Solid(0x2B, 0x1E, 0x00), Solid(0xFF, 0xE5, 0x8A), null, Solid(0xF0, 0xC0, 0x30));
    private static readonly Look Crystal = new(Gradient("#22D3EE", "#8B5CF6"), Solid(0xFF, 0xFF, 0xFF), Solid(0xB9, 0xF3, 0xFF),
        Color.FromArgb(170, 110, 150, 255), Gradient("#22D3EE", "#8B5CF6"));
    private static readonly Look Legend = new(Gradient("#E8475A", "#E59A2E", "#2FB868", "#2C84D8", "#9050E0"), Solid(0xFF, 0xFF, 0xFF), Solid(0xFF, 0xE8, 0xA0),
        Color.FromArgb(180, 230, 120, 255), Gradient("#E8475A", "#E59A2E", "#2FB868", "#2C84D8", "#9050E0"));

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

    private static Look LookOf(int level) => CommunityLevel.Tier(level) switch
    {
        CommunityLevelTier.Legend => Legend,
        CommunityLevelTier.Crystal => Crystal,
        CommunityLevelTier.Gold => Gold,
        CommunityLevelTier.Silver => Silver,
        CommunityLevelTier.Bronze => Bronze,
        _ => Plain,
    };

    /// <summary>계정의 레벨 칩(레벨을 모르면 — 예전 서버·확인 전 — null).</summary>
    public static Border? For(CommunityUser? user, double fontSize = double.NaN) => user is { Level: > 0 } u ? Create(u.Level, fontSize) : null;

    /// <summary>레벨 칩 하나(풍선: 레벨과 경험치 쌓는 법).</summary>
    public static Border Create(int level, double fontSize = double.NaN)
    {
        var look = LookOf(level);
        var text = new TextBlock
        {
            Text = "Lv " + level.ToString(CultureInfo.InvariantCulture),
            FontWeight = FontWeight.Bold,
            FontSize = double.IsNaN(fontSize) ? AppTheme.SmallFontSize - 1 : fontSize,
            Foreground = look.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chip = new Border
        {
            Background = look.Background,
            BorderBrush = look.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0, 4, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = text,
        };
        if (look.Glow is { } glow) chip.BoxShadow = new BoxShadows(new BoxShadow { Blur = 6, Color = glow });
        ToolTip.SetTip(chip, string.Format(CultureInfo.CurrentCulture, UiStrings.Level_ChipTip, level));
        return chip;
    }

    /// <summary>
    /// 계정 창의 레벨 줄(칩은 바로 위 프로필 배지에 있음): "레벨 N"·경험치 쌓는 법 정보박스, 다음 레벨까지 막대, 경험치 글.
    /// 경험치를 모르면(레벨만 옴) 막대 없이 레벨 줄만, 레벨도 모르면 null.
    /// </summary>
    public static Control? Progress(CommunityUser user, double width)
    {
        if (user.Level <= 0) return null;
        var look = LookOf(user.Level);
        var title = new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, UiStrings.Level_Title, user.Level), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { title, Ui.InfoBadge(UiStrings.Level_HowTip) } };
        var panel = new StackPanel { Spacing = 3, Children = { head } };
        if (!user.HasXp || user.NextLevelXp is not { } next) return panel;
        panel.Children.Add(new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = CommunityLevel.Progress(user.Xp, user.LevelStartXp, next),
            Height = 6, MinHeight = 6, Width = width, HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = look.Bar, Background = AppTheme.InputBrush, CornerRadius = new CornerRadius(3),
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
