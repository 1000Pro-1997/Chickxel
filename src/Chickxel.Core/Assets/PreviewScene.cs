using System.Buffers.Binary;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Assets;

/// <summary>미리보기 장면의 픽셀 레이어 하나: 이름(효과의 레이어 고르기 설정이 고르는 이름), 프레임마다 장면 크기 그림, 불투명도, 보이기.</summary>
public sealed record PreviewSceneLayer(string Name, IReadOnlyList<PixelImage> Frames, byte Opacity = 255, bool Visible = true);

/// <summary>
/// 효과 만들기의 미리보기 장면: 병아리 둘레의 픽셀 레이어(아래→위), 프레임, 병아리 자리. 다른 레이어를 읽는 효과(<c>ctx.layer</c> 등)가
/// 부딪힐 바닥, 따라갈 길 같은 것을 효과와 같이 저장하고 패키지로 나른다. 미리보기 칸 i는 장면 프레임 i % <see cref="FrameCount"/>를 쓴다.
/// <para>
/// 저장 형식(<see cref="ToPng"/>): PNG 한 장 — 칸 격자(레이어 × 프레임 셀, 셀 차례 = 레이어 × <see cref="FrameCount"/> + 프레임, 가로로 채우다 넘치면 다음 줄,
/// 한 줄 칸 수 = min(셀 수, <see cref="MaxSheetSize"/> / 폭), 전체 <see cref="MaxSheetSize"/>×<see cref="MaxSheetSize"/> 이하, 레이어가 없으면 1×1 투명) —
/// 와 PNG 글 덩어리(tEXt <see cref="TextKeyword"/>)의 JSON 메타데이터
/// <code>{"schemaVersion":1,"width":…,"height":…,"frames":…,"layers":[{"name":…,"opacity":…,"visible":…}],"chickIndex":…,"chickAnchor":{"x":…,"y":…}}</code>
/// (ASCII — 다른 글자는 \u 이스케이프). 예전 앱은 이 파일을 평범한 미리보기 PNG로 본다.
/// </para>
/// 그림(<see cref="PreviewSceneLayer.Frames"/>)은 넘겨받은 것을 그대로 쥐므로 만든 뒤 고치지 않는다.
/// </summary>
public sealed class PreviewScene
{
    public const int MaxSize = 256, MaxLayers = 8, MaxFrames = 20;
    /// <summary>레이어 이름의 최대 길이(UTF-16 글자).</summary>
    public const int MaxNameLength = 64;
    /// <summary>저장 PNG(칸 격자)의 폭·높이 한도(패키지 미리보기 한도와 같음 — <see cref="AssetPackageLimits.MaxPreviewSize"/>).</summary>
    public const int MaxSheetSize = 512;
    /// <summary>메타데이터를 담는 PNG 글 덩어리 키워드.</summary>
    public const string TextKeyword = "chickxel-scene";
    public const int SchemaVersion = 1;
    /// <summary>기본 장면의 바닥 레이어 이름.</summary>
    public const string DefaultGroundName = "ground";

    // 읽을 때 한도: PNG 파일과 메타데이터 글
    private const int MaxPngBytes = 4 << 20, MaxTextBytes = 16 << 10;

    private static readonly HashSet<string> RootKeys = ["schemaVersion", "width", "height", "frames", "layers", "chickIndex", "chickAnchor"];
    private static readonly HashSet<string> LayerKeys = ["name", "opacity", "visible"];
    private static readonly HashSet<string> AnchorKeys = ["x", "y"];

    /// <summary>값을 검사해 만든다. 한도·크기·프레임 수가 맞지 않으면 <see cref="ArgumentException"/>.</summary>
    public PreviewScene(int width, int height, int frameCount, IReadOnlyList<PreviewSceneLayer> layers, int chickIndex, Point chickAnchor)
    {
        if (width is < 1 or > MaxSize || height is < 1 or > MaxSize) throw Invalid($"size {width}x{height}");
        if (frameCount is < 1 or > MaxFrames) throw Invalid($"frames {frameCount}");
        if (layers.Count > MaxLayers) throw Invalid($"layers {layers.Count}");
        if (chickIndex < 0 || chickIndex > layers.Count) throw Invalid($"chickIndex {chickIndex}");
        if (chickAnchor.X < 0 || chickAnchor.X > width || chickAnchor.Y < 0 || chickAnchor.Y > height) throw Invalid($"chickAnchor {chickAnchor.X},{chickAnchor.Y}");
        foreach (var layer in layers)
        {
            if (!IsValidName(layer.Name)) throw Invalid("name");
            if (layer.Frames.Count != frameCount) throw Invalid($"{layer.Name}: frames {layer.Frames.Count}");
            if (layer.Frames.Any(image => image is null || image.Width != width || image.Height != height)) throw Invalid($"{layer.Name}: frame size");
        }
        if (!Fits(width, height, layers.Count * frameCount))
            throw new ArgumentException(DomainStrings.FormatPreviewScene_TooLarge(cells: layers.Count * frameCount, max: MaxCells(width, height)));
        Width = width;
        Height = height;
        FrameCount = frameCount;
        Layers = [.. layers];
        ChickIndex = chickIndex;
        ChickAnchor = chickAnchor;
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>1~<see cref="MaxFrames"/>. 미리보기 칸 i는 장면 프레임 i % FrameCount.</summary>
    public int FrameCount { get; }
    /// <summary>아래 → 위, 병아리는 들지 않는다. 이름이 겹쳐도 된다.</summary>
    public IReadOnlyList<PreviewSceneLayer> Layers { get; }
    /// <summary>0..<see cref="Layers"/>.Count: 병아리는 Layers[ChickIndex-1]과 Layers[ChickIndex] 사이에 놓인다.</summary>
    public int ChickIndex { get; }
    /// <summary>캔버스 px: x = 병아리 불투명 영역의 가운데 열, y = 병아리의 가장 아래 불투명 줄 바로 아래 줄(발).</summary>
    public Point ChickAnchor { get; }

    /// <summary>레이어 이름으로 쓸 수 있는지: 1~<see cref="MaxNameLength"/>자, 앞뒤 공백·제어 문자 없음.</summary>
    public static bool IsValidName(string? name)
        => name is { Length: > 0 and <= MaxNameLength } && name.Trim().Length == name.Length && !name.Any(char.IsControl);

    /// <summary><paramref name="width"/>×<paramref name="height"/> 셀이 저장 PNG(<see cref="MaxSheetSize"/>²)에 들어가는 최대 수(레이어 수 × 프레임 수의 한도).</summary>
    public static int MaxCells(int width, int height) => MaxSheetSize / Math.Max(1, width) * (MaxSheetSize / Math.Max(1, height));

    private static bool Fits(int width, int height, int cells) => cells <= MaxCells(width, height);

    /// <summary>
    /// 기본 장면: <paramref name="chick"/> 크기에 여백을 둔 캔버스(폭 = 병아리 폭 × 3, 높이 = 병아리 높이 × 2, 48 이상 <see cref="MaxSize"/> 이하),
    /// 빈 <see cref="DefaultGroundName"/> 레이어 하나(병아리 아래), 1프레임. 병아리는 가운데 열, 발은 아래에서 1/4 높이 위.
    /// </summary>
    public static PreviewScene CreateDefault(PixelImage chick)
    {
        int width = Math.Clamp(chick.Width * 3, 48, MaxSize), height = Math.Clamp(chick.Height * 2, 48, MaxSize);
        var bounds = OpaqueBounds(chick);
        int feet = Math.Clamp(height - height / 4, Math.Min(bounds.Bottom, height), height);
        return new PreviewScene(width, height, 1, [new PreviewSceneLayer(DefaultGroundName, [new PixelImage(width, height)])], 1, new Point(width / 2, feet));
    }

    /// <summary>
    /// <paramref name="chick"/>을 놓을 왼쪽 위(캔버스 px): 그 불투명 영역의 아래 가운데(가운데 열, 가장 아래 줄 바로 아래)가 <see cref="ChickAnchor"/>에 오게 —
    /// 병아리마다 크기가 달라도 발이 같은 자리에 선다. 모두 투명이면 그림 전체를 영역으로 본다.
    /// </summary>
    public Point ChickOffset(PixelImage chick)
    {
        var b = OpaqueBounds(chick);
        return new Point(ChickAnchor.X - (b.X + b.Width / 2), ChickAnchor.Y - b.Bottom);
    }

    internal static Rectangle OpaqueBounds(PixelImage image)
        => image.GetOpaqueBounds() is { IsEmpty: false } b ? b : image.Bounds;

    // ───────────── 저장 형식 ─────────────

    /// <summary>저장 PNG(칸 격자 + 메타데이터 글 덩어리). 같은 장면은 늘 같은 바이트다.</summary>
    public byte[] ToPng()
    {
        int cells = Layers.Count * FrameCount;
        var (columns, rows) = Grid(Width, Height, cells);
        var sheet = new PixelImage(cells == 0 ? 1 : columns * Width, cells == 0 ? 1 : rows * Height);
        for (int i = 0; i < cells; i++)
            sheet.Blit(Layers[i / FrameCount].Frames[i % FrameCount], i % columns * Width, i / columns * Height);
        var layers = new JsonArray();
        foreach (var layer in Layers)
            layers.Add(new JsonObject { ["name"] = layer.Name, ["opacity"] = (int)layer.Opacity, ["visible"] = layer.Visible });
        var meta = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["width"] = Width,
            ["height"] = Height,
            ["frames"] = FrameCount,
            ["layers"] = layers,
            ["chickIndex"] = ChickIndex,
            ["chickAnchor"] = new JsonObject { ["x"] = ChickAnchor.X, ["y"] = ChickAnchor.Y },
        };
        using var stream = new MemoryStream();
        // 기본 직렬화는 ASCII 밖의 글자를 \u로 이스케이프하므로 tEXt(Latin-1)에 그대로 들어간다
        PngIO.SaveWithText(sheet, stream, TextKeyword, meta.ToJsonString());
        return stream.ToArray();
    }

    /// <summary>
    /// 저장 PNG를 읽는다. 메타데이터가 없거나 모양·한도가 틀리거나(모르는 키 포함), 그림 크기가 칸 격자와 다르거나, PNG가 손상됐으면 <see cref="InvalidDataException"/>.
    /// </summary>
    public static PreviewScene FromPng(byte[] png)
    {
        try
        {
            return Read(png);
        }
        catch (ArgumentException)
        {
            // 같은 키가 두 번 나온 JSON 객체, 값 검사에 걸린 장면 등
            throw Bad("value");
        }
    }

    private static PreviewScene Read(byte[] png)
    {
        if (png.Length > MaxPngBytes) throw Bad("size");
        string text = PngIO.ReadText(png, TextKeyword, MaxTextBytes) ?? throw Bad(TextKeyword);
        var issues = new List<AssetIssue>();
        if (AssetJson.Parse(Encoding.UTF8.GetBytes(text), TextKeyword, 4, 256, issues) is not JsonObject root) throw Bad("json");
        Keys(root, RootKeys, "");
        if (!AssetJson.TryInt(root["schemaVersion"], out int schema) || schema < 1) throw Bad("schemaVersion");
        if (schema > SchemaVersion) throw Bad("schemaVersion " + schema.ToString(CultureInfo.InvariantCulture));
        int width = Int(root, "width", 1, MaxSize), height = Int(root, "height", 1, MaxSize), frames = Int(root, "frames", 1, MaxFrames);
        if (root["layers"] is not JsonArray array || array.Count > MaxLayers) throw Bad("layers");
        var meta = new List<(string Name, byte Opacity, bool Visible)>();
        foreach (var node in array)
        {
            if (node is not JsonObject o) throw Bad("layers");
            Keys(o, LayerKeys, "layers.");
            if (AssetJson.String(o["name"]) is not { } name || !IsValidName(name)) throw Bad("layers.name");
            int opacity = Int(o, "opacity", 0, 255);
            if (!AssetJson.TryBool(o["visible"], out bool visible)) throw Bad("layers.visible");
            meta.Add((name, (byte)opacity, visible));
        }
        int chickIndex = Int(root, "chickIndex", 0, meta.Count);
        if (root["chickAnchor"] is not JsonObject anchor) throw Bad("chickAnchor");
        Keys(anchor, AnchorKeys, "chickAnchor.");
        int x = Int(anchor, "x", 0, width), y = Int(anchor, "y", 0, height);

        int cells = meta.Count * frames;
        if (!Fits(width, height, cells)) throw Bad("cells");
        var (columns, rows) = Grid(width, height, cells);
        int sheetWidth = cells == 0 ? 1 : columns * width, sheetHeight = cells == 0 ? 1 : rows * height;
        // 그림을 풀기 전에 머리(IHDR)의 크기를 본다
        if (png.Length < 24 || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) != sheetWidth || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) != sheetHeight)
            throw Bad("sheet size");
        PixelImage sheet;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            sheet = PngIO.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or IndexOutOfRangeException or NotSupportedException or OverflowException or FormatException)
        {
            throw Bad("png");
        }
        if (sheet.Width != sheetWidth || sheet.Height != sheetHeight) throw Bad("sheet size");
        var layers = new List<PreviewSceneLayer>(meta.Count);
        for (int l = 0; l < meta.Count; l++)
        {
            var images = new PixelImage[frames];
            for (int f = 0; f < frames; f++)
            {
                int i = l * frames + f;
                images[f] = sheet.Crop(new Rectangle(i % columns * width, i / columns * height, width, height));
            }
            layers.Add(new PreviewSceneLayer(meta[l].Name, images, meta[l].Opacity, meta[l].Visible));
        }
        return new PreviewScene(width, height, frames, layers, chickIndex, new Point(x, y));
    }

    /// <summary>칸 격자: 한 줄 칸 수(가로로 채움)와 줄 수. 셀이 없으면 (1, 0).</summary>
    private static (int Columns, int Rows) Grid(int width, int height, int cells)
    {
        int columns = Math.Max(1, Math.Min(cells, MaxSheetSize / width));
        return (columns, (cells + columns - 1) / columns);
    }

    private static void Keys(JsonObject o, HashSet<string> allowed, string prefix)
    {
        foreach (var (key, _) in o)
            if (!allowed.Contains(key)) throw Bad(prefix + key);
    }

    private static int Int(JsonObject o, string key, int min, int max)
        => AssetJson.TryInt(o[key], out int value) && value >= min && value <= max ? value : throw Bad(key);

    private static ArgumentException Invalid(string detail) => new(DomainStrings.FormatPreviewScene_Invalid(detail: detail));

    private static InvalidDataException Bad(string detail) => new(DomainStrings.FormatPreviewScene_Invalid(detail: detail));

    // ───────────── 편집 문서 ─────────────

    /// <summary>
    /// 메인 편집기에서 고칠 문서: 장면 크기·프레임 수의 프로젝트에 장면 레이어(이름·불투명도·보이기·그림 사본)와 병아리 레이어(이름 <paramref name="chickLayerName"/>,
    /// 모든 프레임에 <paramref name="chick"/>을 <see cref="ChickOffset"/>에)를 장면 차례로 둔다. 실행취소 기록은 비우고 수정됨 표시는 끈다.
    /// </summary>
    public (SpriteProject Project, Layer ChickLayer) ToProject(PixelImage chick, string chickLayerName)
    {
        var project = Build(FrameCount, f => f, chick, _ => ChickOffset(chick), chickLayerName, copy: true);
        project.History.Clear();
        project.IsDirty = false;
        return (project, project.Layers[ChickIndex]);
    }

    /// <summary>
    /// <paramref name="frames"/>프레임 프로젝트: 장면 레이어 <c>i</c>번째 셀 = 장면 프레임 <paramref name="sceneFrame"/>(i), 병아리 레이어 셀 =
    /// <paramref name="chick"/>을 <paramref name="chickAt"/>(i)에 놓은 캔버스 그림. <paramref name="copy"/>면 장면 그림을 복사해 넣는다(고칠 문서).
    /// </summary>
    internal SpriteProject Build(int frames, Func<int, int> sceneFrame, PixelImage chick, Func<int, Point> chickAt, string chickLayerName, bool copy)
    {
        var project = new SpriteProject(Width, Height);
        for (int f = 1; f < frames; f++) project.AddFrame();
        for (int i = 0; i < Layers.Count; i++) project.AddLayer();
        for (int i = 0; i <= Layers.Count; i++)
        {
            if (i == ChickIndex)
            {
                project.SetLayerProperties(i, name: chickLayerName);
                for (int f = 0; f < frames; f++)
                {
                    var cel = new PixelImage(Width, Height);
                    var at = chickAt(f);
                    cel.Blit(chick, at.X, at.Y);
                    if (!cel.IsRegionEmpty(cel.Bounds)) project.SetCel(i, f, cel);
                }
                continue;
            }
            var layer = Layers[i < ChickIndex ? i : i - 1];
            project.SetLayerProperties(i, name: layer.Name, visible: layer.Visible, opacity: layer.Opacity);
            for (int f = 0; f < frames; f++)
            {
                var image = layer.Frames[sceneFrame(f) % FrameCount];
                if (!image.IsRegionEmpty(image.Bounds)) project.SetCel(i, f, copy ? image.Clone() : image);
            }
        }
        return project;
    }

    /// <summary>
    /// 문서에서 장면을 다시 읽는다: 최상위 일반 레이어만(그룹·부위·효과는 무시) 프로젝트 차례대로, 프레임은 앞에서 <see cref="MaxFrames"/>개까지.
    /// 병아리 자리 = <paramref name="chickLayer"/> 프레임 0의 불투명 영역 아래 가운데. 병아리 레이어가 프로젝트에 없거나 프레임 0이 비었으면
    /// <see cref="InvalidOperationException"/>, 장면 한도(크기·레이어 수·칸 격자)를 넘으면 <see cref="ArgumentException"/>.
    /// 레이어 이름은 앞뒤 공백을 빼고 <see cref="MaxNameLength"/>자로 자른다.
    /// </summary>
    public static PreviewScene FromProject(SpriteProject project, Layer chickLayer)
    {
        if (!project.Layers.Contains(chickLayer) || project.Frames.Count == 0) throw new InvalidOperationException(DomainStrings.PreviewScene_NoChick);
        var cel = project.GetCel(chickLayer, project.Frames[0]);
        if (cel?.GetOpaqueBounds() is not { IsEmpty: false } b) throw new InvalidOperationException(DomainStrings.PreviewScene_NoChick);
        int frames = Math.Min(project.Frames.Count, MaxFrames);
        var layers = new List<PreviewSceneLayer>();
        int chickIndex = 0;
        foreach (var layer in project.Layers)
        {
            if (ReferenceEquals(layer, chickLayer))
            {
                chickIndex = layers.Count;
                continue;
            }
            if (layer.Parent is not null || layer.IsGroup || layer.IsPart) continue;
            var images = new PixelImage[frames];
            for (int f = 0; f < frames; f++) images[f] = project.GetCel(layer, project.Frames[f])?.Clone() ?? new PixelImage(project.Width, project.Height);
            layers.Add(new PreviewSceneLayer(LayerName(layer.Name), images, layer.Opacity, layer.Visible));
        }
        return new PreviewScene(project.Width, project.Height, frames, layers, chickIndex, new Point(b.X + b.Width / 2, b.Bottom));
    }

    private static string LayerName(string name)
    {
        string trimmed = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (trimmed.Length > MaxNameLength)
            trimmed = trimmed[..(char.IsHighSurrogate(trimmed[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength)].TrimEnd();
        return trimmed.Length > 0 ? trimmed : DefaultGroundName;
    }
}
