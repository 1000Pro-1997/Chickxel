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
    private const double PreviewHeight = 64, NamePreviewMaxWidth = 170;
    /// <summary>프로필 카드의 경험치 막대 폭(이름 칸 아래 — 카드 가운데에 놓는 프로필 덩어리가 너무 넓어지지 않게).</summary>
    private const double CardProgressWidth = 200;

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// 프로필 카드: 장착한 카드 그림(3배, 가로로 칸을 채움)을 바탕으로 큰 프로필 그림(효과·큰 테두리)·이름·칭호·@핸들·레벨 배지, 그 아래 경험치 막대.
    /// 프로필 덩어리(그림·이름·막대)는 카드 바탕 가운데에 놓는다. 카드가 없으면 패널 색 바탕.
    /// </summary>
    public static Control ProfileCard(CommunityUser user, double width)
    {
        var head = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { IdentityBadge.User(user, 56, handle: IdentityHandle.Below, bold: true) },
        };
        // 경험치 막대는 이름 칸과 같은 자리에서(프로필 그림 56 + 간격 10, 꾸민 큰 그림이면 양옆 자리만큼 더)
        double indent = 56 + 10 + (ProfileAvatar.HasLargeDecoration(user.Equipped, user.Level) ? 2 * ProfileAvatar.LargeRoom : 0);
        if (LevelChip.Progress(user, Math.Min(CardProgressWidth, width - indent - 54)) is { } level)
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
    /// 레벨 보상 묶음: 왼쪽 이번 레벨 보상(지금 레벨까지 받은 것 중 가장 최근 하나 — 아직 안 본 것이면 금빛 상자·별), 오른쪽 다음 보상과 남은 경험치,
    /// 아래 받은 수와 홈페이지에서 바꾸기 단추(<paramref name="change"/>). 지난 보상은 늘어놓지 않는다(전부는 홈페이지 레벨 보상 쪽에서).
    /// </summary>
    public static Control Section(Window owner, CommunityUser user, int seenLevel, out Button change)
    {
        var body = new StackPanel { Spacing = 10 };
        // 제목 줄 · 상자 줄: 두 상자는 같은 줄에 있어 높이가 같다(남은 경험치 글은 다음 보상 상자 안에).
        var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 5 };
        if (LevelRewards.All.LastOrDefault(r => r.Level <= user.Level) is { } current)
        {
            bool isNew = current.Level > seenLevel;
            var title = Heading(F(UiStrings.Account_CurrentReward, current.Level));
            Place(pair, isNew ? Ui.Row(new PixelArtView(new RewardStarArt(), 2), title) : title, Tile(user, current, isNew: isNew), 0);
        }

        if (LevelRewards.Next(user.Level) is { } next)
        {
            string? need = user.HasXp ? F(UiStrings.Account_NextRewardXp, Math.Max(0, CommunityLevel.StartXp(next.Level) - user.Xp)) : null;
            Place(pair, Heading(F(UiStrings.Account_NextReward, next.Level)), Tile(user, next, locked: true, note: need), 1);
        }
        else
        {
            var done = new TextBlock { Text = UiStrings.Account_AllRewardsDone, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            Grid.SetColumn(done, 1);
            Grid.SetRowSpan(done, 2);
            pair.Children.Add(done);
        }
        body.Children.Add(pair);

        int unlocked = LevelRewards.UnlockedAt(user.Level).Count();
        // 레벨 튜토리얼은 누른 순간 새싹 보상을 받게 하고(BeforeChange), 받은 뒤에 홈페이지를 열어 보상 쪽에 바로 보이게 한다.
        change = Ui.Button(VectorIcon.ExternalLink, UiStrings.Account_RewardsChange, async (_, _) =>
        {
            if (BeforeChange is { } before) await before();
            AppCommunity.Open(owner, AppCommunity.Client.RewardsPageUrl());
        });
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

    /// <summary>'변경하기…'를 누르면 홈페이지를 열기 전에 기다릴 일(레벨 튜토리얼의 마지막 단계가 걸어 둠 — 없으면 null).</summary>
    internal static Func<Task>? BeforeChange { get; set; }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>보상 한 칸(<paramref name="column"/> 0 왼쪽·1 오른쪽)을 표에 놓는다: 위 줄 제목, 아래 줄 상자(칸 너비·줄 높이를 채움 — 두 상자 높이가 같음).</summary>
    private static void Place(Grid grid, Control title, Control tile, int column)
    {
        var margin = new Thickness(column == 0 ? 0 : 5, 0, column == 0 ? 5 : 0, 0);
        title.Margin = margin;
        tile.Margin = margin;
        Grid.SetColumn(title, column);
        Grid.SetColumn(tile, column);
        Grid.SetRow(tile, 1);
        grid.Children.Add(title);
        grid.Children.Add(tile);
    }

    /// <summary>
    /// 보상 상자 하나: 위에 실제 모양 미리보기(칭호 글, 내 레벨 배지, 내 프로필 그림에 테두리·효과, 내 이름에 이름 효과, 작은 카드), 아래 이름과 "Lv N · 칸",
    /// 그 아래 덧붙임 글(<paramref name="note"/> — 다음 보상까지 남은 경험치, 없으면 생략). 내용은 상자 가운데(옆 상자와 높이를 맞추면 위아래 가운데).
    /// <paramref name="isNew"/>면 금빛 테두리, <paramref name="locked"/>면 흐리게, 장착 중이면 체크.
    /// </summary>
    private static Control Tile(CommunityUser user, LevelReward reward, bool isNew = false, bool locked = false, string? note = null)
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
        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { preview, name, meta } };
        if (equipped)
        {
            var mark = Ui.Row(Ui.Icon(VectorIcon.Check, 11), new TextBlock { Text = UiStrings.Account_RewardEquipped, FontSize = AppTheme.SmallFontSize, Foreground = AppTheme.SavedBrush });
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(mark);
        }
        if (note is not null)
            stack.Children.Add(new TextBlock
            {
                Text = note, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            });
        var tile = new Border
        {
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
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = NamePreviewMaxWidth,
                Child = new NameEffectText(AppCommunity.DisplayName(user), style) { FontSize = 15, FontWeight = FontWeight.Bold },
            }),
            RewardSlot.Card => Centered(new Border
            {
                CornerRadius = new CornerRadius(4), ClipToBounds = true, Width = 160, Height = 48,
                Child = new PixelArtView(new ProfileCardArt(reward.Id, 80, 24), 2),
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
