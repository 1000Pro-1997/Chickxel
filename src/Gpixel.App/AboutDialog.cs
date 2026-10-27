using Gpixel.Core.IO;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>Gpixel 정보: 이름·버전·한 줄 설명, 저장소·릴리스·Discord(제보·아이디어)·문제 신고·홈페이지·후원(커피) 링크, 라이선스, 오픈 소스 고지, AI CLI(Claude·Codex) 안내, 설정 파일 위치.</summary>
public sealed class AboutDialog : DialogBase
{
    private const double TextWidth = 470;

    public AboutDialog() : base(UiStrings.S_AboutDialog_AboutDialog_39A221AE8E)
    {
        CloseOnly();

        using var png = new MemoryStream(Icons.AppIconPng());
        var icon = new Image { Source = new Bitmap(png), Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
        var heading = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };
        heading.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush });
        heading.Children.Add(Text(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AboutDialog_AboutDialog_280F145056,AppInfo.VersionText) + (AppInfo.IsDevBuild ? UiStrings.S_AboutDialog_AboutDialog_27606A6DB7 : "")));
        heading.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_4A7237E3B0));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(heading);
        Body.Children.Add(top);

        Body.Children.Add(Ui.Row(
            Link(UiStrings.S_AboutDialog_AboutDialog_1BCE018BCE, AppUpdate.RepoUrl),
            Link(UiStrings.S_AboutDialog_AboutDialog_617FEF961D, AppUpdate.ReleasesUrl),
            Ui.DiscordButton(UiStrings.About_Discord, (_, _) => Updater.OpenUrl(this, AppInfo.DiscordUrl)),
            Link(UiStrings.S_AboutDialog_AboutDialog_9A9BD8295A, AppUpdate.IssuesUrl)));
        Body.Children.Add(Ui.Row(
            Link(VectorIcon.Globe, UiStrings.About_Website, AppInfo.LocalizedWebsiteUrl),
            Link(VectorIcon.Coffee, UiStrings.About_BuyCoffee, AppInfo.SupportUrl)));
        Body.Children.Add(Section(UiStrings.S_AboutDialog_AboutDialog_E3F28341F4));
        Body.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_BE04412652));
        Body.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_38A0479DD2));
        Body.Children.Add(Ui.Row(Link(UiStrings.S_AboutDialog_AboutDialog_2085099DF4, AppInfo.ThirdPartyNoticesUrl)));
        Body.Children.Add(Section(UiStrings.S_AboutDialog_AboutDialog_5B2E2D16BC));
        Body.Children.Add(Text(UiStrings.S_AboutDialog_AboutDialog_672604ED50
            + UiStrings.S_AboutDialog_AboutDialog_AE8CAE05A3));
        var settings = Text(UiStrings.S_AboutDialog_AboutDialog_D59819BEBD + AppSettings.FilePath);
        settings.Foreground = AppTheme.DimTextBrush;
        settings.Margin = new Thickness(3, 10, 3, 3);
        Body.Children.Add(settings);
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
