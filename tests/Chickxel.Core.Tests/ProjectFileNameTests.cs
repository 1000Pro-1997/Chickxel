using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>저장한 적 없는 프로젝트의 임시 제목과 저장 대화상자에 넣을 파일 이름(겹치면 _1, _2 …).</summary>
public class ProjectFileNameTests
{
    [Theory]
    [InlineData(null, "sprite")]
    [InlineData("   ", "sprite")]
    [InlineData("  용사 걷기  ", "용사 걷기")]
    [InlineData("검: 불꽃?", "검 불꽃")]
    [InlineData("a/b\\c*d|e\"f<g>h", "abcdefgh")]
    [InlineData("<>?*", "sprite")]
    [InlineData("끝 점...", "끝 점")]
    [InlineData("con", "sprite")]
    [InlineData("Nul.txt", "sprite")]
    [InlineData("console", "console")]
    public void BaseNameFor_strips_characters_files_cannot_use(string? title, string expected)
        => Assert.Equal(expected, ProjectFileName.BaseNameFor(title));

    [Fact]
    public void FirstFree_appends_the_first_unused_number()
    {
        var used = new HashSet<string> { "sprite.chickxel", "sprite_1.chickxel", "sprite_3.chickxel" };
        Assert.Equal("sprite_2.chickxel", ProjectFileName.FirstFree("sprite", ".chickxel", used.Contains));
        Assert.Equal("hero.chickxel", ProjectFileName.FirstFree("hero", ".chickxel", used.Contains));
    }

    [Fact]
    public void Suggest_avoids_names_in_the_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chickxel-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal("sprite.chickxel", ProjectFileName.Suggest(null, dir));
            File.WriteAllText(Path.Combine(dir, "sprite.chickxel"), "");
            Assert.Equal("sprite_1.chickxel", ProjectFileName.Suggest(null, dir));
            File.WriteAllText(Path.Combine(dir, "sprite_1.chickxel"), "");
            Assert.Equal("sprite_2.chickxel", ProjectFileName.Suggest("", dir));
            Assert.Equal("용사.chickxel", ProjectFileName.Suggest("용사", dir));
            File.WriteAllText(Path.Combine(dir, "용사.chickxel"), "");
            Assert.Equal("용사_1.chickxel", ProjectFileName.Suggest(" 용사 ", dir));
            // 폴더를 모르면 겹침을 보지 않는다
            Assert.Equal("sprite.chickxel", ProjectFileName.Suggest(null, null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TempTitle_names_an_unsaved_project_without_marking_it_dirty()
    {
        var p = new SpriteProject(8, 8);
        bool dirty = p.IsDirty;
        Assert.Equal(SpriteProject.UntitledName, p.DisplayName);

        p.TempTitle = "  불꽃\n검  ";
        Assert.Equal("불꽃 검", p.TempTitle);
        Assert.Equal("불꽃 검", p.DisplayName);
        Assert.Equal(dirty, p.IsDirty);

        p.TempTitle = new string('가', SpriteProject.MaxTempTitleLength + 20);
        Assert.Equal(SpriteProject.MaxTempTitleLength, p.TempTitle!.Length);

        p.TempTitle = "   ";
        Assert.Null(p.TempTitle);
        Assert.Equal(SpriteProject.UntitledName, p.DisplayName);

        p.TempTitle = "검";
        p.Path = Path.Combine(Path.GetTempPath(), "blade.chickxel");
        Assert.Equal("blade.chickxel", p.DisplayName);
    }
}
