namespace Chickxel.Core.Edit;

/// <summary>순서 디더(Bayer) 문턱값. 좌표는 음수여도 되고(무늬가 그대로 이어짐) 같은 좌표는 늘 같은 값이다.</summary>
internal static class Bayer
{
    private static readonly int[] M2 = [0, 2, 3, 1];
    private static readonly int[] M4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>2×2 무늬의 0~3.</summary>
    public static int Threshold2(int x, int y) => M2[(y & 1) * 2 + (x & 1)];

    /// <summary>4×4 무늬의 0~15(&lt; 8인 칸이 바둑판).</summary>
    public static int Threshold4(int x, int y) => M4[(y & 3) * 4 + (x & 3)];

    /// <summary>
    /// 0~1 값 <paramref name="t"/>를 그 자리의 무늬로 0 또는 1로(<paramref name="dither"/> 없음이면 쓰지 않음 — 부르는 쪽이 따로 처리).
    /// 2×2는 5단계, 4×4는 17단계(0이면 늘 false, 1이면 늘 true).
    /// </summary>
    public static bool On(Model.EffectDither dither, double t, int x, int y) => dither == Model.EffectDither.Bayer2
        ? t * 4 > Threshold2(x, y) + 0.5 - 1e-9
        : t * 16 > Threshold4(x, y) + 0.5 - 1e-9;
}
