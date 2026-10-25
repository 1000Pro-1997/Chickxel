using Gpixel.Core.IO;

namespace Gpixel.Core.Tests;

public class ChatImageTokenTests
{
    private static string T(int n, string label = "이미지") => ChatImageTokens.Token(label, n);

    [Fact]
    public void Find_only_matches_marked_tokens()
    {
        var text = $"look {T(1)} and [Image #2] then {T(2)}";
        var tokens = ChatImageTokens.Find(text);
        Assert.Equal([1, 2], tokens.Select(t => t.Number));
        Assert.Equal(text.IndexOf(ChatImageTokens.Mark), tokens[0].Start);
        Assert.Equal(T(1).Length, tokens[0].Length);
        Assert.False(ChatImageTokens.HasTokens("plain [Image #1]"));
    }

    [Fact]
    public void Model_and_plain_text_replace_tokens()
    {
        var text = $"{T(1)} 이 캐릭터를 {T(2)} 스타일로";
        Assert.Equal("[Image #1] 이 캐릭터를 [Image #2] 스타일로", ChatImageTokens.ToModel(text));
        Assert.Equal("[이미지 #1] 이 캐릭터를 [이미지 #2] 스타일로", ChatImageTokens.Plain(text, "이미지"));
        Assert.Equal("이 캐릭터를  스타일로", ChatImageTokens.WithoutTokens(text));
        Assert.DoesNotContain(ChatImageTokens.Mark, ChatImageTokens.ToModel(text + ChatImageTokens.Mark));
    }

    [Fact]
    public void Relabel_keeps_numbers()
    {
        var stored = ChatImageTokens.Relabel($"a {T(2)} b {T(1)}", ChatImageTokens.StoredLabel);
        Assert.Equal($"a {T(2, "Image")} b {T(1, "Image")}", stored);
        Assert.Equal([2, 1], ChatImageTokens.Find(stored).Select(t => t.Number));
    }

    [Fact]
    public void Split_returns_text_and_image_segments_in_order()
    {
        var segments = ChatImageTokens.Split($"{T(1)} vs {T(2)}!");
        Assert.Equal(4, segments.Count);
        Assert.True(segments[0].IsImage);
        Assert.Equal(1, segments[0].Image);
        Assert.Equal(" vs ", segments[1].Text);
        Assert.Equal(2, segments[2].Image);
        Assert.Equal("!", segments[3].Text);
        Assert.Empty(ChatImageTokens.Split(""));
    }

    [Fact]
    public void Normalize_renumbers_in_text_order_and_reports_attachment_order()
    {
        // 첨부 3개, 글에는 #3, #1만 남음(#2는 지워짐) → 새 #1 = 옛 #3, 새 #2 = 옛 #1.
        var text = $"x {T(3)} y {T(1)} z";
        int caret = text.Length;
        var result = ChatImageTokens.Normalize(text, 3, "이미지", out var order, ref caret);
        Assert.Equal([2, 0], order);
        Assert.Equal($"x {T(1)} y {T(2)} z", result);
        Assert.Equal(result.Length, caret);
    }

    [Fact]
    public void Normalize_drops_unknown_and_duplicate_tokens_and_relabels()
    {
        var text = $"{T(1, "Image")} {T(1, "Image")} {T(5)}";
        int caret = 0;
        var result = ChatImageTokens.Normalize(text, 2, "이미지", out var order, ref caret);
        Assert.Equal([0], order);
        Assert.Equal($"{T(1)}  ", result);
        Assert.Equal(0, caret);
    }

    [Fact]
    public void Normalize_moves_caret_out_of_tokens_and_keeps_it_in_text()
    {
        var text = $"ab {T(1)} cd";
        int inside = text.IndexOf('[') + 2;
        var result = ChatImageTokens.Normalize(text, 1, "이미지", out _, ref inside);
        Assert.Equal(text, result);
        Assert.Equal(3 + T(1).Length, inside);
        int inText = text.Length - 1;
        ChatImageTokens.Normalize(text, 1, "이미지", out _, ref inText);
        Assert.Equal(text.Length - 1, inText);
    }

    [Fact]
    public void Normalize_removes_broken_token_residue()
    {
        var token = T(1);
        // 끝 표시 글자가 지워진 칩, 앞 표시 글자가 지워진 칩, 떠도는 표시 글자.
        var brokenEnd = "a " + token[..^1] + " b";
        var brokenStart = "a " + token[1..] + " b";
        foreach (var text in new[] { brokenEnd, brokenStart })
        {
            int caret = 0;
            var result = ChatImageTokens.Normalize(text, 1, "이미지", out var order, ref caret);
            Assert.Empty(order);
            Assert.Equal("a  b", result);
        }
        int c = 0;
        Assert.Equal("ab", ChatImageTokens.Normalize("a" + ChatImageTokens.Mark + "b", 0, "x", out _, ref c));
    }

    [Fact]
    public void At_finds_token_before_or_after_caret()
    {
        var text = $"a {T(1)} b";
        var t = ChatImageTokens.Find(text)[0];
        Assert.Equal(t, ChatImageTokens.At(text, t.End, backward: true));
        Assert.Null(ChatImageTokens.At(text, t.End, backward: false));
        Assert.Equal(t, ChatImageTokens.At(text, t.Start, backward: false));
        Assert.Null(ChatImageTokens.At(text, t.Start, backward: true));
        Assert.Equal(t, ChatImageTokens.At(text, t.Start + 3, backward: true));
        Assert.Null(ChatImageTokens.At(text, 0, backward: false));
    }

    [Fact]
    public void Recap_uses_model_tokens_instead_of_image_count()
    {
        var items = new List<ChatItem>
        {
            new() { Kind = ChatItemKind.User, Text = $"draw {T(1, "Image")} bigger", ImageCount = 1 },
            new() { Kind = ChatItemKind.User, Text = "old", ImageCount = 2 },
        };
        var recap = ChatRecap.Build(items)!;
        Assert.Contains("User: draw [Image #1] bigger", recap);
        Assert.DoesNotContain("draw [Image #1] bigger [1 image(s) attached]", recap);
        Assert.Contains("User: old [2 image(s) attached]", recap);
    }
}
