using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Gpixel.App;
using Gpixel.App.Localization;
using Gpixel.Core.IO;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>시작하기: 시작 화면, 업데이트 창, 네트워크(자동 확인 설정), 제보(도움말 메뉴·정보 창).</summary>
internal static class StartShots
{
    /// <summary>CHANGELOG(.ko).md의 버전 머리(## vX.Y.Z (날짜)) 위치들.</summary>
    private static (string Text, List<(SemVersion Version, int Start)> Heads) Changelog()
    {
        var file = Path.Combine(ShotKit.Root, En ? "CHANGELOG.md" : "CHANGELOG.ko.md");
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var heads = Regex.Matches(text, @"^## v(\d+\.\d+\.\d+)[^\n]*$", RegexOptions.Multiline)
            .Select(m => (SemVersion.TryParse(m.Groups[1].Value, out var v) ? v : new SemVersion(0, 0, 0), m.Index)).ToList();
        return (text, heads);
    }

    /// <summary>
    /// 개발 빌드에만 보이는 글자를 릴리스 빌드 모습으로: 버전(개발 빌드 표시 포함) → <paramref name="version"/>,
    /// 환경 설정의 "개발 빌드라 자동 확인 안 함" 안내 → 릴리스 안내, 샷 임시 설정 파일 경로 → Windows 기본 위치.
    /// </summary>
    private static void Scrub(Visual root, string version)
    {
        string devNote = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PreferencesDialog_PreferencesDialog_4B32B49831, AppInfo.VersionText);
        string releaseNote = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PreferencesDialog_PreferencesDialog_80B1F80264, version);
        foreach (var tb in ShotKit.All<TextBlock>(root))
        {
            if (tb.Text is not { } original) continue;
            var t = original == devNote ? releaseNote : original;
            t = t.Replace(AppSettings.FilePath, @"%APPDATA%\Gpixel\settings.json")
                .Replace(UiStrings.S_AboutDialog_AboutDialog_27606A6DB7, "")
                .Replace(AppInfo.VersionText, version);
            if (t != original) tb.Text = t;
        }
    }

    [Shot("start-first-run")]
    public static void FirstRun(ShotKit k)
    {
        var main = k.Main();
        k.Settle(300);
        var start = ShotKit.Get<StartPanel>(main, "_start");
        string root = @"D:\MyGame\Assets";
        start.SetRecent(
            [root + @"\" + FolderName, @"D:\Art\" + L("픽셀 연습", "Pixel practice")],
            [root + @"\" + FolderName + @"\" + SampleName + ".gpixel", @"D:\Art\" + L("픽셀 연습", "Pixel practice") + @"\slime.gpixel"]);
        var hub = ShotKit.Get<ListBox>(start, "_hub");
        var cols = ShotKit.Get<double[]>(start, "HubColumns");
        UnityHubDialog.Fill(hub,
        [
            new UnityHubProject("MyGame", @"D:\MyGame", "6000.0.32f1", new DateTimeOffset(2026, 10, 26, 21, 14, 0, TimeSpan.FromHours(9)), true, true),
            new UnityHubProject("PlatformerProto", @"D:\Unity\PlatformerProto", "2022.3.50f1", new DateTimeOffset(2026, 9, 2, 18, 40, 0, TimeSpan.FromHours(9)), false, true),
            new UnityHubProject("TileTest", @"D:\Unity\TileTest", "2022.3.50f1", new DateTimeOffset(2026, 6, 11, 10, 5, 0, TimeSpan.FromHours(9)), false, true),
        ], cols);
        ShotKit.Get<TextBlock>(start, "_hubMessage").IsVisible = false;
        k.Settle(300);
        main.Title = "Gpixel";
        k.Save(main);
    }

    [Shot("start-update")]
    public static void Update(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        CleanTitle(main);
        var (text, heads) = Changelog();
        var newest = heads[0];
        var current = heads[2].Version.ToString();
        var notes = text[newest.Start..heads[2].Start].Trim();
        var page = new Uri($"{AppUpdate.ReleasesUrl}/tag/v{newest.Version}");
        var asset = new ReleaseAsset("Gpixel-windows-x64.zip", new Uri(AppUpdate.ReleasesUrl + "/download/Gpixel-windows-x64.zip"), 48_000_000);
        var sums = new ReleaseAsset(AppUpdate.ChecksumsAssetName, new Uri(AppUpdate.ReleasesUrl + "/download/" + AppUpdate.ChecksumsAssetName), 300);
        var release = new ReleaseInfo("v" + newest.Version, newest.Version, "Gpixel v" + newest.Version, notes, page, false, false,
            new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(9)), [asset, sums]);
        var plan = new InstallPlan(release, asset, sums, InstallKind.WindowsExe, @"C:\Users\me\AppData\Local\Programs\Gpixel\Gpixel.exe");
        var updater = ShotKit.Get<Updater>(main, "_updater");

        // 위 막대의 업데이트 단추
        ShotKit.Call(main, "ShowUpdateButton", release);
        k.Settle(300);
        var button = ShotKit.Get<Control>(main, "_cbUpdate");
        var zoom = ShotKit.Get<Control>(main, "_cbZoom");
        var b = ShotKit.Bounds(button, main);
        k.Save(main, "start-update-2", crop: new Rect(b.X - 700, 0, main.ClientSize.Width - b.X + 700, ShotKit.Bounds(zoom, main).Bottom + 8), marks: [button]);

        k.Dialog(() => new UpdateDialog(release, plan, null, updater).ShowDialog(main), w =>
        {
            Scrub(w, current);
            ShotKit.Pump(300);
            k.Save(w);
        });
    }

    [Shot("start-privacy")]
    public static void Privacy(ShotKit k)
    {
        var main = Open(k);
        k.Dialog(() => k.Command("편집/환경 설정…"), w =>
        {
            Scrub(w, Changelog().Heads[0].Version.ToString());
            ShotKit.Pump(200);
            var check = ShotKit.Get<Control>(w, "_updateCheck");
            k.Save(w, marks: [check]);
        });
    }

    [Shot("start-feedback")]
    public static void Feedback(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var help = ShotKit.All<MenuItem>(main).Where(m => m.Parent is Menu).Last();
        k.Click(help, settleMs: 400);
        var h = ShotKit.Bounds(help, main);
        k.Save(main, crop: new Rect(0, 0, h.Right + 420, 380), marks: [help]);
        k.Key(main, Avalonia.Input.Key.Escape);

        var (_, heads) = Changelog();
        k.Dialog(() => k.Command("도움말/Gpixel 정보…"), w =>
        {
            Scrub(w, heads[0].Version.ToString());
            ShotKit.Pump(300);
            k.Save(w, "start-feedback-2");
        });
    }
}
