using System.Globalization;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Tests;

public class TranslationCsvTests
{
    private static TranslationSheet RoundTrip(IReadOnlyList<string> keys, params TranslationColumn[] columns)
    {
        var writer = new StringWriter();
        TranslationCsv.Write(writer, keys, columns);
        return TranslationCsv.Read(new StringReader(writer.ToString()));
    }

    [Fact]
    public void Values_with_separators_quotes_newlines_and_edge_spaces_survive_a_round_trip()
    {
        var values = new Dictionary<string, string>
        {
            ["T.Comma"] = "a, b; c\td",
            ["T.Quote"] = "say \"hi\"",
            ["T.Lines"] = "first\nsecond\r\nthird",
            ["T.Spaces"] = "  padded ",
            ["T.Plain"] = "plain",
        };
        var keys = values.Keys.Append("T.Missing").ToList();
        var sheet = RoundTrip(keys, new TranslationColumn("ja", "日本語, 試験", values));

        Assert.Equal(keys, sheet.Keys);
        var column = Assert.Single(sheet.Columns);
        Assert.Equal("ja", column.Code);
        Assert.Equal("日本語, 試験", column.Name);
        // 빈 칸(T.Missing)은 번역하지 않은 문구라 담지 않는다.
        Assert.Equal(values.OrderBy(v => v.Key), column.Values.OrderBy(v => v.Key));
    }

    [Fact]
    public void Semicolon_csv_with_bom_and_without_name_row_is_read()
    {
        var text = "\uFEFFkey;fr;de\r\nT.A;bonjour;\r\n\r\nT.B;\"x;y\";hallo\r\n";
        var sheet = TranslationCsv.Read(new StringReader(text));

        Assert.Equal(["T.A", "T.B"], sheet.Keys);
        Assert.Equal(["fr", "de"], sheet.Columns.Select(c => c.Code));
        Assert.Equal("fr", sheet.Columns[0].Name);
        Assert.Equal("x;y", sheet.Columns[0].Values["T.B"]);
        Assert.False(sheet.Columns[1].Values.ContainsKey("T.A"));
        Assert.Equal("hallo", sheet.Columns[1].Values["T.B"]);
    }

    [Fact]
    public void Later_row_wins_for_a_repeated_key()
    {
        var sheet = TranslationCsv.Read(new StringReader("key,fr\nT.A,old\nT.A,new\n"));
        Assert.Equal(["T.A"], sheet.Keys);
        Assert.Equal("new", sheet.Columns[0].Values["T.A"]);
    }

    [Theory]
    [InlineData("name,fr\nT.A,x\n")]
    [InlineData("key,fr,FR\nT.A,x,y\n")]
    [InlineData("")]
    public void Not_a_translation_csv_or_duplicate_language_is_rejected(string text)
        => Assert.Throws<InvalidDataException>(() => TranslationCsv.Read(new StringReader(text)));

    [Theory]
    [InlineData("ja", true)]
    [InlineData("pt-BR", true)]
    [InlineData("zh_Hant", true)]
    [InlineData("", false)]
    [InlineData("../x", false)]
    [InlineData("a b", false)]
    public void Language_code_must_be_a_safe_file_name(string code, bool valid)
        => Assert.Equal(valid, TranslationCsv.IsValidCode(code));
}

/// <summary>전역 번역 상태를 바꾸므로 다른 테스트와 동시에 돌지 않는다.</summary>
[CollectionDefinition(nameof(TranslationOverlayTests), DisableParallelization = true)]
public sealed class TranslationOverlayCollection;

[Collection(nameof(TranslationOverlayTests))]
public class TranslationOverlayTests
{
    [Fact]
    public void Csv_overlay_wins_and_missing_strings_fall_back_to_the_fallback_language()
    {
        var english = CultureInfo.GetCultureInfo("en");
        var englishTruncated = CoreStrings.ResourceManager.GetString("Png_Truncated", english);
        var koreanTruncated = CoreStrings.ResourceManager.GetString("Png_Truncated", CultureInfo.InvariantCulture);
        Assert.NotEqual(koreanTruncated, englishTruncated);
        try
        {
            Translations.Use(new Dictionary<(string, string), string>
            {
                [(nameof(CoreStrings), "Png_InvalidFile")] = "overlay",
                [(nameof(CoreStrings), "Png_UnsupportedBitDepth")] = "depth {bitDepth}!",
                [("OtherTable", "Png_Truncated")] = "wrong table",
            }, english);

            Assert.Equal("overlay", CoreStrings.Png_InvalidFile);
            Assert.Equal("depth 7!", CoreStrings.FormatPng_UnsupportedBitDepth(7));
            Assert.Equal(englishTruncated, CoreStrings.Png_Truncated);
        }
        finally
        {
            Translations.Use(null, null);
        }
        Assert.Equal(koreanTruncated, CoreStrings.Png_Truncated);
    }
}
