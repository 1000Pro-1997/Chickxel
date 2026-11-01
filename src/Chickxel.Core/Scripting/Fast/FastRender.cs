using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting.Fast;

/// <summary>
/// 컴파일한 render(<see cref="FastCompiler"/>)를 <see cref="ScriptEffectProgram.Render"/>와 같은 계약으로 부른다. 불변이라 여러 스레드에서 같이 쓴다.
/// <c>img.data</c>는 길이가 정확히 너비 × 높이 × 4인 <c>double[]</c>이고, 끝나면 <c>Uint8ClampedArray</c> 규칙(<see cref="ScriptHostApi.ToByte"/>)으로 되쓴다.
/// </summary>
internal sealed class FastRender
{
    // 스레드마다 다시 쓰는 data 배열의 최대 길이(512² 그림 = 8MB). 더 큰 그림은 호출마다 만든다(작업 스레드마다 큰 배열을 잡아 두지 않게)
    private const int MaxReusedLength = 1 << 20;

    [ThreadStatic] private static double[]? t_buffer;

    private readonly FastRenderFn _render;
    private readonly FastSite[] _sites;
    private readonly IReadOnlyList<ScriptSetting> _settings;
    private readonly ScriptLimits _limits;
    private readonly int _slots;
    private readonly bool _deferred;

    public FastRender(FastRenderFn render, FastSite[] sites, IReadOnlyList<ScriptSetting> settings, ScriptLimits limits, bool deferred = false)
    {
        _render = render;
        _sites = sites;
        _settings = settings;
        _limits = limits;
        _deferred = deferred;
        _slots = FastCompiler.SlotCount(settings);
    }

    /// <summary>같은 컴파일 결과를 다른 한도로 돌리는 사본(그림 크기 비례 한도, 시험에서 한도를 바꿔 가며 멈추는 자리를 비교할 때).</summary>
    public FastRender WithLimits(ScriptLimits limits) => new(_render, _sites, _settings, limits, _deferred);

    /// <summary>
    /// <paramref name="image"/>를 제자리에서 바꾼다. JS와 결과가 갈릴 자리에 닿으면 그림을 건드리지 않고 false(그 호출은 Jint로 다시 그린다).
    /// 한도 초과는 <see cref="ScriptEffectException"/>, 취소는 <see cref="OperationCanceledException"/>(그림은 그대로).
    /// </summary>
    public bool TryRender(PixelImage image, Rectangle source, ScriptFrameContext context, IReadOnlyDictionary<string, object> values, CancellationToken cancel)
    {
        var px = image.Pixels;
        long length = (long)px.Length * 4;
        // 스레드 버퍼는 한 번에 한 호출만: 렌더 중 호스트 콜백(ctx.renderedFrameSource가 다른 효과를 그림)이 같은 스레드에서 다시 들어오면 새 배열.
        // 큰 그림(재사용 한도 밖)도 호출마다 새로 잡는다 — Jint 배열 한도와 관계없이 메모리가 허락하는 크기까지
        bool reuse = !t_busy && length <= MaxReusedLength;
        var data = reuse && t_buffer is { } buffer && buffer.Length == length ? buffer : new double[length];
        if (reuse) t_buffer = data;
        for (int i = 0, j = 0; i < px.Length; i++, j += 4)
        {
            var c = px[i];
            data[j] = c.R;
            data[j + 1] = c.G;
            data[j + 2] = c.B;
            data[j + 3] = c.A;
        }
        bool outer = !t_busy;
        t_busy = true;
        try
        {
            Run(data, image.Width, image.Height, source, context, values, cancel, new ScriptColors(_settings, values, context, image, source) { Deferred = _deferred });
        }
        catch (FastDeoptException)
        {
            return false;
        }
        finally
        {
            if (outer) t_busy = false;
        }
        for (int i = 0, j = 0; i < px.Length; i++, j += 4)
            px[i] = new Color32(Byte(data[j]), Byte(data[j + 1]), Byte(data[j + 2]), Byte(data[j + 3]));
        return true;
    }

    [ThreadStatic] private static bool t_busy;

    /// <summary>
    /// <paramref name="data"/>(RGBA, 길이 = 너비 × 높이 × 4)에 render를 돌리고 센 문장 수를 돌려준다. <paramref name="values"/>는
    /// <see cref="ScriptEffectProgram.NormalizeSettings"/>를 거친 값, <paramref name="colors"/>는 같은 값의 색 설정 읽기. 결과가 갈릴 자리에서 <see cref="FastDeoptException"/>.
    /// </summary>
    public long Run(double[] data, int width, int height, Rectangle source, ScriptFrameContext context, IReadOnlyDictionary<string, object> values,
        CancellationToken cancel, ScriptColors colors)
    {
        cancel.ThrowIfCancellationRequested();
        var budget = new FastBudget(_limits, _sites, cancel, data.Length);
        _render(data, Context(width, height, source, context), Settings(values), budget, colors);
        return budget.Statements;
    }

    // 대부분은 0~255 정수 그대로라 반올림·자르기를 건너뛴다(결과는 ScriptHostApi.ToByte와 같음)
    private static byte Byte(double v)
    {
        int k = (int)v;
        return k == v && (uint)k <= 255 ? (byte)k : ScriptHostApi.ToByte(v);
    }

    private static double[] Context(int width, int height, Rectangle source, ScriptFrameContext frame)
    {
        var cv = new double[FastCompiler.CtxLength];
        cv[FastCompiler.CtxT] = frame.Time;
        cv[FastCompiler.CtxPhase] = frame.Phase;
        cv[FastCompiler.CtxProgress] = frame.Progress;
        cv[FastCompiler.CtxFrame] = frame.FrameIndex;
        cv[FastCompiler.CtxStep] = frame.FrameStep;
        cv[FastCompiler.CtxSpan] = frame.FrameSpan;
        cv[FastCompiler.CtxSourceX] = source.X;
        cv[FastCompiler.CtxSourceY] = source.Y;
        cv[FastCompiler.CtxSourceW] = source.Width;
        cv[FastCompiler.CtxSourceH] = source.Height;
        (cv[FastCompiler.CtxPivotX], cv[FastCompiler.CtxPivotY]) = LayerEffects.PivotIn(frame.Pivot, source);
        (cv[FastCompiler.CtxCenterX], cv[FastCompiler.CtxCenterY]) = LayerEffects.PivotIn(null, source);
        cv[FastCompiler.CtxWidth] = width;
        cv[FastCompiler.CtxHeight] = height;
        return cv;
    }

    private double[] Settings(IReadOnlyDictionary<string, object> values)
    {
        var sv = new double[_slots];
        int slot = 0;
        foreach (var setting in _settings)
        {
            var value = values[setting.Key];
            switch (setting.Type)
            {
                case ScriptSettingType.Int:
                    sv[slot++] = (int)value;
                    break;
                case ScriptSettingType.Number:
                    sv[slot++] = (double)value;
                    break;
                case ScriptSettingType.Bool:
                    sv[slot++] = (bool)value ? 1 : 0;
                    break;
                case ScriptSettingType.Color:
                    var c = (Color32)value;
                    sv[slot++] = c.R;
                    sv[slot++] = c.G;
                    sv[slot++] = c.B;
                    sv[slot++] = c.A;
                    break;
                case ScriptSettingType.Choice:
                    var choice = (string)value;
                    int index = 0;
                    while (setting.Choices[index].Value != choice) index++;
                    sv[slot++] = index;
                    break;
                case ScriptSettingType.ColorList:
                    sv[slot++] = ((IReadOnlyList<Color32>)value).Count;
                    break;
                case ScriptSettingType.ColorPairs:
                    sv[slot++] = ((IReadOnlyList<ScriptColorPair>)value).Count;
                    break;
            }
        }
        return sv;
    }
}
