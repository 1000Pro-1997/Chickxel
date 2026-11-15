using Avalonia.Input.TextInput;

namespace Chickxel.App;

/// <summary>
/// 모든 창 공통(Windows): 창이 활성화될 때 포커스된 입력란의 입력기(IME) 연결을 다시 요청한다.
/// Avalonia(11.3 Win32)는 입력기를 활성 창 하나에 붙이는데, 창이 활성화되기 전에 입력란에 포커스를 주면(대화상자가 열리며 이름 칸에 포커스 등)
/// 입력기가 앞 창에 붙거나 꺼진 채로 남고, 포커스가 그대로라 다시 붙지 않아 그 칸에서 한글 조합이 되지 않는다.
/// 입력기 사용을 껐다 켜면 Avalonia가 입력기 연결을 지금 활성 창에 다시 붙인다(포커스·선택·LostFocus는 그대로).
/// 앱 시작 때 <see cref="Install"/> 한 번으로 이후 열리는 모든 창에 적용된다.
/// </summary>
public static class ImeRefresh
{
    /// <summary>앱 시작 때 한 번.</summary>
    public static void Install()
    {
        if (!OperatingSystem.IsWindows()) return;
        WindowBase.IsActiveProperty.Changed.AddClassHandler<Window, bool>((window, e) =>
        {
            // 창 활성화 처리(입력기를 이 창으로 옮김)가 끝난 뒤에
            if (e.NewValue.Value) Dispatcher.UIThread.Post(() => Refresh(window), DispatcherPriority.Input);
        });
    }

    private static void Refresh(Window window)
    {
        if (!window.IsActive || window.FocusManager?.GetFocusedElement() is not InputElement focused
            || !InputMethod.GetIsInputMethodEnabled(focused))
            return;
        InputMethod.SetIsInputMethodEnabled(focused, false);
        InputMethod.SetIsInputMethodEnabled(focused, true);
    }
}
