using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Ai;
using Gpixel.App.Localization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using static GuideShots.AiChat;

namespace GuideShots;

internal static class AiShots
{
    // ───────────── 예시 대화 ─────────────

    private static Script OutlineScript() => new Script()
        .Think(420)
        .Tool("get_document")
        .Tool("add_effect", new JsonObject { ["type"] = "outline", ["settings"] = new JsonObject { ["color"] = "#FFFFFF", ["thickness"] = 1 } })
        .Text(L("캐릭터 레이어에 **외곽선** 효과(1px, 흰색)를 넣었습니다. 효과라서 그림 픽셀은 그대로이고, 효과 창이나 타임라인에서 언제든 고치거나 끌 수 있어요.",
            "I added an **Outline** effect (1px, white) to the character layer. It's an effect, so the pixels stay untouched — you can tweak or turn it off any time in the Effects window or the timeline."))
        .Done(0.031, 12400);

    private static string OutlineAsk => L("캐릭터에 1px 흰 외곽선을 넣어 줘", "Add a 1px white outline to the character");

    private static string SampleName => L("늑대귀 모험가.png", "wolf-adventurer.png");

    /// <summary>예시 그림을 화면 언어에 맞는 이름으로 복사한다.</summary>
    private static string Sample(ShotKit k) => k.CopySample(SampleName);

    /// <summary>합친 그림의 메인 창 제목(실제 제목 끝의 폴더 경로는 넣지 않음).</summary>
    private static string MainTitle => SampleName + " — Gpixel";

    /// <summary>늑대귀 모험가를 열고 Claude 탭에서 "흰 외곽선" 차례 하나를 끝낸 채팅.</summary>
    private static AiChat OutlineChat(ShotKit k, int mainWidth = 1600, int mainHeight = 900, int chatWidth = 470, int chatHeight = 720)
    {
        var main = k.Main(Sample(k), width: mainWidth, height: mainHeight);
        var chat = AiChat.Open(k, main, Claude, chatWidth, chatHeight);
        chat.Send(Claude, OutlineAsk, OutlineScript());
        return chat;
    }

    /// <summary>지팡이 그리기(새 레이어 + 선·타원 — 여러 그리기 도구).</summary>
    private static Script StaffScript() => new Script()
        .Think(610)
        .Tool("get_image")
        .Tool("add_layer", new JsonObject { ["name"] = L("지팡이", "Staff") })
        .Tool("draw_shape", new JsonObject { ["shape"] = "line", ["x1"] = 45, ["y1"] = 30, ["x2"] = 45, ["y2"] = 95, ["color"] = "#7A4A24" })
        .Tool("draw_shape", new JsonObject { ["shape"] = "line", ["x1"] = 46, ["y1"] = 31, ["x2"] = 46, ["y2"] = 95, ["color"] = "#5A3418" })
        .Tool("draw_shape", new JsonObject { ["shape"] = "ellipse", ["x1"] = 43, ["y1"] = 25, ["x2"] = 48, ["y2"] = 30, ["color"] = "#4FC3F7", ["filled"] = true })
        .Tool("set_pixels", new JsonObject { ["pixels"] = new JsonArray(new JsonObject { ["x"] = 44, ["y"] = 26, ["color"] = "#E1F5FE" }) })
        .Text(L("오른쪽에 **지팡이** 레이어를 새로 만들어 나무 막대(갈색 2톤)와 파란 보석을 그렸습니다. 원래 캐릭터 레이어는 건드리지 않았어요.",
            "I made a new **Staff** layer on the right and drew a wooden shaft (two browns) with a blue gem on top. The character layer itself is untouched."))
        .Done(0.058, 21800);

    private static TextBlock ToolLine(AiChat chat, string provider, string tool)
        => ShotKit.All<TextBlock>(chat.Messages(provider)).First(t => t.Text?.StartsWith("• " + tool, StringComparison.Ordinal) == true);

    private static Button StepButton(AiChat chat, string provider, int index = -1)
    {
        var steps = chat.Field<IList>(provider, "_steps");
        return ShotKit.Get<Button>(steps[index < 0 ? steps.Count + index : index]!, "Button");
    }

    private static Control LastActions(AiChat chat, string provider)
    {
        var turns = chat.Field<IList>(provider, "_userTurns");
        return ShotKit.Get<Control>(turns[turns.Count - 1]!, "Actions");
    }

    private static Control LastMessage(AiChat chat, string provider)
    {
        var children = chat.Messages(provider).Children;
        return children[^1];
    }

    private static void Raise(Button b) => b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>오른쪽 칸(도움말·그리기 설정)의 여닫는 움직임을 끝까지 진행한다(화면 없는 실행에서는 움직임 타이머가 늦게 돌 수 있음).</summary>
    private static void FinishPanel(ShotKit k, ChatWindow w)
    {
        k.Settle(300);
        for (int i = 0; i < 20 && ShotKit.Get<Avalonia.Threading.DispatcherTimer>(w, "_helpTimer").IsEnabled; i++)
        {
            ShotKit.Call(w, "StepHelp");
            k.Settle(50);
        }
        k.Settle(400);
    }

    /// <summary>창 좌표 사각형 여러 개를 합쳐 <paramref name="pad"/>만큼 넓힌 자르기 영역.</summary>
    private static Rect Union(Visual top, double pad, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b)).Inflate(pad);

    /// <summary>팝업(메뉴) 안 컨트롤의 <paramref name="top"/> 창 좌표 사각형.</summary>
    private static Rect PopupRect(Control c, Window top)
    {
        var host = TopLevel.GetTopLevel(c)!;
        var r = ShotKit.Bounds(c, host);
        var impl = host.PlatformImpl!;
        var pos = (PixelPoint)impl.GetType().GetProperty("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(impl)!;
        var d = pos - top.Position;
        return r.Translate(new Vector(d.X, d.Y));
    }

    /// <summary>예시 그림(49×98 늑대귀 모험가)을 열린 문서에서 읽는다.</summary>
    private static PixelImage Wolf(AiChat chat) => chat.Project.Flatten(0);

    // ───────────── 무엇을 할 수 있나요 · 채팅 창 열기 ─────────────

    [Shot("ai-overview")]
    public static void Overview(ShotKit k)
    {
        var chat = OutlineChat(k, 1280, 780, 430, 640);
        chat.Main.Title = MainTitle;
        Composite(k, "ai-overview", chat.Main, [(chat.Window, new Point(1280 - 430 - 18, 120))]);
    }

    [Shot("ai-open")]
    public static void OpenChat(ShotKit k)
    {
        var main = k.Main(Sample(k), width: 1280, height: 720);
        var button = ShotKit.All<Button>(main).First(b => ToolTip.GetTip(b)?.ToString()?.StartsWith(MainStrings.Menu_Ai_ToggleChat_226, StringComparison.Ordinal) == true);
        var aiMenu = ShotKit.All<MenuItem>(main).First(m => m.Header?.ToString()?.Replace("_", "").StartsWith("AI", StringComparison.Ordinal) == true);
        k.Click(aiMenu, settleMs: 500);
        var item = aiMenu.Items.OfType<MenuItem>().First(m => m.Header is Grid g
            && g.Children.OfType<TextBlock>().Any(t => t.Text?.StartsWith(MainStrings.Menu_Ai_ToggleChat_226, StringComparison.Ordinal) == true));
        var itemRect = PopupRect(item, main);
        k.Save(main, crop: new Rect(0, 0, 1280, Math.Max(itemRect.Bottom + 80, 140)), marks: [button, aiMenu], markRects: [itemRect]);
    }

    // ───────────── 설치와 로그인 ─────────────

    [Shot("ai-setup")]
    public static void Setup(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude, 520, 640);
        var tab = chat.Tab(Claude);
        ShotKit.Call(tab, "ShowNoCli");
        k.Settle(300);
        var install = (Button)ShotKit.FindText(chat.Window, UiStrings.S_ChatWindow_ShowNoCli_ED06427F94, ancestorControl: true);
        var recheck = (Button)ShotKit.FindText(chat.Window, UiStrings.S_ChatWindow_ShowNoCli_2736EF27CC, ancestorControl: true);
        k.Save(chat.Window, marks: [install, recheck]);

        // 설치는 됐지만 로그인 전(Codex 탭).
        chat.Show(Codex);
        var codex = chat.Tab(Codex);
        ShotKit.Call(codex, "ShowLogin", new AuthStatus(false, null, null, null, null));
        k.Settle(300);
        var login = (Button)ShotKit.FindText(chat.Window, string.Format(UiStrings.S_ChatWindow_ShowLogin_86A2C2515E, AiProvider.Codex.Name), ancestorControl: true);
        var terminal = (Button)ShotKit.FindText(chat.Window, UiStrings.S_ChatWindow_ShowLogin_F93C9FEC46, ancestorControl: true);
        k.Save(chat.Window, "ai-setup-2", crop: new Rect(0, 0, 520, 330), marks: [login, terminal]);

        // 첫 사용 때 먼저 뜨는 AI 상태 창(예시 결과: Claude Code 준비됨, Codex CLI 설치 안 됨).
        StatusDialog(k, main, chat.Mcp, "ai-setup-3");
    }

    /// <summary>AI 상태 창을 점검(CLI 실행) 없이 예시 결과로 채워 찍는다.</summary>
    private static void StatusDialog(ShotKit k, MainForm main, McpServer mcp, string name)
    {
        var asm = typeof(ChatWindow).Assembly;
        var dlgType = asm.GetType("Gpixel.App.Ai.AiStatusDialog")!;
        var levelType = asm.GetType("Gpixel.App.Ai.AiHealthLevel")!;
        var healthType = asm.GetType("Gpixel.App.Ai.AiProviderHealth")!;
        object Level(string n) => Enum.Parse(levelType, n);
        var dlg = (DialogBase)Activator.CreateInstance(dlgType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [mcp, App.Settings, true], null)!;
        ShotKit.Set(dlg, "_checking", true);
        k.Dialog(() => dlg.ShowDialog(main), w =>
        {
            ShotKit.Call(dlg, "SetStatus", ShotKit.Get<Control>(dlg, "_serverIcon"), ShotKit.Get<TextBlock>(dlg, "_serverText"), Level("Ok"),
                UiStrings.AiStatus_McpServerReady.Replace("{tools}", mcp.Tools.ListTools().Count.ToString(), StringComparison.Ordinal));
            var cards = ShotKit.Get<IDictionary>(dlg, "_cards");
            var claude = Activator.CreateInstance(healthType, AiProvider.Claude, @"%USERPROFILE%\.local\bin\claude.exe", "2.1.4 (Claude Code)", null,
                new AuthStatus(true, "you@example.com", "max", null, null), Level("Info"), null)!;
            var codex = Activator.CreateInstance(healthType, AiProvider.Codex, null, null, null, null, Level("Warning"), null)!;
            ShotKit.Call(cards["claude"]!, "Apply", claude);
            ShotKit.Call(cards["codex"]!, "Apply", codex);
            ShotKit.Get<ProgressBar>(dlg, "_progress").IsVisible = false;
            ShotKit.Get<TextBlock>(dlg, "_message").Text = UiStrings.AiStatus_CheckComplete;
            ShotKit.Get<Button>(dlg, "_recheck").IsEnabled = true;
            ShotKit.Get<Button>(dlg, "OkButton").IsEnabled = true;
            ShotKit.Pump(400);
            k.Save(w, name);
        });
    }

    // ───────────── 탭 · 대화하기 · 지난 대화 · 도움말 ─────────────

    [Shot("ai-tabs")]
    public static void Tabs(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 520, chatHeight: 640);
        // Codex 탭에서 일을 맡겨 둔 채(도는 중) Claude 탭으로 돌아와 또 보내 본다.
        chat.Show(Codex);
        chat.Send(Codex, L("걷는 프레임 4장을 그려 줘", "Draw 4 walking frames"), new Script().Think(800).Writing("draw_pixels", 2600).Pause());
        chat.Show(Claude);
        chat.Send(Claude, L("머리카락을 조금 더 밝게 해 줘", "Make the hair a bit brighter"), new Script());
        var toggle = ShotKit.Get<Control>(chat.Window, "_providerToggle");
        var busy = (Control)LastMessage(chat, Claude);
        k.Save(chat.Window, marks: [toggle, busy]);
    }

    [Shot("ai-chat")]
    public static void Chat(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 520, chatHeight: 720);
        // 다음 메시지: 그림 칩 + 글을 입력란에 넣어 둔 상태.
        var wolf = Wolf(chat);
        var head = wolf.Crop(new System.Drawing.Rectangle(0, 0, 49, 46));
        ShotKit.Call(chat.Tab(Claude), "AttachImage", head, true);
        ShotKit.Call(chat.Tab(Claude), "Prefill", L("이 얼굴 표정으로 웃는 얼굴 프레임을 하나 더 만들어 줘", "Make one more frame with this face smiling"));
        k.Settle(400);
        var w = chat.Window;
        var marks = new List<Control>
        {
            chat.Field<ComboBox>(Claude, "Model"),
            ShotKit.Get<Button>(w, "_newChat"),
            ShotKit.Get<Control>(w, "_help"),
            (Control)ShotKit.FindText(w, UiStrings.S_ChatWindow_ChatTab_B428204BA3, ancestorControl: true),
            (Control)ShotKit.FindText(w, UiStrings.S_ChatWindow_ChatTab_F9D4399092, ancestorControl: true),
            chat.Field<Button>(Claude, "_sendButton"),
        };
        k.Save(w, marks: marks);

        // 모델 목록.
        var model = chat.Field<ComboBox>(Claude, "Model");
        model.IsDropDownOpen = true;
        k.Settle(500);
        k.Save(w, "ai-chat-2", crop: new Rect(0, 0, 520, 300), marks: [model]);
        model.IsDropDownOpen = false;
        k.Settle(200);
    }

    [Shot("ai-history")]
    public static void History(ShotKit k)
    {
        // 목록 날짜는 시스템 문화권으로 적힌다: 영어 그림은 영어 날짜로(샷이 끝나면 되돌림).
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        if (ShotKit.Language == "en") System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        try { HistoryShot(k); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    private static void HistoryShot(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude, 470, 560, before: settings =>
        {
            var store = new ChatHistoryStore(Path.Combine(Platform.AppDataDirectory, "Gpixel", "Chats"));
            var models = AiProvider.Claude.Models(settings);
            string? other = models.Count > 1 ? models[1].Value : null;
            var now = DateTime.UtcNow;
            (string Ko, string En, double HoursAgo, string? Model, bool Fav)[] rows =
            [
                ("늑대귀 모험가 걷기 애니메이션 6프레임", "Wolf-eared adventurer walk cycle, 6 frames", 300, null, true),
                ("캐릭터에 1px 흰 외곽선을 넣어 줘", "Add a 1px white outline to the character", 0.2, null, false),
                ("오른손에 지팡이를 그려 줘", "Draw a staff in the right hand", 3, null, false),
                ("16×16 슬라임 튀기기 4프레임", "16×16 slime bounce, 4 frames", 30, other, false),
                ("팔레트를 16색으로 줄여 줘", "Reduce the palette to 16 colors", 80, other, false),
                ("참고 자료의 검처럼 도끼 그려 줘", "Draw an axe like the sword in references", 200, null, false),
            ];
            foreach (var (ko, en, ago, model, fav) in rows)
            {
                var c = new SavedChat
                {
                    Id = ChatHistoryStore.NewId(),
                    Provider = Claude,
                    Title = L(ko, en),
                    Model = model,
                    CreatedUtc = now.AddHours(-ago - 1),
                    UpdatedUtc = now.AddHours(-ago),
                    SessionId = Guid.NewGuid().ToString("N"),
                    Items = [new ChatItem { Kind = ChatItemKind.User, Text = L(ko, en) }],
                };
                store.Save(c);
                if (fav) store.SetFavorite(Claude, c.Id, true);
            }
        });
        var list = chat.Field<StackPanel>(Claude, "_list");
        var firstRow = (Control)list.Children[2];
        var star = ShotKit.All<Button>(firstRow).First();
        var trash = ShotKit.All<Button>(firstRow).Last();
        k.Save(chat.Window, marks: [ShotKit.Get<Button>(chat.Window, "_newChat"), star, trash]);
    }

    [Shot("ai-help")]
    public static void Help(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 470, chatHeight: 720);
        var help = ShotKit.Get<ToggleButton>(chat.Window, "_help");
        help.IsChecked = true;
        FinishPanel(k, chat.Window);
        k.Save(chat.Window, marks: [help]);
        // 예시 부탁·요령까지 내린 모습.
        var scroll = ShotKit.Get<ScrollViewer>(chat.Window, "_helpScroll");
        var example = ShotKit.All<TextBlock>(scroll).FirstOrDefault(t => t.Text == UiStrings.Chat_Help_ExamplesTitle);
        if (example is not null)
        {
            var y = example.TranslatePoint(new Point(0, 0), (Visual)scroll.Content!)!.Value.Y;
            scroll.Offset = new Vector(0, Math.Max(0, y - 8));
            k.Settle(400);
        }
        var host = ShotKit.Get<Control>(chat.Window, "_helpHost");
        k.SaveControl(host, "ai-help-2", pad: 0);
    }

    // ───────────── 그림 생성 ─────────────

    [Shot("ai-generate")]
    public static void Generate(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Codex, 470, 720);
        var w = chat.Window;
        // Codex 상태 확인(CLI 실행)을 건너뛰고 "로그인됨"을 보인다.
        ShotKit.Set(w, "_codexChecking", true);
        var settingsButton = ShotKit.Get<ToggleButton>(w, "_drawSettings");
        settingsButton.IsChecked = true;
        FinishPanel(k, w);
        ShotKit.Get<TextBlock>(w, "_codexStatus").Text = UiStrings.AiDraw_Codex_Ready;
        k.Settle(300);
        k.Save(w, marks: [settingsButton]);
        settingsButton.IsChecked = false;
        FinishPanel(k, w);

        // 생성 결과 카드: 새 빈 그림에 "늑대귀 모험가"를 생성해 넣은 차례.
        var wolf = Wolf(chat);
        var host = (IAiHost)main;
        host.CreateProject(64, 112, null, null, out _);
        k.Settle(400);
        var tab = chat.Tab(Codex);
        var ask = L("늑대귀 모험가 캐릭터를 그려 줘. 은발, 남색 재킷", "Draw a wolf-eared adventurer: silver hair, navy jacket");
        chat.Send(Codex, ask, new Script()
            .Think(300)
            .ToolLine("generate_pixel_art", new JsonObject { ["prompt"] = "wolf-eared adventurer, silver hair, navy jacket", ["name"] = L("늑대귀 모험가", "Wolf adventurer") })
            .Do(() => ShotKit.Call(tab, "OnAiDrawProgress", UiStrings.AiDraw_Progress_Image))
            .Pause());
        chat.FakeElapsed(Codex, TimeSpan.FromSeconds(38), TimeSpan.FromSeconds(31));
        var thinking = ShotKit.Get<TextBlock>(tab, "_thinking");
        var userRow = (Control)chat.Messages(Codex).Children[0];
        k.Save(w, "ai-progress-2", crop: Union(w, 8, userRow, thinking), titleBar: false, marks: [thinking]);

        string message = "";
        chat.Sessions[Codex].Next.Clear();
        var rest = new Script()
            .Do(() =>
            {
                host.InsertPixelArt(L("늑대귀 모험가", "Wolf adventurer"), [wolf], 100, false, out message);
                var job = new Gpixel.Core.AiDraw.AiDrawJob { Id = "guide-sample", Name = L("늑대귀 모험가", "Wolf adventurer"), Frames = 1 };
                ShotKit.Call(tab, "OnAiDrawCompleted", new AiDrawOutcome(job, wolf, message));
            })
            .Text(L("그림을 생성해 49×98 픽셀 아트로 바꿔 새 레이어로 넣었습니다. 색이나 크기를 바꾸려면 **변환 설정 보기**를 눌러 다시 조정하세요.",
                "I generated the picture, converted it to 49×98 pixel art and inserted it as a new layer. Press **View conversion settings** to adjust colors or size."))
            .Done(null, 71300);
        w.Height = 960;
        k.Settle(300);
        chat.Resume(Codex, rest);
        var card = ShotKit.All<Button>(chat.Messages(Codex)).First(b => ShotKit.All<TextBlock>(b).Any(t => t.Text == UiStrings.AiDraw_ViewSettings));
        k.Save(w, "ai-generate-2", marks: [card]);
    }

    // ───────────── 지금 그림만 · 잠긴 레이어 · 진행 ─────────────

    [Shot("ai-file-only")]
    public static void FileOnly(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude, 470, 560, before: s => s.AiCurrentFileOnly = true);
        chat.Send(Claude, L("32×32 새 그림을 만들어서 칼 아이콘을 그려 줘", "Make a new 32×32 picture and draw a sword icon"), new Script()
            .Think(240)
            .Tool("new_project", new JsonObject { ["width"] = 32, ["height"] = 32 })
            .Text(L("지금은 **지금 그림만** 고치도록 되어 있어서 새 그림을 만들 수 없습니다. 이 그림 안에 새 레이어로 그리거나, **지금 그림만**을 끄고 다시 부탁해 주세요.",
                "**Current file only** is on, so I can't create a new picture. I can draw it on a new layer in this picture, or turn **Current file only** off and ask again."))
            .Done(0.012, 6100));
        var toggle = chat.Field<ToggleButton>(Claude, "_scopeToggle");
        var noticeText = UiStrings.Chat_FileOnly_Notice.Replace("{name}", chat.Project.DisplayName, StringComparison.Ordinal);
        var notice = ShotKit.All<SelectableTextBlock>(chat.Messages(Claude)).First(t => t.Text == noticeText);
        k.Save(chat.Window, marks: [toggle, notice, ToolLine(chat, Claude, "new_project")]);
    }

    [Shot("ai-locked")]
    public static void Locked(ShotKit k)
    {
        var main = k.Main(Sample(k), width: 1280, height: 780);
        var chat = AiChat.Open(k, main, Claude, 430, 560);
        var ctx = ShotKit.Get<EditorContext>(main, "_ctx");
        ctx.Structural(() => chat.Project.SetLayerProperties(0, locked: true));
        k.Settle(200);
        chat.Send(Claude, L("재킷을 빨간색으로 바꿔 줘", "Make the jacket red"), new Script()
            .Think(330)
            .Tool("get_document")
            .Tool("replace_color", new JsonObject { ["from"] = "#2B2D4F", ["to"] = "#B33A3A", ["layer"] = 0 })
            .Text(L("**레이어 1**이 잠겨 있어서 바꾸지 않았습니다. 바꾸려면 타임라인에서 자물쇠를 풀고 다시 부탁해 주세요.",
                "**Layer 1** is locked, so I left it alone. Unlock it with the lock icon in the timeline and ask again."))
            .Done(0.009, 5200));
        chat.Main.Title = MainTitle;
        Composite(k, "ai-locked", chat.Main, [(chat.Window, new Point(1280 - 430 - 18, 150))], [ToolLine(chat, Claude, "replace_color")]);
    }

    [Shot("ai-progress")]
    public static void Progress(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 470, chatHeight: 720);
        chat.Send(Claude, L("걷는 프레임 4장을 그려 줘", "Draw 4 walking frames"), new Script()
            .Think(1200)
            .Tool("get_document")
            .Tool("add_frame", new JsonObject { ["duplicate"] = true })
            .Writing("draw_pixels", 3480)
            .Pause());
        chat.FakeElapsed(Claude, TimeSpan.FromSeconds(41), TimeSpan.FromSeconds(12));
        var thinking = chat.Field<TextBlock>(Claude, "_thinking");
        k.Save(chat.Window, marks: [thinking, chat.Field<Button>(Claude, "_stopButton")]);
    }

    // ───────────── 편집 · 효과 · 참고 자료 ─────────────

    [Shot("ai-edits")]
    public static void Edits(ShotKit k)
    {
        var main = k.Main(Sample(k), width: 1280, height: 780);
        var chat = AiChat.Open(k, main, Claude, 430, 640);
        chat.Send(Claude, L("오른쪽에 파란 보석이 달린 지팡이를 그려 줘", "Draw a staff with a blue gem on the right"), StaffScript());
        chat.Main.Title = MainTitle;
        Composite(k, "ai-edits", chat.Main, [(chat.Window, new Point(1280 - 430 - 18, 120))]);
    }

    [Shot("ai-effects")]
    public static void Effects(ShotKit k)
    {
        var chat = OutlineChat(k);
        k.Save(chat.Window, marks: [ToolLine(chat, Claude, "add_effect")]);
    }

    [Shot("ai-references")]
    public static void References(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude);
        // 지금 캔버스를 설명과 함께 넣고(AI 도구 그대로), 앱 밖에서 넣은 것처럼 설명 없는 그림 하나를 폴더에 둔다.
        chat.Mcp.Tools.Call("add_reference", new JsonObject
        {
            ["title"] = L("늑대귀 모험가", "Wolf-eared adventurer"),
            ["description"] = L("49×98 치비 캐릭터. 1px 어두운 외곽선, 왼쪽 위 빛, 머리카락은 은색 3단 명암.",
                "49×98 chibi character. 1px dark outline, light from the top left, silver hair in 3 shades."),
            ["tags"] = new JsonArray(L("캐릭터", "character"), L("늑대", "wolf"), L("치비", "chibi")),
            ["kind"] = "character",
        });
        var lib = ((IAiHost)main).References;
        PngIO.Save(Wolf(chat).Crop(new System.Drawing.Rectangle(0, 0, 49, 46)), Path.Combine(lib.Root, "wolf-head.png"));
        chat.Window.Close();
        k.Settle(200);
        var wolfId = lib.AllItems.First(i => i.Title == L("늑대귀 모험가", "Wolf-eared adventurer")).Id;
        // 도구 창(모달 아님): 명령으로 띄우고 바로 찍는다.
        k.Command("AI/참고 자료");
        k.Settle(800);
        var w = k.Windows.OfType<ReferenceManagerWindow>().Single();
        ShotKit.Call(w, "Select", wolfId);
        k.Settle(800);
        k.Save(w);
        w.Close();
        k.Settle(200);

        // 환경 설정 > AI 참고 자료(폴더 칸은 시험용 임시 폴더 대신 보통 위치를 보인다).
        k.Dialog(() => k.Command("편집/환경 설정"), d =>
        {
            ShotKit.Find<TabView>(d).SelectedIndex = 3;
            ShotKit.Pump(300);
            ShotKit.Get<TextBox>(d, "_refsFolderBox").Text = @"%AppData%\Gpixel\References";
            ShotKit.Pump(300);
            k.Save(d, "ai-references-2");
        });
    }

    // ───────────── 되돌리기 · 마지막 메시지 · 문제 ─────────────

    [Shot("ai-undo")]
    public static void Undo(ShotKit k)
    {
        var chat = OutlineChat(k, 1280, 780, 430, 640);
        var button = StepButton(chat, Claude);
        var w = chat.Window;
        var bubble = (Control)LastActions(chat, Claude).GetVisualParent()!;
        k.Save(w, crop: Union(w, 10, bubble, (Control)button.GetVisualParent()!), titleBar: false, marks: [button]);
        k.Click(button, settleMs: 400);
        chat.Main.Title = MainTitle;
        Composite(k, "ai-undo-2", chat.Main, [(chat.Window, new Point(1280 - 430 - 18, 120))], [button]);
    }

    [Shot("ai-edit-last")]
    public static void EditLast(ShotKit k)
    {
        var chat = OutlineChat(k);
        var actions = LastActions(chat, Claude);
        var row = (Control)actions.GetVisualParent()!;
        var w = chat.Window;
        var answer = ShotKit.All<Control>(chat.Messages(Claude)).First(c => c.GetType().Name == "MarkdownView");
        var crop = Union(w, 10, row, answer);
        crop = crop.WithHeight(ShotKit.Bounds(answer, w).Bottom + 4 - crop.Y);
        k.Save(w, crop: crop, titleBar: false, marks: [actions]);
        var trash = ShotKit.All<Button>(actions).Last();
        k.Dialog(() => Raise(trash), d => k.Save(d, "ai-edit-last-2"));
    }

    [Shot("ai-troubleshoot")]
    public static void Troubleshoot(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Codex, 470, 520);
        var since = chat.Mcp.Counters;
        chat.Send(Codex, OutlineAsk, new Script()
            .Think(200)
            .Problem(chat.Mcp.DescribeProblem(since, null))
            .Text(L("편집기 도구에 연결하지 못해 그림을 고치지 못했습니다.", "I couldn't connect to the editor tools, so nothing was changed."))
            .Done(null, 9400));
        var error = ShotKit.All<SelectableTextBlock>(chat.Messages(Codex)).First(t => t.Text?.Contains("127.0.0.1", StringComparison.Ordinal) == true);
        k.Save(chat.Window, marks: [error]);
    }
}
