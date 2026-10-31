using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>메타의 <c>spriteSheet.sprites</c> 항목 하나(Unity 좌표: 아래 왼쪽 원점).</summary>
public sealed class UnityMetaSprite
{
    public string Name { get; set; } = "";
    /// <summary>Unity 좌표 사각형(아래 왼쪽 원점).</summary>
    public Rectangle UnityRect { get; set; }
    /// <summary>Unity <c>alignment</c>(0~9).</summary>
    public int Alignment { get; set; }
    public Pivot Pivot { get; set; }
    public SpriteBorder Border { get; set; }
    public string SpriteId { get; set; } = "";
    public long InternalId { get; set; }

    /// <summary>편집기 좌표(왼쪽 위 원점)로 바꾼 사각형.</summary>
    public Rectangle ToTopLeftRect(int imageHeight)
        => new(UnityRect.X, imageHeight - UnityRect.Y - UnityRect.Height, UnityRect.Width, UnityRect.Height);
}

/// <summary>Unity TextureImporter 메타에서 읽은 값.</summary>
public sealed class UnityMetaInfo
{
    public string? Guid { get; set; }
    /// <summary>메타의 spriteMode 원값(0 없음, 1 Single, 2 Multiple, 3 Polygon).</summary>
    public int SpriteModeValue { get; set; } = 1;
    public SpriteMode SpriteMode => SpriteModeValue == 2 ? SpriteMode.Multiple : SpriteMode.Single;
    public int TextureType { get; set; } = 8;
    public float PixelsPerUnit { get; set; } = SpriteSettings.DefaultPixelsPerUnit;
    public int Alignment { get; set; }
    public Pivot SpritePivot { get; set; } = Pivot.Center;
    public SpriteBorder Border { get; set; }
    public bool Readable { get; set; }
    public bool FullRectMesh { get; set; } = true;
    /// <summary>나머지 텍스처 가져오기 설정(Extrude·Physics·Advanced·Wrap/Filter·Default 플랫폼). Max Size는 메타 원값.</summary>
    public SpriteSettings Texture { get; } = new();
    public List<UnityMetaSprite> Sprites { get; } = new();
    /// <summary>시트(Single 스프라이트) 수준 spriteID.</summary>
    public string? SheetSpriteId { get; set; }
    /// <summary><c>internalIDToNameTable</c>(클래스 213 = Sprite) 항목.</summary>
    public List<(long Id, string Name)> InternalIdToName { get; } = new();
    public string UserData { get; set; } = "";
    public string AssetBundleName { get; set; } = "";
    public string AssetBundleVariant { get; set; } = "";

    /// <summary>메타의 칸을 편집기 칸(왼쪽 위 원점)으로 바꾼다.</summary>
    public List<Slice> ToSlices(int imageHeight)
        => Sprites.Select(s => new Slice(s.Name, s.ToTopLeftRect(imageHeight), s.Pivot, s.Border)).ToList();

    /// <summary>
    /// 모드·PPU·피벗·테두리·Read/Write·메시 설정과 (Multiple이면) 칸 목록을 문서에 적용한다.
    /// 수정됨 표시는 바꾸지 않는다.
    /// </summary>
    public void ApplyTo(Document doc)
    {
        doc.Sprite.Mode = SpriteMode;
        doc.Sprite.PixelsPerUnit = PixelsPerUnit;
        doc.Sprite.DefaultPivot = SpritePivot;
        doc.Sprite.Border = Border;
        doc.Sprite.Readable = Readable;
        doc.Sprite.FullRectMesh = FullRectMesh;
        var t = Texture;
        doc.Sprite.ExtrudeEdges = t.ExtrudeEdges;
        doc.Sprite.GeneratePhysicsShape = t.GeneratePhysicsShape;
        doc.Sprite.SRgb = t.SRgb;
        doc.Sprite.AlphaSource = t.AlphaSource;
        doc.Sprite.AlphaIsTransparency = t.AlphaIsTransparency;
        doc.Sprite.GenerateMipMaps = t.GenerateMipMaps;
        doc.Sprite.WrapMode = t.WrapMode;
        doc.Sprite.FilterMode = t.FilterMode;
        doc.Sprite.AnisoLevel = t.AnisoLevel;
        // 이미지에 맞춘 자동 크기와 같으면 자동으로 둔다(이미지가 커져도 따라감).
        doc.Sprite.MaxTextureSize = t.MaxTextureSize == UnityMeta.MaxTextureSizeFor(doc.Width, doc.Height) ? 0 : t.MaxTextureSize;
        doc.Sprite.ResizeAlgorithm = t.ResizeAlgorithm;
        doc.Sprite.Format = t.Format;
        doc.Sprite.Compression = t.Compression;
        doc.Sprite.CrunchCompression = t.CrunchCompression;
        doc.Sprite.CompressionQuality = t.CompressionQuality;
        doc.Slices.Clear();
        if (SpriteMode == SpriteMode.Multiple)
            doc.Slices.AddRange(ToSlices(doc.Height));
    }
}

/// <summary>
/// Unity 6 TextureImporter <c>.meta</c> 읽기/쓰기. 쓸 때 기존 메타의 guid와 칸별 spriteID/internalID를
/// 이름 기준(이름이 바뀐 칸은 같은 사각형 기준)으로 유지해 Unity 참조가 끊기지 않게 한다.
/// </summary>
public static class UnityMeta
{
    /// <summary>Sprite 클래스 ID(internalIDToNameTable의 키).</summary>
    private const int SpriteClassId = 213;

    public static string GetMetaPath(string assetPath) => assetPath + ".meta";

    /// <summary>메타 파일을 읽는다. 파일이 없거나 읽을 수 없으면 null.</summary>
    public static UnityMetaInfo? TryRead(string metaPath)
    {
        try
        {
            return File.Exists(metaPath) ? Parse(File.ReadAllText(metaPath)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static UnityMetaInfo Read(string metaPath) => Parse(File.ReadAllText(metaPath));

    /// <summary>메타 YAML 문자열을 해석한다. 모르는 값·깨진 값은 기본값으로 둔다.</summary>
    public static UnityMetaInfo Parse(string yaml)
    {
        var info = new UnityMetaInfo();
        YamlNode root;
        try
        {
            root = MiniYaml.Parse(yaml);
        }
        catch (Exception)
        {
            root = new YamlMapping();
        }

        info.Guid = root["guid"]?.Text is { } g && IsGuid(g) ? g : FindGuidFallback(yaml);
        var ti = root["TextureImporter"];
        if (ti is null) return info;

        info.SpriteModeValue = Int(ti["spriteMode"], 1);
        info.TextureType = Int(ti["textureType"], 8);
        info.PixelsPerUnit = Float(ti["spritePixelsToUnits"], SpriteSettings.DefaultPixelsPerUnit);
        info.Alignment = Int(ti["alignment"], 0);
        info.SpritePivot = ResolvePivot(info.Alignment, ti["spritePivot"]);
        info.Border = ReadBorder(ti["spriteBorder"]);
        info.Readable = Int(ti["isReadable"], 0) != 0;
        info.FullRectMesh = Int(ti["spriteMeshType"], 1) == 0;
        var t = info.Texture;
        var mip = ti["mipmaps"];
        t.GenerateMipMaps = Int(mip?["enableMipMap"], 0) != 0;
        t.SRgb = Int(mip?["sRGBTexture"], 1) != 0;
        var ts = ti["textureSettings"];
        t.FilterMode = EnumOr(Int(ts?["filterMode"], 0), TextureFilter.Point);
        t.AnisoLevel = Int(ts?["aniso"], 1);
        t.WrapMode = EnumOr(Int(ts?["wrapU"], 1), TextureWrap.Clamp);
        t.ExtrudeEdges = Int(ti["spriteExtrude"], 1);
        t.GeneratePhysicsShape = Int(ti["spriteGenerateFallbackPhysicsShape"], 1) != 0;
        t.AlphaSource = EnumOr(Int(ti["alphaUsage"], 1), SpriteAlphaSource.InputTextureAlpha);
        t.AlphaIsTransparency = Int(ti["alphaIsTransparency"], 1) != 0;
        var platform = ti["platformSettings"]?.Items.FirstOrDefault(x => x["buildTarget"]?.Text == "DefaultTexturePlatform");
        // Max Size 원값(0이 아니도록 SpriteSettings에 맞추지 않고 그대로) — ApplyTo에서 자동과 비교한다.
        int maxSize = Int(platform?["maxTextureSize"] ?? ti["maxTextureSize"], 0);
        t.MaxTextureSize = maxSize;
        t.ResizeAlgorithm = EnumOr(Int(platform?["resizeAlgorithm"], 0), TextureResize.Mitchell);
        t.Format = EnumOr(Int(platform?["textureFormat"], -1), TextureFormatChoice.Automatic);
        t.Compression = EnumOr(Int(platform?["textureCompression"], 0), TextureCompressionChoice.None);
        t.CrunchCompression = Int(platform?["crunchedCompression"], 0) != 0;
        t.CompressionQuality = Int(platform?["compressionQuality"], 50);
        info.UserData = ti["userData"]?.Text ?? "";
        info.AssetBundleName = ti["assetBundleName"]?.Text ?? "";
        info.AssetBundleVariant = ti["assetBundleVariant"]?.Text ?? "";

        foreach (var entry in ti["internalIDToNameTable"]?.Items ?? [])
        {
            if (entry["first"] is not YamlMapping first || entry["second"]?.Text is not { } name) continue;
            foreach (var kv in first.Entries)
                if (kv.Key == SpriteClassId.ToString(CultureInfo.InvariantCulture) && Long(kv.Value) is { } id)
                    info.InternalIdToName.Add((id, name));
        }

        var sheet = ti["spriteSheet"];
        if (sheet is not null)
        {
            info.SheetSpriteId = sheet["spriteID"]?.Text is { Length: > 0 } sid ? sid : null;
            foreach (var s in sheet["sprites"]?.Items ?? [])
            {
                var r = s["rect"];
                int align = Int(s["alignment"], 0);
                info.Sprites.Add(new UnityMetaSprite
                {
                    Name = s["name"]?.Text ?? "",
                    UnityRect = new Rectangle(
                        (int)MathF.Round(Float(r?["x"], 0)), (int)MathF.Round(Float(r?["y"], 0)),
                        (int)MathF.Round(Float(r?["width"], 0)), (int)MathF.Round(Float(r?["height"], 0))),
                    Alignment = align,
                    Pivot = ResolvePivot(align, s["pivot"]),
                    Border = ReadBorder(s["border"]),
                    SpriteId = s["spriteID"]?.Text ?? "",
                    InternalId = Long(s["internalID"]) ?? 0,
                });
            }
        }
        return info;
    }

    /// <summary>
    /// 문서를 메타로 쓴다(<c>pngPath + ".meta"</c>). 기존 메타가 있으면 guid·ID를 유지한다.
    /// 임시 파일에 쓴 뒤 바꿔치기한다.
    /// </summary>
    public static void Write(Document doc, string pngPath)
    {
        var metaPath = GetMetaPath(pngPath);
        var existing = TryRead(metaPath);
        var text = Generate(doc, existing);
        var temp = metaPath + ".chickxel-tmp";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, metaPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Unity 6 TextureImporter 메타 텍스트를 만든다. <paramref name="existing"/>이 있으면
    /// guid·시트 spriteID·칸별 ID·internalIDToNameTable·userData·에셋번들 값을 이어받는다.
    /// </summary>
    public static string Generate(Document doc, UnityMetaInfo? existing)
    {
        var settings = doc.Sprite;
        bool multiple = settings.Mode == SpriteMode.Multiple;
        var guid = existing?.Guid is { } g && IsGuid(g) ? g : NewGuid();
        var sheetSpriteId = existing?.SheetSpriteId is { Length: 32 } sid ? sid : NewSpriteId();

        // 사용 중인 internalID(새 ID와 겹치지 않게)
        var usedIds = new HashSet<long>();
        if (existing is not null)
        {
            foreach (var (id, _) in existing.InternalIdToName) usedIds.Add(id);
            foreach (var s in existing.Sprites) usedIds.Add(s.InternalId);
        }

        var sprites = multiple ? AssignIds(doc, existing, usedIds) : new List<UnityMetaSprite>();

        // internalIDToNameTable: 기존 항목은 현재 칸과 충돌(같은 이름·다른 ID 또는 같은 ID·다른 이름)하지 않으면 그대로 두고
        // (Unity도 지난 항목을 남겨 둔다), 기존 메타에 같은 이름·ID로 없던 칸만 추가한다.
        // 그래서 바뀌지 않은 Unity 메타를 다시 저장해도 내용이 같다.
        var table = new List<(long Id, string Name)>();
        var idOfName = new Dictionary<string, long>(StringComparer.Ordinal);
        var nameOfId = new Dictionary<long, string>();
        foreach (var s in sprites)
        {
            idOfName.TryAdd(s.Name, s.InternalId);
            nameOfId.TryAdd(s.InternalId, s.Name);
        }
        if (existing is not null)
        {
            var seen = new HashSet<long>();
            foreach (var (id, name) in existing.InternalIdToName)
            {
                if (idOfName.TryGetValue(name, out var cid) && cid != id) continue;
                if (nameOfId.TryGetValue(id, out var cname) && cname != name) continue;
                if (seen.Add(id)) table.Add((id, name));
            }
        }
        var unchanged = existing?.Sprites.Select(s => (s.InternalId, s.Name)).ToHashSet() ?? [];
        foreach (var s in sprites)
        {
            var entry = (s.InternalId, s.Name);
            if (!unchanged.Contains(entry) && !table.Contains(entry)) table.Add(entry);
        }

        int maxSize = settings.MaxTextureSize > 0 ? settings.MaxTextureSize : MaxTextureSizeFor(doc.Width, doc.Height);
        var sb = new StringBuilder(4096);
        void L(string line) => sb.Append(line).Append('\n');

        L("fileFormatVersion: 2");
        L($"guid: {guid}");
        L("TextureImporter:");
        if (table.Count == 0) L("  internalIDToNameTable: []");
        else
        {
            L("  internalIDToNameTable:");
            foreach (var (id, name) in table)
            {
                L("  - first:");
                L($"      {SpriteClassId}: {id.ToString(CultureInfo.InvariantCulture)}");
                L($"    second: {MiniYaml.QuoteIfNeeded(name)}");
            }
        }
        L("  externalObjects: {}");
        L("  serializedVersion: 13");
        L("  mipmaps:");
        L("    mipMapMode: 0");
        L($"    enableMipMap: {(settings.GenerateMipMaps ? 1 : 0)}");
        L($"    sRGBTexture: {(settings.SRgb ? 1 : 0)}");
        L("    linearTexture: 0");
        L("    fadeOut: 0");
        L("    borderMipMap: 0");
        L("    mipMapsPreserveCoverage: 0");
        L("    alphaTestReferenceValue: 0.5");
        L("    mipMapFadeDistanceStart: 1");
        L("    mipMapFadeDistanceEnd: 3");
        L("  bumpmap:");
        L("    convertToNormalMap: 0");
        L("    externalNormalMap: 0");
        L("    heightScale: 0.25");
        L("    normalMapFilter: 0");
        L("    flipGreenChannel: 0");
        L($"  isReadable: {(settings.Readable ? 1 : 0)}");
        L("  streamingMipmaps: 0");
        L("  streamingMipmapsPriority: 0");
        L("  vTOnly: 0");
        L("  ignoreMipmapLimit: 0");
        L("  grayScaleToAlpha: 0");
        L("  generateCubemap: 6");
        L("  cubemapConvolution: 0");
        L("  seamlessCubemap: 0");
        L("  textureFormat: 1");
        L($"  maxTextureSize: {maxSize}");
        L("  textureSettings:");
        L("    serializedVersion: 2");
        L($"    filterMode: {(int)settings.FilterMode}");
        L($"    aniso: {settings.AnisoLevel}");
        L("    mipBias: 0");
        L($"    wrapU: {(int)settings.WrapMode}");
        L($"    wrapV: {(int)settings.WrapMode}");
        L($"    wrapW: {(int)settings.WrapMode}");
        L("  nPOTScale: 0");
        L("  lightmap: 0");
        L("  compressionQuality: 50");
        L($"  spriteMode: {(multiple ? 2 : 1)}");
        L($"  spriteExtrude: {settings.ExtrudeEdges}");
        L($"  spriteMeshType: {(settings.FullRectMesh ? 0 : 1)}");
        L($"  alignment: {AlignmentFor(settings.DefaultPivot, existing?.Alignment, existing?.SpritePivot)}");
        L($"  spritePivot: {Vec2(settings.DefaultPivot)}");
        L($"  spritePixelsToUnits: {F(settings.PixelsPerUnit)}");
        L($"  spriteBorder: {Vec4(settings.Border)}");
        L($"  spriteGenerateFallbackPhysicsShape: {(settings.GeneratePhysicsShape ? 1 : 0)}");
        L($"  alphaUsage: {(int)settings.AlphaSource}");
        L($"  alphaIsTransparency: {(settings.AlphaIsTransparency ? 1 : 0)}");
        L("  spriteTessellationMethod: 0");
        L("  spriteTessellationDetail: -1");
        L("  spriteGeometrySubdivision: -1");
        L("  textureType: 8");
        L("  textureShape: 1");
        L("  singleChannelComponent: 0");
        L("  flipbookRows: 1");
        L("  flipbookColumns: 1");
        L("  maxTextureSizeSet: 0");
        L("  compressionQualitySet: 0");
        L("  textureFormatSet: 0");
        L("  ignorePngGamma: 0");
        L("  applyGammaDecoding: 0");
        L("  swizzle: 50462976");
        L("  cookieLightType: 0");
        L("  platformSettings:");
        // Default가 실제 적용 값(설정대로)이고, Standalone은 재정의하지 않은 Unity 기본 직렬화 값 그대로 쓴다.
        foreach (var target in new[] { "DefaultTexturePlatform", "Standalone" })
        {
            bool isDefault = target == "DefaultTexturePlatform";
            L("  - serializedVersion: 4");
            L($"    buildTarget: {target}");
            L($"    maxTextureSize: {maxSize}");
            L($"    resizeAlgorithm: {(isDefault ? (int)settings.ResizeAlgorithm : 0)}");
            L($"    textureFormat: {(isDefault ? (int)settings.Format : -1)}");
            L($"    textureCompression: {(isDefault ? (int)settings.Compression : 1)}");
            L($"    compressionQuality: {(isDefault ? settings.CompressionQuality : 50)}");
            L($"    crunchedCompression: {(isDefault && settings.CrunchCompression ? 1 : 0)}");
            L("    allowsAlphaSplitting: 0");
            L("    overridden: 0");
            L("    ignorePlatformSupport: 0");
            L("    androidETC2FallbackOverride: 0");
            L("    forceMaximumCompressionQuality_BC6H_BC7: 0");
        }
        L("  spriteSheet:");
        L("    serializedVersion: 2");
        if (sprites.Count == 0) L("    sprites: []");
        else
        {
            L("    sprites:");
            foreach (var s in sprites)
            {
                L("    - serializedVersion: 2");
                L($"      name: {MiniYaml.QuoteIfNeeded(s.Name)}");
                L("      rect:");
                L("        serializedVersion: 2");
                L($"        x: {s.UnityRect.X}");
                L($"        y: {s.UnityRect.Y}");
                L($"        width: {s.UnityRect.Width}");
                L($"        height: {s.UnityRect.Height}");
                L($"      alignment: {s.Alignment}");
                L($"      pivot: {Vec2(s.Pivot)}");
                L($"      border: {Vec4(s.Border)}");
                L("      customData: ");
                L("      outline: []");
                L("      physicsShape: []");
                L("      tessellationDetail: 0");
                L("      bones: []");
                L($"      spriteID: {s.SpriteId}");
                L($"      internalID: {s.InternalId.ToString(CultureInfo.InvariantCulture)}");
                L("      vertices: []");
                L("      indices: ");
                L("      edges: []");
                L("      weights: []");
            }
        }
        L("    outline: []");
        L("    customData: ");
        L("    physicsShape: []");
        L("    bones: []");
        L($"    spriteID: {sheetSpriteId}");
        L("    internalID: 0");
        L("    vertices: []");
        L("    indices: ");
        L("    edges: []");
        L("    weights: []");
        L("    secondaryTextures: []");
        L("    spriteCustomMetadata:");
        L("      entries: []");
        if (sprites.Count == 0) L("    nameFileIdTable: {}");
        else
        {
            L("    nameFileIdTable:");
            foreach (var s in sprites.OrderBy(s => s.Name, StringComparer.Ordinal))
                L($"      {MiniYaml.QuoteIfNeeded(s.Name)}: {s.InternalId.ToString(CultureInfo.InvariantCulture)}");
        }
        L("  mipmapLimitGroupName: ");
        L("  pSDRemoveMatte: 0");
        L($"  userData: {Scalar(existing?.UserData)}");
        L($"  assetBundleName: {Scalar(existing?.AssetBundleName)}");
        L($"  assetBundleVariant: {Scalar(existing?.AssetBundleVariant)}");
        return sb.ToString();
    }

    /// <summary>
    /// 문서 칸을 메타 스프라이트로 바꾸며 ID를 정한다: 같은 이름 → 그 ID, 없으면 같은 사각형의
    /// 남는(이름이 바뀐) 기존 칸 → 그 ID, 그래도 없으면 새 ID.
    /// </summary>
    private static List<UnityMetaSprite> AssignIds(Document doc, UnityMetaInfo? existing, HashSet<long> usedIds)
    {
        var byName = new Dictionary<string, UnityMetaSprite>(StringComparer.Ordinal);
        var tableByName = new Dictionary<string, long>(StringComparer.Ordinal);
        if (existing is not null)
        {
            foreach (var s in existing.Sprites) byName.TryAdd(s.Name, s);
            foreach (var (id, name) in existing.InternalIdToName) tableByName.TryAdd(name, id);
        }
        var currentNames = doc.Slices.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var claimed = new HashSet<UnityMetaSprite>();
        var assignedIds = new HashSet<long>();
        var result = new List<UnityMetaSprite>(doc.Slices.Count);

        foreach (var slice in doc.Slices)
        {
            var unityRect = new Rectangle(slice.Rect.X, doc.Height - slice.Rect.Y - slice.Rect.Height,
                slice.Rect.Width, slice.Rect.Height);
            UnityMetaSprite? match = null;
            if (byName.TryGetValue(slice.Name, out var named) && !claimed.Contains(named)) match = named;
            match ??= existing?.Sprites.FirstOrDefault(s =>
                !claimed.Contains(s) && !currentNames.Contains(s.Name) && s.UnityRect == unityRect);
            if (match is not null) claimed.Add(match);

            long internalId = match is not null && match.InternalId != 0 && !assignedIds.Contains(match.InternalId)
                ? match.InternalId
                : tableByName.TryGetValue(slice.Name, out var tid) && !assignedIds.Contains(tid)
                    ? tid
                    : NewInternalId(usedIds);
            usedIds.Add(internalId);
            assignedIds.Add(internalId);

            // 잘라낸(trim) 칸은 피벗이 칸 밖(0~1 밖)에 있을 수 있다. Unity Custom 피벗은 이를 허용한다.
            var pivot = slice.Pivot;
            result.Add(new UnityMetaSprite
            {
                Name = slice.Name,
                UnityRect = unityRect,
                Alignment = AlignmentFor(pivot, match?.Alignment, match?.Pivot),
                Pivot = pivot,
                Border = slice.Border,
                SpriteId = match?.SpriteId is { Length: 32 } spriteId ? spriteId : NewSpriteId(),
                InternalId = internalId,
            });
        }
        return result;
    }

    /// <summary>
    /// 피벗의 Unity alignment. 기존 메타가 같은 피벗을 Custom(9)으로 적어 두었으면 그대로 9를 유지해
    /// 다시 저장해도 메타 내용이 바뀌지 않게 한다.
    /// </summary>
    private static int AlignmentFor(Pivot pivot, int? existingAlignment, Pivot? existingPivot)
        => existingAlignment == (int)PivotPreset.Custom && existingPivot == pivot
            ? (int)PivotPreset.Custom
            : (int)pivot.Preset;

    /// <summary>Unity가 축소하지 않도록 이미지 긴 변 이상인 최대 텍스처 크기(최소 2048).</summary>
    public static int MaxTextureSizeFor(int width, int height)
    {
        int size = 2048;
        int longest = Math.Max(width, height);
        while (size < longest && size < PixelImage.MaxSize) size *= 2;
        return size;
    }

    /// <summary>새 에셋 guid(소문자 16진수 32자).</summary>
    public static string NewGuid() => System.Guid.NewGuid().ToString("N");

    /// <summary>새 spriteID(16진수 32자).</summary>
    public static string NewSpriteId() => System.Guid.NewGuid().ToString("N");

    /// <summary>사용 중이지 않은 양수 64비트 internalID.</summary>
    public static long NewInternalId(ISet<long> used)
    {
        Span<byte> buf = stackalloc byte[8];
        while (true)
        {
            RandomNumberGenerator.Fill(buf);
            long id = BitConverter.ToInt64(buf) & long.MaxValue;
            // Unity 고정 ID(21300000 = Single 스프라이트)와 0은 피한다.
            if (id > 21300000 && used.Add(id)) return id;
        }
    }

    // ───────────── 도우미 ─────────────

    private static bool IsGuid(string s) => s.Length == 32 && s.All(Uri.IsHexDigit);

    private static string? FindGuidFallback(string yaml)
    {
        var m = Regex.Match(yaml, @"^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Pivot ResolvePivot(int alignment, YamlNode? pivotNode)
    {
        if (alignment is >= 0 and < (int)PivotPreset.Custom) return Pivot.FromPreset((PivotPreset)alignment);
        return new Pivot(Float(pivotNode?["x"], 0.5f), Float(pivotNode?["y"], 0.5f));
    }

    /// <summary>Unity Vector4 테두리: x=왼쪽, y=아래, z=오른쪽, w=위.</summary>
    private static SpriteBorder ReadBorder(YamlNode? node)
        => new((int)Float(node?["x"], 0), (int)Float(node?["w"], 0), (int)Float(node?["z"], 0), (int)Float(node?["y"], 0));

    private static string Vec2(Pivot p) => $"{{x: {F(p.X)}, y: {F(p.Y)}}}";

    private static string Vec4(SpriteBorder b) => $"{{x: {b.Left}, y: {b.Bottom}, z: {b.Right}, w: {b.Top}}}";

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');

    private static string Scalar(string? s) => string.IsNullOrEmpty(s) ? "" : MiniYaml.QuoteIfNeeded(s);

    /// <summary>정의된 enum 값이면 그것, 아니면 기본값.</summary>
    private static T EnumOr<T>(int value, T fallback) where T : struct, Enum
        => Enum.IsDefined(typeof(T), value) ? (T)Enum.ToObject(typeof(T), value) : fallback;

    private static int Int(YamlNode? node, int fallback)
        => int.TryParse(node?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static long? Long(YamlNode? node)
        => long.TryParse(node?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static float Float(YamlNode? node, float fallback)
        => float.TryParse(node?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v)
            ? v
            : fallback;
}
