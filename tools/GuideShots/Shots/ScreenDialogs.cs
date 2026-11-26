using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;
using static GuideShots.ScreenDemo;
using static GuideShots.ScreenShot;

namespace GuideShots;

/// <summary>화면 구성: 내보내기 창(이미지 시트 · GIF 애니메이션 형식).</summary>
internal static class ScreenDialogShots
{
    /// <summary>
    /// 내보내기 창(이미지 형식, 게임 엔진 Unity): 1 = ① 내보내기 목록 ② 더하기 ③ 목록 프리셋 ④ 저장할 때 체크 ⑤ 빼기 ⑥ 형식 콤보 ⑦ 이름·위치
    /// ⑧ 게임 엔진 ⑨ 프리팹 ⑩ 애니메이션 ⑪ 만들어질 파일 ⑫ 설정 ⑬ 결과 미리보기 ⑭ 나눔줄 ⑮ 내보내기 ⑯ 다른 이름으로 저장,
    /// godot = ⑰ Godot 씬 체크, 2 = 묶음(⑱ 공통 ⑲ 파일 ⑳ 프레임 시트 ㉑ 부위 아틀라스 ㉒ 고급 ㉓ Unity Import Settings),
    /// 3 = 공통 묶음(㉔ 칸 수 ㉕ 정렬 ㉖ 따로 파일 ㉗ 칸 이름 ㉘ 스킨 고르기), 4 = ㉙ 프리셋 팝업, 5 = ㉚ Unity Import Settings 묶음.
    /// </summary>
    [Shot("screen-export")]
    public static void Export(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/내보내기…"), w =>
        {
            ShotKit.Pump(400);
            // 목록에 GIF 줄도 하나 두고(여러 줄 모습) 프로젝트의 시트 줄을 고른다.
            ShotKit.Call(w, "AddAnimation", Chickxel.Core.Model.AnimationExportFormat.Gif, null);
            ShotKit.Call(w, "Select", -1);
            ShotKit.Pump(400);
            // 프레임 시트와 부위 아틀라스를 둘 다 내보내게(두 묶음과 두 미리보기가 다 보이게).
            k.Click(ShotKit.All<TextBlock>(w).First(t => t.Text == "Both" && t.IsEffectivelyVisible));
            ShotKit.Pump(700);
            var sheet = ShotKit.Get<SheetExportPages>(w, "_sheet");
            var columns = ShotKit.Get<Grid>(w, "_columns");
            Control Column(int i) => columns.Children.First(c => Grid.GetColumn(c) == i);
            var exportButton = ShotKit.All<Button>(w).Last(b => b.IsEffectivelyVisible && b.Content is "Export");
            var formatOptions = ShotKit.Get<Control>(sheet, "_formatOptions");
            RadioButton Engine(string text) => ShotKit.All<RadioButton>(formatOptions).First(r => r.Content as string == text);
            // 게임 엔진 Unity: 그 아래 프리팹·애니메이션을 켜서 만들어질 파일 줄까지 보이게.
            k.Click(Engine(UiStrings.Export_EngineUnity));
            ShotKit.Pump(500);
            var unityChecks = ShotKit.All<CheckBox>(formatOptions).Where(c => c.IsEffectivelyVisible).Take(2).ToList();
            foreach (var c in unityChecks.Where(c => c.IsChecked != true)) k.Click(c);
            ShotKit.Pump(500);
            var assets = ShotKit.All<TextBlock>(formatOptions).First(t => t.IsEffectivelyVisible && t.Text?.StartsWith("→ ") == true);
            var radios = ShotKit.All<RadioButton>(formatOptions).Select(r => ShotKit.Bounds(r, w)).Aggregate((a, b) => a.Union(b));
            var listPane = Column(0);
            Button ButtonWith(Control host, string text) => ShotKit.All<TextBlock>(host).First(t => t.Text == text && t.IsEffectivelyVisible)
                .GetVisualAncestors().OfType<Button>().First();
            var list = ShotKit.Get<StackPanel>(w, "_list");
            var firstRow = (Control)list.Children[0];
            Rect Parent(string field) => ShotKit.Bounds((Control)ShotKit.Get<Control>(w, field).GetVisualParent()!, w);
            var nameRows = Parent("_nameBox").Union(Parent("_folderLabel"));
            var settings = ShotKit.Bounds(sheet.SettingsColumn, w).Intersect(ShotKit.Bounds(Column(1), w)).Deflate(4);
            k.Save(w, marks:
            [
                // 목록 칸의 작은 단추·체크는 Room 없이(번호가 ① 번호와 겹치지 않게 단추 모서리에), ① 테두리는 위로 올려 번호가 제목 글자를 가리지 않게
                new Rect(ShotKit.Bounds(listPane, w).X + 2, ShotKit.Bounds(listPane, w).Y - 10, ShotKit.Bounds(listPane, w).Width - 4, ShotKit.Bounds(listPane, w).Height + 8),
                ButtonWith(listPane, UiStrings.ExportList_Add), ButtonWith(listPane, UiStrings.ExportList_Presets),
                ShotKit.All<CheckBox>(firstRow).First(), ShotKit.All<Button>(firstRow).Last(),
                ShotKit.Get<ComboBox>(w, "_formatCombo"), Left(nameRows), Left(radios.Inflate(2)),
                unityChecks[0], unityChecks[1], assets,
                settings, ShotKit.Bounds(Column(3), w).Deflate(4), ShotKit.Bounds(Column(2), w).Inflate(new Thickness(2, -40, 2, -40)),
                exportButton, ShotKit.Get<Button>(w, "_saveCopy"),
            ]);

            // godot: 게임 엔진 Godot 4를 고른 형식 묶음(⑰ 씬 체크)
            k.Click(Engine(UiStrings.Export_EngineGodot));
            ShotKit.Pump(500);
            var scene = ShotKit.All<CheckBox>(formatOptions).First(c => c.IsEffectivelyVisible);
            var formatGroup = ShotKit.Get<ComboBox>(w, "_formatCombo").GetVisualAncestors().OfType<Border>().First(b => b.BorderThickness.Left > 0 && b.Bounds.Height > 150);
            var group = ShotKit.Bounds(formatGroup, w);
            Shot(k, w, "screen-export-godot", group, [scene], 17, only: [group.Inflate(4)]);
            k.Click(Engine(UiStrings.Export_EngineUnity));
            ShotKit.Pump(500);

            // 2: 묶음을 모두 접은 가운데 칸(형식 묶음은 위로 스크롤해 묶음들이 칸 맨 위에)
            var sections = ShotKit.All<Expander>(Column(1)).Where(e => e.IsEffectivelyVisible && e.Header is TextBlock).ToList();
            foreach (var e in sections) e.IsExpanded = false;
            ShotKit.Pump(500);
            ScrollToTop(sections[0]);
            ShotKit.Pump(500);
            var left = ShotKit.Bounds(Column(1), w);
            var stack = sections.Select(e => ShotKit.Bounds(e, w)).Aggregate((a, b) => a.Union(b));
            var crop = new Rect(left.X, stack.Y - 8, left.Width, stack.Height + 18);
            Shot(k, w, "screen-export-2", crop, [.. sections.Select(e => (Mark)Room(ShotKit.Bounds(e, w).Deflate(6)))], 18, only: [crop]);

            // 3: 공통 묶음
            sections[0].IsExpanded = true;
            ShotKit.Pump(500);
            ScrollToTop(sections[0]);
            ShotKit.Pump(500);
            Rect Row(string label)
            {
                var tb = ShotKit.All<TextBlock>(sections[0]).First(t => t.Text == label && t.IsEffectivelyVisible);
                var row = tb.GetVisualAncestors().OfType<Control>().First(a => a.Bounds.Width > 250);
                return ShotKit.Bounds(row, w).Inflate(new Thickness(12, -2, -4, -2));
            }
            var common = ShotKit.Bounds(sections[0], w);
            // ㉘ 스킨 줄(스킨이 없으면 넣을 레이어 줄): 끝의 고르기 단추까지 줄 전체를 감싼다. 스킨이 없을 때 "Skin"을 찾으면 정렬 목록의 줄이 잡히므로 이름표를 골라 찾는다.
            var layersLabel = UiStrings.S_UnityExportDialog_SheetRows_ED3CFCD2B9;
            var skinLabel = ShotKit.All<TextBlock>(sections[0]).Any(t => t.IsEffectivelyVisible && t.Text == layersLabel) ? layersLabel : "Skin";
            Shot(k, w, "screen-export-3", common,
                [Row("Cell count"), Row("Order"), Row("Separate files"), Row("Cell names"), Row(skinLabel)],
                24, only: [common]);

            // 4: 프리셋 팝업(형식 묶음 제목 줄 오른쪽 끝 단추)
            var presetButton = sheet.PresetButton;
            presetButton.BringIntoView();
            ShotKit.Pump(400);
            k.Click(presetButton, settleMs: 0);
            ShotKit.Pump(600);
            var pop = PopupArea(k, w) ?? throw new InvalidOperationException("no preset popup");
            var head = ShotKit.Bounds(presetButton, w).Inflate(new Thickness(120, 6, 6, 6));
            Shot(k, w, "screen-export-4", head.Union(pop).Inflate(6), [Pop(pop)], 29, only: [head, pop]);
            PopupMenus.CloseAll();
            ShotKit.Pump(300);

            // 5: Unity Import Settings 묶음(프리셋 단추 도움말이 남지 않게 마우스를 치운다)
            k.Hover(w, ShotKit.Bounds(Column(3), w).Center, 100);
            var import = ShotKit.Get<Expander>(sheet, "_importSettings");
            import.IsExpanded = true;
            ShotKit.Pump(400);
            import.BringIntoView();
            ShotKit.Pump(500);
            k.Save(w, "screen-export-5", marks: [ShotKit.Bounds(import, w).Deflate(4)], firstNumber: 30);
        }, settleMs: 900);
    }

    /// <summary>
    /// 내보내기 창(GIF 애니메이션 줄): 1 = ① 범위 ② 배율 ③ 속도 ④ 반복 ⑤ 배경 ⑥ 미리보기 ⑦ 결과 정보 ⑧ 이름·위치 ⑨ 내보내기,
    /// 2 = ⑩ 태그 고르기 목록.
    /// </summary>
    [Shot("screen-gif")]
    public static void Gif(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/내보내기…"), w =>
        {
            // 목록에 GIF 줄을 더해 고른다
            ShotKit.Call(w, "Select", ShotKit.Call(w, "AddAnimation", Chickxel.Core.Model.AnimationExportFormat.Gif, null));
            ShotKit.Pump(400);
            var anim = ShotKit.Get<AnimationExportPart>(w, "_anim");
            var groups = ((Panel)anim.Groups).Children.Cast<Control>().ToList();
            Rect Parent(string field) => ShotKit.Bounds((Control)ShotKit.Get<Control>(w, field).GetVisualParent()!, w);
            var exportButton = ShotKit.All<Button>(w).Last(b => b.IsEffectivelyVisible && b.Content is "Export");
            k.Save(w, marks:
            [
                .. groups.Select(g => (Mark)g), Left(ShotKit.Bounds(ShotKit.Get<Control>(anim, "_preview"), w)), Left(ShotKit.Bounds(ShotKit.Get<Control>(anim, "_info"), w)),
                Left(Parent("_nameBox").Union(Parent("_folderLabel"))), exportButton,
            ]);

            // 2: 태그 고르기 목록
            var combo = ShotKit.Get<ComboBox>(anim, "_tagCombo");
            combo.IsDropDownOpen = true;
            ShotKit.Pump(500);
            var list = PopupArea(k, w) ?? throw new InvalidOperationException("no tag list");
            var range = ShotKit.Bounds(groups[0], w);
            Shot(k, w, "screen-gif-2", range.Union(list).Inflate(6), [Pop(list)], 10, only: [range, list]);
            combo.IsDropDownOpen = false;
        }, settleMs: 900);
    }

    /// <summary>둘러싼 ScrollViewer를 스크롤해 <paramref name="c"/>의 위가 보이는 칸 맨 위에 오게 한다.</summary>
    private static void ScrollToTop(Control c)
    {
        var sv = c.GetVisualAncestors().OfType<ScrollViewer>().First();
        if (c.TranslatePoint(default, sv) is { } at) sv.Offset = new Vector(sv.Offset.X, Math.Max(0, sv.Offset.Y + at.Y - 4));
    }
}
