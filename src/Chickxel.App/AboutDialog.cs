using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// Chickxel 정보: 왼쪽 = 이름·버전·한 줄 설명, 저장소·릴리스·Discord(제보·아이디어)·문제 신고·홈페이지 링크, 라이선스, 오픈 소스 고지, AI CLI(Claude·Codex) 안내, 설정 파일 위치.
/// 오른쪽 = 후원 묶음: 감사 글, 커피 단추, 커피를 사 주고 공개를 고른 사람 목록(서버 <c>GET /api/supporters</c> — 창을 열 때마다 새로 받고, 받지 못하면 마지막에 받은 목록).
/// </summary>
public sealed class AboutDialog : DialogBase
{
    private const double TextWidth = 470;
    private const double SupportWidth = 250;
    private readonly StackPanel _supporters = new() { Spacing = 2 };
    private readonly TextBlock _supportersNote;
    /// <summary>후원자 목록을 다시 받기까지(정보 창을 열 때만 확인).</summary>
    private static readonly TimeSpan SupportersRefresh = TimeSpan.FromMinutes(30);

    public AboutDialog() : base(UiStrings.S_AboutDialog_AboutDialog_39A221AE8E)
    {
        // 대상 도움말(F1): 창 = "제보·아이디어"의 정보 창 그림(④~⑦), 버전 = 버그 제보의 버전 항목, 링크 줄 = 제보할 곳·개발 후원, 설정 파일 위치 = "환경 설정"의 설정 폴더
        const string page = "start-feedback";
        this.HelpAt(page, shot: "start-feedback-2");
        CloseOnly();

        using var png = new MemoryStream(Icons.AppIconPng());
        var icon = new Image { Source = new Bitmap(png), Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
        var heading = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };
        heading.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush });
        heading.Children.Add(Text(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AboutDialog_AboutDialog_280F145056,AppInfo.VersionText) + (AppInfo.IsDevBuild ? UiStrings.S_AboutDialog_AboutDialog_27606A6DB7 : ""))
            .HelpAt(page, anchor: "Version: shown in"));
        heading.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_4A7237E3B0));
        var left = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(heading);
        left.Children.Add(top);

        left.Children.Add(Ui.Row(
            Link(UiStrings.S_AboutDialog_AboutDialog_1BCE018BCE, AppUpdate.RepoUrl),
            Link(UiStrings.S_AboutDialog_AboutDialog_617FEF961D, AppUpdate.ReleasesUrl),
            Ui.DiscordButton(UiStrings.About_Discord, (_, _) => Updater.OpenUrl(this, AppInfo.DiscordUrl)),
            Link(UiStrings.S_AboutDialog_AboutDialog_9A9BD8295A, AppUpdate.IssuesUrl)).HelpAt(page, anchor: "Where to report"));
        left.Children.Add(Ui.Row(Link(VectorIcon.Globe, UiStrings.About_Website, AppInfo.LocalizedWebsiteUrl)));
        left.Children.Add(Section(UiStrings.S_AboutDialog_AboutDialog_E3F28341F4));
        left.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_BE04412652));
        left.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_38A0479DD2));
        left.Children.Add(Ui.Row(Link(UiStrings.S_AboutDialog_AboutDialog_2085099DF4, AppInfo.ThirdPartyNoticesUrl)));
        left.Children.Add(Section(UiStrings.S_AboutDialog_AboutDialog_5B2E2D16BC));
        left.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_672604ED50
            + UiStrings.S_AboutDialog_AboutDialog_AE8CAE05A3));
        var settings = Text(UiStrings.S_AboutDialog_AboutDialog_D59819BEBD + AppSettings.FilePath);
        settings.Foreground = AppTheme.DimTextBrush;
        settings.Margin = new Thickness(3, 10, 3, 3);
        left.Children.Add(settings.HelpAt("files-settings", anchor: "The settings folder"));

        // 오른쪽: 후원 묶음(감사 글 · 커피 단추 · 공개를 고른 후원자 · 병아리 후원 마을 가기)
        var coffee = Ui.CoffeeButton(UiStrings.About_BuyCoffee, (_, _) => Updater.OpenUrl(this, AppInfo.SupportUrl)).Tip(AppInfo.SupportUrl);
        var village = Ui.Button(VectorIcon.House, UiStrings.About_Village, (_, _) => Updater.OpenUrl(this, AppInfo.VillageUrl)).Tip(AppInfo.VillageUrl);
        village.HorizontalAlignment = HorizontalAlignment.Stretch;
        village.HorizontalContentAlignment = HorizontalAlignment.Center;
        coffee.HorizontalAlignment = HorizontalAlignment.Stretch;
        coffee.HorizontalContentAlignment = HorizontalAlignment.Center;
        _supportersNote = new TextBlock { Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 2, 3, 2) };
        var support = new StackPanel
        {
            Spacing = 6,
            Width = SupportWidth,
            Children =
            {
                new TextBlock { Text = UiStrings.About_SupportThanks, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 2, 3, 2) },
                coffee.HelpAt(page, anchor: "Supporting development"),
                Ui.Label(UiStrings.About_SupportersTitle, bold: true),
                new ScrollViewer { Content = _supporters, MaxHeight = 360, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
                _supportersNote,
                village,
            },
        };
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        var group = Ui.Group(UiStrings.About_SupportGroup, support);
        group.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(group, 1);
        columns.Children.Add(left);
        columns.Children.Add(group);
        Body.Children.Add(columns);

        // 후원자 목록은 창을 열 때만, 마지막으로 받은 지 30분이 지났으면 새로 받는다(앱을 켤 때는 받지 않음)
        var s = App.Settings;
        if (DateTime.UtcNow - s.SupportersFetchedUtc < SupportersRefresh)
        {
            ShowSupporters(s.Supporters, s.Supporters.Count == 0 ? UiStrings.About_SupportersEmpty : null);
            return;
        }
        ShowSupporters(s.Supporters, UiStrings.About_SupportersLoading);
        Opened += async (_, _) =>
        {
            try
            {
                var list = await AppCommunity.Client.GetSupportersAsync();
                s.Supporters = [.. list];
                s.SupportersFetchedUtc = DateTime.UtcNow;
                ShowSupporters(list, list.Count == 0 ? UiStrings.About_SupportersEmpty : null);
            }
            catch (CommunityException)
            {
                ShowSupporters(s.Supporters, s.Supporters.Count == 0 ? UiStrings.About_SupportersOffline : UiStrings.About_SupportersCached);
            }
        };
    }

    /// <summary>후원자 줄(커피 아이콘 · 닉네임)과 아래 안내 글(null이면 숨김).</summary>
    private void ShowSupporters(IReadOnlyList<CommunitySupporter> list, string? note)
    {
        _supporters.Children.Clear();
        foreach (var s in list)
        {
            var name = new TextBlock { Text = s.Name, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Margin = new Thickness(3, 1) };
            var icon = Ui.Icon(VectorIcon.Coffee, 14);
            icon.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(name);
            _supporters.Children.Add(row);
        }
        _supportersNote.Text = note ?? "";
        _supportersNote.IsVisible = note is not null;
    }

    private static SelectableTextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = TextWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        Foreground = AppTheme.TextBrush,
        Margin = new Thickness(3),
    };

    private static TextBlock Section(string title)
    {
        var t = Ui.Label(title, bold: true);
        t.Margin = new Thickness(3, 10, 3, 1);
        return t;
    }

    private Button Link(string text, string url)
        => Ui.Button(text, (_, _) => Updater.OpenUrl(this, url)).Tip(url);

    private Button Link(VectorIcon icon, string text, string url)
        => Ui.Button(icon, text, (_, _) => Updater.OpenUrl(this, url)).Tip(url);
}
