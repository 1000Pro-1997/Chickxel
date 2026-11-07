using Avalonia.Controls.Documents;
using Chickxel.Core.Assets;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>배지에서 @핸들을 보이는 자리.</summary>
public enum IdentityHandle
{
    /// <summary>핸들을 보이지 않는다.</summary>
    None,
    /// <summary>이름 바로 뒤에 흐린 글씨로.</summary>
    Inline,
    /// <summary>이름 아래 둘째 줄에 흐린 글씨로.</summary>
    Below,
}

/// <summary>
/// 사람 하나를 보이는 한 묶음: 둥근 프로필 그림과 이름(선택: @핸들, 굵게, 흐리게, 뒤에 붙는 흐린 덧붙임).
/// 게시 작성자(<see cref="AssetAuthor"/>)는 <see cref="AuthorAvatarView"/>의 규칙(같은 사이트 HTTPS·디스크 보관)으로,
/// 로그인한 계정(<see cref="CommunityUser"/>)은 <see cref="AppCommunity"/>의 규칙으로 그림을 받는다. 그림이 없으면 사람 아이콘.
/// 그림과 글 사이 간격·큰 그림의 글 크기는 그림 크기에서 정하고, 글이 잘리면 풍선 도움말로 전체를 보인다.
/// </summary>
public static class IdentityBadge
{
    /// <summary>이 크기 이상이면 프로필 머리(큰 이름, 그림 바탕, 보통 크기 핸들).</summary>
    private const double LargeSize = 40;

    /// <summary>
    /// 게시 작성자 배지. <paramref name="author"/>가 null이면(로컬 에셋 등) 사람 아이콘과 <paramref name="fallback"/>을 보인다.
    /// 이름은 작성자 이름, 없으면 <paramref name="fallback"/>.
    /// </summary>
    public static Control Author(AssetAuthor? author, double size, string? fallback = null, IdentityHandle handle = IdentityHandle.None,
        bool bold = false, bool dim = false, double fontSize = double.NaN, string? caption = null, double maxWidth = double.PositiveInfinity)
    {
        var avatar = Circle(size);
        AuthorAvatarView.Show(avatar, author);
        return Build(avatar, author?.Name ?? fallback ?? "", author?.Id, size, handle, bold, dim, fontSize, caption, maxWidth);
    }

    /// <summary>
    /// 계정 배지. <paramref name="user"/>가 null이면 사람 아이콘과 <paramref name="fallback"/>(없으면 "확인하지 않은 계정").
    /// <paramref name="loadPicture"/>가 false면 이름만 그 계정 것으로 보이고 그림은 받지 않는다(확인 전 계정 단추).
    /// </summary>
    public static Control User(CommunityUser? user, double size, bool loadPicture = true, string? fallback = null,
        IdentityHandle handle = IdentityHandle.None, bool bold = false, bool dim = false, double fontSize = double.NaN, string? caption = null,
        double maxWidth = double.PositiveInfinity)
    {
        var avatar = Circle(size);
        if (loadPicture) AppCommunity.ShowAvatar(avatar, user);
        string name = user?.Name ?? fallback ?? AppCommunity.DisplayName(null);
        return Build(avatar, name, user?.Handle, size, handle, bold, dim, fontSize, caption, maxWidth);
    }

    /// <summary>둥근 그림 칸(처음엔 사람 아이콘 — 그림을 받으면 그 그림으로 바뀜).</summary>
    private static Border Circle(double size)
    {
        double icon = Math.Round(size * 0.72);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), ClipToBounds = true,
            Background = size >= LargeSize ? AppTheme.PanelBrush : null,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new IconView(VectorIcon.CircleUser, AppTheme.DimTextBrush) { Width = icon, Height = icon },
        };
    }

    private static Control Build(Border avatar, string name, string? handle, double size, IdentityHandle handlePlace,
        bool bold, bool dim, double fontSize, string? caption, double maxWidth)
    {
        bool large = size >= LargeSize;
        avatar.Margin = new Thickness(0, 0, Math.Clamp(Math.Round(size * 0.3), 4, 10), 0);
        var line = new TrimmedText();
        if (dim) line.Foreground = AppTheme.DimTextBrush;
        if (!double.IsNaN(fontSize)) line.FontSize = fontSize;
        else if (large) line.FontSize = 16;
        line.Add(name, bold ? FontWeight.Bold : FontWeight.Normal, null);
        bool hasHandle = handlePlace != IdentityHandle.None && !string.IsNullOrEmpty(handle);
        if (hasHandle && handlePlace == IdentityHandle.Inline)
            line.Add((name.Length > 0 ? " @" : "@") + handle, FontWeight.Normal, AppTheme.DimTextBrush);
        if (!string.IsNullOrEmpty(caption))
            line.Add((line.FullText.Length > 0 ? " · " : "") + caption, FontWeight.Normal, AppTheme.DimTextBrush);

        Control text = line;
        if (hasHandle && handlePlace == IdentityHandle.Below)
        {
            var second = new TrimmedText { Foreground = AppTheme.DimTextBrush };
            if (!large) second.FontSize = AppTheme.SmallFontSize;
            second.Add("@" + handle, FontWeight.Normal, null);
            text = new StackPanel { Spacing = large ? 2 : 0, VerticalAlignment = VerticalAlignment.Center, Children = { line, second } };
        }
        Grid.SetColumn(text, 1);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = maxWidth,
            Children = { avatar, text },
        };
    }

    /// <summary>한 줄 글(조각마다 굵기·색): 칸이 좁아 잘리면 전체 글을 풍선 도움말로 보인다.</summary>
    private sealed class TrimmedText : TextBlock
    {
        public string FullText { get; private set; } = "";

        public TrimmedText()
        {
            TextTrimming = TextTrimming.CharacterEllipsis;
            VerticalAlignment = VerticalAlignment.Center;
        }

        protected override Type StyleKeyOverride => typeof(TextBlock);

        public void Add(string text, FontWeight weight, IBrush? brush)
        {
            if (text.Length == 0) return;
            var run = new Run(text) { FontWeight = weight };
            if (brush is not null) run.Foreground = brush;
            (Inlines ??= []).Add(run);
            FullText += text;
        }

        protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
        {
            var arranged = base.ArrangeOverride(finalSize);
            ToolTip.SetTip(this, TextLayout.TextLines.Any(textLine => textLine.HasCollapsed) ? FullText : null);
            return arranged;
        }
    }
}
