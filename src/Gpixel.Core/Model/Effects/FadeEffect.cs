namespace Gpixel.Core.Model;

/// <summary>효과 범위 동안 레이어의 알파를 0→원본(페이드 인) 또는 원본→0(페이드 아웃)으로 바꾼다.</summary>
public sealed record FadeEffect : AnimatedEffect
{
    /// <summary>true = 페이드 아웃, false = 페이드 인.</summary>
    public bool FadeOut { get; init; }

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Fade;
}
