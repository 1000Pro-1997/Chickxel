namespace Gpixel.Core.AiDraw;

/// <summary>AI 채팅이 그림을 그리는 방식.</summary>
public enum AiDrawMode
{
    /// <summary>이미지 생성(Codex)으로 그림을 만들고 Gpixel이 픽셀 아트로 변환해 넣는다.</summary>
    Generate,
    /// <summary>AI가 MCP 픽셀 도구로 직접 찍는다(예전 방식).</summary>
    Direct,
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
/// (사용자가 채팅에서 Codex에 맡기라고 하면 그 차례만 생성 도구를 쓴다 — <see cref="EffectiveMode"/>).
/// </summary>
public sealed class AiDrawSettings
{
    /// <summary>결과 크기 직접 입력의 범위(px, 긴 변).</summary>
    public const int MinSize = 8, MaxSize = 256;
    /// <summary>애니메이션 프레임 수 범위.</summary>
    public const int MinFrames = 2, MaxFrames = 12;
    /// <summary>그림을 만들 수 있는 AI(채팅 탭 id).</summary>
    public const string GeneratingProvider = "codex";

    public AiDrawMode Mode { get; set; } = AiDrawMode.Generate;

    /// <summary>결과 크기(긴 변 px): 0 = 자동(그림의 픽셀 격자 그대로).</summary>
    public int SizeTarget { get; set; }

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

    /// <summary>그 AI 탭에서 실제로 쓰는 방식: Codex 탭은 <see cref="Mode"/>, 다른 AI(그림을 만들 수 없음)는 늘 직접 찍기.</summary>
    public AiDrawMode EffectiveMode(string provider) => provider == GeneratingProvider ? Mode : AiDrawMode.Direct;

    /// <summary>동작의 기본 프레임 수(<see cref="MinFrames"/>~<see cref="MaxFrames"/>).</summary>
    public int FramesFor(string action)
        => Math.Clamp(FrameCounts.TryGetValue(action, out var n) ? n : AiDrawActions.DefaultFrames(action), MinFrames, MaxFrames);

    /// <summary>결과 크기 목표(0 = 자동, 아니면 <see cref="MinSize"/>~<see cref="MaxSize"/>).</summary>
    public int SizeTargetClamped => SizeTarget <= 0 ? 0 : Math.Clamp(SizeTarget, MinSize, MaxSize);
}
