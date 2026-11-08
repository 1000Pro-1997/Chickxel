using Chickxel.Core.IO;

namespace Chickxel.Core.Tests;

/// <summary>파일 목록에서 프로젝트에 딸려 보일 파일 묶기(<see cref="ProjectFamily"/>).</summary>
public class ProjectFamilyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "chickxel-family");

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    private static Dictionary<string, List<string>> Group(string[] projects, string[] files,
        Dictionary<string, string[]>? registered = null, Func<string, bool>? exists = null)
        => ProjectFamily.Group(projects, files, p => registered?.GetValueOrDefault(p) ?? [], exists ?? (_ => false));

    [Fact]
    public void LongestStemWins()
    {
        string sprite = P("sprite.chickxel"), sprite3 = P("sprite_3.chickxel");
        var result = Group([sprite, sprite3],
            [P("sprite.png"), P("sprite_3.png"), P("sprite_3.gif"), P("sprite_3_01.png"), P("sprite_walk.png"), P("other.png")]);

        Assert.Equal([P("sprite.png"), P("sprite_walk.png")], result[sprite]);
        Assert.Equal([P("sprite_3.gif"), P("sprite_3.png"), P("sprite_3_01.png")], result[sprite3]);
    }

    [Fact]
    public void StemNeedsSeparatorAndIgnoresCase()
    {
        string sprite = P("sprite.chickxel");
        var result = Group([sprite], [P("spriteX.png"), P("sprites.gif"), P("SPRITE@knight.png"), P("Sprite_01.PNG")]);

        Assert.Equal([P("SPRITE@knight.png"), P("Sprite_01.PNG")], result[sprite]);
    }

    [Fact]
    public void OtherFolderIsNotGroupedByName()
    {
        string sprite = P("sprite.chickxel");
        var result = Group([sprite], [P("sub", "sprite.png"), P("sprite.gif")]);

        Assert.Equal([P("sprite.gif")], result[sprite]);
    }

    [Fact]
    public void ProjectFilesAndNonMediaAreNeverChildren()
    {
        string sprite = P("sprite.chickxel"), sprite2 = P("sprite_2.chickxel"), legacy = P("sprite_old.gpixel");
        var result = Group([sprite, sprite2], [legacy, sprite2, P("sprite.png.meta"), P("sprite.txt"), P("sprite_2.webp")],
            registered: new() { [sprite] = [sprite2, P("sprite.spriteLib")] }, exists: _ => true);

        Assert.Empty(result[sprite]);
        Assert.Equal([P("sprite_2.webp")], result[sprite2]);
    }

    [Fact]
    public void RegisteredExportInCustomFolderIsGrouped()
    {
        string hero = P("hero.chickxel"), heroes = P("hero_big.chickxel");
        string custom = P("Export", "knight_sheet.png"), outside = P("..", "unity", "hero_atlas.png"), missing = P("Export", "gone.png");
        // 이름으로는 hero_big에 맞아도 등록한 프로젝트(hero)에 딸린다
        string claimed = P("hero_big_walk.png");
        var result = Group([hero, heroes], [custom, claimed, P("hero_big.png")],
            registered: new() { [hero] = [custom, outside, missing, claimed] },
            exists: p => string.Equals(p, Path.GetFullPath(outside), StringComparison.OrdinalIgnoreCase));

        Assert.Equal([Path.GetFullPath(outside), claimed, custom], result[hero]);
        Assert.Equal([P("hero_big.png")], result[heroes]);
    }
}
