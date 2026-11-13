using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;
using DPoint = System.Drawing.Point;
using DSize = System.Drawing.Size;

namespace GuideShots;

/// <summary>
/// 타일맵 레이어 장(tilemap-*) 그림. 예시 프로젝트는 기본 타일셋(샌드박스 지상·동굴)으로 만든 작은 맵(흙 언덕·풀·나무, 물웅덩이와 나무 발판·동전,
/// 돌 속 동굴의 배경 벽·횃불·상자)과 하늘 레이어, 프로젝트에만 담긴 타일셋 둘(16px "Dungeon props", 8px "UI icons")이다.
/// 번호는 쪽(h3)마다 이어진다 — 가이드 글의 ①②…와 같은 순서.
/// </summary>
internal static class TilemapShots
{
    private const int Cell = 16, MapW = 24, MapH = 14;
    private const string MapLayer = "Tilemap";
    private const string PropsName = "Dungeon props";
    private const string IconsName = "UI icons";

    // ───────────── 만들기 · 화면 ─────────────

    [Shot("tilemap-create")]
    public static void Create(ShotKit k)
    {
        var main = Open(k);
        // ① 프레임·레이어 > 새 타일맵 레이어
        var item = OpenCommandMenu(k, main, "새 타일맵 레이어");
        SaveTopMenu(k, main, "tilemap-create", [item], 1);
        CloseMenus(k, main);
        // ② 타임라인 레이어 우클릭 > 새 타일맵 레이어
        int row = Index(main, "Sky");
        RightClickRow(k, main, row);
        SaveRowMenu(k, main, "tilemap-create-2", row, [MenuItemByText(UiStrings.Tile_MenuAddLayer)], 2);
    }

    [Shot("tilemap-screen")]
    public static void Screen(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        PickAutoSet(k, main, DomainStrings.TileBuiltIn_Dirt);
        HoverCell(k, main, 6, 6);
        var canvas = Canvas(main);
        var c = ShotKit.Bounds(canvas, main);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        // ① 타일 도구 ② 타일셋 패널 ③ 도구 쓰는 법 ④ 타일맵 보기 단추 ⑤ 도구 설정 타일 묶음 ⑥ 층 묶음 ⑦ 칸 정보
        k.Save(main, marks:
        [
            ShotKit.Get<Control>(main, "_tileToolBar"), Panel(main), ShotKit.Get<Control>(main, "_cbHint"),
            Union(main, ShotKit.Get<Control>(main, "_cbTileCells"), ShotKit.Get<Control>(main, "_cbTileAnim"), ShotKit.Get<Control>(main, "_cbTileView")),
            ShotKit.Get<Control>(options, "_tileBox"), ShotKit.Get<Control>(options, "_tileFloorBox"),
            HudRect(c),
        ]);
    }

    [Shot("tilemap-screen-view")]
    public static void ScreenView(ShotKit k)
    {
        var main = Open(k);
        var view = ShotKit.Get<Control>(main, "_cbTileView");
        var arrow = ((StackPanel)((StackPanel)view).Children[0]).Children[1];
        k.Click(arrow, settleMs: 400);
        var popup = OpenPopupsRect(main) ?? throw new InvalidOperationException("floor view menu not open");
        k.Save(main, crop: ShotKit.Bounds(view, main).Union(popup).Inflate(12));
    }

    [Shot("tilemap-tools")]
    public static void Tools(ShotKit k)
    {
        var main = Open(k);
        PickAutoSet(k, main, DomainStrings.TileBuiltIn_Dirt);
        HoverCell(k, main, 6, 6);
        var c = ShotKit.Bounds(Canvas(main), main);
        var area = MapRect(main, 0, 2, 13, 12).Inflate(12);
        area = area.Union(HudRect(c).Inflate(6));
        // ① 찍으면 붙을 모양(반투명) ② 칸 정보
        k.Save(main, crop: area, marks: [MapRect(main, 5, 5, 8, 8), HudRect(c)]);
    }

    // ───────────── 타일셋 패널 · 지금 찍을 것 · 층 ─────────────

    [Shot("tilemap-panel")]
    public static void PanelShot(ShotKit k)
    {
        var main = Open(k);
        PickAutoSet(k, main, DomainStrings.TileBuiltIn_Dirt);
        var panel = Panel(main);
        var s = Sections(main)[0];
        var head = Head(s);
        var auto = AutoView(s);
        var palette = Palette(s);
        // ① 머리글(이름·타일 수) ② 메뉴 단추 ③ 자동 타일 세트 칸 ④ 새 세트 칸 ⑤ 타일 목록 ⑥ 타일셋 더하기
        var add = ShotKit.Get<Control>(panel, "_addTileset");
        k.Save(main, crop: PanelArea(main), marks:
        [
            Union(main, ShotKit.Get<Control>(head, "Title"), ShotKit.Get<Control>(head, "Count")),
            ShotKit.Get<Control>(head, "MenuButton"), auto, LastSlot(main, auto), palette, add,
        ]);
        // ⑦ 머리글 메뉴
        k.Click(ShotKit.Get<Control>(head, "MenuButton"), settleMs: 500);
        SavePanelMenu(k, main, "tilemap-panel-2", 7);
        CloseMenus(k, main);
        // ⑧ 타일 우클릭 메뉴(나무)
        var tree = Sandbox(main).Find(TileAt(Sandbox(main), 0, 0))!;
        k.Click(main, PaletteSlotCenter(main, palette, palette.DisplayPos(tree)), MouseButton.Right, settleMs: 500);
        SavePanelMenu(k, main, "tilemap-panel-3", 8);
    }

    [Shot("tilemap-pick")]
    public static void Pick(ShotKit k)
    {
        var main = Open(k);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        var tileBox = ShotKit.Get<Control>(options, "_tileBox");
        // ① 지금 찍을 것 카드 ② 구성
        PickAutoSet(k, main, DomainStrings.TileBuiltIn_Grass);
        k.SaveControl(tileBox, pad: 6, marks: [ShotKit.Get<Control>(options, "_tilePick"), ScreenDemo.Named(tileBox, "TileAutoPartsView")]);
        // ③ 뒤집기·회전 ④ 무작위 (타일 붓으로 나무를 고른 상태)
        var palette = Palette(Sections(main)[0]);
        var tree = Sandbox(main).Find(TileAt(Sandbox(main), 2, 0))!;
        k.Click(main, PaletteSlotCenter(main, palette, palette.DisplayPos(tree)));
        k.Settle(300);
        k.SaveControl(tileBox, "tilemap-pick-2", pad: 6, marks: [ShotKit.Get<Control>(options, "_tileTransformRow"), ShotKit.Get<Control>(options, "_tileRandomRow")], firstNumber: 3);
    }

    [Shot("tilemap-floors")]
    public static void Floors(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        var ts = Sandbox(main);
        int ground = FloorId(ts, DomainStrings.Tile_FloorGround), wall = FloorId(ts, DomainStrings.Tile_FloorWall);
        PickAutoSet(k, main, DomainStrings.TileBuiltIn_Dirt);
        ctx.SetTileFloorState(wall, locked: true);
        ctx.TileFloor = ground;
        k.Settle(300);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        var box = ShotKit.Get<Control>(options, "_tileFloorBox");
        var list = ScreenDemo.Named(box, "TileFloorList");
        var rows = ShotKit.Get<Panel>(list, "_rows").Children.OfType<Panel>().ToList();
        var groundRow = rows[ts.Floors.Length - 1 - ts.FloorOrder(ground)];
        var wallRow = rows[^1];
        Control Cell(Panel row, int i) => row.Children[i];
        // ① 자동 ② 수동으로 고른 층 줄(지금 찍을 층 막대) ③ 보이기 ④ 잠금 ⑤ 찍은 칸 수 ⑥ 위로·아래로 ⑦ 층 더하기
        k.SaveControl(box, pad: 14, marks:
        [
            ShotKit.Get<Control>(list, "_auto"), Union(main, Cell(groundRow, 0), Cell(groundRow, 3)), Cell(rows[1], 1), Cell(wallRow, 2),
            Cell(rows[2], 4), Union(main, Cell(rows[0], 5), Cell(rows[0], 6)), FindButton(box, UiStrings.Tileset_AddFloor),
        ]);
        // 층 보기: 모든 층 / 고른 층 강조 / 고른 층만 (바닥 층)
        ctx.SetTileFloorState(wall, locked: false);
        var area = MapRect(main, 0, 1, MapW - 1, MapH - 1);
        var shots = new List<(Avalonia.Media.Imaging.Bitmap, Rect)>();
        foreach (var mode in new[] { TileFloorViewMode.All, TileFloorViewMode.Dim, TileFloorViewMode.Only })
        {
            ctx.TileViewMode = mode;
            k.Settle(400);
            shots.Add((k.Capture(main), area));
        }
        GuideScreenUiCompareShots.Compose(k, "tilemap-floors-compare", shots);
    }

    // ───────────── 타일셋 고르기·관리 · 여러 타일셋 ─────────────

    [Shot("tilemap-choose")]
    public static void Choose(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        var open = ctx.CurrentTilesetSources.Select(t => t.Key).ToHashSet();
        // ① 열려 있음 ② 칸 크기가 달라 못 고름 ③ 기본 타일셋(새 사본) ④ 타일셋 관리…
        k.Dialog(() => TilesetChoiceDialog.Run(main, ctx, UiStrings.Tileset_AddTileset, open, new DSize(Cell, Cell)), w =>
        {
            var rows = ShotKit.Get<StackPanel>(w, "_list").Children.OfType<Control>().ToList();
            k.Save(w, marks: [rows.First(r => Has(r, Sandbox(main).Name) && r is Border && Has(r, UiStrings.TilesetChoice_Open)), rows.First(r => Has(r, IconsName)), rows.Last(), FindButton(w, UiStrings.TilesetChoice_Manage)]);
        });
        // ⑤ 이름 ⑥ 쓰는 곳 ⑦ 파일로 저장 ⑧ 프로젝트에서 지우기 ⑨ 새 빈 타일셋 ⑩ 타일셋 파일 가져오기
        k.Dialog(() => TilesetManagerDialog.Run(main, ctx), w =>
        {
            var rows = ShotKit.Get<StackPanel>(w, "_list").Children.OfType<Border>().ToList();
            var first = rows[0];
            var name = ShotKit.All<TextBox>(first).First();
            var info = ShotKit.All<TextBlock>(first).First(t => t.Text?.Contains(" · ", StringComparison.Ordinal) == true);
            var buttons = ShotKit.All<Button>(first).ToList();
            var unused = rows.First(r => ShotKit.All<TextBox>(r).First().Text == IconsName);
            var head = ShotKit.FindText(w, UiStrings.TilesetChoice_New);
            var create = FindButton(w, UiStrings.TilesetChoice_Create);
            k.Save(w, "tilemap-choose-2", marks:
            [
                name, info, buttons[^2], ShotKit.All<Button>(unused).Last(), Union(w, head, create), FindButton(w, UiStrings.TilesetManager_ImportFile),
            ], firstNumber: 5);
        });
    }

    [Shot("tilemap-multi")]
    public static void Multi(ShotKit k)
    {
        var main = Open(k, multi: true);
        var sections = Sections(main);
        // 첫 묶음은 접어 두 묶음이 한눈에 보이게
        k.Click(ShotKit.Get<Control>(Head(sections[0]), "ArrowButton"));
        var palette = Palette(sections[1]);
        var props = Ctx(main).CurrentTilesetSources[1];
        k.Click(main, PaletteSlotCenter(main, palette, palette.DisplayPos(props.Tiles[2])));
        k.Hover(main, new Avalonia.Point(800, 5), 200);
        var panel = Panel(main);
        // ① 첫 타일셋 묶음(접힘) ② 둘째 타일셋 묶음 ③ 타일셋 더하기
        var b = ShotKit.Bounds(panel, main);
        var add = ShotKit.Get<Control>(panel, "_addTileset");
        var crop = new Rect(b.X, b.Y, b.Width, ShotKit.Bounds(add, main).Bottom - b.Y + 10);
        var canvasArea = MapRect(main, 12, 6, MapW - 1, MapH - 1).Inflate(8);
        k.Save(main, crop: crop, marks: [Head(sections[0]), sections[1], add]);
        k.Save(main, "tilemap-multi-2", crop: canvasArea, marks: [MapRect(main, 19, 11, 19, 11), MapRect(main, 21, 11, 21, 11)], firstNumber: 4);
    }

    // ───────────── 타일셋 설정 창 · 큰 타일 · 그림 고치기 ─────────────

    [Shot("tilemap-settings")]
    public static void Settings(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        var ts = Sandbox(main);
        // ① 페이지 ② 타일 목록 ③ 고른 타일 설정 ④ 그림 고치기·움직이는 타일·복제·지우기
        k.Dialog(() => TilesetSettingsWindow.Run(main, ctx, ts.Key, TilesetSettingsWindow.Page.Tiles, TileAt(ts, 1, 3)), w =>
        {
            var tabs = ShotKit.Get<Control>(w, "_tabs");
            var strip = ShotKit.All<Control>(tabs).First(c => c.GetType().Name == "TabStrip" || c is ItemsControl);
            var palette = ShotKit.Get<Control>(w, "_palette");
            var edit = ShotKit.Get<Control>(w, "_editImage");
            var props = ShotKit.Get<Control>(w, "_singleOnly");
            k.Save(w, marks: [TabHeaders(w), palette.Parent as Control ?? palette, Union(w, props, ShotKit.Get<Control>(w, "_tileFloor")), edit.Parent as Control ?? edit]);
        });
        // ⑤ 세트 줄(이름·층·무리·규칙) ⑥ 무리
        k.Dialog(() => TilesetSettingsWindow.Run(main, ctx, ts.Key, TilesetSettingsWindow.Page.AutoTiles), w =>
        {
            var sets = ShotKit.Get<Grid>(w, "_sets");
            var scroll = sets.FindAncestorOfType<ScrollViewer>()!;
            scroll.Offset = new Vector(0, 10_000);
            ShotKit.Pump(300);
            var groups = ShotKit.Get<Control>(w, "_groups");
            var rowCells = sets.Children.Where(c => Grid.GetRow(c) == Grid.GetRow(sets.Children.Last())).ToList();
            k.Save(w, "tilemap-settings-2", marks: [Union(w, [.. rowCells]), groups], firstNumber: 5);
        });
        // ⑦ 파일 ⑧ 저장·다시 읽기 ⑨ 가져오기
        k.Dialog(() => TilesetSettingsWindow.Run(main, ctx, ts.Key, TilesetSettingsWindow.Page.Tileset), w =>
        {
            var file = ShotKit.Get<Control>(w, "_tsFile");
            var reload = ShotKit.Get<Control>(w, "_reload");
            var png = FindButton(w, UiStrings.Tileset_ImportPng);
            k.Save(w, "tilemap-settings-3", marks: [file.Parent as Control ?? file, reload.Parent as Control ?? reload, png.Parent as Control ?? png], firstNumber: 7);
        });
        // ⑩ 보기
        k.Dialog(() => TilesetSettingsWindow.Run(main, ctx, ts.Key, TilesetSettingsWindow.Page.View), w =>
        {
            k.Save(w, "tilemap-settings-4", marks: [Union(w, ShotKit.Get<Control>(w, "_byFloor"), ShotKit.Get<Control>(w, "_slotSize").Parent as Control ?? ShotKit.Get<Control>(w, "_slotSize"))], firstNumber: 10);
        });
    }

    [Shot("tilemap-settings-position")]
    public static void PositionSettings(ShotKit k)
    {
        var main = Open(k, village: true);
        var ts = Sandbox(main);
        k.Dialog(() => TilesetSettingsWindow.Run(main, Ctx(main), ts.Key, TilesetSettingsWindow.Page.Tiles, TileAt(ts, 0, 0)), w =>
        {
            k.Save(w, marks: [ShotKit.Get<Control>(w, "_drawClassRow"), ShotKit.Get<Control>(w, "_sortYRow")]);
        });
    }

    [Shot("tilemap-big-tiles")]
    public static void BigTiles(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        var ts = Sandbox(main);
        // ① 차지 칸 ② 그림 위치 ③ 그림 아래 맞춤 (나무: 차지 1×1, 그림 32×44)
        k.Dialog(() => TilesetSettingsWindow.Run(main, ctx, ts.Key, TilesetSettingsWindow.Page.Tiles, TileAt(ts, 0, 0)), w =>
        {
            var w0 = ShotKit.Get<Control>(w, "_tileW");
            var off = ShotKit.Get<Control>(w, "_offX");
            var bottom = FindButton(w, UiStrings.Tileset_AlignBottom);
            k.Save(w, marks: [w0.Parent as Control ?? w0, off.Parent as Control ?? off, bottom], titleBar: true);
        });
        // ④ 여러 칸을 골라 우클릭 > 큰 타일로 합치기
        var palette = Palette(Sections(main)[0]);
        var crate = ts.Find(TileAt(ts, 1, 3))!;
        var barrel = ts.Find(TileAt(ts, 2, 3))!;
        var from = palette.DisplayPos(crate);
        var to = palette.DisplayPos(barrel);
        k.Drag(main, PaletteSlotCenter(main, palette, from), PaletteSlotCenter(main, palette, to), steps: 6);
        k.Click(main, PaletteSlotCenter(main, palette, to), MouseButton.Right, settleMs: 500);
        SavePanelMenu(k, main, "tilemap-big-tiles-2", 4, [MenuItemByText(UiStrings.Tileset_Merge)]);
    }

    [Shot("tilemap-edit-image")]
    public static void EditImage(ShotKit k)
    {
        var main = Open(k);
        var ts = Sandbox(main);
        ShotKit.Call(main, "EditTileImageOnCanvas", ts.Key, TileAt(ts, 0, 0));
        k.Settle(600);
        // 나무 그림(차지 칸 기준 x -8~24, y -28~16)이 다 보이게 확대하고 그 가운데를 캔버스 가운데에 둔다
        var pc = ShotKit.Get<PixelCanvas>(main, "_canvas");
        const int zoom = 8;
        pc.SetZoom(zoom);
        k.Settle(300);
        ShotKit.Set(pc, "_offset", new DPoint((int)(pc.Bounds.Width / 2 - 8 * zoom), (int)(pc.Bounds.Height / 2 + 6 * zoom)));
        pc.InvalidateVisual();
        k.Settle(400);
        var canvas = Canvas(main);
        var p = Ctx(main).Project!;
        var tl = CanvasPoint(main, 0, 0);
        var br = CanvasPoint(main, p.Width, p.Height);
        var bar = ShotKit.All<Button>(main).First(b => Has(b, UiStrings.TileImage_Save)).FindAncestorOfType<Border>()!;
        var title = UiStrings.TileImage_TabTitle.Split('{')[0];
        var tab = ShotKit.All<TextBlock>(main).First(t => t.Text?.StartsWith(title, StringComparison.Ordinal) == true && t.IsEffectivelyVisible);
        var area = ShotKit.Bounds(tab, main).Union(ShotKit.Bounds(canvas, main));
        area = new Rect(ShotKit.Bounds(canvas, main).X, area.Y - 6, ShotKit.Bounds(canvas, main).Width, area.Height + 6);
        // ① 탭 ② 캔버스 = 차지 칸 ③ 캔버스 밖 그림 ④ 타일에 저장
        var image = new Rect(CanvasPoint(main, -8, -28), CanvasPoint(main, 24, 16));
        k.Save(main, crop: area, marks: [ShotKit.Bounds(tab, main).Inflate(new Thickness(8, 4)), new Rect(tl, br), new Rect(image.X, image.Y, image.Width, tl.Y - image.Y), FindButton(bar, UiStrings.TileImage_Save)]);
    }

    // ───────────── 자동 타일 · 움직이는 타일 ─────────────

    [Shot("tilemap-autotile")]
    public static void AutoTile(ShotKit k)
    {
        var main = Open(k);
        var ts = Sandbox(main);
        k.Dialog(() => AutoTileEditorDialog.Run(main, ts, Set(ts, DomainStrings.TileBuiltIn_Water)), w =>
        {
            var board = ShotKit.Get<Control>(w, "_preview");
            var at = board.TranslatePoint(new Avalonia.Point(48 * 7.5, 48 * 2.5), w)!.Value;
            w.MouseMove(at);
            ShotKit.Pump(400);
            var name = ShotKit.Get<Control>(w, "_name");
            var group = ShotKit.Get<Control>(w, "_group");
            var filled = ShotKit.Get<System.Collections.IList>(w, "_filledFloors").Cast<Control>().ToList();
            var palette = ShotKit.Get<Control>(w, "_palette");
            var rules = ShotKit.Get<Control>(w, "_ruleScroll");
            var preset = ShotKit.Get<Control>(w, "_preset");
            var fill = ShotKit.Get<Control>(w, "_fillOrder");
            var status = ShotKit.Get<Control>(w, "_previewStatus");
            var boardBox = board.Parent as Control ?? board;
            var boardHead = ShotKit.FindText(w, UiStrings.AutoTile_Preview).Parent as Control;
            // ① 이름·층·이어지는 무리 ② 뭐라도 있음 층 ③ 타일 목록 ④ 규칙 목록 ⑤ 틀·차례대로 채우기 ⑥ 미리보기 판
            k.Save(w, marks:
            [
                Union(w, name, group), Union(w, [.. filled]), palette.Parent as Control ?? palette, rules,
                Union(w, preset, fill), Union(w, boardHead!, boardBox, status),
            ]);
            // ⑦ 조건 칸 ⑧ 넣을 타일 ⑨ 뒤집기·회전 허용 ⑩ 위로·아래로·복제·지우기 (규칙 1)
            var card = ShotKit.Get<System.Collections.IList>(w, "_cards").Cast<Control>().First();
            var cond = ShotKit.Get<Control>(card, "_grid");
            var tiles = ShotKit.Get<Control>(card, "_tiles");
            var flip = ShotKit.Get<Control>(card, "_flipX").Parent as Control;
            var actions = ((Panel)flip!.Parent!).Children[1];
            var crop = ShotKit.Bounds(card, w).Inflate(new Thickness(30, 24, 12, 10));
            k.Save(w, "tilemap-autotile-2", crop: crop, marks: [cond, tiles, flip, actions], firstNumber: 7);
        }, settleMs: 900);
    }

    [Shot("tilemap-autotile-weight")]
    public static void AutoWeight(ShotKit k)
    {
        var main = Open(k, village: true);
        var ts = Sandbox(main);
        k.Dialog(() => AutoTileEditorDialog.Run(main, ts, Set(ts, DomainStrings.TileBuiltIn_Water)), w =>
        {
            var card = ShotKit.Get<System.Collections.IList>(w, "_cards").Cast<Control>().First();
            var tiles = ShotKit.Get<Control>(card, "_tiles");
            var p = tiles.TranslatePoint(new Avalonia.Point(15, 15), w)!.Value;
            k.Click(w, p, MouseButton.Right, settleMs: 400);
            var popup = OpenPopupsRect(w) ?? throw new InvalidOperationException("weight menu not open");
            k.Save(w, crop: ShotKit.Bounds(tiles, w).Union(popup).Inflate(10));
        });
    }

    [Shot("tilemap-autotile-group")]
    public static void AutoGroup(ShotKit k)
    {
        var main = Open(k, village: true);
        var ts = Sandbox(main);
        k.Dialog(() => AutoTileEditorDialog.Run(main, ts, Set(ts, DomainStrings.TileBuiltIn_Grass)), w =>
        {
            var card = ShotKit.Get<System.Collections.IList>(w, "_cards").Cast<Control>().First(c => ShotKit.Get<Control>(c, "_targetRow").IsVisible);
            card.BringIntoView();
            ShotKit.Pump(300);
            k.Save(w, crop: ShotKit.Bounds(card, w).Inflate(8));
        });
    }

    [Shot("tilemap-animated")]
    public static void Animated(ShotKit k)
    {
        var main = Open(k);
        var ts = Sandbox(main);
        var torch = ts.Find(TileAt(ts, 2, 2))!;
        k.Dialog(() => TileAnimationDialog.Run(main, ts, torch), w =>
        {
            var palette = (TilePaletteView)ShotKit.Get<Control>(w, "_palette");
            // 같은 크기(16×32)의 다른 타일이 없으므로 크기가 다른 동전을 골라 빠지는 까닭을 보인다
            var coin = ts.Find(TileAt(ts, 3, 2))!;
            palette.Selection = new System.Drawing.Rectangle(palette.DisplayPos(coin), new DSize(1, 1));
            ShotKit.Call(w, "UpdatePick");
            ShotKit.Pump(300);
            var strip = ShotKit.Get<Control>(w, "_stripScroll");
            var earlier = ShotKit.Get<Control>(w, "_earlier");
            var ms = ShotKit.Get<Control>(w, "_ms");
            var add = ShotKit.Get<Control>(w, "_add");
            var preview = ShotKit.Get<Control>(w, "_preview");
            var play = ShotKit.Get<Control>(w, "_play");
            var tiled = ShotKit.Get<Control>(w, "_tiled");
            // ① 그림 차례 ② 앞뒤로 옮기기·복제·빼기 ③ 한 장 시간·한 바퀴 ④ 타일셋 목록 ⑤ 그림으로 더하기 ⑥ 미리보기 ⑦ 재생·멈춤 ⑧ 3×3로 이어 보기
            k.Save(w, marks:
            [
                strip, earlier.Parent as Control ?? earlier, ms.Parent as Control ?? ms, palette.Parent as Control ?? palette,
                Union(w, add, ShotKit.Get<Control>(w, "_pickText")), preview, play.Parent as Control ?? play, tiled,
            ]);
        }, settleMs: 900);
    }

    // ───────────── 기본 타일셋 · 레이어로 변환 ─────────────

    [Shot("tilemap-builtin")]
    public static void BuiltIn(ShotKit k)
    {
        var main = Open(k);
        k.Hover(main, new Avalonia.Point(5, 5));
        // ① 흙과 진흙(같은 무리) ② 흙과 돌(다른 무리) ③ 풀 덮개 ④ 물 수면·가장자리 ⑤ 횃불(움직임)
        k.Save(main, crop: MapRect(main, 0, 1, MapW - 1, MapH - 1).Inflate(10), marks:
        [
            MapRect(main, 2, 8, 3, 9), MapRect(main, 0, 10, 4, 11), MapRect(main, 5, 7, 9, 7), MapRect(main, 10, 8, 14, 8), MapRect(main, 19, 9, 19, 10),
        ]);
    }

    [Shot("tilemap-builtin-village")]
    public static void Village(ShotKit k)
    {
        var main = Open(k, village: true);
        k.Hover(main, new Avalonia.Point(5, 5));
        k.Save(main, crop: MapRect(main, 0, 0, MapW - 1, MapH - 1).Inflate(10));
    }

    [Shot("tilemap-builtin-compare")]
    public static void VillageCompare(ShotKit k)
    {
        var main = Open(k, village: true);
        var ctx = Ctx(main);
        var ts = Sandbox(main);
        var crop = MapRect(main, 0, 0, MapW - 1, MapH - 1).Inflate(8);
        // Same map and tile properties in both halves: the rear chest has a baseline above the wall.
        int rearChest = TileAt(ts, 0, 0);
        ctx.EditTileset(ts.Key, "Rear chest baseline", t => t with
        {
            Tiles = [.. t.Tiles.Select(tile => tile.Id == rearChest ? tile with { SortY = 0 } : tile)],
        });
        var shots = new List<(Avalonia.Media.Imaging.Bitmap, Rect)>();
        ctx.EditTileset(ts.Key, "Floor order", t => t with { SortMode = TileSortMode.ByFloor });
        k.Settle(350);
        shots.Add((k.Capture(main), crop));
        ctx.EditTileset(ts.Key, "Position order", t => t with { SortMode = TileSortMode.ByPosition });
        k.Settle(350);
        shots.Add((k.Capture(main), crop));
        GuideScreenUiCompareShots.Compose(k, "tilemap-builtin-compare", shots);
    }

    [Shot("tilemap-builtin-play")]
    public static void VillagePlay(ShotKit k)
    {
        var main = Open(k, village: true);
        // The play-test sprite comes from the active document; keep the map document open as a separate tab.
        ShotKit.Call(main, "OpenFile", PartsSample.Build(k));
        k.Settle(300);
        ShotKit.Call(main, "ShowPlayTest");
        k.Settle(500);
        var play = ShotKit.Get<Window>(main, "_play");
        var map = ShotKit.Get<ComboBox>(play, "_map");
        map.SelectedIndex = 1;
        k.Settle(500);
        k.Save(play);
    }

    [Shot("tilemap-convert")]
    public static void Convert(ShotKit k)
    {
        App.Settings.BottomHeight = 330;
        var main = Open(k, bottom: 330);
        int row = Index(main, MapLayer);
        // ① 타일맵 레이어 우클릭 > 타일맵을 레이어로 변환
        RightClickRow(k, main, row);
        SaveRowMenu(k, main, "tilemap-convert", row, [MenuItemByText(UiStrings.Tile_MenuConvert)], 1);
        CloseMenus(k, main);
        ShotKit.Call(Timeline(main), "ConvertTilemap", row);
        k.Settle(500);
        var p = Project(main);
        var group = p.Layers.First(l => l.Kind == LayerKind.Group);
        // 첫 스킨 이름은 앱 코드에 한국어로 고정돼 있어 영어 화면 그림에서만 바꾼다(가이드-이미지.md "찍으면서 발견한 앱 문제")
        p.RenameSkin(group, group.Skins[0], "Default");
        k.Settle(300);
        var parts = p.Layers.Where(l => l.Parent == group).ToList();
        var tl = Timeline(main);
        var b = ShotKit.Bounds(tl, main);
        var groupRow = RowRect(main, p.IndexOf(group));
        var partRows = parts.Select(l => RowRect(main, p.IndexOf(l))).Aggregate((a, c) => a.Union(c));
        // ② 그룹 ③ 층마다 부위
        k.Save(main, "tilemap-convert-2", crop: new Rect(b.X, b.Y, Math.Min(b.Width, 640), b.Height), marks: [groupRow, partRows], firstNumber: 2);
    }

    // ───────────── 예시 프로젝트 ─────────────

    /// <summary>예시 프로젝트를 만들어 저장하고 열어, 타일맵 레이어를 고른 메인 창을 돌려준다.</summary>
    private static MainForm Open(ShotKit k, bool multi = false, int bottom = 250, bool village = false)
    {
        var path = Build(k, multi, village);
        App.Settings.BottomHeight = bottom;
        App.Settings.FilesVisible = false;
        App.Settings.PaletteWidth = 290;
        // 타일맵 모드에서는 피벗 단추가 숨지만 표시는 남아, 캔버스 가운데 피벗 이름표가 맵을 가리지 않게 끈다
        App.Settings.ShowPivot = false;
        var main = k.Main(path, null, 1600, 900);
        ScreenDemo.CleanTitle(main);
        ShotKit.Call(Canvas(main), "FitToWindow");
        Ctx(main).SetCurrentLayer(Index(main, MapLayer));
        k.Settle(300);
        // 열 때 캔버스 가운데 떠 있는 풍선(레이어 이름)이 남지 않게 마우스를 비켜 둔다
        k.Hover(main, new Avalonia.Point(800, 5), 100);
        return main;
    }

    private static string Build(ShotKit k, bool multi, bool village = false)
    {
        var p = new SpriteProject(MapW * Cell, MapH * Cell);
        p.Layers[0].Name = "Sky";
        p.SetCel(0, 0, Sky(p.Width, p.Height));
        var ts = village ? BuiltInTilesets.TopDownVillage() : BuiltInTilesets.Sandbox();
        var layer = p.AddTilemapLayer(ts, name: MapLayer);
        var props = village ? null : Props(ts);
        if (props is not null) p.AddTileset(props);
        if (!village) p.AddTileset(Icons());
        var map = village ? VillageMap(ts) : Map(ts);
        if (multi && props is not null)
        {
            p.AddLayerTileset(layer, props);
            var combined = p.TilesetOf(layer)!;
            int decor = FloorId(ts, DomainStrings.Tile_FloorDecor);
            map = map.Place(combined, decor, new DPoint(19, 11), new TileCell(TileSources.Global(1, props.Tiles[2].Id)));
            map = map.Place(combined, decor, new DPoint(21, 11), new TileCell(TileSources.Global(1, props.Tiles[0].Id)));
        }
        p.SetTileMap(layer, map, "map");
        p.History.Clear();
        var path = Path.Combine(k.WorkDir, "Meadow" + SpriteProject.FileExtension);
        ProjectIO.Save(p, path);
        return path;
    }

    private static TileMap VillageMap(Tileset ts)
    {
        var map = TileMap.Empty;
        TileMap Paint(TileMap m, string name, IEnumerable<DPoint> cells) => AutoTiler.Paint(ts, m, Set(ts, name), cells);
        map = Paint(map, DomainStrings.TileBuiltIn_Sand, Box(0, 7, 15, 10));
        map = Paint(map, DomainStrings.TileBuiltIn_Grass, Box(1, 1, 22, 8));
        map = Paint(map, DomainStrings.TileBuiltIn_Water, Box(0, 11, 23, 13).Concat(Box(16, 9, 23, 10)));
        map = Paint(map, DomainStrings.TileBuiltIn_StonePath, Box(6, 5, 19, 6));
        map = Paint(map, DomainStrings.TileBuiltIn_WoodWall, Box(6, 4, 18, 4));
        map = Paint(map, DomainStrings.TileBuiltIn_WoodWall, Box(9, 5, 13, 5));
        int objects = FloorId(ts, DomainStrings.Tile_FloorObjects);
        TileMap Put(TileMap m, int slotX, int slotY, int x, int y) => m.Place(ts, objects, new DPoint(x, y), new TileCell(TileAt(ts, slotX, slotY)));
        map = Put(map, 0, 0, 10, 4);
        map = Put(map, 2, 0, 11, 5);
        map = Put(map, 4, 1, 3, 7);
        map = Put(map, 3, 1, 19, 8);
        return map;
    }

    /// <summary>하늘: 위는 짙은 파랑, 아래로 갈수록 옅어지는 띠.</summary>
    private static PixelImage Sky(int w, int h)
    {
        var img = new PixelImage(w, h);
        var top = (R: 76, G: 146, B: 214);
        var bottom = (R: 190, G: 226, B: 246);
        for (int y = 0; y < h; y++)
        {
            double t = Math.Floor(y / (double)h * 8) / 7;
            var c = new Color32((byte)(top.R + (bottom.R - top.R) * t), (byte)(top.G + (bottom.G - top.G) * t), (byte)(top.B + (bottom.B - top.B) * t));
            for (int x = 0; x < w; x++) img[x, y] = c;
        }
        return img;
    }

    /// <summary>둘째 타일셋(16px, 장식 층 하나): 기본 타일셋의 상자·통·항아리 등을 보라색 던전 소품으로 바꾼 것.</summary>
    private static Tileset Props(Tileset sandbox)
    {
        var picks = new[] { (7, 2), (0, 3), (1, 3), (2, 3), (6, 2), (4, 2), (7, 1), (3, 3) };
        var tiles = picks.Select((pos, i) =>
        {
            var src = sandbox.Find(TileAt(sandbox, pos.Item1, pos.Item2))!;
            var img = src.Image.Clone();
            for (int j = 0; j < img.Pixels.Length; j++)
            {
                var c = img.Pixels[j];
                if (!c.IsTransparent) img.Pixels[j] = new Color32(c.G, c.B, c.R, c.A);
            }
            return src with { Id = i + 1, Image = img, Frames = [], Floor = 1, PaletteX = i % 4, PaletteY = i / 4 };
        });
        return new Tileset
        {
            Key = Tileset.NewKey(),
            Name = PropsName,
            Floors = [new TileFloor(1, DomainStrings.Tile_FloorDecor)],
            Tiles = [.. tiles],
        };
    }

    /// <summary>칸 크기가 다른(8px) 타일셋: 타일셋 고르기 창에서 칸 크기가 달라 고를 수 없는 줄.</summary>
    private static Tileset Icons()
    {
        var colors = new[] { new Color32(234, 50, 60), new Color32(90, 197, 79), new Color32(0, 152, 220), new Color32(255, 200, 37) };
        var tiles = colors.Select((c, i) =>
        {
            var img = new PixelImage(8, 8);
            for (int y = 1; y < 7; y++)
                for (int x = 1; x < 7; x++)
                    img[x, y] = x == 1 || y == 1 || x == 6 || y == 6 ? Color32.Black : c;
            return new TileDef { Id = i + 1, Image = img, Floor = 1, PaletteX = i, PaletteY = 0 };
        });
        return Tileset.Create(IconsName, 8, 8) with { Tiles = [.. tiles] };
    }

    private static AutoTileSet Set(Tileset ts, string name) => ts.AutoTiles.First(a => a.Name == name);

    /// <summary>타일 목록 자리(x, y)의 타일 Id.</summary>
    private static int TileAt(Tileset ts, int x, int y) => ts.Tiles.First(t => t.PaletteX == x && t.PaletteY == y).Id;

    private static int FloorId(Tileset ts, string name) => ts.Floors.First(f => f.Name == name).Id;

    private static IEnumerable<DPoint> Box(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return new DPoint(x, y);
    }

    /// <summary>
    /// 맵(24×14칸): 왼쪽 흙 언덕(속에 진흙, 풀 덮개·나무·꽃) 아래 광석 섞인 돌, 가운데 물웅덩이와 그 위 나무 발판·동전,
    /// 오른쪽 돌 속 동굴(돌 벽 배경·횃불·상자·수정·사다리).
    /// </summary>
    private static TileMap Map(Tileset ts)
    {
        var map = TileMap.Empty;
        TileMap Paint(TileMap m, string set, IEnumerable<DPoint> cells) => AutoTiler.Paint(ts, m, Set(ts, set), cells);
        int decor = FloorId(ts, DomainStrings.Tile_FloorDecor);
        TileMap Put(TileMap m, int px, int py, int x, int y) => m.Place(ts, decor, new DPoint(x, y), new TileCell(TileAt(ts, px, py)));

        map = Paint(map, DomainStrings.TileBuiltIn_Dirt, Box(0, 6, 4, 10).Concat(Box(5, 7, 9, 10)).Concat(Box(15, 7, 16, 8)));
        map = Paint(map, DomainStrings.TileBuiltIn_Mud, Box(2, 8, 3, 9));
        map = Paint(map, DomainStrings.TileBuiltIn_OreStone, Box(0, 11, 9, 13));
        map = Paint(map, DomainStrings.TileBuiltIn_Stone, Box(10, 11, 14, 13));
        map = Paint(map, DomainStrings.TileBuiltIn_Water, Box(10, 8, 14, 10));
        var rock = Box(15, 9, 23, 13).Concat(Box(17, 7, 23, 8)).Where(c => !(c.X >= 18 && c.X <= 22 && c.Y >= 9 && c.Y <= 11) && !(c.X == 20 && c.Y <= 8));
        map = Paint(map, DomainStrings.TileBuiltIn_Stone, rock);
        map = Paint(map, DomainStrings.TileBuiltIn_StoneWall, Box(18, 9, 22, 11).Append(new DPoint(20, 7)).Append(new DPoint(20, 8)));
        var tops = Enumerable.Range(0, 5).Select(x => new DPoint(x, 6)).Concat(Enumerable.Range(5, 5).Select(x => new DPoint(x, 7)))
            .Concat(new[] { 15, 16, 17, 18, 19, 21, 22, 23 }.Select(x => new DPoint(x, 7)));
        map = Paint(map, DomainStrings.TileBuiltIn_Grass, tops);
        map = Paint(map, DomainStrings.TileBuiltIn_Platform, Box(11, 5, 13, 5));
        map = Paint(map, DomainStrings.TileBuiltIn_Ladder, Box(20, 7, 20, 11));
        map = Put(map, 0, 0, 2, 5);   // 나무
        map = Put(map, 2, 0, 7, 6);   // 소나무
        map = Put(map, 0, 1, 0, 5);   // 풀
        map = Put(map, 2, 1, 4, 5);   // 꽃
        map = Put(map, 3, 1, 5, 6);   // 꽃
        map = Put(map, 4, 0, 9, 6);   // 덤불
        map = Put(map, 5, 1, 1, 5);   // 버섯
        map = Put(map, 5, 0, 16, 6);  // 바위
        map = Put(map, 3, 3, 22, 6);  // 표지판
        map = Put(map, 1, 1, 17, 6);  // 풀
        map = Put(map, 3, 2, 11, 4);  // 동전
        map = Put(map, 3, 2, 12, 4);
        map = Put(map, 3, 2, 13, 4);
        map = Put(map, 2, 2, 19, 10); // 횃불
        map = Put(map, 0, 3, 22, 11); // 상자
        map = Put(map, 7, 1, 18, 11); // 수정
        return map;
    }

    // ───────────── 고르기 · 마우스 ─────────────

    /// <summary>타일셋 패널에서 자동 타일 세트 칸을 눌러 자동 타일 붓으로 고른다.</summary>
    private static void PickAutoSet(ShotKit k, MainForm main, string name)
    {
        var s = Sections(main)[0];
        var auto = AutoView(s);
        var sets = ShotKit.Get<System.Collections.IList>(s, "_autoSets").Cast<AutoTileSet>().ToList();
        int index = sets.FindIndex(a => a.Name == name);
        k.Click(main, AutoSlotRect(main, auto, index).Center);
        k.Settle(300);
    }

    /// <summary>캔버스의 맵 칸 (x, y) 가운데에 마우스를 올린다(찍힐 모양 미리보기·칸 정보).</summary>
    private static void HoverCell(ShotKit k, MainForm main, int x, int y)
    {
        var p = CanvasPoint(main, x * Cell + Cell / 2.0, y * Cell + Cell / 2.0);
        k.Hover(main, p, 500);
    }

    // ───────────── 찾기 ─────────────

    private static EditorContext Ctx(MainForm main) => ShotKit.Get<EditorContext>(main, "_ctx");
    private static SpriteProject Project(MainForm main) => Ctx(main).Project!;
    private static int Index(MainForm main, string name) => Project(main).IndexOf(Project(main).Layers.First(l => l.Name == name));
    private static Tileset Sandbox(MainForm main) => Ctx(main).CurrentTilesetSources[0];
    private static Control Canvas(MainForm main) => ShotKit.Get<Control>(main, "_canvas");
    private static Control Timeline(MainForm main) => ShotKit.Get<Control>(main, "_timeline");
    private static Control Panel(MainForm main) => ShotKit.Get<Control>(main, "_tilesetPanel");
    private static List<Control> Sections(MainForm main) => ShotKit.Get<System.Collections.IList>(Panel(main), "_sections").Cast<Control>().ToList();
    private static Control Head(Control section) => ShotKit.Get<Control>(section, "_head");
    private static Control AutoView(Control section) => ShotKit.Get<Control>(section, "_autoView");
    private static TilePaletteView Palette(Control section) => ShotKit.Get<TilePaletteView>(section, "_palette");

    private static bool Has(Visual root, string text) => ShotKit.All<TextBlock>(root).Any(t => t.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static Button FindButton(Visual root, string text) => ShotKit.All<Button>(root).First(b => Has(b, text));

    /// <summary>컨트롤 여럿을 감싼 사각형(창 좌표).</summary>
    private static Rect Union(TopLevel top, params Control[] controls) => controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b));

    /// <summary>탭 머리글 줄(창 좌표).</summary>
    private static Rect TabHeaders(Window w)
    {
        var heads = ShotKit.All<TextBlock>(w).Where(t => t.Text is { } s && (s == UiStrings.TilesetWin_PageTiles || s == UiStrings.TilesetWin_PageView)).ToList();
        return heads.Select(t => ShotKit.Bounds(t, w)).Aggregate((a, b) => a.Union(b)).Inflate(new Thickness(10, 6));
    }

    private static Avalonia.Point CanvasPoint(MainForm main, double x, double y)
    {
        var canvas = Canvas(main);
        var sp = (Avalonia.Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!;
        return canvas.TranslatePoint(sp, main)!.Value;
    }

    /// <summary>맵 칸 (x0, y0)~(x1, y1)을 덮는 캔버스 사각형(창 좌표).</summary>
    private static Rect MapRect(MainForm main, int x0, int y0, int x1, int y1)
        => new(CanvasPoint(main, x0 * Cell, y0 * Cell), CanvasPoint(main, (x1 + 1) * Cell, (y1 + 1) * Cell));

    /// <summary>캔버스 왼쪽 아래 칸 정보 자리(창 좌표).</summary>
    private static Rect HudRect(Rect canvas) => new(canvas.X + 4, canvas.Bottom - 34, 330, 30);

    private static Avalonia.Point PaletteSlotCenter(MainForm main, TilePaletteView palette, DPoint slot)
    {
        double size = ShotKit.Get<double>(palette, "_slot");
        return palette.TranslatePoint(new Avalonia.Point((slot.X + 0.5) * size, (slot.Y + 0.5) * size), main)!.Value;
    }

    private static Rect AutoSlotRect(MainForm main, Control auto, int index)
    {
        double size = ShotKit.Get<double>(auto, "SlotSize");
        int cols = ShotKit.Get<int>(auto, "_columns");
        var p = auto.TranslatePoint(new Avalonia.Point(index % cols * size, index / cols * size), main)!.Value;
        return new Rect(p.X, p.Y, size, size);
    }

    /// <summary>자동 타일 칸의 마지막 칸(새 세트).</summary>
    private static Rect LastSlot(MainForm main, Control auto)
        => AutoSlotRect(main, auto, ShotKit.Get<System.Collections.ICollection>(auto, "Slots").Count - 1);

    // ───────────── 타임라인 · 메뉴 ─────────────

    private const int NameW = 270, CellH = 36;

    private static Rect RowRect(MainForm main, int layer)
    {
        var grid = ShotKit.Get<Control>(Timeline(main), "_grid");
        int y = (int)ShotKit.Call(grid, "LayerY", layer)!;
        var p = grid.TranslatePoint(new Avalonia.Point(0, y), main)!.Value;
        return new Rect(p.X, p.Y, NameW, CellH);
    }

    private static void RightClickRow(ShotKit k, MainForm main, int layer)
    {
        var row = RowRect(main, layer);
        k.Click(main, new Avalonia.Point(row.X + 120, row.Center.Y), MouseButton.Right, settleMs: 400);
    }

    private static IEnumerable<Popup> OpenPopups() => ShotKit.OpenPopups;

    private static IEnumerable<MenuItem> OpenMenuItems()
    {
        foreach (var p in OpenPopups())
            if (p.Child is Visual v)
                foreach (var m in ShotKit.All<MenuItem>(v).Concat(v is MenuItem mi ? [mi] : []))
                    yield return m;
    }

    private static Rect? OpenPopupsRect(TopLevel top)
    {
        Rect? area = null;
        foreach (var p in OpenPopups())
            if (p.Child is Control c && c.IsVisible)
                area = area is { } a ? a.Union(InTop(top, c)) : InTop(top, c);
        return area;
    }

    private static string HeaderText(MenuItem m) => m.Header switch
    {
        string s => s,
        TextBlock t => t.Text ?? "",
        _ => ShotKit.All<TextBlock>(m).FirstOrDefault()?.Text ?? "",
    };

    private static MenuItem MenuItemByText(string text)
        => OpenMenuItems().FirstOrDefault(m => HeaderText(m).Replace("_", "") == text.Replace("_", ""))
           ?? throw new InvalidOperationException($"no menu item '{text}' in [{string.Join(" | ", OpenMenuItems().Select(HeaderText))}]");

    private static MenuItem CommandItem(MainForm main, string tail)
    {
        static string Plain(string s) => s.TrimEnd('…');
        var entries = ShotKit.Get<System.Collections.IEnumerable>(main, "_commands").Cast<object>().ToList();
        var entry = entries.First(c => Plain(ShotKit.Get<ShortcutCommand>(c, "Info").Id).EndsWith("/" + Plain(tail), StringComparison.Ordinal));
        return ShotKit.Get<MenuItem?>(entry, "Item") ?? throw new InvalidOperationException("command has no menu item: " + tail);
    }

    private static MenuItem OpenCommandMenu(ShotKit k, MainForm main, string tail)
    {
        var item = CommandItem(main, tail);
        var top = item.GetLogicalAncestors().OfType<MenuItem>().Last();
        k.Click(top, settleMs: 500);
        return item;
    }

    private static void SaveTopMenu(ShotKit k, MainForm main, string name, IEnumerable<Control> marks, int firstNumber)
    {
        var menu = OpenPopupsRect(main) ?? new Rect(0, 0, 400, 400);
        k.Save(main, name, crop: new Rect(0, 0, menu.Right + 16, menu.Bottom + 12), marks: [.. marks.Select(m => (Mark)InTop(main, m))], firstNumber: firstNumber);
    }

    /// <summary>열린 우클릭 메뉴와 그 레이어 행을 함께 담아 저장한다.</summary>
    private static void SaveRowMenu(ShotKit k, MainForm main, string name, int layer, IEnumerable<Control> marks, int firstNumber)
    {
        var area = RowRect(main, layer);
        if (OpenPopupsRect(main) is { } menu) area = area.Union(menu);
        k.Save(main, name, crop: area.Inflate(12), marks: [.. marks.Select(m => (Mark)InTop(main, m))], firstNumber: firstNumber);
    }

    /// <summary>타일셋 패널과 열린 메뉴를 함께 담아 저장한다(<paramref name="marks"/>가 없으면 메뉴 전체에 번호).</summary>
    private static void SavePanelMenu(ShotKit k, MainForm main, string name, int number, IEnumerable<Control>? marks = null)
    {
        var menu = OpenPopupsRect(main) ?? throw new InvalidOperationException("no open menu");
        var area = PanelArea(main).Union(menu.Inflate(14));
        List<Mark> list = marks is null ? [menu] : [.. marks.Select(m => (Mark)InTop(main, m))];
        k.Save(main, name, crop: area, marks: list, firstNumber: number);
    }

    /// <summary>타일셋 패널에서 "타일셋 더하기"까지(창 좌표).</summary>
    private static Rect PanelArea(MainForm main)
    {
        var b = ShotKit.Bounds(Panel(main), main);
        var add = ShotKit.Bounds(ShotKit.Get<Control>(Panel(main), "_addTileset"), main);
        return new Rect(b.X, b.Y, b.Width, add.Bottom + 10 - b.Y);
    }

    private static void CloseMenus(ShotKit k, TopLevel top)
    {
        k.Key(top, Key.Escape);
        PopupMenus.CloseAll();
        foreach (var p in OpenPopups()) p.IsOpen = false;
        k.Settle(250);
    }

    private static Rect InTop(TopLevel top, Control c)
    {
        if (c.TranslatePoint(new Avalonia.Point(0, 0), top) is { } p) return new Rect(p, c.Bounds.Size);
        var own = TopLevel.GetTopLevel(c)!;
        var d = ShotKit.Position(own) - ShotKit.Position(top);
        return ShotKit.Bounds(c, own).Translate(new Vector(d.X, d.Y));
    }
}
