using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>Gpixel 정보: 이름·버전·한 줄 설명, 저장소·릴리스·문제 신고 링크, 라이선스, 오픈 소스 고지, Claude CLI 안내, 설정 파일 위치.</summary>
public sealed class AboutDialog : DialogBase
{
    private const double TextWidth = 470;

    public AboutDialog() : base("Gpixel 정보")
    {
        CloseOnly();

        using var png = new MemoryStream(Icons.AppIconPng());
        var icon = new Image { Source = new Bitmap(png), Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
        RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
        var heading = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };
        heading.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush });
        heading.Children.Add(Text($"버전 {AppInfo.VersionText}" + (AppInfo.IsDevBuild ? " (개발 빌드)" : "")));
        heading.Children.Add(Text("게임용 픽셀 아트 편집기 — 프레임·레이어를 .gpixel 프로젝트로 저장하고, 저장할 때 PNG 시트와 Unity .meta를 함께 내보냅니다."));
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(heading);
        Body.Children.Add(top);

        Body.Children.Add(Ui.Row(
            Link("GitHub 저장소", AppUpdate.RepoUrl),
            Link("릴리스", AppUpdate.ReleasesUrl),
            Link("문제 신고", AppUpdate.IssuesUrl)));
        Body.Children.Add(Section("라이선스"));
        Body.Children.Add(Text("MIT 라이선스 · Copyright © 2026 천성훈 (1000Pro-1997)"));
        Body.Children.Add(Text("Gpixel이 쓰는 오픈 소스 구성 요소(.NET, Avalonia, SkiaSharp, SQLite 등)의 라이선스 고지는 THIRD-PARTY-NOTICES.md에 있습니다."));
        Body.Children.Add(Ui.Row(Link("오픈 소스 고지 보기", AppInfo.ThirdPartyNoticesUrl)));
        Body.Children.Add(Section("Claude 채팅"));
        Body.Children.Add(Text("Claude 채팅은 이 컴퓨터에 따로 설치한 Claude Code CLI(Anthropic)와 그 로그인을 씁니다. "
            + "Gpixel에는 Claude Code가 들어 있지 않으며, 사용에는 Anthropic 약관이 적용됩니다."));
        var settings = Text("설정 파일: " + AppSettings.FilePath);
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
}
