using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Gpixel.App;
using static GuideShots.ScreenDemo;
using static GuideShots.ScreenShot;

namespace GuideShots;

/// <summary>화면 구성: 내보내기 창, GIF 애니메이션으로 내보내기 창.</summary>
internal static class ScreenDialogShots
{
    /// <summary>
    /// 내보내기 창: 1 = ① 내보내기 단추 ② 내보내기 탭 ③ Import Settings 탭 ④ 왼쪽 설정 ⑤ 오른쪽 미리보기 ⑥ 나눔줄,
    /// 2 = 묶음(⑦ 공통 ⑧ 파일 ⑨ 프레임 시트 ⑩ 부위 아틀라스 ⑪ 고급), 3 = 공통 묶음(⑫ 칸 수 ⑬ 정렬 ⑭ 따로 파일 ⑮ 칸 이름 ⑯ 스킨 고르기),
    /// 4 = ⑰ 프리셋 팝업, 5 = ⑱ Import Settings 탭 설정.
    /// </summary>
    [Shot("screen-export")]
    public static void Export(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/내보내기…"), w =>
        {
            ShotKit.Pump(400);
            // 프레임 시트와 부위 아틀라스를 둘 다 내보내게(두 묶음과 두 미리보기가 다 보이게).
            k.Click(ShotKit.FindText(w, "Both"));
            ShotKit.Pump(700);
            var tabs = ShotKit.Get<TabView>(w, "_tabs");
            var columns = ShotKit.Get<Grid>(w, "_columns");
            Control Column(int i) => columns.Children.First(c => Grid.GetColumn(c) == i);
            var exportButton = ShotKit.All<Button>(w).Last(b => b.IsEffectivelyVisible && b.Content is "Export");
            Rect Tab(string text) => ShotKit.Bounds(ShotKit.FindText(tabs.Strip, text), w).Inflate(new Thickness(8, 4));
            k.Save(w, marks:
            [
                exportButton, Tab("Export"), Tab("Import Settings"),
                ShotKit.Bounds(Column(0), w).Deflate(4), ShotKit.Bounds(Column(2), w).Deflate(4), ShotKit.Bounds(Column(1), w).Inflate(new Thickness(2, -40, 2, -40)),
            ]);

            // 2: 묶음을 모두 접은 왼쪽 칸
            var sections = ShotKit.All<Expander>(Column(0)).Where(e => e.IsEffectivelyVisible && e.Header is TextBlock).ToList();
            foreach (var e in sections) e.IsExpanded = false;
            ShotKit.Pump(500);
            var left = ShotKit.Bounds(Column(0), w);
            var stack = sections.Select(e => ShotKit.Bounds(e, w)).Aggregate((a, b) => a.Union(b));
            var crop = new Rect(left.X, left.Y, left.Width, stack.Bottom + 10 - left.Y);
            Shot(k, w, "screen-export-2", crop, [.. sections.Select(e => (Mark)Room(ShotKit.Bounds(e, w).Deflate(6)))], 7, only: [crop]);

            // 3: 공통 묶음
            sections[0].IsExpanded = true;
            ShotKit.Pump(500);
            Rect Row(string label)
            {
                var tb = ShotKit.All<TextBlock>(sections[0]).First(t => t.Text == label && t.IsEffectivelyVisible);
                var row = tb.GetVisualAncestors().OfType<Control>().First(a => a.Bounds.Width > 250);
                return ShotKit.Bounds(row, w).Inflate(new Thickness(12, -2, -4, -2));
            }
            var common = ShotKit.Bounds(sections[0], w);
            // ⑯ 스킨 고르기 단추는 스킨 줄 끝에 있어 번호가 단추 위 칸을 가리지 않게 스킨 줄 전체를 감싼다.
            Shot(k, w, "screen-export-3", common,
                [Row("Cell count"), Row("Order"), Row("Separate files"), Row("Cell names"), Row("Skin")],
                12, only: [common]);

            // 4: 프리셋 팝업
            var presetButton = tabs.Strip.Trailing!;
            k.Click(presetButton, settleMs: 0);
            ShotKit.Pump(600);
            var pop = PopupArea(k, w) ?? throw new InvalidOperationException("no preset popup");
            var bar = ShotKit.Bounds(tabs.Strip, w);
            var head = new Rect(bar.Right - 420, bar.Y, 420, bar.Height);
            Shot(k, w, "screen-export-4", head.Union(pop).Inflate(6), [Pop(pop)], 17, only: [head, pop]);
            PopupMenus.CloseAll();
            ShotKit.Pump(300);

            // 5: Import Settings 탭(프리셋 단추 도움말이 남지 않게 마우스를 치운다)
            k.Hover(w, ShotKit.Bounds(Column(0), w).Center, 100);
            tabs.SelectedIndex = 1;
            ShotKit.Pump(500);
            k.Save(w, "screen-export-5", marks: [ShotKit.Bounds(ShotKit.Get<Control>(w, "_importPage"), w).Deflate(4)], firstNumber: 18);
        }, settleMs: 900);
    }

    /// <summary>GIF로 내보내기: 1 = ① 범위 ② 배율 ③ 반복 ④ 배경 ⑤ 미리보기 ⑥ 결과 정보, 2 = ⑦ 태그 고르기 목록.</summary>
    [Shot("screen-gif")]
    public static void Gif(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/GIF 애니메이션으로 내보내기…"), w =>
        {
            ShotKit.Pump(400);
            var column = ShotKit.Get<Control>(w, "_rangeCurrent").GetVisualAncestors().OfType<StackPanel>().First(s => s.Width == 300);
            var groups = column.Children.Cast<Control>().ToList();
            k.Save(w, marks: [.. groups.Select(g => (Mark)g), Left(ShotKit.Bounds(ShotKit.Get<Control>(w, "_preview"), w)), Left(ShotKit.Bounds(ShotKit.Get<Control>(w, "_info"), w))]);

            // 2: 태그 고르기 목록
            var combo = ShotKit.Get<ComboBox>(w, "_tagCombo");
            combo.IsDropDownOpen = true;
            ShotKit.Pump(500);
            var list = PopupArea(k, w) ?? throw new InvalidOperationException("no tag list");
            var range = ShotKit.Bounds(groups[0], w);
            Shot(k, w, "screen-gif-2", range.Union(list).Inflate(6), [Pop(list)], 7, only: [range, list]);
            combo.IsDropDownOpen = false;
        }, settleMs: 900);
    }
}
