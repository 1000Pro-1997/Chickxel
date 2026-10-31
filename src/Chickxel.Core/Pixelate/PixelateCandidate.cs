namespace Chickxel.Core.Pixelate;

/// <summary>후보 하나: 번호(0 = 시작 설정), 설정, 결과, 점수 — 자동 최적화의 시도(<see cref="PixelateTrial"/>)와 정리 창의 프리셋 칸이 쓴다.</summary>
public sealed record PixelateCandidate(int Index, PixelateSettings Settings, PixelateResult Result, PixelateScore Score);
