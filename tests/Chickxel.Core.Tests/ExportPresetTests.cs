using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>내보내기 설정 프리셋: 담기·적용(프로젝트 의존 값 유지), 시트별 다르게, JSON 왕복·오류, 합치기, 기본 제공 프리셋, 실행취소.</summary>
public class ExportPresetTests
{
    /// <summary>그룹 하나(부위·스킨 둘·착장 하나) 프로젝트 — 고른 스킨·착장·뺀 레이어 목록용.</summary>
    private static (SpriteProject P, Layer Group, Skin Knight, Outfit Outfit) Project()
    {
        var p = new SpriteProject(4, 4);
        var g = p.AddGroup("body");
        p.AddPart(g, "Head", 2, 2, new Point(0, 0));
        var knight = p.AddSkin(g, "knight");
        var outfit = p.SaveOutfit("기사");
        return (p, g, knight, outfit);
    }

    /// <summary>모든 값을 기본값과 다르게 바꾼 시트 설정.</summary>
    private static void Customize(SheetOptions o, int seed)
    {
        o.Layout = SheetLayout.Packed;
        o.Order = new GridOrder([GridKey.Frame, GridKey.Skin, GridKey.Tag, GridKey.Part], 1);
        o.Grid = SheetGrid.Rows;
        o.GridCount = 7 + seed;
        o.Spacing = 3 + seed;
        o.Trim = true;
        o.PowerOfTwo = true;
        o.MaxWidth = 512 + seed;
        o.PackOrder = AtlasPackOrder.LargestFirst;
        o.SplitTags = true;
        o.SplitSkins = true;
        o.SplitParts = true;
        o.Naming = SpriteNaming.FrameNumber;
        o.Skins = SkinSource.Selected;
        o.BakeInheritedSkins = true;
    }

    private static void AssertSheetEqual(SheetOptions expected, SheetOptions actual)
    {
        Assert.Equal(expected.Layout, actual.Layout);
        Assert.Equal(expected.Order, actual.Order);
        Assert.Equal(expected.Grid, actual.Grid);
        Assert.Equal(expected.GridCount, actual.GridCount);
        Assert.Equal(expected.Spacing, actual.Spacing);
        Assert.Equal(expected.Trim, actual.Trim);
        Assert.Equal(expected.PowerOfTwo, actual.PowerOfTwo);
        Assert.Equal(expected.MaxWidth, actual.MaxWidth);
        Assert.Equal(expected.PackOrder, actual.PackOrder);
        Assert.Equal(expected.SplitTags, actual.SplitTags);
        Assert.Equal(expected.SplitSkins, actual.SplitSkins);
        Assert.Equal(expected.SplitParts, actual.SplitParts);
        Assert.Equal(expected.Naming, actual.Naming);
        Assert.Equal(expected.Skins, actual.Skins);
        Assert.Equal(expected.BakeInheritedSkins, actual.BakeInheritedSkins);
    }

    /// <summary>프로젝트 의존 값을 뺀 모든 값을 기본값과 다르게 바꾼 설정(시트별 다르게 켬).</summary>
    private static ExportSettings Custom()
    {
        var x = new ExportSettings
        {
            ExportOnSave = false,
            ExportFrames = false,
            ExportParts = true,
            ImageFormat = ExportImageFormat.Jpg,
            JpegQuality = 55,
            WriteMeta = false,
            WriteSpriteLibrary = true,
        };
        Customize(x.Common, 0);
        x.PartsOverride = new SheetOptions();
        Customize(x.PartsOverride, 10);
        x.PartsOverride.Layout = SheetLayout.Grid;
        x.PartsOverride.Order = GridOrder.ByFrame;
        return x;
    }

    [Fact]
    public void CaptureApply_RoundTripsAllPresetFields()
    {
        var src = Custom();
        var preset = ExportPreset.Capture(src, "내 설정");
        Assert.Equal("내 설정", preset.Name);
        Assert.False(preset.BuiltIn);
        Assert.Equal([GridKey.Tag, GridKey.Frame, GridKey.Skin, GridKey.Part], preset.Sheet.Order); // 바로잡힌 순서(태그는 프레임 바로 앞)
        Assert.Equal(1, preset.Sheet.RowBreak);
        Assert.NotNull(preset.PartsSheet);

        var dst = new ExportSettings();
        preset.ApplyTo(dst);
        Assert.Equal((false, false, true), (dst.ExportOnSave, dst.ExportFrames, dst.ExportParts));
        Assert.Equal((ExportImageFormat.Jpg, 55), (dst.ImageFormat, dst.JpegQuality));
        Assert.False(dst.WriteMeta);
        Assert.True(dst.WriteSpriteLibrary);
        AssertSheetEqual(src.Common, dst.Common);
        Assert.NotNull(dst.PartsOverride);
        AssertSheetEqual(src.PartsOverride!, dst.PartsOverride!);
        Assert.Equal(GridOrder.ByFrame, dst.Parts.Order);

        // 다시 담으면 같은 값
        var again = ExportPreset.Capture(dst, "x");
        Assert.Equal(ExportPresets.Serialize([preset.Clone("x")]), ExportPresets.Serialize([again]));

        // WriteMeta 자동(null)도 그대로
        src.WriteMeta = null;
        ExportPreset.Capture(src, "a").ApplyTo(dst);
        Assert.Null(dst.WriteMeta);

        // Clone은 깊은 사본
        var clone = preset.Clone();
        Assert.Equal("내 설정", clone.Name);
        clone.Sheet.Order.Add(GridKey.Frame);
        clone.Sheet.Spacing = 99;
        clone.PartsSheet!.Spacing = 98;
        Assert.Equal(4, preset.Sheet.Order.Count);
        Assert.Equal(3, preset.Sheet.Spacing);
        Assert.Equal(13, preset.PartsSheet!.Spacing);
    }

    [Fact]
    public void ApplyTo_KeepsProjectDependentValues()
    {
        var (p, g, knight, outfit) = Project();
        var x = p.Export;
        x.FileName = "hero";
        x.PartsFileName = "hero_atlas";
        x.Folder = "C:/out";
        x.SingleFrameIndex = 3;
        x.KeepImportedRects = false;
        x.Common.ExcludedSkins = [knight];
        x.Common.Outfits = [outfit];
        x.Common.ExcludedLayers = [g];
        var excludedSkins = x.Common.ExcludedSkins;

        // 시트별 다르게를 새로 켜면 부위 아틀라스 목록은 공통 목록을 복사
        var preset = ExportPreset.Capture(Custom(), "p");
        preset.ApplyTo(x);
        Assert.Equal(("hero", "hero_atlas", "C:/out"), (x.FileName, x.PartsFileName, x.Folder));
        Assert.Equal(3, x.SingleFrameIndex);
        Assert.False(x.KeepImportedRects);
        Assert.Same(excludedSkins, x.Common.ExcludedSkins);
        Assert.Equal([outfit], x.Common.Outfits);
        Assert.Equal([g], x.Common.ExcludedLayers);
        var po = x.PartsOverride!;
        Assert.NotSame(x.Common, po);
        Assert.Equal([knight], po.ExcludedSkins);
        Assert.Equal([outfit], po.Outfits);
        Assert.Equal([g], po.ExcludedLayers);
        Assert.Equal(13, po.Spacing);

        // 이미 켜져 있으면 부위 아틀라스 쪽 목록도 그대로
        po.ExcludedSkins = [];
        preset.ApplyTo(x);
        Assert.Same(po, x.PartsOverride);
        Assert.Empty(x.PartsOverride!.ExcludedSkins);

        // 시트별 다르게가 없는 프리셋은 끈다(목록은 공통 그대로)
        var plain = ExportPresets.BuiltIns[0];
        Assert.Null(plain.PartsSheet);
        plain.ApplyTo(x);
        Assert.Null(x.PartsOverride);
        Assert.Same(x.Common, x.Parts);
        Assert.Same(excludedSkins, x.Common.ExcludedSkins);
        Assert.Equal(("hero", "hero_atlas", "C:/out", 3, false), (x.FileName, x.PartsFileName, x.Folder, x.SingleFrameIndex, x.KeepImportedRects));
    }

    [Fact]
    public void ApplyInsideEdit_IsOneUndoStep()
    {
        var (p, _, knight, _) = Project();
        p.Export.Common.ExcludedSkins = [knight];
        p.Export.Common.Spacing = 2;
        var before = ExportPresets.Serialize([ExportPreset.Capture(p.Export, "before")]);
        int undo = p.History.UndoCount;
        var preset = ExportPresets.BuiltIns.Single(b => b.Name == "부위 아틀라스 + Sprite Library");
        p.Edit("내보내기 프리셋 적용", ProjectChange.Settings, () => preset.ApplyTo(p.Export));
        Assert.Equal(undo + 1, p.History.UndoCount);
        Assert.Equal(SheetLayout.Packed, p.Export.Common.Layout);

        Assert.True(p.Undo());
        Assert.Equal(before, ExportPresets.Serialize([ExportPreset.Capture(p.Export, "before")]));
        Assert.Equal(SheetLayout.Grid, p.Export.Common.Layout);
        Assert.Equal(2, p.Export.Common.Spacing);
        Assert.Equal([knight], p.Export.Common.ExcludedSkins);
        Assert.True(p.Redo());
        Assert.Equal(SheetLayout.Packed, p.Export.Common.Layout);
        Assert.True(p.Export.WriteSpriteLibrary);
    }

    [Fact]
    public void BuiltIns_ApplySensibly()
    {
        Assert.Equal(["프레임 시트 기본", "부위 아틀라스 + Sprite Library", "스킨마다 따로 파일"], ExportPresets.BuiltIns.Select(b => b.Name));
        Assert.All(ExportPresets.BuiltIns, b => Assert.True(b.BuiltIn));

        var x = Custom();
        ExportPresets.BuiltIns[0].ApplyTo(x);
        AssertSheetEqual(new SheetOptions(), x.Common);
        Assert.Null(x.PartsOverride);
        Assert.Equal((true, true, false, false), (x.ExportOnSave, x.ExportFrames, x.ExportParts, x.WriteSpriteLibrary));
        Assert.Equal((ExportImageFormat.Png, 90), (x.ImageFormat, x.JpegQuality));
        Assert.Null(x.WriteMeta);

        x = Custom();
        ExportPresets.BuiltIns[1].ApplyTo(x);
        Assert.Equal((false, true, true), (x.ExportFrames, x.ExportParts, x.WriteSpriteLibrary));
        Assert.Equal((SheetLayout.Packed, SkinSource.Selected), (x.Parts.Layout, x.Parts.Skins));
        Assert.Null(x.PartsOverride);

        x = new ExportSettings();
        ExportPresets.BuiltIns[2].ApplyTo(x);
        Assert.Equal((true, true), (x.ExportFrames, x.ExportParts));
        Assert.Equal((SkinSource.Selected, true), (x.Common.Skins, x.Common.SplitSkins));
        Assert.Equal(SheetLayout.Grid, x.Common.Layout);
    }

    // ───────────── JSON ─────────────

    [Fact]
    public void SerializeParse_RoundTrip_EnumsAsStrings_BuiltInsExcluded()
    {
        var custom = ExportPreset.Capture(Custom(), "내 설정");
        var json = ExportPresets.Serialize([.. ExportPresets.BuiltIns, custom]);
        Assert.Contains("\"Packed\"", json);
        Assert.Contains("\"Jpg\"", json);
        Assert.Contains("\"LargestFirst\"", json);
        Assert.Contains("\"Frame\"", json);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("내 설정", json); // 한글은 이스케이프하지 않음
        Assert.DoesNotContain("프레임 시트 기본", json);
        Assert.DoesNotContain("builtIn", json, StringComparison.OrdinalIgnoreCase);

        var back = Assert.Single(ExportPresets.Parse(json));
        Assert.Equal("내 설정", back.Name);
        Assert.False(back.BuiltIn);
        Assert.Equal(json, ExportPresets.Serialize([back]));
        var a = new ExportSettings();
        var b = new ExportSettings();
        custom.ApplyTo(a);
        back.ApplyTo(b);
        AssertSheetEqual(a.Common, b.Common);
        AssertSheetEqual(a.PartsOverride!, b.PartsOverride!);

        // 시트별 다르게 없음 → partsSheet 생략, 읽으면 null
        var plain = ExportPresets.BuiltIns[0].Clone("plain");
        var plainJson = ExportPresets.Serialize([plain]);
        Assert.DoesNotContain("partsSheet", plainJson);
        Assert.Null(ExportPresets.Parse(plainJson).Single().PartsSheet);
    }

    [Fact]
    public void Parse_DuplicateNamesLastWins_SkipsUnnamed_AndRejectsBadFiles()
    {
        var list = ExportPresets.Parse("""
            { "version": 1, "presets": [
                { "name": "a", "sheet": { "spacing": 1 } },
                { "name": "" },
                { "name": "  b  ", "imageFormat": "Tga" },
                { "name": "a", "sheet": { "spacing": 5, "layout": "Packed" } }, // 주석·끝 쉼표 허용
            ] }
            """);
        Assert.Equal(["b", "a"], list.Select(p => p.Name));
        Assert.Equal((5, SheetLayout.Packed), (list[1].Sheet.Spacing, list[1].Sheet.Layout));
        Assert.Equal(ExportImageFormat.Tga, list[0].ImageFormat);
        Assert.NotNull(list[0].Sheet); // 없으면 기본 시트 설정

        Assert.Empty(ExportPresets.Parse("""{ "version": 1 }"""));
        Assert.Throws<InvalidDataException>(() => ExportPresets.Parse("""{ "version": 2, "presets": [] }"""));
        Assert.Throws<InvalidDataException>(() => ExportPresets.Parse("{ \"presets\": [ "));
        Assert.Throws<InvalidDataException>(() => ExportPresets.Parse("""{ "presets": [ { "name": "x", "imageFormat": "Gif" } ] }"""));
    }

    [Fact]
    public void SaveLoad_File()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", ExportPresets.FileName);
        Assert.Empty(ExportPresets.Load(path));
        var custom = ExportPreset.Capture(Custom(), "내 설정");
        ExportPresets.Save(path, [.. ExportPresets.BuiltIns, custom]);
        Assert.False(File.Exists(path + ".tmp"));
        var back = Assert.Single(ExportPresets.Load(path));
        Assert.Equal(ExportPresets.Serialize([custom]), ExportPresets.Serialize([back]));
        File.WriteAllText(path, "not json");
        Assert.Throws<InvalidDataException>(() => ExportPresets.Load(path));
    }

    [Fact]
    public void Merge_OverwritesSameName_RenamesBuiltInClashes()
    {
        var a = ExportPresets.BuiltIns[0].Clone("a");
        a.Sheet.Spacing = 1;
        var c = ExportPresets.BuiltIns[0].Clone("c");
        var importedA = ExportPresets.BuiltIns[0].Clone("a");
        importedA.Sheet.Spacing = 5;
        var clash = ExportPresets.BuiltIns[1].Clone();
        Assert.False(clash.BuiltIn);

        var merged = ExportPresets.Merge([.. ExportPresets.BuiltIns, a, c], [importedA, clash]);
        Assert.Equal(["a", "c", "부위 아틀라스 + Sprite Library (가져옴)"], merged.Select(p => p.Name));
        Assert.Equal(5, merged[0].Sheet.Spacing);
        Assert.NotSame(importedA, merged[0]); // 사본
        Assert.True(merged[2].WriteSpriteLibrary);
        Assert.All(merged, p => Assert.False(p.BuiltIn));
        Assert.Equal(1, a.Sheet.Spacing); // 원래 목록은 그대로
    }
}
