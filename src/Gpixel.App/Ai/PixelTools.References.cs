using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Gpixel.Core.Help;
using Gpixel.Core.Model;
using Gpixel.Core.References;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

/// <summary>
/// 참고 자료 도구: 보관소(<see cref="IAiHost.References"/>) 찾기·보기, 그림 규칙, 지금 그림을 자료로 넣기, 설명 붙이기.
/// 문서를 고치지 않으므로 실행취소 기록을 남기지 않고, 넣기·설명 붙이기도 "지금 그림만"에서 쓸 수 있다.
/// 찾기에는 사용 가이드(도움말 쪽) 결과도 두 개까지 붙인다.
/// </summary>
public sealed partial class PixelTools
{
    /// <summary>search_references 결과 팔레트 색 수 상한.</summary>
    private const int ReferencePaletteShown = 16;
    /// <summary>get_reference·get_style_rules 글 상한(글자).</summary>
    private const int MaxReferenceText = 12000;
    /// <summary>get_reference가 프레임을 나란히 보여 주는 최대 수.</summary>
    private const int MaxStripFrames = 16;

    private static readonly string[] KindValues = [.. ReferenceKinds.All];

    private static IEnumerable<JsonObject> ReferenceToolDefinitions()
    {
        yield return Tool("search_references",
            "Search the user's local reference library: built-in example sprites, references the user saved, technique notes, the built-in style " +
            "rules and the user's rules.md. Before drawing a new sprite or animation, search for similar items and follow their proportions, palette and " +
            "technique when relevant (look at them with get_reference). Returns JSON: `references` (images: id, title, description, tags, kind, width, " +
            "height, frames, colors = number of distinct colors, palette = up to 16 hex colors, most used first, score 0-1, builtIn), `notes` (notes and " +
            "rules matching the query: id, title, type, snippet) and `guide` (up to 2 Gpixel user-guide pages: page, title, snippet). Filters and " +
            "similarity apply to images only. Without query and filters it lists the most recently changed items.",
            new JsonObject
            {
                ["query"] = Str("Words to search in titles, tags, descriptions, file names and note text (any language, e.g. \"slime\", \"슬라임 점프\")."),
                ["kind"] = Enum("Only images of this kind.", KindValues),
                ["width"] = Int("Only images exactly this wide (px).", 1),
                ["height"] = Int("Only images exactly this tall (px).", 1),
                ["max_colors"] = Int("Only images with at most this many distinct colors.", 1),
                ["min_frames"] = Int("Only images with at least this many frames (2 = animations).", 1),
                ["similar_to_canvas"] = Bool("Rank images by palette and silhouette similarity to the current canvas (visible composite of the current frame); combined with the query score when a query is given."),
                ["similar_to"] = Str("Rank images by similarity to this reference id instead (the reference itself is excluded)."),
                ["limit"] = Int("Maximum number of references/notes (default 5).", 1, 20),
            });
        yield return Tool("get_reference",
            "Look at one reference from search_references. as=\"image\" (default) returns the frame enlarged with nearest neighbor over a light " +
            "checkerboard that is NOT part of the image; with several frames and no `frame`, all frames are drawn side by side in one strip (each frame " +
            "keeps its width). as=\"pixels\" returns the exact pixels of one frame in the get_pixels/draw_pixels text format (legend + rows, at most " +
            "128x128) so you can copy proportions and colors exactly. Notes and rules (ids from `notes`) return their text.",
            new JsonObject
            {
                ["id"] = Str("Reference id from search_references."),
                ["frame"] = Int("Frame index (default: all frames as a strip for as=image, frame 0 for as=pixels).", 0),
                ["as"] = Enum("Output form (default image).", "image", "pixels"),
                ["scale"] = Int("Integer zoom for as=image. Default: the largest factor <= 16 that keeps the long side <= 512 px.", 1, 16),
            },
            "id");
        yield return Tool("get_style_rules",
            "Return the pixel-art style rules to follow: the built-in Gpixel rules plus the user's own rules (rules.md in the reference folder; the " +
            "user's rules win where they differ). Call it once per conversation before drawing a new sprite or animation, then check your result " +
            "against the rules with get_image and fix problems.",
            new JsonObject());
        yield return Tool("add_reference",
            "Save the current canvas (visible composite) or one layer as a new reference in the user's library (a PNG, or a .gpixel when several " +
            "frames are saved) with a title, description, tags and kind. Use it when the user asks to keep the current drawing as a reference. This " +
            "only writes to the reference library, never to the document. Returns the new reference as JSON.",
            new JsonObject
            {
                ["title"] = Str("Short title (also used as the file name). Default: the document name."),
                ["description"] = Str("What it shows and which technique to imitate (size, outline, light direction, shading, proportions, timing)."),
                ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Search tags." },
                ["kind"] = Enum("Kind (default: guessed from the title, else other).", KindValues),
                ["from"] = Enum("canvas = visible composite (default), layer = one layer alone.", "canvas", "layer"),
                ["layer"] = Int("Layer index for from=layer (default: current layer).", 0),
                ["frame_range"] = Str("\"current\" (default), \"all\", or an inclusive range of frame indices like \"2-5\". Several frames keep the animation."),
            });
        yield return Tool("set_reference_info",
            "Set the title, description, tags and/or kind of a reference in the user's library (captioning). Write the description in the user's " +
            "language: what it shows and the technique worth imitating. Built-in examples and rules cannot be changed.",
            new JsonObject
            {
                ["id"] = Str("Reference id."),
                ["title"] = Str("New title."),
                ["description"] = Str("New description."),
                ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "New tags (replace the old ones)." },
                ["kind"] = Enum("New kind.", KindValues),
            },
            "id");
    }

    /// <summary>보관소(열 수 없으면 도구 오류). 앱 밖에서 넣은 파일도 보이게 먼저 다시 살펴본다.</summary>
    private ReferenceLibrary Library()
    {
        try
        {
            var lib = _host.References;
            lib.Rescan();
            return lib;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ToolError(UiStrings.FormatRefs_Tool_OpenFailed(message: e.Message));
        }
    }

    private static ReferenceItem FindReference(ReferenceLibrary lib, JsonObject? a)
    {
        var id = ReqString(a, "id").Trim();
        return lib.Find(id) ?? throw new ToolError(UiStrings.FormatRefs_Tool_NotFound(id: id));
    }

    private JsonObject SearchReferences(JsonObject? a)
    {
        var lib = Library();
        var query = OptString(a, "query")?.Trim();
        int limit = OptInt(a, "limit", 1, 20) ?? 5;
        ReferenceSignature? similar = null;
        string? exclude = null;
        if (OptString(a, "similar_to") is { Length: > 0 } similarId)
        {
            var target = lib.Find(similarId.Trim()) is { IsImage: true } t ? t : throw new ToolError(UiStrings.FormatRefs_Tool_NotFound(id: similarId));
            similar = ReferenceSignature.Of(target);
            exclude = target.Id;
        }
        else if (OptBool(a, "similar_to_canvas") == true)
        {
            var p = RequireProject();
            similar = ReferenceSignature.Of([p.Flatten(Math.Clamp(Ctx.CurrentFrame, 0, p.Frames.Count - 1))]);
        }
        var filter = new ReferenceQuery
        {
            Text = query,
            Kind = OptEnum(a, "kind", KindValues),
            Width = OptInt(a, "width", 1, PixelImage.MaxSize),
            Height = OptInt(a, "height", 1, PixelImage.MaxSize),
            MaxColors = OptInt(a, "max_colors", 1),
            MinFrames = OptInt(a, "min_frames", 1),
            SimilarTo = similar,
            ExcludeId = exclude,
            IncludeNotes = false,
            IncludeRules = false,
            Limit = limit,
        };

        var references = new JsonArray();
        foreach (var h in lib.Search(filter))
        {
            var i = h.Item;
            var o = new JsonObject
            {
                ["id"] = i.Id, ["title"] = i.Title, ["description"] = Clip(i.Description, 300), ["tags"] = Strings([.. i.Tags]), ["kind"] = i.Kind,
                ["width"] = i.Width, ["height"] = i.Height, ["frames"] = i.Frames, ["colors"] = i.Colors,
                ["palette"] = Strings([.. i.Palette.Take(ReferencePaletteShown)]), ["score"] = Math.Round(h.Score, 2),
            };
            if (i.BuiltIn) o["builtIn"] = true;
            references.Add(o);
        }
        // 메모·규칙은 글이 있고 그림 거르기가 없을 때만(거르기는 그림에만 해당).
        var notes = new JsonArray();
        if (!string.IsNullOrWhiteSpace(query) && !filter.HasImageFilter)
        {
            foreach (var h in lib.Search(new ReferenceQuery { Text = query, IncludeImages = false, Limit = limit }))
                notes.Add(new JsonObject { ["id"] = h.Item.Id, ["title"] = h.Item.Title, ["type"] = h.Item.Type, ["snippet"] = h.Snippet, ["score"] = Math.Round(h.Score, 2) });
        }
        var result = new JsonObject { ["references"] = references, ["notes"] = notes };
        if (!string.IsNullOrWhiteSpace(query))
        {
            var guide = new JsonArray();
            foreach (var g in Guide().Search.Search(query, 2))
                guide.Add(new JsonObject { ["page"] = g.PageId, ["title"] = Guide().Titles.GetValueOrDefault(g.PageId), ["snippet"] = g.Snippet });
            result["guide"] = guide;
        }
        if (references.Count == 0 && notes.Count == 0)
            result["note"] = "Nothing matched. Try fewer words or filters, or omit them to list recent references.";
        return TextResult(result.ToJsonString(JsonOptions));
    }

    private JsonObject GetReference(JsonObject? a)
    {
        var lib = Library();
        var item = FindReference(lib, a);
        if (!item.IsImage)
        {
            var text = lib.ReadText(item) ?? "";
            return TextResult($"{item.Title} ({item.Id})\n\n{Clip(text, MaxReferenceText)}");
        }
        ReferenceFrames frames;
        try { frames = lib.LoadFrames(item); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new ToolError(UiStrings.FormatRefs_Tool_ReadFailed(id: item.Id, message: e.Message));
        }
        int count = frames.Images.Count;
        int? frame = OptIndex(a, "frame", count, UiStrings.S_PixelTools_Frame_CAED4A473B);
        var mode = OptEnum(a, "as", "image", "pixels") ?? "image";
        var info = new JsonObject
        {
            ["id"] = item.Id, ["title"] = item.Title, ["description"] = item.Description, ["tags"] = Strings([.. item.Tags]), ["kind"] = item.Kind,
            ["width"] = item.Width, ["height"] = item.Height, ["frames"] = count,
            ["frameDurationsMs"] = new JsonArray([.. frames.DurationsMs.Select(d => (JsonNode?)d)]),
        };

        if (mode == "pixels")
        {
            int f = frame ?? 0;
            var src = frames.Images[f];
            if (src.Width > MaxPixelRegion || src.Height > MaxPixelRegion)
                throw new ToolError(UiStrings.FormatRefs_Tool_TooLargeForPixels(width: src.Width, height: src.Height, max: MaxPixelRegion));
            info["frame"] = f;
            return TextResult(info.ToJsonString(JsonOptions) + "\n" + PixelRows(src, new Rectangle(0, 0, src.Width, src.Height)));
        }

        PixelImage image;
        if (frame is not null || count == 1)
        {
            image = frames.Images[frame ?? 0];
            info["frame"] = frame ?? 0;
        }
        else
        {
            int shown = Math.Min(count, MaxStripFrames);
            int w = frames.Images[0].Width, h = frames.Images[0].Height;
            image = new PixelImage(w * shown, h);
            for (int i = 0; i < shown; i++) image.Blit(frames.Images[i], i * w, 0);
            info["strip"] = $"frames 0-{shown - 1} side by side, each {w}x{h}";
        }
        var (png, zoom) = Enlarge(image, OptInt(a, "scale", 1, 16));
        info["zoom"] = zoom;
        return ImageResult(png, info.ToJsonString(JsonOptions));
    }

    private JsonObject GetStyleRules()
    {
        var lib = Library();
        var sb = new StringBuilder();
        var builtIn = lib.Find(BuiltInReferences.RulesId)!;
        sb.Append(lib.ReadText(builtIn)).Append("\n\n");
        if (lib.ReadUserRules() is { } user && user.Trim().Length > 0)
            sb.Append("--- User rules (").Append(ReferenceLibrary.RulesFileName).Append("; these win where they differ) ---\n\n").Append(Clip(user, MaxReferenceText));
        else
            sb.Append("--- The user has no rules.md yet; follow the rules above. ---");
        return TextResult(sb.ToString().TrimEnd());
    }

    private JsonObject AddReference(JsonObject? a)
    {
        var p = RequireProject();
        Ctx.CommitFloating();
        var from = OptEnum(a, "from", "canvas", "layer") ?? "canvas";
        int? layer = from == "layer" ? OptLayer(a) ?? Ctx.CurrentLayer : null;
        var (first, last) = ReferenceFrameRange(a, p);
        var images = new List<PixelImage>();
        var durations = new List<int>();
        for (int f = first; f <= last; f++)
        {
            images.Add(Source(p, f, layer).Clone());
            durations.Add(p.Frames[f].DurationMs);
        }
        var title = OptString(a, "title")?.Trim();
        if (string.IsNullOrEmpty(title)) title = layer is { } l ? $"{p.BaseName} {p.Layers[l].Name}" : p.BaseName;
        var tags = Arg(a, "tags") is JsonArray arr ? arr.Select(AsString).OfType<string>().ToList() : null;
        var lib = Library();
        ReferenceItem item;
        try
        {
            item = lib.AddImage(new ReferenceFrames(images, durations), title, title, OptString(a, "description"), tags,
                OptEnum(a, "kind", KindValues), CaptionSources.Ai);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ToolError(UiStrings.FormatRefs_Tool_OpenFailed(message: e.Message));
        }
        return TextResult(ReferenceJson(item).ToJsonString(JsonOptions));
    }

    /// <summary>add_reference의 frame_range: "current"(기본)·"all"·"A-B"·"A".</summary>
    private (int First, int Last) ReferenceFrameRange(JsonObject? a, SpriteProject p)
    {
        var text = (OptString(a, "frame_range") ?? "current").Trim().ToLowerInvariant();
        int max = p.Frames.Count - 1;
        if (text is "" or "current") { int c = Math.Clamp(Ctx.CurrentFrame, 0, max); return (c, c); }
        if (text == "all") return (0, max);
        var parts = text.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length is 1 or 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int from)
            && int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int to) && from <= to && to <= max)
            return (from, to);
        throw new ToolError(UiStrings.FormatRefs_Tool_BadFrameRange(text: text, max: max));
    }

    private JsonObject SetReferenceInfo(JsonObject? a)
    {
        var lib = Library();
        var item = FindReference(lib, a);
        if (item.BuiltIn || !item.IsImage && item.Type != ReferenceTypes.Note) throw new ToolError(UiStrings.FormatRefs_Tool_ReadOnly(id: item.Id));
        var tags = Arg(a, "tags") is JsonArray arr ? arr.Select(AsString).OfType<string>().ToList() : null;
        try
        {
            item = lib.Update(item.Id, OptString(a, "title"), OptString(a, "description"), tags, OptEnum(a, "kind", KindValues), CaptionSources.Ai);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ToolError(UiStrings.FormatRefs_Tool_OpenFailed(message: e.Message));
        }
        return TextResult(ReferenceJson(item).ToJsonString(JsonOptions));
    }

    private static JsonObject ReferenceJson(ReferenceItem i) => new()
    {
        ["id"] = i.Id, ["title"] = i.Title, ["description"] = i.Description, ["tags"] = Strings([.. i.Tags]), ["kind"] = i.Kind,
        ["width"] = i.Width, ["height"] = i.Height, ["frames"] = i.Frames, ["colors"] = i.Colors,
        ["palette"] = Strings([.. i.Palette.Take(ReferencePaletteShown)]),
    };

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    // ───────────── 사용 가이드 검색 ─────────────

    private sealed record GuideIndex(HelpSearch Search, Dictionary<string, string> Titles);

    private static GuideIndex? s_guide;
    private static string? s_guideLanguage;

    /// <summary>지금 화면 언어의 사용 가이드 쪽 검색(처음 부를 때 만든다).</summary>
    private static GuideIndex Guide()
    {
        bool korean = UiLanguage.Current == UiLanguage.Korean;
        var lang = korean ? "ko" : "en";
        if (s_guide is { } g && s_guideLanguage == lang) return g;
        var pages = HelpWindow.LoadBook(lang).Groups.SelectMany(x => x.Chapters).SelectMany(c => c.Pages).ToList();
        s_guide = new GuideIndex(new HelpSearch(pages.Select(pg => new HelpDocument(pg.Id, pg.Title, pg.Text))),
            pages.GroupBy(pg => pg.Id).ToDictionary(x => x.Key, x => x.First().Title));
        s_guideLanguage = lang;
        return s_guide;
    }
}
