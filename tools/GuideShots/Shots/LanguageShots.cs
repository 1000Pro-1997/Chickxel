using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Chickxel.App;
using Chickxel.App.Localization;

namespace GuideShots;

/// <summary>언어: 환경 설정 > 일반 탭 맨 위의 언어 부분, 다시 시작 물음, CSV로 더한 언어.</summary>
internal static class LanguageShots
{
    private static ComboBox Combo(Window w) => ShotKit.Get<ComboBox>(w, "_language");

    private static Control Title(Window w)
        => ShotKit.All<TextBlock>(w).First(t => t.Text == UiStrings.Language_Title && t.FontWeight == FontWeight.Bold);

    private static Control Button(Window w, string text) => ShotKit.FindText(w, text, ancestorControl: true);

    /// <summary>언어 부분(제목 ~ 안내 글, 넣은 결과 글이 있으면 그것까지)을 담는 자르기 영역.</summary>
    private static Rect Area(Window w)
    {
        var status = ShotKit.Get<TextBlock>(w, "_languageStatus");
        var parts = new List<Control> { Title(w), Combo(w), Button(w, UiStrings.Language_OpenFolder), ShotKit.FindText(w, UiStrings.Language_Note) };
        if (!string.IsNullOrEmpty(status.Text)) parts.Add(status);
        return parts.Select(c => ShotKit.Bounds(c, w)).Aggregate((a, b) => a.Union(b)).Inflate(10);
    }

    // 화면 언어 바꾸기: ① 편집 > 환경 설정…, ② 언어 ③ 화면 언어 목록 ④ 확인, 다시 시작 물음 ⑤ 지금 다시 시작 ⑥ 나중에.
    [Shot("language-change")]
    public static void Change(ShotKit k)
    {
        var main = k.Main(k.CopySample());
        var edit = StartMenus.Open(k, main, 1);
        var prefs = StartMenus.Item(edit, MainStrings.Menu_Edit_ShowPreferences_131.TrimEnd('…'));
        k.Save(main, crop: StartMenus.Crop(main, edit), marks: [StartMenus.PopupRect(prefs, main)]);
        k.Key(main, Avalonia.Input.Key.Escape);
        k.Key(main, Avalonia.Input.Key.Escape);

        ShortcutsShots.Preferences(k, 0, w =>
        {
            StartShots.Scrub(w, StartShots.Changelog().Heads[0].Version.ToString());
            // ② 언어 부분 전체(번호가 "Language" 글자를 가리지 않게 위를 조금 넓힘) ③ 펼친 화면 언어 목록 ④ 확인.
            var a = Area(w);
            var section = new Rect(a.X + 4, a.Y - 4, a.Width - 8, a.Height + 4);
            var combo = Combo(w);
            combo.IsDropDownOpen = true;
            ShotKit.Pump(500);
            k.Save(w, "language-change-2", firstNumber: 2, marks: [section, combo, ShotKit.Get<Button>(w, "OkButton")]);
            combo.IsDropDownOpen = false;
            ShotKit.Pump(200);
        });

        // 확인 뒤 묻는 창(직접 띄워 찍고 닫음 — 닫으면 "나중에"와 같아 다시 시작하지 않는다).
        k.Dialog(() => Ui.Ask(main, UiStrings.Language_RestartNotice, "Chickxel", MessageButtons.YesNo, MessageIcon.Question,
                UiStrings.Language_RestartNow, UiStrings.Language_RestartLater),
            w => k.Save(w, "language-change-3", firstNumber: 5, marks: [ShotKit.Get<Button>(w, "OkButton"), ShotKit.Get<Button>(w, "CancelButtonControl")]));
    }

    // 새 언어 만들기: ① 번역 CSV로 내보내기 ② CSV로 언어 추가, 넣은 뒤 ③ 목록에 생긴 언어.
    [Shot("language-make")]
    public static void Make(ShotKit k)
    {
        k.Main(k.CopySample());
        ShortcutsShots.Preferences(k, 0, w =>
        {
            k.Save(w, crop: Area(w), marks: [Button(w, UiStrings.Language_Export), Button(w, UiStrings.Language_Import)]);

            // "CSV로 언어 추가…"로 일본어 열을 더한 CSV를 넣은 뒤의 모습(파일 고르기 창 대신 같은 처리를 직접 부름).
            var csv = Path.Combine(k.WorkDir, "chickxel-translations.csv");
            File.WriteAllText(csv, "key,ja\n#name,日本語\nUiStrings.Language_Title,言語\nUiStrings.Language_Export,翻訳CSVを書き出す…\nUiStrings.Language_Import,CSVから言語を追加…\n");
            var result = UiLanguage.ImportCsv(csv);
            ShotKit.Call(w, "FillLanguages", "ja");
            ShotKit.Get<TextBlock>(w, "_languageStatus").Text = string.Join("\n", result.Languages.Select(l => $"{l.Name} ({l.Code}): added {l.Count} strings."));
            ShotKit.Pump(300);
            var combo = Combo(w);
            combo.IsDropDownOpen = true;
            ShotKit.Pump(500);
            var item = (Control)combo.ContainerFromIndex(combo.SelectedIndex)!;
            var itemRect = StartMenus.PopupRect(item, w);
            k.Save(w, "language-make-2", crop: Area(w).Union(itemRect.Inflate(10)), marks: [itemRect], firstNumber: 3);
            combo.IsDropDownOpen = false;
            ShotKit.Pump(200);
        });
    }

    // 내장 문구 고치기와 지우기: ① 언어 폴더 열기.
    [Shot("language-fix")]
    public static void Fix(ShotKit k)
    {
        k.Main(k.CopySample());
        ShortcutsShots.Preferences(k, 0, w => k.Save(w, crop: Area(w), marks: [Button(w, UiStrings.Language_OpenFolder)]));
    }
}
