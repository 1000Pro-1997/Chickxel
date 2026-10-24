using System.Runtime.InteropServices;
using Gpixel.App.Localization;
using Gpixel.Core.Model;
using Gpixel.Core.References;
using SkiaSharp;

namespace Gpixel.App;

/// <summary>
/// AI 참고 자료의 앱 쪽: 폴더 위치(설정 → 기본 <c>&lt;설정 폴더&gt;/Gpixel/References</c>), GIF 풀기(SkiaSharp),
/// 기본 예시·기본 규칙(지금 화면 언어)을 붙여 <see cref="ReferenceLibrary"/>를 연다.
/// </summary>
public static class AppReferences
{
    public static string DefaultFolder => Path.Combine(Platform.AppDataDirectory, "Gpixel", "References");

    /// <summary>설정의 폴더(비었으면 기본 폴더).</summary>
    public static string FolderOf(AppSettings settings)
        => string.IsNullOrWhiteSpace(settings.ReferencesFolder) ? DefaultFolder : settings.ReferencesFolder;

    private static IReadOnlyList<BuiltInReference>? _builtIns;

    /// <summary>
    /// 설정대로 자료 폴더를 연다(없으면 만들고, 색인을 파일과 맞춤). 폴더를 만들 수 없으면 IO 예외.
    /// </summary>
    public static ReferenceLibrary Open(AppSettings settings)
    {
        var lib = ReferenceLibrary.Open(FolderOf(settings), new ReferenceLibraryOptions
        {
            DecodeOther = DecodeGif,
            OtherImageExtensions = [".gif"],
            BuiltIns = _builtIns ??= BuiltInReferences.Examples(),
            Korean = UiLanguage.Current == UiLanguage.Korean,
        });
        lib.ShowBuiltIns = settings.ReferencesShowExamples;
        return lib;
    }

    /// <summary>GIF(움직이는 것 포함)를 프레임마다 완성된 그림으로 푼다. 못 풀면 null.</summary>
    internal static ReferenceFrames? DecodeGif(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null) return null;
        int w = codec.Info.Width, h = codec.Info.Height;
        if (w <= 0 || h <= 0 || w > PixelImage.MaxSize || h > PixelImage.MaxSize) return null;
        var infos = codec.FrameInfo;
        int count = Math.Max(1, infos.Length);
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var images = new List<PixelImage>(count);
        var durations = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            // 앞 프레임 위에 이어 푼다(코덱이 그 프레임을 바탕으로 쓸 수 없다고 하면 처음부터).
            var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, i > 0 ? i - 1 : -1));
            if (result == SKCodecResult.InvalidParameters && i > 0) result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, -1));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return images.Count > 0 ? new ReferenceFrames(images, durations) : null;
            var pixels = new Color32[w * h];
            MemoryMarshal.Cast<byte, Color32>(bitmap.GetPixelSpan()).Slice(0, pixels.Length).CopyTo(pixels);
            images.Add(new PixelImage(w, h, pixels));
            int ms = i < infos.Length ? infos[i].Duration : Frame.DefaultDurationMs;
            durations.Add(ms > 10 ? ms : Frame.DefaultDurationMs);
        }
        return new ReferenceFrames(images, durations);
    }

    /// <summary>종류 id의 화면 이름.</summary>
    public static string KindLabel(string kind) => kind switch
    {
        ReferenceKinds.Character => UiStrings.Refs_Kind_Character,
        ReferenceKinds.Item => UiStrings.Refs_Kind_Item,
        ReferenceKinds.Tile => UiStrings.Refs_Kind_Tile,
        ReferenceKinds.Effect => UiStrings.Refs_Kind_Effect,
        ReferenceKinds.Ui => UiStrings.Refs_Kind_Ui,
        _ => UiStrings.Refs_Kind_Other,
    };

    /// <summary>"AI로 설명 붙이기": 채팅 입력란에 넣을 부탁(설명 없는 자료 id들).</summary>
    public static string CaptionRequest(IReadOnlyList<ReferenceItem> items)
        => UiStrings.FormatRefs_CaptionRequest(count: items.Count, ids: string.Join(", ", items.Select(i => i.Id)));
}
