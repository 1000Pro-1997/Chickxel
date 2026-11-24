using Avalonia.Media.Immutable;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 레벨 보상으로 꾸민 프로필 그림(앱 어디서나 같은 모양): 작은 프로필 그림(<see cref="LargeSize"/>보다 작음)은 작은 테두리, 큰 프로필 그림은 효과(뒤) + 큰 테두리(위).
/// 테두리·효과는 그림 칸 밖으로 번져도 레이아웃을 밀지 않는다(<see cref="PixelArtView"/>). 작은 테두리는 픽셀 2배, 큰 것은 3배(앱의 큰 프로필 그림은 56px이라 —
/// 홈페이지의 72px는 4배 — 날개·불꽃이 이름까지 번지지 않게).
/// 칭호 글(<see cref="Title"/>)도 여기서 만든다.
/// </summary>
public static class ProfileAvatar
{
    /// <summary>이 크기 이상이면 큰 테두리·효과를 쓴다.</summary>
    public const double LargeSize = 40;

    /// <summary>큰 테두리·효과가 있을 때 프로필 그림 양옆에 비워 두는 자리(날개·불꽃·결정이 이름을 가리지 않게, px).</summary>
    public const double LargeRoom = 28;

    /// <summary>꾸민 큰 프로필 그림인지(양옆에 <see cref="LargeRoom"/>을 둠).</summary>
    public static bool HasLargeDecoration(ProfileRewards rewards, int level)
        => rewards.Equipped(RewardSlot.FrameLarge, level) is not null || rewards.Equipped(RewardSlot.Effect, level) is not null;

    /// <summary>
    /// <paramref name="avatar"/>(지름 <paramref name="size"/>의 둥근 그림)에 <paramref name="rewards"/>의 테두리·효과(레벨 <paramref name="level"/>로 받을 수 있는 것만)를 겹친 칸.
    /// 꾸밀 것이 없으면 <paramref name="avatar"/> 그대로. 큰 그림을 꾸미면 양옆에 <see cref="LargeRoom"/>을 더 비운다.
    /// </summary>
    public static Control Decorate(Control avatar, double size, ProfileRewards rewards, int level)
    {
        bool large = size >= LargeSize;
        int scale = large ? 3 : 2;
        int diameter = Math.Max(4, (int)Math.Round(size / scale));
        var frame = rewards.Equipped(large ? RewardSlot.FrameLarge : RewardSlot.FrameSmall, level);
        var effect = large ? rewards.Equipped(RewardSlot.Effect, level) : null;
        if (frame is null && effect is null) return avatar;
        var m = avatar.Margin;
        double room = large ? LargeRoom : 0;
        var panel = new Panel
        {
            Width = size, Height = size, VerticalAlignment = avatar.VerticalAlignment,
            Margin = new Thickness(m.Left + room, m.Top, m.Right + room, m.Bottom),
        };
        avatar.Margin = default;
        if (effect is not null) panel.Children.Add(new PixelArtView(new AvatarEffectArt(effect.Id, diameter), scale) { Fill = true });
        panel.Children.Add(avatar);
        if (frame is not null) panel.Children.Add(new PixelArtView(new AvatarFrameArt(frame.Id, diameter), scale) { Fill = true });
        return panel;
    }

    private static readonly IBrush RainbowBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new Avalonia.Media.GradientStop(Color.Parse("#FF7A8A"), 0), new Avalonia.Media.GradientStop(Color.Parse("#FFB347"), 0.2),
            new Avalonia.Media.GradientStop(Color.Parse("#FFE066"), 0.4), new Avalonia.Media.GradientStop(Color.Parse("#6EE7A0"), 0.6),
            new Avalonia.Media.GradientStop(Color.Parse("#7CB8FF"), 0.8), new Avalonia.Media.GradientStop(Color.Parse("#C49BFF"), 1),
        },
    }.ToImmutable();

    /// <summary>칭호 글자 색(40레벨 이상 칭호는 무지개).</summary>
    public static IBrush TitleBrush(LevelReward title)
    {
        if (LevelRewards.IsRainbowTitle(title)) return RainbowBrush;
        var c = LevelRewards.TitleColor(title);
        return new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
    }

    /// <summary>칭호 한 줄(받은 레벨 단계 색, 굵게).</summary>
    public static TextBlock Title(LevelReward title, double fontSize = double.NaN)
    {
        var text = new TextBlock
        {
            Text = title.Name,
            Foreground = TitleBrush(title),
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (!double.IsNaN(fontSize)) text.FontSize = fontSize;
        return text;
    }
}
