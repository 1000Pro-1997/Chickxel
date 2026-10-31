using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.Core.Model;
using static GuideShots.ScreenDemo;
using static GuideShots.ScreenShot;

namespace GuideShots;

/// <summary>화면 구성: 파일 브라우저, 팔레트, 색 고르기, 미리보기, 문서 탭.</summary>
internal static class ScreenPanelShots
{
    /// <summary>목록의 파일 칸(이름으로 — 목록이 다시 만들어질 수 있어 쓸 때마다 찾는다).</summary>
    private static ListBoxItem FileItem(Control browser, string fileName)
        => ShotKit.All<ListBoxItem>(browser).First(i => i.Tag is { } t && Path.GetFileName(ShotKit.Get<string>(t, "Path")) == fileName);

    /// <summary>
    /// 파일 브라우저: 1 = ① 폴더 트리 ② 썸네일 목록 ③ 검색 ④ 하위 폴더 포함 ⑤ 제목 줄 아이콘 셋 ⑥ .chickxel 배지 ⑦ 저장 안 한 빨간 점,
    /// 2 = ⑧ 여러 개 고름 ⑨ 우클릭 메뉴, 3 = 합치기 창(⑩ 순서 ⑪ 이름 ⑫ 방식 ⑬ 캔버스·기준점·원본 삭제 ⑭ 미리보기 ⑮ 타임라인 모양),
    /// 4 = 앱 휴지통(⑯ 목록·미리보기 ⑰ 되살리기·영구 삭제·비우기 ⑱ 보관 정책).
    /// </summary>
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
        k.Settle(100);
        string projectName = SampleName + ".chickxel", nightName = SampleName + "_night.png";
        var project = FileItem(browser, projectName);
        var badge = ShotKit.All<Control>(project).First(c => !ReferenceEquals(c, project) && ToolTip.GetTip(c) is string && c.Bounds.Width < 30);
        var dot = ShotKit.Get<Control>(project.Tag!, "Dot");
        var tree = ShotKit.Get<Control>(browser, "_tree");
        var list = ShotKit.Get<Control>(browser, "_list");
        var search = ShotKit.Get<Control>(browser, "_search");
        var includeSub = ShotKit.Get<Control>(browser, "_includeSub");
        var headerButtons = ShotKit.All<Button>(browser).Where(x => x.IsEffectivelyVisible).Take(3).Cast<Control>().ToArray();
        var b = ShotKit.Bounds(browser, main);
        var items = ShotKit.All<ListBoxItem>(browser).Where(i => i.IsEffectivelyVisible).Select(i => ShotKit.Bounds(i, main)).Aggregate((x, y) => x.Union(y));
        var panel = new Rect(b.X, b.Y, b.Width, items.Bottom + 12 - b.Y);
        Shot(k, main, null, panel,
        [
            ShotKit.Bounds(tree, main).Deflate(2), items.Inflate(new Thickness(2, 4, 2, 2)),
            search, includeSub, U(main, headerButtons).Inflate(new Thickness(4, 4, 2, 2)), Grow(badge, main, 3), Grow(dot, main, 5),
        ], only: [panel]);

        // 2: 여러 개 고르고 우클릭
        k.Click(FileItem(browser, projectName));
        k.Click(FileItem(browser, nightName), mods: RawInputModifiers.Control);
        k.Click(FileItem(browser, nightName), MouseButton.Right, settleMs: 500);
        var menu = PopupArea(k, main) ?? throw new InvalidOperationException("no file menu");
        var chosen = U(main, FileItem(browser, projectName), FileItem(browser, nightName));
        Shot(k, main, "screen-file-browser-2", panel.Union(menu).Inflate(4), [chosen, Pop(menu)], 8, only: [panel, menu]);
        PopupMenus.CloseAll();
        k.Settle(200);

        // 3: 합치기 창
        k.Dialog(() => ShotKit.Call(browser, "MergeSelected"), w =>
        {
            ShotKit.Pump(300);
            var order = ShotKit.Get<Control>(w, "_order");
            var o = ShotKit.Bounds(order, w);
            var moveButtons = ShotKit.All<Button>(w).Where(x => x.IsEffectivelyVisible && ShotKit.Bounds(x, w) is var r && r.X > o.Right && r.X < o.Right + 60 && r.Y < o.Bottom).Cast<Control>();
            k.Save(w, "screen-file-browser-3", marks:
            [
                U(w, [order, .. moveButtons]),
                ShotKit.Get<Control>(w, "_name"),
                U(w, ShotKit.Get<Control>(w, "_layers"), ShotKit.Get<Control>(w, "_group"), ShotKit.Get<Control>(w, "_skins"), ShotKit.Get<Control>(w, "_frames"), ShotKit.Get<Control>(w, "_blankCover")),
                U(w, ShotKit.Get<Control>(w, "_w"), ShotKit.Get<Control>(w, "_h"), ShotKit.Get<Control>(w, "_anchor"), ShotKit.Get<Control>(w, "_deleteOriginals")),
                ShotKit.Get<Control>(w, "_preview"),
                ShotKit.Get<Control>(w, "_timeline"),
            ], firstNumber: 10);
        });

        // 4: 앱 휴지통(하나 지운 뒤)
        k.Click(FileItem(browser, SampleName + "_hurt.png"));
        ShotKit.Call(browser, "DeleteSelected");
        k.Settle(400);
        k.Dialog(() => ShotKit.Call(browser, "ShowTrash"), w =>
        {
            ShotKit.Find<ListBox>(w).SelectedIndex = 0;
            ShotKit.Pump(500);
            ScrubPaths(k, w);
            ShotKit.Pump(200);
            var buttons = ShotKit.All<Button>(w).Where(x => x.IsEffectivelyVisible && x.Width == 100).Cast<Control>().ToArray();
            k.Save(w, "screen-file-browser-4", marks:
            [
                U(w, ShotKit.Get<Control>(w, "_list"), ShotKit.Get<Control>(w, "_preview")),
                U(w, buttons),
                ShotKit.Get<Control>(w, "_policy"),
            ], firstNumber: 16);
        });
    }

    /// <summary>
    /// 팔레트: 1 = ① 기본색/보조색 ② 쌓인 팔레트(그림에서 만든 팔레트) ③ 새 팔레트 ④ 팔레트 머리글 ⑤ 활성 팔레트 파란 줄 ⑥ 기본색 칸 ⑦ 보조색 칸,
    /// 2 = ⑧ 머리글 메뉴, 3 = 팔레트 색으로 변환 창(⑨ 대상 ⑩ 디더링 ⑪ 전·후 미리보기 ⑫ 색 수).
    /// </summary>
    [Shot("screen-palette")]
    public static void Palette(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var colors = ShotKit.Get<ColorPanel>(main, "_colors");
        var ctx = Ctx(main);
        var extracted = Chickxel.Core.IO.PaletteIO.ExtractFromImage(ctx.VisibleImage(), 24);
        colors.SetPalettes([.. colors.Palettes, new NamedPalette(SampleName, extracted)]);
        k.Settle(300);
        // PaletteSection은 앱 안 형식이라 속성을 이름으로 읽는다.
        var sections = ShotKit.Get<System.Collections.IList>(colors, "_sections").Cast<object>().ToList();
        var grid = ShotKit.Get<Control>(sections[0], "Grid");
        var header = ShotKit.Get<Control>(sections[0], "Header");
        var wolfGrid = ShotKit.Get<Control>(sections[1], "Grid");
        var wolfHeader = ShotKit.Get<Control>(sections[1], "Header");
        Rect Cell(Control g, int i) => ((Rect)ShotKit.Call(g, "CellRect", i)!).Translate((Vector)ShotKit.Bounds(g, main).Position);
        // 그림에서 만든 팔레트를 활성 팔레트(파란 줄)로(칸을 누르는 것과 같음 — 누르기는 앞 샷의 상태에 따라 멈출 때가 있어 직접 정한다).
        ShotKit.Call(colors, "SetActive", sections[1]);
        var primary = new Color32(0x5B, 0x6E, 0xE1);
        var secondary = new Color32(0xFF, 0xFF, 0xFF);
        ctx.Primary = primary;
        ctx.Secondary = secondary;
        k.Settle(300);
        var editor = ShotKit.Get<Control>(colors, "_editor");
        var c = ShotKit.Bounds(colors, main);
        var area = new Rect(c.X, c.Y, c.Width, ShotKit.Bounds(editor, main).Y - c.Y);
        Rect Swatch(Color32 color)
        {
            var shown = ShotKit.Get<IReadOnlyList<Color32>>(grid, "Shown");
            return Cell(grid, shown.ToList().FindIndex(x => x == color)).Inflate(3);
        }
        var wh = ShotKit.Bounds(wolfHeader, main);
        Shot(k, main, null, area,
        [
            Room(U(main, ShotKit.Get<Control>(colors, "_primarySlot"), ShotKit.Get<Control>(colors, "_secondarySlot"))),
            U(main, wolfHeader, wolfGrid).Inflate(new Thickness(-2, 0, -2, 2)),
            ShotKit.Get<Control>(colors, "_addPalette"),
            Room(ShotKit.Bounds(header, main)),
            new Rect(wh.X + wh.Width * 0.7, wh.Bottom - 2, wh.Width * 0.12, 2).Inflate(new Thickness(0, 3)),
            Swatch(primary), Swatch(secondary),
        ], only: [area]);

        // 2: 머리글 메뉴
        k.Click(ShotKit.Get<Control>(sections[0], "MenuButton"), settleMs: 500);
        var menu = PopupArea(k, main) ?? throw new InvalidOperationException("no palette menu");
        Shot(k, main, "screen-palette-2", area.Union(menu).Inflate(4), [Pop(menu)], 8, only: [area, menu]);
        PopupMenus.CloseAll();
        k.Settle(200);
        ShotKit.Call(colors, "SetActive", sections[0]);

        // 3: 팔레트 색으로 변환 창(활성 팔레트 = DB32)
        k.Dialog(() => k.Command("편집/팔레트 색으로 변환…"), w =>
        {
            ShotKit.Pump(400);
            k.Save(w, "screen-palette-3", marks:
            [
                U(w, ShotKit.Get<Control>(w, "_cel"), ShotKit.Get<Control>(w, "_layer"), ShotKit.Get<Control>(w, "_all")),
                U(w, ShotKit.Get<Control>(w, "_noDither"), ShotKit.Get<Control>(w, "_bayer")),
                U(w, ShotKit.Get<Control>(w, "_before"), ShotKit.Get<Control>(w, "_after")),
                Above(ShotKit.Bounds(ShotKit.Get<Control>(w, "_count"), w)),
            ], firstNumber: 9);
        }, settleMs: 800);
    }

    /// <summary>색 고르기: 1 = 사각형(① 알파 ② RGB ③ HSV ④ A·HEX ⑤ 최근 색 ⑥ 방식 탭 ⑦ 사각형), 2~4 = ⑧ 벌집 ⑨ 색상환 ⑩ 슬라이더.</summary>
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
        Control F(string name) => ShotKit.Get<Control>(editor, name);
        colors.PickerMode = ColorPickerMode.Square;
        k.Settle(300);
        var e = ShotKit.Bounds(editor, main).Inflate(2);
        // 이름표(R·H·A)가 왼쪽 끝에 붙은 줄은 편집기 왼쪽부터 감싸고 번호를 왼쪽 여백에 둔다.
        Rect Row(params Control[] cs) { var r = U(main, cs); return Left(new Rect(e.X + 3, r.Y, r.Right - e.X - 3, r.Height)); }
        Shot(k, main, null, e,
        [
            Left(ShotKit.Bounds(F("_alpha"), main)), Row(F("_r"), F("_g"), F("_b")), Row(F("_hNum"), F("_sNum"), F("_vNum")), Row(F("_a"), F("_hex")),
            Left(ShotKit.Bounds(F("_recent"), main)), F("_modeTabs"), Left(ShotKit.Bounds(F("_squarePicker"), main)),
        ], only: [e]);
        (ColorPickerMode Mode, string Field)[] others = [(ColorPickerMode.Honeycomb, "_honeycomb"), (ColorPickerMode.Wheel, "_wheel"), (ColorPickerMode.Sliders, "_sliders")];
        for (int i = 0; i < others.Length; i++)
        {
            colors.PickerMode = others[i].Mode;
            k.Settle(300);
            e = ShotKit.Bounds(editor, main).Inflate(2);
            Shot(k, main, $"screen-color-picker-{i + 2}", e, [Left(ShotKit.Bounds(F(others[i].Field), main))], 8 + i, only: [e]);
        }
    }

    /// <summary>미리보기: 1 = ① 재생 화면 ② FPS ③ 배율 단추 ④ 배경, 2 = ⑤ 그림 위 우클릭 메뉴, 3 = ⑥ 배경 목록.</summary>
    [Shot("screen-preview")]
    public static void Preview(ShotKit k)
    {
        var main = Open(k);
        // 그룹이 없으면 착장 줄이 숨어 아래 막대(FPS·배율·배경) 위가 비므로 번호를 그 위에 둔다.
        Build(k, main, group: false);
        var preview = ShotKit.Get<Control>(main, "_preview");
        var view = ShotKit.Get<Control>(preview, "_view");
        var fps = U(main, ShotKit.FindText(preview, "FPS"), ShotKit.Get<Control>(preview, "_fps"));
        var p = ShotKit.Bounds(preview, main);
        Shot(k, main, null, p,
            [Left(ShotKit.Bounds(view, main)), Above(fps), Above(ShotKit.Bounds(ShotKit.Get<Control>(preview, "_scaleButton"), main)), Above(ShotKit.Bounds(ShotKit.Get<Control>(preview, "_background"), main))],
            only: [p]);

        // 2: 그림 위 우클릭 메뉴
        var v = ShotKit.Bounds(view, main);
        k.Click(main, new Point(v.X + 40, v.Y + 30), MouseButton.Right, settleMs: 500);
        var menu = PopupArea(k, main) ?? throw new InvalidOperationException("no preview menu");
        Shot(k, main, "screen-preview-2", p.Union(menu).Inflate(4), [Pop(menu)], 5, only: [p, menu]);
        PopupMenus.CloseAll();
        k.Settle(200);

        // 3: 배경 목록
        var background = ShotKit.Get<ComboBox>(preview, "_background");
        background.IsDropDownOpen = true;
        k.Settle(500);
        var list = PopupArea(k, main) ?? throw new InvalidOperationException("no background list");
        Shot(k, main, "screen-preview-3", p.Union(list).Inflate(4), [Pop(list)], 6, only: [p, list]);
        background.IsDropDownOpen = false;
    }

    /// <summary>
    /// 문서 탭: 1(2배) = ① 탭 제목 ② 빨간 점 ③ 전체 경로 도움말 ④ 닫기 X ⑤ +, 2 = ⑥ 탭 우클릭 메뉴,
    /// 3 = 좌우로 나눔(⑦ 다른 쪽으로 옮기기 단추 ⑧ 오른쪽 칸 ⑨ 포커스된 칸의 강조색 선).
    /// </summary>
    [Shot("screen-tabs")]
    public static void Tabs(ShotKit k)
    {
        var main = OpenFolder(k);
        Build(k, main);
        var dir = Path.Combine(k.WorkDir, FolderName);
        ShotKit.Call(main, "OpenFile", Path.Combine(dir, SampleName + "_night.png"));
        k.Settle(300);
        ShotKit.Call(main, "OpenFile", Path.Combine(dir, SampleName + "_hurt.png"));
        k.Settle(300);
        Ctx(main).Structural(() => Project(main).SetFrameDuration(0, 120));
        k.Settle(300);
        var timeline = ShotKit.Get<Control>(main, "_timeline");
        List<Control> Strips() => ShotKit.All<Control>(main).Where(c => c.GetType().Name == "DocumentTabStrip" && c.IsEffectivelyVisible).ToList();
        Rect Area()
        {
            var r = Strips().Select(s => ShotKit.Bounds(s, main)).Aggregate((a, b) => a.Union(b));
            return new Rect(r.X, r.Y, r.Width, ShotKit.Bounds(timeline, main).Y - r.Y);
        }
        var strip = Strips()[0];
        var tabs = ShotKit.Get<List<(Border Box, TextBlock Title, Border Close)>>(strip, "_tabs");
        var active = tabs.Last();
        var dot = ShotKit.All<Control>(active.Box).First(c => c.GetType().Name == "IconView" && c.IsVisible && c.Bounds.Width <= 10);
        // 마우스를 올린 첫 탭의 전체 경로 도움말(임시 작업 폴더 대신 예시 경로로)
        var first = tabs[0].Box;
        if (ToolTip.GetTip(first) is string tip)
            ToolTip.SetTip(first, tip.Replace(Path.Combine(k.WorkDir, FolderName), @"D:\MyGame\Assets\" + FolderName, StringComparison.OrdinalIgnoreCase).Replace('/', '\\'));
        k.Hover(main, ShotKit.Center(first, main), 300);
        ToolTip.SetIsOpen(first, true);
        k.Settle(500);
        var tipArea = PopupArea(k, main, tips: true) ?? throw new InvalidOperationException("no tab tooltip");
        var s = ShotKit.Bounds(strip, main);
        var newButton = ShotKit.Get<Control>(strip, "_newButton");
        var row = new Rect(s.X, s.Y, ShotKit.Bounds(newButton, main).Right + 16 - s.X, s.Height);
        // 작은 표시가 붙어 있어 2배로 키워 찍고, 번호는 탭 줄 위 여백에.
        static Rect Up(Rect r) => r.Inflate(new Thickness(1, 9, 1, 1));
        Shot(k, main, null, row.Union(tipArea),
        [
            Up(ShotKit.Bounds(active.Title, main)), Up(Grow(dot, main, 2)), tipArea.Inflate(new Thickness(4, 4, 1, 1)),
            Up(Grow(active.Close, main, 1)), Up(Grow(newButton, main, 1)),
        ], only: [row, tipArea], scale: 2);
        ToolTip.SetIsOpen(first, false);
        k.Hover(main, new Point(s.Center.X, s.Bottom + 200), 300);

        // 2: 탭 우클릭 메뉴
        k.Click(tabs[0].Box, MouseButton.Right, settleMs: 500);
        var menu = PopupArea(k, main) ?? throw new InvalidOperationException("no tab menu");
        var strip2 = ShotKit.Bounds(strip, main);
        Shot(k, main, "screen-tabs-2", strip2.Union(menu).Inflate(4), [Pop(menu)], 6, only: [strip2, menu]);
        PopupMenus.CloseAll();
        k.Settle(200);

        // 3: 다른 쪽으로 옮겨 좌우로 나눔
        k.Command("보기/다른 쪽으로 옮기기");
        k.Settle(400);
        var ordered = Strips().OrderBy(x => ShotKit.Bounds(x, main).X).ToList();
        var right = ordered.Last();
        var focused = ordered.First(x => ShotKit.Get<bool>(x, "_highlight"));
        var r = ShotKit.Bounds(right, main);
        var f = ShotKit.Bounds(focused, main);
        var a = Area();
        // ⑦ 왼쪽 칸의 옮기기 단추, ⑧ 오른쪽 칸, ⑨ 포커스 선(번호가 탭 글자를 가리지 않게 선의 오른쪽 끝 부분만 감쌈)
        Shot(k, main, "screen-tabs-3", a,
        [
            Grow(ShotKit.Get<Control>(ordered[0], "_splitButton"), main, 2),
            new Rect(r.X, r.Y, r.Width, a.Bottom - r.Y).Deflate(4),
            new Rect(f.X + f.Width * 0.84, f.Bottom - 2, f.Width * 0.1, 2).Inflate(new Thickness(0, 4)),
        ], 7, only: [a]);
    }
}
