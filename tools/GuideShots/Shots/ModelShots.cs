using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;

namespace GuideShots;

/// <summary>
/// 3D 모델 장(model-*): 파일 브라우저의 모델 썸네일·상세 정보, 3D 모델 가져오기 창(전체·프리셋·보기·픽셀 다듬기·프레임·애니메이션 불러오기),
/// 가져온 새 탭, 그리고 실제 그리기(<see cref="ModelFrames.Render"/>)로 설정 하나만 바꾼 비교 그림. 예시 모델은 <see cref="ModelSample"/>.
/// </summary>
internal static class ModelShots
{
    // 설정 칸 줄 번호(ModelImportDialog의 AddRow 순서).
    private const int RowPreset = 0, RowSize = 1, RowSmooth = 9, RowPixel = 10, RowOutlineLook = 12, RowFrames = 13, RowFps = 16;

    private static Grid SettingsGrid(Window w) => (Grid)ShotKit.Get<Control>(w, "_size").Parent!;

    /// <summary>
    /// 설정 칸 한 줄(이름표 + 내용)의 창 좌표 — 번호 동그라미가 이름표 왼쪽 여백에 오게 왼쪽으로 넓히고, 이웃 줄 표시와 겹치지 않게 위아래는 조금 줄인다.
    /// </summary>
    private static Rect GridRow(Window w, int row, double left = 14)
    {
        var g = SettingsGrid(w);
        var label = (Control)g.Children[row * 2];
        var content = (Control)g.Children[row * 2 + 1];
        return ShotKit.Bounds(label, w).Union(ShotKit.Bounds(content, w)).Inflate(new Thickness(left, -3, 3, -3));
    }

    private static Rect Rows(Window w, int from, int to) => Enumerable.Range(from, to - from + 1).Select(r => GridRow(w, r)).Aggregate((a, b) => a.Union(b));

    /// <summary>줄 안의 작은 컨트롤 표시: 번호 동그라미가 컨트롤 위 빈 띠(<see cref="SaveRows"/>)에 오게 위로 넓힌다.</summary>
    private static Rect Up(Window w, params Control[] controls) => Union(w, controls).Inflate(new Thickness(3, 13, 3, 1));

    /// <summary>
    /// 설정 줄 여럿을 줄마다 위에 빈 띠(번호 동그라미 자리)를 두고 세로로 쌓아 저장한다 — 한 줄에 작은 컨트롤이 붙어 있어 번호가 위 줄을 가리지 않게.
    /// 띠는 그 줄 맨 위 한 줄 픽셀(줄 사이 빈 곳)을 늘려 칠한다. <paramref name="rows"/>마다 (줄 번호, 표시들) — 번호는 적은 순서대로.
    /// </summary>
    private static void SaveRows(ShotKit k, Window w, string name, IReadOnlyList<(int Row, Rect[] Marks)> rows, int firstNumber = 1)
    {
        const int Band = 24, Side = 30;
        var grid = ShotKit.Bounds(SettingsGrid(w), w);
        var frame = k.Capture(w);
        var crops = rows.Select(r =>
        {
            var g = SettingsGrid(w);
            var b = ShotKit.Bounds((Control)g.Children[r.Row * 2], w).Union(ShotKit.Bounds((Control)g.Children[r.Row * 2 + 1], w));
            return new Rect(grid.X - Side, b.Y - 2, grid.Width + Side * 2, b.Height + 4);
        }).ToList();
        int width = (int)Math.Ceiling(crops.Max(c => c.Width));
        int height = (int)Math.Ceiling(crops.Sum(c => c.Height + Band)) + 4;
        using var target = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        var rects = new List<Rect>();
        using (var ctx = target.CreateDrawingContext())
        {
            double y = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var c = crops[i];
                ctx.DrawImage(frame, new Rect(c.X, c.Y, c.Width, 1), new Rect(0, y, width, Band));
                y += Band;
                ctx.DrawImage(frame, c, new Rect(0, y, c.Width, c.Height));
                rects.AddRange(rows[i].Marks.Select(m => m.Translate(new Vector(-c.X, y - c.Y))));
                y += c.Height;
            }
            var last = crops[^1];
            ctx.DrawImage(frame, new Rect(last.X, last.Bottom - 1, last.Width, 1), new Rect(0, y, width, height - y));
            ShotKit.DrawMarks(ctx, rects, firstNumber, new Rect(0, 0, width, height));
        }
        k.Write(target, name);
    }

    private static Rect Box(Control c, Window w, double pad = 3) => ShotKit.Bounds(c, w).Inflate(pad);

    private static Rect Union(Window w, params Control[] controls) => controls.Select(c => ShotKit.Bounds(c, w)).Aggregate((a, b) => a.Union(b));

    private static T F<T>(Window w, string name) => ShotKit.Get<T>(w, name);

    /// <summary>미리보기가 다른 스레드에서 다시 그려질 때까지 기다린다(모달 창 안).</summary>
    private static void WaitPreview() => ShotKit.Pump(1500);

    private static void OpenImport(ShotKit k, MainForm m, string path, Action<Window> withWindow)
    {
        var scene = ModelLoader.Load(path);
        k.Dialog(() => new ModelImportDialog(path, scene).ShowDialog(m), w =>
        {
            WaitPreview();
            withWindow(w);
        }, settleMs: 1200);
    }

    private static void UsePreset(Window w, int index)
    {
        F<ComboBox>(w, "_preset").SelectedIndex = index;
        WaitPreview();
    }

    // ───────────── 3D 모델 열기 ─────────────

    /// <summary>① 모델 썸네일 ② 상자 배지 ③ OBJ 썸네일(파일 브라우저), ④ 종류 ⑤ 삼각형·재질·애니메이션 수(상세 정보 창).</summary>
    [Shot("model-open")]
    public static void Open(ShotKit k)
    {
        var dir = Path.Combine(k.WorkDir, ScreenDemo.FolderName);
        Directory.CreateDirectory(dir);
        var chick = ModelSample.Chick(dir);
        ModelSample.Crate(dir);
        var anims = Path.Combine(dir, "Animations");
        Directory.CreateDirectory(anims);
        ModelSample.Walk(anims);
        ModelSample.Jump(anims);
        var sample = k.CopySample(ScreenDemo.SampleName + ".png", ScreenDemo.FolderName);
        // 상세 정보 창의 날짜가 이 컴퓨터의 지역 형식이 아니라 영어 화면에 맞는 형식으로 보이게.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new("en-US");
        var m = k.Main(sample, dir);
        ScreenDemo.PrettyRoot(m);
        k.Settle(3000);
        var browser = ShotKit.Get<Control>(m, "_browser");
        ListBoxItem Item(string name) => ShotKit.All<ListBoxItem>(browser).First(i => i.Tag is { } t && Path.GetFileName(ShotKit.Get<string>(t, "Path")) == name);
        var chickItem = Item("chick.glb");
        var crateItem = Item("crate.obj");
        var badge = ShotKit.All<Control>(chickItem).First(c => ToolTip.GetTip(c) as string == UiStrings.FileBrowser_ModelBadge);
        var b = ShotKit.Bounds(browser, m);
        var items = ShotKit.All<ListBoxItem>(browser).Where(i => i.IsEffectivelyVisible).Select(i => ShotKit.Bounds(i, m)).Aggregate((x, y) => x.Union(y));
        k.Save(m, "model-open", crop: new Rect(b.X, b.Y, b.Width, items.Bottom + 12 - b.Y),
            marks: [ShotKit.Bounds(chickItem, m).Deflate(2), ShotKit.Bounds(badge, m).Inflate(3), ShotKit.Bounds(crateItem, m).Deflate(2)]);

        k.Dialog(() => new FileInfoDialog(chick).ShowDialog(m), w =>
        {
            ShotKit.Pump(1500);
            ScreenDemo.ScrubPaths(k, w);
            ShotKit.Pump(100);
            // 값 칸만(이름표는 그대로 보이게 — 번호 동그라미가 이름표와 값 사이 빈 곳에 옴).
            Rect ValueOf(string label)
            {
                var l = ShotKit.FindText(w, label);
                var grid = (Grid)l.Parent!;
                int row = Grid.GetRow(l);
                var cells = grid.Children.Where(c => Grid.GetRow(c) == row && !ReferenceEquals(c, l)).Select(c => ShotKit.Bounds(c, w));
                return cells.Aggregate((x, y) => x.Union(y)).Inflate(new Thickness(8, 2, 8, 2));
            }
            k.Save(w, "model-open-2", marks:
            [
                ValueOf(UiStrings.FileInfo_Kind),
                ValueOf(UiStrings.FileInfo_Triangles).Union(ValueOf(UiStrings.FileInfo_Materials)).Union(ValueOf(UiStrings.FileInfo_Animations)),
            ], firstNumber: 4);
        });
    }

    // ───────────── 가져오기 창 ─────────────

    /// <summary>
    /// 창 전체(① 원본 ② 픽셀 결과 ③ 재생 ④ 요약 ⑤ 프리셋 ⑥ 설정 ⑦ 예상 ⑧ 가져오기),
    /// 원본 손잡이(⑨ 축 표시 ⑩ 회전 고리 ⑪ 바깥 기울이기 고리 ⑫ 해),
    /// 프리셋 목록(⑬ 목록 ⑭ 저장 ⑮ 지우기).
    /// </summary>
    [Shot("model-import")]
    public static void Import(ShotKit k)
    {
        var chick = ModelSample.Chick(k.WorkDir);
        ModelPresets.Save(ModelImportDialog.PresetPath,
        [
            new ModelPreset
            {
                Name = "My RPG 8-dir 40px",
                Render = ModelPresets.BuiltIns[0].Render with { Width = 40, Height = 40, Colors = 12 },
                Frames = ModelPresets.BuiltIns[0].Frames,
            },
        ]);
        var m = k.Main();
        OpenImport(k, m, chick, w =>
        {
            UsePreset(w, 0);
            F<Slider>(w, "_frame").Value = 1;
            WaitPreview();
            var preview = F<Control>(w, "_preview");
            var panes = ShotKit.Bounds(preview, w);
            var play = F<Control>(w, "_play");
            var bar = F<Control>(w, "_frame");
            k.Save(w, "model-import", marks:
            [
                new Rect(panes.X, panes.Y, 330, 330).Inflate(2),
                new Rect(panes.X + 334, panes.Y, 330, 330).Inflate(2),
                Union(w, play, bar).Inflate(new Thickness(3, -2, 3, 3)),
                ShotKit.Bounds(F<Control>(w, "_summary"), w).Inflate(new Thickness(14, 3, 3, 3)),
                GridRow(w, RowPreset),
                Rows(w, RowSize, RowFps),
                ShotKit.Bounds(F<Control>(w, "_estimate"), w).Inflate(new Thickness(14, 3, 3, 3)),
                Box(F<Button>(w, "OkButton"), w, 2),
            ]);

            // 실제 ModelPreview 왼쪽 원본 칸에 그려진 축·회전 고리·기울이기 고리·해를 자른다.
            var o = new Rect(panes.X, panes.Y, 330, 330);
            k.Hover(w, new Point(o.Center.X, o.Center.Y), 200);
            k.Save(w, "model-import-controls", crop: o.Inflate(18), marks:
            [
                new Rect(o.X + 6, o.Bottom - 88, 78, 78),
                // 회전 고리는 미리보기 가운데(165, 165) 둘레: 가로 고리 반지름 112×39, 바깥 기울이기 고리 137, 해 손잡이 (287, 44).
                new Rect(o.X + 165 - 116, o.Y + 165 - 43, 232, 86),
                new Rect(o.X + 165 + 137 - 12, o.Y + 165 - 55, 24, 110),
                new Rect(o.X + 287 - 32, o.Y + 44 - 32, 64, 64),
            ], firstNumber: 9);

            var preset = F<ComboBox>(w, "_preset");
            var row = GridRow(w, RowPreset);
            var save = ShotKit.FindText(w, UiStrings.Model_PresetSave, ancestorControl: true);
            var delete = F<Control>(w, "_deletePreset");
            preset.SelectedIndex = preset.ItemCount - 1;
            WaitPreview();
            preset.IsDropDownOpen = true;
            ShotKit.Pump(600);
            var list = PixelateKit.DropDownBounds(preset, w);
            k.Save(w, "model-import-2", crop: new Rect(row.X - 20, row.Y - 24, Math.Max(list.Right, row.Right) - row.X + 40, list.Bottom - row.Y + 40),
                marks: [list.Inflate(2), Box(save, w, 2), Box(delete, w, 2)], firstNumber: 13);
            preset.IsDropDownOpen = false;
            ShotKit.Pump(200);
        });
    }

    // ───────────── 보는 각도·빛·명암 ─────────────

    /// <summary>① 크기 ② 보는 각도 ③ 보기 방식·시야각 ④ 확대·맞춤 ⑤ 빛 방향 ⑥ 밝기 ⑦ 명암 단계 ⑧ 가장자리 다듬기·텍스처 + 비교 그림.</summary>
    [Shot("model-view")]
    public static void View(ShotKit k)
    {
        var chick = ModelSample.Chick(k.WorkDir);
        var m = k.Main();
        OpenImport(k, m, chick, w =>
        {
            F<ComboBox>(w, "_projection").SelectedIndex = 1;
            F<NumericUpDown>(w, "_steps").Value = 3;
            WaitPreview();
            SaveRows(k, w, "model-view", [.. Enumerable.Range(RowSize, RowSmooth - RowSize + 1).Select(r => (r, new[] { GridRow(w, r) }))]);
        });

        var still = new ModelFrameOptions();
        var ball = ModelLoader.Load(ModelSample.Ball(k.WorkDir));
        var round = new ModelRenderSettings { Width = 64, Height = 64, Yaw = 15, Pitch = 25, Supersample = 4, HardAlpha = true };
        Compare.Save(k, "model-view-compare", 4,
            Compare.Render(ball, round, still), Compare.Render(ball, round with { ShadeSteps = 3 }, still));
        var scene = ModelLoader.Load(chick);
        var basic = new ModelRenderSettings { Width = 64, Height = 64, Yaw = 30, Pitch = 20, Supersample = 4 };
        Compare.Save(k, "model-view-compare-2", 4,
            Compare.Render(scene, basic with { Supersample = 1 }, still), Compare.Render(scene, basic, still));
        var crate = ModelLoader.Load(ModelSample.Crate(k.WorkDir));
        var box = basic with { Yaw = 35, Pitch = 30, ShadeSteps = 3, HardAlpha = true };
        Compare.Save(k, "model-view-compare-3", 4,
            Compare.Render(crate, box, still), Compare.Render(crate, box with { Projection = ModelProjection.Perspective, FieldOfView = 70 }, still));
    }

    // ───────────── 픽셀 다듬기·외곽선 ─────────────

    /// <summary>
    /// ① 반투명 없애기 ② 색 수 ③ 겉·바깥에 ④ 안쪽 겹침 ⑤ 각진 곳·각도 ⑥ 두께 ⑦ 선 색 방식 ⑧ 선 색(한 가지 색) ⑨ 어둡게 할 정도 + 비교 그림.
    /// </summary>
    [Shot("model-pixel")]
    public static void Pixel(ShotKit k)
    {
        var chick = ModelSample.Chick(k.WorkDir);
        var m = k.Main();
        OpenImport(k, m, chick, w =>
        {
            UsePreset(w, 0);
            F<CheckBox>(w, "_inner").IsChecked = true;
            F<CheckBox>(w, "_creaseOn").IsChecked = true;
            F<ComboBox>(w, "_outlineColorMode").SelectedIndex = 0;
            WaitPreview();
            SaveRows(k, w, "model-pixel",
            [
                (RowPixel, [Up(w, F<Control>(w, "_hardAlpha")), Up(w, ShotKit.FindText(w, UiStrings.Model_Colors), F<Control>(w, "_colors"))]),
                (RowPixel + 1, [Up(w, F<Control>(w, "_outer"), F<Control>(w, "_outside")), Up(w, F<Control>(w, "_inner")), Up(w, F<Control>(w, "_creaseOn"), F<Control>(w, "_crease"))]),
                (RowOutlineLook,
                [
                    Up(w, ShotKit.FindText(w, UiStrings.Model_OutlineWidth), F<Control>(w, "_outlineWidth")), Up(w, F<Control>(w, "_outlineColorMode")),
                    Up(w, F<Control>(w, "_outlineColor")), Up(w, F<Control>(w, "_darken")),
                ]),
            ]);
        });

        var scene = ModelLoader.Load(chick);
        var still = new ModelFrameOptions();
        var look = ModelPresets.BuiltIns[0].Render with { Width = 64, Height = 64, Yaw = 35, Pitch = 20, Colors = 16, OutlineOuter = false };
        var fixedColor = look with { OutlineColorMode = ModelOutlineColor.Fixed };
        Compare.Save(k, "model-pixel-compare", 4,
            Compare.Render(scene, fixedColor, still),
            Compare.Render(scene, fixedColor with { OutlineOuter = true }, still),
            Compare.Render(scene, fixedColor with { OutlineOuter = true, OutlineInner = true, OutlineCrease = 60 }, still));
        var both = look with { OutlineOuter = true, OutlineInner = true };
        Compare.Save(k, "model-pixel-compare-2", 4,
            Compare.Render(scene, both with { OutlineColorMode = ModelOutlineColor.Fixed }, still),
            Compare.Render(scene, both with { OutlineColorMode = ModelOutlineColor.Darken, OutlineDarken = 0.55f }, still));
        var soft = new ModelRenderSettings { Width = 64, Height = 64, Yaw = 35, Pitch = 20, Supersample = 4 };
        Compare.Save(k, "model-pixel-compare-3", 4,
            Compare.Render(scene, soft, still), Compare.Render(scene, soft with { HardAlpha = true, Colors = 8 }, still));
    }

    // ───────────── 프레임 ─────────────

    /// <summary>① 프레임 방식 목록 ② 돌려 보기 프레임 수 ③ 애니메이션 ④ 초당 프레임·최대, 돌려 보기 8방향 결과 그림, ⑤ 새 탭 ⑥ 프레임(가져온 뒤 메인 창).</summary>
    [Shot("model-frames")]
    public static void Frames(ShotKit k)
    {
        var chick = ModelSample.Chick(k.WorkDir);
        App.Settings.ShowPivot = false;
        var m = k.Main();
        OpenImport(k, m, chick, w =>
        {
            UsePreset(w, 0);
            SaveRows(k, w, "model-frames", [.. Enumerable.Range(RowFrames, RowFps - RowFrames + 1).Select(r => (r, new[] { GridRow(w, r) }))]);
        });

        var scene = ModelLoader.Load(chick);
        var top = ModelPresets.BuiltIns[0];
        var turn = ModelFrames.Render(scene, top.Render, top.Frames);
        Compare.Save(k, "model-frames-2", 3, [.. turn.Images]);

        // 가져오기: 파일 열기 → 읽는 진행 창 → 가져오기 창에서 탑다운 8방향 → 가져오기 → 그리는 진행 창 → 새 탭.
        // 창마다 모달 루프라 타이머로 가져오기 창이 뜰 때까지 살피다가, 뜨면 프리셋을 고르고 가져오기를 누른다.
        void Poll()
        {
            if (k.Windows.OfType<ModelImportDialog>().FirstOrDefault() is not { } dlg)
            {
                ShotKit.Later(50, Poll);
                return;
            }
            ShotKit.Later(800, () =>
            {
                F<ComboBox>(dlg, "_preset").SelectedIndex = 0;
                ShotKit.Later(800, () => F<Button>(dlg, "OkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
        }
        ShotKit.Later(50, Poll);
        ShotKit.Call(m, "OpenFile", chick);
        k.Settle(1500);
        ToolFileHelp.Canvas(m).SetZoom(6);
        k.Settle(400);
        var timeline = ShotKit.Get<Control>(m, "_timeline");
        var tab = ShotKit.FindText(m, "chick.glb");
        var grid = ShotKit.Get<Control>(timeline, "_grid");
        int x0 = (int)ShotKit.Call(grid, "FrameX", 0)!, x7 = (int)ShotKit.Call(grid, "FrameX", 7)!, w7 = (int)ShotKit.Call(grid, "FrameW", 7)!;
        var head0 = grid.TranslatePoint(new Point(x0, 20), m)!.Value;
        var head7 = grid.TranslatePoint(new Point(x7 + w7, 50), m)!.Value;
        var tb = ShotKit.Bounds(timeline, m);
        var canvas = ShotKit.Bounds(ToolFileHelp.Canvas(m), m);
        var cropMain = new Rect(tb.X - 2, ShotKit.Bounds(tab, m).Y - 30, canvas.Right - tb.X + 2, 0);
        cropMain = cropMain.WithHeight(tb.Bottom - cropMain.Y);
        k.Save(m, "model-frames-3", crop: cropMain,
            marks: [ShotKit.Bounds(tab, m).Inflate(new Thickness(4, 16, 30, 4)), new Rect(head0, head7)], firstNumber: 5);
    }

    // ───────────── 애니메이션 불러오기 ─────────────

    /// <summary>① 불러오기… ② 붙인 애니메이션 목록 ③ 요약(애니메이션 수) ④ 프레임 = 애니메이션 ⑤ 재생·프레임 막대, 걷기 결과 그림.</summary>
    [Shot("model-animation")]
    public static void Animation(ShotKit k)
    {
        var chick = ModelSample.Chick(k.WorkDir);
        var anims = Path.Combine(k.WorkDir, "Animations");
        Directory.CreateDirectory(anims);
        string[] files = [ModelSample.Walk(anims), ModelSample.Jump(anims)];
        var m = k.Main();
        ModelScene attached = null!;
        OpenImport(k, m, chick, w =>
        {
            UsePreset(w, 3);
            // '불러오기…'가 고른 파일에 하는 일(ModelImportDialog.ImportAnimations) 그대로 — 파일 고르기 창만 건너뜀.
            var scene = ShotKit.Get<ModelScene>(w, "_scene");
            int first = scene.Animations.Count;
            foreach (var f in files)
                scene = ModelAnimationImport.Attach(scene, ModelLoader.LoadAnimations(f), Path.GetFileNameWithoutExtension(f)).Scene;
            attached = scene;
            ShotKit.Set(w, "_scene", scene);
            ShotKit.Set(w, "_posed", null);
            ShotKit.Call(w, "FillClips", first);
            F<ComboBox>(w, "_mode").SelectedIndex = (int)ModelFrameMode.Animation;
            ShotKit.Call(w, "UpdateSummary");
            ShotKit.Call(w, "Sync");
            WaitPreview();
            F<Slider>(w, "_frame").Value = 2;
            WaitPreview();
            var clip = F<ComboBox>(w, "_clip");
            clip.IsDropDownOpen = true;
            ShotKit.Pump(600);
            var list = PixelateKit.DropDownBounds(clip, w);
            k.Save(w, "model-animation", marks:
            [
                Box(F<Control>(w, "_importAnim"), w, 2),
                list.Union(ShotKit.Bounds(clip, w)).Inflate(3),
                Box(F<Control>(w, "_summary"), w),
                Box(F<Control>(w, "_mode"), w),
                Union(w, F<Control>(w, "_play"), F<Control>(w, "_frame")).Inflate(3),
            ]);
            clip.IsDropDownOpen = false;
            ShotKit.Pump(200);
        });

        var side = ModelPresets.BuiltIns[3];
        var walk = ModelFrames.Render(attached, side.Render, side.Frames with { Animation = 1, Fps = 10 });
        Compare.Save(k, "model-animation-2", 3, [.. walk.Images]);
    }

    /// <summary>실제 그리기 결과를 나란히 놓은 그림(어두운 바탕 위 체커 칸, 최근접 정수 배 확대).</summary>
    private static class Compare
    {
        private static readonly Color32 Back = new(43, 43, 43);
        private static readonly Color32 Light = new(204, 204, 204), Dark = new(170, 170, 170);
        private const int Margin = 16, Gap = 24, Checker = 8;

        public static PixelImage Render(ModelScene scene, ModelRenderSettings settings, ModelFrameOptions frames)
            => ModelFrames.Render(scene, settings, frames).Images[0];

        public static void Save(ShotKit k, string name, int scale, params PixelImage[] panels)
        {
            int gap = panels.Length > 4 ? Gap / 2 : Gap;
            int height = panels.Max(p => p.Height) * scale;
            var img = new PixelImage(Margin * 2 + panels.Sum(p => p.Width * scale) + gap * (panels.Length - 1), Margin * 2 + height);
            for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Back;
            int left = Margin;
            foreach (var p in panels)
            {
                for (int y = 0; y < p.Height * scale; y++)
                    for (int x = 0; x < p.Width * scale; x++)
                        img[left + x, Margin + y] = Over(p[x / scale, y / scale], (x / Checker + y / Checker) % 2 == 0 ? Light : Dark);
                left += p.Width * scale + gap;
            }
            var file = Path.Combine(ShotKit.OutDir, name + ".png");
            Directory.CreateDirectory(ShotKit.OutDir);
            PngIO.Save(img, file);
            k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
        }

        private static Color32 Over(Color32 c, Color32 under)
        {
            if (c.A == 255) return c;
            double a = c.A / 255.0;
            byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a));
            return new Color32(Mix(c.R, under.R), Mix(c.G, under.G), Mix(c.B, under.B));
        }
    }
}
