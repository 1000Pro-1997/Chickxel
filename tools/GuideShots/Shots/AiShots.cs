using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Ai;
using Gpixel.App.Localization;
using Gpixel.Core.AiDraw;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using static GuideShots.AiChat;

namespace GuideShots;

/// <summary>
/// AI 채팅 장(ai-*) 그림. 쪽마다 번호는 그 쪽의 그림 전체에 이어 붙는다(가이드 글의 ①②…와 같은 순서).
/// </summary>
internal static class AiShots
{
    // ───────────── 예시 대화 ─────────────

    private static Script OutlineScript() => new Script()
        .Think(420)
        .Tool("get_document")
        .Tool("add_effect", new JsonObject { ["type"] = "outline", ["settings"] = new JsonObject { ["color"] = "#FFFFFF", ["thickness"] = 1 } })
        .Text("I added an **Outline** effect (1px, white) to the character layer. It's an effect, so the pixels stay untouched — you can tweak or turn it off any time in the Effects window or the timeline.")
        .Done(0.031, 12400);

    private const string OutlineAsk = "Add a 1px white outline to the character";

    private const string SampleName = "wolf-adventurer.png";

    private static string Sample(ShotKit k) => k.CopySample(SampleName);

    /// <summary>합친 그림의 메인 창 제목(실제 제목 끝의 폴더 경로는 넣지 않음).</summary>
    private const string MainTitle = SampleName + " — Gpixel";

    /// <summary>합친 그림에서 채팅 창을 놓는 자리(메인 창 1280 너비, 채팅 창 430 너비).</summary>
    private static readonly Point ChatAt = new(1280 - 430 - 18, 120);

    /// <summary>늑대귀 모험가를 열고 Claude 탭에서 "흰 외곽선" 차례 하나를 끝낸 채팅.</summary>
    private static AiChat OutlineChat(ShotKit k, int mainWidth = 1600, int mainHeight = 900, int chatWidth = 470, int chatHeight = 720)
    {
        var main = k.Main(Sample(k), width: mainWidth, height: mainHeight);
        var chat = AiChat.Open(k, main, Claude, chatWidth, chatHeight);
        chat.Send(Claude, OutlineAsk, OutlineScript());
        FinishTimeline(k, main);
        return chat;
    }

    /// <summary>지팡이 그리기(새 레이어 + 선·타원 — 여러 그리기 도구).</summary>
    private static Script StaffScript() => new Script()
        .Think(610)
        .Tool("get_image")
        .Tool("add_layer", new JsonObject { ["name"] = "Staff" })
        .Tool("draw_shape", new JsonObject { ["shape"] = "line", ["x1"] = 45, ["y1"] = 30, ["x2"] = 45, ["y2"] = 95, ["color"] = "#7A4A24" })
        .Tool("draw_shape", new JsonObject { ["shape"] = "line", ["x1"] = 46, ["y1"] = 31, ["x2"] = 46, ["y2"] = 95, ["color"] = "#5A3418" })
        .Tool("draw_shape", new JsonObject { ["shape"] = "ellipse", ["x1"] = 43, ["y1"] = 25, ["x2"] = 48, ["y2"] = 30, ["color"] = "#4FC3F7", ["filled"] = true })
        .Tool("set_pixels", new JsonObject { ["pixels"] = new JsonArray(new JsonObject { ["x"] = 44, ["y"] = 26, ["color"] = "#E1F5FE" }) })
        .Text("I made a new **Staff** layer on the right and drew a wooden shaft (two browns) with a blue gem on top. The character layer itself is untouched.")
        .Done(0.058, 21800);

    // ───────────── 찾기 ─────────────

    private static TextBlock ToolLine(AiChat chat, string provider, string tool)
        => ToolLines(chat, provider, tool).First();

    private static IEnumerable<TextBlock> ToolLines(AiChat chat, string provider, string tool)
        => ShotKit.All<TextBlock>(chat.Messages(provider)).Where(t => t.Text?.StartsWith("• " + tool, StringComparison.Ordinal) == true);

    private static Button StepButton(AiChat chat, string provider, int index = -1)
    {
        var steps = chat.Field<IList>(provider, "_steps");
        return ShotKit.Get<Button>(steps[index < 0 ? steps.Count + index : index]!, "Button");
    }

    private static object LastTurn(AiChat chat, string provider)
    {
        var turns = chat.Field<IList>(provider, "_userTurns");
        return turns[turns.Count - 1]!;
    }

    /// <summary>마지막 사용자 메시지 아래 연필·휴지통 단추 줄.</summary>
    private static Panel LastActions(AiChat chat, string provider) => ShotKit.Get<Panel>(LastTurn(chat, provider), "Actions");

    /// <summary>마지막 사용자 메시지 줄(말풍선 + 단추 줄).</summary>
    private static Panel LastUserRow(AiChat chat, string provider) => ShotKit.Get<Panel>(LastTurn(chat, provider), "Row");

    /// <summary>마지막 사용자 메시지 말풍선.</summary>
    private static Control LastBubble(AiChat chat, string provider) => LastUserRow(chat, provider).Children[0];

    private static Control LastMessage(AiChat chat, string provider) => chat.Messages(provider).Children[^1];

    /// <summary>차례 끝 줄(비용 · 걸린 시간, 또는 시간만).</summary>
    private static TextBlock CostLine(AiChat chat, string provider)
        => ShotKit.All<TextBlock>(chat.Messages(provider)).Last(t => t.Text is { } s && Regex.IsMatch(s, @"^(\$[\d.]+ · )?[\d.]+s$"));

    /// <summary>보이는 단추 중 글이 <paramref name="text"/>인 것.</summary>
    private static Button VisibleButton(Visual root, string text)
        => ShotKit.All<Button>(root).First(b => b.IsEffectivelyVisible && ShotKit.All<TextBlock>(b).Any(t => t.Text == text));

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

    /// <summary>컨트롤 여러 개를 담는 창 좌표 사각형(+<paramref name="pad"/>).</summary>
    private static Rect Union(Visual top, double pad, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b)).Inflate(pad);

    /// <summary>팝업(메뉴·목록·풍선) 안 컨트롤의 <paramref name="top"/> 창 좌표 사각형.</summary>
    private static Rect PopupRect(Control c, Window top)
    {
        var host = TopLevel.GetTopLevel(c)!;
        var r = ShotKit.Bounds(c, host);
        var impl = host.PlatformImpl!;
        var pos = (PixelPoint)impl.GetType().GetProperty("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(impl)!;
        var d = pos - top.Position;
        return r.Translate(new Vector(d.X, d.Y));
    }

    /// <summary>지금 열린 팝업(풍선·플라이아웃 — 촬영 도구가 열린 순서로 적어 둔 것).</summary>
    private static List<Popup> OpenPopups()
        => ((List<Popup>)typeof(ShotKit).GetField("s_popups", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Where(p => p.IsOpen).ToList();

    /// <summary>
    /// 풍선 도움말을 연 채로 둔다(마우스를 올린 모습). 화면 없는 실행에는 마우스 자리가 없어 풍선이 엉뚱한 곳에 뜨므로
    /// 이 그림에서만 풍선 자리를 컨트롤 옆(<paramref name="placement"/>)으로 정한다.
    /// </summary>
    private static void OpenTip(ShotKit k, Control c, PlacementMode placement, double dx = 0)
    {
        ToolTip.SetPlacement(c, placement);
        ToolTip.SetHorizontalOffset(c, dx);
        // 기본 세로 간격(마우스 아래로 띄우는 값)을 없애 풍선이 컨트롤에 겹치지 않게 한다.
        ToolTip.SetVerticalOffset(c, placement is PlacementMode.Top ? -4 : 4);
        ToolTip.SetIsOpen(c, true);
        k.Settle(500);
    }

    /// <summary>열린 풍선 도움말의 <paramref name="top"/> 창 좌표 사각형.</summary>
    private static Rect TipRect(Control c, Window top)
    {
        if (ToolTip.GetTip(c) is Control tip && TopLevel.GetTopLevel(tip) is not null)
            return PopupRect(tip.FindAncestorOfType<ToolTip>() ?? tip, top);
        // 글 풍선: 마지막으로 열린 팝업(ToolTip이 글을 감쌈).
        var popup = OpenPopups().LastOrDefault() ?? throw new InvalidOperationException("tooltip did not open");
        return PopupRect((Control)popup.Child!, top);
    }

    /// <summary>
    /// 번호 동그라미(테두리 왼쪽 위 모서리)가 표시한 것을 가리지 않게 테두리를 왼쪽(<paramref name="left"/>)·위(<paramref name="up"/>)로 늘린 사각형.
    /// 글줄·작은 아이콘처럼 모서리 바로 안에 내용이 있는 것에 쓴다.
    /// </summary>
    private static Rect Room(Control c, Visual top, double left = 0, double up = 14) => Room(ShotKit.Bounds(c, top), left, up);

    private static Rect Room(Rect r, double left = 0, double up = 14) => new(r.X - left, r.Y - up, r.Width + left, r.Height + up);

    /// <summary>타임라인 행·열 움직임(레이어·효과 줄이 생기며 늘어남)을 끝까지 진행한다(화면 없는 실행에서는 움직임 타이머가 늦게 돌 수 있음).</summary>
    private static void FinishTimeline(ShotKit k, MainForm m)
    {
        var grid = TimelineGrid(m);
        var timer = ShotKit.Get<Avalonia.Threading.DispatcherTimer>(grid, "_animTimer");
        for (int i = 0; i < 40 && timer.IsEnabled; i++)
        {
            ShotKit.Call(grid, "OnAnimTick", null, EventArgs.Empty);
            k.Settle(20);
        }
        k.Settle(100);
    }

    /// <summary>
    /// 대화 칸 왼쪽을 <see cref="Gutter"/>만큼 더 띄운다(이 그림에서만): 위아래로 붙은 글줄 여러 개에 번호를 달 때 번호가 그 왼쪽 빈자리에 오게.
    /// </summary>
    private static void WidenGutter(ShotKit k, AiChat chat, string provider)
    {
        var messages = chat.Messages(provider);
        var m = messages.Margin;
        messages.Margin = new Thickness(m.Left + Gutter, m.Top, m.Right, m.Bottom);
        k.Settle(300);
    }

    private const double Gutter = 24;

    /// <summary>예시 그림(49×98 늑대귀 모험가)을 열린 문서에서 읽는다.</summary>
    private static PixelImage Wolf(AiChat chat) => chat.Project.Flatten(0);

    // ── 메인 창(캔버스·타임라인) 자리

    /// <summary>캔버스에 보이는 그림 전체의 메인 창 좌표 사각형.</summary>
    private static Rect ImageRect(MainForm m)
    {
        var c = ShotKit.Get<PixelCanvas>(m, "_canvas");
        var off = ShotKit.Get<System.Drawing.Point>(c, "_offset");
        var p = ShotKit.Get<EditorContext>(m, "_ctx").Project!;
        var local = new Rect(off.X, off.Y, p.Width * c.Zoom, p.Height * c.Zoom);
        var at = c.TranslatePoint(local.Position, m) ?? throw new InvalidOperationException("canvas not attached");
        return new Rect(at, local.Size);
    }

    private static Control TimelineGrid(MainForm m) => ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");

    /// <summary>타임라인 격자 좌표 사각형을 메인 창 좌표로.</summary>
    private static Rect GridRect(MainForm m, Rect r)
    {
        var at = TimelineGrid(m).TranslatePoint(r.Position, m) ?? throw new InvalidOperationException("timeline not attached");
        return new Rect(at, r.Size);
    }

    private static int LayerY(MainForm m, int layer) => (int)ShotKit.Call(TimelineGrid(m), "LayerY", layer)!;

    /// <summary>타임라인 레이어 이름 칸의 자물쇠 아이콘 자리.</summary>
    private static Rect LockRect(MainForm m, int layer) => GridRect(m, new Rect(24, LayerY(m, layer), 22, 36));

    /// <summary>타임라인 레이어 이름 칸 전체(눈·자물쇠·이름).</summary>
    private static Rect LayerNameRect(MainForm m, int layer) => GridRect(m, new Rect(1, LayerY(m, layer), 268, 36));

    // ───────────── 무엇을 할 수 있나요 · 채팅 창 열기 ─────────────

    [Shot("ai-overview")]
    public static void Overview(ShotKit k)
    {
        var chat = OutlineChat(k, 1280, 780, 430, 640);
        chat.Main.Title = MainTitle;
        // ① 말로 한 부탁 ② 그 부탁으로 바뀐 캔버스 ③ Claude·Codex 탭.
        Composite(k, "ai-overview", chat.Main, [(chat.Window, ChatAt)],
            [LastBubble(chat, Claude), ImageRect(chat.Main), ShotKit.Get<Control>(chat.Window, "_providerToggle")]);
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
        k.Save(main, crop: new Rect(0, 0, 1280, Math.Max(itemRect.Bottom + 80, 140)), marks: [button, aiMenu, itemRect]);
    }

    // ───────────── 설치와 로그인 ─────────────

    [Shot("ai-setup")]
    public static void Setup(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude, 760, 640);
        var w = chat.Window;

        // ⑫ 로그인한 계정(머리줄 — 넓은 창이라 다 보임).
        var status = ShotKit.Get<TextBlock>(w, "_status");
        // 글이 창 왼쪽 끝에 붙어 있어 번호가 글을 가리지 않게 이 그림에서만 왼쪽을 띄운다.
        var margin = status.Margin;
        status.Margin = new Thickness(30, 0, 4, 0);
        k.Settle(200);
        var account = new Rect(ShotKit.Bounds(status, w).Position, new Size(status.DesiredSize.Width, status.Bounds.Height));
        var header = Union(w, 6, status, ShotKit.Get<Control>(w, "_help"));
        k.Save(w, "ai-setup-5", crop: new Rect(0, header.Y, header.Right, header.Height), marks: [Room(account, left: 16, up: 0)], firstNumber: 12);
        status.Margin = margin;
        w.Width = 520;
        k.Settle(300);

        // ①~④ CLI를 못 찾은 화면.
        var tab = chat.Tab(Claude);
        ShotKit.Call(tab, "ShowNoCli");
        k.Settle(300);
        var install = VisibleButton(w, UiStrings.S_ChatWindow_ShowNoCli_ED06427F94);
        var copy = VisibleButton(w, UiStrings.S_ChatWindow_CommandBox_05C2558491);
        var pathTitle = (Control)ShotKit.FindText(w, string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_ShowNoCli_09620464D8, AiProvider.Claude.Command));
        var apply = VisibleButton(w, UiStrings.S_ChatWindow_ShowNoCli_306897216F);
        var recheck = VisibleButton(w, UiStrings.S_ChatWindow_ShowNoCli_2736EF27CC);
        k.Save(w, marks: [install, copy, Union(w, 2, pathTitle, apply), recheck]);

        // ⑤⑥ 설치 단추를 누르면 실행할 명령을 묻는 창(가이드는 Windows 기준이라 Windows 명령으로 보인다).
        var macSteps = UiStrings.S_AiInstaller_Preview_91DCED206B;
        k.Dialog(() => ShotKit.Call(tab, "StartInstall"), d =>
        {
            var message = ShotKit.All<TextBlock>(d).First(t => t.Text?.Contains(macSteps, StringComparison.Ordinal) == true);
            message.Text = message.Text!.Replace(macSteps, UiStrings.S_AiInstaller_Preview_A6B5B3079B, StringComparison.Ordinal);
            ShotKit.Pump(300);
            var ok = ShotKit.All<Button>(d).FirstOrDefault(b => b.IsDefault && b.IsEffectivelyVisible) ?? VisibleButton(d, "OK");
            k.Save(d, "ai-setup-2", marks: [Room(message, d, left: 14, up: 0), ok], firstNumber: 5);
        });

        // ⑦~⑨ 첫 사용 때 먼저 뜨는 AI 상태 창(예시 결과: Claude Code 준비됨, Codex CLI 설치 안 됨).
        StatusDialog(k, main, chat.Mcp, "ai-setup-3", firstNumber: 7);

        // ⑩⑪ 설치는 됐지만 로그인 전(Codex 탭).
        chat.Show(Codex);
        var codex = chat.Tab(Codex);
        ShotKit.Call(codex, "ShowLogin", new AuthStatus(false, null, null, null, null));
        k.Settle(300);
        var login = VisibleButton(w, string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_ShowLogin_86A2C2515E, AiProvider.Codex.Name));
        var terminal = VisibleButton(w, UiStrings.S_ChatWindow_ShowLogin_F93C9FEC46);
        k.Save(w, "ai-setup-4", crop: new Rect(0, 0, 520, 200), marks: [login, terminal], firstNumber: 10);
    }

    /// <summary>AI 상태 창을 점검(CLI 실행) 없이 예시 결과로 채워 찍는다.</summary>
    private static void StatusDialog(ShotKit k, MainForm main, McpServer mcp, string name, int firstNumber)
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
                UiStrings.AiStatus_McpServerReady.Replace("{tools}", mcp.Tools.ListTools().Count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
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
            TextBlock Line(string part) => ShotKit.All<TextBlock>(w).First(t => t.IsEffectivelyVisible && t.Text?.Contains(part, StringComparison.Ordinal) == true);
            // 줄 앞의 상태 아이콘까지 담는다(번호가 글을 가리지 않게).
            var claudeRows = Room(Union(w, 2, Line("2.1.4"), Line("you@example.com")), left: 22, up: 0);
            var codexRow = Room(Line(AiProvider.Codex.ProductName + " was not found"), w, left: 22, up: 0);
            k.Save(w, name, marks: [claudeRows, codexRow, ShotKit.Get<Button>(dlg, "OkButton")], firstNumber: firstNumber);
        });
    }

    // ───────────── 탭 · 대화하기 · 지난 대화 · 도움말 ─────────────

    [Shot("ai-tabs")]
    public static void Tabs(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 520, chatHeight: 640);
        // Codex 탭에서 일을 맡겨 둔 채(도는 중) Claude 탭으로 돌아와 또 보내 본다.
        chat.Show(Codex);
        chat.Send(Codex, "Draw 4 walking frames", new Script().Think(800).Writing("draw_pixels", 2600).Pause());
        chat.Show(Claude);
        var tab = chat.Tab(Claude);
        chat.Input(Claude).Text = "";
        ShotKit.Call(tab, "Prefill", "Make the hair a bit brighter");
        k.Settle(100);
        ShotKit.Call(tab, "Send");
        k.Settle(400);
        var toggle = ShotKit.Get<Control>(chat.Window, "_providerToggle");
        WidenGutter(k, chat, Claude);
        k.Save(chat.Window, marks: [toggle, Room(LastMessage(chat, Claude), chat.Window, left: Gutter - 2, up: 0), Room(chat.Input(Claude), chat.Window)]);
    }

    [Shot("ai-chat")]
    public static void Chat(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 520, chatHeight: 720);
        var w = chat.Window;
        var tab = chat.Tab(Claude);
        // 다음 메시지: 그림 칩 + 글을 입력란에 넣어 둔 상태.
        var head = Wolf(chat).Crop(new System.Drawing.Rectangle(0, 0, 49, 46));
        ShotKit.Call(tab, "AttachImage", head, true);
        ShotKit.Call(tab, "Prefill", "Make one more frame with this face smiling");
        k.Settle(400);

        // ①~⑦ 표의 차례: 보내기 · 이미지… · 캔버스 첨부 · 모델 · 새 대화 · 물음표 · 연필/휴지통.
        var model = chat.Field<ComboBox>(Claude, "Model");
        k.Save(w, marks:
        [
            chat.Field<Button>(Claude, "_sendButton"),
            VisibleButton(w, UiStrings.S_ChatWindow_ChatTab_B428204BA3),
            VisibleButton(w, UiStrings.S_ChatWindow_ChatTab_F9D4399092),
            model,
            ShotKit.Get<Button>(w, "_newChat"),
            ShotKit.Get<Control>(w, "_help"),
            LastActions(chat, Claude),
        ]);

        // ⑨ 모델 목록(첫 칸 = 기본 · 실제 모델).
        model.IsDropDownOpen = true;
        k.Settle(500);
        var first = (Control)model.ContainerFromIndex(0)!;
        var last = (Control)model.ContainerFromIndex(model.ItemCount - 1)!;
        var listRect = PopupRect(first, w).Union(PopupRect(last, w));
        k.Save(w, "ai-chat-3", crop: new Rect(0, 0, w.Bounds.Width, Math.Max(listRect.Bottom + 12, 140)), marks: [PopupRect(first, w)], firstNumber: 9);
        model.IsDropDownOpen = false;
        k.Settle(300);

        // ⑩⑪ 입력란의 그림 칩과 마우스를 올린 미리보기.
        var chip = ShotKit.Get<Canvas>(tab, "_chipLayer").Children.OfType<Border>().First(c => c.IsVisible);
        // 풍선을 조금 오른쪽으로 띄워 칩 위 왼쪽에 번호 자리를 남긴다.
        OpenTip(k, chip, PlacementMode.Top, dx: 40);
        var tipRect = TipRect(chip, w);
        var inputArea = Union(w, 8, chat.Input(Claude), chat.Field<Button>(Claude, "_sendButton"));
        k.Save(w, "ai-chat-4", crop: inputArea.Union(tipRect.Inflate(10)).Intersect(new Rect(w.Bounds.Size)), marks: [Room(chip, w, up: 12), tipRect], firstNumber: 10);
        ToolTip.SetIsOpen(chip, false);
        k.Settle(200);

        // ⑫⑬ 칩을 누르면: 큰 그림과 "첨부 빼기".
        k.Click(chip, settleMs: 500);
        var flyout = OpenPopups().Last();
        var content = (Control)flyout.Child!;
        var big = ShotKit.All<Image>(content).First();
        var remove = ShotKit.All<Button>(content).First();
        var flyRect = PopupRect(content, w);
        k.Save(w, "ai-chat-5", crop: inputArea.Union(flyRect.Inflate(10)).Intersect(new Rect(w.Bounds.Size)),
            marks: [PopupRect(big, w), PopupRect(remove, w)], firstNumber: 12);
        flyout.IsOpen = false;
        k.Settle(200);

        // ⑭ 보낸 메시지의 칩.
        chat.Sessions[Claude].Next.Enqueue(new Script()
            .Think(300)
            .Text("Sure — I'll draw a smiling version of this face as a new frame.")
            .Pause());
        ShotKit.Call(tab, "Send");
        k.Settle(600);
        var bubble = LastBubble(chat, Claude);
        var sentChip = ShotKit.All<Border>(bubble).First(b => b.Cursor is not null && ShotKit.All<TextBlock>(b).Any(t => t.Text == "#1"));
        // ⑧ 답을 받는 동안의 정지 단추.
        chat.FakeElapsed(Claude, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2));
        var stop = chat.Field<Button>(Claude, "_stopButton");
        k.Save(w, "ai-chat-6", crop: Union(w, 30, bubble).Intersect(new Rect(w.Bounds.Size)), marks: [Room(sentChip, w, left: 16, up: 0)], firstNumber: 14);
        k.Save(w, "ai-chat-2", crop: Union(w, 8, chat.Field<TextBlock>(Claude, "_thinking"), chat.Input(Claude), stop), marks: [stop], firstNumber: 8);
    }

    [Shot("ai-history")]
    public static void History(ShotKit k)
    {
        // 목록 날짜는 시스템 문화권으로 적힌다: 영어 날짜로(샷이 끝나면 되돌림).
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try { HistoryShot(k); }
        finally { CultureInfo.CurrentCulture = culture; }
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
            (string Title, double HoursAgo, string? Model, bool Fav)[] rows =
            [
                ("Wolf-eared adventurer walk cycle, 6 frames", 300, null, true),
                ("Add a 1px white outline to the character", 0.2, null, false),
                ("Draw a staff in the right hand", 3, null, false),
                ("16×16 slime bounce, 4 frames", 30, other, false),
                ("Reduce the palette to 16 colors", 80, other, false),
                ("Draw an axe like the sword in references", 200, null, false),
            ];
            foreach (var (title, ago, model, fav) in rows)
            {
                var c = new SavedChat
                {
                    Id = ChatHistoryStore.NewId(),
                    Provider = Claude,
                    Title = title,
                    Model = model,
                    CreatedUtc = now.AddHours(-ago - 1),
                    UpdatedUtc = now.AddHours(-ago),
                    SessionId = Guid.NewGuid().ToString("N"),
                    Items = [new ChatItem { Kind = ChatItemKind.User, Text = title }],
                };
                store.Save(c);
                if (fav) store.SetFavorite(Claude, c.Id, true);
            }
        });
        var w = chat.Window;
        var list = chat.Field<StackPanel>(Claude, "_list");
        var firstRow = (Control)list.Children[2];
        var star = ShotKit.All<Button>(firstRow).First();
        var trash = ShotKit.All<Button>(firstRow).Last();
        // ① 새 대화 ② 지난 대화 목록 ③ 즐겨찾기 별 ④ 휴지통 ⑤ 새 대화를 시작하는 입력란.
        k.Save(w, marks: [ShotKit.Get<Button>(w, "_newChat"), list, star, trash, Room(chat.Input(Claude), w, up: 10)]);
        // ⑥ 휴지통을 누르면 묻는 창.
        k.Dialog(() => Raise(trash), d =>
        {
            var yes = ShotKit.All<Button>(d).First(b => b.IsEffectivelyVisible);
            k.Save(d, "ai-history-2", marks: [yes], firstNumber: 6);
        });
    }

    [Shot("ai-help")]
    public static void Help(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 470, chatHeight: 720);
        var w = chat.Window;
        var help = ShotKit.Get<ToggleButton>(w, "_help");
        help.IsChecked = true;
        FinishPanel(k, w);
        var panel = ShotKit.Get<Control>(w, "_helpPanel");
        var close = ShotKit.All<Button>(panel).First(b => ToolTip.GetTip(b) as string == UiStrings.Chat_Help_Close);
        var scroll = ShotKit.Get<ScrollViewer>(w, "_helpScroll");
        var tools = ShotKit.All<TextBlock>(scroll).First(t => t.Text == UiStrings.Chat_Help_ToolsTitle);
        // ① 물음표 단추 ② 닫기 X ③ AI가 할 수 있는 일.
        k.Save(w, marks: [help, close, Room(tools, w, left: 12, up: 0)]);

        // ④ 예시 부탁 ⑤ 잘 쓰는 요령 — 칸을 내린 모습.
        var example = ShotKit.All<TextBlock>(scroll).First(t => t.Text == UiStrings.Chat_Help_ExamplesTitle);
        var y = example.TranslatePoint(new Point(0, 0), (Visual)scroll.Content!)!.Value.Y;
        scroll.Offset = new Vector(0, Math.Max(0, y - 8));
        k.Settle(400);
        var exampleButton = ShotKit.All<Button>(scroll).First(b => b.IsEffectivelyVisible && ToolTip.GetTip(b) as string == UiStrings.Chat_Help_ExampleTip);
        var tips = ShotKit.All<TextBlock>(scroll).First(t => t.Text == UiStrings.Chat_Help_TipsTitle);
        var host = ShotKit.Get<Control>(w, "_helpHost");
        // 칸 왼쪽의 대화 칸을 조금 넣어 번호를 그 자리에 단다(칸 안 글을 가리지 않게).
        var hostRect = ShotKit.Bounds(host, w);
        k.Save(w, "ai-help-2", crop: new Rect(hostRect.X - 28, hostRect.Y, hostRect.Width + 28, hostRect.Height),
            marks: [Room(exampleButton, w, left: 14, up: 0), Room(tips, w, left: 22, up: 0)], firstNumber: 4);
    }

    // ───────────── 그림 생성 ─────────────

    [Shot("ai-generate")]
    public static void Generate(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Codex, 470, 900, before: s => s.AiDraw.Mode = AiDrawMode.Direct);
        var w = chat.Window;
        // Codex 상태 확인(CLI 실행)을 건너뛰고 "로그인됨"을 보인다.
        ShotKit.Set(w, "_codexChecking", true);
        var settingsButton = ShotKit.Get<ToggleButton>(w, "_drawSettings");
        settingsButton.IsChecked = true;
        FinishPanel(k, w);
        var codexStatus = ShotKit.Get<TextBlock>(w, "_codexStatus");
        codexStatus.Text = UiStrings.AiDraw_Codex_Ready;
        k.Settle(300);
        var modeGenerate = ShotKit.Get<RadioButton>(w, "_modeGenerate");
        var modeDirect = ShotKit.Get<RadioButton>(w, "_modeDirect");
        var info = ((Panel)modeGenerate.GetVisualParent()!).Children[1];
        var paletteRow = ShotKit.Get<Control>(w, "_paletteRow");
        var drawPanel = ShotKit.Get<Control>(w, "_drawPanel");
        // ① 그리기 설정 단추 ② Codex CLI 상태 ③ 정보 아이콘 ④ 그림 생성 ⑤ 직접 찍기 ⑥ 팔레트 색만 사용.
        // 칸 안의 줄은 번호가 단추·글을 가리지 않게 왼쪽 여백 쪽으로 늘린다.
        k.Save(w, crop: new Rect(0, 0, w.Bounds.Width, ShotKit.Bounds(codexStatus, w).Bottom + 24), titleBar: true,
            marks: [settingsButton, Room(codexStatus, w, left: 12, up: 0), Room(info, w, up: 12), Room(modeGenerate, w, left: 12, up: 0),
                Room(modeDirect, w, left: 12, up: 0), Room(paletteRow, w, left: 14, up: 0)]);

        // ⑦~⑭ 생성 옵션(표 순서).
        modeGenerate.IsChecked = true;
        k.Settle(400);
        // 선택지 줄(이름 + 컨트롤) — 번호가 체크 상자·이름을 가리지 않게 왼쪽 여백 쪽으로 늘린다.
        Rect Row(string field, int up = 1)
        {
            Visual c = ShotKit.Get<Control>(w, field);
            for (int i = 0; i < up; i++) c = c.GetVisualParent()!;
            return Room((Control)c, w, left: 12, up: 0);
        }
        var frameGrid = (Control)ShotKit.Get<IDictionary>(w, "_frameBoxes").Values.Cast<Control>().First().GetVisualParent()!;
        var options = ShotKit.Get<Control>(w, "_generateOptions");
        k.Save(w, "ai-generate-2", crop: new Rect(0, 0, w.Bounds.Width, Math.Min(w.Bounds.Height, ShotKit.Bounds(options, w).Bottom + 16)), titleBar: true, marks:
        [
            Row("_resizeResult"),
            Row("_sizeRow", 0),
            Row("_removeBackground"),
            Row("_autoOptimize"),
            Row("_insertCombo"),
            Row("_styleCombo"),
            Room(frameGrid, w, left: 12, up: 0),
            Row("_animCombo", 2),
        ], firstNumber: 7);
        settingsButton.IsChecked = false;
        FinishPanel(k, w);

        // ⑰⑱ 생성 결과 카드: 새 빈 그림에 "늑대귀 모험가"를 생성해 넣은 차례.
        var wolf = Wolf(chat);
        var host = (IAiHost)main;
        host.CreateProject(64, 112, null, null, out _);
        k.Settle(400);
        var tab = chat.Tab(Codex);
        chat.Send(Codex, "Draw a wolf-eared adventurer: silver hair, navy jacket", new Script()
            .Think(300)
            .ToolLine("generate_pixel_art", new JsonObject { ["prompt"] = "wolf-eared adventurer, silver hair, navy jacket", ["name"] = "Wolf adventurer" })
            .Do(() => ShotKit.Call(tab, "OnAiDrawProgress", UiStrings.AiDraw_Progress_Image))
            .Pause());
        chat.Sessions[Codex].Next.Clear();
        string message = "";
        var rest = new Script()
            .Do(() =>
            {
                host.InsertPixelArt("Wolf adventurer", [wolf], 100, false, out message);
                var job = new AiDrawJob { Id = "guide-sample", Name = "Wolf adventurer", Frames = 1 };
                ShotKit.Call(tab, "OnAiDrawCompleted", new AiDrawOutcome(job, wolf, message));
            })
            .Text("I generated the picture, converted it to 49×98 pixel art and inserted it as a new layer. Press **View conversion settings** to adjust colors or size.")
            .Done(null, 71300);
        chat.Resume(Codex, rest);
        var cardButton = ShotKit.All<Button>(chat.Messages(Codex)).First(b => ShotKit.All<TextBlock>(b).Any(t => t.Text == UiStrings.AiDraw_ViewSettings));
        var card = (Control)cardButton.GetVisualParent()!;
        var askRow = LastUserRow(chat, Codex);
        var cardCrop = Union(w, 10, askRow, card);
        k.Save(w, "ai-generate-4", crop: cardCrop.WithHeight(ShotKit.Bounds(card, w).Bottom + 8 - cardCrop.Y), marks: [card, cardButton], firstNumber: 17);

        // ⑮⑯ 애니메이션: 스프라이트 시트 한 장 생성 중인 진행 줄과 정지 단추.
        chat.Send(Codex, "Make a 4-frame walk animation of her", new Script()
            .Think(300)
            .ToolLine("generate_pixel_art", new JsonObject { ["prompt"] = "wolf-eared adventurer walk cycle", ["animation"] = "walk", ["frames"] = 4 })
            .Do(() => ShotKit.Call(tab, "OnAiDrawProgress", UiStrings.AiDraw_Progress_Sheet.Replace("{count}", "4", StringComparison.Ordinal)))
            .Pause());
        chat.FakeElapsed(Codex, TimeSpan.FromSeconds(52), TimeSpan.FromSeconds(44));
        var thinking = chat.Field<TextBlock>(Codex, "_thinking");
        var stop = chat.Field<Button>(Codex, "_stopButton");
        WidenGutter(k, chat, Codex);
        var progressArea = Union(w, 8, LastUserRow(chat, Codex), thinking, stop);
        k.Save(w, "ai-generate-3", crop: new Rect(0, progressArea.Y, w.Bounds.Width, progressArea.Height), marks: [Room(thinking, w, left: Gutter - 2, up: 0), stop], firstNumber: 15);

        // ⑲ Claude 탭: 그림 생성 옆 빨간 경고 아이콘과 그 풍선.
        chat.Show(Claude);
        settingsButton.IsChecked = true;
        FinishPanel(k, w);
        ShotKit.Call(w, "RefreshDrawSettings", false);
        k.Settle(200);
        var warning = ShotKit.Get<Control>(w, "_modeWarning");
        OpenTip(k, warning, PlacementMode.BottomEdgeAlignedRight);
        var tip = TipRect(warning, w);
        var panelRect = ShotKit.Bounds(drawPanel, w);
        var crop = new Rect(panelRect.X - 8, 0, panelRect.Width + 8, ShotKit.Bounds(codexStatus, w).Bottom + 16).Union(tip.Inflate(8)).Intersect(new Rect(w.Bounds.Size));
        k.Save(w, "ai-generate-5", crop: crop, marks: [warning], firstNumber: 19);
        ToolTip.SetIsOpen(warning, false);
    }

    // ───────────── 지금 그림만 · 잠긴 레이어 · 진행 ─────────────

    [Shot("ai-file-only")]
    public static void FileOnly(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude, 470, 560, before: s => s.AiCurrentFileOnly = true);
        chat.Send(Claude, "Make a new 32×32 picture and draw a sword icon", new Script()
            .Think(240)
            .Tool("new_project", new JsonObject { ["width"] = 32, ["height"] = 32 })
            .Text("**Current file only** is on, so I can't create a new picture. I can draw it on a new layer in this picture, or turn **Current file only** off and ask again.")
            .Done(0.012, 6100));
        var toggle = chat.Field<ToggleButton>(Claude, "_scopeToggle");
        var noticeText = UiStrings.Chat_FileOnly_Notice.Replace("{name}", chat.Project.DisplayName, StringComparison.Ordinal);
        var notice = ShotKit.All<SelectableTextBlock>(chat.Messages(Claude)).First(t => t.Text == noticeText);
        // ① 지금 그림만 ② 편집 범위 안내 ③ 거절된 새 그림 만들기(화면 위에서 아래 순서 — 두 줄이 붙어 있어 번호를 왼쪽 빈자리에 단다).
        WidenGutter(k, chat, Claude);
        var w = chat.Window;
        k.Save(w, marks: [toggle, Room(notice, w, left: Gutter - 2, up: 10), Room(ToolLine(chat, Claude, "new_project"), w, left: Gutter - 2, up: 0)]);
    }

    [Shot("ai-locked")]
    public static void Locked(ShotKit k)
    {
        var main = k.Main(Sample(k), width: 1280, height: 780);
        var chat = AiChat.Open(k, main, Claude, 430, 560);
        var ctx = ShotKit.Get<EditorContext>(main, "_ctx");
        ctx.Structural(() => chat.Project.SetLayerProperties(0, locked: true));
        k.Settle(200);
        chat.Send(Claude, "Make the jacket red", new Script()
            .Think(330)
            .Tool("get_document")
            .Tool("replace_color", new JsonObject { ["from"] = "#2B2D4F", ["to"] = "#B33A3A", ["layer"] = 0 })
            .Text("**Layer 1** is locked, so I left it alone. Unlock it with the lock icon in the timeline and ask again.")
            .Done(0.009, 5200));
        chat.Main.Title = MainTitle;
        var at = new Point(1280 - 430 - 18, 150);
        // ① 잠겨 있어 거절된 줄 ② 타임라인 자물쇠.
        Composite(k, "ai-locked", chat.Main, [(chat.Window, at)], [ToolLine(chat, Claude, "replace_color"), LockRect(main, 0)]);
    }

    [Shot("ai-progress")]
    public static void Progress(ShotKit k)
    {
        var chat = OutlineChat(k, chatWidth: 470, chatHeight: 720);
        chat.Send(Claude, "Draw 4 walking frames", new Script()
            .Think(1200)
            .Tool("get_document")
            .Tool("add_frame", new JsonObject { ["duplicate"] = true })
            .Writing("draw_pixels", 3480)
            .Pause());
        chat.FakeElapsed(Claude, TimeSpan.FromSeconds(41), TimeSpan.FromSeconds(12));
        WidenGutter(k, chat, Claude);
        k.Save(chat.Window, marks: [Room(chat.Field<TextBlock>(Claude, "_thinking"), chat.Window, left: Gutter - 2, up: 0)]);
    }

    // ───────────── 편집 · 효과 · 참고 자료 ─────────────

    [Shot("ai-edits")]
    public static void Edits(ShotKit k)
    {
        var main = k.Main(Sample(k), width: 1280, height: 780);
        var chat = AiChat.Open(k, main, Claude, 430, 640);
        chat.Send(Claude, "Draw a staff with a blue gem on the right", StaffScript());
        FinishTimeline(k, main);
        chat.Main.Title = MainTitle;
        var w = chat.Window;
        var drawing = ToolLines(chat, Claude, "draw_shape").Concat(ToolLines(chat, Claude, "set_pixels")).Cast<Control>().ToArray();
        int staff = chat.Project.Layers.ToList().FindIndex(l => l.Name == "Staff");
        // ① 그림 보기 ② 그리기 도구 ③ 타임라인에 생긴 새 레이어(add_layer).
        Composite(k, "ai-edits", chat.Main, [(w, ChatAt)],
            [ToolLine(chat, Claude, "get_image"), InBack(Union(w, 1, drawing), ChatAt), LayerNameRect(main, staff)]);
    }

    [Shot("ai-effects")]
    public static void Effects(ShotKit k)
    {
        var chat = OutlineChat(k, 1280, 780, 430, 640);
        var main = chat.Main;
        main.Title = MainTitle;
        // 타임라인의 효과 줄을 펼쳐 AI가 넣은 외곽선 효과가 보이게 한다.
        var layer = chat.Project.Layers[0];
        if (layer.EffectsCollapsed) ShotKit.Call(ShotKit.Get<Control>(main, "_timeline"), "ToggleEffectRows", 0);
        k.Settle(100);
        FinishTimeline(k, main);
        var fxRows = GridRect(main, new Rect(1, LayerY(main, 0) + 36, 268, 16 * layer.Effects.Count));
        // ① 타임라인의 효과 줄 ② 부탁 ③ add_effect 도구 줄 ④ 이 단계 되돌리기(실행취소 한 단계).
        Composite(k, "ai-effects", main, [(chat.Window, ChatAt)],
            [fxRows, LastBubble(chat, Claude), ToolLine(chat, Claude, "add_effect"), StepButton(chat, Claude)]);
    }

    [Shot("ai-references")]
    public static void References(ShotKit k)
    {
        var main = k.Main(Sample(k));
        var chat = AiChat.Open(k, main, Claude);
        // 지금 캔버스를 설명과 함께 넣고(AI 도구 그대로), 앱 밖에서 넣은 것처럼 설명 없는 그림 하나를 폴더에 둔다.
        chat.Mcp.Tools.Call("add_reference", new JsonObject
        {
            ["title"] = "Wolf-eared adventurer",
            ["description"] = "49×98 chibi character. 1px dark outline, light from the top left, silver hair in 3 shades.",
            ["tags"] = new JsonArray("character", "wolf", "chibi"),
            ["kind"] = "character",
        });
        var lib = ((IAiHost)main).References;
        PngIO.Save(Wolf(chat).Crop(new System.Drawing.Rectangle(0, 0, 49, 46)), Path.Combine(lib.Root, "wolf-head.png"));
        chat.Window.Close();
        k.Settle(200);
        var wolfId = lib.AllItems.First(i => i.Title == "Wolf-eared adventurer").Id;
        // 도구 창(모달 아님): 명령으로 띄우고 바로 찍는다.
        k.Command("AI/참고 자료");
        k.Settle(800);
        var w = k.Windows.OfType<ReferenceManagerWindow>().Single();
        w.Height = 880;
        ShotKit.Call(w, "Select", wolfId);
        k.Settle(800);
        var detail = ShotKit.Get<Border>(w, "_detail");
        var preview = (Control)ShotKit.Get<Control>(w, "_preview").GetVisualParent()!;
        var fields = Union(w, 2, ShotKit.Get<Control>(w, "_title"), ShotKit.Get<Control>(w, "_kind"));
        var swatches = ShotKit.All<WrapPanel>(detail).First();
        var kindFilter = ShotKit.Get<ComboBox>(w, "_kindFilter");
        // ①~⑥ 창의 칸(본문 순서) ⑦~⑫ 단추(표 순서) ⑬ 설명 없음 알림.
        k.Save(w, marks:
        [
            ShotKit.Get<Control>(w, "_list"),
            ShotKit.Get<Control>(w, "_search"),
            kindFilter,
            preview,
            fields,
            swatches,
            VisibleButton(w, UiStrings.Refs_AddFiles),
            VisibleButton(w, UiStrings.Refs_AddCanvas),
            VisibleButton(detail, UiStrings.Refs_Delete),
            VisibleButton(w, UiStrings.Refs_OpenFolder),
            VisibleButton(w, UiStrings.Refs_EditRules),
            VisibleButton(w, UiStrings.Refs_CaptionWithAi),
            ShotKit.Get<Control>(w, "_notice"),
        ]);

        // ③ 종류 거르기 목록을 펼친 모습.
        kindFilter.IsDropDownOpen = true;
        k.Settle(500);
        var lastKind = (Control)kindFilter.ContainerFromIndex(kindFilter.ItemCount - 1)!;
        var side = ShotKit.Bounds(kindFilter, w);
        k.Save(w, "ai-references-2", crop: new Rect(0, side.Y - 70, side.Right + 160, PopupRect(lastKind, w).Bottom + 16 - (side.Y - 70)),
            marks: [kindFilter], firstNumber: 3);
        kindFilter.IsDropDownOpen = false;
        k.Settle(200);
        w.Close();
        k.Settle(200);

        // ⑭~⑱ 환경 설정 > AI 참고 자료(폴더 칸은 시험용 임시 폴더 대신 보통 위치를 보인다).
        k.Dialog(() => k.Command("편집/환경 설정"), d =>
        {
            ShotKit.Find<TabView>(d).SelectedIndex = 3;
            ShotKit.Pump(300);
            var box = ShotKit.Get<TextBox>(d, "_refsFolderBox");
            box.Text = @"%AppData%\Gpixel\References";
            ShotKit.Pump(300);
            var builtIn = ShotKit.Get<CheckBox>(d, "_refsExamples");
            k.Save(d, "ai-references-3", marks:
            [
                Room(box, d, left: 14, up: 0),
                VisibleButton(d, UiStrings.Refs_Prefs_ChangeFolder),
                VisibleButton(d, UiStrings.Refs_Prefs_DefaultFolder),
                VisibleButton(d, UiStrings.Refs_OpenFolder),
                Room(builtIn, d, left: 16, up: 0),
            ], firstNumber: 14);
        });
    }

    // ───────────── 되돌리기 · 마지막 메시지 · 문제 ─────────────

    [Shot("ai-undo")]
    public static void Undo(ShotKit k)
    {
        var chat = OutlineChat(k, 1280, 780, 430, 640);
        var button = StepButton(chat, Claude);
        var w = chat.Window;
        var step = (Panel)button.GetVisualParent()!;
        var snapshot = step.Children[0];
        var bubble = (Control)LastActions(chat, Claude).GetVisualParent()!;
        // ① 결과 그림 ② 이 단계 되돌리기.
        k.Save(w, crop: Union(w, 10, bubble, step), titleBar: false, marks: [snapshot, button]);
        k.Click(button, settleMs: 400);
        chat.Main.Title = MainTitle;
        // ③ 다시 적용(누른 뒤 — 캔버스의 외곽선도 사라짐).
        Composite(k, "ai-undo-2", chat.Main, [(chat.Window, ChatAt)], [button], firstNumber: 3);
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
        var buttons = actions.Children.ToArray();
        // ① 연필(수정) ② 휴지통(삭제).
        k.Save(w, crop: crop, titleBar: false, marks: [Room(buttons[0], w, left: 16, up: 0), buttons[1]]);
        // ③~⑤ 그림을 바꾼 차례를 지울 때 묻는 창.
        k.Dialog(() => Raise((Button)buttons[1]), d =>
        {
            var choices = ShotKit.All<Button>(d).Where(b => b.IsEffectivelyVisible).Select(b => (Mark)b).ToList();
            k.Save(d, "ai-edit-last-2", marks: choices, firstNumber: 3);
        });
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
            .Text("I couldn't connect to the editor tools, so nothing was changed.")
            .Done(null, 9400));
        var error = ShotKit.All<SelectableTextBlock>(chat.Messages(Codex)).First(t => t.Text?.Contains("127.0.0.1", StringComparison.Ordinal) == true);
        // ① 연결 실패 빨간 줄 ② 시간만 보이는 끝 줄(Codex).
        WidenGutter(k, chat, Codex);
        var w = chat.Window;
        k.Save(w, marks: [Room(error, w, left: Gutter - 2, up: 0), Room(CostLine(chat, Codex), w, left: Gutter - 2, up: 0)]);
    }
}
