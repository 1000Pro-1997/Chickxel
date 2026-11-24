using System.Globalization;
using Avalonia.Media.Immutable;
using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 계정 레벨 표시: 이름 옆 픽셀 그림 배지(<c>Lv 7</c> — <see cref="LevelBadgeArt"/>를 정수 배로 키워 또렷하게; 장착한 배지 보상의 모양, 없으면 레벨 단계 모양 —
/// 단계가 높을수록 빛·반짝이·흐르는 색이 화려해지고 1~4 흐린 회색은 멈춤)와 계정 창의 레벨·경험치 막대. 홈페이지 배지와 같은 그림을 쓴다.
/// 새 레벨 보상을 아직 보지 않았으면 배지 둘레가 금빛으로 반짝인다(<see cref="RewardAttentionArt"/>).
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

    /// <summary>계정의 레벨 배지(레벨을 모르면 — 예전 서버·확인 전 — null). 장착한 배지 보상 모양으로.</summary>
    public static Control? For(CommunityUser? user, bool large = false, bool attention = false)
        => user is { Level: > 0 } u ? Create(u.Level, large, u.Equipped.Equipped(RewardSlot.Badge, u.Level)?.Id, attention) : null;

    /// <summary>
    /// 레벨 배지 하나(풍선: 레벨과 경험치 쌓는 법). <paramref name="large"/>면 그림 한 칸이 3px, 아니면 2px. <paramref name="skin"/> = 배지 보상 ID(없으면 레벨 단계 모양).
    /// <paramref name="attention"/>이면 둘레가 반짝여 새 보상을 알린다.
    /// </summary>
    public static Control Create(int level, bool large = false, string? skin = null, bool attention = false)
    {
        int scale = large ? 3 : 2;
        var art = new LevelBadgeArt(level, skin);
        Control badge = new PixelArtView(art, scale) { VerticalAlignment = VerticalAlignment.Center };
        if (attention)
            badge = new Panel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { new PixelArtView(new RewardAttentionArt(art.BadgeWidth, LevelBadgeArt.BadgeHeight), scale) { Fill = true }, badge },
            };
        ToolTip.SetTip(badge, string.Format(CultureInfo.CurrentCulture, attention ? UiStrings.Level_ChipNewTip : UiStrings.Level_ChipTip, level));
        return badge;
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
