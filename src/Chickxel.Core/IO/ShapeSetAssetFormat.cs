using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 모양 펜 세트 에셋(<c>shape-set</c>): 영구 ID와 세트 값. <see cref="Set"/>은 읽거나 <see cref="From"/>으로 떠 둔 사본이라 고치지 않는다
/// (고칠 때는 <see cref="ShapeSetAssetFormat.Copy"/>로 새 세트를 만든다).
/// </summary>
public sealed record ShapeSetAsset(string Id, StampSet Set) : IAssetData
{
    public string Name => Set.DisplayName;

    /// <summary>
    /// 세트의 지금 모습을 떠 둔 에셋(이름 = 보이는 이름, 모양 목록은 새 목록 — 원래 세트를 고쳐도 바뀌지 않음). 모양 그림은 더한 뒤 바뀌지 않으므로 같이 쓴다.
    /// </summary>
    public static ShapeSetAsset From(StampSet set, string id = "") => new(id, ShapeSetAssetFormat.Copy(set, set.Id));
}

/// <summary>
/// 모양 펜 세트 에셋 파일(<c>assets/shapes/*.json</c>) — 세트 하나를 담는 세트 파일(<see cref="StampLibrary.SerializeSet"/>)과 같은 내용이다:
/// <code>{ "format": "chickxel-shapes", "version": 1, "set": { "name": "…", "order": "random", "shapes": [ "&lt;base64 RGBA PNG&gt;", … ] } }</code>
/// <c>order</c>는 <c>random</c>·<c>sequential</c>·<c>selected</c>(대소문자 무시) 또는 0~2. 모양은 1~<see cref="AssetPackageLimits.MaxShapesPerSet"/>개,
/// 저마다 폭·높이 1~<see cref="StampLibrary.MaxShapeSize"/>px의 PNG다. ID는 파일에 없고 manifest(개인 에셋이면 저장 줄)가 붙인다(<see cref="AssetReadContext.AssetId"/>).
/// </summary>
public static class ShapeSetAssetFormat
{
    private static readonly HashSet<string> Keys = ["format", "version", "set"];
    private static readonly HashSet<string> SetKeys = ["name", "order", "shapes"];

    /// <summary>파일 내용(UTF-8 JSON, <see cref="StampLibrary.SerializeSet"/>).</summary>
    public static ReadOnlyMemory<byte> Write(StampSet set) => Encoding.UTF8.GetBytes(StampLibrary.SerializeSet(set));

    /// <summary>파일 내용을 읽고 검사한다. 문제가 하나라도 있으면 <paramref name="context"/>에 남기고 null. 세트 이름표는 <see cref="AssetReadContext.AssetId"/>.</summary>
    public static ShapeSetAsset? Read(ReadOnlyMemory<byte> data, AssetReadContext context)
    {
        var utf8 = data.Span;
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) utf8 = utf8[3..];
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(utf8, documentOptions: new JsonDocumentOptions { MaxDepth = context.Limits.MaxJsonDepth });
        }
        catch (JsonException)
        {
            context.Add(AssetIssueCode.InvalidJson);
            return null;
        }
        return Read(node, context);
    }

    private static ShapeSetAsset? Read(JsonNode? node, AssetReadContext context)
    {
        int before = context.Issues.Count;
        if (node is not JsonObject o)
        {
            context.Add(AssetIssueCode.InvalidJson);
            return null;
        }
        if (AssetJson.String(o["format"]) != StampLibrary.SetFileFormat)
        {
            context.Bad("format");
            return null;
        }
        if (!AssetJson.TryInt(o["version"], out int version) || version < 1)
        {
            context.Bad("version");
            return null;
        }
        if (version > StampLibrary.FileVersion)
        {
            context.Add(AssetIssueCode.FutureSchema, version.ToString(CultureInfo.InvariantCulture));
            return null;
        }
        if (o["set"] is not JsonObject s)
        {
            context.Bad("set");
            return null;
        }
        if (context.Strict)
        {
            foreach (var (key, _) in o)
                if (!Keys.Contains(key)) context.Bad(key);
            foreach (var (key, _) in s)
                if (!SetKeys.Contains(key)) context.Bad("set." + key);
        }

        string name = AssetJson.String(s["name"]) ?? "";
        if (!AssetManifest.IsValidName(name)) context.Bad("set.name");
        var order = Order(s["order"], context);

        var shapes = new List<PixelImage>();
        if (s["shapes"] is not JsonArray array || array.Count == 0) context.Bad("set.shapes");
        else if (array.Count > context.Limits.MaxShapesPerSet) context.Add(AssetIssueCode.TooManyItems, "set.shapes");
        else
            for (int i = 0; i < array.Count; i++)
            {
                if (Shape(AssetJson.String(array[i])) is { } shape) shapes.Add(shape);
                else
                {
                    context.Bad(string.Create(CultureInfo.InvariantCulture, $"set.shapes[{i}]"));
                    break;
                }
            }

        if (context.Issues.Count > before) return null;
        var set = new StampSet { Id = context.AssetId, Name = name.Trim(), Order = order };
        set.Shapes.AddRange(shapes);
        return new ShapeSetAsset(context.AssetId, set);
    }

    /// <summary><c>order</c>: 없으면 무작위, 이름(대소문자 무시) 또는 0~2. 틀리면 문제를 남긴다.</summary>
    private static StampOrder Order(JsonNode? node, AssetReadContext context)
    {
        if (node is null) return StampOrder.Random;
        if (AssetJson.String(node) is { Length: > 0 } text && !char.IsAsciiDigit(text.TrimStart('-')[0])
            && Enum.TryParse(text, ignoreCase: true, out StampOrder named) && Enum.IsDefined(named))
            return named;
        if (AssetJson.TryInt(node, out int number) && Enum.IsDefined((StampOrder)number)) return (StampOrder)number;
        context.Bad("set.order");
        return StampOrder.Random;
    }

    /// <summary>base64 PNG 모양 하나: 머리(IHDR)의 폭·높이를 먼저 보고(큰 그림을 풀지 않음) 1~<see cref="StampLibrary.MaxShapeSize"/>px일 때만 디코드한다. 틀리면 null.</summary>
    private static PixelImage? Shape(string? base64)
    {
        if (base64 is null) return null;
        byte[] png;
        try { png = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(signature) || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return null;
        uint width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (width is 0 or > StampLibrary.MaxShapeSize || height is 0 or > StampLibrary.MaxShapeSize) return null;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            var image = PngIO.Load(stream);
            return image.Width == width && image.Height == height ? image : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException or ArgumentException
                                       or IndexOutOfRangeException or NotSupportedException or OverflowException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// 에셋으로 저장·게시할 수 있는 세트인지(이름 1~100자, 모양 1~<see cref="AssetPackageLimits.MaxShapesPerSet"/>개, 저마다 1~<see cref="StampLibrary.MaxShapeSize"/>px).
    /// 틀리면 <see cref="InvalidDataException"/>(문제마다 한 줄).
    /// </summary>
    public static void Validate(StampSet set)
    {
        var context = new AssetReadContext(set.DisplayName, strict: true);
        if (Read(Write(set), context) is null)
            throw new InvalidDataException(string.Join(Environment.NewLine, context.Issues.Select(issue => issue.Message)));
    }

    /// <summary>세트의 사본(이름표 <paramref name="id"/>, 이름 = 보이는 이름, 순서, 새 모양 목록 — 모양 그림은 같이 씀).</summary>
    public static StampSet Copy(StampSet set, string id)
    {
        var copy = new StampSet { Id = id, Name = set.DisplayName, Order = set.Order };
        copy.Shapes.AddRange(set.Shapes);
        return copy;
    }
}
