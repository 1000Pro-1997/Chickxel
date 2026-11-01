using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>
/// 지우개 강도·채널: 한 획에서 픽셀마다 한 번만 적용한다(획을 다시 문지르면 남은 값에서 또 줄어듦).
/// R·G·B를 모두 고르면 알파를 강도만큼 줄여 투명하게 하고(100% = 완전히 지움), 일부만 고르면 고른 색 채널만 강도만큼 줄인다(알파는 그대로 — 그 색만 빼기).
/// </summary>
public static class PartialErase
{
    /// <summary>
    /// <paramref name="c"/>에 강도 <paramref name="strength"/>(1~100%)로 지우기를 적용한 색. 투명이거나 고른 채널이 없으면 그대로.
    /// 알파가 0이 되면 <see cref="Color32.Transparent"/>(완전히 지운 픽셀과 같은 값).
    /// </summary>
    public static Color32 Apply(Color32 c, int strength, bool red, bool green, bool blue)
    {
        if (c.A == 0 || !(red || green || blue)) return c;
        double keep = 1 - Math.Clamp(strength, 1, 100) / 100.0;
        // 내림: 강도가 아무리 낮아도 한 번 문지르면 조금은 줄어 끝내 0(투명)에 닿는다
        byte Scale(byte v) => (byte)Math.Floor(v * keep);
        if (red && green && blue)
        {
            byte a = Scale(c.A);
            return a == 0 ? Color32.Transparent : new Color32(c.R, c.G, c.B, a);
        }
        return new Color32(red ? Scale(c.R) : c.R, green ? Scale(c.G) : c.G, blue ? Scale(c.B) : c.B, c.A);
    }

    /// <summary>강도 100%·R·G·B 모두면 보통 지우개(완전히 지움)와 같다.</summary>
    public static bool IsFull(int strength, bool red, bool green, bool blue) => strength >= 100 && red && green && blue;
}
