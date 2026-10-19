namespace Gpixel.App;

/// <summary>
/// 지금 눌려 있는 키(Space로 화면 이동, H로 이전 프레임 보기 등). 창마다 <see cref="Attach"/>하면
/// 그 창 안 어디에 포커스가 있어도 키 누름/뗌을 기록하고, 창이 비활성화되면 모두 뗀 것으로 본다.
/// </summary>
public static class KeyState
{
    private static readonly HashSet<Key> Down = new();

    /// <summary>키가 지금 눌려 있는지(붙인 창 기준).</summary>
    public static bool IsDown(Key key) => Down.Contains(key);

    public static void Attach(Window window)
    {
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => Down.Add(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, (_, e) => Down.Remove(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Deactivated += (_, _) => Down.Clear();
    }
}
