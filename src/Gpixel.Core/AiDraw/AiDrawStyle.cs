using Gpixel.Core.References;

namespace Gpixel.Core.AiDraw;

/// <summary>생성에 함께 보낼 픽셀 아트 스타일 참고 그림 고르기(참고 자료 보관소의 기본 예시 + 사용자 자료).</summary>
public static class AiDrawStyle
{
    /// <summary>크기 목표가 자동일 때 견줄 긴 변(px).</summary>
    public const int AutoSize = 32;

    /// <summary>
    /// 가장 알맞은 그림 하나(없으면 null). 그림만, 점수 = 종류가 같으면 +2, 글 점수(<paramref name="textScores"/>, 0~1 — 대상 설명으로 찾은 것) ×2,
    /// 긴 변이 목표 크기와 다른 만큼(log₂ 차) −0.5씩, 색 수가 4~32면 +0.3, 사용자 자료면 +0.2(사용자의 그림체). 같으면 id 순.
    /// <paramref name="requireText"/>면 글이 맞은 그림만(자동 모드에서 붙인 그림이 없을 때).
    /// </summary>
    public static ReferenceItem? Pick(IEnumerable<ReferenceItem> items, IReadOnlyDictionary<string, double>? textScores, string? kind, int sizeTarget, bool requireText = false)
    {
        int target = sizeTarget > 0 ? sizeTarget : AutoSize;
        ReferenceItem? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (var i in items.Where(i => i.IsImage && i.Width > 0 && i.Height > 0).OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            double text = textScores is not null && textScores.TryGetValue(i.Id, out var t) ? t : 0;
            if (requireText && text <= 0) continue;
            double score = 2 * text;
            if (kind is not null && i.Kind == kind) score += 2;
            score -= 0.5 * Math.Abs(Math.Log2(Math.Max(i.Width, i.Height) / (double)target));
            if (i.Colors is >= 4 and <= 32) score += 0.3;
            if (!i.BuiltIn) score += 0.2;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }
}
