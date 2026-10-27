using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Localization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using static GuideShots.PartsSample;

namespace GuideShots;

/// <summary>부위(그룹) 레이어 · 스킨 장(parts-*) 그림. 번호는 쪽(h3)마다 이어진다 — 가이드 글의 ①②…와 같은 순서.</summary>
internal static class PartsShots
{
    // ───────────── 그룹과 부위 · 만들기 · 타임라인 ─────────────

    [Shot("parts-concept")]
    public static void Concept(ShotKit k)
    {
        var main = Open(k);
        int g = Index(main, GroupName), head = Index(main, "Head"), legs = Index(main, "Legs");
        // ① 그룹 ② 부위 행 ③ 부위 크기 ④ 프레임마다 그림 ⑤ 프레임 안 위치(캔버스의 Head 테두리)
        k.SaveArea([Canvas(main), Timeline(main)], pad: 2, marks:
        [
            RowRect(main, g), RowRect(main, head).Union(RowRect(main, legs)), SizeRect(main, head).Inflate(4),
            CellsRect(main, head, 0, 3), PartOnCanvas(main, Layer(main, "Head")),
        ]);
    }

    [Shot("parts-create")]
    public static void Create(ShotKit k)
    {
        var main = Open(k);
        // ① 부위로 시작 ② 첫 부위 이름·크기
        k.Dialog(() => k.Command("파일/새 프로젝트"), w =>
        {
            var parts = ShotKit.All<RadioButton>(w).First(r => r.Content as string == UiStrings.S_Dialogs_NewImageDialog_0F2FF932AA);
            parts.IsChecked = true;
            ShotKit.Pump(200);
            k.Save(w, marks: [parts, AfterLabel(w, ShotKit.Get<Panel>(w, "_partRow"))]);
        });
        // ③ 새 그룹 ④ 새 부위… (프레임·레이어 메뉴)
        var newGroup = OpenCommandMenu(k, main, "새 그룹");
        var newPart = CommandItem(main, "새 부위");
        SaveTopMenu(k, main, "parts-create-2", [newGroup, newPart], 3);
        CloseMenus(k, main);
        // ⑤ 이름 ⑥ 크기 (새 부위 창)
        k.Dialog(() => k.Command("프레임·레이어/새 부위"), w =>
        {
            var name = ShotKit.All<TextBox>(w).First();
            name.Text = "Arm";
            name.SelectionStart = name.SelectionEnd = name.CaretIndex = 3;
            ShotKit.Pump(200);
            var size = AfterLabel(w, (Panel)ShotKit.Get<Control>(w, "_w").Parent!);
            k.Save(w, "parts-create-3", marks: [name, size], firstNumber: 5);
        });
        // ⑦ 새 그룹 ⑧ 부위 추가… (그룹 우클릭)
        int g = Index(main, GroupName);
        RightClickRow(k, main, g);
        SaveMenu(k, main, "parts-create-4", g,
            [MenuItemByText(UiStrings.S_TimelinePanel_ShowLayerMenu_13B10F0946), MenuItemByText(UiStrings.S_TimelinePanel_ShowLayerMenu_CC4093DC8F)], 7);
    }

    [Shot("parts-timeline")]
    public static void TimelineView(ShotKit k)
    {
        var main = Open(k);
        int g = Index(main, GroupName), head = Index(main, "Head"), legs = Index(main, "Legs");
        var tl = Timeline(main);
        var b = ShotKit.Bounds(tl, main);
        var crop = new Rect(b.X, b.Y, Math.Min(b.Width, 520), b.Height);
        var row = RowRect(main, g);
        // 부위 행은 들여 쓴 이름부터(번호가 보이기·잠금 단추를 가리지 않게)
        var parts = RowRect(main, head).Union(RowRect(main, legs));
        parts = new Rect(parts.X + 64, parts.Y + 8, parts.Width - 64, parts.Height - 8);
        // ① 접기 삼각형 ② 그룹 합성 썸네일 ③ 부위 행(들여쓰기·크기) ④ 스킨 칩 ⑤ 보이기·잠금
        k.Save(main, crop: crop, marks:
        [
            ToggleRect(main, g).Inflate(6), CellsRect(main, g, 0, 3), parts, ChipRect(main, g),
            new Rect(row.X + 4, row.Y + 3, 44, 30),
        ]);
    }

    // ───────────── 배치(그룹 선택) · 그리기(부위 선택) ─────────────

    [Shot("parts-arrange")]
    public static void Arrange(ShotKit k)
    {
        var main = Open(k, height: 1300);
        var head = Layer(main, "Head");
        var body = Layer(main, "Body");
        var legs = Layer(main, "Legs");
        // Head 클릭 = 고른 부위(파란 실선), Legs 위에 마우스 = 점선 + 이름
        k.Click(main, CanvasPoint(main, head.PartRect.X + head.PartRect.Width / 2.0, head.PartRect.Y + 8));
        k.Hover(main, CanvasPoint(main, legs.PartRect.X + legs.PartRect.Width / 2.0, legs.PartRect.Bottom - 4), 400);
        var tl = CanvasPoint(main, 0, 0);
        var br = CanvasPoint(main, CanvasW, CanvasH);
        // 이름표(테두리 위)까지 담고 번호가 이름표를 가리지 않게 넉넉히
        var hover = PartOnCanvas(main, legs);
        hover = hover.Union(new Rect(hover.X, hover.Y - 22, 90, 22)).Inflate(10);
        var (bx, by) = body.PartPivot.ToLocalPixel(body.PartSize.Width, body.PartSize.Height);
        var pivot = Ctx(main).Pivot.ToLocalPixel(CanvasW, CanvasH);
        // ① 마우스 올린 부위 ② 고른 부위 ③ 부위 피벗(파랑) ④ 프레임 피벗(주황)
        k.Save(main, crop: new Rect(tl, br).Inflate(new Thickness(160, 30)), marks:
        [
            hover, PartOnCanvas(main, head), Around(CanvasPoint(main, body.PartOffset.X + bx, body.PartOffset.Y + by), 10),
            Around(CanvasPoint(main, pivot.X, pivot.Y), 12),
        ]);
    }

    [Shot("parts-draw")]
    public static void Draw(ShotKit k)
    {
        var main = Open(k);
        int head = Index(main, "Head");
        var part = Layer(main, "Head");
        Select(k, main, "Head");
        ShotKit.Call(Canvas(main), "FitToWindow");
        k.Command("보기/축소");
        k.Command("보기/축소");
        k.Settle(300);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var crop = new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y);
        // ① 고른 부위 행 ② 자기 크기로 보이는 부위(부위 모드라 캔버스 좌표 = 부위 안 좌표)
        var own = new Rect(CanvasPoint(main, 0, 0), CanvasPoint(main, part.PartSize.Width, part.PartSize.Height));
        k.Save(main, crop: crop, marks: [RowRect(main, head), own]);
        // ③ 프레임·레이어 > 부위 크기…
        var resize = OpenCommandMenu(k, main, "부위 크기");
        SaveTopMenu(k, main, "parts-draw-2", [resize], 3);
        CloseMenus(k, main);
        // ④ 크기… ⑤ 위치… (부위 우클릭)
        RightClickRow(k, main, head);
        var size = MenuItemStarting(UiStrings.S_TimelinePanel_ShowLayerMenu_66BE4C8AA7);
        var pos = MenuItemStarting(UiStrings.S_TimelinePanel_ShowLayerMenu_73BDC86998);
        SaveMenu(k, main, "parts-draw-3", head, [size, pos], 4);
        CloseMenus(k, main);
        // ⑥ 기준점 9방향 (부위 크기 창)
        k.Dialog(() => ShotKit.Call(Timeline(main), "ResizePartAt", head), w =>
        {
            ShotKit.Pump(200);
            k.Save(w, "parts-draw-4", marks: [ShotKit.Get<Control>(w, "_anchor")], firstNumber: 6);
        });
        // ⑦ 피벗 도구·9-slice 도구 (도구 막대 아래쪽)
        var slots = ShotKit.Get<List<ToolSlotButton>>(main, "_toolSlots");
        var pivotSlot = slots.First(s => s.Contains(ToolKind.Pivot));
        var sliceSlot = slots.First(s => s.Contains(ToolKind.Slice));
        var p0 = ShotKit.Bounds(pivotSlot, main);
        var s0 = ShotKit.Bounds(sliceSlot, main);
        var area = new Rect(0, p0.Y - 120, p0.Right + 10, s0.Bottom + 12 - (p0.Y - 120));
        // 번호가 피벗 도구 아이콘을 가리지 않게 테두리를 위로 늘린다.
        var tools = p0.Union(s0);
        k.Save(main, "parts-draw-5", crop: area, marks: [new Rect(tools.X, tools.Y - 16, tools.Width, tools.Height + 16)], firstNumber: 7);
    }

    // ───────────── 자동 배치 ─────────────

    [Shot("parts-auto-layout")]
    public static void AutoLayout(ShotKit k)
    {
        // 가로로 늘어놓을 자리가 있도록 캔버스를 넓히고, 장비 그룹은 숨긴다.
        var main = Open(k, p =>
        {
            p.ResizeCanvas(136, CanvasH, Anchor.MiddleLeft);
            p.SetLayerProperties(p.IndexOf(p.Layers.First(l => l.Name == GearName)), visible: false);
        });
        int g = Index(main, GroupName);
        var group = Layer(main, GroupName);
        // ① 그룹 우클릭 > 자동 배치…
        RightClickRow(k, main, g);
        SaveMenu(k, main, "parts-auto-layout", g, [MenuItemByText(UiStrings.S_TimelinePanel_ShowLayerMenu_E476AEFAC3)], 1);
        CloseMenus(k, main);
        // ② 미리 보기 ③ 적용, ④~⑩ 설정 표의 줄 순서
        GroupLayout? layout = null;
        k.Dialog(() => ShotKit.Call(Timeline(main), "EditGroupLayout", g), w =>
        {
            ShotKit.Pump(300);
            layout = ((GroupLayoutDialog)w).Layout;
            var rows = ShotKit.Get<Panel>(w, "Body").Children;
            var preview = (Control)ShotKit.Get<Control>(w, "_preview").GetVisualParent()!;
            var apply = ShotKit.FindText(w, "Apply", ancestorControl: true);
            Rect Field(int i) => AfterLabel(w, (Panel)rows[i]);
            k.Save(w, "parts-auto-layout-2", marks:
            [
                preview, apply, Field(1), Field(2), Field(3), Field(4), Field(5), Field(6), ShotKit.Get<Control>(w, "_includeHidden"),
            ], firstNumber: 2);
        });
        Ctx(main).SetGroupLayout(group, layout);
        k.Settle(300);
        ShotKit.Call(Canvas(main), "FitToWindow");
        k.Settle(300);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var crop = new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y);
        // ⑪ 마름모 표시
        k.Save(main, "parts-auto-layout-3", crop: crop, marks: [BadgeRect(main, g).Inflate(new Thickness(6, 4))], firstNumber: 11);
    }

    // ───────────── 내보내기 ─────────────

    [Shot("parts-export")]
    public static void Export(ShotKit k)
    {
        var main = Open(k);
        k.Dialog(() => k.Command("파일/내보내기"), w =>
        {
            // 왼쪽 설정이 한눈에 다 보이게 창을 키운다.
            w.Height = 1520;
            ShotKit.Pump(800);
            ScrubPaths(k, w);
            var frames = SectionStarting(w, "Frame sheet");
            var parts = Section(w, UiStrings.S_UnityExportDialog_UnityExportDialog_B18F4A0368);
            var output = (Control)ShotKit.All<TextBlock>(w).First(t => t.Text == UiStrings.S_UnityExportDialog_BuildCommonBlock_FE8EEAD4AB).GetVisualParent()!;
            var name = ShotKit.All<TextBox>(parts).First();
            var common = (Control)SectionStarting(w, "Common").Header!;
            // ① 프레임 시트 ② 부위 아틀라스 ③ 내보낼 결과 ④ 부위 아틀라스 이름 ⑤ 공통 설정
            var pad = new Thickness(10, 3);
            k.Save(w, marks: [frames, parts, ShotKit.Bounds(output, w).Inflate(pad), ShotKit.Bounds(name, w).Inflate(6), ShotKit.Bounds(common, w).Inflate(pad)]);
        });
    }

    // ───────────── 스킨 ─────────────

    [Shot("parts-skins")]
    public static void Skins(ShotKit k)
    {
        var main = Open(k);
        int head = Index(main, "Head");
        int g = Index(main, GroupName);
        int gear = Index(main, GearName), weapon = Index(main, "Weapon");
        var area = ShotKit.Bounds(Canvas(main), main).Union(ShotKit.Bounds(Timeline(main), main)).Inflate(2);
        // ① 그룹의 지금 스킨 ② 위에 겹치는 다른 그룹 ③ 물려받아 흐린 칸
        k.Save(main, crop: area, marks: [ChipRect(main, g), RowRect(main, gear).Union(RowRect(main, weapon)), CellsRect(main, head, 0, 3)]);
    }

    [Shot("parts-per-part")]
    public static void PerPart(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        var group = Layer(main, GroupName);
        var head = Layer(main, "Head");
        int hi = Index(main, "Head"), bi = Index(main, "Body"), g = Index(main, GroupName);
        // 몸은 그룹 스킨 knight, 머리만 zombie
        ctx.SetPartSkin(head, group.Skins.First(s => s.Name == "zombie"));
        k.Settle(300);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var crop = new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y);
        // ① 몸(그룹을 따름) ② 머리(부위 재정의) ③ 그룹의 지금 스킨
        k.Save(main, crop: crop, marks: [ChipRect(main, bi), ChipRect(main, hi), ChipRect(main, g)]);

        // ④ 이 부위 스킨 단추 ⑤ "개별" 표시 (스킨 관리 창 Head 줄)
        var w = OpenSkinManager(k, main, head, height: 1120);
        var box = GroupBox(w, "Per part (3");
        var add = ShotKit.FindText(box, "This part's skins", ancestorControl: true);
        var hat = ShotKit.Bounds(Card(box, "Hat"), w).Inflate(6);
        k.SaveControl(box, "parts-per-part-2", pad: 6, marks: [add, hat], firstNumber: 4);
        // ⑨ 전체 스킨으로 올리기 (Hat 카드 우클릭)
        k.Click(Card(box, "Hat"), MouseButton.Right, settleMs: 400);
        SaveWindowMenu(k, w, "parts-per-part-4", box, [MenuItemByText(UiStrings.S_SkinManagerWindow_PartOnlyCardMenu_C1E4068EB0)], 9);
        CloseMenus(k, w);
        // ⑩ 이 부위 전용으로 내리기 (Head 줄의 knight 카드 우클릭)
        box = GroupBox(w, "Per part (3");
        var headKnight = Card(box, "knight", 1);
        k.Click(headKnight, MouseButton.Right, settleMs: 400);
        SaveWindowMenu(k, w, "parts-per-part-5", box, [MenuItemStarting(UiStrings.S_SkinManagerWindow_PartCardMenu_2BB7F0B311)], 10);
        CloseMenus(k, w);
        w.Close();
        k.Settle(200);

        // ⑥ 이 부위 스킨 하위 메뉴 ⑦ 이 부위 스킨… (새로 만들기) ⑧ 모자 (개별)
        RightClickRow(k, main, hi);
        var sub = OpenMenuItems().First(m => m.ItemsSource is not null);
        sub.IsSubMenuOpen = true;
        k.Settle(400);
        SaveMenu(k, main, "parts-per-part-3", hi,
        [
            sub, MenuItemByText(UiStrings.S_TimelinePanel_PartSkinMenuItems_AD14B32035),
            MenuItemByText(string.Format(UiStrings.S_TimelinePanel_PartSkinMenuItems_6C100EAA80, HatSkin)),
        ], 6);
    }

    [Shot("parts-skin-manager")]
    public static void SkinManager(ShotKit k)
    {
        var main = Open(k);
        int g = Index(main, GroupName);
        // ① 스킨 칩 ② 프레임·레이어 > 스킨 관리… ③ 착장 줄의 관리…
        var manage = ShotKit.All<Button>(ShotKit.Get<Control>(Preview(main), "_outfitHost")).First(b => b.Content is string s && s.StartsWith("Manage", StringComparison.Ordinal));
        var item = OpenCommandMenu(k, main, "스킨 관리");
        k.Save(main, marks: [ChipRect(main, g), InTop(main, item), ShotKit.Bounds(manage, main).Inflate(4)]);
        CloseMenus(k, main);

        var w = OpenSkinManager(k, main, null, height: 1120);
        var filter = ShotKit.Get<Control>(w, "_filter");
        var preview = ShotKit.Get<Control>(w, "_preview");
        var box = GroupBox(w, "Per part (3");
        // 캐릭터 그룹의 "부위별"을 펼친다(부위 줄·없음 카드).
        if (!ShotKit.All<TextBlock>(box).Any(t => t.Text == "None"))
        {
            k.Click(PerPartButton(box));
            k.Settle(500);
            box = GroupBox(w, "Per part (3");
        }
        var cards = new[] { Card(box, t => t.StartsWith(BaseSkin, StringComparison.Ordinal)), Card(box, "knight"), Card(box, "zombie") };
        var cardsRect = cards.Select(c => ShotKit.Bounds(c, w)).Aggregate((a, b) => a.Union(b));
        var newSkin = ShotKit.FindText(box, "New skin", ancestorControl: true);
        var dup = ShotKit.FindText(box, "Duplicate", ancestorControl: true);
        // ④ 전체 착장 ⑤ 그룹 스킨 카드 ⑥ 새 스킨 ⑦ 복제 — 창 위쪽부터 캐릭터 그룹 카드까지
        k.Save(w, "parts-skin-manager-2", crop: new Rect(0, 0, w.Bounds.Width, cardsRect.Bottom + 14), titleBar: true,
            marks: [ShotKit.Bounds(preview, w).Deflate(12), cardsRect, newSkin, dup], firstNumber: 4);
        // ⑧ 카드 우클릭 메뉴(이름 바꾸기…·삭제·앞으로/뒤로) ⑨ 부위별 ⑩ 없음 카드 ⑪ 보기 거르기
        var perPart = PerPartButton(box);
        var none = Card(box, "None");
        k.Click(Card(box, "zombie"), MouseButton.Right, settleMs: 400);
        var edits = InTop(w, MenuItemByText(UiStrings.S_SkinManagerWindow_GroupCardMenu_120694637E))
            .Union(InTop(w, MenuItemByText(UiStrings.S_SkinManagerWindow_GroupCardMenu_B0B89947BD)));
        k.Save(w, "parts-skin-manager-3", marks: [edits, perPart, none, filter], firstNumber: 8);
        CloseMenus(k, w);
        w.Close();
    }

    [Shot("parts-outfits")]
    public static void Outfits(ShotKit k)
    {
        var main = Open(k);
        var preview = Preview(main);
        var host = ShotKit.Get<Control>(preview, "_outfitHost");
        // ① 착장 줄
        k.SaveControl(preview, pad: 2, marks: [host]);
        // ② 그룹 우클릭 > 스킨
        int g = Index(main, GroupName);
        RightClickRow(k, main, g);
        var skin = MenuItemStarting(UiStrings.S_TimelinePanel_ShowLayerMenu_07F7E49A77);
        skin.IsSubMenuOpen = true;
        k.Settle(400);
        SaveMenu(k, main, "parts-outfits-2", g, [skin], 2);
        CloseMenus(k, main);
        // ③ 부위 우클릭 > 이 부위 스킨
        int hi = Index(main, "Head");
        RightClickRow(k, main, hi);
        var sub = OpenMenuItems().First(m => m.ItemsSource is not null);
        sub.IsSubMenuOpen = true;
        k.Settle(400);
        SaveMenu(k, main, "parts-outfits-3", hi, [sub], 3);
        CloseMenus(k, main);
        // ④ 지금 착장 저장… ⑤ 착장 프리셋 카드 ⑥ 카드 우클릭 메뉴
        var w = OpenSkinManager(k, main, null, width: 560);
        var outfits = ShotKit.Get<Control>(w, "_outfits");
        var save = ShotKit.All<Button>(outfits).First(b => ShotKit.All<TextBlock>(b).Any(t => t.Text?.StartsWith("Save current outfit", StringComparison.Ordinal) == true)
                                                          || b.Content is string s && s.StartsWith("Save current outfit", StringComparison.Ordinal));
        var cards = ShotKit.Bounds(Card(outfits, OutfitName), w).Union(ShotKit.Bounds(Card(outfits, "Zombie"), w));
        // 카드 오른쪽 아래에서 우클릭(메뉴가 카드를 덮지 않게)
        var zombie = ShotKit.Bounds(Card(outfits, "Zombie"), w);
        k.Click(w, new Point(zombie.Right - 3, zombie.Bottom - 3), MouseButton.Right, settleMs: 400);
        var menu = OpenPopupsRect(w) ?? throw new InvalidOperationException("outfit menu did not open");
        var area = ShotKit.Bounds(outfits, w).Union(menu).Inflate(8);
        // 덮어쓰기·이름 바꾸기·삭제·순서(맨 위 "적용" 다음부터)
        var items = OpenMenuItems().Where(m => m.IsVisible).Skip(1).Select(m => InTop(w, m)).Aggregate((a, b) => a.Union(b));
        k.Save(w, "parts-outfits-4", crop: area, marks: [InTop(w, save), cards, items], firstNumber: 4);
        CloseMenus(k, w);
        w.Close();
    }

    [Shot("parts-skin-export")]
    public static void SkinExport(ShotKit k)
    {
        var main = Open(k, p => p.Edit("skins", ProjectChange.Settings, () => p.Export.Common.Skins = SkinSource.Selected));
        k.Dialog(() => k.Command("파일/내보내기"), w =>
        {
            ShotKit.Pump(600);
            ScrubPaths(k, w);
            // 나눔 단추(TabStrip 칸 = Border) 또는 보통 단추
            Control Btn(string text) => ShotKit.All<TextBlock>(w).First(t => t.Text?.StartsWith(text, StringComparison.Ordinal) == true && t.IsEffectivelyVisible)
                .GetVisualAncestors().OfType<Control>().First(c => c is Button or Border);
            // ① 지금 착장 ② 고른 스킨 ③ 스킨 고르기… ④ 한 이미지 ⑤ 따로 파일
            Rect B(string text) => ShotKit.Bounds(Btn(text), w).Inflate(3);
            k.Save(w, marks: [B("Current outfit"), B("Selected skins"), B("Choose skins"), B("One image"), B("Separate files")]);
        });
        var p = Project(main);
        k.Dialog(() => new SkinPickerWindow(p, p.Export.Common, partsOnly: false, showBake: true, 0).ShowDialog(main), w =>
        {
            foreach (var sv in ShotKit.All<ScrollViewer>(w)) sv.MaxHeight = double.PositiveInfinity;
            w.SizeToContent = SizeToContent.Height;
            ShotKit.Pump(600);
            Rect Heading(string text) => ShotKit.Bounds(ShotKit.All<TextBlock>(w).First(t => t.Text == text), w);
            var skins = Heading("Skins");
            var outfits = Heading("Outfit presets");
            var layers = Heading("Layers to composite");
            double right = w.Bounds.Width - 16;
            // 묶음 테두리를 제목 위 빈틈까지 올려 번호가 제목 글자를 가리지 않게 한다.
            var skinArea = new Rect(skins.X, skins.Y - 14, right - skins.X, outfits.Y - 20 - skins.Y + 14);
            var outfitArea = new Rect(outfits.X, outfits.Y - 14, right - outfits.X, layers.Y - 6 - outfits.Y + 14);
            var bake = ShotKit.All<CheckBox>(w).First(c => c.Content as string == UiStrings.S_SkinPickerWindow_SkinPickerWindow_D6CC3CF8C8);
            var bakeArea = Heading("Part atlas").Union(ShotKit.Bounds(bake, w));
            // ⑥ 스킨 ⑦ 착장 프리셋 ⑧ 물려받은 칸도 이 스킨 스프라이트로
            k.Save(w, "parts-skin-export-2", marks: [skinArea, outfitArea, bakeArea], firstNumber: 6);
        });
    }

    [Shot("parts-sprite-library")]
    public static void SpriteLibrary(ShotKit k)
    {
        var main = Open(k, p => p.Edit("lib", ProjectChange.Settings, () => p.Export.WriteSpriteLibrary = true));
        k.Dialog(() => k.Command("파일/내보내기"), w =>
        {
            ShotKit.Pump(600);
            var parts = Section(w, UiStrings.S_UnityExportDialog_UnityExportDialog_B18F4A0368);
            parts.BringIntoView();
            ShotKit.Pump(400);
            ScrubPaths(k, w);
            var label = ShotKit.All<TextBlock>(parts).First(t => t.Text == "Sprite Library");
            var row = AfterLabel(w, (Panel)label.GetVisualParent()!);
            var result = ShotKit.All<TextBlock>(parts).First(t => t.Text?.StartsWith("→", StringComparison.Ordinal) == true && t.Text.Contains(".spriteLib", StringComparison.Ordinal) && t.IsEffectivelyVisible);
            // ① Sprite Library ② 만들어지는 파일
            k.SaveControl(parts, pad: 6, marks: [row, ShotKit.Bounds(result, w).Inflate(new Thickness(10, 2))]);
        });
    }

    [Shot("parts-import")]
    public static void Import(ShotKit k)
    {
        // 예시 프로젝트의 부위 아틀라스(PNG + .meta, 칸 크기가 서로 다름)를 다른 폴더에 내보내 그 PNG를 연다.
        var outDir = Path.Combine(k.WorkDir, "sheet");
        Directory.CreateDirectory(outDir);
        var src = Build(k, p =>
        {
            for (int f = p.Frames.Count - 1; f > 0; f--) p.DeleteFrame(f);
            p.DeleteLayer(p.IndexOf(p.Layers.First(l => l.Name == GearName)));
            var group = p.Layers.First(l => l.Name == GroupName);
            p.SetActiveSkin(group, group.Skins[0]);
            p.Edit("export", ProjectChange.Settings, () =>
            {
                p.Export.ExportFrames = false;
                p.Export.ExportParts = true;
                p.Export.WriteMeta = true;
                p.Export.Folder = outDir;
                p.Export.PartsFileName = "wolf_parts";
                p.Export.Common.Naming = SpriteNaming.FrameNumber;
            });
        });
        ProjectIO.SaveWithExport(ProjectIO.Load(src), src);
        var png = Directory.GetFiles(outDir, "*.png").First();
        App.Settings.BottomHeight = 330;
        App.Settings.FilesVisible = false;
        var main = k.Main(png);
        ShotKit.Call(Canvas(main), "FitToWindow");
        k.Settle(400);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var p = Project(main);
        int g = p.Layers.ToList().FindIndex(l => l.IsGroup);
        int first = p.Layers.ToList().FindIndex(l => l.IsPart), last = p.Layers.ToList().FindLastIndex(l => l.IsPart);
        // ① 그룹 하나 ② 칸마다 부위
        k.Save(main, crop: new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y),
            marks: [RowRect(main, g), RowRect(main, Math.Min(first, last)).Union(RowRect(main, Math.Max(first, last)))]);
        // ③ 프레임 시트는 꺼진 채(내보낼 결과 = 부위 아틀라스)
        k.Dialog(() => k.Command("파일/내보내기"), w =>
        {
            ShotKit.Pump(600);
            ScrubPaths(k, w);
            var output = (Control)ShotKit.All<TextBlock>(w).First(t => t.Text == UiStrings.S_UnityExportDialog_BuildCommonBlock_FE8EEAD4AB).GetVisualParent()!;
            var common = SectionStarting(w, "Common");
            k.SaveControl(common, "parts-import-2", pad: 4, marks: [output], firstNumber: 3);
        });
    }

    // ───────────── 도우미 ─────────────

    private static Control Canvas(MainForm main) => ShotKit.Get<Control>(main, "_canvas");
    private static Control Timeline(MainForm main) => ShotKit.Get<Control>(main, "_timeline");
    private static Control Preview(MainForm main) => ShotKit.Get<Control>(main, "_preview");
    private static Control Grid(MainForm main) => ShotKit.Get<Control>(Timeline(main), "_grid");

    private const int NameW = 270, CellH = 36, CellW = 42;

    /// <summary>타임라인 이름 칸의 레이어 행(창 좌표).</summary>
    private static Rect RowRect(MainForm main, int layer)
    {
        var grid = Grid(main);
        int y = (int)ShotKit.Call(grid, "LayerY", layer)!;
        var p = grid.TranslatePoint(new Point(0, y), main)!.Value;
        return new Rect(p.X, p.Y, NameW, CellH);
    }

    /// <summary>레이어 행의 프레임 칸들(창 좌표).</summary>
    private static Rect CellsRect(MainForm main, int layer, int from, int to)
    {
        var row = RowRect(main, layer);
        return new Rect(row.X + NameW + from * CellW, row.Y, (to - from + 1) * CellW, CellH);
    }

    private static Rect ChipRect(MainForm main, int layer)
    {
        var (x, w) = ((int, int))ShotKit.Call(Grid(main), "SkinChip", Project(main).Layers[layer])!;
        var row = RowRect(main, layer);
        return new Rect(row.X + x, row.Y + 9, w, CellH - 18);
    }

    /// <summary>부위 행의 크기 글자("47×40") — 스킨 칩 바로 왼쪽(타임라인 그리기와 같은 자리).</summary>
    private static Rect SizeRect(MainForm main, int layer)
    {
        var part = Project(main).Layers[layer];
        var (x, _) = ((int, int))ShotKit.Call(Grid(main), "SkinChip", part)!;
        var (_, w) = ((string, int))ShotKit.Call(Grid(main), "PartSizeText", part)!;
        var row = RowRect(main, layer);
        return new Rect(row.X + x - 4 - w, row.Y + 9, w, CellH - 18);
    }

    private static Rect BadgeRect(MainForm main, int layer)
    {
        var (x, w) = ((int, int))ShotKit.Call(Grid(main), "LayoutBadge", Project(main).Layers[layer])!;
        var row = RowRect(main, layer);
        return new Rect(row.X + x, row.Y + 9, w, CellH - 18);
    }

    /// <summary>그룹 접기 삼각형 자리(이름 칸 48~64).</summary>
    private static Rect ToggleRect(MainForm main, int layer)
    {
        var row = RowRect(main, layer);
        return new Rect(row.X + 50, row.Y + 9, 10, CellH - 18);
    }

    /// <summary>캔버스의 이미지 좌표 → 창 좌표.</summary>
    private static Point CanvasPoint(MainForm main, double x, double y)
    {
        var canvas = Canvas(main);
        var sp = (Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!;
        return canvas.TranslatePoint(sp, main)!.Value;
    }

    /// <summary>그룹 화면에서 부위가 차지하는 캔버스 영역(창 좌표).</summary>
    private static Rect PartOnCanvas(MainForm main, Layer part)
        => new(CanvasPoint(main, part.PartRect.X, part.PartRect.Y), CanvasPoint(main, part.PartRect.Right, part.PartRect.Bottom));

    private static Rect Around(Point p, double r) => new(p.X - r, p.Y - r, 2 * r, 2 * r);

    private static void RightClickRow(ShotKit k, MainForm main, int layer)
    {
        var row = RowRect(main, layer);
        k.Click(main, new Point(row.X + 120, row.Center.Y), MouseButton.Right, settleMs: 400);
    }

    private static IEnumerable<Popup> OpenPopups()
    {
        var popups = (System.Collections.IEnumerable)typeof(ShotKit).GetField("s_popups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        return popups.Cast<Popup>().Where(p => p.IsOpen).ToList();
    }

    private static IEnumerable<MenuItem> OpenMenuItems()
    {
        foreach (var p in OpenPopups())
            if (p.Child is Visual v)
                foreach (var m in ShotKit.All<MenuItem>(v).Concat(v is MenuItem mi ? [mi] : []))
                    yield return m;
    }

    /// <summary>열린 팝업(메뉴·하위 메뉴)들을 모두 담는 사각형(<paramref name="top"/> 창 좌표). 열린 것이 없으면 null.</summary>
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
    {
        string plain = text.Replace("_", "");
        return OpenMenuItems().FirstOrDefault(m => HeaderText(m).Replace("_", "") == plain)
               ?? throw new InvalidOperationException($"no menu item '{text}' in [{string.Join(" | ", OpenMenuItems().Select(HeaderText))}]");
    }

    /// <summary>머리글이 형식 문자열 <paramref name="format"/>의 {0} 앞부분으로 시작하는 메뉴 항목.</summary>
    private static MenuItem MenuItemStarting(string format)
    {
        string head = format.Split('{')[0].Replace("_", "");
        return OpenMenuItems().FirstOrDefault(m => HeaderText(m).Replace("_", "").StartsWith(head, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"no menu item starting '{head}' in [{string.Join(" | ", OpenMenuItems().Select(HeaderText))}]");
    }

    /// <summary>열린 메뉴와 그 레이어 행을 함께 담아 저장한다.</summary>
    private static void SaveMenu(ShotKit k, MainForm main, string name, int layer, IEnumerable<Control> marks, int firstNumber)
    {
        var area = RowRect(main, layer);
        if (OpenPopupsRect(main) is { } menu) area = area.Union(menu);
        var rects = Spread(marks.Select(m => InTop(main, m)));
        foreach (var r in rects) area = area.Union(r.Inflate(16));
        k.Save(main, name, crop: area.Inflate(10), marks: Marks(rects), firstNumber: firstNumber);
    }

    /// <summary>창 <paramref name="w"/>의 열린 우클릭 메뉴와 <paramref name="context"/>를 함께 담아 저장한다.</summary>
    private static void SaveWindowMenu(ShotKit k, Window w, string name, Control context, IEnumerable<Control> marks, int firstNumber)
    {
        var area = ShotKit.Bounds(context, w);
        if (OpenPopupsRect(w) is { } menu) area = area.Union(menu);
        var rects = Spread(marks.Select(m => InTop(w, m)));
        foreach (var r in rects) area = area.Union(r.Inflate(16));
        k.Save(w, name, crop: area.Inflate(8), marks: Marks(rects), firstNumber: firstNumber);
    }

    /// <summary>명령 id 끝이 <paramref name="tail"/>인 메뉴 항목(끝의 "…"는 무시).</summary>
    private static MenuItem CommandItem(MainForm main, string tail)
    {
        static string Plain(string s) => s.TrimEnd('…');
        var entries = ShotKit.Get<System.Collections.IEnumerable>(main, "_commands").Cast<object>().ToList();
        var entry = entries.FirstOrDefault(c => Plain(ShotKit.Get<ShortcutCommand>(c, "Info").Id).EndsWith("/" + Plain(tail), StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"no command '{tail}' in [{string.Join(" | ", entries.Select(c => ShotKit.Get<ShortcutCommand>(c, "Info").Id))}]");
        return ShotKit.Get<MenuItem?>(entry, "Item") ?? throw new InvalidOperationException("command has no menu item: " + tail);
    }

    /// <summary>명령 <paramref name="tail"/>이 든 위 메뉴를 열고 그 항목을 돌려준다.</summary>
    private static MenuItem OpenCommandMenu(ShotKit k, MainForm main, string tail)
    {
        var item = CommandItem(main, tail);
        var top = item.GetLogicalAncestors().OfType<MenuItem>().Last();
        k.Click(top, settleMs: 500);
        return item;
    }

    /// <summary>창 왼쪽 위부터 열린 위 메뉴까지 잘라 저장한다.</summary>
    private static void SaveTopMenu(ShotKit k, MainForm main, string name, IEnumerable<Control> marks, int firstNumber)
    {
        var menu = OpenPopupsRect(main) ?? new Rect(0, 0, 400, 400);
        var rects = Spread(marks.Select(m => InTop(main, m)));
        k.Save(main, name, crop: new Rect(0, 0, menu.Right + 16, menu.Bottom + 12), marks: Marks(rects), firstNumber: firstNumber);
    }

    private static void CloseMenus(ShotKit k, TopLevel top)
    {
        k.Key(top, Key.Escape);
        PopupMenus.CloseAll();
        foreach (var p in OpenPopups()) p.IsOpen = false;
        k.Settle(250);
    }

    /// <summary>
    /// 번호 동그라미(사각형 왼쪽 위)가 앞의 번호와 겹치면(위아래로 붙은 메뉴 항목 등) 그 사각형을 왼쪽으로 늘려 번호를 비켜 놓는다.
    /// </summary>
    private static List<Rect> Spread(IEnumerable<Rect> rects)
    {
        var placed = new List<Rect>();
        foreach (var r0 in rects)
        {
            var r = r0;
            while (placed.Any(p => Math.Abs(p.X - r.X) < 26 && Math.Abs(p.Y - r.Y) < 26))
                r = new Rect(r.X - 28, r.Y, r.Width + 28, r.Height);
            placed.Add(r);
        }
        return placed;
    }

    /// <summary>설정 줄에서 맨 앞 이름표를 뺀 컨트롤들의 영역(번호가 이름표 글자를 가리지 않게).</summary>
    private static Rect AfterLabel(TopLevel top, Panel row)
        => row.Children.Skip(1).Where(c => c.IsVisible).Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b));

    private static List<Mark> Marks(IEnumerable<Rect> rects) => [.. rects.Select(r => (Mark)r)];

    /// <summary>컨트롤의 <paramref name="top"/> 창 좌표 사각형(겹침 팝업은 그 창 안, 따로 뜬 팝업 창은 창 위치 차이만큼 옮김).</summary>
    private static Rect InTop(TopLevel top, Control c)
    {
        if (c.TranslatePoint(new Point(0, 0), top) is { } p) return new Rect(p, c.Bounds.Size);
        var own = TopLevel.GetTopLevel(c)!;
        return ShotKit.Bounds(c, own).Translate(PositionOf(own) - PositionOf(top));
    }

    private static Vector PositionOf(TopLevel top)
    {
        if (top is Window w) return new Vector(w.Position.X, w.Position.Y);
        var impl = top.PlatformImpl;
        if (impl?.GetType().GetProperty("Position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(impl) is PixelPoint pp)
            return new Vector(pp.X, pp.Y);
        return default;
    }

    /// <summary>내보내기 창 왼쪽 설정 묶음(제목 글자가 정확히 같은 것).</summary>
    private static Expander Section(Window w, string title)
        => ShotKit.All<Expander>(w).First(e => e.Header is TextBlock t && t.Text == title);

    private static Expander SectionStarting(Window w, string title)
        => ShotKit.All<Expander>(w).First(e => e.Header is TextBlock t && t.Text?.StartsWith(title, StringComparison.Ordinal) == true);

    /// <summary>스킨 관리 창의 그룹 상자(<paramref name="perPart"/> = 그 상자의 "부위별" 단추 글자 앞부분).</summary>
    private static Control GroupBox(Window w, string perPart)
    {
        var list = ShotKit.Get<StackPanel>(w, "_list");
        return list.Children.First(c => ShotKit.All<TextBlock>(c).Any(t => t.Text?.StartsWith(perPart, StringComparison.Ordinal) == true));
    }

    private static Control PerPartButton(Control box)
        => ShotKit.All<TextBlock>(box).First(t => t.Text?.StartsWith("Per part", StringComparison.Ordinal) == true)
            .GetVisualAncestors().OfType<Button>().First();

    /// <summary>제목이 <paramref name="title"/>인 스킨·착장 카드(<paramref name="nth"/>번째, 화면 순서).</summary>
    private static Control Card(Visual root, string title, int nth = 0) => Card(root, t => t == title, nth);

    private static Control Card(Visual root, Func<string, bool> title, int nth = 0)
        => ShotKit.All<TextBlock>(root).Where(t => t.Text is { } s && title(s)).Select(t => (Control)t.GetVisualParent()!.GetVisualParent()!).ElementAt(nth);

    /// <summary>스킨 관리 창(모달 아님)을 열어 크기를 맞춘 뒤 돌려준다(<paramref name="focus"/> = 강조할 그룹/부위).</summary>
    private static Window OpenSkinManager(ShotKit k, MainForm main, Layer? focus, double width = 1000, double height = 1000)
    {
        SkinManagerWindow.Open(Ctx(main), main, focus);
        k.Settle(300);
        var w = (Window)typeof(SkinManagerWindow).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        w.Width = width;
        w.Height = height;
        k.Settle(800);
        return w;
    }

    /// <summary>예시 폴더(임시 폴더 · 사용자 이름이 든 경로)를 보이는 글자에서 보기 좋은 예시 경로로 바꾼다(그림에만, 동작과 무관).</summary>
    private static void ScrubPaths(ShotKit k, Visual root)
    {
        const string Neutral = @"D:\MyGame\Assets\Characters";
        string dir = k.WorkDir.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var t in ShotKit.All<TextBlock>(root))
            if (t.Text is { } s && s.Contains(dir, StringComparison.OrdinalIgnoreCase))
                t.Text = s.Replace(dir, Neutral, StringComparison.OrdinalIgnoreCase);
        foreach (var t in ShotKit.All<TextBox>(root))
            if (t.Text is { } s && s.Contains(dir, StringComparison.OrdinalIgnoreCase))
                t.Text = s.Replace(dir, Neutral, StringComparison.OrdinalIgnoreCase);
        ShotKit.Pump(200);
    }
}
