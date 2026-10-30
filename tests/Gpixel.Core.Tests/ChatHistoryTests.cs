using Gpixel.Core.IO;

namespace Gpixel.Core.Tests;

public class ChatHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gpixel-chat-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ChatSummary S(string id, int minutes, bool favorite = false) => new(id, id, null, T0.AddMinutes(minutes), favorite);

    private static SavedChat Chat(string id, int minutes, bool favorite = false, string provider = "claude") => new()
    {
        Id = id,
        Provider = provider,
        Title = "t-" + id,
        CreatedUtc = T0,
        UpdatedUtc = T0.AddMinutes(minutes),
        Favorite = favorite,
        Items = [new ChatItem { Kind = ChatItemKind.User, Text = "hi " + id }],
    };

    [Fact]
    public void Order_puts_favorites_first_then_newest()
    {
        var order = ChatHistoryStore.Order([S("a", 1), S("b", 5), S("c", 2, favorite: true), S("d", 9), S("e", 0, favorite: true)]);
        Assert.Equal(["c", "e", "d", "b", "a"], order.Select(c => c.Id));
    }

    [Fact]
    public void Evict_removes_oldest_non_favorites_down_to_the_limit()
    {
        var chats = new List<ChatSummary> { S("old-fav", 0, favorite: true), S("a", 1), S("b", 2), S("c", 3), S("new", 4) };
        Assert.Equal(["a", "b"], ChatHistoryStore.SelectEvict(chats, 3, "new").Select(c => c.Id));
        Assert.Empty(ChatHistoryStore.SelectEvict(chats, 5, "new"));
    }

    [Fact]
    public void Evict_never_removes_favorites_or_the_kept_chat()
    {
        var chats = new List<ChatSummary> { S("f1", 0, true), S("f2", 1, true), S("new", 2) };
        Assert.Empty(ChatHistoryStore.SelectEvict(chats, 2, "new"));
    }

    [Fact]
    public void Title_collapses_whitespace_and_trims_to_length()
    {
        Assert.Equal("Draw a slime", ChatHistoryStore.MakeTitle("  Draw\n a   slime \r\n"));
        var title = ChatHistoryStore.MakeTitle(new string('x', 60));
        Assert.Equal(ChatHistoryStore.TitleLength + 1, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var store = new ChatHistoryStore(_root);
        var chat = Chat("abc", 3);
        chat.Model = "opus";
        chat.SessionId = "session-1";
        chat.SessionDirectory = @"C:\work";
        chat.Items.Add(new ChatItem { Kind = ChatItemKind.Tool, Text = "draw_pixels", Error = true });
        chat.Items.Add(new ChatItem { Kind = ChatItemKind.AiDrawPrompt, Text = "full generation prompt\nwith original pose", PromptSummary = "character pose" });
        chat.Items.Add(new ChatItem { Kind = ChatItemKind.Snapshot, Images = ["iVBORw0KGgo="] });
        store.Save(chat);

        var back = store.Load("claude", "abc");
        Assert.NotNull(back);
        Assert.Equal("t-abc", back.Title);
        Assert.Equal("opus", back.Model);
        Assert.Equal("session-1", back.SessionId);
        Assert.Equal(@"C:\work", back.SessionDirectory);
        Assert.Equal(chat.UpdatedUtc, back.UpdatedUtc);
        Assert.Equal([ChatItemKind.User, ChatItemKind.Tool, ChatItemKind.AiDrawPrompt, ChatItemKind.Snapshot], back.Items.Select(i => i.Kind));
        Assert.True(back.Items[1].Error);
        Assert.Equal("full generation prompt\nwith original pose", back.Items[2].Text);
        Assert.Equal("character pose", back.Items[2].PromptSummary);
        Assert.Equal(["iVBORw0KGgo="], back.Items[3].Images!);
        Assert.Equal(["abc"], store.List("claude").Select(c => c.Id));
        Assert.Empty(store.List("codex"));
    }

    [Fact]
    public void Saving_past_the_limit_deletes_oldest_non_favorite_and_keeps_favorites()
    {
        var store = new ChatHistoryStore(_root);
        store.Save(Chat("fav", 0, favorite: true));
        for (int i = 1; i < ChatHistoryStore.MaxChats; i++) store.Save(Chat($"c{i:00}", i));
        Assert.Equal(ChatHistoryStore.MaxChats, store.List("claude").Count);

        var evicted = store.Save(Chat("newest", 100));
        Assert.Equal(["c01"], evicted);
        var list = store.List("claude");
        Assert.Equal(ChatHistoryStore.MaxChats, list.Count);
        Assert.Equal("fav", list[0].Id);
        Assert.Equal("newest", list[1].Id);
        Assert.Null(store.Load("claude", "c01"));
        Assert.NotNull(store.Load("claude", "fav"));
    }

    [Fact]
    public void Favorites_beyond_the_limit_are_kept()
    {
        var store = new ChatHistoryStore(_root);
        for (int i = 0; i < ChatHistoryStore.MaxChats; i++) store.Save(Chat($"f{i:00}", i, favorite: true));
        Assert.Empty(store.Save(Chat("plain", 50)));
        Assert.Equal(ChatHistoryStore.MaxChats + 1, store.List("claude").Count);
    }

    [Fact]
    public void Favorite_toggle_moves_chat_to_top_and_persists()
    {
        var store = new ChatHistoryStore(_root);
        store.Save(Chat("a", 1));
        store.Save(Chat("b", 2));
        Assert.Equal(["b", "a"], store.List("claude").Select(c => c.Id));
        Assert.True(store.SetFavorite("claude", "a", true));
        Assert.Equal(["a", "b"], store.List("claude").Select(c => c.Id));
        Assert.True(store.Load("claude", "a")!.Favorite);
        Assert.True(store.List("claude")[0].Favorite);
        Assert.False(store.SetFavorite("claude", "missing", true));
    }

    [Fact]
    public void Delete_removes_chat_and_index_is_rebuilt_when_broken()
    {
        var store = new ChatHistoryStore(_root);
        store.Save(Chat("a", 1));
        store.Save(Chat("b", 2, favorite: true));
        Assert.True(store.Delete("claude", "a"));
        Assert.Equal(["b"], store.List("claude").Select(c => c.Id));

        File.WriteAllText(Path.Combine(_root, "claude", ChatHistoryStore.IndexName), "{ broken");
        var rebuilt = store.List("claude");
        Assert.Equal(["b"], rebuilt.Select(c => c.Id));
        Assert.True(rebuilt[0].Favorite);
    }

    [Fact]
    public void Providers_keep_separate_lists_and_names_are_validated()
    {
        var store = new ChatHistoryStore(_root);
        store.Save(Chat("a", 1, provider: "claude"));
        store.Save(Chat("b", 1, provider: "codex"));
        Assert.Equal(["a"], store.List("claude").Select(c => c.Id));
        Assert.Equal(["b"], store.List("codex").Select(c => c.Id));
        Assert.Null(store.Load("claude", "b"));
        Assert.Null(store.Load("claude", "../codex/b"));
        Assert.False(ChatHistoryStore.IsValidName("../x"));
        Assert.Throws<ArgumentException>(() => store.Save(Chat("a", 1, provider: "Bad/Name")));
    }
}
