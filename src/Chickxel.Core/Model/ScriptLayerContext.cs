using System.Drawing;
using Chickxel.Core.Edit;

namespace Chickxel.Core.Model;

/// <summary>
/// 스크립트 효과가 그리는 프레임에서 보는 효과 원본 배치와 다른 레이어(<c>ctx.slice</c>, 레이어 고르기 설정(<c>type: "layer"</c>)으로 고른 레이어를 읽는
/// <c>ctx.layer</c>·<c>ctx.layerWithEffects</c>·<c>ctx.layerEffects</c>).
/// 프로젝트 렌더가 순서표를 만들 때(부르는 스레드에서) 프로젝트 상태를 담아 두므로, 다른 스레드에서 그려도 프로젝트를 읽지 않는다.
/// <para>
/// 고른 레이어: 이 레이어의 켜진 스크립트 효과가 레이어 고르기 설정에 적은 이름마다 레이어 하나 — 레이어 목록에서 가장 가까운 것(같은 거리면 아래쪽)이되
/// 이 효과의 레이어와 그 레이어를 담은 그룹은 찾지 않는다(자기 그림은 이미 <c>img</c>이고 담은 그룹 그림에는 자기가 들어 있어 되풀이가 된다). 그래서 자기 이름을
/// 고르면 같은 이름의 다른 레이어가 있을 때만 그것을 준다. 숨긴 레이어도 찾는다(보이기와 관계없이 그 레이어의 그림을 준다 — 숨긴 레이어를 재료로 쓸 수 있다).
/// 고른 이름만 담으므로 다른 레이어가 바뀌어도 이 효과 결과는 그대로다.
/// </para>
/// <para>
/// 그림은 레이어 불투명도를 곱하지 않은 셀 그대로(그룹이면 보이는 부위를 부위 불투명도·부위 효과로 합친 그림 — <see cref="SpriteProject.EffectSource"/>와 같음)이고,
/// 효과까지 함수는 그 레이어 효과(그룹이면 그룹 효과)를 이 프레임 그대로 입힌 그림이다. 둘 다 이 효과의 그림(<c>img</c>)과 같은 크기·같은 자리로
/// 맞춘다(<c>img.data[i]</c>와 같은 캔버스 픽셀, 밖은 투명). 셀이 없으면(그룹이면 그릴 부위가 없으면) null.
/// </para>
/// <para>
/// 겹침 규칙(되풀이 없음): 이 스크립트를 위해 다른 레이어를 그릴 때(<c>ctx.layerWithEffects</c>의 효과 그리기와, 부위 효과가 들어가는 그룹 원본), 그 안에서 다른 레이어를
/// 효과까지 입혀 읽는 스크립트 효과(<see cref="Scripting.ScriptInfo.RenderedLayerNames"/>를 쓰는 효과)는 꺼진 효과처럼 건너뛴다(한 단계). 그래서 서로를 효과까지 읽는
/// 두 레이어는 상대의 그 효과를 뺀 모습을 보고, 결과는 늘 같다. 효과 없는 원본만 읽는 효과는 안에서도 그대로 그린다(원본 읽기는 셀에서 끝나므로 되풀이가 없다).
/// 다른 레이어를 그리는 안쪽(겹침 규칙의 한 단계 안)에서는 고른 그룹의 그림을 주지 않는다(null — 그룹 원본이 다시 그룹을 읽는 되풀이를 끊는다).
/// 잔상이 그리는 앞 프레임 모습 안에서는 다른 레이어를 주지 않는다(<c>ctx.frameSource</c>와 같음 — 그 안의 그림 함수는 null, 효과 이름은 빈 배열, <c>ctx.slice</c>는 null).
/// </para>
/// <para>
/// 효과 이름: 고른 레이어의 효과 중 이 프레임에 켜진(켜져 있고 프레임 범위 안, 아는 효과) 것의 화면 이름(<see cref="LayerEffect.DisplayName"/> —
/// 스크립트 효과는 선언 이름, 기본 효과는 앱 언어 이름), 목록 순서.
/// </para>
/// </summary>
public sealed class ScriptLayerContext
{
    private readonly Point _origin;
    private readonly IReadOnlyDictionary<string, LayerNeighbor> _named;
    private readonly IReadOnlyDictionary<string, string[]> _effects;
    private readonly RenderPlanOptions _options;
    private readonly Dictionary<(string Name, bool Effects, int Width, int Height, Rectangle Source), PixelImage?> _images = [];

    internal ScriptLayerContext(SpriteBorder? slice, Point origin, IReadOnlyDictionary<string, LayerNeighbor> named, IReadOnlyDictionary<string, string[]> effects)
    {
        Slice = slice;
        _origin = origin;
        _named = named;
        _effects = effects;
        _options = RenderPlanOptions.Default;
        var items = new List<object?> { "layers", slice, origin };
        foreach (var (name, names) in effects.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            items.Add(name);
            items.Add(names.Length);
            items.AddRange(names);
        }
        foreach (var (name, neighbor) in named.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            items.Add(name);
            items.Add(neighbor.Key);
        }
        Key = new CacheKey([.. items]);
    }

    private ScriptLayerContext(ScriptLayerContext from, RenderPlanOptions options)
    {
        Slice = from.Slice;
        _origin = from._origin;
        _named = from._named;
        _effects = from._effects;
        Key = from.Key;
        _options = options;
    }

    /// <summary>효과 원본의 9-slice 테두리(<c>ctx.slice</c>, px). 없거나 원본 크기에 맞지 않으면 null.</summary>
    public SpriteBorder? Slice { get; }

    /// <summary>담은 내용(테두리·고른 레이어의 셀과 효과·효과 이름) 열쇠 — 이 내용이 바뀌면 이 레이어 효과 결과의 캐시 열쇠도 바뀐다.</summary>
    internal CacheKey Key { get; }

    /// <summary>렌더링 한 번(<paramref name="run"/>의 취소·캐시)에 쓰는 사본. 효과까지 입힌 레이어 그림은 이 사본 안에서 한 번만 그린다.</summary>
    internal ScriptLayerContext Bind(RenderRun run) => new(this, new RenderPlanOptions { Cancellation = run.Cancellation, Cache = run.Cache });

    /// <summary>
    /// 레이어 고르기 설정으로 고른 이름 <paramref name="name"/>의 레이어 그림(<paramref name="withEffects"/>면 효과까지)을 <paramref name="width"/>×<paramref name="height"/>
    /// 그림에 맞춘 것 — 이 효과의 원본은 그 그림의 <paramref name="source"/>에 있다. 그런 레이어나 셀이 없으면 null. 돌려준 그림은 읽기만 한다.
    /// </summary>
    internal PixelImage? Named(string name, bool withEffects, int width, int height, Rectangle source)
    {
        if (!_named.TryGetValue(name, out var neighbor)) return null;
        var key = (name, withEffects, width, height, source);
        lock (_images)
            if (_images.TryGetValue(key, out var known)) return known;
        var image = neighbor.Render(withEffects, width, height, source.X - _origin.X, source.Y - _origin.Y, _options);
        lock (_images) _images[key] = image;
        return image;
    }

    /// <summary>고른 이름 <paramref name="name"/>의 레이어에서 이 프레임에 켜진 효과 이름. 없으면 빈 목록.</summary>
    internal IReadOnlyList<string> EffectsByName(string name) => _effects.TryGetValue(name, out var names) ? names : [];
}

/// <summary>
/// 스크립트가 보는 고른 레이어 하나(순서표를 만들 때 담음): 효과 없는 원본(일반·부위 셀과 그 캔버스 자리, 그룹이면 부위 합성 순서표)과
/// 효과까지 입혀 그리는 단계(불투명도 255, 캔버스 좌표 — 겹침 규칙으로 다른 레이어를 효과까지 읽는 효과는 뺀 것).
/// </summary>
internal sealed class LayerNeighbor
{
    private readonly Layer _layer;
    private readonly int _frame;
    private readonly PixelImage? _cel;
    private readonly Point _celOrigin;
    private readonly RenderPlan? _groupSource;
    private readonly RenderStep? _rendered;

    public LayerNeighbor(Layer layer, int frame, PixelImage? cel, Point celOrigin, RenderPlan? groupSource, RenderStep? rendered)
    {
        _layer = layer;
        _frame = frame;
        _cel = cel;
        _celOrigin = celOrigin;
        _groupSource = groupSource;
        _rendered = rendered;
        Key = new CacheKey("neighbor", layer, frame, cel, celOrigin, groupSource?.ContentKey, rendered?.Key);
    }

    public CacheKey Key { get; }

    /// <summary>
    /// 캔버스 (0, 0)이 결과의 (<paramref name="shiftX"/>, <paramref name="shiftY"/>)에 오도록 <paramref name="width"/>×<paramref name="height"/>에 그린 그림. 그릴 것이 없으면 null.
    /// </summary>
    public PixelImage? Render(bool withEffects, int width, int height, int shiftX, int shiftY, RenderPlanOptions options)
    {
        if (withEffects)
        {
            if (_rendered is null) return null;
            var target = new PixelImage(width, height);
            _rendered.Run(new RenderRun(target, options, _rendered.Units), target, shiftX, shiftY, null);
            return target;
        }
        if (_cel is not null)
        {
            var target = new PixelImage(width, height);
            target.Blit(_cel, _celOrigin.X + shiftX, _celOrigin.Y + shiftY);
            return target;
        }
        if (_groupSource is not { IsEmpty: false } plan) return null;
        var canvas = options.Cache is { } cache ? cache.GroupSource(_layer, _frame, plan, () => plan.Render(options)) : plan.Render(options);
        var aligned = new PixelImage(width, height);
        aligned.Blit(canvas, shiftX, shiftY);
        return aligned;
    }
}
