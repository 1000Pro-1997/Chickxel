using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>
/// 튜토리얼(앱의 TutorialSpotlight · MainForm.Tutorial.cs): 예시 파일 만들기와 점검. 단계 목록·대상은 저장소 루트 <c>튜토리얼.md</c>.
/// </summary>
internal static class TutorialShots
{
    /// <summary>
    /// '그림을 픽셀 아트로'의 예시 그림을 다시 만든다: 가이드 예시 늑대(49×98)를 AI가 만든 듯 흐리게 키운 JPG(<see cref="PixelateKit.MakeAiImage(ShotKit)"/>)를
    /// 앱 내장 자원 <c>src/Chickxel.App/Assets/Tutorial/pixelate-example.jpg</c>로 복사. '효과 쓰기' 예시(effect-example.png)는 늑대 PNG 그대로,
    /// '3D를 픽셀 아트로' 예시(model-example.glb)는 가이드 3D 장의 상자 병아리 모델(<see cref="ModelSample.Chick"/>).
    /// </summary>
    [Shot("tutorial-assets")]
    public static void Assets(ShotKit k)
    {
        var jpg = PixelateKit.MakeAiImage(k);
        var dir = Path.Combine(ShotKit.Root, "src", "Chickxel.App", "Assets", "Tutorial");
        Directory.CreateDirectory(dir);
        File.Copy(jpg, Path.Combine(dir, MainForm.TutorialPixelateExample), overwrite: true);
        File.Copy(ShotKit.SamplePng, Path.Combine(dir, "effect-example.png"), overwrite: true);
        File.Copy(ModelSample.Chick(k.WorkDir), Path.Combine(dir, MainForm.TutorialModelExample), overwrite: true);
        Console.WriteLine("tutorial assets -> " + dir);
    }

    /// <summary>
    /// 튜토리얼 점검: 튜토리얼 창과 세 튜토리얼을 실제로 돌리며 단계마다 (1) 기대한 단계 id에 왔는지, (2) 비출 대상이 보이는지 확인하고
    /// 그 창 그림(덮개·말풍선 포함)을 작업 폴더의 <c>tutorial-check/</c>에 저장한 뒤 (3) 사용자가 할 일을 대신 해서 다음 단계로 넘어가는지 본다.
    /// 어긋나면 그 단계 id와 함께 실패한다. 그림 폴더 경로는 끝에 찍는다.
    /// </summary>
    [Shot("tutorial-check")]
    public static void Check(ShotKit k)
    {
        var outDir = Path.Combine(Path.GetDirectoryName(k.WorkDir)!, "tutorial-check");
        Directory.CreateDirectory(outDir);
        // 픽셀로 정리: 불러오면 자동 최적화 끔, 프리셋마다 짧은 예산(점검이 오래 걸리지 않게).
        foreach (var p in PixelateKit.Options().OptimizePresets)
        {
            p.Trials = 20;
            p.Seconds = 2;
        }
        App.Settings.ShowTutorialOnStart = true;
        var m = ScreenDemo.Open(k, width: 1400, height: 860);
        ScreenDemo.Build(k, m, group: false);

        // 튜토리얼 창: 켤 때 뜨는 것과 같은 창(아직 아무것도 마치지 않아 '효과 만들기'는 잠김).
        TutorialWindow.Open(m, App.Settings);
        k.Settle(800);
        var welcome = TutorialWindow.Current ?? throw new InvalidOperationException("tutorial window did not open");
        Snap(k, welcome, outDir, "window");
        welcome.Close();
        k.Settle(200);
        // 선행 튜토리얼을 마치지 않았으면 시작하지 않고 튜토리얼 창(잠긴 카드)을 띄운다.
        Start(k, m, TutorialKind.EffectMaker);
        if (TutorialSpotlight.IsRunning || TutorialWindow.Current is null) throw new InvalidOperationException("locked tutorial started");
        TutorialWindow.Current.Close();
        k.Settle(200);

        // ── 레이어와 프레임 ──
        Start(k, m, TutorialKind.LayersFrames);
        Control Timeline() => ShotKit.Get<Control>(m, "_timeline");
        Expect(k, m, outDir, "layers.intro", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "layers.add", () => ShotKit.Call(Timeline(), "AddLayer"));
        Expect(k, m, outDir, "frames.add", () => ShotKit.Call(Timeline(), "AppendFrames", false, 1, 0));
        Expect(k, m, outDir, "frames.delete", () => ShotKit.Call(Timeline(), "DeleteFrame"));
        Expect(k, m, outDir, "frames.link", () =>
        {
            ShotKit.Call(ShotKit.Get<Control>(Timeline(), "_grid"), "SelectRect", 0, 0, null, 1);
            ShotKit.Call(Timeline(), "LinkSelectedCels");
        });
        Expect(k, m, outDir, "frames.linked", () => ClickBubble(k, m, UiStrings.Tutorial_Finish));
        Ended(k, "layers");

        // ── 레이어 묶기와 스킨: 자식 레이어 창(모달)은 기본 이름 그대로 확인 ──
        Start(k, m, TutorialKind.Skins);
        Expect(k, m, outDir, "skins.child", () =>
            k.Dialog(() => ShotKit.Call(Timeline(), "AddChildLayer"), w => k.Click(ShotKit.Get<Control>(w, "OkButton"))));
        Expect(k, m, outDir, "skins.tree", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        // 스킨 추가·바꾸기는 우클릭 메뉴·이름 창 대신 프로젝트 API로(같은 결과).
        var wolfLayer = ScreenDemo.Project(m).Layers.First(l => l.Parent is null);
        Expect(k, m, outDir, "skins.add", () => ScreenDemo.Project(m).AddSkin(wolfLayer, "B", wolfLayer.ActiveSkin));
        Expect(k, m, outDir, "skins.switch", () => ScreenDemo.Project(m).SetActiveSkin(wolfLayer, wolfLayer.Skins[0]));
        Expect(k, m, outDir, "skins.stack", () => ClickBubble(k, m, UiStrings.Tutorial_Finish));
        Ended(k, "skins");

        // ── 캔버스 다루기 ──
        Start(k, m, TutorialKind.Canvas);
        Expect(k, m, outDir, "canvas.view", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "canvas.mouse", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "canvas.ctrl", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "canvas.resize", () => k.Command("편집/캔버스 크기…"));
        Expect(k, m, outDir, "canvas.resize.edit", () => ShotKit.Call(ScreenDemo.Ctx(m), "CancelCanvasResize"));
        Expect(k, m, outDir, "canvas.layer", () => k.Command("편집/레이어 크기…"));
        // 마지막 단계: 적용(Enter와 같음)하면 마친다.
        Expect(k, m, outDir, "canvas.layer.edit", () => ScreenDemo.Ctx(m).CommitFloating());
        Ended(k, "canvas");
        if (!App.Settings.TutorialsDone.Contains(nameof(TutorialKind.Canvas))) throw new InvalidOperationException("canvas: applying on the last step did not finish");

        // ── 타일맵 ──
        Start(k, m, TutorialKind.Tilemap);
        Expect(k, m, outDir, "tile.create", () => k.Command("프레임·레이어/새 타일맵 레이어…"));
        Expect(k, m, outDir, "tile.tileset", () =>
        {
            // 첫 타일셋 묶음의 첫 자동 타일 칸을 누른다.
            var section = ShotKit.Get<System.Collections.IList>(ShotKit.Get<Control>(m, "_tilesetPanel"), "_sections").Cast<Control>().First();
            var auto = ShotKit.Get<Control>(section, "_autoView");
            double slot = ShotKit.Get<double>(auto, "SlotSize");
            k.Click(m, auto.TranslatePoint(new Point(slot / 2, slot / 2), m)!.Value);
        });
        Expect(k, m, outDir, "tile.tools", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "tile.paint", () =>
        {
            var canvas = ShotKit.Get<Control>(m, "_canvas");
            var sp = (Point)ShotKit.Call(canvas, "ImageToScreen", 40.0, 40.0)!;
            k.Click(m, canvas.TranslatePoint(sp, m)!.Value);
        });
        Expect(k, m, outDir, "tile.cells", () => ClickBubble(k, m, UiStrings.Tutorial_Finish));
        Ended(k, "tilemap");

        // ── 내보내기: 내보내기 창은 모달이라 그 안에서 나머지 단계를 본다 ──
        Start(k, m, TutorialKind.Export);
        Expect(k, m, outDir, "export.open", () => k.Dialog(() => ShotKit.Call(m, "OpenExportDialog"), w =>
        {
            Expect(k, m, outDir, "export.list", () => ClickBubble(k, w, UiStrings.Tutorial_Next));
            Expect(k, m, outDir, "export.add", () => k.Click(ShotKit.Get<Control>(w, "_addButton")));
            Expect(k, m, outDir, "export.format", () => ShotKit.Get<ComboBox>(w, "_formatCombo").SelectedIndex = 0);
            Expect(k, m, outDir, "export.onsave", () => ClickBubble(k, w, UiStrings.Tutorial_Next));
            // 마지막 단계에서 창을 닫으면 마친 것으로 친다.
            Expect(k, m, outDir, "export.run", w.Close);
        }, keepOpen: true));
        Ended(k, "export");
        if (!App.Settings.TutorialsDone.Contains(nameof(TutorialKind.Export))) throw new InvalidOperationException("export: closing on the last step did not finish");

        // ── 레벨(로그인한 가짜 계정) ──
        CommunityShots.UseFakeAccount(signedIn: true);
        Start(k, m, TutorialKind.Level);
        Expect(k, m, outDir, "level.badge", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "level.upload", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "level.rewards", () => ClickBubble(k, m, UiStrings.Tutorial_Finish));
        Ended(k, "level");

        // ── 효과 쓰기 ──
        Start(k, m, TutorialKind.Effects);
        Expect(k, m, outDir, "effects.open", () =>
        {
            // 어두운 곳은 막혀 있다(누르면 아래 컨트롤이 아니라 덮개 판이 받음).
            foreach (var p in new[] { new Avalonia.Point(600, 300), new Avalonia.Point(30, 30) })
                if (m.GetVisualAt(p)?.GetType().Name != "Shade") throw new InvalidOperationException($"effects.open: {p} not blocked");
            k.Click(ShotKit.Get<Control>(m, "_tbEffects"));
        });
        // 3~5프레임 칸 끌어 고르기(타임라인 셀 묶음).
        Expect(k, m, outDir, "effects.select", () =>
            ShotKit.Call(ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid"), "SelectRect", 0, 2, null, 4));
        Expect(k, m, outDir, "effects.add", () =>
        {
            var tile = (Control)ShotKit.Call(m, "TutorialOutlineTile")!;
            k.DoubleClick(tile);
        });
        // 고른 프레임에만 들어갔는지.
        var added = ScreenDemo.Project(m).Layers[0].Effects[^1];
        if (added.Frames is not { From: 2, To: 4 }) throw new InvalidOperationException("effect not limited to frames 3-5: " + added.Frames);
        Expect(k, m, outDir, "effects.adjust", () =>
        {
            var section = ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_toolOptions"), "_effectSection");
            var num = ShotKit.All<NumericUpDown>(section).First(n => n.IsEffectivelyVisible);
            num.Value = num.Value + 1 <= num.Maximum ? num.Value + 1 : num.Value - 1;
        });
        // 이전 단추: 지난 단계로 돌아가도 저절로 넘어가지 않고, 다음으로 다시 온다.
        Expect(k, m, outDir, "effects.timeline", () => ClickBubble(k, m, UiStrings.Tutorial_Prev));
        Expect(k, m, outDir, "effects.adjust", () => ClickBubble(k, m, UiStrings.Tutorial_Next), snap: false);
        Expect(k, m, outDir, "effects.timeline", () => ClickBubble(k, m, UiStrings.Tutorial_Next), snap: false);
        // 마지막: 추가 다운로드(상점) 단추 — 누르면 브라우저가 열리므로 점검은 끝내기로.
        Expect(k, m, outDir, "effects.store", () => ClickBubble(k, m, UiStrings.Tutorial_Finish));
        Ended(k, "effects");

        // ── 3D를 픽셀 아트로: 3D 모델 창은 모달이라 그 안에서 나머지 단계를 본다 ──
        Start(k, m, TutorialKind.Model);
        Expect(k, m, outDir, "model.open", () => k.Dialog(() => k.Command("3D/3D 모델 열기…"), w =>
        {
            Expect(k, m, outDir, "model.load", () => ClickBubble(k, w, UiStrings.Tutorial_Next));
            Expect(k, m, outDir, "model.rotate", () =>
            {
                if (NextShown(w)) throw new InvalidOperationException("model.rotate: Next shown on a required step");
                var yaw = ShotKit.Get<NumericUpDown>(w, "_yaw");
                yaw.Value = yaw.Value + 30 <= yaw.Maximum ? yaw.Value + 30 : yaw.Value - 30;
            });
            Expect(k, m, outDir, "model.output", () => ShotKit.Set(ShotKit.Get<object>(w, "_size"), "Size", new PixelSize(96, 96)));
            Expect(k, m, outDir, "model.look", () => ClickBubble(k, w, UiStrings.Tutorial_Next));
            // 가져오기를 누르면 창이 닫히며 마친다(새 탭으로 그림).
            Expect(k, m, outDir, "model.import", () => k.Click(ShotKit.Get<Control>(w, "OkButton")));
        }, keepOpen: true, settleMs: 1200));
        Ended(k, "model");
        if (!App.Settings.TutorialsDone.Contains(nameof(TutorialKind.Model))) throw new InvalidOperationException("model: import on the last step did not finish");

        // ── 그림을 픽셀 아트로 ──
        Start(k, m, TutorialKind.Pixelate);
        PixelateDialog Dialog() => k.Windows.OfType<PixelateDialog>().LastOrDefault() ?? throw new InvalidOperationException("pixelate dialog not open");
        // 창은 사용자가 메뉴(AI > AI 그림 픽셀로 정리…)로 연다 — 그 메뉴 명령으로.
        Expect(k, m, outDir, "pixelate.open", () => k.Command("AI/AI 그림 픽셀로 정리…"));
        Expect(k, m, outDir, "pixelate.source", () => ClickBubble(k, Dialog(), UiStrings.Tutorial_Next));
        // 준비 기다리기: 들어올 때 아직이면 끝나는 대로 저절로, 이미 끝났으면 '다음'(건너뛰지 않고 차례대로).
        Expect(k, m, outDir, "pixelate.wait", () => { if (NextShown(Dialog())) ClickBubble(k, Dialog(), UiStrings.Tutorial_Next); }, waitMs: 120000);
        // 꼭 해야 하는 단계에는 '다음'이 없다.
        Expect(k, m, outDir, "pixelate.settings", () =>
        {
            if (NextShown(Dialog())) throw new InvalidOperationException("pixelate.settings: Next shown on a required step");
            ShotKit.Call(Dialog(), "SetSettingsExpanded", true, false);
        });
        Expect(k, m, outDir, "pixelate.quick", () =>
        {
            var colors = ShotKit.Get<NumericUpDown>(Dialog(), "_quickTargetColors");
            var auto = ShotKit.Get<CheckBox>(Dialog(), "_quickTargetAuto");
            if (auto.IsChecked == true) auto.IsChecked = false;
            else colors.Value = colors.Value > colors.Minimum ? colors.Value - 1 : colors.Value + 1;
        });
        // 돌리기는 해 보면(최적화가 시작되면) 저절로 넘어간다 — 프리셋 칸 우클릭 대신 정리하기로 시작.
        Expect(k, m, outDir, "pixelate.rerun", () =>
        {
            var found = ShotKit.Call(Dialog(), "TutorialIdleTile", "shade") ?? throw new InvalidOperationException("no idle preset tile");
            Console.WriteLine($"  rerun tile: {found.GetType().GetField("Item2")!.GetValue(found)}");
            ShotKit.Call(Dialog(), "RunCurrent");
        });
        // 앞 단계에서 이미 돌기 시작했으면 재생 단계는 '다음'으로(저절로 건너뛰지 않음).
        Expect(k, m, outDir, "pixelate.run", () => { if (NextShown(Dialog())) ClickBubble(k, Dialog(), UiStrings.Tutorial_Next); });
        Expect(k, m, outDir, "pixelate.optimize", () => ClickBubble(k, Dialog(), UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "pixelate.colors", () => ClickBubble(k, Dialog(), UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "pixelate.apply", () => ClickBubble(k, Dialog(), UiStrings.Tutorial_Finish));
        Ended(k, "pixelate");
        Dialog().Close();
        k.Settle(300);
        // 두 번째 실행: 메뉴로 다시 연(닫으면 숨기만 하는) 픽셀로 정리 창에 예시 그림이 들어가고, 지난번에 펼쳐 둔 설정 칸·끝난 준비가 있어도 차례대로 가며,
        // 도중에 창을 닫으면 튜토리얼이 끝나는지.
        Start(k, m, TutorialKind.Pixelate);
        Expect(k, m, outDir, "pixelate.open", () => k.Command("AI/AI 그림 픽셀로 정리…"), snap: false);
        Expect(k, m, outDir, "pixelate.source", () => ClickBubble(k, Dialog(), UiStrings.Tutorial_Next), snap: false);
        Expect(k, m, outDir, "pixelate.wait", () => { if (NextShown(Dialog())) ClickBubble(k, Dialog(), UiStrings.Tutorial_Next); }, waitMs: 120000, snap: false);
        Expect(k, m, outDir, "pixelate.settings", () => Dialog().Close(), snap: false);
        Ended(k, "pixelate (closed)");
        k.Settle(300);

        // ── AI로 그리기: 로그인은 이 컴퓨터 상태에 달려 대상이 뜨는지만 보고 건너뛰기로 끝낸다 ──
        // 첫 사용 상태 점검 창(모달)을 이미 본 것으로 해 채팅 창이 바로 뜨게.
        var probe = typeof(MainForm).Assembly.GetType("Chickxel.App.Ai.AiStatusProbe")!;
        App.Settings.AiStatusDismissedFingerprint = (string)probe.GetMethod("Fingerprint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [App.Settings])!;
        Start(k, m, TutorialKind.Ai);
        Expect(k, m, outDir, "ai.open", () => k.Click(ShotKit.Get<Control>(m, "_tbChat")));
        var chat = WaitFor(k, () => k.Windows.OfType<Chickxel.App.Ai.ChatWindow>().FirstOrDefault(), 10000, "AI chat window")!;
        if (TutorialSpotlight.CurrentStepId == "ai.login")
            Expect(k, m, outDir, "ai.login", () => ClickBubble(k, chat, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "ai.settings", () => ClickBubble(k, chat, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "ai.input", () => ClickBubble(k, chat, UiStrings.Tutorial_Next));
        // 레이어 잠금(메인 창 타임라인의 자물쇠).
        Expect(k, m, outDir, "ai.lock", () => ClickBubble(k, m, UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "ai.external", () => ClickBubble(k, m, UiStrings.Tutorial_Finish));
        Ended(k, "ai");
        chat.Close();
        k.Settle(300);

        // ── 효과 만들기: AI로 그리기 · 효과 쓰기를 마쳐 열렸다 ──
        Start(k, m, TutorialKind.EffectMaker);
        // 효과 목록의 단추는 앱 MainWindow로 열어(점검 앱에는 없음) 메뉴와 같은 길로 연다.
        Expect(k, m, outDir, "maker.open", () => EffectMakerWindow.Open(m, ScreenDemo.Ctx(m)));
        Window Maker() => k.Windows.First(w => w.GetType().Name == "EffectMakerWindow");
        Expect(k, m, outDir, "maker.ai", () => ClickBubble(k, Maker(), UiStrings.Tutorial_Next), waitMs: 20000);
        Expect(k, m, outDir, "maker.code", () => ClickBubble(k, Maker(), UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "maker.preview", () => ClickBubble(k, Maker(), UiStrings.Tutorial_Next));
        Expect(k, m, outDir, "maker.use", () => ClickBubble(k, Maker(), UiStrings.Tutorial_Finish));
        Ended(k, "maker");
        Maker().Close();
        // 마친 튜토리얼은 카드에 "완료"(끝까지 마치면 튜토리얼 창이 다시 뜸).
        var after = WaitFor(k, () => TutorialWindow.Current, 5000, "tutorial window after finishing")!;
        k.Settle(600);
        Snap(k, after, outDir, "window-done");
        Console.WriteLine("done: " + string.Join(", ", App.Settings.TutorialsDone));
        after.Close();
        Console.WriteLine("tutorial check pictures -> " + outDir);
    }

    private static void Start(ShotKit k, MainForm m, TutorialKind kind)
    {
        ShotKit.Call(m, "StartTutorial", kind);
        k.Settle(500);
    }

    /// <summary>
    /// 단계 <paramref name="id"/>에 왔는지 확인하고(대상이 보일 때까지 기다림), 그 창 그림을 저장한 뒤(<paramref name="snap"/>) <paramref name="act"/>로 할 일을 하고,
    /// 다른 단계로 가거나 끝날 때까지 기다린다.
    /// </summary>
    private static void Expect(ShotKit k, MainForm m, string outDir, string id, Action act, int waitMs = 15000, bool snap = true)
    {
        WaitFor(k, () => TutorialSpotlight.CurrentStepId == id, 20000, $"step {id} (now {TutorialSpotlight.CurrentStepId ?? "ended"})");
        // 대상(덮개가 붙은 창)이 뜰 때까지: 덮개가 메인 창이 아닌 창에 붙거나, 메인 창에서 뚫린 칸이 생기면.
        var top = WaitFor(k, () => OverlayHost(k), 20000, $"overlay for {id}")!;
        k.Settle(300);
        var hole = HoleOf(top);
        Console.WriteLine($"{id}: {top.GetType().Name} hole={hole?.ToString() ?? "none"}");
        if (hole is null) throw new InvalidOperationException($"step {id}: target not visible");
        // 말풍선이 다른 창에 있으면 메인 창은 통째로 어둡게 덮여 있다.
        if (!ReferenceEquals(top, m) && !Dimmed(m)) throw new InvalidOperationException($"step {id}: main window not dimmed");
        if (snap) Snap(k, top, outDir, id);
        act();
        WaitFor(k, () => TutorialSpotlight.CurrentStepId != id, waitMs, $"leaving {id}");
    }

    /// <summary>창이 통째로 어둡게 덮였는지(뚫린 칸 없는 덮개 판).</summary>
    private static bool Dimmed(TopLevel top)
        => OverlayLayer.GetOverlayLayer(top) is { } layer && layer.GetVisualDescendants().Any(v => v.GetType().Name == "Shade"
            && v.GetType().GetField("_dim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(v) is true);

    /// <summary>튜토리얼이 끝났는지 확인하고, 끝까지 마쳐 다시 뜬 튜토리얼 창은 닫는다(다음 튜토리얼 그림을 가리지 않게).</summary>
    private static void Ended(ShotKit k, string name)
    {
        WaitFor(k, () => !TutorialSpotlight.IsRunning, 5000, $"{name} tutorial end");
        k.Settle(300);
        if (name != "maker") TutorialWindow.Current?.Close();
    }

    private static T? WaitFor<T>(ShotKit k, Func<T?> probe, int timeoutMs, string what)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            TutorialSpotlight.Tick();
            var v = probe();
            if (v is bool b ? b : v is not null) return v;
            if (DateTime.UtcNow > until) throw new InvalidOperationException("timed out waiting for " + what);
            k.Settle(100);
        }
    }

    private static bool WaitFor(ShotKit k, Func<bool> probe, int timeoutMs, string what) => WaitFor<bool>(k, probe, timeoutMs, what);

    /// <summary>덮개가 붙은 창(없으면 null).</summary>
    private static TopLevel? OverlayHost(ShotKit k)
    {
        foreach (var w in k.Windows)
            if (OverlayLayer.GetOverlayLayer(w) is { } layer && layer.Children.Count > 0 && HoleOf(w) is not null) return w;
        return null;
    }

    /// <summary>창 덮개 판의 뚫린 칸(덮개 좌표), 없으면 null.</summary>
    private static Rect? HoleOf(TopLevel top)
    {
        if (OverlayLayer.GetOverlayLayer(top) is not { } layer) return null;
        foreach (var shade in layer.GetVisualDescendants().Where(v => v.GetType().Name == "Shade"))
            if (shade.GetType().GetField("_hole", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(shade) is Rect r) return r;
        return null;
    }

    /// <summary>말풍선의 '다음' 단추가 보이는지.</summary>
    private static bool NextShown(TopLevel top)
        => OverlayLayer.GetOverlayLayer(top) is { } layer && ShotKit.All<Button>(layer).Any(b => b.IsEffectivelyVisible && b.Content as string == UiStrings.Tutorial_Next);

    /// <summary>말풍선의 글자가 <paramref name="text"/>인 단추를 누른다.</summary>
    private static void ClickBubble(ShotKit k, TopLevel top, string text)
    {
        var layer = OverlayLayer.GetOverlayLayer(top) ?? throw new InvalidOperationException("no overlay");
        var button = ShotKit.All<Button>(layer).First(b => b.IsEffectivelyVisible && b.Content as string == text);
        k.Click(button);
    }

    private static void Snap(ShotKit k, TopLevel top, string outDir, string name)
    {
        using var image = k.Capture(top);
        image.Save(Path.Combine(outDir, name + ".png"));
    }
}
