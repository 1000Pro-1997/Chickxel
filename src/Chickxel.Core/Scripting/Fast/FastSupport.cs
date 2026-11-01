using System.Diagnostics;
using System.Numerics;
using Acornima.Ast;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting.Fast;

/// <summary>컴파일할 수 없는 원문(허용한 부분집합 밖). 이 원문의 render는 통째로 Jint로 돈다.</summary>
internal sealed class FastUnsupportedException(string reason, Node? node) : Exception(reason)
{
    /// <summary>처음 걸린 자리의 줄(1부터, 모르면 0).</summary>
    public int Line { get; } = node?.Location.Start.Line ?? 0;

    /// <summary>처음 걸린 자리의 칸(1부터, 모르면 0).</summary>
    public int Column { get; } = node is null ? 0 : node.Location.Start.Column + 1;
}

/// <summary>실행 중 JS와 결과가 갈릴 수 있는 자리에 닿음: 이 호출은 그림을 건드리지 않은 채 Jint로 다시 돈다.</summary>
internal sealed class FastDeoptException : Exception;

/// <summary>
/// 문장 계수 자리 하나. 줄·칸은 Jint가 그 자리에서 문장 한도에 걸릴 때 알려 주는 위치와 같다
/// (Jint 호출 스택 첫 줄 = 마지막 구문 요소의 끝 줄, 시작 칸 + 1). <see cref="Deopt"/>면 Jint의 위치를 미리 알 수 없는 자리
/// (식 몸통 화살표 함수: Jint가 위치를 바꾸지 않고 센다)라서 한도에 걸리면 그 호출을 Jint로 다시 돌린다.
/// </summary>
internal readonly record struct FastSite(int Line, int Column, bool Deopt);

/// <summary>
/// 호출 한 번의 실행 한도. 컴파일한 코드는 문장마다 지역 계수기(<c>ticks</c>)를 1 줄이고, 다 쓰면 <see cref="Refill"/>로 다음 몫을 받는다.
/// 몫을 받을 때마다 취소·시간을 보고, 문장 수는 몫을 합쳐 정확히 <see cref="ScriptLimits.MaxStatements"/>까지만 준다.
/// 그래서 문장 한도는 Jint(<c>++count &gt; MaxStatements</c>이면 그 문장 앞에서 멈춤)와 같은 문장에서 걸린다.
/// </summary>
internal sealed class FastBudget
{
    /// <summary>한 번에 주는 문장 수: 시간·취소를 이만큼마다 본다(Jint는 시간을 64문장, 취소를 문장마다 본다).</summary>
    private const long Slice = 4096;

    private readonly ScriptLimits _limits;
    private readonly FastSite[] _sites;
    private readonly CancellationToken _cancel;
    private readonly long _deadline;
    private long _left;
    private long _granted;

    public FastBudget(ScriptLimits limits, FastSite[] sites, CancellationToken cancel, long dataLength = 0)
    {
        _limits = limits;
        _sites = sites;
        _cancel = cancel;
        _deadline = Stopwatch.GetTimestamp() + (long)(limits.Timeout.TotalSeconds * Stopwatch.Frequency);
        _left = limits.MaxStatements > 0 ? limits.MaxStatements : long.MaxValue;
        // 숫자 배열은 img.data만큼까지(큰 그림의 사본) — Jint의 배열 한도보다 큰 그림은 빠른 경로만 그린다
        MaxArrayLength = Math.Max(limits.MaxArrayLength, dataLength);
    }

    /// <summary>실행을 마친 호출이 센 문장 수(<see cref="Finish"/> 뒤).</summary>
    public long Statements { get; private set; }

    /// <summary>계수 자리 <paramref name="site"/>에서 몫이 떨어졌다: 그 문장 하나를 센 뒤의 남은 몫을 돌려준다.</summary>
    public long Refill(int site)
    {
        _cancel.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() > _deadline)
        {
            var at = _sites[site];
            throw ScriptSandbox.TimeoutError(_limits, at.Line, at.Column);
        }
        if (_left == 0)
        {
            var at = _sites[site];
            if (at.Deopt) throw new FastDeoptException();
            throw ScriptSandbox.StatementLimitError(_limits, at.Line, at.Column);
        }
        long n = Math.Min(Slice, _left);
        _left -= n;
        _granted += n;
        return n - 1;
    }

    /// <summary>끝까지 실행한 호출의 남은 몫으로 센 문장 수를 정한다.</summary>
    public void Finish(long ticks) => Statements = _granted - ticks;

    private long _allocated;

    /// <summary>
    /// 빠른 경로가 만든 배열(<c>new Float64Array(n)</c> 등)의 바이트를 센다. 합이 메모리 한도를 넘으면 그 호출은 Jint로 다시 돈다
    /// (Jint가 한도 오류를 낼지 정한다).
    /// </summary>
    public void Allocate(long bytes)
    {
        _allocated += bytes;
        if (_allocated > _limits.MemoryBytes) throw new FastDeoptException();
    }

    /// <summary>숫자 배열 한 개의 길이 한도: <see cref="ScriptLimits.MaxArrayLength"/>와 이 그림 <c>img.data</c> 길이 중 큰 것.</summary>
    public long MaxArrayLength { get; }
}

/// <summary>빠른 경로의 다른 그림(<c>img.blank</c>, <c>ctx.renderedFrameSource</c>의 image): RGBA 숫자 배열과 크기.</summary>
internal sealed class FastImage(double[] data, int width, int height)
{
    public readonly double[] Data = data;
    public readonly int Width = width, Height = height;
}

/// <summary>빠른 경로의 <c>ctx.renderedFrameSource</c> 결과(없으면 null).</summary>
internal sealed class FastRendered(FastImage image, int offsetX, int offsetY)
{
    public readonly FastImage Image = image;
    public readonly int OffsetX = offsetX, OffsetY = offsetY;
}

/// <summary>
/// 컴파일한 코드가 부르는 JS 숫자 규칙. 결과가 Jint 4.17과 비트까지 같도록 Jint의 구현(<c>MathInstance</c>, <c>TypeConverter</c>,
/// <c>JintBinaryExpression</c>)을 그대로 옮겼다. 규격보다 Jint가 기준이다: Jint 판을 올리면 비교 시험이 차이를 잡는다.
/// fdlibm으로 계산하는 함수(cbrt, expm1, log1p, acosh, asinh, atanh)는 옮기지 않았다(부분집합 밖).
/// </summary>
internal static class JsOps
{
    // ───── 형 변환 (TypeConverter) ─────

    public static int ToInt32(double value)
        => value is >= int.MinValue and <= int.MaxValue ? (int)value : DoubleToInt32Slow(value);

    public static uint ToUint32(double value)
        => value is >= 0.0 and <= uint.MaxValue ? (uint)value : (uint)DoubleToInt32Slow(value);

    private static int DoubleToInt32Slow(double o)
    {
        var bits = BitConverter.DoubleToInt64Bits(o);
        var sign = (int)(bits >> 63);
        var exponent = (int)((bits >> 52) & 0x7FF) - 1023;
        if ((uint)exponent >= 84) return 0;
        var mantissa = (bits & 0xFFFFFFFFFFFFFL) | 0x10000000000000L;
        var int32Value = exponent >= 52 ? (int)(mantissa << (exponent - 52)) : (int)(mantissa >> (52 - exponent));
        return (int32Value + sign) ^ sign;
    }

    private static bool IsIntegral(double value) => double.IsFinite(value) && Math.Floor(value) == value;

    private static bool IsNegativeZero(double x) => x == 0 && double.IsNegative(x);

    private static bool IsPositiveZero(double x) => x == 0 && !double.IsNegative(x);

    // ───── 연산자 ─────

    /// <summary><c>%</c>: Jint의 정수 지름길과 같은 값(나머지 부호 = 나뉘는 수 부호).</summary>
    public static double Remainder(double left, double right)
    {
        var leftInteger = (int)left;
        var rightInteger = (int)right;
        if (leftInteger == left && rightInteger == right && rightInteger != 0)
        {
            var modulo = (long)leftInteger % rightInteger;
            return modulo == 0 && BitConverter.DoubleToInt64Bits(left) < 0 ? -0.0 : modulo;
        }
        return left % right;
    }

    /// <summary><c>**</c> (Jint <c>Exponentiate</c>, <c>Math.pow</c>와 따로 구현되어 있어 따로 옮김).</summary>
    public static double Exponentiate(double baseValue, double exponent)
    {
        if (double.IsNaN(exponent)) return double.NaN;
        if (exponent == 0) return 1;
        if (double.IsNaN(baseValue)) return double.NaN;
        static bool IsOddIntegral(double value) => IsIntegral(value) && value % 2 != 0;
        if (double.IsPositiveInfinity(baseValue)) return exponent > 0 ? double.PositiveInfinity : 0.0;
        if (double.IsNegativeInfinity(baseValue))
        {
            if (exponent > 0) return IsOddIntegral(exponent) ? double.NegativeInfinity : double.PositiveInfinity;
            return IsOddIntegral(exponent) ? -0.0 : 0.0;
        }
        if (IsPositiveZero(baseValue)) return exponent > 0 ? 0.0 : double.PositiveInfinity;
        if (IsNegativeZero(baseValue))
        {
            if (exponent > 0) return IsOddIntegral(exponent) ? -0.0 : 0.0;
            return IsOddIntegral(exponent) ? double.NegativeInfinity : double.PositiveInfinity;
        }
        if (double.IsPositiveInfinity(exponent))
        {
            var absBase = Math.Abs(baseValue);
            return absBase > 1 ? double.PositiveInfinity : absBase == 1 ? double.NaN : 0.0;
        }
        if (double.IsNegativeInfinity(exponent))
        {
            var absBase = Math.Abs(baseValue);
            return absBase > 1 ? 0.0 : absBase == 1 ? double.NaN : double.PositiveInfinity;
        }
        if (baseValue < 0 && !IsIntegral(exponent)) return double.NaN;
        return Math.Pow(baseValue, exponent);
    }

    // ───── Math (MathInstance) ─────

    public static double Abs(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (IsNegativeZero(x)) return 0.0;
        if (double.IsInfinity(x)) return double.PositiveInfinity;
        return Math.Abs(x);
    }

    public static double Acos(double x)
    {
        if (double.IsNaN(x) || x > 1 || x < -1) return double.NaN;
        if (x == 1) return 0;
        return Math.Acos(x);
    }

    public static double Asin(double x)
    {
        if (double.IsNaN(x) || x > 1 || x < -1) return double.NaN;
        if (x == 0) return x;
        return Math.Asin(x);
    }

    public static double Atan(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0) return x;
        if (double.IsPositiveInfinity(x)) return Math.PI / 2;
        if (double.IsNegativeInfinity(x)) return -Math.PI / 2;
        return Math.Atan(x);
    }

    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (y > 0 && x.Equals(0)) return Math.PI / 2;
        if (IsPositiveZero(y))
        {
            if (x > 0) return 0.0;
            if (IsPositiveZero(x)) return 0.0;
            if (IsNegativeZero(x)) return Math.PI;
            if (x < 0) return Math.PI;
        }
        if (IsNegativeZero(y))
        {
            if (x > 0) return -0.0;
            if (IsPositiveZero(x)) return -0.0;
            if (IsNegativeZero(x)) return -Math.PI;
            if (x < 0) return -Math.PI;
        }
        if (y < 0 && x.Equals(0)) return -Math.PI / 2;
        if (y > 0 && !double.IsInfinity(y))
        {
            if (double.IsPositiveInfinity(x)) return 0.0;
            if (double.IsNegativeInfinity(x)) return Math.PI;
        }
        if (y < 0 && !double.IsInfinity(y))
        {
            if (double.IsPositiveInfinity(x)) return -0.0;
            if (double.IsNegativeInfinity(x)) return -Math.PI;
        }
        if (double.IsPositiveInfinity(y) && !double.IsInfinity(x)) return Math.PI / 2;
        if (double.IsNegativeInfinity(y) && !double.IsInfinity(x)) return -Math.PI / 2;
        if (double.IsPositiveInfinity(y) && double.IsPositiveInfinity(x)) return Math.PI / 4;
        if (double.IsPositiveInfinity(y) && double.IsNegativeInfinity(x)) return 3 * Math.PI / 4;
        if (double.IsNegativeInfinity(y) && double.IsPositiveInfinity(x)) return -Math.PI / 4;
        if (double.IsNegativeInfinity(y) && double.IsNegativeInfinity(x)) return -3 * Math.PI / 4;
        return Math.Atan2(y, x);
    }

    public static double Ceil(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0 || double.IsInfinity(x)) return x;
        return Math.Ceiling(x);
    }

    public static double Cos(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0) return 1;
        if (double.IsInfinity(x)) return double.NaN;
        return Math.Cos(x);
    }

    public static double Cosh(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0) return 1;
        if (double.IsInfinity(x)) return double.PositiveInfinity;
        return Math.Cosh(x);
    }

    public static double Exp(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0) return 1;
        if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
        if (double.IsNegativeInfinity(x)) return 0.0;
        return Math.Exp(x);
    }

    public static double Floor(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0 || double.IsInfinity(x)) return x;
        return Math.Floor(x);
    }

    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0) return double.NaN;
        if (x == 0) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
        if (x == 1) return 0.0;
        return Math.Log(x);
    }

    public static double Log2(double x)
    {
        if (double.IsNaN(x) || x < 0) return double.NaN;
        if (x == 0) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
        if (x == 1) return 0.0;
        return Math.Log(x, 2);
    }

    public static double Log10(double x)
    {
        if (double.IsNaN(x) || x < 0) return double.NaN;
        if (x == 0) return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
        if (x == 1) return 0.0;
        return Math.Log10(x);
    }

    /// <summary><c>Math.max</c> 한 걸음: 처음 값은 -Infinity, NaN이 나오면 끝까지 NaN, +0이 -0보다 크다.</summary>
    public static double MaxStep(double highest, double number)
    {
        if (double.IsNaN(highest) || double.IsNaN(number)) return double.NaN;
        if (IsPositiveZero(number) && IsNegativeZero(highest)) highest = 0;
        return number > highest ? number : highest;
    }

    /// <summary><c>Math.min</c> 한 걸음: 처음 값은 +Infinity, NaN이 나오면 끝까지 NaN, -0이 +0보다 작다.</summary>
    public static double MinStep(double lowest, double number)
    {
        if (double.IsNaN(lowest) || double.IsNaN(number)) return double.NaN;
        if (IsNegativeZero(number) && IsPositiveZero(lowest)) lowest = -0.0;
        return number < lowest ? number : lowest;
    }

    public static double Pow(double x, double y)
    {
        if (x > 1 && y > 1 && x < int.MaxValue && y < int.MaxValue) return Math.Pow(x, y);
        if (y == 0) return 1;
        if (double.IsNaN(y) || double.IsNaN(x)) return double.NaN;
        var absX = Math.Abs(x);
        if (absX > 1)
        {
            if (double.IsPositiveInfinity(y)) return double.PositiveInfinity;
            if (double.IsNegativeInfinity(y)) return 0.0;
        }
        if (absX == 1 && double.IsInfinity(y)) return double.NaN;
        if (absX < 1)
        {
            if (double.IsPositiveInfinity(y)) return 0;
            if (double.IsNegativeInfinity(y)) return double.PositiveInfinity;
        }
        if (double.IsPositiveInfinity(x))
        {
            if (y > 0) return double.PositiveInfinity;
            if (y < 0) return 0.0;
        }
        if (double.IsNegativeInfinity(x))
        {
            if (y > 0) return Math.Abs(y % 2).Equals(1) ? double.NegativeInfinity : double.PositiveInfinity;
            if (y < 0) return Math.Abs(y % 2).Equals(1) ? -0.0 : 0.0;
        }
        if (IsPositiveZero(x))
        {
            if (y > 0) return 0;
            if (y < 0) return double.PositiveInfinity;
        }
        if (IsNegativeZero(x))
        {
            if (y > 0) return Math.Abs(y % 2).Equals(1) ? -0.0 : 0.0;
            if (y < 0) return Math.Abs(y % 2).Equals(1) ? double.NegativeInfinity : double.PositiveInfinity;
        }
        if (x < 0 && !double.IsInfinity(x) && !double.IsInfinity(y) && !y.Equals((int)y)) return double.NaN;
        return Math.Pow(x, y);
    }

    /// <summary><c>Math.round</c>: 짝수 쪽 반올림 뒤 정확히 .5였으면 위로(Jint 구현 그대로).</summary>
    public static double Round(double x)
    {
        var round = Math.Round(x);
        return round.Equals(x - 0.5) ? round + 1 : round;
    }

    public static double Fround(double x) => (float)x;

    public static double Sin(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0) return x;
        if (double.IsInfinity(x)) return double.NaN;
        return Math.Sin(x);
    }

    public static double Sinh(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0 || double.IsInfinity(x)) return x;
        return Math.Sinh(x);
    }

    public static double Sqrt(double x) => Math.Sqrt(x);

    public static double Tan(double x) => Math.Tan(x);

    public static double Tanh(double x) => Math.Tanh(x);

    public static double Trunc(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0 || double.IsInfinity(x)) return x;
        return Math.Truncate(x);
    }

    public static double Sign(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x == 0) return x;
        if (double.IsPositiveInfinity(x)) return 1;
        if (double.IsNegativeInfinity(x)) return -1;
        return Math.Sign(x);
    }

    public static double Hypot0() => 0.0;

    public static double Hypot1(double a) => Hypot([a]);

    public static double Hypot2(double a, double b) => Hypot([a, b]);

    public static double Hypot3(double a, double b, double c) => Hypot([a, b, c]);

    private static double Hypot(ReadOnlySpan<double> values)
    {
        foreach (var value in values)
            if (double.IsInfinity(value)) return double.PositiveInfinity;
        var onlyZero = true;
        double y = 0;
        foreach (var number in values)
        {
            if (double.IsNaN(number)) return double.NaN;
            if (onlyZero && number != 0) onlyZero = false;
            y += number * number;
        }
        return onlyZero ? 0.0 : Math.Sqrt(y);
    }

    public static double Imul(double x, double y) => unchecked(ToInt32(x) * ToInt32(y));

    public static double Clz32(double x) => BitOperations.LeadingZeroCount((uint)ToInt32(x));

    // ───── host API (ScriptHostApi와 같은 계산) ─────

    /// <summary><c>hsvToRgb(h, s, v[, a])</c>: 인자가 넷 이상이면 넷째를 알파로(<see cref="ScriptHostApi.ToByte"/>).</summary>
    public static Color32 HsvToRgb(double h, double s, double v, bool hasAlpha, double a)
        => Color32.FromHsv((float)h, (float)s, (float)v, hasAlpha ? ScriptHostApi.ToByte(a) : (byte)255);

    /// <summary><c>rgbToHsv(r, g, b)</c>.</summary>
    public static (float H, float S, float V) RgbToHsv(double r, double g, double b)
        => new Color32(ScriptHostApi.ToByte(r), ScriptHostApi.ToByte(g), ScriptHostApi.ToByte(b)).ToHsv();

    /// <summary><c>ctx.hash(x, y, seed[, salt])</c>: 넷째가 없으면 NaN → 0(Jint의 undefined와 같음).</summary>
    public static double Hash(double x, double y, double seed, double salt) => LayerEffects.Random(ToInt32(seed), ToInt32(x), ToInt32(y), ToInt32(salt));

    /// <summary><c>ctx.random(seed)</c>: 프레임 번호와 시드로 정한 값.</summary>
    public static double FrameRandom(double seed, double frame) => LayerEffects.Random(ToInt32(seed), (int)frame, 0, 1);

    /// <summary><c>ctx.bayer2(x, y)</c>: 0~3.</summary>
    public static double Bayer2(double x, double y) => Bayer.Threshold2(ToInt32(x), ToInt32(y));

    /// <summary><c>ctx.bayer4(x, y)</c>: 0~15.</summary>
    public static double Bayer4(double x, double y) => Bayer.Threshold4(ToInt32(x), ToInt32(y));

    /// <summary>
    /// <c>ctx.color</c>·<c>ctx.colorAt</c>·<c>ctx.pairColor</c>의 색(기본 범위). 목록 번호(<paramref name="item"/>, 색은 -1)가 정수 항목 번호가 아니면
    /// Jint로 다시 그린다(Jint가 오류를 낸다).
    /// </summary>
    public static Color32 SampleColor(ScriptColors colors, int setting, double item, bool to, double x, double y)
        => colors.Sample(setting, Item(colors, setting, item), to, ToInt32(x), ToInt32(y));

    /// <summary><see cref="SampleColor"/>을 범위 (bx, by, bw, bh)로.</summary>
    public static Color32 SampleColorIn(ScriptColors colors, int setting, double item, bool to, double x, double y, double bx, double by, double bw, double bh)
        => colors.Sample(setting, Item(colors, setting, item), to, ToInt32(x), ToInt32(y), new System.Drawing.Rectangle(ToInt32(bx), ToInt32(by), ToInt32(bw), ToInt32(bh)));

    /// <summary><c>ctx.hasGradient("슬롯")</c>(슬롯은 컴파일할 때 정함 — 지금 목록에 없는 항목이면 false).</summary>
    public static bool HasGradient(ScriptColors colors, int setting, int item, bool to) => colors.Has(setting, item) && colors.HasGradient(setting, item, to);

    /// <summary><c>rgbToOklab(r, g, b)</c>: 채널은 <see cref="ScriptHostApi.ToByte"/>로, 결과는 <see cref="Pixelate.OkLab.FromRgb"/>의 float를 double로.</summary>
    public static (double L, double A, double B) RgbToOklab(double r, double g, double b)
    {
        var lab = Pixelate.OkLab.FromRgb(ScriptHostApi.ToByte(r), ScriptHostApi.ToByte(g), ScriptHostApi.ToByte(b));
        return (lab.L, lab.A, lab.B);
    }

    /// <summary><c>oklabToRgb(l, a, b)</c>: float로 바꿔 <see cref="Pixelate.OkLab.ToGamutColor"/>(밝기·색상 유지, sRGB 밖이면 채도만 줄임), 알파 255.</summary>
    public static Color32 OklabToRgb(double l, double a, double b) => Pixelate.OkLab.ToGamutColor(new Pixelate.Lab((float)l, (float)a, (float)b));

    // ───── 숫자 배열(typed array·img.data 사본) ─────

    /// <summary>배열 종류: 0 = JS 배열 사본(<c>img.data.slice()</c>, 바꾸지 않음), 1 = Float64Array, 2 = Int32Array, 3 = Uint8Array.</summary>
    public const int ArrayPlain = 0, ArrayFloat64 = 1, ArrayInt32 = 2, ArrayUint8 = 3;

    private static int BytesPer(int kind) => kind switch { ArrayInt32 => 4, ArrayUint8 => 1, _ => 8 };

    /// <summary>
    /// <c>new Float64Array(n)</c> 등: 칸 값은 그 종류로 바꾼 수(Int32는 ToInt32, Uint8은 256으로 나눈 나머지)를 double로 담는다.
    /// n이 0~최대 길이 정수가 아니면 Jint로(샌드박스가 TypeError를 낸다).
    /// </summary>
    public static double[] NewArray(FastBudget budget, double length, int kind)
    {
        if (!(length >= 0 && length <= budget.MaxArrayLength && length == Math.Floor(length))) throw new FastDeoptException();
        budget.Allocate((long)length * BytesPer(kind));
        return new double[(int)length];
    }

    /// <summary><c>new Float64Array(배열)</c>·<c>img.data.slice()</c>: 칸마다 그 종류로 바꾼 사본.</summary>
    public static double[] CopyArray(FastBudget budget, double[] source, int kind)
    {
        budget.Allocate((long)source.Length * BytesPer(kind));
        var copy = (double[])source.Clone();
        if (kind is ArrayInt32 or ArrayUint8)
            for (int i = 0; i < copy.Length; i++) copy[i] = Store(copy[i], kind);
        return copy;
    }

    /// <summary>typed array에 넣을 때의 값(Int32 = ToInt32, Uint8 = ToUint32의 아래 8비트, 그 밖은 그대로).</summary>
    public static double Store(double value, int kind) => kind switch
    {
        ArrayInt32 => ToInt32(value),
        ArrayUint8 => ToUint32(value) & 255,
        _ => value,
    };

    /// <summary><c>a.fill(v)</c>(인자 하나): 모든 칸을 그 종류로 바꾼 v로.</summary>
    public static void Fill(double[] array, double value, int kind) => Array.Fill(array, Store(value, kind));

    // ───── 다른 그림(img.blank, img.blendOver, ctx.renderedFrameSource) ─────

    /// <summary><c>img.blank(w, h)</c>: 0으로 채운 그림. 크기가 1~최대 정수가 아니거나 배열·메모리 한도를 넘으면 Jint로(Jint가 TypeError를 낸다).</summary>
    public static FastImage Blank(FastBudget budget, double width, double height)
    {
        int w = ToInt32(width), h = ToInt32(height);
        if (w < 1 || h < 1 || w > Model.PixelImage.MaxSize || h > Model.PixelImage.MaxSize || (long)w * h * 4 > budget.MaxArrayLength)
            throw new FastDeoptException();
        budget.Allocate((long)w * h * 4 * 8);
        return new FastImage(new double[w * h * 4], w, h);
    }

    /// <summary>
    /// <c>dst.blendOver(src, x, y)</c>: src의 알파가 0이 아닌 칸을 dst의 (x, y)에 <see cref="Color32.BlendOver"/>로(Jint 호스트 함수와 같은 계산 —
    /// 칸 값은 <see cref="ScriptHostApi.ToByte"/>로 읽는다). 칸 수가 크기와 다르면 Jint로.
    /// </summary>
    public static void BlendOver(double[] dst, double dstWidth, double dstHeight, double[] src, double srcWidth, double srcHeight, double x, double y)
    {
        int dw = (int)dstWidth, dh = (int)dstHeight, sw = (int)srcWidth, sh = (int)srcHeight, dx = ToInt32(x), dy = ToInt32(y);
        if (dst.Length != (long)dw * dh * 4 || src.Length != (long)sw * sh * 4) throw new FastDeoptException();
        int x0 = Math.Max(0, -dx), y0 = Math.Max(0, -dy);
        int x1 = (int)Math.Min(sw, (long)dw - dx), y1 = (int)Math.Min(sh, (long)dh - dy);
        for (int sy = y0; sy < y1; sy++)
            for (int sx = x0; sx < x1; sx++)
            {
                int s = (sy * sw + sx) * 4;
                var top = new Color32(ScriptHostApi.ToByte(src[s]), ScriptHostApi.ToByte(src[s + 1]), ScriptHostApi.ToByte(src[s + 2]), ScriptHostApi.ToByte(src[s + 3]));
                if (top.A == 0) continue;
                int d = ((sy + dy) * dw + sx + dx) * 4;
                var under = new Color32(ScriptHostApi.ToByte(dst[d]), ScriptHostApi.ToByte(dst[d + 1]), ScriptHostApi.ToByte(dst[d + 2]), ScriptHostApi.ToByte(dst[d + 3]));
                var blended = Color32.BlendOver(under, top);
                dst[d] = blended.R;
                dst[d + 1] = blended.G;
                dst[d + 2] = blended.B;
                dst[d + 3] = blended.A;
            }
    }

    /// <summary>
    /// <c>ctx.renderedFrameSource(i)</c>: 미루는 스크립트만, 그 프레임 원본이 있고 크기가 원본 자리와 같을 때 다른 효과를 입힌 그림(새 사본)과 위치. 아니면 null
    /// (<see cref="ScriptHostApi.CreateContext"/>와 같은 규칙).
    /// </summary>
    public static FastRendered? RenderedFrame(FastBudget budget, ScriptColors host, double index)
    {
        var frame = host.Frame;
        if (!host.Deferred || frame.FrameSource is not { } frameSource) return null;
        int i = ToInt32(index);
        if (frameSource(i) is not { } raw || raw.Width != host.Source.Width || raw.Height != host.Source.Height) return null;
        var (picture, offsetX, offsetY) = frame.RenderedFrameSource?.Invoke(i, raw) ?? (raw, 0, 0);
        var px = picture.Pixels;
        budget.Allocate((long)px.Length * 4 * 8);
        var data = new double[px.Length * 4];
        for (int p = 0, j = 0; p < px.Length; p++, j += 4)
        {
            var c = px[p];
            (data[j], data[j + 1], data[j + 2], data[j + 3]) = (c.R, c.G, c.B, c.A);
        }
        return new FastRendered(new FastImage(data, picture.Width, picture.Height), offsetX, offsetY);
    }

    /// <summary>null인 <c>ctx.renderedFrameSource</c> 결과의 속성을 읽으면 Jint로(Jint가 TypeError를 낸다).</summary>
    public static FastRendered Present(FastRendered? rendered) => rendered ?? throw new FastDeoptException();

    /// <summary><c>ctx.hasGradient("목록 키", 번호[, "from"|"to"])</c>: 번호가 정수 항목 번호가 아니면 false.</summary>
    public static bool HasGradientAt(ScriptColors colors, int setting, double item, bool to)
    {
        int k = item == Math.Floor(item) && item is >= 0 and <= int.MaxValue ? (int)item : -1;
        return colors.Has(setting, k) && colors.HasGradient(setting, k, to);
    }

    private static int Item(ScriptColors colors, int setting, double item)
    {
        int k = item == Math.Floor(item) && item is >= int.MinValue and <= int.MaxValue ? (int)item : -2;
        return colors.Has(setting, k) ? k : throw new FastDeoptException();
    }
}
