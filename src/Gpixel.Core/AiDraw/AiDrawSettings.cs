using System.Text.RegularExpressions;

namespace Gpixel.Core.AiDraw;

/// <summary>AI 채팅이 그림을 그리는 방식.</summary>
public enum AiDrawMode
{
    /// <summary>이미지 생성(Codex)으로 그림을 만들고 Gpixel이 픽셀 아트로 변환해 넣는다.</summary>
    Generate,
    /// <summary>AI가 MCP 픽셀 도구로 직접 찍는다.</summary>
    Direct,
    /// <summary>새 그림은 생성하고, 정확한 크기나 기존 그림의 수정은 직접 찍는다.</summary>
    Auto,
}

/// <summary>변환한 그림을 넣는 곳.</summary>
public enum AiDrawInsert
{
    NewLayer,
    NewDocument,
}

/// <summary>생성할 때 함께 보낼 픽셀 아트 스타일 참고 그림.</summary>
public enum AiDrawStyleReference
{
    /// <summary>
    /// 참고 자료 보관소에서 고른다: 사용자가 픽셀 아트가 아닌 그림(캐릭터)을 붙여 그 캐릭터를 그리게 할 때, 또는 그리는 대상과 글이 맞는 자료가 있을 때만.
    /// </summary>
    Auto,
    None,
    /// <summary><see cref="AiDrawSettings.StyleReferenceId"/>의 자료를 늘 쓴다.</summary>
    Specific,
}

/// <summary>애니메이션을 생성하는 방식.</summary>
public enum AiDrawAnimationMethod
{
    /// <summary>한 번의 생성으로 모든 프레임을 한 장(스프라이트 시트)에 — 같은 그림 안이라 캐릭터·크기·카메라가 맞는다(기본).</summary>
    SingleSheet,
    /// <summary>프레임마다 따로 생성(첫·앞 프레임을 참고로) — 프레임 사이 일관성이 낮다.</summary>
    PerFrame,
}

/// <summary>
/// AI 그리기 설정(채팅 창의 설정 칸, 설정 파일에 저장). 그림 생성은 Codex만 할 수 있어, Claude 탭은 설정과 관계없이 직접 찍는다
/// (사용자가 채팅에서 Codex에 맡기라고 하면 그 차례만 생성 도구를 쓴다).
/// </summary>
public sealed partial class AiDrawSettings
{
    /// <summary>결과 크기 직접 입력의 범위(px, 긴 변).</summary>
    public const int MinSize = 8, MaxSize = 256;
    /// <summary>애니메이션 프레임 수 범위.</summary>
    public const int MinFrames = 2, MaxFrames = 12;
    /// <summary>그림을 만들 수 있는 AI(채팅 탭 id).</summary>
    public const string GeneratingProvider = "codex";

    public AiDrawMode Mode { get; set; } = AiDrawMode.Auto;

    /// <summary>
    /// 팔레트 색만 사용(직접 찍기): AI가 픽셀 도구로 칠하는 색을 활성 팔레트의 가장 가까운 색으로 바꾼다(<see cref="PaletteLock"/>). 그림 생성 결과(변환기)와 레이어 효과는 그대로.
    /// </summary>
    public bool PaletteOnly { get; set; }

    /// <summary>
    /// 결과 크기 보정(기본 끔): 일반 생성 그림에서 켜면 변환 결과를 <see cref="SizeTarget"/>(또는 채팅에서 정한 크기)에 맞춘다.
    /// 끄면 대표 프리셋의 결과 크기 설정을 따른다. 참고 이미지를 픽셀 아트로 바꾸는 차례는 채팅 크기, 켜 둔 보정 크기,
    /// 이미지 판정 크기 중 먼저 정해진 값으로 결과를 맞춘다. 예전 설정 파일에는 없으면 끔이다.
    /// </summary>
    public bool ResizeResult { get; set; }

    /// <summary>결과 크기 보정을 켰을 때 쓸 크기의 기본값(px).</summary>
    public const int DefaultSize = 32;

    /// <summary>결과 크기(긴 변 px, <see cref="ResizeResult"/>일 때만 쓰임). 0 이하(예전 "자동" 값)는 <see cref="DefaultSize"/>로 본다.</summary>
    public int SizeTarget { get; set; } = DefaultSize;

    /// <summary>
    /// 생성 프롬프트에 넣을 크기 힌트와 변환 고정 크기(0 = 없음·자동): <paramref name="requested"/> = 채팅에서 정한 크기(없으면 null).
    /// 보정이 꺼져 있으면 힌트는 채팅에서 정한 크기뿐이고 변환은 자동, 켜져 있으면 둘 다 채팅 크기 또는 설정 크기.
    /// </summary>
    public (int Hint, int Convert) SizesFor(int? requested)
    {
        int clamped = requested is { } r ? Math.Clamp(r, MinSize, MaxSize) : 0;
        if (!ResizeResult) return (clamped, 0);
        int size = requested is not null ? clamped : SizeTargetClamped;
        return (size, size);
    }

    /// <summary>변환할 때 배경을 지운다(기본 켬).</summary>
    public bool RemoveBackground { get; set; } = true;

    /// <summary>변환할 때 자동 최적화를 돌린다(기본 켬).</summary>
    public bool AutoOptimize { get; set; } = true;

    public AiDrawInsert Insert { get; set; } = AiDrawInsert.NewLayer;

    public AiDrawStyleReference StyleReference { get; set; } = AiDrawStyleReference.Auto;

    public AiDrawAnimationMethod AnimationMethod { get; set; } = AiDrawAnimationMethod.SingleSheet;

    /// <summary><see cref="AiDrawStyleReference.Specific"/>일 때 쓸 참고 자료 id.</summary>
    public string? StyleReferenceId { get; set; }

    /// <summary>동작 이름(<see cref="AiDrawActions"/>) → 기본 프레임 수. 빠진 동작은 <see cref="AiDrawActions.DefaultFrames"/>.</summary>
    public Dictionary<string, int> FrameCounts { get; set; } = AiDrawActions.DefaultFrameCounts();

    /// <summary>Codex 자동 모드: 참고 이미지를 픽셀 아트로 변환할 때는 지정 크기도 생성에 넘기고, 그 밖의 정확한 크기·기존 그림 수정은 직접 찍는다. Claude는 늘 직접 찍는다.</summary>
    public AiDrawMode EffectiveMode(string provider, string request, bool hasOpenProject = false, bool hasAttachedImage = false)
    {
        if (provider != GeneratingProvider) return AiDrawMode.Direct;
        if (Mode != AiDrawMode.Auto) return Mode;
        if (hasAttachedImage && PixelArtConversion().IsMatch(request)) return AiDrawMode.Generate;
        if (ExplicitSize().IsMatch(request)) return AiDrawMode.Direct;
        bool existing = CurrentArtKo().IsMatch(request) || CurrentArtEn().IsMatch(request);
        bool changing = EditKo().IsMatch(request) || EditEn().IsMatch(request);
        return (existing && changing) || (hasOpenProject && (ChangeKo().IsMatch(request) || ChangeEn().IsMatch(request)))
            ? AiDrawMode.Direct : AiDrawMode.Generate;
    }

    [GeneratedRegex(@"(?:픽셀\s*(?:아트|로|화)|도트\s*(?:그림|아트)?|pixel[\s-]*(?:art|sprite)).*(?:변환|바꿔|바꾸|만들|그려|그리|convert|turn|make|draw)|(?:변환|바꿔|바꾸|만들|그려|그리|convert|turn|make|draw).*(?:픽셀\s*(?:아트|로|화)|도트\s*(?:그림|아트)?|pixel[\s-]*(?:art|sprite))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PixelArtConversion();

    [GeneratedRegex(@"(?<!\w)\d+\s*(?:[x×*]\s*\d+|px|pixels?|픽셀|화소)|(?:가로|세로|width|height)\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitSize();

    [GeneratedRegex(@"(?:지금|현재|기존|열린|이)\s*(?:그림|이미지|캔버스|스프라이트|레이어)")]
    private static partial Regex CurrentArtKo();

    [GeneratedRegex(@"\b(?:this|current|existing|open)\s+(?:image|picture|canvas|sprite|layer|drawing|artwork)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrentArtEn();

    [GeneratedRegex(@"수정|고쳐|고치|바꿔|바꾸|변경|편집|지워|지우|삭제|덧그|칠해|칠하|그려|그리|추가|옮겨|옮기|늘려|줄여|교체")]
    private static partial Regex EditKo();

    [GeneratedRegex(@"\b(?:edit|change|modify|recolor|fix|adjust|add|remove|erase|paint|draw|move|resize)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EditEn();
    [GeneratedRegex(@"수정|고쳐|고치|바꿔|바꾸|변경|편집|지워|지우|삭제|덧그|칠해|칠하|옮겨|옮기|늘려|줄여|교체")]
    private static partial Regex ChangeKo();

    [GeneratedRegex(@"\b(?:edit|change|modify|recolor|fix|adjust|remove|erase|move|resize)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChangeEn();

    /// <summary>동작의 기본 프레임 수(<see cref="MinFrames"/>~<see cref="MaxFrames"/>).</summary>
    public int FramesFor(string action)
        => Math.Clamp(FrameCounts.TryGetValue(action, out var n) ? n : AiDrawActions.DefaultFrames(action), MinFrames, MaxFrames);

    /// <summary>결과 크기 보정 크기(<see cref="MinSize"/>~<see cref="MaxSize"/>, 0 이하는 <see cref="DefaultSize"/>).</summary>
    public int SizeTargetClamped => SizeTarget <= 0 ? DefaultSize : Math.Clamp(SizeTarget, MinSize, MaxSize);
}
