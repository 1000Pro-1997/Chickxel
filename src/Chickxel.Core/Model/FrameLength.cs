namespace Chickxel.Core.Model;

/// <summary>프레임 길이 창에서 길이를 정하는 방법.</summary>
public enum FrameLengthMode
{
    /// <summary>프레임마다 같은 길이(ms).</summary>
    Milliseconds,
    /// <summary>프레임마다 같은 길이를 초당 프레임 수로(길이 = 1000 ÷ FPS).</summary>
    Fps,
    /// <summary>프레임마다 같은 길이(초).</summary>
    Seconds,
    /// <summary>프레임마다 같은 길이를 60fps 게임 프레임 수로(길이 = 수 × 1000 ÷ 60).</summary>
    GameFrames,
    /// <summary>지금 길이마다 ÷ 배속(프레임끼리의 길이 차이는 그대로).</summary>
    Speed,
    /// <summary>고른 프레임 길이의 합을 이 값(ms)으로 — 지금 길이 비율대로 나눔.</summary>
    Total,
}

/// <summary>프레임 길이 계산(프레임 길이 창): 방법과 값으로 고른 프레임들의 새 길이를 낸다.</summary>
public static class FrameLength
{
    public const int MinMs = 1;
    public const int MaxMs = 60000;
    /// <summary>게임 프레임 수의 기준 FPS.</summary>
    public const int GameFps = 60;

    /// <summary>
    /// 지금 길이 <paramref name="current"/>(고른 프레임 차례)에 방법 <paramref name="mode"/>·값 <paramref name="value"/>를 적용한 새 길이(ms, 1~60000으로 자름).
    /// 값이 0 이하이면 지금 길이 그대로.
    /// </summary>
    public static int[] Apply(IReadOnlyList<int> current, FrameLengthMode mode, double value)
    {
        var result = new int[current.Count];
        if (current.Count == 0) return result;
        if (!(value > 0) || double.IsInfinity(value))
        {
            for (int i = 0; i < result.Length; i++) result[i] = Clamp(current[i]);
            return result;
        }
        switch (mode)
        {
            case FrameLengthMode.Speed:
                for (int i = 0; i < result.Length; i++) result[i] = Clamp(Math.Round(current[i] / value));
                return result;
            case FrameLengthMode.Total:
                return SplitTotal(current, value);
            default:
                Array.Fill(result, Clamp(Math.Round(mode switch
                {
                    FrameLengthMode.Fps => 1000.0 / value,
                    FrameLengthMode.Seconds => value * 1000.0,
                    FrameLengthMode.GameFrames => value * 1000.0 / GameFps,
                    _ => value,
                })));
                return result;
        }
    }

    /// <summary>
    /// 합 <paramref name="total"/>ms를 지금 길이 비율대로 나눈다(정수 ms). 내림한 뒤 남은 ms를 버린 소수가 큰 프레임부터 1ms씩 더해 합을 맞춘다
    /// (프레임마다 1~60000으로 자르면 합이 모자라거나 넘칠 수 있음).
    /// </summary>
    private static int[] SplitTotal(IReadOnlyList<int> current, double total)
    {
        int n = current.Count;
        long sum = 0;
        foreach (var ms in current) sum += Math.Max(MinMs, ms);
        long target = (long)Math.Round(Math.Clamp(total, (double)n * MinMs, (double)n * MaxMs));
        var result = new int[n];
        var rest = new (double Fraction, int Index)[n];
        long given = 0;
        for (int i = 0; i < n; i++)
        {
            double exact = (double)Math.Max(MinMs, current[i]) * target / sum;
            int whole = (int)Math.Floor(exact);
            result[i] = whole;
            given += whole;
            rest[i] = (exact - whole, i);
        }
        Array.Sort(rest, (a, b) => a.Fraction != b.Fraction ? b.Fraction.CompareTo(a.Fraction) : a.Index.CompareTo(b.Index));
        for (int k = 0; given < target && k < n; k++, given++) result[rest[k].Index]++;
        for (int i = 0; i < n; i++) result[i] = Clamp(result[i]);
        return result;
    }

    private static int Clamp(double ms) => (int)Math.Clamp(ms, MinMs, MaxMs);
}
