using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class LospecTests
{
    private static Dictionary<string, List<string>> QueryOf(Uri url)
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var pair in url.Query.TrimStart('?').Split('&'))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            if (!map.TryGetValue(key, out var values)) map[key] = values = new List<string>();
            values.Add(kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "");
        }
        return map;
    }

    [Fact]
    public void ListUrl_HasEveryParameterExactlyOnce()
    {
        var url = LospecPalettes.ListUrl(new LospecQuery(LospecSort.Newest, LospecColorFilter.Exact, 4, "gameboy", 3));
        Assert.Equal("https://lospec.com/palette-list/load", url.GetLeftPart(UriPartial.Path));
        var q = QueryOf(url);
        Assert.Equal(["colorNumberFilterType", "colorNumber", "page", "tag", "sortingType"], q.Keys);
        Assert.All(q.Values, v => Assert.Single(v));
        Assert.Equal("exact", q["colorNumberFilterType"][0]);
        Assert.Equal("4", q["colorNumber"][0]);
        Assert.Equal("3", q["page"][0]);
        Assert.Equal("gameboy", q["tag"][0]);
        Assert.Equal("newest", q["sortingType"][0]);
    }

    [Fact]
    public void ListUrl_Defaults_EmptyTagStillPresent_AndCountClamped()
    {
        var q = QueryOf(LospecPalettes.ListUrl(new LospecQuery(ColorCount: 999, Page: -2)));
        Assert.Equal("any", q["colorNumberFilterType"][0]);
        Assert.Equal("256", q["colorNumber"][0]);
        Assert.Equal("0", q["page"][0]);
        Assert.Equal("", q["tag"][0]);
        Assert.Equal("default", q["sortingType"][0]);
    }

    [Fact]
    public void ListUrl_EscapesTag()
    {
        var url = LospecPalettes.ListUrl(new LospecQuery(Tag: " a&b=c #d "));
        Assert.Contains("tag=a%26b%3Dc%20%23d&", url.OriginalString);
        Assert.Equal("a&b=c #d", QueryOf(url)["tag"][0]);
        Assert.Equal("gameboy", QueryOf(LospecPalettes.ListUrl(new LospecQuery(Tag: "GameBoy")))["tag"][0]);
        Assert.Equal(5, QueryOf(url).Count);
    }

    [Theory]
    [InlineData("Sweetie 16", "sweetie-16")]
    [InlineData("  PICO-8  ", "pico-8")]
    [InlineData("endesga   32", "endesga-32")]
    [InlineData("   ", "")]
    public void ToSlug_LowercasesAndJoinsWords(string text, string slug) => Assert.Equal(slug, LospecPalettes.ToSlug(text));

    private const string PageJson = """
        {
          "palettes": [
            { "title": "famicube 16", "slug": "famicube-16", "colors": ["e03c28", "#871646", "zzzzzz", 5, "abc"],
              "numberOfColors": 16, "likes": 36, "downloads": "1,271", "tags": ["fantasyconsole", ""],
              "user": { "name": "polyphrog", "slug": "poly-phrog" }, "url": "/palette-list/famicube-16" },
            { "title": "No User", "slug": "no-user", "colors": ["000000", "ffffff80"], "likes": 2, "downloads": "9" },
            { "title": "No Colors", "slug": "no-colors", "colors": ["nothex"] },
            "garbage",
            { "slug": "untitled", "colors": ["112233"], "user": null }
          ],
          "totalCount": 4532
        }
        """;

    [Fact]
    public void ParsePage_ReadsPalettes_SkipsInvalidEntries()
    {
        var page = LospecPalettes.ParsePage(PageJson);
        Assert.Equal(4532, page.TotalCount);
        Assert.Equal(["famicube-16", "no-user", "untitled"], page.Palettes.Select(p => p.Slug));

        var a = page.Palettes[0];
        Assert.Equal("famicube 16", a.Title);
        Assert.Equal("polyphrog", a.Author);
        Assert.Equal([new Color32(0xe0, 0x3c, 0x28), new Color32(0x87, 0x16, 0x46)], a.Colors);
        Assert.Equal(36, a.Likes);
        Assert.Equal(1271, a.Downloads);
        Assert.Equal(["fantasyconsole"], a.Tags);
        Assert.Equal("https://lospec.com/palette-list/famicube-16", a.WebUrl);

        var b = page.Palettes[1];
        Assert.Equal("", b.Author);
        Assert.Equal([new Color32(0, 0, 0), new Color32(255, 255, 255, 0x80)], b.Colors);
        Assert.Empty(b.Tags);

        var c = page.Palettes[2];
        Assert.Equal("untitled", c.Title);
        Assert.Equal("", c.Author);
    }

    [Fact]
    public void ParsePage_RejectsNonJson()
        => Assert.Throws<FormatException>(() => LospecPalettes.ParsePage("<!DOCTYPE html><html></html>"));

    [Fact]
    public void ParseSingle_ReadsSlugJson_NullForHtmlOrEmpty()
    {
        var p = LospecPalettes.ParseSingle("""{"name":"Sweetie 16","author":"","colors":["1a1c2c","5d275d"]}""", "sweetie-16");
        Assert.NotNull(p);
        Assert.Equal("Sweetie 16", p.Title);
        Assert.Equal("sweetie-16", p.Slug);
        Assert.Equal("", p.Author);
        Assert.Equal([new Color32(0x1a, 0x1c, 0x2c), new Color32(0x5d, 0x27, 0x5d)], p.Colors);
        Assert.Null(LospecPalettes.ParseSingle("<html>not found</html>", "x"));
        Assert.Null(LospecPalettes.ParseSingle("""{"name":"Empty","colors":[]}""", "x"));
    }
}
