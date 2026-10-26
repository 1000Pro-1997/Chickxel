using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Gpixel.App;
using Gpixel.Core.Model;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>화면 구성: 파일 브라우저, 팔레트, 색 고르기, 미리보기, 문서 탭.</summary>
internal static class ScreenPanelShots
{
    /// <summary>목록의 파일 칸(이름으로).</summary>
    private static ListBoxItem FileItem(Control browser, string fileName)
        => ShotKit.All<ListBoxItem>(browser).First(i => i.Tag is { } t && Path.GetFileName(ShotKit.Get<string>(t, "Path")) == fileName);

    [Shot("screen-file-browser")]
    public static void FileBrowser(ShotKit k)
    {
        var main = OpenFolder(k);
        Build(k, main);
        var browser = ShotKit.Get<Control>(main, "_browser");
        // 저장 안 한 변경(빨간 점)
        Ctx(main).Structural(() => Project(main).SetFrameDuration(0, 120));
        k.Settle(1500);
        PrettyRoot(main);
        var b = ShotKit.Bounds(browser, main);
        var headerButtons = ShotKit.All<Button>(browser).Where(x => x.IsEffectivelyVisible).Take(3).Cast<Control>().ToArray();
        k.Save(main, crop: new Rect(b.X, b.Y, b.Width + 4, 560), marks: headerButtons);

        // 2: 여러 개 고르고 우클릭
        var project = FileItem(browser, SampleName + ".gpixel");
        var night = FileItem(browser, SampleName + L("_밤", "_night") + ".png");
        k.Click(project);
        k.Click(night, mods: RawInputModifiers.Control);
        k.Click(night, MouseButton.Right, settleMs: 400);
        k.Save(main, "screen-file-browser-2", crop: new Rect(b.X, b.Y, 560, 640));
        k.Key(main, Key.Escape);

        // 3: 합치기 창
        k.Dialog(() => ShotKit.Call(browser, "MergeSelected"), w => k.Save(w, "screen-file-browser-3"));

        // 4: 앱 휴지통(하나 지운 뒤)
        var hurt = FileItem(browser, SampleName + L("_피격", "_hurt") + ".png");
        k.Click(hurt);
        ShotKit.Call(browser, "DeleteSelected");
        k.Settle(400);
        k.Dialog(() => ShotKit.Call(browser, "ShowTrash"), w =>
        {
            ShotKit.Find<ListBox>(w).SelectedIndex = 0;
            ShotKit.Pump(500);
            ScrubPaths(k, w);
            ShotKit.Pump(200);
            k.Save(w, "screen-file-browser-4");
        });
    }

    [Shot("screen-palette")]
    public static void Palette(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var colors = ShotKit.Get<Control>(main, "_colors");
        var ctx = Ctx(main);
        var panel = (ColorPanel)colors;
        var extracted = Gpixel.Core.IO.PaletteIO.ExtractFromImage(ctx.VisibleImage(), 24);
        panel.SetPalettes([.. panel.Palettes, new NamedPalette(SampleName, extracted)]);
        k.Settle(300);
        ctx.Primary = new Color32(0x5B, 0x6E, 0xE1);
        ctx.Secondary = new Color32(0xFF, 0xFF, 0xFF);
        k.Settle(200);
        var c = ShotKit.Bounds(colors, main);
        k.Save(main, crop: new Rect(c.X, c.Y, c.Width, 250));

        var sections = ShotKit.Get<System.Collections.IList>(colors, "_sections");
        var menu = (Button)sections[0]!.GetType().GetProperty("MenuButton")!.GetValue(sections[0])!;
        k.Click(menu, settleMs: 400);
        k.Save(main, "screen-palette-2", crop: new Rect(c.X, c.Y, 560, 560), marks: [menu]);
    }

    [Shot("screen-color-picker")]
    public static void ColorPicker(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var colors = ShotKit.Get<ColorPanel>(main, "_colors");
        var ctx = Ctx(main);
        foreach (var c in new[] { 0xD95763u, 0xFBF236u, 0x6ABE30u, 0x5FCDE4u, 0x76428Au })
        {
            ctx.Primary = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c);
            ctx.PushRecentColor(ctx.Primary);
        }
        ctx.Primary = new Color32(0x5B, 0x6E, 0xE1);
        var editor = ShotKit.Get<Control>(colors, "_editor");
        string[] names = ["screen-color-picker", "screen-color-picker-2", "screen-color-picker-3", "screen-color-picker-4"];
        ColorPickerMode[] modes = [ColorPickerMode.Square, ColorPickerMode.Honeycomb, ColorPickerMode.Wheel, ColorPickerMode.Sliders];
        for (int i = 0; i < modes.Length; i++)
        {
            colors.PickerMode = modes[i];
            k.Settle(300);
            k.SaveControl(editor, names[i], pad: 4);
        }
    }

    [Shot("screen-preview")]
    public static void Preview(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var preview = ShotKit.Get<Control>(main, "_preview");
        var view = ShotKit.Get<Control>(preview, "_view");
        var p = ShotKit.Bounds(preview, main);
        k.Save(main, crop: p.Inflate(4).Intersect(new Rect(main.ClientSize)),
            marks: [ShotKit.Get<Control>(preview, "_fps"), ShotKit.Get<Control>(preview, "_scaleButton"), ShotKit.Get<Control>(preview, "_background")]);
        k.Click(view, MouseButton.Right, settleMs: 400);
        k.Save(main, "screen-preview-2", crop: new Rect(p.X - 260, p.Y, p.Width + 264, 300).Intersect(new Rect(main.ClientSize)));
    }

    [Shot("screen-tabs")]
    public static void Tabs(ShotKit k)
    {
        var main = OpenFolder(k);
        Build(k, main);
        var dir = Path.Combine(k.WorkDir, FolderName);
        ShotKit.Call(main, "OpenFile", Path.Combine(dir, SampleName + L("_밤", "_night") + ".png"));
        k.Settle(300);
        ShotKit.Call(main, "OpenFile", Path.Combine(dir, SampleName + L("_피격", "_hurt") + ".png"));
        k.Settle(300);
        Ctx(main).Structural(() => Project(main).SetFrameDuration(0, 120));
        k.Settle(300);
        var timeline = ShotKit.Get<Control>(main, "_timeline");
        Rect Area()
        {
            var strips = ShotKit.All<Control>(main).Where(c => c.GetType().Name == "DocumentTabStrip" && c.IsEffectivelyVisible).ToList();
            var r = strips.Select(s => ShotKit.Bounds(s, main)).Aggregate((a, b) => a.Union(b));
            return new Rect(r.X, r.Y, r.Width, ShotKit.Bounds(timeline, main).Y - r.Y);
        }
        k.Save(main, crop: Area());

        // 2: 다른 쪽으로 옮겨 좌우로 나눔
        k.Command("보기/다른 쪽으로 옮기기");
        k.Settle(400);
        k.Save(main, "screen-tabs-2", crop: Area());

        // 3: 탭 우클릭 메뉴
        var strip = ShotKit.All<Control>(main).First(c => c.GetType().Name == "DocumentTabStrip" && c.IsEffectivelyVisible);
        var firstTab = ShotKit.All<Border>(strip).First(x => x.IsEffectivelyVisible && x.Bounds.Width > 40);
        k.Click(firstTab, MouseButton.Right, settleMs: 400);
        var a = Area();
        k.Save(main, "screen-tabs-3", crop: new Rect(a.X, a.Y, 640, 260));
    }
}
