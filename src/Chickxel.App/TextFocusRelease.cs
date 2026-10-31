using System.Runtime.CompilerServices;
using Avalonia.VisualTree;

namespace Chickxel.App;

/// <summary>
/// 모든 창 공통: 글자·숫자 입력란(<see cref="TextBox"/>, <see cref="NumericUpDown"/> 안쪽 입력란 포함)을 고치는 중에 그 밖을 누르면
/// 입력 상태를 끝낸다. 누른 곳이 스스로 포커스를 가져가지 않으면(패널 빈 곳·그림 등) 창마다 정한 곳(<see cref="SetFallback"/>,
/// 예: 메인 창 = 캔버스)으로, 없으면 포커스를 아예 비운다. 숫자 상자는 이때 값이 확정된다.
/// 앱 시작 때 <see cref="Install"/> 한 번으로 이후 열리는 모든 창(대화상자·도구 창 포함)에 붙는다.
/// </summary>
public static class TextFocusRelease
{
    private static readonly ConditionalWeakTable<TopLevel, Func<IInputElement?>> Fallbacks = new();
    private static readonly ConditionalWeakTable<TopLevel, object> Hooked = new();

    /// <summary>앱 시작 때 한 번.</summary>
    public static void Install() => Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Hook(w));

    /// <summary>이 창에서 입력란 밖을 눌렀을 때 포커스를 줄 곳(기본: 포커스 비우기).</summary>
    public static void SetFallback(TopLevel top, Func<IInputElement?> fallback)
    {
        Fallbacks.AddOrUpdate(top, fallback);
        Hook(top);
    }

    private static void Hook(TopLevel top)
    {
        if (Hooked.TryGetValue(top, out _)) return;
        Hooked.Add(top, new object());
        top.AddHandler(InputElement.PointerPressedEvent, (_, e) => OnPressed(top, e), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPressed(TopLevel top, PointerPressedEventArgs e)
    {
        if (top.FocusManager?.GetFocusedElement() is not TextBox box) return;
        Control owner = box.FindAncestorOfType<NumericUpDown>() is { } num ? num : box;
        if (e.Source is Visual v && (ReferenceEquals(v, owner) || owner.IsVisualAncestorOf(v))) return;
        // 누른 곳이 스스로 포커스를 가져가는지 본 뒤에(단추·다른 입력란·목록이면 그대로 둠).
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(top.FocusManager?.GetFocusedElement(), box)) return;
            bool moved = Fallbacks.TryGetValue(top, out var fallback) && fallback() is { } target && target.Focus();
            if (!moved || ReferenceEquals(top.FocusManager?.GetFocusedElement(), box)) top.FocusManager?.ClearFocus();
        });
    }
}
