using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Localization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using static GuideShots.PartsSample;

namespace GuideShots;

/// <summary>부위(그룹) 레이어 · 스킨 장(parts-*) 그림.</summary>
internal static class PartsShots
{
    // ───────────── 그룹과 부위 · 만들기 · 타임라인 ─────────────

    [Shot("parts-concept")]
    public static void Concept(ShotKit k)
    {
        var main = Open(k);
        k.SaveArea([Canvas(main), Timeline(main)], pad: 2);
    }

    [Shot("parts-create")]
    public static void Create(ShotKit k)
    {
        var main = Open(k);
        k.Dialog(() => k.Command("파일/새 프로젝트"), w =>
        {
            var parts = ShotKit.All<RadioButton>(w).First(r => r.Content as string == UiStrings.S_Dialogs_NewImageDialog_0F2FF932AA);
            parts.IsChecked = true;
            ShotKit.Pump(200);
            k.Save(w, marks: [parts]);
        });
        k.Dialog(() => k.Command("프레임·레이어/새 부위"), w =>
        {
            var name = ShotKit.All<TextBox>(w).First();
            name.Text = "Arm";
            name.SelectionStart = name.SelectionEnd = name.CaretIndex = 3;
            ShotKit.Pump(200);
            k.Save(w, "parts-create-2");
        });
        // 그룹 우클릭 메뉴: "부위 추가…"
        RightClickRow(k, main, Index(main, GroupName));
        var add = MenuItemByText(main, UiStrings.S_TimelinePanel_ShowLayerMenu_CC4093DC8F);
        SaveMenu(k, main, "parts-create-3", Index(main, GroupName), [add]);
    }

    [Shot("parts-timeline")]
    public static void TimelineView(ShotKit k)
    {
        var main = Open(k);
        int g = Index(main, GroupName);
        var tl = Timeline(main);
        var b = ShotKit.Bounds(tl, main);
        var crop = new Rect(b.X, b.Y, Math.Min(b.Width, 520), b.Height);
        k.Save(main, crop: crop, markRects: [ChipRect(main, g), ToggleRect(main, g)]);
    }

    // ───────────── 배치(그룹 선택) · 그리기(부위 선택) ─────────────

    [Shot("parts-arrange")]
    public static void Arrange(ShotKit k)
    {
        var main = Open(k);
        var head = Layer(main, "Head");
        var body = Layer(main, "Body");
        // Head 클릭 = 고른 부위(파란 실선), Body 위에 마우스 = 점선 + 이름
        k.Click(main, CanvasPoint(main, head.PartRect.X + head.PartRect.Width / 2.0, head.PartRect.Y + 8));
        k.Hover(main, CanvasPoint(main, body.PartRect.X + body.PartRect.Width / 2.0, body.PartRect.Y + body.PartRect.Height / 2.0), 400);
        var tl = CanvasPoint(main, 0, 0);
        var br = CanvasPoint(main, CanvasW, CanvasH);
        k.Save(main, crop: new Rect(tl, br).Inflate(new Thickness(140, 24)));
    }

    [Shot("parts-draw")]
    public static void Draw(ShotKit k)
    {
        var main = Open(k);
        int head = Index(main, "Head");
        Select(k, main, "Head");
        ShotKit.Call(Canvas(main), "FitToWindow");
        k.Command("보기/축소");
        k.Command("보기/축소");
        k.Settle(300);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var crop = new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y);
        k.Save(main, crop: crop, markRects: [RowRect(main, head)]);
        // 부위 우클릭 메뉴: 크기… · 위치… · 이 부위 스킨
        RightClickRow(k, main, head);
        var size = MenuItemStarting(main, UiStrings.S_TimelinePanel_ShowLayerMenu_66BE4C8AA7);
        var pos = MenuItemStarting(main, UiStrings.S_TimelinePanel_ShowLayerMenu_73BDC86998);
        SaveMenu(k, main, "parts-draw-2", head, [size, pos]);
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
        GroupLayout? layout = null;
        k.Dialog(() => ShotKit.Call(Timeline(main), "EditGroupLayout", g), w =>
        {
            ShotKit.Pump(300);
            layout = ((GroupLayoutDialog)w).Layout;
            k.Save(w);
        });
        Ctx(main).SetGroupLayout(group, layout);
        k.Settle(300);
        ShotKit.Call(Canvas(main), "FitToWindow");
        k.Settle(300);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var crop = new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y);
        k.Save(main, "parts-auto-layout-2", crop: crop, markRects: [BadgeRect(main, g)]);
    }

    // ───────────── 내보내기 ─────────────

    [Shot("parts-export")]
    public static void Export(ShotKit k)
    {
        var main = Open(k);
        k.Dialog(() => k.Command("파일/내보내기"), w =>
        {
            ShotKit.Pump(600);
            var output = ShotKit.All<TextBlock>(w).First(t => t.Text == UiStrings.S_UnityExportDialog_BuildCommonBlock_FE8EEAD4AB);
            var parts = Section(w, UiStrings.S_UnityExportDialog_UnityExportDialog_B18F4A0368);
            var section = ShotKit.Get<object>(w, "_partsSection");
            ShotKit.Get<Control>(section, "Panel").BringIntoView();
            ShotKit.Pump(400);
            ScrubPaths(k, w);
            k.Save(w, marks: [(Control)output.GetVisualParent()!]);
            parts.BringIntoView();
            ShotKit.Pump(400);
            k.Save(w, "parts-export-2", marks: [parts]);
        });
    }

    // ───────────── 스킨 ─────────────

    [Shot("parts-skins")]
    public static void Skins(ShotKit k)
    {
        var main = Open(k);
        int head = Index(main, "Head");
        int g = Index(main, GroupName);
        var area = ShotKit.Bounds(Canvas(main), main).Union(ShotKit.Bounds(Timeline(main), main)).Inflate(2);
        k.Save(main, crop: area, markRects: [CellsRect(main, head, 0, 3), ChipRect(main, g)]);
    }

    [Shot("parts-per-part")]
    public static void PerPart(ShotKit k)
    {
        var main = Open(k);
        var ctx = Ctx(main);
        var head = Layer(main, "Head");
        int hi = Index(main, "Head");
        ctx.SetPartSkin(head, head.OwnSkins[0]);
        k.Settle(300);
        var canvas = ShotKit.Bounds(Canvas(main), main);
        var tl = ShotKit.Bounds(Timeline(main), main);
        var crop = new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y);
        k.Save(main, crop: crop, markRects: [ChipRect(main, hi)]);
        // 부위 우클릭 → "이 부위 스킨" 하위 메뉴
        RightClickRow(k, main, hi);
        var sub = OpenMenuItems().First(m => m.ItemsSource is not null);
        sub.IsSubMenuOpen = true;
        k.Settle(400);
        SaveMenu(k, main, "parts-per-part-2", hi, [sub]);
    }

    [Shot("parts-skin-manager")]
    public static void SkinManager(ShotKit k)
    {
        var main = Open(k);
        var w = OpenSkinManager(k, main, Layer(main, "Head"), height: 1120);
        k.Save(w);
        w.Close();
    }

    [Shot("parts-outfits")]
    public static void Outfits(ShotKit k)
    {
        var main = Open(k);
        var preview = Preview(main);
        var host = ShotKit.Get<Control>(preview, "_outfitHost");
        k.SaveControl(preview, pad: 2, marks: [host]);
        var w = OpenSkinManager(k, main, null, width: 560);
        var outfits = ShotKit.Get<Control>(w, "_outfits");
        k.SaveControl(outfits, "parts-outfits-2", pad: 6);
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
            Control Row(string text) => (Control)ShotKit.All<TextBlock>(w).First(t => t.Text == text && t.IsEffectivelyVisible).GetVisualParent()!;
            k.Save(w, marks: [Row(UiStrings.S_UnityExportDialog_SheetRows_4EAC72F29F), Row(UiStrings.S_UnityExportDialog_SheetRows_F65F934352)]);
        });
        var p = Project(main);
        k.Dialog(() => new SkinPickerWindow(p, p.Export.Common, partsOnly: false, showBake: true, 0).ShowDialog(main), w =>
        {
            foreach (var sv in ShotKit.All<ScrollViewer>(w)) sv.MaxHeight = double.PositiveInfinity;
            w.SizeToContent = SizeToContent.Height;
            ShotKit.Pump(600);
            var bake = ShotKit.All<CheckBox>(w).FirstOrDefault(c => c.Content as string == UiStrings.S_SkinPickerWindow_SkinPickerWindow_D6CC3CF8C8);
            k.Save(w, "parts-skin-export-2", marks: bake is null ? null : [bake]);
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
            var row = (Control)label.GetVisualParent()!;
            k.SaveControl(parts, pad: 6, marks: [row]);
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
                p.Export.PartsFileName = L("늑대귀_부위", "wolf_parts");
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
        k.Save(main, crop: new Rect(canvas.X, canvas.Y, canvas.Width, tl.Bottom - canvas.Y));
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

    private static Rect OutfitRect(MainForm main)
        => ShotKit.Bounds(ShotKit.Get<Control>(Preview(main), "_outfitHost"), main);

    /// <summary>캔버스의 이미지 좌표 → 창 좌표.</summary>
    private static Point CanvasPoint(MainForm main, double x, double y)
    {
        var canvas = Canvas(main);
        var sp = (Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!;
        return canvas.TranslatePoint(sp, main)!.Value;
    }

    private static void RightClickRow(ShotKit k, MainForm main, int layer)
    {
        var row = RowRect(main, layer);
        k.Click(main, new Point(row.X + 120, row.Center.Y), MouseButton.Right, settleMs: 400);
    }

    private static IEnumerable<MenuItem> OpenMenuItems()
    {
        var popups = (System.Collections.IEnumerable)typeof(ShotKit).GetField("s_popups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        foreach (var p in popups.Cast<Popup>().Where(p => p.IsOpen))
            if (p.Child is Visual v)
                foreach (var m in ShotKit.All<MenuItem>(v).Concat(v is MenuItem mi ? [mi] : []))
                    yield return m;
    }

    private static string HeaderText(MenuItem m) => m.Header switch
    {
        string s => s,
        TextBlock t => t.Text ?? "",
        _ => ShotKit.All<TextBlock>(m).FirstOrDefault()?.Text ?? "",
    };

    private static MenuItem MenuItemByText(MainForm main, string text)
    {
        string plain = text.Replace("_", "");
        return OpenMenuItems().First(m => HeaderText(m).Replace("_", "") == plain);
    }

    /// <summary>머리글이 형식 문자열 <paramref name="format"/>의 {0} 앞부분으로 시작하는 메뉴 항목.</summary>
    private static MenuItem MenuItemStarting(MainForm main, string format)
    {
        string head = format.Split('{')[0].Replace("_", "");
        return OpenMenuItems().First(m => HeaderText(m).Replace("_", "").StartsWith(head, StringComparison.Ordinal));
    }

    /// <summary>열린 메뉴와 그 레이어 행을 함께 담아 저장한다.</summary>
    private static void SaveMenu(ShotKit k, MainForm main, string name, int layer, IEnumerable<Control> marks)
    {
        var area = RowRect(main, layer);
        var popups = (System.Collections.IEnumerable)typeof(ShotKit).GetField("s_popups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        foreach (var p in popups.Cast<Popup>().Where(p => p.IsOpen))
            if (p.Child is Control c && c.IsVisible)
                area = area.Union(InMain(main, c));
        k.Save(main, name, crop: area.Inflate(10), markRects: marks.Select(m => InMain(main, m)).ToList());
    }

    /// <summary>컨트롤의 메인 창 좌표 사각형(겹침 팝업은 메인 창 안, 따로 뜬 팝업 창은 창 위치 차이만큼 옮김).</summary>
    private static Rect InMain(MainForm main, Control c)
    {
        if (c.TranslatePoint(new Point(0, 0), main) is { } p) return new Rect(p, c.Bounds.Size);
        var top = TopLevel.GetTopLevel(c)!;
        return ShotKit.Bounds(c, top).Translate(PositionOf(top) - PositionOf(main));
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
