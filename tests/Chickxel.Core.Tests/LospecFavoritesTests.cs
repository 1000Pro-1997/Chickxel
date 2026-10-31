using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class LospecFavoritesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chickxel-lospec-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static LospecPalette P(string slug, int colors = 4, params string[] tags) => new(
        "Title " + slug, slug, "author-" + slug,
        [.. Enumerable.Range(0, colors).Select(i => new Color32((byte)(i * 10), 20, 30))],
        12, 345, tags,
        [new Uri($"https://cdn.lospec.com/gallery/{slug}-a.png"), new Uri($"https://cdn.lospec.com/gallery/{slug}-b.png")]);

    [Fact]
    public void Toggle_adds_then_removes_and_persists()
    {
        var store = LospecFavoriteStore.Load(_root);
        Assert.True(store.Toggle(P("sweetie-16"), T0));
        Assert.True(store.Contains("sweetie-16"));
        Assert.True(File.Exists(Path.Combine(_root, LospecFavoriteStore.FileName)));
        Assert.True(LospecFavoriteStore.Load(_root).Contains("sweetie-16"));

        Assert.False(store.Toggle(P("sweetie-16"), T0.AddMinutes(1)));
        Assert.False(store.Contains("sweetie-16"));
        Assert.Empty(LospecFavoriteStore.Load(_root).Items);
    }

    [Fact]
    public void Round_trip_keeps_everything_needed_offline()
    {
        var original = P("pico-8", 16, "pico8", "fantasy console");
        LospecFavoriteStore.Load(_root).Add(original, T0);

        var f = Assert.Single(LospecFavoriteStore.Load(_root).Items);
        Assert.Equal(T0, f.AddedUtc);
        var p = f.ToPalette();
        Assert.Equal(original.Slug, p.Slug);
        Assert.Equal(original.Title, p.Title);
        Assert.Equal(original.Author, p.Author);
        Assert.Equal(original.Colors, p.Colors);
        Assert.Equal(original.Tags, p.Tags);
        Assert.Equal(original.Likes, p.Likes);
        Assert.Equal(original.Downloads, p.Downloads);
        Assert.Equal(original.Examples, p.Examples);
    }

    [Fact]
    public void Items_are_newest_first()
    {
        var store = LospecFavoriteStore.Load(_root);
        store.Add(P("a"), T0.AddMinutes(1));
        store.Add(P("b"), T0.AddMinutes(5));
        store.Add(P("c"), T0.AddMinutes(3));
        Assert.Equal(["b", "c", "a"], store.Items.Select(f => f.Slug));
        Assert.Equal(["b", "c", "a"], LospecFavoriteStore.Load(_root).Items.Select(f => f.Slug));
    }

    [Fact]
    public void Adding_twice_keeps_the_first_time()
    {
        var store = LospecFavoriteStore.Load(_root);
        store.Add(P("a"), T0);
        store.Add(P("a"), T0.AddDays(1));
        Assert.Equal(T0, Assert.Single(store.Items).AddedUtc);
    }

    [Fact]
    public void Corrupt_file_is_tolerated()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, LospecFavoriteStore.FileName), "{ not json");
        var store = LospecFavoriteStore.Load(_root);
        Assert.Empty(store.Items);
        Assert.True(store.Toggle(P("a"), T0));
        Assert.Single(LospecFavoriteStore.Load(_root).Items);
    }

    [Fact]
    public void Entries_without_slug_or_colors_are_skipped()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, LospecFavoriteStore.FileName),
            """[{"Slug":"","Colors":["#000000"]},{"Slug":"x","Colors":[]},{"Slug":"ok","Colors":["#112233"],"Title":null}, null]""");
        var f = Assert.Single(LospecFavoriteStore.Load(_root).Items);
        Assert.Equal("ok", f.Slug);
        Assert.Equal("ok", f.ToPalette().Title);
    }

    [Fact]
    public void Examples_are_kept_only_for_favorites_and_removed_with_them()
    {
        var store = LospecFavoriteStore.Load(_root);
        Assert.False(store.WriteExample("a", 0, [1, 2, 3]));
        store.Add(P("a"), T0);
        Assert.Equal([0, 1], store.MissingExamples("a"));
        Assert.True(store.WriteExample("a", 1, [1, 2, 3]));
        Assert.Equal(Path.Combine(_root, "examples", "a", "1.png"), store.ExamplePath("a", 1));
        Assert.Equal([1, 2, 3], store.ReadExample("a", 1));
        Assert.Equal([0], store.MissingExamples("a"));

        store.Remove("a");
        Assert.False(Directory.Exists(Path.Combine(_root, "examples", "a")));
        Assert.Null(store.ReadExample("a", 1));
    }

    [Fact]
    public void Unsafe_slugs_never_become_paths()
    {
        var store = LospecFavoriteStore.Load(_root);
        Assert.Null(store.ExamplePath("../evil", 0));
        Assert.Null(store.ExamplePath("a", LospecFavoriteStore.MaxExamples));
        Assert.False(LospecFavoriteStore.IsSafeSlug("A b"));
        Assert.True(LospecFavoriteStore.IsSafeSlug("sweetie-16"));
    }

    [Fact]
    public void Filter_by_color_count_and_title_or_tag()
    {
        var store = LospecFavoriteStore.Load(_root);
        store.Add(P("gb", 4, "gameboy"), T0.AddMinutes(1));
        store.Add(P("pico", 16, "pico8"), T0.AddMinutes(2));
        store.Add(P("big", 32), T0.AddMinutes(3));

        string[] Slugs(LospecColorFilter filter, int count, string text)
            => [.. LospecFavoriteStore.Filter(store.Items, filter, count, text).Select(f => f.Slug)];

        Assert.Equal(["big", "pico", "gb"], Slugs(LospecColorFilter.Any, 8, ""));
        Assert.Equal(["gb"], Slugs(LospecColorFilter.Max, 8, ""));
        Assert.Equal(["big", "pico"], Slugs(LospecColorFilter.Min, 16, ""));
        Assert.Equal(["pico"], Slugs(LospecColorFilter.Exact, 16, ""));
        Assert.Equal(["gb"], Slugs(LospecColorFilter.Any, 8, "GAMEBOY"));
        Assert.Equal(["pico"], Slugs(LospecColorFilter.Any, 8, "title PI"));
        Assert.Empty(Slugs(LospecColorFilter.Max, 8, "pico"));
    }
}
