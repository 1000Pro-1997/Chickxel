using Avalonia;
using Avalonia.Controls;
using Gpixel.App;
using Gpixel.App.Localization;

namespace GuideShots;

internal static class ShortcutsShots
{
    /// <summary>환경 설정 창을 열어 <paramref name="tab"/>번째 탭을 고른 뒤 <paramref name="withWindow"/>(모달 루프 안 — Pump로 기다림).</summary>
    public static void Preferences(ShotKit k, int tab, Action<Window> withWindow)
    {
        k.Dialog(() => k.Command("편집/환경 설정"), w =>
        {
            var tabs = ShotKit.Find<TabView>(w);
            tabs.SelectedIndex = tab;
            ShotKit.Pump(400);
            withWindow(w);
        });
    }

    // 단축키 바꾸기: 환경 설정 > 단축키 탭, 한 줄은 "키를 누르세요" 상태(바꾸기를 누른 뒤).
    [Shot("shortcuts-customize")]
    public static void Customize(ShotKit k)
    {
        k.Main(k.CopySample());
        Preferences(k, 1, w =>
        {
            ShotKit.Call(w, "StartCapture", "파일/저장(+ 시트 내보내기)", true);
            ShotKit.Pump(400);
            var rows = ShotKit.Get<StackPanel>(w, "_rows");
            var capturing = rows.Children.OfType<Grid>().First(g => ShotKit.All<TextBlock>(g).Any(t => t.Foreground == AppTheme.AccentBrush));
            var search = ShotKit.Get<TextBox>(w, "_search");
            k.Save(w, marks: [capturing, search]);
        });
        // 도움말 > 단축키 목록(기본값).
        k.Dialog(() => k.Command("도움말/단축키 목록"), w => k.Save(w, "shortcuts-customize-2"));
    }
}
