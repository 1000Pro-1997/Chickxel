using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>
/// 레이어 효과 목록 고치기(빼기·옮겨 넣기)에서 클리핑(<see cref="LayerEffect.ClipToPrevious"/>)이 다른 효과에 잘못 붙지 않게 맞춘다.
/// 타임라인 효과 줄과 AI 도구가 같이 쓴다.
/// </summary>
public static class EffectStack
{
    /// <summary>효과를 빼면서, 기준 효과가 빠진 클리핑 효과는 다른 효과에 잘못 붙지 않게 클리핑을 해제한다.</summary>
    public static List<LayerEffect> Remove(IReadOnlyList<LayerEffect> effects, IReadOnlySet<int> removed)
    {
        var result = new List<LayerEffect>(Math.Max(0, effects.Count - removed.Count));
        for (int i = 0; i < effects.Count; i++)
        {
            if (removed.Contains(i)) continue;
            var effect = effects[i];
            if (effect.ClipToPrevious)
            {
                int anchor = i - 1;
                while (anchor >= 0 && effects[anchor].ClipToPrevious) anchor--;
                if (anchor < 0 || removed.Contains(anchor))
                    effect = effect with { ClipToPrevious = false };
            }
            result.Add(effect);
        }
        return result;
    }

    /// <summary>
    /// 효과 덩어리 <paramref name="block"/>를 <paramref name="effects"/>의 <paramref name="at"/> 자리(0~개수로 맞춤)에 옮겨 넣는다:
    /// 덩어리 첫 효과는 새 기준 효과가 되고(클리핑 해제), 그 자리에 있던 클리핑 효과들은 덩어리에 붙지 않게 클리핑을 해제한다. 넣은 자리를 돌려준다.
    /// </summary>
    public static int InsertBlock(List<LayerEffect> effects, int at, IReadOnlyList<LayerEffect> block)
    {
        at = Math.Clamp(at, 0, effects.Count);
        if (block.Count == 0) return at;
        for (int i = at; i < effects.Count && effects[i].ClipToPrevious; i++)
            effects[i] = effects[i] with { ClipToPrevious = false };
        var moved = block.ToList();
        if (moved[0].ClipToPrevious) moved[0] = moved[0] with { ClipToPrevious = false };
        effects.InsertRange(at, moved);
        return at;
    }

    /// <summary>효과 하나를 <paramref name="from"/>에서 (그 효과를 뺀 목록 기준) <paramref name="to"/> 자리로 옮긴 새 목록(<see cref="Remove"/> + <see cref="InsertBlock"/>).</summary>
    public static List<LayerEffect> Move(IReadOnlyList<LayerEffect> effects, int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, effects.Count);
        var rest = Remove(effects, new HashSet<int> { from });
        InsertBlock(rest, to, [effects[from]]);
        return rest;
    }
}
