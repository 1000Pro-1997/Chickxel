using Gpixel.App.Localization;
using Gpixel.Core.References;

namespace Gpixel.App;

/// <summary>
/// AI 참고 자료의 앱 쪽: 폴더 위치(설정 → 기본 <c>&lt;설정 폴더&gt;/Gpixel/References</c>), GIF 풀기(<see cref="ImageUtil.DecodeFrames"/>),
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
            DecodeOther = path => ImageUtil.DecodeFrames(path) is { } f ? new ReferenceFrames(f.Images, f.DurationsMs) : null,
            OtherImageExtensions = [".gif"],
            BuiltIns = _builtIns ??= BuiltInReferences.Examples(),
            Korean = UiLanguage.Current == UiLanguage.Korean,
        });
        lib.ShowBuiltIns = settings.ReferencesShowExamples;
        return lib;
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
