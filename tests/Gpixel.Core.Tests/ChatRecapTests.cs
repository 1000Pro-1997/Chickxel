using Gpixel.Core.IO;

namespace Gpixel.Core.Tests;

public class ChatRecapTests
{
    private static ChatItem U(string text, int images = 0) => new() { Kind = ChatItemKind.User, Text = text, ImageCount = images };
    private static ChatItem A(string text) => new() { Kind = ChatItemKind.Assistant, Text = text };
    private static ChatItem T(string text, bool error = false) => new() { Kind = ChatItemKind.Tool, Text = text, Error = error };
    private static ChatItem F(string text) => new() { Kind = ChatItemKind.Footer, Text = text };

    [Fact]
    public void RemoveLastTurn_drops_last_user_message_and_everything_after()
    {
        var items = new List<ChatItem>
        {
            new() { Kind = ChatItemKind.Notice, Text = "model" },
            U("one"), A("a1"), F("1s"),
            U("two"), T("• fill · x=1"), new() { Kind = ChatItemKind.Snapshot, Images = ["png"] }, F("2s"),
        };
        var removed = ChatRecap.RemoveLastTurn(items);
        Assert.Equal("two", removed?.Text);
        Assert.Equal(["model", "one", "a1", "1s"], items.Select(i => i.Text));

        Assert.Equal("one", ChatRecap.RemoveLastTurn(items)?.Text);
        Assert.Equal(["model"], items.Select(i => i.Text));
        Assert.False(ChatRecap.HasTurns(items));
        Assert.Null(ChatRecap.RemoveLastTurn(items));
        Assert.Single(items);
    }

    [Fact]
    public void Build_lists_user_and_assistant_text_and_summarizes_tools()
    {
        var recap = ChatRecap.Build(
        [
            new ChatItem { Kind = ChatItemKind.Notice, Text = "ignored notice" },
            U("draw a slime", images: 1),
            T("• draw_pixels · rows=[16 rows]"),
            T("• fill · x=1 — out of bounds", error: true),
            T("• draw_pixels · rows=[2 rows]"),
            A("Done."),
            F("$0.010 · 3.0s"),
            U("make it blue"),
            A("Recolored."),
        ]);
        Assert.Equal(
            "User: draw a slime [1 image(s) attached]\n\n" +
            "(Assistant used tools: draw_pixels, fill)\n\n" +
            "Assistant: Done.\n\n" +
            "User: make it blue\n\n" +
            "Assistant: Recolored.",
            recap);
    }

    [Fact]
    public void Build_returns_null_without_user_turns()
    {
        Assert.Null(ChatRecap.Build([]));
        Assert.Null(ChatRecap.Build([new ChatItem { Kind = ChatItemKind.Notice, Text = "x" }]));
    }

    [Fact]
    public void Build_keeps_the_most_recent_part_when_too_long()
    {
        var items = new List<ChatItem>();
        for (int i = 0; i < 20; i++)
        {
            items.Add(U($"question {i} " + new string('q', 80)));
            items.Add(A($"answer {i} " + new string('a', 80)));
        }
        var recap = ChatRecap.Build(items, maxChars: 500)!;
        Assert.StartsWith(ChatRecap.Omitted, recap);
        Assert.EndsWith("answer 19 " + new string('a', 80), recap);
        Assert.DoesNotContain("question 0 ", recap);
        Assert.True(recap.Length <= 500 + ChatRecap.Omitted.Length + 2);

        // 가장 최근 문단 하나가 넘치면 그 끝부분만
        var single = ChatRecap.Build([U(new string('x', 100) + "END")], maxChars: 20)!;
        Assert.EndsWith("END", single);
        Assert.True(single.Length <= 20 + ChatRecap.Omitted.Length + 2);
    }

    [Fact]
    public void Compose_puts_recap_before_the_new_message()
    {
        var text = ChatRecap.Compose("User: hi", "next");
        Assert.Equal($"{ChatRecap.Header}\nUser: hi\n\n{ChatRecap.NewMessage}\nnext", text);
    }

    [Fact]
    public void NeedsRecap_round_trips_through_the_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "gpixel-recap-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ChatHistoryStore(root);
            var chat = new SavedChat { Id = "c1", Provider = "claude", Title = "t", NeedsRecap = true, Items = [U("hi")] };
            store.Save(chat);
            var loaded = store.Load("claude", "c1");
            Assert.NotNull(loaded);
            Assert.True(loaded.NeedsRecap);
            Assert.Null(loaded.SessionId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
