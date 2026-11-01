using System.Globalization;
using System.Text.RegularExpressions;
using Acornima.Ast;
using Chickxel.Core.Localization;
using Jint;
using Jint.Constraints;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 작업 스레드 하나의 Jint 실행기(13.3, 13.4). 같은 <see cref="ScriptLimits"/>면 스레드마다 하나를 만들어 계속 쓴다.
/// <para>
/// 격리: .NET 접근(<c>AllowClr</c>)은 켜지 않고, 문자열 코드 실행(<c>eval</c>, <c>Function</c>, <c>ShadowRealm</c>)을 막는다.
/// <c>Math.random</c>, <c>Date</c>, <c>Intl</c>, <c>Temporal</c>, <c>WeakRef</c>, <c>FinalizationRegistry</c>와 <c>ArrayBuffer</c> 계열, 아래 셋을 뺀 typed array는 지운다
/// (typed array는 한 번에 크게 할당해 메모리 한도를 건너뛸 수 있다). <c>Float64Array</c>·<c>Int32Array</c>·<c>Uint8Array</c>는 만들 때 길이를 검사하는 대리
/// (<see cref="InstallTypedArrays"/>)로만 만들 수 있고 <c>buffer</c>로 <c>ArrayBuffer</c>에 닿지 못한다. 남은 기본 객체는 모두 얼린다.
/// </para>
/// <para>
/// 결정론: 호출마다 처음 상태의 전역으로 되돌린 뒤(<c>RestoreGlobalSnapshot</c>) 해석해 둔 스크립트를 다시 실행해 선언을 새로 만든다.
/// 기본 객체는 얼어 있으므로 앞 호출이 남긴 값이 다음 호출에 닿지 않는다. 그래서 어느 스레드에서 몇 번째로 불러도 결과가 같다.
/// </para>
/// </summary>
internal sealed class ScriptSandbox
{
    private const string FileName = "effect.js";

    // 지울 전역: 시간·무작위·로캘·GC 관찰처럼 결과를 바꾸는 것, 문자열 코드 실행, 한 번에 크게 할당하는 버퍼(검사하는 typed array 셋은 남김)
    private const string Lockdown = """
        (() => {
          'use strict';
          for (const name of ['eval', 'Function', 'Date', 'Intl', 'Temporal', 'ShadowRealm', 'WeakRef', 'FinalizationRegistry',
              'ArrayBuffer', 'SharedArrayBuffer', 'DataView', 'Atomics', 'Int8Array', 'Uint8ClampedArray',
              'Int16Array', 'Uint16Array', 'Uint32Array', 'Float16Array', 'Float32Array',
              'BigInt64Array', 'BigUint64Array']) {
            delete globalThis[name];
          }
          delete Object.getPrototypeOf(Float64Array.prototype).buffer;
          delete Math.random;
          globalThis.lerp = (a, b, t) => a + (b - a) * t;
          // 문법으로만 닿는 기본 객체(생성기, async, 반복자)도 얼린다. Set은 돌면서 더한 값도 차례로 돈다
          const seen = new Set([globalThis, function* () {}, (function* () {})(), async function () {}, async function* () {},
            [][Symbol.iterator](), new Map()[Symbol.iterator](), new Set()[Symbol.iterator](), ''[Symbol.iterator](), /a/g[Symbol.matchAll]('')]);
          const visit = (value) => {
            if ((typeof value === 'object' || typeof value === 'function') && value !== null) seen.add(value);
          };
          for (const target of seen) {
            for (const key of Reflect.ownKeys(target)) {
              const property = Reflect.getOwnPropertyDescriptor(target, key);
              if (property !== undefined) {
                visit(property.value);
                visit(property.get);
                visit(property.set);
              }
            }
            visit(Reflect.getPrototypeOf(target));
            if (target !== globalThis) Object.freeze(target);
          }
        })();
        """;

    // Jint 호출 스택 첫 줄 "at name (effect.js:3:5)"의 줄과 칸(1부터)
    private static readonly Regex StackFrame = new(@":(\d+):(\d+)\)?\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    [ThreadStatic] private static Dictionary<ScriptLimits, ScriptSandbox>? t_sandboxes;

    private readonly CallCancellation _cancel = new();
    private readonly Jint.GlobalSnapshot _baseline;
    private readonly bool _cached;
    private bool _busy;
    private int _declarations;
    private JsValue _declaration = JsValue.Undefined;

    private ScriptSandbox(ScriptLimits limits, bool cached)
    {
        Limits = limits;
        _cached = cached;
        Engine = new Engine(options => options
            .Strict()
            .DisableStringCompilation()
            .Culture(CultureInfo.InvariantCulture)
            .LocalTimeZone(TimeZoneInfo.Utc)
            .RegexTimeoutInterval(limits.Timeout)
            .MaxStatements((int)Math.Min(int.MaxValue, limits.MaxStatements))
            .LimitMemory(limits.MemoryBytes)
            .LimitRecursion(limits.MaxRecursion)
            .MaxArraySize(limits.MaxArrayLength)
            .Constraint(_cancel));
        Engine.SetValue("effect", new ClrFunction(Engine, "effect", (_, args) =>
        {
            if (++_declarations == 1) _declaration = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        }, 1));
        ImagePrototype = ScriptHostApi.Install(this);
        InstallTypedArrays();
        Engine.Execute(Lockdown);
        _baseline = Engine.Advanced.CaptureGlobalSnapshot();
    }

    /// <summary>
    /// <c>Float64Array</c>·<c>Int32Array</c>·<c>Uint8Array</c>를 길이를 검사하는 대리(<c>Proxy</c>의 construct가 호스트 함수 — 문장으로 세지 않음)로 바꾼다:
    /// 길이(수 하나, 또는 배열·typed array 인자의 <c>length</c>)가 0~<see cref="ScriptLimits.MaxArrayLength"/> 정수이고 바이트가 메모리 한도 안이어야 한다.
    /// 원형의 <c>constructor</c>도 대리라 <c>a.constructor</c>·<c>from</c>·<c>slice</c> 등으로 만드는 배열도 같은 검사를 거친다.
    /// </summary>
    private void InstallTypedArrays()
    {
        var wrap = Engine.Evaluate("(native, trap) => { const proxy = new Proxy(native, { construct: trap }); "
                                   + "Object.defineProperty(native.prototype, 'constructor', { value: proxy, writable: false, enumerable: false, configurable: false }); return proxy; }");
        foreach (var (name, bytes) in new[] { ("Float64Array", 8L), ("Int32Array", 4L), ("Uint8Array", 1L) })
        {
            var native = Engine.GetValue(name);
            var trap = new ClrFunction(Engine, "construct", (_, args) =>
            {
                var list = args.Length > 1 ? args[1] as Jint.Native.Array.ArrayInstance : null;
                var first = list is not null && ScriptHostApi.Length(list) > 0 ? list[0] : JsValue.Undefined;
                double length = first is JsNumber number ? number.AsNumber()
                    : first is Jint.Native.Object.ObjectInstance obj && !first.IsCallable() ? Jint.Runtime.TypeConverter.ToNumber(obj.Get("length"))
                    : double.NaN;
                if (!(length >= 0 && length <= Limits.MaxArrayLength && length == Math.Floor(length) && length * bytes <= Limits.MemoryBytes))
                    throw new Jint.Runtime.JavaScriptException(Engine.Intrinsics.TypeError,
                        DomainStrings.FormatScript_InvalidTypedArrayLength(name: name, max: Limits.MaxArrayLength));
                return Engine.Construct(native, [first]);
            }, 3);
            Engine.SetValue(name, Engine.Invoke(wrap, native, trap));
        }
    }

    public ScriptLimits Limits { get; }
    public Engine Engine { get; }

    /// <summary>이 실행기에서 만든 그림(<c>img</c>, <c>img.blank</c>, <c>ctx.frameSource</c>)의 원형: <c>blank</c>, <c>blendOver</c>.</summary>
    public Jint.Native.Object.ObjectInstance ImagePrototype { get; }

    /// <summary>
    /// 이 스레드의 실행기에서 <paramref name="body"/>를 돌린다. 한도 초과와 스크립트 오류는 <see cref="ScriptEffectException"/>으로,
    /// <paramref name="cancel"/> 취소는 <see cref="OperationCanceledException"/>으로 나간다.
    /// </summary>
    public static T Run<T>(ScriptLimits limits, CancellationToken cancel, Func<ScriptSandbox, T> body) => Run(limits, limits, cancel, body);

    /// <summary>
    /// <see cref="Run{T}(ScriptLimits, CancellationToken, Func{ScriptSandbox, T})"/>를 이 호출만 <paramref name="call"/> 한도(문장·시간·메모리 —
    /// 보통 <see cref="ScriptLimits.ForPixels"/>)로. 실행기는 <paramref name="limits"/>로 찾으므로 그림 크기마다 새로 만들지 않는다.
    /// </summary>
    public static T Run<T>(ScriptLimits limits, ScriptLimits call, CancellationToken cancel, Func<ScriptSandbox, T> body)
    {
        var sandboxes = t_sandboxes ??= [];
        if (!sandboxes.TryGetValue(limits, out var sandbox)) sandboxes[limits] = sandbox = new ScriptSandbox(limits, cached: true);
        // 렌더 중 호스트 콜백(ctx.frameSource 등)이 같은 스레드에서 다시 스크립트를 부르면 쓰던 상태를 건드리지 않게 따로 만든다
        if (sandbox._busy) sandbox = new ScriptSandbox(limits, cached: false);
        return sandbox.Enter(call, cancel, body);
    }

    private static readonly System.Reflection.FieldInfo? MemoryLimitField =
        typeof(MemoryLimitConstraint).GetField("_memoryLimit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    // 이 호출의 문장·메모리·시간 한도를 넣는다(실행기를 만들 때의 값이 바탕 — 끝나면 되돌림)
    private void Apply(ScriptLimits call)
    {
        CallLimits = call;
        if (Engine.Constraints.Find<MaxStatementsConstraint>() is { } statements) statements.MaxStatements = (int)Math.Min(int.MaxValue, call.MaxStatements);
        if (Engine.Constraints.Find<MemoryLimitConstraint>() is { } memory) MemoryLimitField?.SetValue(memory, call.MemoryBytes);
        _cancel.Timeout = call.Timeout;
    }

    private T Enter<T>(ScriptLimits call, CancellationToken cancel, Func<ScriptSandbox, T> body)
    {
        cancel.ThrowIfCancellationRequested();
        _busy = true;
        _cancel.Token = cancel;
        Apply(call);
        try
        {
            Engine.Advanced.RestoreGlobalSnapshot(_baseline);
            _cancel.Start();
            return body(this);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (ScriptEffectException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
        finally
        {
            Apply(Limits);
            _cancel.Token = default;
            _declaration = JsValue.Undefined;
            _busy = false;
            if (!_cached) Engine.Dispose();
        }
    }

    /// <summary>지금 호출의 한도(<see cref="Run{T}(ScriptLimits, ScriptLimits, CancellationToken, Func{ScriptSandbox, T})"/>의 call, 아니면 <see cref="Limits"/>).</summary>
    public ScriptLimits CallLimits { get; private set; } = null!;

    /// <summary>스크립트 맨 위를 실행하고 <c>effect(...)</c>에 넘긴 선언 객체를 돌려준다.</summary>
    public Jint.Native.Object.ObjectInstance Declare(Prepared<Script> script)
    {
        _declarations = 0;
        _declaration = JsValue.Undefined;
        Engine.Execute(script);
        if (_declarations == 0)
            throw new ScriptEffectException(new(ScriptErrorKind.Declaration, DomainStrings.Script_NoEffect, 0, 0));
        if (_declarations > 1)
            throw Declaration(DomainStrings.Script_DuplicateEffect, ScriptSourceLocator.EffectCall(script.Program!, 1));
        if (_declaration is not Jint.Native.Object.ObjectInstance declaration || _declaration.IsCallable())
            throw Declaration(DomainStrings.Script_DeclarationNotObject, ScriptSourceLocator.EffectCall(script.Program!, 0));
        return declaration;
    }

    public static Prepared<Script> Prepare(string source) => Engine.PrepareScript(source, FileName, strict: true);

    private static ScriptEffectException Declaration(string message, (int Line, int Column) at)
        => new(new(ScriptErrorKind.Declaration, message, at.Line, at.Column));

    /// <summary>문장 한도 오류(Jint와 빠른 경로가 같은 문구를 쓴다).</summary>
    internal static ScriptEffectException StatementLimitError(ScriptLimits limits, int line, int column)
        => new(new(ScriptErrorKind.Statements, DomainStrings.FormatScript_StatementLimit(count: limits.MaxStatements), line, column));

    /// <summary>시간 한도 오류(Jint와 빠른 경로가 같은 문구를 쓴다).</summary>
    internal static ScriptEffectException TimeoutError(ScriptLimits limits, int line, int column)
        => new(new(ScriptErrorKind.Timeout,
            DomainStrings.FormatScript_Timeout(seconds: limits.Timeout.TotalSeconds.ToString("0.##", CultureInfo.CurrentCulture)), line, column));

    private ScriptEffectException Translate(Exception ex)
    {
        var (line, column) = ex is JavaScriptException { Location.Start.Line: > 0 } thrown
            ? (thrown.Location.Start.Line, thrown.Location.Start.Column + 1)
            : StackLocation();
        switch (ex)
        {
            case StatementsCountOverflowException:
                return StatementLimitError(CallLimits, line, column);
            case TimeoutException or RegexMatchTimeoutException:
                return TimeoutError(CallLimits, line, column);
        }
        var (kind, message) = ex switch
        {
            JavaScriptException js => (ScriptErrorKind.Runtime, DomainStrings.FormatScript_RuntimeError(detail: js.Message)),
            MemoryLimitExceededException or OutOfMemoryException => (ScriptErrorKind.Memory,
                DomainStrings.FormatScript_MemoryLimit(megabytes: CallLimits.MemoryBytes >> 20, length: CallLimits.MaxArrayLength)),
            RecursionDepthOverflowException or InsufficientExecutionStackException => (ScriptErrorKind.Recursion,
                DomainStrings.FormatScript_RecursionLimit(depth: Limits.MaxRecursion)),
            _ => (ScriptErrorKind.Runtime, DomainStrings.FormatScript_RuntimeError(detail: ex.Message)),
        };
        return new ScriptEffectException(new(kind, message, line, column));
    }

    // 한도 예외에는 위치가 없으므로 멈춘 순간의 JS 호출 스택에서 읽는다
    private (int Line, int Column) StackLocation()
    {
        var match = StackFrame.Match(Engine.Advanced.StackTrace);
        return match.Success
            ? (int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture))
            : (0, 0);
    }

    /// <summary>
    /// 호출마다 바꾸는 취소 토큰과 시간 한도(Jint 기본 제약은 실행기를 만들 때 값이 정해진다 — 시간 한도는 그림 크기에 따라 호출마다 다르다).
    /// 시간은 64문장마다 본다.
    /// </summary>
    private sealed class CallCancellation : Constraint
    {
        public CancellationToken Token;
        public TimeSpan Timeout;
        private long _deadline = long.MaxValue;
        private int _count;

        public void Start()
        {
            _deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(Timeout.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
            _count = 0;
        }

        public override void Check()
        {
            Token.ThrowIfCancellationRequested();
            if ((++_count & 63) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > _deadline) throw new TimeoutException();
        }

        public override void Reset()
        {
        }
    }
}
