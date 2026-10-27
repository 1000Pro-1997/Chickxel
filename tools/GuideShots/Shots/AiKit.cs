using System.Collections;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Gpixel.App;
using Gpixel.App.Ai;
using Gpixel.Core.Model;

namespace GuideShots;

/// <summary>
/// AI 채팅 샷 도우미: 진짜 채팅 창(<see cref="ChatWindow"/>)을 CLI 없이 대본 세션(<see cref="ScriptSession"/>)으로 띄우고,
/// 대본의 도구 호출은 편집기의 진짜 AI 도구(<see cref="PixelTools.Call"/>)로 실행한다(모델 요청·외부 프로세스 없음).
/// </summary>
internal sealed class AiChat
{
    public const string Claude = "claude", Codex = "codex";

    public ChatWindow Window { get; }
    public MainForm Main { get; }
    public McpServer Mcp { get; }
    public Dictionary<string, ScriptSession> Sessions { get; } = new();
    private readonly ShotKit _k;

    private AiChat(ShotKit k, MainForm main, ChatWindow window, McpServer mcp)
    {
        _k = k;
        Main = main;
        Window = window;
        Mcp = mcp;
    }

    /// <summary>편집기 MCP 서버를 만들어(메인 창의 EnsureMcp) 채팅 창을 대본 세션으로 띄운다. <paramref name="before"/>는 창을 만들기 전 설정·보관소 준비.</summary>
    public static AiChat Open(ShotKit k, MainForm main, string provider = Claude, int width = 470, int height = 720, Action<AppSettings>? before = null)
    {
        if (ShotKit.Call(main, "EnsureMcp", main) is not true) throw new InvalidOperationException("MCP server did not start");
        // 샷이 그림을 고치므로 닫을 때 "저장할까요?"(모달)를 묻지 않게 한다(묻으면 샷 정리가 멈춤).
        ShotKit.Set(main, "_closeConfirmed", true);
        var mcp = ShotKit.Get<McpServer>(main, "_mcp");
        var settings = App.Settings;
        settings.ChatProvider = provider;
        settings.ChatWidth = width;
        settings.ChatHeight = height;
        settings.ChatX = -1;
        settings.ChatY = -1;
        before?.Invoke(settings);
        var window = new ChatWindow(ShotKit.Get<EditorContext>(main, "_ctx"), mcp, settings);
        var chat = new AiChat(k, main, window, mcp);
        ShotKit.Call(window, "ShowChatWithoutCli", new Func<AiProvider, IChatSession>(p =>
        {
            var s = new ScriptSession(mcp.Tools);
            chat.Sessions[p.Id] = s;
            return s;
        }));
        ShotKit.Set(main, "_chat", window);
        window.Show(main);
        k.Settle(500);
        // 머리줄 상태 글: 실제로는 로그인한 계정이 보인다(예시 계정).
        foreach (var id in new[] { Claude, Codex })
            ShotKit.Call(chat.Tab(id), "SetHeader", id == Claude ? "you@example.com · Max" : "you@example.com · Plus", true);
        k.Settle(100);
        return chat;
    }

    public object Tab(string provider) => ShotKit.Get<IList>(Window, "_tabs").Cast<object>()
        .First(t => ShotKit.Get<AiProvider>(t, "Provider").Id == provider);

    public T Field<T>(string provider, string name) => ShotKit.Get<T>(Tab(provider), name);

    public TextBox Input(string provider) => Field<TextBox>(provider, "_input");
    public StackPanel Messages(string provider) => Field<StackPanel>(provider, "_messages");

    public void Show(string provider)
    {
        Window.SelectProvider(provider);
        _k.Settle(200);
    }

    /// <summary>입력란에 글을 넣고 보낸 뒤 대본이 멈추거나 끝날 때까지 돌린다.</summary>
    public void Send(string provider, string text, Script script)
    {
        var tab = Tab(provider);
        Input(provider).Text = "";
        ShotKit.Call(tab, "Prefill", text);
        _k.Settle(100);
        Sessions[provider].Next.Enqueue(script);
        ShotKit.Call(tab, "Send");
        _k.Settle(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Sessions[provider].Busy && sw.ElapsedMilliseconds < 10000) _k.Settle(50);
        _k.Settle(300);
    }

    /// <summary>멈춘 대본을 이어 돌린다(<paramref name="more"/>가 있으면 남은 단계 뒤에 더해서).</summary>
    public void Resume(string provider, Script? more = null)
    {
        Sessions[provider].Resume(more);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Sessions[provider].Busy && sw.ElapsedMilliseconds < 10000) _k.Settle(50);
        _k.Settle(300);
    }

    /// <summary>진행 줄의 시간을 그럴듯하게(전체·지금 단계가 이만큼 지난 것처럼) 맞춘다.</summary>
    public void FakeElapsed(string provider, TimeSpan total, TimeSpan phase)
    {
        var tab = Tab(provider);
        ShotKit.Set(tab, "_turnStart", DateTime.UtcNow - total);
        ShotKit.Set(tab, "_phaseStart", DateTime.UtcNow - phase);
        ShotKit.Call(tab, "UpdateProgress");
        _k.Settle(100);
    }

    /// <summary>열린 문서.</summary>
    public SpriteProject Project => ShotKit.Get<EditorContext>(Main, "_ctx").Project!;

    // ───────────── 여러 창을 한 그림으로 ─────────────

    /// <summary>
    /// 창 여러 개를 화면에 놓인 모습처럼 한 장으로 저장한다: 첫 창이 바탕(제목 줄 포함), 나머지는 <paramref name="placed"/> 위치에 그림자와 함께.
    /// <paramref name="marks"/>는 어느 창의 컨트롤이든(그 창이 놓인 자리로 옮김) 또는 바탕 창 좌표 사각형(<see cref="InBack"/>으로 다른 창 좌표를 바꿀 수 있음)이고,
    /// 그 자리에 빨간 번호 테두리를 그린다(<paramref name="firstNumber"/>부터).
    /// </summary>
    public static string Composite(ShotKit k, string name, Window back, IReadOnlyList<(Window Window, Point At)> placed, IEnumerable<Mark>? marks = null, int firstNumber = 1)
    {
        var bar = ShotKit.TitleHeight;
        using var backImg = k.Capture(back);
        var size = new PixelSize(backImg.PixelSize.Width, backImg.PixelSize.Height + bar);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        var origins = new Dictionary<Window, Point> { [back] = new Point(0, bar) };
        using (var ctx = target.CreateDrawingContext())
        {
            DrawTitleBar(ctx, back.Title ?? "Gpixel", new Rect(0, 0, size.Width, bar));
            ctx.DrawImage(backImg, new Rect(0, bar, backImg.PixelSize.Width, backImg.PixelSize.Height));
            foreach (var (w, at) in placed)
            {
                using var img = k.Capture(w);
                var outer = new Rect(at.X, at.Y, img.PixelSize.Width, img.PixelSize.Height + bar);
                for (int i = 1; i <= 10; i++)
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(26 - i * 2), 0, 0, 0)), null, outer.Inflate(i).Translate(new Vector(2, 4)), 6, 6);
                DrawTitleBar(ctx, w.Title ?? "", new Rect(at.X, at.Y, img.PixelSize.Width, bar));
                ctx.DrawImage(img, new Rect(at.X, at.Y + bar, img.PixelSize.Width, img.PixelSize.Height));
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 100)), 1), outer.Deflate(0.5));
                origins[w] = new Point(at.X, at.Y + bar);
            }
            var rects = (marks ?? []).Select(m =>
            {
                if (m.Control is not { } c) return m.Rect.Translate((Vector)origins[back]);
                var top = (Window)TopLevel.GetTopLevel(c)!;
                return ShotKit.Bounds(c, top).Translate((Vector)origins[top]);
            });
            ShotKit.DrawMarks(ctx, rects, firstNumber, new Rect(0, 0, size.Width, size.Height));
        }
        return k.Write(target, name);
    }

    /// <summary><paramref name="at"/>에 놓인 창의 창 좌표 사각형을 <see cref="Composite"/>의 바탕 창 좌표로 바꾼다.</summary>
    public static Rect InBack(Rect r, Point at) => r.Translate(new Vector(at.X, at.Y));

    private static void DrawTitleBar(DrawingContext ctx, string title, Rect r)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 36)), r);
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(60, 60, 66)), new Rect(r.X, r.Bottom - 1, r.Width, 1));
        using (var stream = new MemoryStream(Icons.AppIconPng()))
        {
            using var icon = new Bitmap(stream);
            ctx.DrawImage(icon, new Rect(r.X + 10, r.Y + 8, 16, 16));
        }
        var text = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Malgun Gothic, Segoe UI"), 13, Brushes.Gainsboro);
        ctx.DrawText(text, new Point(r.X + 34, r.Y + (r.Height - text.Height) / 2));
        var pen = new Pen(Brushes.Gainsboro, 1);
        double x = r.Right - 128;
        ctx.DrawLine(pen, new Point(x + 15, r.Y + 16.5), new Point(x + 25, r.Y + 16.5));
        x += 46;
        ctx.DrawRectangle(null, pen, new Rect(x + 15.5, r.Y + 11.5, 9, 9));
        x += 46;
        ctx.DrawLine(pen, new Point(x + 15, r.Y + 11), new Point(x + 25, r.Y + 21));
        ctx.DrawLine(pen, new Point(x + 25, r.Y + 11), new Point(x + 15, r.Y + 21));
    }
}

/// <summary>대본 한 차례: 단계 목록(<see cref="Script.Pause"/>에서 멈춤).</summary>
internal sealed class Script
{
    public List<Action<ScriptSession>> Steps { get; } = [];
    /// <summary>멈춤 표시(이 단계에서 대본을 멈추고 <see cref="ScriptSession.Resume"/>을 기다림).</summary>
    public static readonly Action<ScriptSession> PauseMark = _ => { };

    public Script Think(int chars = 400) { Steps.Add(s => s.Block("thinking", null, chars)); return this; }

    /// <summary>진짜 AI 도구를 부른다(도구 줄 + 실행 + 결과).</summary>
    public Script Tool(string name, JsonObject? args = null) { Steps.Add(s => s.RunTool(name, args ?? new JsonObject())); return this; }

    /// <summary>도구 줄만(실행하지 않음 — 그림 생성처럼 밖에서 결과를 넣을 때).</summary>
    public Script ToolLine(string name, JsonObject? args = null) { Steps.Add(s => s.ToolUseOnly(name, args ?? new JsonObject())); return this; }

    public Script Writing(string tool, int chars) { Steps.Add(s => s.Block("tool_use", tool, chars)); return this; }

    public Script Text(string text) { Steps.Add(s => s.Answer(text)); return this; }

    public Script Do(Action action) { Steps.Add(_ => action()); return this; }

    public Script Problem(string text) { Steps.Add(s => s.RaiseProblem(text)); return this; }

    public Script Pause() { Steps.Add(PauseMark); return this; }

    public Script Done(double? cost, double ms) { Steps.Add(s => s.Complete(cost, ms)); return this; }
}

/// <summary>CLI 대신 대본을 돌리는 채팅 세션. 이벤트는 모두 UI 스레드에서(보낸 뒤 대기열에서) 낸다.</summary>
internal sealed class ScriptSession(PixelTools tools) : IChatSession
{
    private int _toolId;
    private readonly Queue<Action<ScriptSession>> _running = new();

    public Queue<Script> Next { get; } = new();
    public bool Busy { get; private set; }

    public string? Model { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? SessionId { get; private set; }
    public string? SessionDirectory => null;
    public bool IsTurnActive => Busy;

    // 대본은 글을 한 번에 보내므로(스트리밍 없음) 아래 일부 이벤트는 내지 않는다.
#pragma warning disable CS0067
    public event Action? TextStarted;
    public event Action<string>? TextDelta;
    public event Action<string, string?>? BlockStarted;
    public event Action<int>? BlockProgress;
    public event Action<string>? AssistantText;
    public event Action<ToolUseInfo>? ToolUse;
    public event Action<ToolResultInfo>? ToolResult;
    public event Action<TurnResult>? TurnCompleted;
    public event Action<ExitInfo>? Exited;
    public event Action<string>? Notice;
    public event Action<string>? McpProblem;
    public event Action<string>? ModelResolved;
#pragma warning restore CS0067

    public void Send(string text, IReadOnlyList<byte[]> pngImages)
    {
        SessionId ??= Guid.NewGuid().ToString("N");
        _running.Clear();
        if (Next.TryDequeue(out var script)) foreach (var s in script.Steps) _running.Enqueue(s);
        Busy = true;
        Dispatcher.UIThread.Post(Run, DispatcherPriority.Background);
    }

    public void Resume(Script? more = null)
    {
        foreach (var s in more?.Steps ?? []) _running.Enqueue(s);
        Busy = true;
        Dispatcher.UIThread.Post(Run, DispatcherPriority.Background);
    }

    private void Run()
    {
        while (_running.TryDequeue(out var step))
        {
            if (ReferenceEquals(step, Script.PauseMark))
            {
                Busy = false;
                return;
            }
            step(this);
        }
        Busy = false;
    }

    public void Block(string kind, string? tool, int chars)
    {
        BlockStarted?.Invoke(kind, tool);
        BlockProgress?.Invoke(chars);
    }

    public void RunTool(string name, JsonObject args)
    {
        var id = "t" + ++_toolId;
        BlockStarted?.Invoke("tool_use", name);
        ToolUse?.Invoke(new ToolUseInfo(id, name, (JsonObject)args.DeepClone()));
        var result = tools.Call(name, args);
        bool error = result["isError"]?.GetValue<bool>() == true;
        var text = result["content"] is JsonArray { Count: > 0 } c && c[0]?["text"]?.GetValue<string>() is { } t ? t : "";
        ToolResult?.Invoke(new ToolResultInfo(id, error, error ? text : ""));
    }

    public void ToolUseOnly(string name, JsonObject args)
    {
        var id = "t" + ++_toolId;
        BlockStarted?.Invoke("tool_use", name);
        ToolUse?.Invoke(new ToolUseInfo(id, name, args));
    }

    public void Answer(string text)
    {
        BlockStarted?.Invoke("text", null);
        AssistantText?.Invoke(text);
    }

    public void RaiseProblem(string text) => McpProblem?.Invoke(text);

    public void Complete(double? cost, double ms) => TurnCompleted?.Invoke(new TurnResult(false, "success", null, cost, ms));

    public void Cancel()
    {
        _running.Clear();
        Busy = false;
    }

    public void Restart() { }
    public void Reset() => SessionId = null;
    public void Resume(string sessionId, string? directory) => SessionId = sessionId;
    public void Dispose() => Cancel();
}
