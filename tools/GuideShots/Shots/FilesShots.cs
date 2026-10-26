using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Localization;
using Gpixel.Core.IO;

namespace GuideShots;

/// <summary>파일·저장·내보내기 장(files-*) 그림.</summary>
internal static class FilesShots
{
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

    /// <summary>상태 표시줄 메시지 글자 영역.</summary>
    private static Rect MessageRect(MainForm m)
    {
        var message = ShotKit.Get<TextBlock>(m, "_stMessage");
        var r = ShotKit.Bounds(message, m);
        // 강조 테두리는 3px 바깥에 그려지므로 창 아래 끝에서 잘리지 않게 위아래를 줄인다.
        return new Rect(r.X, r.Y + 3, Math.Min(r.Width, message.DesiredSize.Width), r.Height - 6);
    }

    /// <summary>위 메뉴 하나를 연다(0 = 파일, 1 = 편집 …).</summary>
    private static void OpenTopMenu(ShotKit k, MainForm m, int index)
    {
        var top = ShotKit.Find<Menu>(m).Items.OfType<MenuItem>().ElementAt(index);
        k.Click(top, settleMs: 400);
    }

    /// <summary>명령 id(단축키 표의 "분류/이름")의 메뉴 항목.</summary>
    private static MenuItem? CommandItem(MainForm m, string id)
        => ShotKit.Get<System.Collections.IEnumerable>(m, "_commands").Cast<object>()
            .Where(c => ShotKit.Get<ShortcutCommand>(c, "Info").Id == id)
            .Select(c => ShotKit.Get<MenuItem?>(c, "Item")).FirstOrDefault();

    /// <summary>창의 메뉴 + 열린 아래 메뉴를 담는 영역.</summary>
    private static Rect MenuCrop(MainForm m)
    {
        var menu = ToolFileShotsMenus.OpenMenuRect(m) ?? new Rect(0, 0, 400, 400);
        return new Rect(0, 0, menu.Right + 16, menu.Bottom + 12);
    }

    // ───────────── 저장 ─────────────

    [Shot("files-save")]
    public static void Save(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // 1) 파일 메뉴의 저장(+ 시트 내보내기).
        OpenTopMenu(k, m, 0);
        var save = CommandItem(m, "파일/저장(+ 시트 내보내기)");
        k.Save(m, crop: MenuCrop(m), marks: save is null ? null : [save]);
        k.Key(m, Key.Escape);
        Gpixel.App.PopupMenus.CloseAll();
        k.Settle(200);

        // 2) 저장한 뒤 상태 표시줄: 저장한 파일과 함께 내보낸 결과.
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(500);
        k.Save(m, name: "files-save-2", crop: BottomStrip(m), markRects: [MessageRect(m)]);
    }

    [Shot("files-autosave")]
    public static void AutoSave(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // 1) 환경 설정 > 일반 > 저장(자동 저장 켜기·기다리는 시간).
        k.Dialog(() => k.Command("편집/환경 설정"), w =>
        {
            var crop = PrefsSection(w, UiStrings.S_PreferencesDialog_PreferencesDialog_06B0DD01B8, UiStrings.S_PreferencesDialog_PreferencesDialog_B57D05987F);
            var autoSave = ShotKit.Get<Control>(w, "_autoSave");
            var delay = ShotKit.Get<Control>(w, "_autoSaveDelay");
            k.Save(w, crop: crop, markRects: [ShotKit.Bounds(autoSave, w).Union(ShotKit.Bounds(delay, w)).Inflate(3)]);
        });

        // 2) 한 번 저장한 파일을 고친 뒤 자동 저장이 돈 상태 표시줄.
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(300);
        Scribble(k, m);
        ShotKit.Call(m, "AutoSaveTick");
        k.Settle(500);
        k.Save(m, name: "files-autosave-2", crop: BottomStrip(m), markRects: [MessageRect(m)]);
    }

    [Shot("files-revert")]
    public static void Revert(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        k.Command("파일/저장(+ 시트 내보내기)");
        k.Settle(300);
        Scribble(k, m);
        // 1) 고친 뒤: 도구 막대 아래 되돌리기 단추가 켜진다.
        var revert = ShotKit.Get<Control>(m, "_tbRevert");
        k.Save(m, crop: BottomStrip(m), marks: [revert]);
        // 2) 누르면 묻는 창.
        k.Dialog(() => k.Command("파일/저장한 내용으로 되돌리기"), w => k.Save(w, name: "files-revert-2"));
    }

    [Shot("files-history")]
    public static void History(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        k.Dialog(() => k.Command("편집/환경 설정"), w =>
        {
            var crop = PrefsSection(w, UiStrings.S_PreferencesDialog_PreferencesDialog_02113E25F5, UiStrings.S_PreferencesDialog_PreferencesDialog_06B0DD01B8);
            var on = ShotKit.Get<Control>(w, "_historyOn");
            var days = ShotKit.Get<Control>(w, "_historyDays");
            var mb = ShotKit.Get<Control>(w, "_historyMb");
            k.Save(w, crop: crop, markRects: [ShotKit.Bounds(on, w).Union(ShotKit.Bounds(days, w)).Union(ShotKit.Bounds(mb, w)).Inflate(3)]);
        });
    }

    /// <summary>환경 설정 일반 탭에서 굵은 제목 <paramref name="header"/>부터 다음 제목 <paramref name="next"/> 앞까지(창 폭 전체).</summary>
    private static Rect PrefsSection(Window w, string header, string next)
    {
        var top = ShotKit.Bounds(ShotKit.FindText(w, header), w);
        var bottom = ShotKit.Bounds(ShotKit.FindText(w, next), w);
        return new Rect(0, top.Y - 6, w.Bounds.Width, bottom.Y - top.Y + 2);
    }

    // ───────────── 열기·가져오기 ─────────────

    [Shot("files-png-import")]
    public static void PngImport(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        // 1) 칸 크기가 모두 같은 시트: 부위로 / 애니메이션 프레임으로.
        k.Dialog(() => new ImportKindDialog("hero_walk.png", 8, new System.Drawing.Size(32, 32)).ShowDialog(m), w => k.Save(w));
        // 2) 칸 정보가 없는 시트: 격자로 나눠 프레임 만들기.
        k.Dialog(() => k.Command("프레임·레이어/격자로 나눠 프레임 만들기(시트 → 프레임)"), w => k.Save(w, name: "files-png-import-2"));
    }

    [Shot("files-open-media")]
    public static void OpenMedia(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        // 1) 파일 메뉴의 열기(.gpixel · 이미지 · GIF · 영상).
        OpenTopMenu(k, m, 0);
        var open = CommandItem(m, "파일/열기(.gpixel / PNG 가져오기)…");
        k.Save(m, crop: MenuCrop(m), marks: open is null ? null : [open]);
        Gpixel.App.PopupMenus.CloseAll();
        k.Settle(200);
        // 2) 영상을 열면 먼저 뜨는 영상 가져오기 창(8.4초, 1280×720, 30fps 예시).
        var info = new VideoInfo(8.4, [new VideoStreamInfo(0, 1280, 720, 30, "h264", false)]);
        k.Dialog(() => new VideoImportDialog("walk_cycle.mp4", info).ShowDialog(m), w => k.Save(w, name: "files-open-media-2"));
    }

    [Shot("files-drop-layer")]
    public static void DropLayer(ShotKit k)
    {
        var heart = ToolFileHelp.HeartPng(Path.Combine(k.WorkDir, "heart.png"));
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), timeline: 200);
        ShotKit.Call(m, "SetPixelGrid", false);
        ToolFileHelp.HideFiles(k, m);
        var canvas = ToolFileHelp.Canvas(m);
        // 1) 끄는 중: 캔버스 둘레가 빛나고 들어갈 자리가 점선 사각형.
        var at = new System.Drawing.Point(38, 22);
        var size = new System.Drawing.Size(11, 10);
        var ghost = Gpixel.Core.Edit.ImageDrop.Place(new System.Drawing.Size(49, 98), size, at);
        canvas.SetDropHint([new System.Drawing.Rectangle(ghost.Location, size)]);
        k.Settle(300);
        k.Save(m, crop: ToolFileHelp.CanvasRect(m));
        canvas.SetDropHint(null);

        // 2) 놓은 뒤: 파일 이름의 새 레이어 + 그림 한 장이면 바로 변형 모드.
        ShotKit.Call(m, "AddImageLayers", (IReadOnlyList<string>)[heart], at);
        k.Settle(500);
        // 새 레이어(타임라인)·변형 손잡이(캔버스)·안내(상태 표시줄)가 창 곳곳에 있어 창 전체를 찍는다.
        k.Save(m, name: "files-drop-layer-2", markRects: [MessageRect(m)]);
    }

    // ───────────── 정리·칸 이름 ─────────────

    [Shot("files-cleanup")]
    public static void Cleanup(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        var browser = ShotKit.Get<FileBrowserPanel>(m, "_browser");
        // 내보내기 이름을 바꿔 더 이상 만들지 않게 된 옛 결과(시트 + .meta)를 저장 때 앱 휴지통으로 옮긴 상태.
        var old = Path.Combine(k.WorkDir, "wolf_sheet_old.png");
        File.Copy(ShotKit.SamplePng, old, overwrite: true);
        File.WriteAllText(old + ".meta", "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");
        // 앱이 옛 결과를 옮길 때 쓰는 항목 이름(생성된 문구라 공개가 아니어서 리플렉션).
        var trashMany = typeof(MainStrings).GetMethod("FormatExport_TrashMany",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var label = (string)trashMany.Invoke(null, [Path.GetFileName(old), 1])!;
        browser.Trash.Add([old, old + ".meta"], label);
        k.Dialog(() => ShotKit.Call(browser, "ShowTrash"), w => k.Save(w));
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
            && i.Header is string s && s.Replace("_", "") == UiStrings.S_TimelinePanel_AddFrameTail_3DE73C6921.Replace("_", ""));
        var menu = ToolFileShotsMenus.OpenMenuRect(m) ?? new Rect(head, new Size(300, 300));
        var tl = ShotKit.Bounds(timeline, m);
        var crop = ToolFileHelp.Clamp(tl.Union(menu.Inflate(12)), m);
        k.Save(m, crop: crop, marks: item is null ? null : [item]);
        if (item is null) throw new InvalidOperationException("cell name menu item not found");
        // 이름 입력 창.
        k.Dialog(() => k.Click(item), dlg =>
        {
            if (ShotKit.All<AutoCompleteBox>(dlg).FirstOrDefault() is { } box) box.Text = "wolf_idle_0";
            else if (ShotKit.All<TextBox>(dlg).FirstOrDefault() is { } text) text.Text = "wolf_idle_0";
            ShotKit.Pump(200);
            k.Save(dlg, name: "files-cell-names-2");
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
        k.Dialog(() => m.Close(), w => k.Save(w));
    }

    [Shot("files-settings")]
    public static void Settings(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        k.Dialog(() => k.Command("편집/환경 설정"), w =>
        {
            k.Save(w);
            var tabs = ShotKit.Find<TabStrip>(w);
            tabs.SelectedIndex = 1;
            ShotKit.Pump(500);
            k.Save(w, name: "files-settings-2");
        });
    }

    [Shot("files-crash-log")]
    public static void CrashLog(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        var log = Path.Combine(CrashGuard.LogDirectory, "crash-20261027-141502.log");
        var error = new InvalidOperationException("Sequence contains no elements");
        var show = typeof(CrashGuard).GetMethod("ShowDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        k.Dialog(() => show.Invoke(null, [error, log]), w => k.Save(w));
    }
}
