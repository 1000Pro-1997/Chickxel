namespace Chickxel.Core.AiDraw;

/// <summary>AI 채팅이 그림을 그리는 방식.</summary>
public enum AiDrawMode
{
    /// <summary>이미지 생성(Codex)으로 원본 그림을 만들고, 사용자가 픽셀 아트 정리 창에서 변환해 넣는다.</summary>
    Generate,
    /// <summary>AI가 MCP 픽셀 도구로 직접 찍는다.</summary>
    Direct,
    /// <summary>"지금 그림만"이 켜져 있으면 직접 찍고, 아니면 그림 생성을 먼저 하고 생성이 안 되면 직접 찍는다.</summary>
    Auto,
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
public sealed class AiDrawSettings
{
    /// <summary>채팅에서 정한 그림 크기의 범위(px, 긴 변 — 생성 프롬프트의 크기 힌트).</summary>
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
    /// 생성 요청 다듬기(화면 이름, 예전 "자동 프롬프트" — 기본 켬): 켜면 AI가 정한 대상 설명을 앱의 생성 틀(스타일·픽셀 규칙·배경·시트 배치·자세·스타일 참고)로 감싸 보낸다.
    /// 끄면 사용자 메시지를 그대로 이미지 생성에 보내고(붙인 그림은 순서대로 함께), 생성 전 크기 판정·스타일 참고·애니메이션 계획 없이 한 장을 만든다.
    /// 예전 설정 파일에는 없으면 켬이다.
    /// </summary>
    public bool AutoPrompt { get; set; } = true;

    public AiDrawStyleReference StyleReference { get; set; } = AiDrawStyleReference.Auto;

    public AiDrawAnimationMethod AnimationMethod { get; set; } = AiDrawAnimationMethod.SingleSheet;

    /// <summary><see cref="AiDrawStyleReference.Specific"/>일 때 쓸 참고 자료 id.</summary>
    public string? StyleReferenceId { get; set; }

    /// <summary>동작 이름(<see cref="AiDrawActions"/>) → 기본 프레임 수. 빠진 동작은 <see cref="AiDrawActions.DefaultFrames"/>.</summary>
    public Dictionary<string, int> FrameCounts { get; set; } = AiDrawActions.DefaultFrameCounts();

    /// <summary>
    /// 이번 차례의 그리는 방식. Claude는 늘 직접 찍는다. Codex 자동 모드는 채팅의 "지금 그림만"이 켜져 열린 그림만 고칠 때(<paramref name="currentFileOnly"/>)는 직접 찍고,
    /// 아니면 그림 생성 → 픽셀 변환을 먼저 한다(생성이 안 되면 직접 찍기로 — 모델에게 알리는 글은 <c>AiCli.DrawingModeLine</c>).
    /// </summary>
    public AiDrawMode EffectiveMode(string provider, bool currentFileOnly)
    {
        if (provider != GeneratingProvider) return AiDrawMode.Direct;
        if (Mode != AiDrawMode.Auto) return Mode;
        return currentFileOnly ? AiDrawMode.Direct : AiDrawMode.Generate;
    }

    /// <summary>동작의 기본 프레임 수(<see cref="MinFrames"/>~<see cref="MaxFrames"/>).</summary>
    public int FramesFor(string action)
        => Math.Clamp(FrameCounts.TryGetValue(action, out var n) ? n : AiDrawActions.DefaultFrames(action), MinFrames, MaxFrames);
}
