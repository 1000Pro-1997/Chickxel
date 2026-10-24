using Gpixel.Core.Help;

namespace Gpixel.Core.Tests;

public class HelpSearchTests
{
    private static readonly HelpDocument[] KoreanDocs =
    [
        new("canvas", "캔버스", "가운데 캔버스에서 그림을 그립니다. 확대와 축소는 마우스 휠로 합니다."),
        new("palette", "팔레트 사용", "팔레트에서 색을 고르고, 팔레트를 저장하거나 불러옵니다. 색을 끌어 순서를 바꿉니다."),
        new("timeline", "타임라인", "프레임과 레이어를 한눈에 봅니다. 프레임을 끌어 순서를 바꿉니다."),
    ];

    [Fact]
    public void Korean_word_with_particle_finds_the_page_about_it()
    {
        var results = new HelpSearch(KoreanDocs).Search("팔레트가");

        Assert.NotEmpty(results);
        Assert.Equal("palette", results[0].PageId);
    }

    [Fact]
    public void Title_match_outranks_body_only_match()
    {
        var docs = new[]
        {
            new HelpDocument("body", "레이어 편집", "레이어를 고친 뒤에 내보내기 창을 열어 결과를 봅니다. 레이어 순서와 이름도 바꿀 수 있습니다."),
            new HelpDocument("title", "내보내기", "저장할 때 PNG 시트를 씁니다. 파일 이름과 폴더를 고릅니다."),
            new HelpDocument("other", "타임라인", "프레임을 끌어 순서를 바꿉니다."),
        };

        var results = new HelpSearch(docs).Search("내보내기");

        Assert.Equal(["title", "body"], results.Select(r => r.PageId).ToArray());
    }

    [Fact]
    public void English_partial_word_is_case_insensitive()
    {
        var docs = new[]
        {
            new HelpDocument("palette", "Using palettes", "Pick colors from the Palette panel and save palettes to files."),
            new HelpDocument("canvas", "Canvas", "Draw on the canvas in the middle of the window."),
        };

        var results = new HelpSearch(docs).Search("PALET");

        Assert.NotEmpty(results);
        Assert.Equal("palette", results[0].PageId);
        Assert.DoesNotContain(results, r => r.PageId == "canvas");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" .,!? ")]
    public void Empty_query_returns_nothing(string query)
    {
        Assert.Empty(new HelpSearch(KoreanDocs).Search(query));
        Assert.Empty(HelpSearch.HighlightTerms(query));
    }

    [Fact]
    public void Snippet_shows_text_around_the_query_word()
    {
        var filler = string.Concat(Enumerable.Repeat("앞부분 설명입니다. ", 20));
        var docs = new[] { new HelpDocument("p", "제목", filler + "여기에 양파 껍질 설명이 있습니다." + filler) };

        var result = Assert.Single(new HelpSearch(docs).Search("양파"));

        Assert.Contains("양파", result.Snippet);
        Assert.True(result.Snippet.Length <= 82, result.Snippet);
        Assert.StartsWith("…", result.Snippet);
    }

    [Fact]
    public void Snippet_and_highlight_fall_back_to_the_word_without_its_particle()
    {
        var result = new HelpSearch(KoreanDocs).Search("팔레트가")[0];

        Assert.Contains("팔레트", result.Snippet);
        Assert.Equal(["팔레트"], HelpSearch.ResolveTerms(KoreanDocs[1].Text, HelpSearch.HighlightTerms("팔레트가")));
    }

    [Fact]
    public void Highlight_terms_are_distinct_lowercase_words_and_matches_ignore_case()
    {
        var terms = HelpSearch.HighlightTerms("Ctrl+Z ctrl  Undo");

        Assert.Equal(["ctrl", "z", "undo"], terms);
        Assert.Equal([(0, 4), (8, 4)], HelpSearch.Matches("Ctrl or CTRL", ["ctrl"]));
        Assert.Equal([(0, 6)], HelpSearch.Matches("abcdef", ["abcd", "cdef"]));
    }
}
