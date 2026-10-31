using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Chickxel.App;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>단축키: 환경 설정 > 단축키 탭, 겹치는 키 물음, 메뉴에 보이는 키, 도움말 > 단축키 목록, 표마다 그 키가 보이는 메뉴.</summary>
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

    private const string GifId = "파일/GIF 애니메이션으로 내보내기…";
    private const string GifName = "Export as animated GIF…";

    // 단축키 바꾸기: 단축키 탭 ①~⑩, 겹치는 키 물음 ⑪, 바뀐 키가 보이는 메뉴 ⑫, 도움말 > 단축키 목록 ⑬, 목록 창.
    [Shot("shortcuts-customize")]
    public static void Customize(ShotKit k)
    {
        var main = Open(k);
        CleanTitle(main);
        Preferences(k, 1, w =>
        {
            var search = ShotKit.Get<TextBox>(w, "_search");
            search.Text = "export";
            ShotKit.Pump(300);
            ShotKit.Call(w, "StartCapture", GifId, true);
            ShotKit.Pump(400);
            var rows = ShotKit.Get<StackPanel>(w, "_rows");
            var row = rows.Children.OfType<Grid>().First(g => g.Children.OfType<TextBlock>().Any(t => t.Text == GifName));
            var buttons = ShotKit.All<Button>(row).ToList();
            k.Save(w, marks:
            [
                PadLeft(ShotKit.FindText(w, "Shortcuts", ancestorControl: true), w, 10),
                buttons[0], buttons[1], buttons[2], buttons[3],
                PadLeft(row.Children[2], w, 12),
                search,
                ShotKit.FindText(w, "Reset all", ancestorControl: true),
                ShotKit.FindText(w, "Export (JSON)…", ancestorControl: true),
                ShotKit.FindText(w, "Import (JSON)…", ancestorControl: true),
            ]);

            // 다른 명령(내보내기…)이 쓰는 Ctrl+E를 누르면 그 명령에서 빼도 되는지 묻는다(찍고 닫음 = 아니요 — 그대로 둠).
            // 화면 없는 창은 포커스를 받지 않아 키 입력이 메인 창으로 가므로 환경 설정 창에 키 이벤트를 바로 보낸다.
            k.Dialog(() => w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.E, KeyModifiers = KeyModifiers.Control }), ask =>
            {
                var message = ShotKit.All<TextBlock>(ask).First(t => t.Text?.Contains("Ctrl+E", StringComparison.Ordinal) == true);
                k.Save(ask, "shortcuts-customize-2", marks: [message], firstNumber: 11);
            });
        });

        // 바꾼 키(GIF로 내보내기 = Ctrl+Alt+G)가 바로 보이는 파일 메뉴.
        App.Settings.Shortcuts = new() { [GifId] = ["Ctrl+Alt+G"] };
        ShotKit.Call(main, "ApplyShortcuts");
        k.Settle(200);
        var file = StartMenus.Open(k, main, 0);
        var gif = StartMenus.Item(file, GifName.TrimEnd('…'));
        var key = StartMenus.PopupRect(StartMenus.Key(gif)!, main);
        k.Save(main, "shortcuts-customize-3", crop: StartMenus.Crop(main, file), marks: [new Rect(key.X - 14, key.Y, key.Width + 14, key.Height)], firstNumber: 12);
        CloseMenu(k, main);

        // 도움말 > 단축키 목록, 그 창(기본값 목록).
        var help = StartMenus.Open(k, main, 6);
        var list = StartMenus.Item(help, "Shortcut list");
        k.Save(main, "shortcuts-customize-4", crop: StartMenus.Crop(main, help), marks: [StartMenus.PopupRect(list, main)], firstNumber: 13);
        CloseMenu(k, main);
        k.Dialog(() => k.Command("도움말/단축키 목록"), w => k.Save(w, "shortcuts-customize-5"));
    }

    /// <summary>번호 동그라미가 글자를 가리지 않게 왼쪽을 <paramref name="pad"/>만큼 넓힌 창 좌표 사각형.</summary>
    private static Rect PadLeft(Control c, Window w, double pad)
    {
        var r = ShotKit.Bounds(c, w);
        return new Rect(r.X - pad, r.Y, r.Width + pad, r.Height);
    }

    private static void CloseMenu(ShotKit k, MainForm main)
    {
        k.Key(main, Key.Escape);
        k.Key(main, Key.Escape);
    }

    /// <summary>표 쪽 그림: <paramref name="menuIndex"/>번째 위 메뉴를 펼쳐 표에 든 명령들의 단축키 열(①, 묶음이 둘이면 ①②)을 표시한다.</summary>
    private static void MenuShot(ShotKit k, int menuIndex, params (string First, string Last)[] groups)
    {
        var main = Open(k);
        CleanTitle(main);
        var menu = StartMenus.Open(k, main, menuIndex);
        var marks = groups.Select(g => (Mark)StartMenus.Keys(menu, StartMenus.Item(menu, g.First), StartMenus.Item(menu, g.Last), main)).ToList();
        k.Save(main, crop: StartMenus.Crop(main, menu), marks: marks);
        CloseMenu(k, main);
    }

    [Shot("shortcuts-file")]
    public static void File(ShotKit k) => MenuShot(k, 0, ("New project", "Close"));

    [Shot("shortcuts-edit")]
    public static void Edit(ShotKit k) => MenuShot(k, 1, ("Undo", "Deselect"));

    [Shot("shortcuts-transform")]
    public static void Transform(ShotKit k) => MenuShot(k, 1, ("Flip horizontal", "Canvas size"));

    [Shot("shortcuts-tools")]
    public static void Tools(ShotKit k) => MenuShot(k, 4, ("Pencil", "9-slice"), ("Filled shapes", "Primary"));

    [Shot("shortcuts-view")]
    public static void View(ShotKit k) => MenuShot(k, 2, ("Zoom in", "Onion skin"));

    [Shot("shortcuts-timeline")]
    public static void Timeline(ShotKit k) => MenuShot(k, 3, ("Play / stop", "Next skin"));
}
