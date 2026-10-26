using Avalonia;
using Avalonia.Controls;
using Gpixel.App;
using Gpixel.App.Localization;

namespace GuideShots;

internal static class LanguageShots
{
    /// <summary>환경 설정 > 일반 탭의 언어 부분(제목 ~ 안내 글)을 강조와 함께 찍는다.</summary>
    private static void LanguageArea(ShotKit k, string name, bool openList, Func<Window, IEnumerable<Control>> marks)
    {
        k.Main(k.CopySample());
        ShortcutsShots.Preferences(k, 0, w =>
        {
            var combo = ShotKit.Get<ComboBox>(w, "_language");
            var note = ShotKit.FindText(w, UiStrings.Language_Note);
            var title = ShotKit.All<TextBlock>(w).First(t => t.Text == UiStrings.Language_Title && t.FontWeight == Avalonia.Media.FontWeight.Bold);
            var folder = ShotKit.FindText(w, UiStrings.Language_OpenFolder, ancestorControl: true);
            var area = new[] { title, combo, folder, note }.Select(c => ShotKit.Bounds(c, w)).Aggregate((a, b) => a.Union(b)).Inflate(10);
            if (openList)
            {
                combo.IsDropDownOpen = true;
                ShotKit.Pump(500);
            }
            k.Save(w, name, crop: area, marks: marks(w));
        });
    }

    // 화면 언어 바꾸기: 화면 언어 목록을 펼친 모습.
    [Shot("language-change")]
    public static void Change(ShotKit k)
        => LanguageArea(k, "language-change", openList: true, w => [ShotKit.Get<ComboBox>(w, "_language")]);

    // 새 언어 만들기: 번역 CSV로 내보내기 · CSV로 언어 추가.
    [Shot("language-make")]
    public static void Make(ShotKit k)
        => LanguageArea(k, "language-make", openList: false, w =>
        [
            ShotKit.FindText(w, UiStrings.Language_Export, ancestorControl: true),
            ShotKit.FindText(w, UiStrings.Language_Import, ancestorControl: true),
        ]);

    // 내장 문구 고치기와 지우기: 언어 폴더 열기.
    [Shot("language-fix")]
    public static void Fix(ShotKit k)
        => LanguageArea(k, "language-fix", openList: false, w => [ShotKit.FindText(w, UiStrings.Language_OpenFolder, ancestorControl: true)]);
}
