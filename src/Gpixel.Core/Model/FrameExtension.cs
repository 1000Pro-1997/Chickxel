namespace Gpixel.Core.Model;

/// <summary>프레임 늘리기(<see cref="SpriteProject.ExtendFrames(FrameExtension)"/>)에서 늘어난 자리를 채우는 방법.</summary>
public enum FrameExtendMode
{
    /// <summary>같은 비율로: 프레임마다 고르게 되풀이(n → 2n이면 프레임마다 두 번).</summary>
    Stretch,
    /// <summary>첫 프레임으로: 첫 프레임을 앞에 더 두고 원래 프레임들.</summary>
    HoldFirst,
    /// <summary>마지막 프레임으로: 원래 프레임들 뒤에 마지막 프레임을 더 둔다.</summary>
    HoldLast,
    /// <summary>반복: 1, 2, …, n, 1, 2, …처럼 차례를 되풀이.</summary>
    Loop,
}

/// <summary>
/// 프레임 늘리기 계획: 프레임 [<see cref="From"/>, <see cref="To"/>](n = 길이)를 <see cref="Count"/>개(≥ n)로 바꾼다.
/// 늘린 뒤 프레임 번호 ↔ 원래 프레임 번호와, 태그·효과 범위가 어떻게 바뀌는지를 계산한다(프로젝트는 고치지 않음 — 대화상자 미리보기에도 씀).
/// </summary>
public readonly record struct FrameExtension
{
    /// <summary>늘린 범위의 최대 프레임 수.</summary>
    public const int MaxCount = 256;

    public FrameExtension(int from, int to, int count, FrameExtendMode mode)
    {
        if (from < 0 || to < from) throw new ArgumentOutOfRangeException(nameof(from), $"프레임 범위가 잘못되었습니다: {from}~{to}");
        if (count < to - from + 1) throw new ArgumentOutOfRangeException(nameof(count), $"프레임 수({count})가 범위 길이({to - from + 1})보다 작습니다 — 줄이기는 할 수 없습니다.");
        if (count > Math.Max(MaxCount, to - from + 1)) throw new ArgumentOutOfRangeException(nameof(count), $"프레임 수는 {MaxCount}개까지입니다.");
        From = from;
        To = to;
        Count = count;
        Mode = mode;
    }

    public int From { get; }
    public int To { get; }
    /// <summary>늘린 뒤 범위의 프레임 수.</summary>
    public int Count { get; }
    public FrameExtendMode Mode { get; }

    /// <summary>원래 범위 길이 n.</summary>
    public int Length => To - From + 1;

    /// <summary>더해지는 프레임 수(Count − n).</summary>
    public int Added => Count - Length;

    /// <summary>
    /// 길이 <paramref name="length"/>인 범위를 늘릴 수 있는 프레임 수로 맞춘다: 범위 길이 이상, <see cref="MaxCount"/> 이하(범위가 이미 그보다 길면 그 길이).
    /// 다른 범위가 늘어나 보이는 프레임 수처럼 그 밖의 값을 받은 곳에서 생성자에 넘기기 전에 쓴다.
    /// </summary>
    public static int ClampCount(int length, int count) => Math.Clamp(count, length, Math.Max(length, MaxCount));

    /// <summary>늘린 범위의 <paramref name="k"/>번째(0 ~ Count−1) 자리가 원래 범위의 몇 번째(0 ~ n−1) 프레임인지.</summary>
    public int SourceOf(int k) => Mode switch
    {
        FrameExtendMode.Stretch => (int)((long)k * Length / Count),
        FrameExtendMode.HoldFirst => Math.Max(0, k - Added),
        FrameExtendMode.HoldLast => Math.Min(k, Length - 1),
        _ => k % Length,
    };

    /// <summary>원래 범위의 <paramref name="i"/>번째 프레임이 늘린 범위에서 차지하는 자리(처음 ~ 끝, 범위 기준). 반복은 되풀이한 뒷부분을 마지막 프레임 몫으로 친다.</summary>
    private (int Lo, int Hi) SpanOf(int i) => Mode switch
    {
        FrameExtendMode.Stretch => (CeilDiv((long)i * Count, Length), CeilDiv((long)(i + 1) * Count, Length) - 1),
        FrameExtendMode.HoldFirst => i == 0 ? (0, Added) : (i + Added, i + Added),
        _ => i == Length - 1 ? (i, Count - 1) : (i, i),
    };

    private static int CeilDiv(long a, int b) => (int)((a + b - 1) / b);

    /// <summary>늘린 뒤 프레임 번호 → 원래 프레임 번호.</summary>
    public int OriginalFrame(int frame)
        => frame < From ? frame : frame >= From + Count ? frame - Added : From + SourceOf(frame - From);

    /// <summary>원래 프레임 번호 → 늘린 뒤 그 프레임(원본이 남는 자리 = 처음 자리) 번호.</summary>
    public int ExtendedFrame(int original)
        => original < From ? original : original > To ? original + Added : From + SpanOf(original - From).Lo;

    /// <summary>늘린 뒤 프레임 <paramref name="frame"/>이 새로 더한 사본인지(원래 프레임이 남는 자리가 아닌지).</summary>
    public bool IsCopy(int frame)
    {
        int k = frame - From;
        return k >= 0 && k < Count && SpanOf(SourceOf(k)).Lo != k;
    }

    /// <summary>
    /// 태그·효과 범위를 늘린 뒤 번호로: 범위 뒤는 밀리고, 범위 안 끝은 그 프레임이 차지한 자리 끝까지(늘린 범위를 모두 덮으면 Count개로 늘어남).
    /// </summary>
    public FrameRange MapRange(FrameRange r)
    {
        int from = r.From < From ? r.From : r.From > To ? r.From + Added : From + SpanOf(r.From - From).Lo;
        int to = r.To < From ? r.To : r.To > To ? r.To + Added : From + SpanOf(r.To - From).Hi;
        return new FrameRange(from, to);
    }

    /// <summary>늘린 범위의 원래 프레임 차례(범위 기준 0부터) — 미리보기·알림용.</summary>
    public int[] Sequence() => Enumerable.Range(0, Count).Select(SourceOf).ToArray();

    /// <summary>방법 이름(화면 표시).</summary>
    public static string ModeName(FrameExtendMode mode) => mode switch
    {
        FrameExtendMode.Stretch => "같은 비율로",
        FrameExtendMode.HoldFirst => "첫 프레임으로",
        FrameExtendMode.HoldLast => "마지막 프레임으로",
        _ => "반복",
    };
}
