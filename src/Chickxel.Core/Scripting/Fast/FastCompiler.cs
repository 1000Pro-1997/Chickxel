using System.Globalization;
using System.Reflection;
using Acornima.Ast;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using E = System.Linq.Expressions.Expression;
using LabelTarget = System.Linq.Expressions.LabelTarget;
using ParameterExpression = System.Linq.Expressions.ParameterExpression;

namespace Chickxel.Core.Scripting.Fast;

/// <summary>
/// 컴파일한 render: <paramref name="data"/>는 RGBA <c>img.data</c>, <paramref name="ctx"/>는 <see cref="FastCompiler"/>의 Ctx 칸, <paramref name="settings"/>는 설정 칸,
/// <paramref name="colors"/>는 색 설정 읽기(<c>ctx.color</c> 등).
/// </summary>
internal delegate void FastRenderFn(double[] data, double[] ctx, double[] settings, FastBudget budget, ScriptColors colors);

/// <summary>
/// 스크립트 효과 원문의 숫자 부분집합을 .NET 식 트리로 바꿔 컴파일한다(빠른 경로, 설계 8장). Jint가 해석한 나무(<see cref="Script"/>)를 그대로 읽는다.
/// <para>
/// 허용(이 밖은 <see cref="FastUnsupportedException"/> → render 전체를 Jint로):
/// </para>
/// <list type="bullet">
/// <item>맨 위: <c>effect({ … render … })</c> 하나(선언 객체 안에는 리터럴·배열·객체·함수만), <c>const</c>(숫자·참거짓 식, 함수, render에서 읽지 않는 리터럴 묶음), <c>function</c> 선언.</item>
/// <item>문장: <c>let</c>/<c>const</c>, <c>if</c>, <c>for</c>, <c>while</c>, <c>do … while</c>, 이름 없는 <c>break</c>/<c>continue</c>, <c>return</c>, 블록, 빈 문장, 식 문장.</item>
/// <item>값: 숫자와 참거짓만 변수에 담는다. 숫자 연산·비교·비트 연산(ToInt32/ToUint32), <c>**</c>, 논리 연산, 조건식, 쉼표, 증감, 복합 대입.</item>
/// <item>전역: <c>Math</c>의 상수와 abs, acos, asin, atan, atan2, ceil, clz32, cos, cosh, exp, floor, fround, hypot(인자 3개까지), imul, log, log10, log2,
/// max, min, pow, round, sign, sin, sinh, sqrt, tan, tanh, trunc(모두 Jint 구현 그대로 <see cref="JsOps"/>), <c>NaN</c>, <c>Infinity</c>, <c>lerp</c>,
/// <c>hsvToRgb</c>, <c>rgbToHsv</c>.</item>
/// <item>host: <c>img.width/height/data</c>, <c>d[i]</c> 읽기·쓰기, <c>d.length</c>, <c>s.키</c>(int·number·bool·color, choice는 문자열 리터럴과 같음 비교만,
/// 색 목록·색 쌍 목록은 <c>s.키.length</c>만), <c>ctx.t/progress/frame/step/span/source/pivot/center</c>, <c>ctx.color</c>·<c>ctx.colorAt</c>·<c>ctx.pairColor</c>
/// (키·쌍 칸은 문자열 리터럴, 범위는 <c>{ x, y, w, h }</c> 리터럴이나 숫자 객체), <c>ctx.hasGradient("슬롯")</c>, <c>ctx.hash</c>, <c>ctx.random</c>,
/// <c>ctx.bayer2</c>, <c>ctx.bayer4</c>.</item>
/// <item>함수: <c>const</c>에 묶은 화살표·<c>function</c>과 <c>function</c> 선언을 부르는 자리에 펼친다(재귀 불가, 함수를 돌려주기 불가).</item>
/// </list>
/// <para>
/// 실행 중 JS와 결과가 갈릴 수 있는 자리(배열 밖·정수 아닌 칸, 값으로 쓰는 함수가 값 없이 끝남)는 <see cref="FastDeoptException"/>을 던져
/// 그 호출만 Jint로 다시 그린다.
/// </para>
/// <para>
/// 문장 수: Jint 4.17과 같은 자리에서 1씩 센다(<see cref="Charge"/>). Jint는 실행하는 문장마다(<c>JintStatement.Execute</c>, 블록 문장 자체는 빼고),
/// 문장 목록을 실행할 때마다(<c>JintStatementList.Execute</c>: 함수 몸통과 문장이 하나가 아닌 블록), 식 몸통 화살표 함수를 부를 때마다 센다.
/// 문장 목록 바로 안의 리터럴 <c>return</c>(<c>return 0;</c>)은 실행하지 않고 값만 꺼내므로 세지 않는다. 반복문의 조건·증감식은 세지 않고,
/// <c>for</c>의 <c>let</c> 초기화는 문장으로 센다.
/// </para>
/// </summary>
internal sealed class FastCompiler
{
    public const int CtxT = 0, CtxProgress = 1, CtxFrame = 2, CtxStep = 3, CtxSpan = 4, CtxSourceX = 5, CtxSourceY = 6, CtxSourceW = 7,
        CtxSourceH = 8, CtxPivotX = 9, CtxPivotY = 10, CtxCenterX = 11, CtxCenterY = 12, CtxWidth = 13, CtxHeight = 14, CtxPhase = 15, CtxLength = 16;

    // 펼친 함수 호출 수 한도: 넘으면 코드가 너무 커지므로 Jint로
    private const int MaxInlines = 4096;

    private enum Kind
    {
        Num,     // double
        Bool,    // bool
        Void,    // 값을 쓰지 않는 자리의 식
        Choice,  // 고르기 설정: Expr = 값의 칸 번호(double)
        Data,    // img.data
        Typed,   // 숫자 배열(new Float64Array/Int32Array/Uint8Array, img.data.slice()): Expr = double[] 변수, Name = 종류(JsOps.Array*)
        Image,   // img
        Ctx,     // ctx
        Record,  // 숫자 필드만 있는 객체(s, 색, ctx.source …)
        Math,
        Func,    // 펼칠 함수
        Builtin, // lerp, hsvToRgb, rgbToHsv
        Opaque,  // render에서 쓸 수 없는 맨 위 상수(설정 목록 등)
    }

    private sealed class Val
    {
        public required Kind Kind;
        public E? Expr;
        public E? Pre; // 별칭 값을 만들 때 먼저 실행할 부수 효과
        public Dictionary<string, Val>? Fields;
        public FuncDef? Fn;
        public string? Name;
        public IReadOnlyList<string>? Choices;

        public static Val Num(E e) => new() { Kind = Kind.Num, Expr = e };
        public static Val Bool(E e) => new() { Kind = Kind.Bool, Expr = e };
        public static Val Void(E e) => new() { Kind = Kind.Void, Expr = e };
        public bool IsValue => Kind is Kind.Num or Kind.Bool;
    }

    private sealed class FuncDef(string name, string[] parameters, Node body, bool concise, Node node)
    {
        public string Name { get; } = name;
        public string[] Params { get; } = parameters;
        public Node Body { get; } = body;
        public bool Concise { get; } = concise;
        public Node Node { get; } = node;
        public Scope Scope { get; set; } = null!;
    }

    private sealed class Binding
    {
        public ParameterExpression? Var; // 숫자·참거짓 변수
        public Val? Alias;               // 별칭(그림, ctx, 색, 함수 …)
        public bool Mutable;
        public bool Pending;             // 선언 문장에 닿기 전(TDZ)
    }

    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, Binding> _map = new(StringComparer.Ordinal);
        public List<ParameterExpression> Vars { get; } = [];

        public void Bind(string name, Binding binding)
        {
            _map[name] = binding;
            if (binding.Var is not null) Vars.Add(binding.Var);
        }

        public void Pending(string name) => _map[name] = new Binding { Pending = true };

        public Binding? Find(string name) => _map.TryGetValue(name, out var b) ? b : parent?.Find(name);
    }

    private sealed class FnContext
    {
        public required LabelTarget Return;
        public required string Name;
        public bool ValueNeeded;
        public ParameterExpression? Result;
        public Kind ResultKind;
    }

    private static readonly E Empty = E.Empty();
    // null일 수 있는 객체 Val의 표시(ctx.renderedFrameSource 결과 — 조건 자리에서 null이 아닌지 본다)
    private const string Nullable = "nullable";
    private static readonly E NaN = E.Constant(double.NaN);
    private static readonly MethodInfo RefillMethod = typeof(FastBudget).GetMethod(nameof(FastBudget.Refill))!;
    private static readonly MethodInfo FinishMethod = typeof(FastBudget).GetMethod(nameof(FastBudget.Finish))!;

    private readonly ParameterExpression _data = E.Parameter(typeof(double[]), "data");
    private readonly ParameterExpression _cv = E.Parameter(typeof(double[]), "ctx");
    private readonly ParameterExpression _sv = E.Parameter(typeof(double[]), "settings");
    private readonly ParameterExpression _budget = E.Parameter(typeof(FastBudget), "budget");
    private readonly ParameterExpression _colors = E.Parameter(typeof(ScriptColors), "colors");
    private readonly IReadOnlyList<ScriptSetting> _schema;
    private readonly ParameterExpression _ticks = E.Variable(typeof(long), "ticks");
    private readonly List<ParameterExpression> _hoisted = []; // 호출 결과 객체의 필드: 별칭이 블록 밖까지 살 수 있어 맨 바깥에 둔다
    private readonly List<FastSite> _sites = [];
    private readonly Stack<(LabelTarget Break, LabelTarget Continue)> _loops = new();
    private readonly Stack<FnContext> _functions = new();
    private readonly HashSet<FuncDef> _inlining = [];
    private readonly ScriptLimits _limits;
    private readonly Val _settings, _ctx;
    private Scope _scope = new(null);
    private bool _prologue;
    private int _depth;
    private int _inlines;

    private FastCompiler(IReadOnlyList<ScriptSetting> settings, ScriptLimits limits)
    {
        _limits = limits;
        _schema = settings;
        var fields = new Dictionary<string, Val>(StringComparer.Ordinal);
        int slot = 0;
        foreach (var setting in settings)
        {
            switch (setting.Type)
            {
                case ScriptSettingType.Int:
                case ScriptSettingType.Number:
                    fields[setting.Key] = Val.Num(Slot(_sv, slot++));
                    break;
                case ScriptSettingType.Bool:
                    fields[setting.Key] = Val.Bool(E.NotEqual(Slot(_sv, slot++), E.Constant(0.0)));
                    break;
                case ScriptSettingType.Color:
                    fields[setting.Key] = Record(("r", Slot(_sv, slot)), ("g", Slot(_sv, slot + 1)), ("b", Slot(_sv, slot + 2)), ("a", Slot(_sv, slot + 3)));
                    slot += 4;
                    break;
                case ScriptSettingType.Choice:
                    fields[setting.Key] = new Val { Kind = Kind.Choice, Expr = Slot(_sv, slot++), Name = setting.Key, Choices = [.. setting.Choices.Select(c => c.Value)] };
                    break;
                // 색 목록·색 쌍 목록은 항목 수 한 칸: s.키.length만 읽는다(항목은 ctx.colorAt·ctx.pairColor로). 그 밖은 이름 오류 → Jint
                case ScriptSettingType.ColorList:
                case ScriptSettingType.ColorPairs:
                    fields[setting.Key] = Record(("length", Slot(_sv, slot++)));
                    break;
                // 레이어 고르기(이름 글)는 칸이 없다: s.키를 읽으면 이름 오류 → Jint(그 레이어를 읽는 ctx.layer 등도 Jint에만 있음)
                case ScriptSettingType.Layer:
                    break;
            }
        }
        _settings = new Val { Kind = Kind.Record, Fields = fields };
        _ctx = new Val
        {
            Kind = Kind.Ctx,
            Fields = new(StringComparer.Ordinal)
            {
                ["t"] = Val.Num(Slot(_cv, CtxT)),
                ["phase"] = Val.Num(Slot(_cv, CtxPhase)),
                ["progress"] = Val.Num(Slot(_cv, CtxProgress)),
                ["frame"] = Val.Num(Slot(_cv, CtxFrame)),
                ["step"] = Val.Num(Slot(_cv, CtxStep)),
                ["span"] = Val.Num(Slot(_cv, CtxSpan)),
                ["source"] = Record(("x", Slot(_cv, CtxSourceX)), ("y", Slot(_cv, CtxSourceY)), ("w", Slot(_cv, CtxSourceW)), ("h", Slot(_cv, CtxSourceH))),
                ["pivot"] = Record(("x", Slot(_cv, CtxPivotX)), ("y", Slot(_cv, CtxPivotY))),
                // ctx.center는 늘 같은 모양의 숫자 객체라 빠른 경로도 읽는다. ctx.slice(null일 수 있음)·다른 레이어 함수(ctx.layer 등)는 이름 오류 → Jint
                ["center"] = Record(("x", Slot(_cv, CtxCenterX)), ("y", Slot(_cv, CtxCenterY))),
            },
        };
    }

    /// <summary>설정 칸 배치(선언 순서): 숫자·참거짓·고르기(값의 번호)·색 목록과 색 쌍 목록(항목 수) = 1칸, 색 = r, g, b, a 4칸, 레이어 고르기 = 0칸.</summary>
    public static int SlotCount(IReadOnlyList<ScriptSetting> settings)
        => settings.Sum(s => s.Type switch { ScriptSettingType.Color => 4, ScriptSettingType.Layer => 0, _ => 1 });

    /// <summary>
    /// <paramref name="script"/>(해석한 원문)의 render를 컴파일한다. 부분집합 밖이면 <see cref="FastUnsupportedException"/>.
    /// <paramref name="deferred"/>는 미루는 스크립트인지(<c>ctx.renderedFrameSource</c>가 그림을 줄지).
    /// </summary>
    public static FastRender Compile(Script script, IReadOnlyList<ScriptSetting> settings, ScriptLimits limits, bool deferred = false)
    {
        var compiler = new FastCompiler(settings, limits);
        var render = compiler.CompileScript(script);
        return new FastRender(render, [.. compiler._sites], settings, limits, deferred);
    }

    private static E Slot(ParameterExpression array, int index) => E.ArrayIndex(array, E.Constant(index));

    private static Val Record(params (string Name, E Expr)[] fields)
        => new() { Kind = Kind.Record, Fields = fields.ToDictionary(f => f.Name, f => Val.Num(f.Expr), StringComparer.Ordinal) };

    // ───────────── 오류 ─────────────

    private static FastUnsupportedException Syntax(Node node) => new(DomainStrings.FormatScript_FastSyntax(construct: Describe(node)), node);

    private static FastUnsupportedException NameError(string name, Node node) => new(DomainStrings.FormatScript_FastName(name: name), node);

    private static FastUnsupportedException ValueError(Node node) => new(DomainStrings.FormatScript_FastValue(construct: Describe(node)), node);

    private static string Describe(Node node) => node switch
    {
        Identifier id => id.Name,
        MemberExpression { Computed: false, Property: Identifier p } m => Describe(m.Object) + "." + p.Name,
        StringLiteral => "string",
        _ => node.Type.ToString(),
    };

    // ───────────── 맨 위 ─────────────

    private FastRenderFn CompileScript(Script script)
    {
        Node? render = null;
        foreach (var statement in script.Body)
        {
            if (statement is FunctionDeclaration function) DeclareFunction(function);
            else if (statement is VariableDeclaration declaration) DeclarePending(declaration);
        }
        var prologue = new List<E>();
        _prologue = true;
        foreach (var statement in script.Body)
        {
            switch (statement)
            {
                case ExpressionStatement { Expression: StringLiteral }:
                case EmptyStatement:
                case FunctionDeclaration:
                    break;
                case ExpressionStatement { Expression: CallExpression { Callee: Identifier { Name: "effect" } } call }:
                    render = FindRender(call);
                    break;
                case VariableDeclaration { Kind: VariableDeclarationKind.Const } constants:
                    prologue.Add(Declaration(constants));
                    break;
                default:
                    throw new FastUnsupportedException(DomainStrings.Script_FastTopLevel, statement);
            }
        }
        _prologue = false;
        var fn = render switch
        {
            null => throw new FastUnsupportedException(DomainStrings.Script_MissingRender, null),
            Identifier id => Lookup(id) is { Kind: Kind.Func, Fn: { } named } ? named : throw ValueError(id),
            _ => ToFunc("render", render),
        };
        var body = new List<E> { E.Assign(_ticks, E.Constant(0L)) };
        body.AddRange(prologue);
        body.Add(CompileRender(fn));
        body.Add(E.Call(_budget, FinishMethod, _ticks));
        var lambda = E.Lambda<FastRenderFn>(E.Block(typeof(void), [_ticks, .. _hoisted, .. _scope.Vars], body), _data, _cv, _sv, _budget, _colors);
        return lambda.Compile();
    }

    // 선언 객체를 읽는 동안(Jint의 맨 위 실행) 코드가 돌지 않아야 render 앞의 전역 상태가 원문 그대로다
    private Node FindRender(CallExpression call)
    {
        if (call.Arguments.Count < 1 || call.Arguments[0] is not ObjectExpression declaration)
            throw new FastUnsupportedException(DomainStrings.Script_FastTopLevel, call);
        for (int i = 1; i < call.Arguments.Count; i++) CheckInert(call.Arguments[i]);
        Node? render = null;
        foreach (var node in declaration.Properties)
        {
            if (node is not ObjectProperty { Kind: PropertyKind.Init, Computed: false } property || PropertyName(property.Key) is not { } name)
                throw Syntax(node);
            if (name == "render")
            {
                render = property.Value is Identifier or FunctionExpression or ArrowFunctionExpression ? property.Value : throw ValueError(property.Value);
            }
            else
            {
                CheckInert(property.Value);
            }
        }
        return render ?? throw new FastUnsupportedException(DomainStrings.Script_MissingRender, declaration);
    }

    private static string? PropertyName(Node key) => key switch
    {
        Identifier id => id.Name,
        StringLiteral s => s.Value,
        _ => null,
    };

    /// <summary>실행해도 코드가 돌지 않는 값: 리터럴, 이름, 함수, 그런 것만 담은 배열·객체.</summary>
    private static void CheckInert(Node? node)
    {
        switch (node)
        {
            case null:
            case Literal:
            case Identifier:
            case FunctionExpression:
            case ArrowFunctionExpression:
            case TemplateLiteral { Expressions.Count: 0 }:
            case NonUpdateUnaryExpression { Operator: Acornima.Operator.UnaryNegation or Acornima.Operator.UnaryPlus, Argument: NumericLiteral }:
                return;
            case ArrayExpression array:
                foreach (var element in array.Elements) CheckInert(element);
                return;
            case ObjectExpression obj:
                foreach (var p in obj.Properties)
                {
                    if (p is not ObjectProperty { Kind: PropertyKind.Init, Computed: false } property) throw Syntax(p);
                    CheckInert(property.Value);
                }
                return;
            default:
                throw Syntax(node);
        }
    }

    private static bool IsInert(Node node)
    {
        try
        {
            CheckInert(node);
            return true;
        }
        catch (FastUnsupportedException)
        {
            return false;
        }
    }

    private void DeclarePending(VariableDeclaration declaration)
    {
        if (declaration.Kind is not (VariableDeclarationKind.Let or VariableDeclarationKind.Const)) return; // 컴파일할 때 거부
        foreach (var d in declaration.Declarations)
            if (d.Id is Identifier id) _scope.Pending(id.Name);
    }

    private void DeclareFunction(FunctionDeclaration function)
    {
        var name = function.Id?.Name ?? throw Syntax(function);
        _scope.Bind(name, new Binding { Alias = new Val { Kind = Kind.Func, Fn = ToFunc(name, function) } });
    }

    private FuncDef ToFunc(string name, Node node)
    {
        switch (node)
        {
            case ArrowFunctionExpression { Async: false } arrow:
                return new FuncDef(name, Params(arrow.Params), arrow.Body, arrow.Body is not FunctionBody, arrow) { Scope = _scope };
            case FunctionDeclaration { Async: false, Generator: false } declaration:
                return new FuncDef(name, Params(declaration.Params), declaration.Body, false, declaration) { Scope = _scope };
            case FunctionExpression { Async: false, Generator: false } expression:
            {
                var fn = new FuncDef(name, Params(expression.Params), expression.Body, false, expression);
                if (expression.Id is { } id)
                {
                    // 이름 있는 함수식: 이름은 함수 안에서만 자기 자신
                    var self = new Scope(_scope);
                    self.Bind(id.Name, new Binding { Alias = new Val { Kind = Kind.Func, Fn = fn } });
                    fn.Scope = self;
                }
                else
                {
                    fn.Scope = _scope;
                }
                return fn;
            }
            default:
                throw Syntax(node);
        }
    }

    private static string[] Params(in NodeList<Node> parameters)
    {
        var names = new string[parameters.Count];
        for (int i = 0; i < names.Length; i++) names[i] = parameters[i] is Identifier id ? id.Name : throw Syntax(parameters[i]);
        return names;
    }

    // ───────────── 문장 수 ─────────────

    /// <summary>
    /// 문장 하나를 센다: 지역 계수기를 1 줄이고 몫이 떨어지면 <see cref="FastBudget.Refill"/>.
    /// 위치는 Jint가 그 자리에서 멈출 때의 위치(끝 줄, 시작 칸 + 1).
    /// </summary>
    private E Charge(Node node, bool deopt = false)
    {
        if (_prologue) throw new FastUnsupportedException(DomainStrings.Script_FastTopLevel, node);
        int site = _sites.Count;
        _sites.Add(new FastSite(node.Location.End.Line, node.Location.Start.Column + 1, deopt));
        return E.IfThen(E.LessThan(E.PreDecrementAssign(_ticks), E.Constant(0L)),
            E.Assign(_ticks, E.Call(_budget, RefillMethod, E.Constant(site))));
    }

    // ───────────── 함수 ─────────────

    private E CompileRender(FuncDef fn)
    {
        if (fn.Params.Length > 3) throw Syntax(fn.Node);
        var scope = new Scope(fn.Scope);
        Val[] args = [new Val { Kind = Kind.Image }, _ctx, _settings];
        for (int i = 0; i < fn.Params.Length; i++) scope.Bind(fn.Params[i], new Binding { Alias = args[i] });
        _inlining.Add(fn);
        _depth = 1;
        return Body(fn, scope, [], valueNeeded: false).Expr!;
    }

    private Val Inline(FuncDef fn, CallExpression call, bool valueNeeded)
    {
        var arguments = call.Arguments;
        if (arguments.Count < fn.Params.Length) throw new FastUnsupportedException(DomainStrings.FormatScript_FastArguments(name: fn.Name), call);
        // 인자는 부르는 자리(호출 깊이도 그 자리)에서 왼쪽부터 계산하고, 남는 인자도 계산은 한다
        var inits = new List<E>();
        var scope = new Scope(fn.Scope);
        for (int i = 0; i < arguments.Count; i++)
        {
            var arg = Expr(arguments[i]);
            if (i >= fn.Params.Length)
            {
                inits.Add(Effect(arg, arguments[i]));
            }
            else if (arg.IsValue)
            {
                var p = E.Variable(arg.Expr!.Type, fn.Params[i]);
                inits.Add(E.Assign(p, arg.Expr));
                scope.Bind(fn.Params[i], new Binding { Var = p, Mutable = true });
            }
            else if (arg.Kind is Kind.Void or Kind.Opaque)
            {
                throw ValueError(arguments[i]);
            }
            else
            {
                if (arg.Pre is not null) inits.Add(arg.Pre);
                scope.Bind(fn.Params[i], new Binding { Alias = WithoutPre(arg) });
            }
        }
        if (!_inlining.Add(fn) || _depth + 2 > _limits.MaxRecursion)
            throw new FastUnsupportedException(DomainStrings.FormatScript_FastRecursion(name: fn.Name), call);
        if (++_inlines > MaxInlines) throw new FastUnsupportedException(DomainStrings.Script_FastTooLarge, call);
        _depth++;
        try
        {
            return Body(fn, scope, inits, valueNeeded);
        }
        finally
        {
            _depth--;
            _inlining.Remove(fn);
        }
    }

    private Val Body(FuncDef fn, Scope scope, List<E> inits, bool valueNeeded)
    {
        var savedScope = _scope;
        var savedLoops = _loops.ToArray();
        _loops.Clear(); // 함수 안에서 바깥 반복을 break할 수 없다
        _scope = scope;
        try
        {
            if (fn.Concise)
            {
                var expression = (Expression)fn.Body;
                inits.Add(Charge(expression, deopt: true));
                var v = Expr(expression, valueNeeded);
                if (!valueNeeded)
                {
                    inits.Add(Effect(v, expression));
                    return Val.Void(E.Block(typeof(void), scope.Vars, inits));
                }
                if (v.IsValue)
                {
                    inits.Add(v.Expr!);
                    return new Val { Kind = v.Kind, Expr = E.Block(v.Expr!.Type, scope.Vars, inits) };
                }
                // 함수를 돌려주면 펼친 범위 밖에서 그 범위 변수를 쓰게 된다
                if (v.Kind is Kind.Void or Kind.Func or Kind.Opaque) throw ValueError(expression);
                if (v.Pre is not null) inits.Add(v.Pre);
                var alias = WithoutPre(v);
                alias.Pre = E.Block(typeof(void), scope.Vars, inits);
                return alias;
            }

            var body = (FunctionBody)fn.Body;
            var context = new FnContext { Return = E.Label(fn.Name + "Return"), Name = fn.Name, ValueNeeded = valueNeeded };
            _functions.Push(context);
            try
            {
                inits.Add(Charge(body));
                inits.Add(StatementList(body.Body, new Scope(scope), inList: true));
            }
            finally
            {
                _functions.Pop();
            }
            if (!valueNeeded)
            {
                inits.Add(E.Label(context.Return));
                return Val.Void(E.Block(typeof(void), scope.Vars, inits));
            }
            var result = context.Result ?? throw new FastUnsupportedException(DomainStrings.FormatScript_FastNoValue(name: fn.Name), fn.Node);
            inits.Add(E.Throw(E.New(typeof(FastDeoptException)))); // 끝까지 가면 undefined
            inits.Add(E.Label(context.Return));
            inits.Add(result);
            return new Val { Kind = context.ResultKind, Expr = E.Block(result.Type, [.. scope.Vars, result], inits) };
        }
        finally
        {
            _scope = savedScope;
            for (int i = savedLoops.Length - 1; i >= 0; i--) _loops.Push(savedLoops[i]);
        }
    }

    // ───────────── 문장 ─────────────

    /// <summary>
    /// 문장 목록(함수 몸통, 블록). 함수 선언은 첫머리에서 만들고, let/const 이름은 선언 문장에 닿기 전까지 쓸 수 없게(TDZ) 미리 잡는다.
    /// <paramref name="inList"/>면 Jint의 <c>JintStatementList</c>처럼 리터럴 <c>return</c>을 세지 않는다.
    /// </summary>
    private E StatementList(in NodeList<Statement> statements, Scope scope, bool inList)
    {
        var saved = _scope;
        _scope = scope;
        try
        {
            foreach (var s in statements)
            {
                if (s is FunctionDeclaration f) DeclareFunction(f);
                else if (s is VariableDeclaration d) DeclarePending(d);
            }
            var list = new List<E>();
            foreach (var s in statements) list.Add(Statement(s, inList));
            list.Add(Empty);
            return E.Block(typeof(void), scope.Vars, list);
        }
        finally
        {
            _scope = saved;
        }
    }

    // 문장이 하나뿐인 블록은 그 문장만 세고(JintBlockStatement의 한 문장 길), 아니면 블록도 하나로 센다
    private E Block(NestedBlockStatement block)
    {
        var scope = new Scope(_scope);
        if (block.Body.Count == 1) return StatementList(block.Body, scope, inList: false);
        return E.Block(typeof(void), Charge(block), StatementList(block.Body, scope, inList: true));
    }

    private E Statement(Statement statement, bool inList)
    {
        if (statement is NestedBlockStatement block) return Block(block);
        if (inList && statement is ReturnStatement { Argument: Literal and not RegExpLiteral } literal) return Return(literal);
        var charge = Charge(statement);
        E body;
        switch (statement)
        {
            case EmptyStatement:
            case FunctionDeclaration: // 첫머리에서 만들었다
            case ExpressionStatement { Expression: StringLiteral }: // 지시문
                body = Empty;
                break;
            case ExpressionStatement expression:
                body = Effect(Expr(expression.Expression, valueNeeded: false), expression.Expression);
                break;
            case VariableDeclaration declaration:
                body = Declaration(declaration);
                break;
            case IfStatement s:
            {
                var test = Cond(s.Test);
                var then = Statement(s.Consequent, inList: false);
                body = s.Alternate is null ? E.IfThen(test, then) : E.IfThenElse(test, then, Statement(s.Alternate, inList: false));
                break;
            }
            case ForStatement s:
                body = For(s);
                break;
            case WhileStatement s:
                body = Loop(s.Test, s.Body, null, null);
                break;
            case DoWhileStatement s:
                body = Loop(null, s.Body, null, s.Test);
                break;
            case BreakStatement { Label: null } when _loops.Count > 0:
                body = E.Break(_loops.Peek().Break);
                break;
            case ContinueStatement { Label: null } when _loops.Count > 0:
                body = E.Continue(_loops.Peek().Continue);
                break;
            case ReturnStatement r:
                body = Return(r);
                break;
            default:
                throw Syntax(statement);
        }
        return E.Block(typeof(void), charge, body);
    }

    private E Return(ReturnStatement statement)
    {
        var fn = _functions.Peek();
        if (!fn.ValueNeeded)
        {
            return statement.Argument is null
                ? E.Return(fn.Return)
                : E.Block(typeof(void), Effect(Expr(statement.Argument, valueNeeded: false), statement.Argument), E.Return(fn.Return));
        }
        if (statement.Argument is null) return E.Throw(E.New(typeof(FastDeoptException))); // undefined를 값으로 씀
        var v = Expr(statement.Argument);
        if (!v.IsValue) throw ValueError(statement.Argument);
        if (fn.Result is null)
        {
            fn.Result = E.Variable(v.Expr!.Type, fn.Name + "Result");
            fn.ResultKind = v.Kind;
        }
        else if (fn.ResultKind != v.Kind)
        {
            throw ValueError(statement.Argument);
        }
        return E.Block(typeof(void), E.Assign(fn.Result, v.Expr!), E.Return(fn.Return));
    }

    private E For(ForStatement s)
    {
        var scope = new Scope(_scope);
        var saved = _scope;
        _scope = scope;
        try
        {
            E init;
            switch (s.Init)
            {
                case null:
                    init = Empty;
                    break;
                case VariableDeclaration d:
                    DeclarePending(d);
                    init = E.Block(typeof(void), Charge(d), Declaration(d)); // 초기화 선언은 문장으로 실행된다
                    break;
                case Expression x:
                    init = Effect(Expr(x, valueNeeded: false), x);
                    break;
                default:
                    throw Syntax(s.Init);
            }
            var loop = Loop(s.Test, s.Body, s.Update, null);
            return E.Block(typeof(void), scope.Vars, init, loop);
        }
        finally
        {
            _scope = saved;
        }
    }

    private E Loop(Expression? test, Statement body, Expression? update, Expression? endTest)
    {
        var brk = E.Label("break");
        var cont = E.Label("continue");
        var parts = new List<E>();
        if (test is not null) parts.Add(E.IfThen(E.Not(Cond(test)), E.Break(brk)));
        _loops.Push((brk, cont));
        try
        {
            parts.Add(Statement(body, inList: false));
        }
        finally
        {
            _loops.Pop();
        }
        parts.Add(E.Label(cont));
        if (update is not null) parts.Add(Effect(Expr(update, valueNeeded: false), update));
        if (endTest is not null) parts.Add(E.IfThen(E.Not(Cond(endTest)), E.Break(brk)));
        return E.Loop(E.Block(typeof(void), parts), brk);
    }

    private E Declaration(VariableDeclaration declaration)
    {
        if (declaration.Kind is not (VariableDeclarationKind.Let or VariableDeclarationKind.Const)) throw Syntax(declaration);
        bool mutable = declaration.Kind == VariableDeclarationKind.Let;
        var list = new List<E>();
        foreach (var d in declaration.Declarations)
        {
            var name = d.Id is Identifier id ? id.Name : throw Syntax(d.Id);
            if (d.Init is null) throw ValueError(d.Id); // undefined
            Binding binding;
            if (d.Init is ArrowFunctionExpression or FunctionExpression)
            {
                binding = new Binding { Alias = new Val { Kind = Kind.Func, Fn = ToFunc(name, d.Init) } };
            }
            else if (_prologue && d.Init is ArrayExpression or ObjectExpression or StringLiteral or TemplateLiteral && IsInert(d.Init))
            {
                binding = new Binding { Alias = new Val { Kind = Kind.Opaque, Name = name } };
            }
            else
            {
                var v = Expr(d.Init);
                if (v.IsValue)
                {
                    var variable = E.Variable(v.Expr!.Type, name);
                    list.Add(E.Assign(variable, v.Expr));
                    binding = new Binding { Var = variable, Mutable = mutable };
                }
                else if (v.Kind is Kind.Void or Kind.Opaque)
                {
                    throw ValueError(d.Init);
                }
                else
                {
                    if (v.Pre is not null) list.Add(v.Pre);
                    binding = new Binding { Alias = WithoutPre(v) };
                }
            }
            _scope.Bind(name, binding);
        }
        list.Add(Empty);
        return E.Block(typeof(void), list);
    }

    // ───────────── 식 ─────────────

    private Val Expr(Expression node, bool valueNeeded = true)
    {
        switch (node)
        {
            case ParenthesizedExpression p:
                return Expr(p.Expression, valueNeeded);
            case NumericLiteral n:
                return Val.Num(E.Constant(LiteralValue(n)));
            case BooleanLiteral b:
                return Val.Bool(E.Constant(b.Value));
            case Identifier id:
                return Lookup(id);
            case MemberExpression m:
                return Member(m);
            case CallExpression c:
                return Call(c, valueNeeded);
            case LogicalExpression l:
                return Logical(l, valueNeeded);
            case NonLogicalBinaryExpression b:
                return Binary(b);
            case UpdateExpression u:
                return Update(u, valueNeeded);
            case NonUpdateUnaryExpression u:
                return Unary(u);
            case AssignmentExpression a:
                return Assign(a);
            case NewExpression n:
                return NewArray(n);
            case ConditionalExpression c:
            {
                var test = Cond(c.Test);
                var yes = Expr(c.Consequent, valueNeeded);
                var no = Expr(c.Alternate, valueNeeded);
                if (!valueNeeded) return Val.Void(E.IfThenElse(test, Effect(yes, c.Consequent), Effect(no, c.Alternate)));
                if (yes.Kind != no.Kind || !yes.IsValue) throw ValueError(c);
                return new Val { Kind = yes.Kind, Expr = E.Condition(test, yes.Expr!, no.Expr!) };
            }
            case SequenceExpression s:
            {
                var parts = new List<E>();
                for (int i = 0; i < s.Expressions.Count - 1; i++) parts.Add(Effect(Expr(s.Expressions[i], valueNeeded: false), s.Expressions[i]));
                var last = s.Expressions[^1];
                return WithPre(E.Block(typeof(void), [.. parts, Empty]), Expr(last, valueNeeded));
            }
            case StringLiteral or NullLiteral or ArrayExpression or ObjectExpression or TemplateLiteral:
                throw ValueError(node);
            default:
                throw Syntax(node);
        }
    }

    // Jint는 숫자 리터럴을 원문에서 다시 읽어 가장 가까운 double로 정한다(AstExtensions.NearestDouble). .NET 9 이상의 double.Parse와 같은 값
    private static double LiteralValue(NumericLiteral literal)
    {
        var raw = literal.Raw;
        bool fractionOrExponent = !(raw.Length > 1 && raw[0] == '0' && (raw[1] | 0x20) == 'x') && raw.AsSpan().IndexOfAny(".eE") >= 0;
        if (literal.Value < 9223372036854775808.0 && !fractionOrExponent) return literal.Value;
        if (raw.AsSpan().IndexOfAnyExcept("0123456789.eE+-") < 0 && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        throw Syntax(literal);
    }

    private Val Lookup(Identifier id)
    {
        if (_scope.Find(id.Name) is { } b)
        {
            if (b.Pending) throw new FastUnsupportedException(DomainStrings.FormatScript_FastBeforeDeclaration(name: id.Name), id);
            if (b.Var is { } v) return v.Type == typeof(double) ? Val.Num(v) : Val.Bool(v);
            return b.Alias!.Kind == Kind.Opaque ? throw ValueError(id) : b.Alias;
        }
        return id.Name switch
        {
            "Math" => new Val { Kind = Kind.Math },
            "NaN" => Val.Num(NaN),
            "Infinity" => Val.Num(E.Constant(double.PositiveInfinity)),
            "lerp" or "hsvToRgb" or "rgbToHsv" or "rgbToOklab" or "oklabToRgb" => new Val { Kind = Kind.Builtin, Name = id.Name },
            _ => throw NameError(id.Name, id),
        };
    }

    private Val Member(MemberExpression m)
    {
        if (m.Optional) throw Syntax(m);
        var obj = Expr(m.Object);
        string name;
        if (m.Computed)
        {
            if (obj.Kind is Kind.Data or Kind.Typed)
                return WithPre(obj.Pre, Val.Num(ReadArray(ArrayOf(obj), Number(Expr(m.Property), m.Property))));
            name = m.Property is StringLiteral key ? key.Value : throw Syntax(m);
        }
        else
        {
            name = m.Property is Identifier id ? id.Name : throw Syntax(m);
        }
        return WithPre(obj.Pre, Field(obj, name, m));
    }

    private Val Field(Val obj, string name, MemberExpression node)
    {
        switch (obj.Kind)
        {
            case Kind.Image when obj.Expr is { } other:
                // 다른 그림(img.blank, ctx.renderedFrameSource의 image): 크기와 data(JS 배열 — 바꾸지 않고 담음)
                return name switch
                {
                    "width" => Val.Num(E.Convert(E.Field(other, nameof(FastImage.Width)), typeof(double))),
                    "height" => Val.Num(E.Convert(E.Field(other, nameof(FastImage.Height)), typeof(double))),
                    "data" => new Val { Kind = Kind.Typed, Expr = E.Field(other, nameof(FastImage.Data)), Name = "0" },
                    _ => throw NameError(Describe(node), node),
                };
            case Kind.Image:
                return name switch
                {
                    "width" => Val.Num(Slot(_cv, CtxWidth)),
                    "height" => Val.Num(Slot(_cv, CtxHeight)),
                    "data" => new Val { Kind = Kind.Data },
                    _ => throw NameError(Describe(node), node),
                };
            case Kind.Data or Kind.Typed when name == "length":
                return Val.Num(E.Convert(E.ArrayLength(ArrayOf(obj)), typeof(double)));
            case Kind.Ctx or Kind.Record:
                return obj.Fields!.TryGetValue(name, out var field) ? field : throw NameError(Describe(node), node);
            case Kind.Math:
                return Val.Num(E.Constant(name switch
                {
                    // Jint MathInstance와 같은 식
                    "E" => Math.E,
                    "LN10" => Math.Log(10),
                    "LN2" => Math.Log(2),
                    "LOG10E" => Math.Log(Math.E, 10),
                    "LOG2E" => Math.Log(Math.E, 2),
                    "PI" => Math.PI,
                    "SQRT1_2" => Math.Sqrt(0.5),
                    "SQRT2" => Math.Sqrt(2),
                    _ => throw NameError(Describe(node), node),
                }));
            default:
                throw NameError(Describe(node), node);
        }
    }

    // img.data는 _data, 숫자 배열은 그 변수
    private E ArrayOf(Val obj) => obj.Kind == Kind.Data ? _data : obj.Expr!;

    // 숫자 배열에 넣을 때 바꾸는 종류(JsOps.Array*): img.data와 JS 배열 사본은 그대로
    private static int StoreKind(Val obj) => obj.Kind == Kind.Typed ? int.Parse(obj.Name!, CultureInfo.InvariantCulture) : JsOps.ArrayPlain;

    private static E ReadArray(E array, E index)
    {
        var i = E.Variable(typeof(double), "i");
        var k = E.Variable(typeof(int), "k");
        return E.Block(typeof(double), [i, k],
            E.Assign(i, index),
            E.Assign(k, E.Convert(i, typeof(int))),
            E.Condition(InRange(array, i, k), E.ArrayIndex(array, k), E.Throw(E.New(typeof(FastDeoptException)), typeof(double))));
    }

    /// <summary>
    /// 자리를 먼저 계산하고(JS 순서: 대상, 칸, 값), 칸이 배열 안 정수가 아니면 Jint로(img.data 밖 쓰기는 배열을 늘리고 typed array 밖 쓰기는 무시된다).
    /// 식의 값은 넣은 값(바꾸기 전), 칸에는 typed array 종류(<paramref name="kind"/>)로 바꾼 값이 담긴다.
    /// </summary>
    private static E WriteArray(E array, int kind, E index, Func<E, E> value)
    {
        var i = E.Variable(typeof(double), "i");
        var k = E.Variable(typeof(int), "k");
        var v = E.Variable(typeof(double), "v");
        var slot = E.ArrayAccess(array, k);
        E stored = kind is JsOps.ArrayInt32 or JsOps.ArrayUint8 ? E.Call(Op(nameof(JsOps.Store)), v, E.Constant(kind)) : v;
        return E.Block(typeof(double), [i, k, v],
            E.Assign(i, index),
            E.Assign(k, E.Convert(i, typeof(int))),
            E.IfThen(E.Not(InRange(array, i, k)), E.Throw(E.New(typeof(FastDeoptException)))),
            E.Assign(v, value(slot)),
            E.Assign(slot, stored),
            v);
    }

    // 정수이고 0 ≤ k < 길이. double → int 변환은 포화(.NET 9 이상: NaN = 0, 넘치면 끝값)라 k == i가 소수·NaN·범위 밖을 걸러 낸다. -0은 0번 칸(JS도 같음)
    private static E InRange(E array, ParameterExpression i, ParameterExpression k)
        => E.AndAlso(E.Equal(E.Convert(k, typeof(double)), i),
            E.LessThan(E.Convert(k, typeof(uint)), E.Convert(E.ArrayLength(array), typeof(uint))));

    /// <summary>
    /// <c>new Float64Array(n | img.data | 숫자 배열)</c>·<c>Int32Array</c>·<c>Uint8Array</c>: 호출 결과 변수에 담은 숫자 배열(종류에 맞게 바꾼 값).
    /// 길이가 0~최대 정수가 아니거나 메모리 한도를 넘으면 Jint로(Jint가 오류를 낸다).
    /// </summary>
    private Val NewArray(NewExpression n)
    {
        if (n.Callee is not Identifier { Name: "Float64Array" or "Int32Array" or "Uint8Array" } callee || _scope.Find(callee.Name) is not null
            || n.Arguments.Count != 1 || n.Arguments[0] is SpreadElement)
            throw Syntax(n);
        int kind = callee.Name switch { "Int32Array" => JsOps.ArrayInt32, "Uint8Array" => JsOps.ArrayUint8, _ => JsOps.ArrayFloat64 };
        var argument = Expr(n.Arguments[0]);
        E call = argument.Kind is Kind.Data or Kind.Typed
            ? E.Call(Op(nameof(JsOps.CopyArray)), _budget, ArrayOf(argument), E.Constant(kind))
            : E.Call(Op(nameof(JsOps.NewArray)), _budget, Number(argument, n.Arguments[0]), E.Constant(kind));
        return TypedResult(argument.Kind is Kind.Data or Kind.Typed ? argument.Pre : null, call, kind);
    }

    private Val TypedResult(E? pre, E call, int kind)
    {
        var array = E.Variable(typeof(double[]), "array");
        _hoisted.Add(array);
        E assign = E.Assign(array, call);
        return new Val
        {
            Kind = Kind.Typed, Expr = array, Name = kind.ToString(CultureInfo.InvariantCulture),
            Pre = pre is null ? assign : E.Block(typeof(void), pre, assign),
        };
    }

    /// <summary>숫자 배열의 메서드: <c>a.fill(v)</c>(인자 하나)만. <c>img.data.slice()</c>(인자 없음)는 JS 배열 사본.</summary>
    private Val ArrayCall(Val target, string name, CallExpression c, MemberExpression callee)
    {
        if (target.Kind == Kind.Data)
        {
            if (name != "slice" || c.Arguments.Count != 0) throw NameError(Describe(callee), callee);
            return TypedResult(null, E.Call(Op(nameof(JsOps.CopyArray)), _budget, _data, E.Constant(JsOps.ArrayPlain)), JsOps.ArrayPlain);
        }
        if (name != "fill" || c.Arguments.Count != 1 || c.Arguments[0] is SpreadElement) throw NameError(Describe(callee), callee);
        var value = Number(Expr(c.Arguments[0]), c.Arguments[0]);
        // Jint는 typed array fill 한 번을 문장 하나로 더 센다(배열 길이와 무관 — 측정)
        return new Val
        {
            Kind = Kind.Typed, Expr = target.Expr, Name = target.Name,
            Pre = E.Block(typeof(void), Charge(c), E.Call(Op(nameof(JsOps.Fill)), target.Expr!, value, E.Constant(StoreKind(target)))),
        };
    }

    /// <summary>
    /// 그림 메서드: <c>img.blank(w, h)</c>(호출 결과 변수의 새 그림), <c>dst.blendOver(src, x, y)</c>(src는 img나 다른 그림 — 같은 그림 dst를 돌려준다).
    /// </summary>
    private Val ImageCall(Val target, string name, CallExpression c, MemberExpression callee)
    {
        switch (name)
        {
            case "blank":
            {
                var values = Arguments(c.Arguments, 2, out var pre, out var temps);
                var image = E.Variable(typeof(FastImage), "blank");
                _hoisted.Add(image);
                _hoisted.AddRange(temps);
                E assign = E.Assign(image, E.Call(Op(nameof(JsOps.Blank)), _budget, values[0], values[1]));
                return new Val { Kind = Kind.Image, Expr = image, Pre = pre is null ? assign : E.Block(typeof(void), pre, assign) };
            }
            case "blendOver":
            {
                if (c.Arguments.Count < 1) throw ValueError(c);
                var src = Expr(c.Arguments[0]);
                if (src.Kind != Kind.Image) throw ValueError(c.Arguments[0]);
                var parts = new List<E>();
                if (src.Pre is not null) parts.Add(src.Pre);
                var temps = new List<ParameterExpression>();
                E Coordinate(int index)
                {
                    if (index >= c.Arguments.Count) return NaN;
                    var t = E.Variable(typeof(double), "at");
                    temps.Add(t);
                    parts.Add(E.Assign(t, Number(Expr(c.Arguments[index]), c.Arguments[index])));
                    return t;
                }
                var x = Coordinate(1);
                var y = Coordinate(2);
                for (int i = 3; i < c.Arguments.Count; i++) parts.Add(Effect(Expr(c.Arguments[i]), c.Arguments[i]));
                var (dst, dw, dh) = ImageParts(target);
                var (from, sw, sh) = ImageParts(src);
                parts.Add(E.Call(Op(nameof(JsOps.BlendOver)), dst, dw, dh, from, sw, sh, x, y));
                _hoisted.AddRange(temps);
                return new Val { Kind = Kind.Image, Expr = target.Expr, Pre = E.Block(typeof(void), parts) };
            }
            default:
                throw NameError(Describe(callee), callee);
        }
    }

    // 그림의 data·너비·높이 식(img는 data 배열과 ctx 칸)
    private (E Data, E Width, E Height) ImageParts(Val image)
        => image.Expr is { } other
            ? (E.Field(other, nameof(FastImage.Data)), E.Convert(E.Field(other, nameof(FastImage.Width)), typeof(double)),
                E.Convert(E.Field(other, nameof(FastImage.Height)), typeof(double)))
            : (_data, Slot(_cv, CtxWidth), Slot(_cv, CtxHeight));

    private Val Call(CallExpression c, bool valueNeeded)
    {
        if (c.Optional) throw Syntax(c);
        foreach (var argument in c.Arguments)
            if (argument is SpreadElement) throw Syntax(argument);
        if (c.Callee is MemberExpression { Computed: false, Optional: false, Property: Identifier method } callee)
        {
            var target = Expr(callee.Object);
            var result = target.Kind switch
            {
                Kind.Math => MathCall(method.Name, c, callee),
                Kind.Ctx => CtxCall(method.Name, c, callee),
                Kind.Data or Kind.Typed => ArrayCall(target, method.Name, c, callee),
                Kind.Image => ImageCall(target, method.Name, c, callee),
                _ => throw NameError(Describe(callee), callee),
            };
            return WithPre(target.Pre, result);
        }
        var fn = Expr(c.Callee);
        return fn.Kind switch
        {
            Kind.Func => WithPre(fn.Pre, Inline(fn.Fn!, c, valueNeeded)),
            Kind.Builtin => fn.Name switch
            {
                "lerp" => Lerp(c),
                "hsvToRgb" => HsvToRgb(c),
                "rgbToOklab" => RgbToOklab(c),
                "oklabToRgb" => OklabToRgb(c),
                _ => RgbToHsv(c),
            },
            _ => throw ValueError(c.Callee),
        };
    }

    /// <summary>
    /// 인자를 왼쪽부터 숫자로 계산한다. 앞 <paramref name="used"/>개를 돌려주고 빠진 인자는 NaN(<c>ToNumber(undefined)</c>).
    /// 남는 인자가 있으면 순서를 지키려고 모두 임시 변수에 담는다(<paramref name="pre"/>).
    /// </summary>
    private E[] Arguments(in NodeList<Expression> arguments, int used, out E? pre, out List<ParameterExpression> temps)
    {
        var values = new E[used];
        temps = [];
        pre = null;
        if (arguments.Count <= used)
        {
            for (int i = 0; i < used; i++) values[i] = i < arguments.Count ? Number(Expr(arguments[i]), arguments[i]) : NaN;
            return values;
        }
        var parts = new List<E>();
        for (int i = 0; i < arguments.Count; i++)
        {
            if (i < used)
            {
                var t = E.Variable(typeof(double), "arg" + i);
                temps.Add(t);
                parts.Add(E.Assign(t, Number(Expr(arguments[i]), arguments[i])));
                values[i] = t;
            }
            else
            {
                parts.Add(Effect(Expr(arguments[i]), arguments[i]));
            }
        }
        parts.Add(Empty);
        pre = E.Block(typeof(void), parts);
        return values;
    }

    private Val CallNumber(MethodInfo method, in NodeList<Expression> arguments, params E[] extra)
    {
        var values = Arguments(arguments, method.GetParameters().Length - extra.Length, out var pre, out var temps);
        E call = E.Call(method, [.. values, .. extra]);
        return Val.Num(pre is null ? call : E.Block(typeof(double), temps, pre, call));
    }

    private static MethodInfo Op(string name) => typeof(JsOps).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    private Val MathCall(string name, CallExpression c, MemberExpression callee)
    {
        switch (name)
        {
            case "min" or "max":
            {
                // 인자를 모두 숫자로 만든 뒤 차례로 비교(Jint Min/Max): 한 걸음씩 접어도 계산 순서는 같다
                var step = Op(name == "min" ? nameof(JsOps.MinStep) : nameof(JsOps.MaxStep));
                E acc = E.Constant(name == "min" ? double.PositiveInfinity : double.NegativeInfinity);
                foreach (var argument in c.Arguments) acc = E.Call(step, acc, Number(Expr(argument), argument));
                return Val.Num(acc);
            }
            case "hypot":
                return c.Arguments.Count <= 3 ? CallNumber(Op("Hypot" + c.Arguments.Count), c.Arguments) : throw NameError(Describe(callee), callee);
        }
        var method = name switch
        {
            "abs" => nameof(JsOps.Abs),
            "acos" => nameof(JsOps.Acos),
            "asin" => nameof(JsOps.Asin),
            "atan" => nameof(JsOps.Atan),
            "atan2" => nameof(JsOps.Atan2),
            "ceil" => nameof(JsOps.Ceil),
            "clz32" => nameof(JsOps.Clz32),
            "cos" => nameof(JsOps.Cos),
            "cosh" => nameof(JsOps.Cosh),
            "exp" => nameof(JsOps.Exp),
            "floor" => nameof(JsOps.Floor),
            "fround" => nameof(JsOps.Fround),
            "imul" => nameof(JsOps.Imul),
            "log" => nameof(JsOps.Log),
            "log10" => nameof(JsOps.Log10),
            "log2" => nameof(JsOps.Log2),
            "pow" => nameof(JsOps.Pow),
            "round" => nameof(JsOps.Round),
            "sign" => nameof(JsOps.Sign),
            "sin" => nameof(JsOps.Sin),
            "sinh" => nameof(JsOps.Sinh),
            "sqrt" => nameof(JsOps.Sqrt),
            "tan" => nameof(JsOps.Tan),
            "tanh" => nameof(JsOps.Tanh),
            "trunc" => nameof(JsOps.Trunc),
            _ => throw NameError(Describe(callee), callee),
        };
        return CallNumber(Op(method), c.Arguments);
    }

    private Val CtxCall(string name, CallExpression c, MemberExpression callee)
    {
        switch (name)
        {
            case "color":
                return ColorCall(c, callee, ScriptSettingType.Color, hasIndex: false, hasPart: false);
            case "colorAt":
                return ColorCall(c, callee, ScriptSettingType.ColorList, hasIndex: true, hasPart: false);
            case "pairColor":
                return ColorCall(c, callee, ScriptSettingType.ColorPairs, hasIndex: true, hasPart: true);
            case "hasGradient":
            {
                // 슬롯 이름은 문자열 리터럴만(컴파일할 때 설정·항목을 정한다). 잘못된 이름은 Jint가 오류를 낸다
                if (c.Arguments.Count < 1 || c.Arguments[0] is not StringLiteral slot) throw ValueError(c);
                if (c.Arguments.Count >= 2)
                {
                    // 목록 항목을 번호로: hasGradient("목록 키", 번호[, "from"|"to"]) — 키·쌍 칸은 리터럴, 번호는 숫자 식
                    int list = SchemaIndex(slot.Value);
                    if (list < 0 || !_schema[list].IsList) throw NameError(Describe(callee) + "(\"" + slot.Value + "\")", c);
                    bool pair = _schema[list].Type == ScriptSettingType.ColorPairs;
                    if (pair ? c.Arguments.Count != 3 || c.Arguments[2] is not StringLiteral { Value: "from" or "to" } : c.Arguments.Count != 2) throw ValueError(c);
                    bool toPart = pair && ((StringLiteral)c.Arguments[2]).Value == "to";
                    var index = Number(Expr(c.Arguments[1]), c.Arguments[1]);
                    return Val.Bool(E.Call(Op(nameof(JsOps.HasGradientAt)), _colors, E.Constant(list), index, E.Constant(toPart)));
                }
                if (!ScriptColorSlots.TryParse(_schema, slot.Value, out var setting, out int item, out bool to))
                    throw NameError(Describe(callee) + "(\"" + slot.Value + "\")", c);
                E call = E.Call(Op(nameof(JsOps.HasGradient)), _colors, E.Constant(SchemaIndex(setting.Key)), E.Constant(item), E.Constant(to));
                return Val.Bool(call);
            }
            case "hash":
                return CallNumber(Op(nameof(JsOps.Hash)), c.Arguments);
            case "random":
                return CallNumber(Op(nameof(JsOps.FrameRandom)), c.Arguments, Slot(_cv, CtxFrame));
            case "bayer2":
                return CallNumber(Op(nameof(JsOps.Bayer2)), c.Arguments);
            case "bayer4":
                return CallNumber(Op(nameof(JsOps.Bayer4)), c.Arguments);
            case "renderedFrameSource":
            {
                // 결과는 null일 수 있는 객체: if (!r)로 가린 뒤 r.image·r.offsetX·r.offsetY를 읽는다(null에서 읽으면 Jint로)
                var values = Arguments(c.Arguments, 1, out var pre, out var temps);
                var rendered = E.Variable(typeof(FastRendered), "rendered");
                _hoisted.Add(rendered);
                _hoisted.AddRange(temps);
                E assign = E.Assign(rendered, E.Call(Op(nameof(JsOps.RenderedFrame)), _budget, _colors, values[0]));
                var present = E.Call(Op(nameof(JsOps.Present)), rendered);
                return new Val
                {
                    Kind = Kind.Record, Name = Nullable, Expr = rendered,
                    Pre = pre is null ? assign : E.Block(typeof(void), pre, assign),
                    Fields = new(StringComparer.Ordinal)
                    {
                        ["image"] = new Val { Kind = Kind.Image, Expr = E.Field(present, nameof(FastRendered.Image)) },
                        ["offsetX"] = Val.Num(E.Convert(E.Field(present, nameof(FastRendered.OffsetX)), typeof(double))),
                        ["offsetY"] = Val.Num(E.Convert(E.Field(present, nameof(FastRendered.OffsetY)), typeof(double))),
                    },
                };
            }
            default:
                throw NameError(Describe(callee), callee);
        }
    }

    private int SchemaIndex(string key)
    {
        for (int i = 0; i < _schema.Count; i++)
            if (_schema[i].Key == key) return i;
        return -1;
    }

    /// <summary>
    /// <c>ctx.color(키, x, y[, 범위])</c>·<c>ctx.colorAt(키, 번호, x, y[, 범위])</c>·<c>ctx.pairColor(키, 번호, "from"|"to", x, y[, 범위])</c>:
    /// 키와 쌍 칸은 문자열 리터럴, 범위는 <c>{ x, y, w, h }</c> 객체 리터럴이나 그 네 필드를 가진 숫자 객체(<c>ctx.source</c> 등).
    /// 인자는 JS처럼 왼쪽부터 계산하고 고른 색을 호출 결과 변수에 담는다. 목록 번호가 항목 밖이면 Jint로(Jint가 오류를 낸다).
    /// </summary>
    private Val ColorCall(CallExpression c, MemberExpression callee, ScriptSettingType type, bool hasIndex, bool hasPart)
    {
        var args = c.Arguments;
        if (args.Count < 1 || args[0] is not StringLiteral key) throw ValueError(c);
        int setting = SchemaIndex(key.Value);
        if (setting < 0 || _schema[setting].Type != type) throw NameError(Describe(callee) + "(\"" + key.Value + "\")", c);
        int at = 1;
        bool to = false;
        var temps = new List<ParameterExpression>();
        var parts = new List<E>();
        E NumberArg(int index, string name)
        {
            if (index >= args.Count) return NaN;
            var t = E.Variable(typeof(double), name);
            temps.Add(t);
            parts.Add(E.Assign(t, Number(Expr(args[index]), args[index])));
            return t;
        }
        E item = hasIndex ? NumberArg(at++, "item") : E.Constant(-1.0);
        if (hasPart)
        {
            if (at >= args.Count || args[at] is not StringLiteral { Value: "from" or "to" } part) throw ValueError(c);
            to = part.Value == "to";
            at++;
        }
        var x = NumberArg(at++, "x");
        var y = NumberArg(at++, "y");
        E[]? bounds = null;
        if (at < args.Count)
        {
            bounds = new E[4];
            string[] names = ["x", "y", "w", "h"];
            if (args[at] is ObjectExpression literal)
            {
                // 속성을 쓴 차례대로 계산한다. 정확히 x, y, w, h 넷(겹치지 않음)만
                if (literal.Properties.Count != 4) throw ValueError(args[at]);
                foreach (var node in literal.Properties)
                {
                    if (node is not ObjectProperty { Kind: PropertyKind.Init, Computed: false, Method: false, Value: Expression value } property
                        || PropertyName(property.Key) is not { } field)
                        throw ValueError(args[at]);
                    int slot = Array.IndexOf(names, field);
                    if (slot < 0 || bounds[slot] is not null) throw ValueError(args[at]);
                    var t = E.Variable(typeof(double), "bound" + field);
                    temps.Add(t);
                    parts.Add(E.Assign(t, Number(Expr(value), value)));
                    bounds[slot] = t;
                }
            }
            else
            {
                var record = Expr(args[at]);
                if (record.Kind != Kind.Record || names.Any(n => record.Fields!.GetValueOrDefault(n) is not { Kind: Kind.Num })) throw ValueError(args[at]);
                if (record.Pre is not null) parts.Add(record.Pre);
                for (int i = 0; i < 4; i++)
                {
                    var t = E.Variable(typeof(double), "bound" + names[i]);
                    temps.Add(t);
                    parts.Add(E.Assign(t, record.Fields![names[i]].Expr!));
                    bounds[i] = t;
                }
            }
            at++;
        }
        for (int i = at; i < args.Count; i++) parts.Add(Effect(Expr(args[i]), args[i]));

        var color = E.Variable(typeof(Color32), "color");
        _hoisted.Add(color);
        _hoisted.AddRange(temps);
        E call = bounds is null
            ? E.Call(Op(nameof(JsOps.SampleColor)), _colors, E.Constant(setting), item, E.Constant(to), x, y)
            : E.Call(Op(nameof(JsOps.SampleColorIn)), [_colors, E.Constant(setting), item, E.Constant(to), x, y, .. bounds]);
        parts.Add(E.Assign(color, call));
        parts.Add(Empty);
        E Channel(string property) => E.Convert(E.Property(color, property), typeof(double));
        return new Val
        {
            Kind = Kind.Record,
            Pre = E.Block(typeof(void), parts),
            Fields = new(StringComparer.Ordinal)
            {
                ["r"] = Val.Num(Channel(nameof(Color32.R))),
                ["g"] = Val.Num(Channel(nameof(Color32.G))),
                ["b"] = Val.Num(Channel(nameof(Color32.B))),
                ["a"] = Val.Num(Channel(nameof(Color32.A))),
            },
        };
    }

    // lerp는 잠금 단계에서 넣은 JS 화살표 함수(a, b, t) => a + (b - a) * t: 부를 때 식 몸통 하나를 센다
    private Val Lerp(CallExpression c)
    {
        if (_depth + 2 > _limits.MaxRecursion) throw new FastUnsupportedException(DomainStrings.FormatScript_FastRecursion(name: "lerp"), c);
        var values = Arguments(c.Arguments, c.Arguments.Count, out _, out _);
        var a = E.Variable(typeof(double), "a");
        var b = E.Variable(typeof(double), "b");
        var t = E.Variable(typeof(double), "t");
        var parts = new List<E>();
        var vars = new List<ParameterExpression> { a, b, t };
        E[] targets = [a, b, t];
        for (int i = 0; i < values.Length; i++)
        {
            if (i < 3) parts.Add(E.Assign(targets[i], values[i]));
            else parts.Add(values[i]);
        }
        for (int i = values.Length; i < 3; i++) parts.Add(E.Assign(targets[i], NaN));
        parts.Add(Charge(c, deopt: true));
        parts.Add(E.Add(a, E.Multiply(E.Subtract(b, a), t)));
        return Val.Num(E.Block(typeof(double), vars, parts));
    }

    private Val HsvToRgb(CallExpression c)
    {
        int count = c.Arguments.Count;
        var values = Arguments(c.Arguments, 4, out var pre, out var temps);
        var color = E.Variable(typeof(Color32), "rgb");
        _hoisted.Add(color);
        _hoisted.AddRange(temps);
        E call = E.Call(Op(nameof(JsOps.HsvToRgb)), values[0], values[1], values[2], E.Constant(count > 3), values[3]);
        var assign = E.Assign(color, call);
        E Channel(string property) => E.Convert(E.Property(color, property), typeof(double));
        return new Val
        {
            Kind = Kind.Record,
            Pre = pre is null ? assign : E.Block(typeof(void), pre, assign),
            Fields = new(StringComparer.Ordinal)
            {
                ["r"] = Val.Num(Channel(nameof(Color32.R))),
                ["g"] = Val.Num(Channel(nameof(Color32.G))),
                ["b"] = Val.Num(Channel(nameof(Color32.B))),
                ["a"] = Val.Num(Channel(nameof(Color32.A))),
            },
        };
    }

    // rgbToOklab(r, g, b) → { l, a, b }(Pixelate.OkLab과 같은 float 계산)
    private Val RgbToOklab(CallExpression c)
    {
        var values = Arguments(c.Arguments, 3, out var pre, out var temps);
        var method = Op(nameof(JsOps.RgbToOklab));
        var lab = E.Variable(method.ReturnType, "lab");
        _hoisted.Add(lab);
        _hoisted.AddRange(temps);
        var assign = E.Assign(lab, E.Call(method, values));
        return new Val
        {
            Kind = Kind.Record,
            Pre = pre is null ? assign : E.Block(typeof(void), pre, assign),
            Fields = new(StringComparer.Ordinal)
            {
                ["l"] = Val.Num(E.Field(lab, "Item1")),
                ["a"] = Val.Num(E.Field(lab, "Item2")),
                ["b"] = Val.Num(E.Field(lab, "Item3")),
            },
        };
    }

    // oklabToRgb(l, a, b) → { r, g, b, a }(sRGB 밖이면 채도만 줄임, 알파 255)
    private Val OklabToRgb(CallExpression c)
    {
        var values = Arguments(c.Arguments, 3, out var pre, out var temps);
        var color = E.Variable(typeof(Color32), "rgb");
        _hoisted.Add(color);
        _hoisted.AddRange(temps);
        var assign = E.Assign(color, E.Call(Op(nameof(JsOps.OklabToRgb)), values));
        E Channel(string property) => E.Convert(E.Property(color, property), typeof(double));
        return new Val
        {
            Kind = Kind.Record,
            Pre = pre is null ? assign : E.Block(typeof(void), pre, assign),
            Fields = new(StringComparer.Ordinal)
            {
                ["r"] = Val.Num(Channel(nameof(Color32.R))),
                ["g"] = Val.Num(Channel(nameof(Color32.G))),
                ["b"] = Val.Num(Channel(nameof(Color32.B))),
                ["a"] = Val.Num(Channel(nameof(Color32.A))),
            },
        };
    }

    private Val RgbToHsv(CallExpression c)
    {
        var values = Arguments(c.Arguments, 3, out var pre, out var temps);
        var method = Op(nameof(JsOps.RgbToHsv));
        var hsv = E.Variable(method.ReturnType, "hsv");
        _hoisted.Add(hsv);
        _hoisted.AddRange(temps);
        var assign = E.Assign(hsv, E.Call(method, values));
        E Part(string field) => E.Convert(E.Field(hsv, field), typeof(double));
        return new Val
        {
            Kind = Kind.Record,
            Pre = pre is null ? assign : E.Block(typeof(void), pre, assign),
            Fields = new(StringComparer.Ordinal)
            {
                ["h"] = Val.Num(Part("Item1")),
                ["s"] = Val.Num(Part("Item2")),
                ["v"] = Val.Num(Part("Item3")),
            },
        };
    }

    private Val Logical(LogicalExpression l, bool valueNeeded)
    {
        if (l.Operator == Acornima.Operator.NullishCoalescing) throw Syntax(l);
        bool and = l.Operator == Acornima.Operator.LogicalAnd;
        if (!valueNeeded)
        {
            var test = Cond(l.Left);
            var effect = Effect(Expr(l.Right, valueNeeded: false), l.Right);
            return Val.Void(E.IfThen(and ? test : E.Not(test), effect));
        }
        var left = Expr(l.Left);
        var right = Expr(l.Right);
        if (left.Kind == Kind.Bool && right.Kind == Kind.Bool)
            return Val.Bool(and ? E.AndAlso(left.Expr!, right.Expr!) : E.OrElse(left.Expr!, right.Expr!));
        if (left.Kind == Kind.Num && right.Kind == Kind.Num)
        {
            // a && b = a가 참이면 b, 아니면 a(숫자 그대로)
            var t = E.Variable(typeof(double), "t");
            var test = Truthy(t);
            return Val.Num(E.Block(typeof(double), [t], E.Assign(t, left.Expr!),
                and ? E.Condition(test, right.Expr!, t) : E.Condition(test, t, right.Expr!)));
        }
        throw ValueError(l);
    }

    private Val Binary(NonLogicalBinaryExpression b)
    {
        var op = b.Operator;
        bool equality = op is Acornima.Operator.StrictEquality or Acornima.Operator.StrictInequality or Acornima.Operator.Equality or Acornima.Operator.Inequality;
        bool negate = op is Acornima.Operator.StrictInequality or Acornima.Operator.Inequality;
        if (equality && (b.Left is StringLiteral || b.Right is StringLiteral))
        {
            // 고르기 설정과 문자열 리터럴 비교: 값의 번호로(두 쪽 다 문자열이라 == 와 === 가 같다)
            var (other, literal) = b.Left is StringLiteral l ? (b.Right, l) : (b.Left, (StringLiteral)b.Right);
            var choice = Expr(other);
            if (choice.Kind != Kind.Choice) throw ValueError(b);
            E same = E.Equal(choice.Expr!, E.Constant((double)choice.Choices!.ToList().IndexOf(literal.Value)));
            return Val.Bool(negate ? E.Not(same) : same);
        }
        var left = Expr(b.Left);
        var right = Expr(b.Right);
        if (equality) return Equality(op, negate, left, right, b);
        E Num(Val v, Node at) => Number(v, at);
        E x = Num(left, b.Left), y = Num(right, b.Right);
        return op switch
        {
            Acornima.Operator.Addition => Val.Num(E.Add(x, y)),
            Acornima.Operator.Subtraction => Val.Num(E.Subtract(x, y)),
            Acornima.Operator.Multiplication => Val.Num(E.Multiply(x, y)),
            Acornima.Operator.Division => Val.Num(E.Divide(x, y)),
            Acornima.Operator.Remainder => Val.Num(E.Call(Op(nameof(JsOps.Remainder)), x, y)),
            Acornima.Operator.Exponentiation => Val.Num(E.Call(Op(nameof(JsOps.Exponentiate)), x, y)),
            Acornima.Operator.LessThan => Val.Bool(E.LessThan(x, y)),
            Acornima.Operator.LessThanOrEqual => Val.Bool(E.LessThanOrEqual(x, y)),
            Acornima.Operator.GreaterThan => Val.Bool(E.GreaterThan(x, y)),
            Acornima.Operator.GreaterThanOrEqual => Val.Bool(E.GreaterThanOrEqual(x, y)),
            _ => Val.Num(Bitwise(op, x, y) ?? throw Syntax(b)),
        };
    }

    private static E? Bitwise(Acornima.Operator op, E x, E y) => op switch
    {
        Acornima.Operator.BitwiseAnd => E.Convert(E.And(Int32(x), Int32(y)), typeof(double)),
        Acornima.Operator.BitwiseOr => E.Convert(E.Or(Int32(x), Int32(y)), typeof(double)),
        Acornima.Operator.BitwiseXor => E.Convert(E.ExclusiveOr(Int32(x), Int32(y)), typeof(double)),
        Acornima.Operator.LeftShift => E.Convert(E.LeftShift(Int32(x), Shift(y)), typeof(double)),
        Acornima.Operator.RightShift => E.Convert(E.RightShift(Int32(x), Shift(y)), typeof(double)),
        Acornima.Operator.UnsignedRightShift => E.Convert(E.RightShift(Uint32(x), Shift(y)), typeof(double)),
        _ => null,
    };

    private static E Int32(E x) => E.Call(Op(nameof(JsOps.ToInt32)), x);
    private static E Uint32(E x) => E.Call(Op(nameof(JsOps.ToUint32)), x);
    private static E Shift(E x) => E.And(Int32(x), E.Constant(31));

    private Val Equality(Acornima.Operator op, bool negate, Val left, Val right, Node node)
    {
        if (left.Kind == Kind.Num && right.Kind == Kind.Num)
            return Val.Bool(negate ? E.NotEqual(left.Expr!, right.Expr!) : E.Equal(left.Expr!, right.Expr!));
        if (left.Kind == Kind.Bool && right.Kind == Kind.Bool)
            return Val.Bool(negate ? E.NotEqual(left.Expr!, right.Expr!) : E.Equal(left.Expr!, right.Expr!));
        if (left.IsValue && right.IsValue)
        {
            // 숫자와 참거짓: ===는 종류가 달라 늘 다르고, ==는 참거짓을 숫자로 바꿔 비교
            if (op is Acornima.Operator.StrictEquality or Acornima.Operator.StrictInequality)
                return Val.Bool(E.Block(typeof(bool), left.Expr!, right.Expr!, E.Constant(negate)));
            E x = Number(left, node), y = Number(right, node);
            return Val.Bool(negate ? E.NotEqual(x, y) : E.Equal(x, y));
        }
        throw ValueError(node);
    }

    private Val Unary(NonUpdateUnaryExpression u)
    {
        switch (u.Operator)
        {
            case Acornima.Operator.UnaryNegation:
                return Val.Num(E.Negate(Number(Expr(u.Argument), u.Argument)));
            case Acornima.Operator.UnaryPlus:
                return Val.Num(Number(Expr(u.Argument), u.Argument));
            case Acornima.Operator.LogicalNot:
                return Val.Bool(E.Not(Cond(u.Argument)));
            case Acornima.Operator.BitwiseNot:
                return Val.Num(E.Convert(E.Not(Int32(Number(Expr(u.Argument), u.Argument))), typeof(double)));
            default:
                throw Syntax(u);
        }
    }

    private Val Update(UpdateExpression u, bool valueNeeded)
    {
        double delta = u.Operator == Acornima.Operator.Increment ? 1 : -1;
        switch (u.Argument)
        {
            case Identifier id:
            {
                var v = MutableNumber(id);
                if (!valueNeeded || u.Prefix) return Val.Num(delta > 0 ? E.PreIncrementAssign(v) : E.PreDecrementAssign(v));
                return Val.Num(delta > 0 ? E.PostIncrementAssign(v) : E.PostDecrementAssign(v));
            }
            case MemberExpression { Computed: true, Optional: false } m:
            {
                var obj = Expr(m.Object);
                if (obj.Kind is not (Kind.Data or Kind.Typed)) throw new FastUnsupportedException(DomainStrings.FormatScript_FastAssign(name: Describe(m)), m);
                var index = Number(Expr(m.Property), m.Property);
                var array = ArrayOf(obj);
                int kind = StoreKind(obj);
                if (u.Prefix || !valueNeeded) return WithPre(obj.Pre, Val.Num(WriteArray(array, kind, index, slot => E.Add(slot, E.Constant(delta)))));
                var old = E.Variable(typeof(double), "old");
                return WithPre(obj.Pre, Val.Num(E.Block(typeof(double), [old],
                    WriteArray(array, kind, index, slot => E.Add(E.Assign(old, slot), E.Constant(delta))), old)));
            }
            default:
                throw new FastUnsupportedException(DomainStrings.FormatScript_FastAssign(name: Describe(u.Argument)), u.Argument);
        }
    }

    private Val Assign(AssignmentExpression a)
    {
        Func<E, E, E>? combine = a.Operator switch
        {
            Acornima.Operator.Assignment => null,
            Acornima.Operator.AdditionAssignment => E.Add,
            Acornima.Operator.SubtractionAssignment => E.Subtract,
            Acornima.Operator.MultiplicationAssignment => E.Multiply,
            Acornima.Operator.DivisionAssignment => E.Divide,
            Acornima.Operator.RemainderAssignment => (x, y) => E.Call(Op(nameof(JsOps.Remainder)), x, y),
            Acornima.Operator.ExponentiationAssignment => (x, y) => E.Call(Op(nameof(JsOps.Exponentiate)), x, y),
            Acornima.Operator.BitwiseAndAssignment => (x, y) => Bitwise(Acornima.Operator.BitwiseAnd, x, y)!,
            Acornima.Operator.BitwiseOrAssignment => (x, y) => Bitwise(Acornima.Operator.BitwiseOr, x, y)!,
            Acornima.Operator.BitwiseXorAssignment => (x, y) => Bitwise(Acornima.Operator.BitwiseXor, x, y)!,
            Acornima.Operator.LeftShiftAssignment => (x, y) => Bitwise(Acornima.Operator.LeftShift, x, y)!,
            Acornima.Operator.RightShiftAssignment => (x, y) => Bitwise(Acornima.Operator.RightShift, x, y)!,
            Acornima.Operator.UnsignedRightShiftAssignment => (x, y) => Bitwise(Acornima.Operator.UnsignedRightShift, x, y)!,
            _ => throw Syntax(a),
        };
        switch (a.Left)
        {
            case Identifier id:
            {
                var b = _scope.Find(id.Name);
                if (b is { Pending: true }) throw new FastUnsupportedException(DomainStrings.FormatScript_FastBeforeDeclaration(name: id.Name), id);
                if (b?.Var is not { } variable || !b.Mutable) throw new FastUnsupportedException(DomainStrings.FormatScript_FastAssign(name: id.Name), id);
                var right = Expr(a.Right);
                if (variable.Type == typeof(bool))
                {
                    // 참거짓 변수에는 참거짓만(숫자를 넣으면 변수의 종류가 바뀐다)
                    if (combine is not null || right.Kind != Kind.Bool) throw ValueError(a);
                    return Val.Bool(E.Assign(variable, right.Expr!));
                }
                if (combine is null)
                {
                    if (right.Kind != Kind.Num) throw ValueError(a);
                    return Val.Num(E.Assign(variable, right.Expr!));
                }
                return Val.Num(E.Assign(variable, combine(variable, Number(right, a.Right))));
            }
            case MemberExpression { Computed: true, Optional: false } m:
            {
                var obj = Expr(m.Object);
                if (obj.Kind is not (Kind.Data or Kind.Typed)) throw new FastUnsupportedException(DomainStrings.FormatScript_FastAssign(name: Describe(m)), m);
                var index = Number(Expr(m.Property), m.Property);
                var right = Expr(a.Right);
                if (combine is null && right.Kind != Kind.Num) throw ValueError(a); // 숫자가 아닌 값을 넣으면 되쓰기가 달라진다
                var value = Number(right, a.Right);
                return WithPre(obj.Pre, Val.Num(WriteArray(ArrayOf(obj), StoreKind(obj), index, slot => combine is null ? value : combine(slot, value))));
            }
            default:
                throw new FastUnsupportedException(DomainStrings.FormatScript_FastAssign(name: Describe(a.Left)), a.Left);
        }
    }

    private ParameterExpression MutableNumber(Identifier id)
    {
        var b = _scope.Find(id.Name);
        if (b is { Pending: true }) throw new FastUnsupportedException(DomainStrings.FormatScript_FastBeforeDeclaration(name: id.Name), id);
        if (b?.Var is not { } variable || !b.Mutable || variable.Type != typeof(double))
            throw new FastUnsupportedException(DomainStrings.FormatScript_FastAssign(name: id.Name), id);
        return variable;
    }

    // ───────────── 값 변환 ─────────────

    private static E Number(Val v, Node at) => v.Kind switch
    {
        Kind.Num => v.Expr!,
        Kind.Bool => E.Condition(v.Expr!, E.Constant(1.0), E.Constant(0.0)),
        _ => throw ValueError(at),
    };

    /// <summary>조건 자리: 논리 연산과 <c>!</c>는 값 종류가 섞여도 참거짓만 보면 된다(<c>ToBoolean(a &amp;&amp; b) = ToBoolean(a) &amp;&amp; ToBoolean(b)</c>).</summary>
    private E Cond(Expression node)
    {
        switch (node)
        {
            case ParenthesizedExpression p:
                return Cond(p.Expression);
            case LogicalExpression { Operator: Acornima.Operator.LogicalAnd } l:
                return E.AndAlso(Cond(l.Left), Cond(l.Right));
            case LogicalExpression { Operator: Acornima.Operator.LogicalOr } l:
                return E.OrElse(Cond(l.Left), Cond(l.Right));
            case NonUpdateUnaryExpression { Operator: Acornima.Operator.LogicalNot } u:
                return E.Not(Cond(u.Argument));
        }
        var v = Expr(node);
        switch (v.Kind)
        {
            case Kind.Bool:
                return v.Expr!;
            case Kind.Num:
                if (v.Expr is ParameterExpression p) return Truthy(p);
                var t = E.Variable(typeof(double), "t");
                return E.Block(typeof(bool), [t], E.Assign(t, v.Expr!), Truthy(t));
            case Kind.Record when v.Name == Nullable:
                return v.Pre is null ? E.NotEqual(v.Expr!, E.Constant(null)) : E.Block(typeof(bool), v.Pre, E.NotEqual(v.Expr!, E.Constant(null)));
            default:
                throw ValueError(node);
        }
    }

    // 0, -0, NaN만 거짓
    private static E Truthy(ParameterExpression t) => E.AndAlso(E.NotEqual(t, E.Constant(0.0)), E.Equal(t, t));

    /// <summary>값을 버리는 자리: 부수 효과만 남긴다.</summary>
    private static E Effect(Val v, Node at) => v.Kind switch
    {
        Kind.Num or Kind.Bool or Kind.Void or Kind.Choice => v.Expr!,
        Kind.Opaque => throw ValueError(at),
        _ => v.Pre ?? Empty,
    };

    private static Val WithPre(E? pre, Val v)
    {
        if (pre is null) return v;
        if (v.Kind is Kind.Num or Kind.Bool or Kind.Void or Kind.Choice)
            return new Val { Kind = v.Kind, Expr = E.Block(v.Expr!.Type, pre, v.Expr), Choices = v.Choices, Name = v.Name };
        var copy = WithoutPre(v);
        copy.Pre = v.Pre is null ? pre : E.Block(typeof(void), pre, v.Pre);
        return copy;
    }

    private static Val WithoutPre(Val v) => new() { Kind = v.Kind, Expr = v.Expr, Fields = v.Fields, Fn = v.Fn, Name = v.Name, Choices = v.Choices };
}
