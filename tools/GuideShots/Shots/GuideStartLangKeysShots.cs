using Avalonia;
using Avalonia.Controls;
using Chickxel.App;
using Chickxel.App.Localization;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>시작하기 장의 "도움말 보기(F1)": 도움말 메뉴, 도움말을 고르는 동안의 상태 줄 안내, 사용 가이드 창(검색 중).</summary>
internal static class GuideStartLangKeysShots
{
    // ① 화면에서 도움말 선택(F1) ② 사용 가이드… — 도움말 메뉴.
    // ③ 도움말을 고르는 동안 상태 줄의 안내.
    // ④ 메인이 아닌 창(환경 설정)에서 고르는 동안 창 위쪽의 안내 띠.
    // ⑤ 검색 칸 ⑥ 검색 결과(검색하지 않으면 목차) ⑦ 고른 쪽 ⑧ 홈페이지에서 열기 ⑨ 개발 후원하기 — 사용 가이드 창.
    [Shot("start-help")]
    public static void Help(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        CleanTitle(main);

        var help = StartMenus.Open(k, main, 6);
        var pick = StartMenus.Item(help, UiStrings.Help_SelectTarget);
        var guide = StartMenus.Item(help, MainStrings.Menu_Help_Guide.TrimEnd('…'));
        k.Save(main, "start-help", crop: StartMenus.Crop(main, help), marks: [StartMenus.PopupRect(pick, main), StartMenus.PopupRect(guide, main)]);
        k.Key(main, Avalonia.Input.Key.Escape);
        k.Key(main, Avalonia.Input.Key.Escape);
        k.Settle(200);

        // 도움말 고르기: 상태 줄에 안내가 뜬다(마우스 커서는 그림에 찍히지 않음).
        HelpPicker.Toggle(main);
        k.Settle(300);
        var status = ShotKit.Get<Control>(main, "_helpStatus");
        var message = (TextBlock)ShotKit.FindText(status, UiStrings.Help_SelectHint);
        var s = ShotKit.Bounds(status, main);
        var m = ShotKit.Bounds(message, main);
        var formatted = new Avalonia.Media.FormattedText(message.Text!, System.Globalization.CultureInfo.CurrentCulture, Avalonia.Media.FlowDirection.LeftToRight,
            new Avalonia.Media.Typeface(message.FontFamily, message.FontStyle, message.FontWeight), message.FontSize, null);
        var hint = new Rect(m.X - 6, m.Y - 2, formatted.Width + 12, m.Height + 4);
        k.Save(main, "start-help-2", crop: new Rect(0, s.Y - 40, Math.Min(main.ClientSize.Width, hint.Right + 40), s.Height + 40),
            marks: [hint], firstNumber: 3);
        HelpPicker.Stop();
        k.Settle(200);

        // 따로 뜬 창(환경 설정)에서 고르기: 상태 표시줄 대신 창 위쪽 안내 띠.
        k.Dialog(() => k.Command("편집/환경 설정…"), d =>
        {
            HelpPicker.Toggle(d);
            ShotKit.Pump(300);
            var session = typeof(HelpPicker).GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)
                ?? throw new InvalidOperationException("help picking did not start");
            var banner = ShotKit.Get<Control>(session, "_banner");
            k.Save(d, "start-help-4", crop: new Rect(0, 0, d.ClientSize.Width, ShotKit.Bounds(banner, d).Bottom + 120), marks: [banner], firstNumber: 4);
            HelpPicker.Stop();
            ShotKit.Pump(100);
        });

        // 사용 가이드 창: 검색어를 넣은 모습.
        var w = HelpWindow.Open(main);
        w.ShowPage("start-help");
        w.Width = 1000;
        w.Height = 640;
        k.Settle(500);
        var query = ShotKit.Get<TextBox>(w, "_query");
        query.Text = UiLanguage.Current == UiLanguage.Korean ? "피벗" : "pivot";
        ShotKit.Call(w, "RunSearch");
        ShotKit.Call(w, "Select", 0);
        k.Settle(800);
        var side = ShotKit.Get<Control>(w, "_sideScroll");
        var page = ShotKit.Get<Control>(w, "_pageScroll");
        k.Save(w, "start-help-3", firstNumber: 5, marks:
        [
            query,
            side,
            page,
            ShotKit.FindText(w, UiStrings.Help_OpenWebsite, ancestorControl: true),
            ShotKit.FindText(w, UiStrings.Help_Support, ancestorControl: true),
        ]);
        w.Close();
    }
}
