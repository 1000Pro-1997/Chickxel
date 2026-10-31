using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>파일·저장·내보내기 장(files-*) 그림. 번호는 쪽(h3)마다 이어진다 — 가이드 글의 ①②…와 같은 순서.</summary>
internal static class FilesShots
{
    // ───────────── 도움 함수 ─────────────

    /// <summary>연필로 한 획 그어 문서를 "수정됨"으로 만든다(그림 왼쪽 위 빈 곳).</summary>
    private static void Scribble(ShotKit k, MainForm m)
    {
        ToolFileHelp.Tool(k, "연필");
        k.Drag(m, ToolFileHelp.At(m, 1.5, 1.5), ToolFileHelp.At(m, 5.5, 1.5));
        k.Settle(200);
    }

    /// <summary>상태 표시줄 + 도구 막대 아래 저장·되돌리기 단추가 함께 보이는 아래쪽 띠(파일 칸은 접은 상태).</summary>
    private static Rect BottomStrip(MainForm m, double extraRight = 24)
    {
        var message = ShotKit.Get<TextBlock>(m, "_stMessage");
        var save = ShotKit.Bounds(ShotKit.Get<Control>(m, "_tbSave"), m);
        var msg = ShotKit.Bounds(message, m);
        double right = Math.Min(m.Bounds.Width, msg.X + message.DesiredSize.Width + extraRight);
        return new Rect(0, save.Y - 10, right, m.Bounds.Height - (save.Y - 10));
    }

    /// <summary>상태 표시줄 메시지 글자 영역(번호 동그라미가 글자를 가리지 않게 왼쪽을 조금 넓힘).</summary>
    private static Rect MessageRect(MainForm m)
    {
        var message = ShotKit.Get<TextBlock>(m, "_stMessage");
        var r = ShotKit.Bounds(message, m);
        // 강조 테두리는 3px 바깥에 그려지므로 창 아래 끝에서 잘리지 않게 위아래를 줄인다.
        return new Rect(r.X - 14, r.Y + 3, Math.Min(r.Width, message.DesiredSize.Width) + 14, r.Height - 6);
    }

    /// <summary>컨트롤 사각형을 왼쪽으로 <paramref name="left"/>만큼 넓힌 것(번호 동그라미가 글자 앞 빈 곳에 오게).</summary>
    private static Rect Lead(Control c, Visual top, double left = 10)
    {
        var r = ShotKit.Bounds(c, top);
        return new Rect(r.X - left, r.Y, r.Width + left, r.Height);
    }

    /// <summary>여러 컨트롤을 모두 감싸는 사각형.</summary>
    private static Rect Union(Visual top, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b));

    /// <summary>한 줄(Ui.Row)에서 맨 앞 이름표부터 <paramref name="field"/>까지(뒤의 긴 설명은 뺌).</summary>
    private static Rect RowUpTo(Control field, Visual top, double left = 10)
    {
        var row = field.GetVisualParent() as Panel;
        var first = row?.Children.FirstOrDefault(c => c.IsVisible) ?? field;
        var r = Union(top, first, field);
        return new Rect(r.X - left, r.Y, r.Width + left, r.Height);
    }

    /// <summary>타임라인에서 레이어 <paramref name="layer"/>(프로젝트 레이어 순번) 줄의 이름 칸(창 좌표). 이름은 그려지는 글자라 TextBlock이 없다.</summary>
    private static Rect LayerRow(MainForm m, int layer)
    {
        var grid = ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");
        int y = (int)ShotKit.Call(grid, "LayerY", layer)!;
        var at = grid.TranslatePoint(new Point(0, y), m)!.Value;
        return new Rect(at.X + 2, at.Y + 2, 266, 32);
    }

    /// <summary>
    /// <see cref="ShotKit.Save"/>와 같지만 <paramref name="crop"/>이 창 밖으로 나가도 된다(나간 곳은 투명 — 가이드 그림 바탕색이 보임) —
    /// 창 왼쪽 끝에 붙은 파일 메뉴·이름표 같은 것 앞에 번호 동그라미 자리를 만든다.
    /// </summary>
    private static string SaveOut(ShotKit k, TopLevel top, string name, Rect crop, IEnumerable<Mark> marks, int firstNumber)
    {
        var frame = k.Capture(top);
        var size = new PixelSize((int)Math.Round(crop.Width), (int)Math.Round(crop.Height));
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var inside = crop.Intersect(new Rect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height));
            ctx.DrawImage(frame, inside, inside.Translate(new Vector(-crop.X, -crop.Y)));
            ShotKit.DrawMarks(ctx, marks.Select(mk => mk.In(top).Translate(new Vector(-crop.X, -crop.Y))), firstNumber,
                new Rect(0, 0, size.Width, size.Height));
        }
        return k.Write(target, name);
    }

    /// <summary>
    /// 대화상자 전체(그려 넣은 제목 줄 포함)를 왼쪽에 <paramref name="margin"/>만큼 투명한 자리를 두고 찍는다 — 창 왼쪽 끝 가까이의
    /// 이름표·체크 상자에 단 번호 동그라미가 글자를 가리지 않게.
    /// </summary>
    private static string SaveWindow(ShotKit k, Window w, string name, IEnumerable<Mark> marks, int firstNumber, double margin = 22)
    {
        var frame = k.Capture(w);
        int bar = ShotKit.TitleHeight;
        var size = new PixelSize(frame.PixelSize.Width + (int)margin, frame.PixelSize.Height + bar);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(margin, 0)))
                typeof(ShotKit).GetMethod("DrawTitleBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, [ctx, w.Title ?? "Chickxel", frame.PixelSize.Width]);
            ctx.DrawImage(frame, new Rect(margin, bar, frame.PixelSize.Width, frame.PixelSize.Height));
            ShotKit.DrawMarks(ctx, marks.Select(mk => mk.In(w).Translate(new Vector(margin, bar))), firstNumber,
                new Rect(0, 0, size.Width, size.Height));
        }
        return k.Write(target, name);
    }

    /// <summary>위 메뉴 하나를 연다(0 = 파일, 1 = 편집, 2 = 보기, 3 = 프레임·레이어, 4 = 도구, 5 = AI, 6 = 도움말).</summary>
    private static void OpenTopMenu(ShotKit k, MainForm m, int index)
    {
        var top = ShotKit.Find<Menu>(m).Items.OfType<MenuItem>().ElementAt(index);
        k.Click(top, settleMs: 400);
    }

    private static void CloseMenus(ShotKit k)
    {
        PopupMenus.CloseAll();
        foreach (var menu in k.Windows.SelectMany(w => ShotKit.All<Menu>(w))) menu.Close();
        k.Settle(200);
    }

    /// <summary>명령 id(단축키 표의 "분류/이름", 끝의 "…"는 있어도 없어도 됨)의 메뉴 항목.</summary>
    private static MenuItem CommandItem(MainForm m, string id)
        => ShotKit.Get<System.Collections.IEnumerable>(m, "_commands").Cast<object>()
               .Where(c => ShotKit.Get<ShortcutCommand>(c, "Info").Id.TrimEnd('…') == id.TrimEnd('…'))
               .Select(c => ShotKit.Get<MenuItem?>(c, "Item")).FirstOrDefault()
           ?? throw new InvalidOperationException("no menu item " + id);

    /// <summary>위 메뉴 하나를 열어 <paramref name="item"/> 항목에 번호를 달아 찍는다(메뉴가 창 왼쪽 끝이면 왼쪽에 번호 자리).</summary>
    private static void MenuShot(ShotKit k, MainForm m, int menu, string item, string name, int number)
    {
        OpenTopMenu(k, m, menu);
        var open = ToolFileShotsMenus.OpenMenuRect(m) ?? new Rect(0, 0, 400, 400);
        var crop = new Rect(open.X - 24, 0, open.Width + 40, open.Bottom + 12);
        SaveOut(k, m, name, crop, [Lead(CommandItem(m, item), m, 8)], number);
        CloseMenus(k);
    }

    /// <summary>환경 설정 창을 열어(편집 > 환경 설정…) <paramref name="withWindow"/>를 부른다.</summary>
    private static void Preferences(ShotKit k, Action<Window> withWindow)
        => k.Dialog(() => k.Command("편집/환경 설정"), withWindow);

    /// <summary>환경 설정 탭 고르기.</summary>
    private static void PrefsTab(Window w, int index)
    {
        ShotKit.Find<TabStrip>(w).SelectedIndex = index;
        ShotKit.Pump(400);
    }

    /// <summary>환경 설정 탭 머리(글자 둘레).</summary>
    private static Rect TabHeader(Window w, string text) => ShotKit.Bounds(ShotKit.FindText(w, text), w).Inflate(new Thickness(10, 4));

    /// <summary>환경 설정 일반 탭에서 굵은 제목 <paramref name="header"/>부터 다음 제목 <paramref name="next"/> 앞까지(창 폭 전체).</summary>
    private static Rect PrefsSection(Window w, string header, string next)
    {
        var top = ShotKit.Bounds(ShotKit.FindText(w, header), w);
        var bottom = ShotKit.Bounds(ShotKit.FindText(w, next), w);
        return new Rect(-22, top.Y - 8, w.Bounds.Width + 22, bottom.Y - top.Y + 4);
    }

    /// <summary>대화상자 단추(글자로 찾음).</summary>
    private static Control Button(Visual root, string text) => ShotKit.FindText(root, text, ancestorControl: true);

    /// <summary>작은 움직이는 GIF(통통 튀는 초록 슬라임 16×16, <paramref name="frames"/>장, 프레임마다 <paramref name="ms"/>ms).</summary>
    private static string SlimeGif(string path, int frames, int ms)
    {
        var body = new Color32(88, 196, 92);
        var dark = new Color32(36, 110, 52);
        var eye = new Color32(20, 24, 28);
        int[] lift = [0, 2, 4, 2];
        var list = new List<GifFrame>(frames);
        for (int i = 0; i < frames; i++)
        {
            var img = new PixelImage(16, 16);
            int y = 6 - lift[i % lift.Length];
            img.FillRect(new System.Drawing.Rectangle(3, y + 1, 10, 8), body);
            img.FillRect(new System.Drawing.Rectangle(4, y, 8, 1), body);
            img.FillRect(new System.Drawing.Rectangle(3, y + 8, 10, 1), dark);
            img.Set(6, y + 3, eye);
            img.Set(9, y + 3, eye);
            list.Add(new GifFrame(img, ms));
        }
        GifWriter.Save(list, path, new GifOptions());
        return path;
    }

    // ───────────── 저장 ─────────────

    [Shot("files-save")]
    public static void Save(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // ① 파일 메뉴의 저장(+ 시트 내보내기).
        MenuShot(k, m, 0, "파일/저장(+ 시트 내보내기)", "files-save", 1);

        // ② 저장한 뒤 상태 표시줄: 저장한 파일과 함께 내보낸 결과.
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(500);
        k.Save(m, name: "files-save-2", crop: BottomStrip(m), marks: [MessageRect(m)], firstNumber: 2);

        // ③ .chickxel은 저장됐지만 이미지 내보내기가 실패: 내보낼 이름 자리에 폴더가 있어 쓰지 못하게 한다.
        var ctx = ToolFileHelp.Ctx(m);
        ctx.Project!.Export.FileName = "wolf_sheet";
        Directory.CreateDirectory(Path.Combine(k.WorkDir, "wolf_sheet.png"));
        Scribble(k, m);
        k.Dialog(() => k.Command("파일/저장(+ 시트 내보내기)"), w =>
            SaveWindow(k, w, "files-save-3", [Lead(ShotKit.All<TextBlock>(w).First(t => t.Text is { Length: > 20 }), w, 10)], 3));
    }

    [Shot("files-autosave")]
    public static void AutoSave(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // ① 한 번 저장한 파일을 고친 뒤 자동 저장이 돈 상태 표시줄.
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(300);
        Scribble(k, m);
        ShotKit.Call(m, "AutoSaveTick");
        k.Settle(500);
        k.Save(m, crop: BottomStrip(m), marks: [MessageRect(m)]);

        // ②③ 환경 설정 > 일반 > 저장: 자동 저장 켜기, 기다리는 시간.
        Preferences(k, w =>
        {
            var crop = PrefsSection(w, UiStrings.S_PreferencesDialog_PreferencesDialog_06B0DD01B8, UiStrings.S_PreferencesDialog_PreferencesDialog_B57D05987F);
            var autoSave = ShotKit.Get<Control>(w, "_autoSave");
            var delay = ShotKit.Get<Control>(w, "_autoSaveDelay");
            SaveOut(k, w, "files-autosave-2", crop, [Lead(autoSave, w), Lead(delay, w, 6)], 2);
        });
    }

    [Shot("files-revert")]
    public static void Revert(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(300);
        Scribble(k, m);
        // ① 고친 뒤: 도구 막대 맨 아래 되돌리기 단추가 켜진다.
        var strip = BottomStrip(m);
        SaveOut(k, m, "files-revert", new Rect(strip.X - 22, strip.Y, strip.Width + 22, strip.Height), [ShotKit.Get<Control>(m, "_tbRevert")], 1);
        // ② 파일 메뉴의 저장한 내용으로 되돌리기.
        MenuShot(k, m, 0, "파일/저장한 내용으로 되돌리기", "files-revert-2", 2);
        // ③ 누르면 묻는 창.
        k.Dialog(() => k.Command("파일/저장한 내용으로 되돌리기"), w =>
            k.Save(w, name: "files-revert-3", marks: [Button(w, UiStrings.S_Ui_MessageDialog_76C4BD0F9B)], firstNumber: 3));
    }

    [Shot("files-history")]
    public static void History(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        // ① 기록 남기기 ② 보관 기간 ③ 최대 용량 ④ 기록 모두 지우기.
        Preferences(k, w =>
        {
            var crop = PrefsSection(w, UiStrings.S_PreferencesDialog_PreferencesDialog_02113E25F5, UiStrings.S_PreferencesDialog_PreferencesDialog_06B0DD01B8);
            var on = ShotKit.Get<Control>(w, "_historyOn");
            var days = ShotKit.Get<Control>(w, "_historyDays");
            var mb = ShotKit.Get<Control>(w, "_historyMb");
            var clear = Button(w, UiStrings.S_PreferencesDialog_PreferencesDialog_A127858673);
            SaveOut(k, w, "files-history", crop, [Lead(on, w), RowUpTo(days, w), RowUpTo(mb, w), clear], 1);
        });
    }

    // ───────────── 열기·가져오기 ─────────────

    [Shot("files-png-import")]
    public static void PngImport(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // ①② 칸 크기가 모두 같은 시트: 부위로 / 애니메이션 프레임으로.
        k.Dialog(() => new ImportKindDialog("hero_walk.png", 8, new System.Drawing.Size(32, 32)).ShowDialog(m), w =>
        {
            var buttons = ShotKit.All<Button>(w).Where(b => b.IsEffectivelyVisible).OrderBy(b => ShotKit.Bounds(b, w).X).ToList();
            k.Save(w, marks: [buttons[0], buttons[1]]);
        });
        // ③ 프레임·레이어 메뉴의 격자로 나눠 프레임 만들기.
        MenuShot(k, m, 3, "프레임·레이어/격자로 나눠 프레임 만들기(시트 → 프레임)", "files-png-import-2", 3);
        // ④ 나누는 기준(칸 크기 / 칸 수 / 자동) 목록을 편 창, 이어서 ⑤ 칸 크기 ⑥ 칸 수 ⑦ 자동(덩어리) 최소 크기.
        k.Dialog(() => k.Command("프레임·레이어/격자로 나눠 프레임 만들기(시트 → 프레임)"), w =>
        {
            var method = ShotKit.Get<ComboBox>(w, "_method");
            method.IsDropDownOpen = true;
            ShotKit.Pump(400);
            var list = ToolFileShotsMenus.OpenMenuRect(w) ?? throw new InvalidOperationException("split method list did not open");
            SaveWindow(k, w, "files-png-import-3", [list], 4);
            method.IsDropDownOpen = false;
            ShotKit.Pump(200);
            var cellH = ShotKit.Get<Control>(w, "_cellH");
            var rows = ShotKit.Get<Control>(w, "_rows");
            var min = ShotKit.Get<Control>(w, "_minSize");
            SaveWindow(k, w, "files-png-import-4", [RowUpTo(cellH, w, 6), RowUpTo(rows, w, 6), RowUpTo(min, w, 6)], 5);
        });
    }

    [Shot("files-open-media")]
    public static void OpenMedia(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), timeline: 200);
        ToolFileHelp.HideFiles(k, m);
        // ① 파일 메뉴의 열기(.chickxel · 이미지 · GIF · 영상).
        MenuShot(k, m, 0, "파일/열기(.chickxel / PNG 가져오기)", "files-open-media", 1);

        // ②③④ 움직이는 GIF를 연 새 문서: 새 탭, 파일 이름 레이어 하나, 그림 프레임마다 프레임(길이 그대로).
        var gif = SlimeGif(Path.Combine(k.WorkDir, "slime_bounce.gif"), 4, 120);
        ShotKit.Call(m, "OpenFile", gif);
        k.Settle(600);
        ToolFileHelp.Canvas(m).SetZoom(12);
        k.Settle(300);
        var timeline = ShotKit.Get<Control>(m, "_timeline");
        var tab = ShotKit.FindText(m, "slime_bounce.gif");
        var grid = ShotKit.Get<Control>(timeline, "_grid");
        int x0 = (int)ShotKit.Call(grid, "FrameX", 0)!, x3 = (int)ShotKit.Call(grid, "FrameX", 3)!, w3 = (int)ShotKit.Call(grid, "FrameW", 3)!;
        var head0 = grid.TranslatePoint(new Point(x0, 20), m)!.Value;
        var head3 = grid.TranslatePoint(new Point(x3 + w3, 50), m)!.Value;
        var left = ShotKit.Bounds(timeline, m).X - 2;
        var crop = new Rect(left, ShotKit.Bounds(tab, m).Y - 8, ShotKit.Bounds(ToolFileHelp.Canvas(m), m).Right - left, 0);
        crop = crop.WithHeight(ShotKit.Bounds(timeline, m).Bottom - crop.Y);
        k.Save(m, name: "files-open-media-2", crop: crop,
            marks: [ShotKit.Bounds(tab, m).Inflate(new Thickness(12, 6, 30, 6)), LayerRow(m, 0), new Rect(head0, head3)], firstNumber: 2);

        // ⑤ 프레임이 300개를 넘는 그림: 모두 / 앞 300개만 / 취소.
        var many = SlimeGif(Path.Combine(k.WorkDir, "long_loop.gif"), 320, 40);
        k.Dialog(() => ShotKit.Call(m, "OpenFile", many), w =>
        {
            var buttons = ShotKit.All<Button>(w).Where(b => b.IsEffectivelyVisible).ToList();
            k.Save(w, name: "files-open-media-3", marks: [Union(w, [.. buttons]).Inflate(4)], firstNumber: 5);
        });

        // ⑥⑦⑧ 환경 설정 > 영상(ffmpeg): 탭, ffmpeg 위치, 찾아보기.
        Preferences(k, w =>
        {
            PrefsTab(w, 5);
            var box = ShotKit.Get<Control>(w, "_ffmpegBox");
            // 찾은 ffmpeg 위치는 이 컴퓨터의 경로라 Windows 예시 경로로 바꿔 찍는다.
            if (Chickxel.App.FfmpegTool.Locate((string?)null) is { } found)
                foreach (var t in ShotKit.All<TextBlock>(w).Where(t => t.Text?.Contains(found, StringComparison.Ordinal) == true))
                    t.Text = t.Text!.Replace(found, @"C:\ffmpeg\bin\ffmpeg.exe", StringComparison.Ordinal);
            ShotKit.Pump(100);
            SaveWindow(k, w, "files-open-media-4", [TabHeader(w, UiStrings.Ffmpeg_Prefs_Tab), Lead(box, w, 14), Button(w, UiStrings.Ffmpeg_Prefs_Browse)], 6);
        });

        // ⑨~⑮ 영상을 열면 먼저 뜨는 영상 가져오기 창(8.4초, 1280×720, 30fps 예시).
        var info = new VideoInfo(8.4, [new VideoStreamInfo(0, 1280, 720, 30, "h264", false)]);
        k.Dialog(() => new VideoImportDialog("walk_cycle.mp4", info).ShowDialog(m), w =>
        {
            var start = ShotKit.Get<Control>(w, "_start");
            var fps = ShotKit.Get<Control>(w, "_fps");
            var max = ShotKit.Get<Control>(w, "_maxFrames");
            var scale = ShotKit.Get<Control>(w, "_scale");
            var side = ShotKit.Get<Control>(w, "_longSide");
            var smooth = ShotKit.Get<Control>(w, "_smooth");
            var estimate = ShotKit.Get<Control>(w, "_estimate");
            var range = (Control)start.GetVisualParent()!;
            Rect Labeled(string label, params Control[] fields) => Union(w, [ShotKit.FindText(w, label), .. fields]).Inflate(new Thickness(14, 0, 0, 0));
            SaveWindow(k, w, "files-open-media-5",
            [
                Labeled(UiStrings.Video_Range, range), Labeled(UiStrings.Video_Fps, fps), Labeled(UiStrings.Video_MaxFrames, max),
                Labeled(UiStrings.Video_Scale, scale, side), Lead(smooth, w, 6), Lead(estimate, w, 14), ShotKit.Get<Control>(w, "OkButton"),
            ], 9);
        });
    }

    [Shot("files-drop-layer")]
    public static void DropLayer(ShotKit k)
    {
        var heart = ToolFileHelp.HeartPng(Path.Combine(k.WorkDir, "heart.png"));
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), timeline: 250);
        ShotKit.Call(m, "SetPixelGrid", false);
        ToolFileHelp.HideFiles(k, m);
        var canvas = ToolFileHelp.Canvas(m);
        // ① 끄는 중: 캔버스 둘레가 빛나고 들어갈 자리가 점선 사각형.
        var at = new System.Drawing.Point(38, 22);
        var size = new System.Drawing.Size(11, 10);
        var ghost = Chickxel.Core.Edit.ImageDrop.Place(new System.Drawing.Size(49, 98), size, at);
        canvas.SetDropHint([new System.Drawing.Rectangle(ghost.Location, size)]);
        k.Settle(300);
        var hint = new Rect(ToolFileHelp.At(m, ghost.Location.X, ghost.Location.Y), ToolFileHelp.At(m, ghost.Location.X + size.Width, ghost.Location.Y + size.Height));
        k.Save(m, crop: ToolFileHelp.CanvasRect(m), marks: [hint.Inflate(10)]);
        canvas.SetDropHint(null);

        // ②③④ 놓은 뒤: 파일 이름의 새 레이어 + 그림 한 장이면 바로 변형 모드 + 안내.
        ShotKit.Call(m, "AddImageLayers", (IReadOnlyList<string>)[heart], at);
        k.Settle(800);
        // 새 줄이 펼쳐지는 애니메이션이 화면 없이 도는 동안 덜 끝나 보일 수 있어 끝난 모습으로 맞춘다.
        var timelineGrid = ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");
        ShotKit.Call(timelineGrid, "SyncLayout", false);
        ShotKit.Get<Avalonia.Controls.Primitives.ScrollBar>(timelineGrid, "_v").Value = 0;
        ShotKit.Call(timelineGrid, "InvalidateVisual");
        k.Settle(300);
        var layer = ToolFileHelp.Ctx(m).Project!.Layers.ToList().FindIndex(l => l.Name == "heart");
        var placed = new Rect(ToolFileHelp.At(m, ghost.Location.X, ghost.Location.Y), ToolFileHelp.At(m, ghost.Location.X + size.Width, ghost.Location.Y + size.Height)).Inflate(8);
        // 새 레이어(타임라인)·변형 손잡이(캔버스)·안내(상태 표시줄)가 창 곳곳에 있어 창 전체를 찍는다.
        k.Save(m, name: "files-drop-layer-2", marks: [LayerRow(m, layer), placed, MessageRect(m)], firstNumber: 2);
    }

    // ───────────── 정리·칸 이름 ─────────────

    [Shot("files-cleanup")]
    public static void Cleanup(ShotKit k)
    {
        var png = ToolFileHelp.Sample(k);
        var m = k.Main(png, folder: k.WorkDir);
        // 한 번 저장(내보내기 기록: wolf adventurer.png) → 내보낼 이름을 바꿔 다시 저장하면 옛 결과가 앱 휴지통으로.
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(500);
        var ctx = ToolFileHelp.Ctx(m);
        ctx.Project!.Export.FileName = "wolf_sheet";
        ctx.Project.IsDirty = true;
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(800);
        // ① 상태 표시줄: 옛 결과를 앱 휴지통으로 옮겼다는 알림.
        k.Save(m, crop: BottomStrip(m), marks: [MessageRect(m)]);

        // ②③ 파일 목록 우클릭: 되살리기(Ctrl+Z) / 앱 휴지통 보기.
        var browser = ShotKit.Get<FileBrowserPanel>(m, "_browser");
        var list = ShotKit.Get<ListBox>(browser, "_list");
        var lb = ShotKit.Bounds(list, m);
        k.Click(m, new Point(lb.X + 60, lb.Bottom - 30), MouseButton.Right, settleMs: 500);
        var restore = ShotKit.All<MenuItem>(m).FirstOrDefault(i => i.IsEffectivelyVisible && i.Header is string s && s == UiStrings.S_FileBrowserPanel_ListMenu_FD792AE5B7);
        var view = ShotKit.All<MenuItem>(m).FirstOrDefault(i => i.IsEffectivelyVisible && i.Header is string s && s == UiStrings.S_FileBrowserPanel_ListMenu_5FC61831E6);
        var menu = ToolFileShotsMenus.OpenMenuRect(m) ?? lb;
        var crop = ToolFileHelp.Clamp(new Rect(lb.X - 22, lb.Y - 8, 0, 0).Union(menu.Inflate(12)), m);
        k.Save(m, name: "files-cleanup-2", crop: crop,
            marks: restore is null || view is null ? null : [Lead(restore, m, 8), Lead(view, m, 8)], firstNumber: 2);
        CloseMenus(k);
        // 창을 닫아도 파일 브라우저의 감시는 남아, 같은 예시 폴더를 쓰는 다음 샷(저장)에서 멈춘다 — 여기서 멈춰 둔다.
        ShotKit.Get<FileSystemWatcher?>(browser, "_watcher")?.Dispose();
        ShotKit.Set(browser, "_watcher", null);
        if (restore is null || view is null) throw new InvalidOperationException("trash menu items not found");

        // ④⑤ 앱 휴지통 창: 옮겨진 옛 결과 항목, 되살리기.
        k.Dialog(() => ShotKit.Call(browser, "ShowTrash"), w =>
        {
            var item = ShotKit.All<ListBoxItem>(w).First();
            SaveWindow(k, w, "files-cleanup-3", [item, Button(w, UiStrings.S_TrashDialog_TrashDialog_2C94038691)], 4);
        });
    }

    [Shot("files-cell-names")]
    public static void CellNames(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        var timeline = ShotKit.Get<Control>(m, "_timeline");
        var grid = ShotKit.Get<Control>(timeline, "_grid");
        int x = (int)ShotKit.Call(grid, "FrameX", 0)!, w = (int)ShotKit.Call(grid, "FrameW", 0)!;
        var head = grid.TranslatePoint(new Point(x + w / 2.0, 20 + 15), m)!.Value;
        k.Click(m, head, MouseButton.Right, settleMs: 500);
        var item = ShotKit.All<MenuItem>(m).FirstOrDefault(i => i.IsEffectivelyVisible
            && i.Header is string s && s.Replace("_", "") == UiStrings.S_TimelinePanel_AddFrameTail_3DE73C6921.Replace("_", ""))
            ?? throw new InvalidOperationException("cell name menu item not found");
        var menu = ToolFileShotsMenus.OpenMenuRect(m) ?? new Rect(head, new Size(300, 300));
        var tl = ShotKit.Bounds(timeline, m);
        var crop = ToolFileHelp.Clamp(tl.Union(menu.Inflate(12)), m);
        // ① 프레임 우클릭 메뉴의 내보낼 칸 이름….
        k.Save(m, crop: crop, marks: [Lead(item, m, 8)]);
        // ② 이름 입력 창.
        k.Dialog(() => k.Click(item), dlg =>
        {
            Control box = ShotKit.All<AutoCompleteBox>(dlg).FirstOrDefault() is { } auto ? auto : ShotKit.All<TextBox>(dlg).First();
            if (box is AutoCompleteBox a) a.Text = "wolf_idle_0";
            else ((TextBox)box).Text = "wolf_idle_0";
            ShotKit.Pump(200);
            SaveWindow(k, dlg, "files-cell-names-2", [Lead(box, dlg, 8)], 2);
        });
    }

    // ───────────── 끝내기·설정·오류 ─────────────

    [Shot("files-quit")]
    public static void Quit(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        Scribble(k, m);
        var button = ToolFileHelp.ButtonPng(Path.Combine(k.WorkDir, "ui_button.png"));
        ShotKit.Call(m, "OpenFile", button);
        k.Settle(400);
        Scribble(k, m);
        // ① 저장하지 않은 파일 목록 ② 선택한 파일 저장 ③ 저장 안 함 ④ 취소.
        k.Dialog(() => m.Close(), w =>
        {
            var checks = ShotKit.All<CheckBox>(w).Where(c => c.IsEffectivelyVisible).ToList();
            var buttons = ShotKit.All<Button>(w).Where(b => b.IsEffectivelyVisible && b is not CheckBox)
                .OrderBy(b => ShotKit.Bounds(b, w).X).ToList();
            var listRect = Union(w, [.. checks]);
            var rows = new Rect(listRect.X - 8, listRect.Y - 6, ShotKit.Bounds(buttons[^1], w).Right - listRect.X + 8, listRect.Height + 12);
            SaveWindow(k, w, "files-quit", [rows, buttons[0], buttons[1], buttons[2]], 1);
        });
    }

    [Shot("files-settings")]
    public static void Settings(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // ① 편집 메뉴의 환경 설정….
        MenuShot(k, m, 1, "편집/환경 설정", "files-settings", 1);
        Preferences(k, w =>
        {
            // ②~⑥ 일반 탭: 실행취소 횟수, 실행취소 기록, 자동 저장, 앱 휴지통 보관.
            var undo = ShotKit.Get<Control>(w, "_undo");
            var historyOn = ShotKit.Get<Control>(w, "_historyOn");
            var clear = Button(w, UiStrings.S_PreferencesDialog_PreferencesDialog_A127858673);
            var autoSave = ShotKit.Get<Control>(w, "_autoSave");
            var delay = ShotKit.Get<Control>(w, "_autoSaveDelay");
            var trash = ShotKit.Get<Control>(w, "_trash");
            var history = Union(w, historyOn, ShotKit.Get<Control>(w, "_historyMb"), clear);
            SaveWindow(k, w, "files-settings-2",
            [
                TabHeader(w, UiStrings.S_PreferencesDialog_PreferencesDialog_2626F2C3C7),
                RowUpTo(undo, w),
                new Rect(history.X - 10, history.Y, history.Width + 10, history.Height),
                Union(w, autoSave, delay).Inflate(new Thickness(10, 0, 0, 0)),
                Lead(trash, w),
            ], 2);

            // ⑦~⑩ 단축키 탭: 바꾸기·추가·없음·기본값, 검색("save"), JSON 내보내기·불러오기(글 순서).
            PrefsTab(w, 1);
            var search = ShotKit.Get<TextBox>(w, "_search");
            search.Text = "save";
            ShotKit.Pump(400);
            var rows = ShotKit.Get<StackPanel>(w, "_rows");
            var firstRow = (Panel)rows.Children[0];
            var rowButtons = firstRow.Children[^1];
            var import = Button(w, UiStrings.S_PreferencesDialog_PreferencesDialog_1B59D72F85);
            var export = Button(w, UiStrings.S_PreferencesDialog_PreferencesDialog_CC8999072A);
            SaveWindow(k, w, "files-settings-3",
            [
                TabHeader(w, UiStrings.S_PreferencesDialog_PreferencesDialog_DA9395C360),
                rowButtons,
                Lead(search, w, 14),
                Union(w, export, import),
            ], 7);

            // ⑪ 다른 명령이 쓰는 키(Ctrl+S)를 저장한 내용으로 되돌리기에 주면 묻는 창.
            var commands = ShotKit.Get<IReadOnlyList<ShortcutCommand>>(w, "_commands");
            var revert = commands.First(c => c.Id == "파일/저장한 내용으로 되돌리기");
            var ctrlS = commands.First(c => c.Id == "파일/저장(+ 시트 내보내기)").Defaults.ToList();
            k.Dialog(() => ShotKit.Call(w, "SetKeys", revert.Id, ctrlS), ask =>
                k.Save(ask, name: "files-settings-4", marks: [Button(ask, UiStrings.S_Ui_MessageDialog_76C4BD0F9B)], firstNumber: 11));
        });
    }

    [Shot("files-crash-log")]
    public static void CrashLog(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        var log = Path.Combine(CrashGuard.LogDirectory, "crash-20261027-141502.log");
        var error = new InvalidOperationException("Sequence contains no elements");
        var show = typeof(CrashGuard).GetMethod("ShowDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        // ① 작업은 계속할 수 있다는 안내와 로그 파일 이름 ② 로그 폴더 열기 ③ Discord에 제보.
        k.Dialog(() => show.Invoke(null, [error, log]), w =>
        {
            var message = ShotKit.All<TextBlock>(w).First(t => t.IsEffectivelyVisible && t.Text?.Contains(Path.GetFileName(log), StringComparison.Ordinal) == true);
            var buttons = ShotKit.All<Button>(w).Where(b => b.IsEffectivelyVisible).ToList();
            var discord = buttons.First(b => ShotKit.All<TextBlock>(b).Any(t => t.Text?.Contains("Discord", StringComparison.Ordinal) == true));
            var folder = buttons.Where(b => b != discord).OrderBy(b => ShotKit.Bounds(b, w).X).First();
            SaveWindow(k, w, "files-crash-log", [Lead(message, w, 8), folder, discord], 1);
        });
        // ④ 도움말 메뉴의 오류 로그 폴더 열기.
        MenuShot(k, m, 6, "도움말/오류 로그 폴더 열기", "files-crash-log-2", 4);
    }
}
