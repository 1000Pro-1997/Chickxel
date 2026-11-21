using System.Runtime.InteropServices;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using SkiaSharp;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// 3D 모델 파일(glTF 2.0 .gltf·.glb, Wavefront .obj+.mtl, FBX 바이너리 7.x .fbx)을 <see cref="ModelScene"/>으로 읽는다.
/// 잘못되었거나 지원하지 않는 파일은 <see cref="InvalidDataException"/>(현지화된 문구), 파일을 못 읽으면 <see cref="IOException"/>이 그대로 나간다.
/// </summary>
public static class ModelLoader
{
    /// <summary>한 장면에서 그릴 수 있는 삼각형 수의 최댓값(그리기·메모리가 감당할 만큼).</summary>
    public const int MaxTriangles = 5_000_000;

    /// <summary>열 수 있는 확장자(소문자, 점 포함).</summary>
    public static IReadOnlyList<string> Extensions => MediaFormats.ModelExtensions;

    /// <summary>확장자로 보아 3D 모델 파일인지(대소문자 무시).</summary>
    public static bool IsModelFile(string path) => MediaFormats.KindOf(path) == MediaKind.Model;

    public static ModelScene Load(string path)
    {
        var scene = Read(path);

        // 그려질 삼각형 수: 메시를 단 노드마다(같은 메시를 여러 노드가 쓰면 그만큼 더 그림).
        long triangles = 0;
        foreach (var node in scene.Nodes)
        {
            if ((uint)node.Mesh >= (uint)scene.Meshes.Count) continue;
            foreach (var p in scene.Meshes[node.Mesh].Primitives) triangles += p.Indices.Length / 3;
        }
        if (triangles == 0) throw new InvalidDataException(CoreStrings.Model_NoTriangles);
        if (triangles > MaxTriangles) throw new InvalidDataException(CoreStrings.FormatModel_TooManyTriangles(count: triangles, max: MaxTriangles));
        return scene;
    }

    /// <summary>
    /// 애니메이션을 가져올 파일 읽기: <see cref="Load"/>와 같은 형식을 읽되 삼각형이 없어도 된다(Mixamo "Without Skin" FBX처럼 뼈대와 애니메이션만 있는 파일).
    /// 채널이 있는 애니메이션이 하나도 없으면 <see cref="InvalidDataException"/>.
    /// </summary>
    public static ModelScene LoadAnimations(string path)
    {
        var scene = Read(path);
        foreach (var animation in scene.Animations)
            if (animation.Channels.Count > 0) return scene;
        throw new InvalidDataException(CoreStrings.Model_NoAnimations);
    }

    /// <summary>확장자(대소문자 무시)로 고른 읽기.</summary>
    private static ModelScene Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".glb" or ".gltf" => GltfReader.Read(path),
            ".obj" => ObjReader.Read(path),
            ".fbx" => FbxReader.Read(path),
            _ => throw new InvalidDataException(CoreStrings.FormatModel_UnsupportedFormat(extension: ext)),
        };
    }
}

/// <summary>모델 읽기들이 같이 쓰는 도우미: 텍스처 풀기, 알림 모으기.</summary>
internal static class ModelTexture
{
    /// <summary>
    /// PNG·JPEG(·WebP) 바이트를 sRGB 그대로 <see cref="PixelImage"/>로 푼다. 풀 수 없으면 알림을 남기고 null,
    /// <see cref="PixelImage.MaxSize"/>보다 크면 <see cref="InvalidDataException"/>.
    /// </summary>
    public static PixelImage? Decode(ReadOnlySpan<byte> bytes, string label, List<string> warnings)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null)
        {
            Warn(warnings, CoreStrings.FormatModel_Warning_TextureUnreadable(name: label));
            return null;
        }
        int w = codec.Info.Width, h = codec.Info.Height;
        if (w > PixelImage.MaxSize || h > PixelImage.MaxSize)
            throw new InvalidDataException(CoreStrings.FormatModel_TextureTooLarge(width: w, height: h, max: PixelImage.MaxSize));
        if (w < 1 || h < 1)
        {
            Warn(warnings, CoreStrings.FormatModel_Warning_TextureUnreadable(name: label));
            return null;
        }

        // Color32(0xAARRGGBB)의 메모리 배치는 B,G,R,A = Bgra8888, 알파는 미리 곱하지 않음 — 픽셀 배열에 바로 푼다.
        var image = new PixelImage(w, h);
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        SKCodecResult result;
        try { result = codec.GetPixels(info, handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            Warn(warnings, CoreStrings.FormatModel_Warning_TextureUnreadable(name: label));
            return null;
        }
        return image;
    }

    /// <summary>텍스처에 반투명·투명 픽셀이 있는지(OBJ 재질의 자르기 여부).</summary>
    public static bool HasTransparency(PixelImage image)
    {
        foreach (var c in image.Pixels)
            if (c.A < 255) return true;
        return false;
    }

    /// <summary>같은 알림은 한 번만 남긴다.</summary>
    public static void Warn(List<string> warnings, string message)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
    }
}
