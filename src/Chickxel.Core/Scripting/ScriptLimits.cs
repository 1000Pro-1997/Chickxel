namespace Chickxel.Core.Scripting;

/// <summary>
/// 스크립트 효과 호출 한 번(맨 위 선언 실행, <c>padding</c>, <c>render</c> 각각)에 거는 한도.
/// 넘으면 <see cref="ScriptEffectException"/>으로 그 호출만 멈추고 프로세스는 그대로 산다.
/// 값이 같으면 작업 스레드의 Jint 실행기를 같이 쓴다(값 비교 record).
/// <para>
/// <c>render</c>는 그림 크기에 비례하는 한도를 받는다(<see cref="ForPixels"/>): 아래 값은 바탕이고, 여백을 넣은 그림 픽셀마다
/// <see cref="StatementsPerPixel"/>·<see cref="TimePerPixel"/>·<see cref="MemoryPerPixel"/>가 더해진다. 빠른 경로와 Jint가 같은 값을 읽으므로
/// 멈추는 문장은 두 길에서 같다. 근거는 <c>측정/2026-11-01-스크립트-한도-크기-비례.md</c>.
/// </para>
/// </summary>
public sealed record ScriptLimits
{
    /// <summary>앱에서 쓰는 한도. 값을 고른 근거는 각 속성 설명에 있다.</summary>
    public static ScriptLimits Default { get; } = new();

    /// <summary>
    /// 호출 한 번의 시간 한도(Jint <c>TimeoutInterval</c>, 정규식 한 번의 한도도 같음). 대표 효과의 목표가 256²에서 50ms이므로
    /// 40배 여유를 둔다. 기계마다 속도가 달라 멈추는 지점이 달라지므로 실제로는 <see cref="MaxStatements"/>가 먼저 걸리게 잡는다.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 호출 한 번에 실행할 수 있는 문장 수(Jint <c>MaxStatements</c>). 기계와 상관없이 같은 지점에서 멈추는 결정론적 한도다.
    /// 빛 지나가기 JS가 256²에서 약 53만 문장(약 100배 여유), 빈 반복이 이 한도에 닿는 데 약 0.8초라 시간 한도보다 먼저 걸린다.
    /// </summary>
    public long MaxStatements { get; init; } = 50_000_000;

    /// <summary>
    /// 호출 한 번에 새로 할당할 수 있는 바이트(Jint <c>LimitMemory</c>). Jint는 살아 있는 메모리가 아니라 그 스레드가 호출 동안
    /// 할당한 양을 세므로 버린 객체도 포함된다. 문자열을 두 배씩 늘리면 한도를 넘긴 뒤 한 번 더 할당하고 멈추므로(부록 B: 256MB 한도에 402MB)
    /// 실제 예산 512MB의 절반으로 잡는다. 픽셀마다 계산하는 스크립트는 256²에서 약 20~35MB를 할당한다(빛 지나가기 24MB).
    /// </summary>
    public long MemoryBytes { get; init; } = 256L << 20;

    /// <summary>JS 함수 호출 깊이(Jint <c>LimitRecursion</c>). .NET 스택이 넘치기 전에 멈추도록 낮게 둔다.</summary>
    public int MaxRecursion { get; init; } = 256;

    /// <summary>
    /// 배열 한 개의 최대 칸 수(Jint <c>MaxArraySize</c>). <c>img.data</c>도 배열이라 그림 크기 상한이 된다: 2048×2048 RGBA.
    /// </summary>
    public uint MaxArrayLength { get; init; } = 1u << 24;

    /// <summary>
    /// render 한도에 그림 픽셀마다 더하는 문장 수. 공식 효과 23개의 기본 설정에서 가장 많이 센 것이 픽셀당 약 47문장(터지기, 512²)이라
    /// 무거운 설정까지 10배 여유를 둔다. 2048²에서 int 최대(약 21억)에 닿는다.
    /// </summary>
    public int StatementsPerPixel { get; init; } = 500;

    /// <summary>
    /// render 한도에 그림 픽셀마다 더하는 시간(안전망). Jint에서 가장 느린 공식 효과가 픽셀당 약 28µs(터지기, 512²)라 3배 넘게 둔다 —
    /// 기계마다 다르므로 결정론적인 문장 한도가 먼저 걸리게 잡는다.
    /// </summary>
    public TimeSpan TimePerPixel { get; init; } = TimeSpan.FromTicks(1000); // 100µs

    /// <summary>
    /// render 한도에 그림 픽셀마다 더하는 할당 바이트(Jint는 호출 동안 할당한 양을 셈 — 버린 객체 포함). 공식 효과의 기본 설정에서
    /// 가장 많이 할당한 것이 픽셀당 약 2.3KB(터지기, 512²)라 약 7배 둔다.
    /// </summary>
    public long MemoryPerPixel { get; init; } = 16L << 10;

    /// <summary>
    /// 여백을 넣은 그림 <paramref name="pixels"/>개에 render를 돌릴 때의 한도: 문장·시간·메모리에 픽셀 비례 몫을 더한다(모두 long — 16384²에서도 넘치지 않음).
    /// 같은 크기면 늘 같은 값이라 결정론적이다. Jint는 문장 한도를 int로만 받으므로 int 최대에서 멈추지만 Jint가 받는 그림(<see cref="MaxArrayLength"/>)
    /// 안에서는 닿지 않는다. 빠른 경로는 long 그대로 센다.
    /// </summary>
    public ScriptLimits ForPixels(long pixels) => pixels <= 0 ? this : this with
    {
        MaxStatements = MaxStatements + Math.Min(long.MaxValue / 4, (long)StatementsPerPixel * pixels),
        Timeout = Timeout + TimeSpan.FromTicks(Math.Min(TimeSpan.MaxValue.Ticks / 4, TimePerPixel.Ticks * pixels)),
        MemoryBytes = MemoryBytes + Math.Min(long.MaxValue / 4, MemoryPerPixel * pixels),
    };
}
