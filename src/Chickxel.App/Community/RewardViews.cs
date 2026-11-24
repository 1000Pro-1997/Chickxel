using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 계정 창의 레벨 보상 화면 조각: 프로필 카드(장착한 카드 바탕 위 큰 프로필 그림·이름·칭호·배지·경험치), 보상 상자(칸마다 실제 모양 미리보기 — 내 프로필 그림·내 레벨로),
/// 레벨 보상 묶음(이번 레벨 보상, 다음 보상, 받은 수, 홈페이지에서 바꾸기).
/// </summary>
internal static class RewardViews
{
    private const double TileWidth = 96, PreviewHeight = 64;

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// 프로필 카드: 장착한 카드 그림(3배, 가로로 칸을 채움)을 바탕으로 큰 프로필 그림(효과·큰 테두리)·이름·칭호·@핸들·레벨 배지, 그 아래 경험치 막대.
    /// 카드가 없으면 패널 색 바탕.
    /// </summary>
    public static Control ProfileCard(CommunityUser user, double width)
    {
        var head = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 14),
            Children = { IdentityBadge.User(user, 56, handle: IdentityHandle.Below, bold: true) },
        };
        // 경험치 막대는 이름 칸과 같은 자리에서(프로필 그림 56 + 간격 10, 꾸민 큰 그림이면 양옆 자리만큼 더)
        double indent = 56 + 10 + (ProfileAvatar.HasLargeDecoration(user.Equipped, user.Level) ? 2 * ProfileAvatar.LargeRoom : 0);
        if (LevelChip.Progress(user, width - indent - 54) is { } level)
        {
            level.Margin = new Thickness(indent, 0, 0, 0);
            head.Children.Add(level);
        }
        var card = new Panel();
        if (user.Equipped.Equipped(RewardSlot.Card, user.Level) is { } reward)
            card.Children.Add(new PixelArtView(new ProfileCardArt(reward.Id, (int)Math.Ceiling(width / 3), 46), 3) { Fill = true });
        card.Children.Add(head);
        return new Border
        {
            Width = width,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Child = card,
        };
    }

    /// <summary>
    /// 레벨 보상 묶음: 이번 레벨 보상(지금 레벨까지 받은 것 중 가장 최근 하나 — 아직 안 본 것이면 금빛 상자·별), 다음 보상과 남은 경험치,
    /// 받은 수와 홈페이지에서 바꾸기 단추. 지난 보상은 늘어놓지 않는다(전부는 홈페이지 레벨 보상 쪽에서).
    /// </summary>
    public static Control Section(Window owner, CommunityUser user, int seenLevel)
    {
        var body = new StackPanel { Spacing = 10 };
        if (LevelRewards.All.LastOrDefault(r => r.Level <= user.Level) is { } current)
        {
            bool isNew = current.Level > seenLevel;
            var title = new TextBlock { Text = F(UiStrings.Account_CurrentReward, current.Level), FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(Ui.Row(Tile(user, current, isNew: isNew), Info(isNew ? Ui.Row(new PixelArtView(new RewardStarArt(), 2), title) : title, current)));
        }

        if (LevelRewards.Next(user.Level) is { } next)
        {
            var info = Info(new TextBlock { Text = F(UiStrings.Account_NextReward, next.Level), FontWeight = FontWeight.Bold }, next);
            if (user.HasXp)
            {
                long need = Math.Max(0, CommunityLevel.StartXp(next.Level) - user.Xp);
                info.Children.Add(new TextBlock { Text = F(UiStrings.Account_NextRewardXp, need), Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize });
            }
            body.Children.Add(Ui.Row(Tile(user, next, locked: true), info));
        }
        else body.Children.Add(new TextBlock { Text = UiStrings.Account_AllRewardsDone, Foreground = AppTheme.DimTextBrush });

        int unlocked = LevelRewards.UnlockedAt(user.Level).Count();
        var change = Ui.Button(VectorIcon.ExternalLink, UiStrings.Account_RewardsChange, (_, _) => AppCommunity.Open(owner, AppCommunity.Client.RewardsPageUrl()));
        change.Tip(UiStrings.Account_RewardsChangeTip);
        var count = new TextBlock
        {
            Text = F(UiStrings.Account_RewardsCount, unlocked, LevelRewards.All.Count), Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center,
        };
        var footer = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(change, Dock.Right);
        footer.Children.Add(change);
        footer.Children.Add(count);
        body.Children.Add(footer);
        return Ui.Group(UiStrings.Account_Rewards, body);
    }

    /// <summary>상자 옆 설명: 제목 줄, 보상 이름 · 칸.</summary>
    private static StackPanel Info(Control title, LevelReward reward) => new()
    {
        Spacing = 3,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { title, new TextBlock { Text = reward.Name + " · " + LevelRewards.SlotName(reward.Slot), Foreground = AppTheme.TextBrush } },
    };

    /// <summary>
    /// 보상 상자 하나: 위에 실제 모양 미리보기(칭호 글, 내 레벨 배지, 내 프로필 그림에 테두리·효과, 내 이름에 이름 효과, 작은 카드), 아래 이름과 "Lv N · 칸".
    /// <paramref name="isNew"/>면 금빛 테두리, <paramref name="locked"/>면 흐리게, 장착 중이면 체크.
    /// </summary>
    public static Control Tile(CommunityUser user, LevelReward reward, bool isNew = false, bool locked = false)
    {
        var preview = new Panel { Height = PreviewHeight, ClipToBounds = false };
        preview.Children.Add(Preview(user, reward));
        bool equipped = user.Equipped.Equipped(reward.Slot, user.Level)?.Id == reward.Id;
        var name = new TextBlock
        {
            Text = reward.Name, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center,
        };
        var meta = new TextBlock
        {
            Text = F(UiStrings.Account_RewardLevel, reward.Level, LevelRewards.SlotName(reward.Slot)),
            Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var stack = new StackPanel { Spacing = 2, Children = { preview, name, meta } };
        if (equipped)
            stack.Children.Add(Ui.Row(Ui.Icon(VectorIcon.Check, 11), new TextBlock { Text = UiStrings.Account_RewardEquipped, FontSize = AppTheme.SmallFontSize, Foreground = AppTheme.SavedBrush }));
        var tile = new Border
        {
            Width = TileWidth,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(4, 6),
            CornerRadius = new CornerRadius(6),
            Background = AppTheme.InputBrush,
            BorderBrush = isNew ? new SolidColorBrush(Color.Parse("#FFD54A")) : AppTheme.BorderBrush,
            BorderThickness = new Thickness(isNew ? 2 : 1),
            Opacity = locked ? 0.6 : 1,
            Child = stack,
        };
        ToolTip.SetTip(tile, reward.Name + " — " + F(UiStrings.Account_RewardLevel, reward.Level, LevelRewards.SlotName(reward.Slot)));
        return tile;
    }

    /// <summary>칸별 미리보기(내 프로필 그림·내 이름·내 레벨로 — 장착하면 보일 모양 그대로). 테두리는 큰 프로필 그림의 큰 모양으로 보인다.</summary>
    private static Control Preview(CommunityUser user, LevelReward reward)
    {
        Control Avatar(double size, Func<int, IPixelArt>? under, Func<int, IPixelArt>? over)
        {
            var circle = IdentityBadge.Circle(size);
            AppCommunity.ShowAvatar(circle, user);
            int diameter = (int)Math.Round(size / 2);
            var panel = new Panel { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (under is not null) panel.Children.Add(new PixelArtView(under(diameter), 2) { Fill = true });
            panel.Children.Add(circle);
            if (over is not null) panel.Children.Add(new PixelArtView(over(diameter), 2) { Fill = true });
            return panel;
        }
        return reward.Slot switch
        {
            RewardSlot.Title => Centered(ProfileAvatar.Title(reward, 13)),
            RewardSlot.Badge => Centered(new PixelArtView(new LevelBadgeArt(Math.Max(1, user.Level), reward.Id), 2)),
            RewardSlot.Frame => Avatar(24, null, d => new AvatarFrameArt(reward.Id, d, large: true)),
            RewardSlot.Effect => Avatar(24, d => new AvatarEffectArt(reward.Id, d), null),
            RewardSlot.NameStyle when NameStyle.Find(reward.Id) is { } style => Centered(new Viewbox
            {
                // 긴 이름은 상자 폭에 맞게 줄여 보인다
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = TileWidth - 16,
                Child = new NameEffectText(AppCommunity.DisplayName(user), style) { FontSize = 15, FontWeight = FontWeight.Bold },
            }),
            RewardSlot.Card => Centered(new Border
            {
                CornerRadius = new CornerRadius(4), ClipToBounds = true, Width = 80, Height = 48,
                Child = new PixelArtView(new ProfileCardArt(reward.Id, 40, 24), 2),
            }),
            // 목록에 없는 이름 효과
            _ => new Panel(),
        };
    }

    private static Control Centered(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }
}
