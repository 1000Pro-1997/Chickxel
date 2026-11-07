using Chickxel.Core.Text;

namespace Chickxel.Core.Tests;

public class MiniMarkdownTests
{
    private static MdSpan S(string text, MdStyle style = MdStyle.None, string? url = null) => new(text, style, url);

    private static IReadOnlyList<MdSpan> Inline(string text) => MiniMarkdown.ParseInline(text);

    [Fact]
    public void Bold_and_italic_with_both_markers()
    {
        Assert.Equal([S("a "), S("b", MdStyle.Bold), S(" c "), S("d", MdStyle.Italic), S(" "), S("e", MdStyle.Bold), S(" "), S("f", MdStyle.Italic)],
            Inline("a **b** c *d* __e__ _f_"));
        Assert.Equal([S("x", MdStyle.Bold | MdStyle.Italic)], Inline("***x***"));
        Assert.Equal([S("a "), S("b ", MdStyle.Bold), S("c", MdStyle.Bold | MdStyle.Italic), S(" d", MdStyle.Bold)], Inline("a **b *c* d**"));
    }

    [Fact]
    public void Korean_particles_after_bold_still_close()
    {
        Assert.Equal([S("이름은 "), S("'숲의 모험가'", MdStyle.Bold), S("입니다.")], Inline("이름은 **'숲의 모험가'**입니다."));
        Assert.Equal([S("이건"), S("굵게", MdStyle.Bold), S("다")], Inline("이건**굵게**다"));
    }

    [Theory]
    [InlineData("snake_case_name")]
    [InlineData("2*3*4")]
    [InlineData("a*b and c*d")]
    [InlineData("2 * 3 * 4")]
    [InlineData("**not closed")]
    [InlineData("*.png and *.jpg")]
    [InlineData("file*.txt or b*.c")]
    [InlineData("__init")]
    [InlineData("`unclosed code")]
    [InlineData("[text](javascript:alert(1))")]
    [InlineData("[text](ftp://example.com)")]
    [InlineData("[text](not a url)")]
    public void Unbalanced_or_intraword_markers_stay_literal(string text)
    {
        Assert.Equal([S(text)], Inline(text));
    }

    [Fact]
    public void Inline_code_keeps_markers_and_nests_inside_bold()
    {
        Assert.Equal([S("run "), S("a **b** c_d", MdStyle.Code)], Inline("run `a **b** c_d`"));
        Assert.Equal([S("use ", MdStyle.Bold), S("x", MdStyle.Bold | MdStyle.Code), S(" now", MdStyle.Bold)], Inline("**use `x` now**"));
        Assert.Equal([S("a ` b", MdStyle.Code)], Inline("`` a ` b ``"));
    }

    [Fact]
    public void Backslash_escapes_markers()
    {
        Assert.Equal([S("*a*")], Inline(@"\*a\*"));
    }

    [Fact]
    public void Links_only_http_and_https()
    {
        Assert.Equal([S("see "), S("site", MdStyle.None, "https://lospec.com"), S(".")], Inline("see [site](https://lospec.com)."));
        Assert.Equal([S("bold", MdStyle.Bold, "http://a.com/x_(y)")], Inline("[**bold**](http://a.com/x_(y))"));
        Assert.Equal([S("go "), S("https://a.com/p?q=1", MdStyle.None, "https://a.com/p?q=1"), S(".")], Inline("go https://a.com/p?q=1."));
        Assert.Equal([S("https://lospec.com", MdStyle.None, "https://lospec.com"), S("에서 찾으세요")], Inline("https://lospec.com에서 찾으세요"));
        Assert.Equal([S("("), S("https://a.com", MdStyle.None, "https://a.com"), S(")")], Inline("(https://a.com)"));
        Assert.Equal([S("https://a.com", MdStyle.Bold, "https://a.com")], Inline("**https://a.com**"));
        Assert.Equal([S("my_file_name "), S("https://a.com/x_y_z", MdStyle.None, "https://a.com/x_y_z")], Inline("my_file_name https://a.com/x_y_z"));
    }

    [Fact]
    public void Line_breaks_stay_inside_paragraph()
    {
        var blocks = MiniMarkdown.Parse("first **line**\nsecond line\n\nnext paragraph");
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(MdBlockKind.Paragraph, b.Kind));
        Assert.Equal([S("first "), S("line", MdStyle.Bold), S("\nsecond line")], blocks[0].Spans);
        Assert.Equal("next paragraph", Assert.Single(blocks[1].Spans).Text);
    }

    [Fact]
    public void Headings_levels_and_closing_hashes()
    {
        var blocks = MiniMarkdown.Parse("# One\n## Two ##\n### C#\n#### Four\n#nospace");
        Assert.Equal([1, 2, 3, 3], blocks.Take(4).Select(b => b.Level));
        Assert.All(blocks.Take(4), b => Assert.Equal(MdBlockKind.Heading, b.Kind));
        Assert.Equal(["One", "Two", "C#", "Four"], blocks.Take(4).Select(b => b.Spans.Single().Text));
        Assert.Equal(MdBlockKind.Paragraph, blocks[4].Kind);
    }

    [Fact]
    public void Lists_nest_by_indent()
    {
        var blocks = MiniMarkdown.Parse("Steps:\n1. first\n   - sub *a*\n     - deeper\n   - sub b\n2. second\n  continued\n* star\n+ plus");
        Assert.Equal(MdBlockKind.Paragraph, blocks[0].Kind);
        var items = blocks.Skip(1).ToList();
        Assert.All(items, b => Assert.Equal(MdBlockKind.ListItem, b.Kind));
        Assert.Equal([0, 1, 2, 1, 0, 0, 0], items.Select(b => b.Level));
        Assert.Equal(["1.", null, null, null, "2.", null, null], items.Select(b => b.Number));
        Assert.Equal([S("sub "), S("a", MdStyle.Italic)], items[1].Spans);
        Assert.Equal("second\ncontinued", items[4].Spans.Single().Text);
    }

    [Fact]
    public void Numbered_line_other_than_one_does_not_interrupt_paragraph()
    {
        var blocks = MiniMarkdown.Parse("It was made in\n2024. Then more.");
        Assert.Equal(MdBlockKind.Paragraph, Assert.Single(blocks).Kind);
    }

    [Fact]
    public void Fenced_code_keeps_content_verbatim()
    {
        var code = "var a = **b**; // _x_\n\n  indented `tick`\n# not heading";
        var blocks = MiniMarkdown.Parse($"before\n```csharp\n{code}\n```\nafter");
        Assert.Equal([MdBlockKind.Paragraph, MdBlockKind.Code, MdBlockKind.Paragraph], blocks.Select(b => b.Kind));
        Assert.Equal(code, blocks[1].Code);
        Assert.Equal("after", blocks[2].Spans.Single().Text);
    }

    [Fact]
    public void Unclosed_fence_runs_to_end()
    {
        var blocks = MiniMarkdown.Parse("text\n```\nline **1**\nline 2");
        Assert.Equal(MdBlockKind.Code, blocks[^1].Kind);
        Assert.Equal("line **1**\nline 2", blocks[^1].Code);
    }

    [Fact]
    public void Quote_contains_blocks()
    {
        var blocks = MiniMarkdown.Parse("> quoted **text**\n> - item\n\nafter");
        var quote = blocks[0];
        Assert.Equal(MdBlockKind.Quote, quote.Kind);
        Assert.Equal([MdBlockKind.Paragraph, MdBlockKind.ListItem], quote.Children.Select(b => b.Kind));
        Assert.Equal([S("quoted "), S("text", MdStyle.Bold)], quote.Children[0].Spans);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
    }

    [Fact]
    public void Streaming_prefix_keeps_earlier_block_sources()
    {
        var full = "# Title\n\nFirst para.\n\n- a\n- b **bold**";
        var partial = MiniMarkdown.Parse(full[..^5]);
        var done = MiniMarkdown.Parse(full);
        Assert.Equal(done.Count, partial.Count);
        for (int i = 0; i < done.Count - 1; i++) Assert.Equal(done[i].Source, partial[i].Source);
        Assert.Equal([S("b **b")], partial[^1].Spans);
        Assert.Equal([S("b "), S("bold", MdStyle.Bold)], done[^1].Spans);
    }

    [Fact]
    public void H4_to_h6_render_as_level_three()
    {
        var blocks = MiniMarkdown.Parse("#### Four\n##### Five\n###### Six\n####### Seven");
        Assert.Equal([3, 3, 3], blocks.Take(3).Select(b => b.Level));
        Assert.All(blocks.Take(3), b => Assert.Equal(MdBlockKind.Heading, b.Kind));
        Assert.Equal(["Four", "Five", "Six"], blocks.Take(3).Select(b => b.Spans.Single().Text));
        Assert.Equal(MdBlockKind.Paragraph, blocks[3].Kind);
    }

    [Fact]
    public void Table_with_alignment_and_inline_cells()
    {
        var blocks = MiniMarkdown.Parse("| 순서 | 레이어 이름 | 수 |\n|---|:---:|--:|\n| 1 | `bg_far` | **3** |\n| 2 | [link](https://a.example) | 4 |");
        var table = Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, table.Kind);
        Assert.Equal([MdAlign.Left, MdAlign.Center, MdAlign.Right], table.Columns);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal([S("순서")], table.Rows[0][0]);
        Assert.Equal([S("레이어 이름")], table.Rows[0][1]);
        Assert.Equal([S("bg_far", MdStyle.Code)], table.Rows[1][1]);
        Assert.Equal([S("3", MdStyle.Bold)], table.Rows[1][2]);
        Assert.Equal([S("link", url: "https://a.example")], table.Rows[2][1]);
    }

    [Fact]
    public void Table_without_outer_pipes_and_escaped_pipe()
    {
        var blocks = MiniMarkdown.Parse("a | b\n:-- | ---\n`x \\| y` | c \\| d");
        var table = Assert.Single(blocks);
        Assert.Equal(MdBlockKind.Table, table.Kind);
        Assert.Equal([MdAlign.Left, MdAlign.Left], table.Columns);
        Assert.Equal([S("x | y", MdStyle.Code)], table.Rows[1][0]);
        Assert.Equal([S("c | d")], table.Rows[1][1]);
    }

    [Fact]
    public void Table_rows_are_padded_or_trimmed_to_header()
    {
        var table = MiniMarkdown.Parse("| a | b | c |\n|---|---|---|\n| 1 |\n| 1 | 2 | 3 | 4 |")[0];
        Assert.All(table.Rows, r => Assert.Equal(3, r.Count));
        Assert.Empty(table.Rows[1][1]);
        Assert.Empty(table.Rows[1][2]);
        Assert.Equal([S("3")], table.Rows[2][2]);
    }

    [Theory]
    [InlineData("| a | b |")]
    [InlineData("| a | b |\n|---")]
    [InlineData("| a | b |\n|---|---|---|")]
    [InlineData("| a | b |\n| x | y |")]
    [InlineData("a\n---|x")]
    public void Table_needs_matching_delimiter_row(string text)
    {
        Assert.All(MiniMarkdown.Parse(text), b => Assert.NotEqual(MdBlockKind.Table, b.Kind));
    }

    [Fact]
    public void Table_ends_at_blank_or_pipeless_line()
    {
        var blocks = MiniMarkdown.Parse("Intro:\n| a | b |\n|---|---|\n| 1 | 2 |\nafter text\n\n| c | d |\n|---|---|\n\nend");
        Assert.Equal([MdBlockKind.Paragraph, MdBlockKind.Table, MdBlockKind.Paragraph, MdBlockKind.Table, MdBlockKind.Paragraph], blocks.Select(b => b.Kind));
        Assert.Equal("Intro:", blocks[0].Spans.Single().Text);
        Assert.Equal(2, blocks[1].Rows.Count);
        Assert.Equal("after text", blocks[2].Spans.Single().Text);
        Assert.Single(blocks[3].Rows);
    }

    [Fact]
    public void Rules_versus_lists_and_setext()
    {
        var blocks = MiniMarkdown.Parse("---\n***\n___\n- - -\n * * *\n- item\ntext\n---\nmore\n===\n--\n-- x");
        Assert.Equal(
            [MdBlockKind.Rule, MdBlockKind.Rule, MdBlockKind.Rule, MdBlockKind.Rule, MdBlockKind.Rule, MdBlockKind.ListItem,
             MdBlockKind.Paragraph, MdBlockKind.Rule, MdBlockKind.Paragraph],
            blocks.Select(b => b.Kind));
        Assert.Equal("text", blocks[6].Spans.Single().Text);
        Assert.Equal("more\n===\n--\n-- x", blocks[8].Spans.Single().Text);
    }

    [Fact]
    public void Strikethrough_needs_balanced_double_tildes()
    {
        Assert.Equal([S("old", MdStyle.Strike)], Inline("~~old~~"));
        Assert.Equal([S("a "), S("b ", MdStyle.Strike), S("c", MdStyle.Strike | MdStyle.Bold)], Inline("a ~~b **c**~~"));
        Assert.Equal([S("a"), S("b", MdStyle.Strike), S("c")], Inline("a~~b~~c"));
        Assert.Equal([S("a~~b")], Inline("a~~b"));
        Assert.Equal([S("~~ a ~~")], Inline("~~ a ~~"));
        Assert.Equal([S("~a~ and ~~~b~~~")], Inline("~a~ and ~~~b~~~"));
        Assert.Equal([S("~~x~~")], Inline(@"\~~x~~"));
    }

    [Fact]
    public void Streaming_table_keeps_earlier_block_sources()
    {
        var full = "Intro text.\n\n| a | b |\n|---|---|\n| `x` | y |\n\n---\n\n#### Done ~~old~~";
        var done = MiniMarkdown.Parse(full);
        Assert.Equal([MdBlockKind.Paragraph, MdBlockKind.Table, MdBlockKind.Rule, MdBlockKind.Heading], done.Select(b => b.Kind));
        for (int len = 1; len < full.Length; len++)
        {
            var partial = MiniMarkdown.Parse(full[..len]);
            // 끝 블록 말고는 다 끝난 블록과 같은 원문이다.
            for (int i = 0; i < partial.Count - 1; i++)
            {
                Assert.Equal(done[i].Kind, partial[i].Kind);
                Assert.Equal(done[i].Source, partial[i].Source);
            }
        }
        // 구분 줄이 덜 왔으면 머리 줄은 문단으로 남는다.
        Assert.Equal(MdBlockKind.Paragraph, MiniMarkdown.Parse(full[..full.IndexOf("|---")])[^1].Kind);
    }
}
