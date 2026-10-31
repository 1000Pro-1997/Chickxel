using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>위 메뉴 펼치기와 팝업(메뉴·펼친 목록) 안 컨트롤의 창 좌표 — 시작하기·언어·단축키 장이 같이 쓴다.</summary>
internal static class StartMenus
{
    /// <summary>팝업 안 컨트롤의 <paramref name="top"/> 창 좌표 사각형(팝업 위치는 플랫폼 구현의 Position — 공개 API가 아님).</summary>
    public static Rect PopupRect(Control c, TopLevel top)
    {
        var host = TopLevel.GetTopLevel(c) ?? throw new InvalidOperationException("detached");
        var local = ShotKit.Bounds(c, host);
        if (ReferenceEquals(host, top)) return local;
        var impl = host.PlatformImpl ?? throw new InvalidOperationException("no popup impl");
        var pos = (PixelPoint)impl.GetType().GetProperty("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(impl)!;
        var origin = top is Window w ? w.Position : default;
        return local.Translate(new Vector(pos.X - origin.X, pos.Y - origin.Y));
    }

    /// <summary><paramref name="index"/>번째 위 메뉴(파일 0, 편집 1, 보기 2, 프레임·레이어 3, 도구 4, AI 5, 도움말 6)를 펼친다.</summary>
    public static MenuItem Open(ShotKit k, MainForm main, int index)
    {
        var top = ShotKit.Find<Menu>(main).Items.OfType<MenuItem>().ElementAt(index);
        k.Click(top, settleMs: 500);
        return top;
    }

    /// <summary>펼친 메뉴에서 이름이 <paramref name="name"/>으로 시작하는 항목.</summary>
    public static MenuItem Item(MenuItem menu, string name)
        => menu.Items.OfType<MenuItem>().FirstOrDefault(m => Name(m).StartsWith(name, StringComparison.Ordinal))
           ?? throw new InvalidOperationException("no menu item " + name);

    private static string Name(MenuItem m) => m.Header switch
    {
        string s => s.Replace("_", ""),
        Grid g => g.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "",
        TextBlock t => t.Text ?? "",
        _ => "",
    };

    /// <summary><paramref name="first"/>~<paramref name="last"/> 항목 줄을 모두 담는 창 좌표 사각형.</summary>
    public static Rect Rows(MenuItem first, MenuItem last, TopLevel top) => PopupRect(first, top).Union(PopupRect(last, top));

    /// <summary>메뉴 항목 오른쪽의 단축키 글자(없으면 null).</summary>
    public static TextBlock? Key(MenuItem m)
        => m.Header is Grid g ? g.Children.OfType<TextBlock>().Skip(1).FirstOrDefault(t => !string.IsNullOrEmpty(t.Text)) : null;

    /// <summary>
    /// <paramref name="first"/>~<paramref name="last"/> 항목들의 단축키 글자를 모두 담는 창 좌표 사각형(키 열). 글자 한 개짜리 열은 번호 동그라미가
    /// 키를 가리지 않게 왼쪽(빈 곳)으로 넓힌다.
    /// </summary>
    public static Rect Keys(MenuItem menu, MenuItem first, MenuItem last, TopLevel top)
    {
        var items = menu.Items.OfType<MenuItem>().ToList();
        var r = items.Skip(items.IndexOf(first)).Take(items.IndexOf(last) - items.IndexOf(first) + 1)
            .Select(Key).OfType<TextBlock>().Select(t => PopupRect(t, top)).Aggregate((a, b) => a.Union(b));
        const double minWidth = 48;
        return r.Width >= minWidth ? r : new Rect(r.Right - minWidth, r.Y, minWidth, r.Height);
    }

    /// <summary>위 메뉴 머리와 펼친 아래 메뉴를 담는 자르기 영역.</summary>
    public static Rect Crop(MainForm main, MenuItem menu)
    {
        var head = ShotKit.Bounds(menu, main);
        var items = menu.Items.OfType<MenuItem>().Where(m => m.IsVisible).ToList();
        var whole = Rows(items[0], items[^1], main).Inflate(6);
        double left = Math.Max(0, Math.Min(head.X, whole.X) - 12);
        return new Rect(left, 0, whole.Right + 16 - left, whole.Bottom + 12);
    }

    /// <summary>위 메뉴 머리 사각형(번호 동그라미가 글자를 가리지 않게 왼쪽을 조금 넓힘).</summary>
    public static Rect Head(MenuItem menu, TopLevel top)
    {
        var r = ShotKit.Bounds(menu, top);
        return new Rect(r.X - 5, r.Y, r.Width + 5, r.Height);
    }
}

/// <summary>시작하기: 시작 화면, 업데이트(위 막대 단추·업데이트 창·도움말 메뉴·환경 설정), 네트워크(자동 확인 설정), 제보(도움말 메뉴·정보 창).</summary>
internal static class StartShots
{
    /// <summary>CHANGELOG.md의 버전 머리(## vX.Y.Z (날짜)) 위치들.</summary>
    internal static (string Text, List<(SemVersion Version, int Start)> Heads) Changelog()
    {
        var file = Path.Combine(ShotKit.Root, "CHANGELOG.md");
        var text = File.ReadAllText(file).Replace("\r\n", "\n");
        var heads = Regex.Matches(text, @"^## v(\d+\.\d+\.\d+)[^\n]*$", RegexOptions.Multiline)
            .Select(m => (SemVersion.TryParse(m.Groups[1].Value, out var v) ? v : new SemVersion(0, 0, 0), m.Index)).ToList();
        return (text, heads);
    }

    /// <summary>
    /// 개발 빌드에만 보이는 글자를 릴리스 빌드 모습으로: 버전(개발 빌드 표시 포함) → <paramref name="version"/>,
    /// 환경 설정의 "개발 빌드라 자동 확인 안 함" 안내 → 릴리스 안내, 샷 임시 설정 파일 경로 → Windows 기본 위치.
    /// </summary>
    internal static void Scrub(Visual root, string version)
    {
        string devNote = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PreferencesDialog_PreferencesDialog_4B32B49831, AppInfo.VersionText);
        string releaseNote = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PreferencesDialog_PreferencesDialog_80B1F80264, version);
        foreach (var tb in ShotKit.All<TextBlock>(root))
        {
            if (tb.Text is not { } original) continue;
            var t = original == devNote ? releaseNote : original;
            t = t.Replace(AppSettings.FilePath, @"%APPDATA%\Chickxel\settings.json")
                .Replace(UiStrings.S_AboutDialog_AboutDialog_27606A6DB7, "")
                .Replace(AppInfo.VersionText, version);
            if (t != original) tb.Text = t;
        }
    }

    // 시작 화면: ① 폴더 열기 ② 최근 폴더 ③ 파일 브라우저 ④ Unity Hub 프로젝트 목록.
    [Shot("start-first-run")]
    public static void FirstRun(ShotKit k)
    {
        var main = k.Main();
        k.Settle(300);
        var start = ShotKit.Get<StartPanel>(main, "_start");
        string root = @"D:\MyGame\Assets";
        start.SetRecent(
            [root + @"\Sprites", @"D:\Art\Pixel practice"],
            [root + @"\Sprites\Wolf Adventurer.chickxel", @"D:\Art\Pixel practice\slime.chickxel"]);
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
        main.Title = "Chickxel";
        var openFolder = ShotKit.FindText(start, StartStrings.OpenFolder, ancestorControl: true);
        var recent = ShotKit.Get<ListBox>(start, "_recentFolders");
        var browser = ShotKit.Bounds(ShotKit.Get<Control>(main, "_browser"), main);
        // Unity Hub 목록은 시작 화면 밖으로 넓게 깔려 있어 보이는 부분(시작 화면 안)만 표시한다. 번호 동그라미가 제목·글자를 가리지 않게
        // 왼쪽 빈 곳으로 조금 넓히고, 파일 브라우저는 그림 목록이 나오는 곳(머리줄·안내 아래)을 표시한다.
        var hubRect = ShotKit.Bounds(hub, main).Intersect(ShotKit.Bounds(start, main).Deflate(new Thickness(0, 0, 24, 24)));
        k.Save(main, marks: [Pad(openFolder, main, 8), Pad(recent, main, 12), new Rect(browser.X, browser.Y + 56, browser.Width, browser.Height - 56), new Rect(hubRect.X - 12, hubRect.Y, hubRect.Width + 12, hubRect.Height)]);
    }

    private static Rect Pad(Control c, Visual top, double left)
    {
        var r = ShotKit.Bounds(c, top);
        return new Rect(r.X - left, r.Y, r.Width + left, r.Height);
    }

    // 업데이트: ① 위 막대 단추, 업데이트 창 ②~⑥, 도움말 메뉴 ⑦, 환경 설정 ⑧.
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
        var asset = new ReleaseAsset("Chickxel-windows-x64.zip", new Uri(AppUpdate.ReleasesUrl + "/download/Chickxel-windows-x64.zip"), 48_000_000);
        var sums = new ReleaseAsset(AppUpdate.ChecksumsAssetName, new Uri(AppUpdate.ReleasesUrl + "/download/" + AppUpdate.ChecksumsAssetName), 300);
        var release = new ReleaseInfo("v" + newest.Version, newest.Version, "Chickxel v" + newest.Version, notes, page, false, false,
            new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(9)), [asset, sums]);
        var plan = new InstallPlan(release, asset, sums, InstallKind.WindowsExe, @"C:\Users\me\AppData\Local\Programs\Chickxel\Chickxel.exe");
        var updater = ShotKit.Get<Updater>(main, "_updater");

        // ① 위 막대의 업데이트 단추
        ShotKit.Call(main, "ShowUpdateButton", release);
        k.Settle(300);
        var button = ShotKit.Get<Control>(main, "_cbUpdate");
        var zoom = ShotKit.Get<Control>(main, "_cbZoom");
        var b = ShotKit.Bounds(button, main);
        k.Save(main, "start-update", crop: new Rect(b.X - 420, 0, main.ClientSize.Width - b.X + 420, ShotKit.Bounds(zoom, main).Bottom + 8), marks: [button]);

        // ②~⑥ 업데이트 창: 지금 업데이트 · 나중에 · 다음 업데이트까지 띄우지 않기 · 새/지금 버전 · 바뀐 점
        k.Dialog(() => new UpdateDialog(release, plan, null, updater).ShowDialog(main), w =>
        {
            Scrub(w, current);
            ShotKit.Pump(300);
            var body = ShotKit.Get<StackPanel>(w, "Body");
            k.Save(w, "start-update-2", firstNumber: 2, marks:
            [
                ShotKit.Get<Button>(w, "OkButton"),
                ShotKit.Get<Button>(w, "CancelButtonControl"),
                ShotKit.Get<Button>(w, "_skip"),
                body.Children[1],
                body.Children[2],
            ]);
        });

        // ⑦ 도움말 > 업데이트 확인…
        var help = StartMenus.Open(k, main, 6);
        var check = StartMenus.Item(help, MainStrings.Menu_Help_CheckAsync_236.TrimEnd('…'));
        k.Save(main, "start-update-3", crop: StartMenus.Crop(main, help), marks: [StartMenus.PopupRect(check, main)], firstNumber: 7);
        k.Key(main, Avalonia.Input.Key.Escape);
        k.Key(main, Avalonia.Input.Key.Escape);

        // ⑧ 환경 설정 > 일반의 시작할 때 새 버전 확인
        k.Dialog(() => k.Command("편집/환경 설정…"), w =>
        {
            Scrub(w, heads[0].Version.ToString());
            ShotKit.Pump(200);
            var box = ShotKit.Get<CheckBox>(w, "_updateCheck");
            var panel = (Panel)box.Parent!;
            int i = panel.Children.IndexOf(box);
            var area = new[] { panel.Children[i - 1], box, panel.Children[i + 1] }.Select(c => ShotKit.Bounds(c, w)).Aggregate((x, y) => x.Union(y));
            area = new Rect(0, area.Y - 4, area.Right + 12, area.Height + 14);
            k.Save(w, "start-update-4", crop: area, marks: [Pad(box, w, 12)], firstNumber: 8);
        });
    }

    // 네트워크와 개인정보: ① 업데이트 확인을 끄는 곳.
    [Shot("start-privacy")]
    public static void Privacy(ShotKit k)
    {
        Open(k);
        k.Dialog(() => k.Command("편집/환경 설정…"), w =>
        {
            Scrub(w, Changelog().Heads[0].Version.ToString());
            ShotKit.Pump(200);
            var check = ShotKit.Get<Control>(w, "_updateCheck");
            k.Save(w, marks: [Pad(check, w, 12)]);
        });
    }

    // 제보: ① 도움말 ② 제보·아이디어(Discord) ③ Chickxel 정보, 정보 창 ④ Discord ⑤ 문제 신고 ⑥ 버전 ⑦ 개발자에게 커피 사주기.
    [Shot("start-feedback")]
    public static void Feedback(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var help = StartMenus.Open(k, main, 6);
        var report = StartMenus.Item(help, MainStrings.Menu_Help_Discord.TrimEnd('…'));
        var about = StartMenus.Item(help, MainStrings.Menu_Help_AboutDialog_238.TrimEnd('…'));
        k.Save(main, crop: StartMenus.Crop(main, help), marks: [StartMenus.Head(help, main), StartMenus.PopupRect(report, main), StartMenus.PopupRect(about, main)]);
        k.Key(main, Avalonia.Input.Key.Escape);
        k.Key(main, Avalonia.Input.Key.Escape);

        var (_, heads) = Changelog();
        k.Dialog(() => k.Command("도움말/Chickxel 정보…"), w =>
        {
            Scrub(w, heads[0].Version.ToString());
            ShotKit.Pump(300);
            string versionHead = UiStrings.S_AboutDialog_AboutDialog_280F145056.Split('{')[0];
            var version = ShotKit.All<TextBlock>(w).First(t => t.Text?.StartsWith(versionHead, StringComparison.Ordinal) == true);
            var v = ShotKit.Bounds(version, w);
            var versionRect = new Rect(v.X - 8, v.Y, v.Width + 8, v.Height);
            k.Save(w, "start-feedback-2", firstNumber: 4, marks:
            [
                ShotKit.FindText(w, UiStrings.About_Discord, ancestorControl: true),
                ShotKit.FindText(w, UiStrings.S_AboutDialog_AboutDialog_9A9BD8295A, ancestorControl: true),
                versionRect,
                ShotKit.FindText(w, UiStrings.About_BuyCoffee, ancestorControl: true),
            ]);
        });
    }
}
