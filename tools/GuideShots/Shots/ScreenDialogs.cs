using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Chickxel.App;
using static GuideShots.ScreenDemo;
using static GuideShots.ScreenShot;

namespace GuideShots;

/// <summary>화면 구성: 내보내기 창(이미지 시트 · GIF 애니메이션 형식).</summary>
internal static class ScreenDialogShots
{
    /// <summary>
    /// 내보내기 창(이미지 형식): 1 = ① 내보내기 단추 ② 다른 이름으로 저장 단추 ③ 형식 콤보 ④ Unity용으로 내보내기 체크 ⑤ 왼쪽 설정 ⑥ 오른쪽 미리보기 ⑦ 나눔줄,
    /// 2 = 묶음(⑧ 공통 ⑨ 파일 ⑩ 프레임 시트 ⑪ 부위 아틀라스 ⑫ 고급 ⑬ Unity Import Settings), 3 = 공통 묶음(⑭ 칸 수 ⑮ 정렬 ⑯ 따로 파일 ⑰ 칸 이름 ⑱ 스킨 고르기),
    /// 4 = ⑲ 프리셋 팝업, 5 = ⑳ Unity Import Settings 묶음.
    /// </summary>
    [Shot("screen-export")]
    public static void Export(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        App.Settings.ExportFormat = ExportFormat.Image;
        k.Dialog(() => k.Command("파일/내보내기…"), w =>
        {
            ShotKit.Pump(400);
            // 프레임 시트와 부위 아틀라스를 둘 다 내보내게(두 묶음과 두 미리보기가 다 보이게).
            k.Click(ShotKit.All<TextBlock>(w).First(t => t.Text == "Both" && t.IsEffectivelyVisible));
            ShotKit.Pump(700);
            var sheet = ShotKit.Get<SheetExportPages>(w, "_sheet");
            var columns = ShotKit.Get<Grid>(w, "_columns");
            Control Column(int i) => columns.Children.First(c => Grid.GetColumn(c) == i);
            var exportButton = ShotKit.All<Button>(w).Last(b => b.IsEffectivelyVisible && b.Content is "Export");
            var unity = ShotKit.All<CheckBox>(ShotKit.Get<Control>(sheet, "_formatOptions")).First();
            k.Save(w, marks:
            [
                exportButton, ShotKit.Get<Button>(w, "_saveCopy"), ShotKit.Get<ComboBox>(w, "_formatCombo"), unity,
                ShotKit.Bounds(Column(0), w).Deflate(4), ShotKit.Bounds(Column(2), w).Deflate(4), ShotKit.Bounds(Column(1), w).Inflate(new Thickness(2, -40, 2, -40)),
            ]);

            // 2: 묶음을 모두 접은 왼쪽 칸
            var sections = ShotKit.All<Expander>(Column(0)).Where(e => e.IsEffectivelyVisible && e.Header is TextBlock).ToList();
            foreach (var e in sections) e.IsExpanded = false;
            ShotKit.Pump(500);
            var left = ShotKit.Bounds(Column(0), w);
            var stack = sections.Select(e => ShotKit.Bounds(e, w)).Aggregate((a, b) => a.Union(b));
            var crop = new Rect(left.X, left.Y, left.Width, stack.Bottom + 10 - left.Y);
            Shot(k, w, "screen-export-2", crop, [.. sections.Select(e => (Mark)Room(ShotKit.Bounds(e, w).Deflate(6)))], 8, only: [crop]);

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
            // ⑱ 스킨 고르기 단추는 스킨 줄 끝에 있어 번호가 단추 위 칸을 가리지 않게 스킨 줄 전체를 감싼다.
            Shot(k, w, "screen-export-3", common,
                [Row("Cell count"), Row("Order"), Row("Separate files"), Row("Cell names"), Row("Skin")],
                14, only: [common]);

            // 4: 프리셋 팝업(형식 묶음 제목 줄 오른쪽 끝 단추)
            var presetButton = sheet.PresetButton;
            k.Click(presetButton, settleMs: 0);
            ShotKit.Pump(600);
            var pop = PopupArea(k, w) ?? throw new InvalidOperationException("no preset popup");
            var head = ShotKit.Bounds(presetButton, w).Inflate(new Thickness(120, 6, 6, 6));
            Shot(k, w, "screen-export-4", head.Union(pop).Inflate(6), [Pop(pop)], 19, only: [head, pop]);
            PopupMenus.CloseAll();
            ShotKit.Pump(300);

            // 5: Unity Import Settings 묶음(프리셋 단추 도움말이 남지 않게 마우스를 치운다)
            k.Hover(w, ShotKit.Bounds(Column(2), w).Center, 100);
            var import = ShotKit.Get<Expander>(sheet, "_importSettings");
            import.IsExpanded = true;
            ShotKit.Pump(400);
            import.BringIntoView();
            ShotKit.Pump(500);
            k.Save(w, "screen-export-5", marks: [ShotKit.Bounds(import, w).Deflate(4)], firstNumber: 20);
        }, settleMs: 900);
    }

    /// <summary>내보내기 창(GIF 애니메이션): 1 = ① 범위 ② 배율 ③ 속도 ④ 반복 ⑤ 배경 ⑥ 미리보기 ⑦ 결과 정보, 2 = ⑧ 태그 고르기 목록.</summary>
    [Shot("screen-gif")]
    public static void Gif(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        App.Settings.ExportFormat = ExportFormat.Gif;
        k.Dialog(() => k.Command("파일/내보내기…"), w =>
        {
            ShotKit.Pump(400);
            var anim = ShotKit.Get<AnimationExportPart>(w, "_anim");
            var groups = ((Panel)anim.Groups).Children.Cast<Control>().ToList();
            k.Save(w, marks: [.. groups.Select(g => (Mark)g), Left(ShotKit.Bounds(ShotKit.Get<Control>(anim, "_preview"), w)), Left(ShotKit.Bounds(ShotKit.Get<Control>(anim, "_info"), w))]);

            // 2: 태그 고르기 목록
            var combo = ShotKit.Get<ComboBox>(anim, "_tagCombo");
            combo.IsDropDownOpen = true;
            ShotKit.Pump(500);
            var list = PopupArea(k, w) ?? throw new InvalidOperationException("no tag list");
            var range = ShotKit.Bounds(groups[0], w);
            Shot(k, w, "screen-gif-2", range.Union(list).Inflate(6), [Pop(list)], 8, only: [range, list]);
            combo.IsDropDownOpen = false;
        }, settleMs: 900);
    }
}
