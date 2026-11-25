using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;

namespace GuideShots;

/// <summary>
/// 튜토리얼 말풍선의 작은 움짤(그 단계에서 할 일만 가까이 잘라 찍은 것): 앱 내장 <c>src/Chickxel.App/Assets/Tutorial/clip-&lt;이름&gt;.webp</c>.
/// 단계 ↔ 움짤은 <c>MainForm.Tutorial.cs</c>의 <c>TutorialStep.Clip</c>과 저장소 루트 <c>튜토리얼.md</c>. 샷 id = <c>clip-tutorial-&lt;이름&gt;</c>.
/// </summary>
internal static class TutorialClipShots
{
    private const string Out = "src/Chickxel.App/Assets/Tutorial/clip-";

    private static void Save(ClipRecorder rec, string name) => rec.Save(Out + name, ClipFormat.WebP, webpLossless: false, webpQuality: 82);

    // ───────────── 효과 쓰기 ─────────────

    /// <summary>예시 늑대 6프레임 문서 + 맨 왼쪽 칸에 효과 목록(파일 칸 닫음).</summary>
    private static MainForm EffectsMain(ShotKit k)
    {
        // 타임라인 칸이 효과 목록과 가깝게: 팔레트·오른쪽 칸을 좁힌다.
        App.Settings.BottomHeight = 230;
        App.Settings.PaletteWidth = 170;
        App.Settings.RightWidth = 250;
        var m = ScreenDemo.Open(k, width: 1280, height: 640);
        ScreenDemo.Build(k, m, group: false);
        ShotKit.Call(m, "SetLeftPanes", false, true, false);
        k.Settle(500);
        return m;
    }

    private static Control Grid(MainForm m) => ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");

    private static int Body(MainForm m) => ScreenDemo.Project(m).IndexOf(ScreenDemo.Project(m).Layers.First(l => l.Name == "Body"));

    /// <summary>레이어 × 프레임 칸의 창 좌표.</summary>
    private static Rect Cel(MainForm m, int layer, int frame)
    {
        var g = Grid(m);
        var r = (Rect)ShotKit.Call(g, "CelRect", layer, frame)!;
        return new Rect(g.TranslatePoint(r.Position, m)!.Value, r.Size);
    }

    private static Control OutlineTile(MainForm m)
        => ShotKit.All<Control>(ShotKit.Get<Control>(m, "_effectList")).First(c => c.GetType().Name == "EffectTile" && c.IsEffectivelyVisible
            && ((string)c.GetType().GetProperty("Key")!.GetValue(c)!).Contains("outline", StringComparison.OrdinalIgnoreCase));

    /// <summary>창 안으로 자르고 짝수 크기로(영상 인코더).</summary>
    private static Rect Fit(Rect r, TopLevel top)
    {
        r = r.Intersect(new Rect(top.ClientSize));
        return new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Floor(r.Width / 2) * 2, Math.Floor(r.Height / 2) * 2);
    }

    /// <summary>
    /// 효과 넣기: 몸 레이어 3~5프레임 칸을 골라 둔 상태에서 외곽선 상자를 그 칸으로 끌어 놓으면 그 세 프레임에만 효과 줄이 생긴다.
    /// 상자와 칸은 멀리 떨어져 있어 두 곳만 잘라 붙여 찍는다(<see cref="ClipRecorder.Regions"/> — 사이를 지나는 동안은 짧게).
    /// </summary>
    [Shot("clip-tutorial-effects-add")]
    public static void EffectsAdd(ShotKit k)
    {
        var m = EffectsMain(k);
        int body = Body(m);
        ShotKit.Call(Grid(m), "SelectRect", body, 2, null, 4);
        k.Settle(300);
        var tile = ShotKit.Bounds(OutlineTile(m), m);
        var first = Cel(m, body, 2);
        var last = Cel(m, body, 4);
        var drop = Cel(m, body, 3);
        var tileArea = Fit(tile.Inflate(8), m);
        var celArea = Fit(new Rect(first.X - 24, first.Y - 40, last.Right - first.X + 48, first.Height + 40 + 30), m);
        using var rec = new ClipRecorder(k, m, tileArea.Union(celArea), frameMs: 40) { Regions = [tileArea, celArea] };
        rec.Place(new Point(tile.Center.X + 40, tile.Bottom + 2));
        rec.Hold(500);
        rec.Move(tile.Center, 500);
        rec.Hold(250);
        rec.Down();
        rec.Hold(150);
        rec.Move(new Point(tileArea.Right + 4, tile.Center.Y), 250);
        rec.Move(new Point(celArea.X - 4, drop.Center.Y), 300);
        rec.Move(drop.Center, 500);
        rec.Hold(250);
        rec.Up();
        rec.Hold(1600);
        var effects = ScreenDemo.Project(m).Layers[body].Effects;
        if (effects.Count == 0 || effects[0].Frames is not { From: 2, To: 4 }) throw new InvalidOperationException("effect not dropped on frames 3-5");
        Save(rec, "effects-add");
    }

    /// <summary>효과 설정: 넣은 외곽선의 첫 숫자 칸(두께) 위 단추를 두 번 누른다.</summary>
    [Shot("clip-tutorial-effects-adjust")]
    public static void EffectsAdjust(ShotKit k)
    {
        var m = EffectsMain(k);
        k.DoubleClick(OutlineTile(m));
        k.Settle(600);
        var section = ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_toolOptions"), "_effectSection");
        section.BringIntoView();
        k.Settle(500);
        var box = ShotKit.Bounds(section, m);
        var num = ShotKit.All<NumericUpDown>(section).First(n => n.IsEffectivelyVisible);
        var nb = ShotKit.Bounds(num, m);
        var crop = Fit(new Rect(box.X, box.Y, box.Width, Math.Min(box.Height, nb.Bottom - box.Y + 90)), m);
        var up = new Point(nb.Right - 9, nb.Y + nb.Height * 0.28);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(crop.Center.X, crop.Bottom - 20));
        rec.Hold(500);
        rec.Move(up, 700);
        rec.Hold(200);
        rec.Click(up, 0, pressMs: 120, afterMs: 700);
        rec.Click(up, 0, pressMs: 120, afterMs: 1400);
        Save(rec, "effects-adjust");
    }

    /// <summary>효과 줄: 1프레임에만 넣은 외곽선 상자의 오른쪽 끝을 4프레임까지 끌어 범위를 늘린다.</summary>
    [Shot("clip-tutorial-effects-timeline")]
    public static void EffectsTimeline(ShotKit k)
    {
        var m = EffectsMain(k);
        k.DoubleClick(OutlineTile(m));
        k.Settle(600);
        var g = Grid(m);
        int body = Body(m);
        int cellH = ShotKit.Get<int>(g, "CellH");
        const int FxH = 16;
        int layerY = (int)ShotKit.Call(g, "LayerY", body)!;
        int X(int f) => (int)ShotKit.Call(g, "FrameX", f)!;
        int W(int f) => (int)ShotKit.Call(g, "FrameW", f)!;
        Point At(double x, double y) => g.TranslatePoint(new Point(x, y), m)!.Value;
        double rowY = layerY + cellH + FxH / 2.0;
        var from = At(X(0) + W(0) - 2, rowY);
        var to = At(X(3) + W(3) - 3, rowY);
        var topLeft = At(X(0) - 200, layerY - 6);
        var bottomRight = At(X(5) + W(5) + 12, layerY + cellH + FxH + 12);
        var crop = Fit(new Rect(topLeft, bottomRight), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(from.X - 60, from.Y + 30));
        rec.Hold(500);
        rec.Move(from, 600);
        rec.Hold(300);
        rec.Down();
        rec.Hold(150);
        rec.Move(to, 1100);
        rec.Hold(150);
        rec.Up();
        rec.Hold(1600);
        var effect = ScreenDemo.Project(m).Layers[body].Effects[0];
        if (effect.Frames is not { To: 3 }) throw new InvalidOperationException("effect range not extended: " + effect.Frames);
        Save(rec, "effects-timeline");
    }

    // ───────────── 그림을 픽셀 아트로 ─────────────

    /// <summary>AI 그림 흉내를 넣은 픽셀로 정리 창(프리셋마다 짧은 예산). <paramref name="run"/>이면 체크한 프리셋을 한 번 돌려 결과까지.</summary>
    private static PixelateDialog PixelateOpen(ShotKit k, bool run)
    {
        foreach (var p in PixelateKit.Options().OptimizePresets)
        {
            p.Trials = 40;
            p.Seconds = 3;
        }
        var main = ScreenDemo.Open(k);
        ShotKit.Call(main, "PixelateFile", PixelateKit.MakeAiImage(k));
        k.Settle(400);
        var dlg = k.Windows.OfType<PixelateDialog>().Last();
        if (run) PixelateKit.WaitCandidates(k, dlg);
        else
            for (int i = 0; i < 600 && ShotKit.Get<bool>(dlg, "TutorialReady") is false; i++) k.Settle(200);
        k.Settle(500);
        return dlg;
    }

    private static MainForm Main(ShotKit k) => k.Windows.OfType<MainForm>().First();


    /// <summary>찍기 전에 대화상자 안 컨트롤의 메인 창 좌표(녹화기가 대화상자를 놓는 자리 기준 — 찍을 영역을 정하려고).</summary>
    private static Rect Probe(ShotKit k, MainForm m, Control c)
    {
        using var probe = new ClipRecorder(k, m, new Rect(0, 0, 2, 2));
        return probe.BoundsOf(c);
    }
    /// <summary>프리셋 하나만 돌리기: '같은 색상 계열' 칸 우클릭 → '이 프리셋만 돌리기' → 그 칸이 돈다.</summary>
    [Shot("clip-tutorial-pixelate-rerun")]
    public static void PixelateRerun(ShotKit k)
    {
        var dlg = PixelateOpen(k, run: false);
        var m = Main(k);
        var found = ShotKit.Call(dlg, "TutorialIdleTile", "shade") ?? throw new InvalidOperationException("no idle preset tile");
        var tile = (Control)found.GetType().GetField("Item1")!.GetValue(found)!;
        var t = Probe(k, m, tile);
        var crop = Fit(new Rect(t.X - 12, t.Y - 12, t.Width + 260, t.Height + 24), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(t.Right + 120, t.Center.Y + 40));
        rec.Hold(500);
        rec.Click(t.Center, 700, MouseButton.Right, pressMs: 120, afterMs: 700);
        var item = ShotKit.OpenPopups.Where(p => p.Child is not null).SelectMany(p => ShotKit.All<MenuItem>(p.Child!))
            .First(i => i.IsEffectivelyVisible && (i.Header as string ?? (i.Header as TextBlock)?.Text) == UiStrings.Pixelate_Opt_RunPreset);
        rec.Move(rec.At(item), 600);
        rec.Hold(300);
        rec.Click(rec.At(item), 0, pressMs: 120, afterMs: 2200);
        Save(rec, "pixelate-rerun");
    }

    /// <summary>최적화와 취소: 자동 최적화를 누르면 후보 칸들이 돌고 단추가 '취소'로 바뀐다 → 취소를 누르면 멈춘다.</summary>
    [Shot("clip-tutorial-pixelate-optimize")]
    public static void PixelateOptimize(ShotKit k)
    {
        var dlg = PixelateOpen(k, run: false);
        var m = Main(k);
        var button = ShotKit.Get<Control>(dlg, "_optRun");
        var b = Probe(k, m, button);
        var crop = Fit(new Rect(b.X - 12, b.Y - 12, 470, 230), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(b.Right + 140, b.Center.Y + 60));
        rec.Hold(500);
        rec.Click(b.Center, 700, pressMs: 120, afterMs: 1800);
        rec.Click(rec.At(button), 400, pressMs: 120, afterMs: 1500);
        Save(rec, "pixelate-optimize");
    }

    /// <summary>결과 색: 견본 하나를 우클릭하면 빠진다(끝으로 흐리게 옮겨지고 "색 빼기 · 모두 되살리기" 줄이 생김, 결과에서 그 색이 사라짐).</summary>
    [Shot("clip-tutorial-pixelate-colors")]
    public static void PixelateColors(ShotKit k)
    {
        var dlg = PixelateOpen(k, run: true);
        var m = Main(k);
        var colors = ShotKit.Get<Control>(dlg, "_colors");
        var swatches = ShotKit.Get<Panel>(colors, "_swatches");
        var swatch = swatches.Children.Where(c => c.IsVisible).ElementAt(Math.Min(4, swatches.Children.Count - 1));
        var block = Probe(k, m, colors);
        var crop = Fit(block.Inflate(new Thickness(10, 10, 10, 40)), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        var s = rec.BoundsOf(swatch).Center;
        rec.Place(new Point(s.X + 60, block.Bottom + 24));
        rec.Hold(500);
        rec.Click(s, 700, MouseButton.Right, pressMs: 120, afterMs: 1600);
        rec.Move(new Point(s.X + 40, block.Bottom + 20), 600);
        rec.Hold(1200);
        Save(rec, "pixelate-colors");
    }

    // ───────────── 3D를 픽셀 아트로 ─────────────

    /// <summary>
    /// 튜토리얼 창의 '3D를 픽셀 아트로' 카드 움짤(<c>Assets/Tutorial/card-model.webp</c>): 3D 모델 창에서 예시 병아리 모델을 끌어 돌리면
    /// 오른쪽 픽셀 결과가 따라 바뀐다. 원본 · 결과 두 칸을 16:9로 잘라 찍는다.
    /// </summary>
    [Shot("clip-tutorial-model-card")]
    public static void ModelCard(ShotKit k)
    {
        var m = ScreenDemo.Open(k, width: 1280, height: 900);
        k.Settle(400);
        var path = ModelSample.Chick(k.WorkDir);
        var scene = Chickxel.Core.Scene3D.ModelLoader.Load(path);
        k.Dialog(() => new ModelImportDialog(path, scene).ShowDialog(m), w =>
        {
            ShotKit.Pump(1500);
            var preview = ShotKit.Get<Control>(w, "_preview");
            // 미리보기 두 칸(664×330) + 아래 조금 → 16:9. 대화상자는 메인 창 가운데에 그려진다(ClipRecorder.DialogOrigin과 같은 셈).
            var size = w.ClientSize;
            var origin = new Point(Math.Round((m.ClientSize.Width - size.Width) / 2),
                Math.Round(Math.Max(0, (m.ClientSize.Height - size.Height - ShotKit.TitleHeight) / 2)) + ShotKit.TitleHeight);
            var pb = ShotKit.Bounds(preview, w).Translate((Vector)origin);
            var crop = new Rect(pb.X, pb.Y, Math.Floor(pb.Width / 2) * 2, Math.Round(pb.Width * 9 / 16 / 2) * 2);
            // 모델이 돌면 두 칸이 통째로 바뀌어 프레임마다 무거우므로 프레임을 성기게(100ms).
            using var rec = new ClipRecorder(k, m, crop, frameMs: 100);
            // 원본 칸 가운데에서 조금 왼쪽 아래(고리·축이 아닌 곳 — 누르면 모델 돌리기). 가로 1px = 0.5°.
            var left = new Point(pb.X + pb.Width / 4 - 40, pb.Center.Y + 60);
            rec.Place(new Point(left.X + 40, left.Y + 50));
            rec.Hold(600);
            rec.Move(left, 500);
            rec.Hold(200);
            // 가로로 80°쯤, 이어 위로 조금 — 오른쪽 결과가 따라 바뀐다.
            rec.Drag(left, new Point(left.X + 160, left.Y), 1800);
            rec.Hold(700);
            rec.MarkPoster();
            // 다시 같은 자리에서 위로(바깥 고리를 잡으면 화면 회전이 되므로 늘 안쪽에서 누른다).
            rec.Move(left, 500);
            rec.Drag(left, new Point(left.X + 10, left.Y - 50), 1200);
            rec.Hold(1200);
            // 카드(폭 264)에 작게만 보이므로 두 배 폭으로 줄여 쓴다(앱에 넣는 파일 크기).
            rec.Save("src/Chickxel.App/Assets/Tutorial/card-model", ClipFormat.WebP, webpLossless: false, webpQuality: 80, webpWidth: 528);
        }, settleMs: 1200);
    }

    // ───────────── 레벨 ─────────────

    /// <summary>
    /// 갤러리에 올리기(튜토리얼 '레벨'의 큰 움짤 — 실제로 올리지 않고 보이기만): 로그인한 계정으로 메뉴 줄의 갤러리 단추 → 올리기 창에서 제목 적기
    /// → 편집 파일 함께 올리기 켜기 → 올리기(올리는 중 막대) → 올린 뒤 알림(+15 XP). 가짜 서버라 올리기 단추의 실제 동작 대신 창의 올리는 중 표시와
    /// 앱이 올린 뒤 띄우는 알림 글(<c>Gallery_Done</c> + <c>Gallery_XpGained</c>)을 그대로 띄운다.
    /// </summary>
    [Shot("clip-tutorial-level-upload")]
    public static void LevelUpload(ShotKit k)
    {
        CommunityShots.UseFakeAccount(signedIn: true);
        // 올리기 창(약 800 높이)이 아래 단추·진행 막대까지 들어가게 창을 세로로 넉넉히.
        var m = ScreenDemo.Open(k, width: 1120, height: 940);
        ScreenDemo.Build(k, m, group: false);
        k.Settle(500);
        var gallery = ShotKit.Get<Button>(m, "_galleryButton");
        using var rec = new ClipRecorder(k, m, new Rect(0, 0, 1120, 940), titleBar: true, frameMs: 50) { TitleFilter = t => t.Contains("Chickxel") ? "Chickxel" : t };
        rec.Place(new Point(820, 300));
        rec.Hold(600);
        rec.Move(rec.At(gallery), 800);
        rec.Hold(300);
        k.Dialog(() => rec.PressAndRelease(rec.At(gallery), 0, pressMs: 120), w =>
        {
            Console.WriteLine($"  gallery dialog {w.ClientSize}");
            // 제목은 문서 이름으로 미리 차 있다: 비우고 새로 적는 모습으로.
            var title = ShotKit.Get<TextBox>(w, "_title");
            title.Text = "";
            rec.Hold(900);
            const string name = "Wolf idle";
            rec.Click(rec.At(title), 700, pressMs: 100, afterMs: 200);
            rec.Type(name, 90);
            rec.Hold(400);
            var share = ShotKit.Get<CheckBox>(w, "_shareProject");
            var sb = rec.BoundsOf(share);
            rec.Click(new Point(sb.X + 10, sb.Center.Y), 800, pressMs: 100, afterMs: 700);
            // 올리기: 누르는 동안 올리는 중으로 바꿔(단추가 꺼져 실제로 보내지 않음) 진행 막대를 채운다.
            var ok = ShotKit.Get<Button>(w, "OkButton");
            rec.Move(rec.At(ok), 800);
            rec.Hold(200);
            rec.Down();
            ShotKit.Call(w, "SetUploading", true);
            ok.IsEnabled = false;
            rec.Hold(150);
            rec.Up();
            for (int i = 1; i <= 10; i++)
            {
                ShotKit.Call(w, "ReportProgress", i / 10.0);
                rec.Hold(130);
            }
            var level = Chickxel.App.AppCommunity.Client.User?.Level ?? 1;
            string done = string.Format(UiStrings.Gallery_Done, name) + "\n\n" + string.Format(UiStrings.Gallery_XpGained, 15, level);
            k.Dialog(() => Ui.Info(w, done, UiStrings.Gallery_Title), _ =>
            {
                rec.MarkPoster();
                rec.Hold(2600);
            }, settleMs: 300);
        }, settleMs: 600);
        rec.Hold(400);
        // 큰 말풍선(움짤 폭 약 610)에 맞춰 줄여 쓴다(앱에 넣는 파일 크기).
        rec.Save(Out + "level-upload", ClipFormat.WebP, webpLossless: false, webpQuality: 75, webpWidth: 860);
    }
}
