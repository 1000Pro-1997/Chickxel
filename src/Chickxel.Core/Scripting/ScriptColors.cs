using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>
/// render 한 번이 쓰는 색 설정 읽기(<c>ctx.color</c>·<c>ctx.colorAt</c>·<c>ctx.pairColor</c>·<c>ctx.hasGradient</c>). Jint와 빠른 경로가 같은 것을 쓴다.
/// <para>
/// 그라데이션이 없는 슬롯은 설정의 단색 그대로다. 있으면 기본 효과와 같은 <see cref="EffectGradientSampler"/>로 (x, y)에서 고른다:
/// 범위는 따로 주지 않으면 효과를 입히기 전 그림의 불투명 범위(기본 효과의 색 설정과 같음 — 처음 쓸 때 한 번 잰다), 디더 무늬는 효과 원본 왼쪽 위 기준,
/// 움직이는 그라데이션의 시간은 <see cref="ScriptFrameContext.GradientTime"/>(효과의 범위 안 진행)에 그라데이션 자신의 곡선·반복을 넣은 값이다.
/// </para>
/// <para>
/// 설정 번호는 schema 차례, 항목 번호는 목록 설정의 칸(색은 -1)이다. 한 스레드에서만 쓴다(render 한 번 = 한 스레드).
/// </para>
/// </summary>
internal sealed class ScriptColors
{
    private sealed class Slot(Color32 solid, EffectColorGradient? gradient)
    {
        public readonly Color32 Solid = solid;
        public readonly EffectColorGradient? Gradient = gradient;
        public bool HasDefault;
        public EffectGradientSampler Default;
        public bool HasLast;
        public Rectangle LastBounds;
        public EffectGradientSampler Last;
    }

    private readonly IReadOnlyList<ScriptSetting> _schema;
    private readonly Slot[]?[] _slots;
    private readonly int[] _counts;
    private readonly double _time;
    private readonly PixelImage? _image;
    private readonly int _sourceX, _sourceY;
    private Rectangle? _bounds;

    /// <summary>색 설정이 없는 빈 표(시험에서 설정 없이 ctx를 만들 때).</summary>
    public static ScriptColors None { get; } = new([], new Dictionary<string, object>(), ScriptFrameContext.Still, null, default);

    /// <param name="values"><see cref="ScriptEffectProgram.NormalizeSettings"/>를 거친 값.</param>
    /// <param name="image">효과를 입히기 전 그림(기본 범위를 잰다 — render가 끝날 때까지 바뀌지 않는다).</param>
    /// <param name="source">효과 원본 자리(디더 무늬 기준).</param>
    public ScriptColors(IReadOnlyList<ScriptSetting> schema, IReadOnlyDictionary<string, object> values, ScriptFrameContext frame, PixelImage? image, Rectangle source)
    {
        _schema = schema;
        _slots = new Slot[]?[schema.Count];
        _counts = new int[schema.Count];
        _time = frame.GradientTime;
        _image = image;
        _sourceX = source.X;
        _sourceY = source.Y;
        var gradients = frame.Gradients;
        EffectColorGradient? Gradient(string slot) => gradients.Count != 0 && gradients.TryGetValue(slot, out var g) ? g : null;
        Frame = frame;
        Source = source;
        for (int i = 0; i < schema.Count; i++)
        {
            var setting = schema[i];
            switch (setting.Type)
            {
                case ScriptSettingType.Color:
                    _slots[i] = [new((Color32)values[setting.Key], Gradient(setting.Key))];
                    _counts[i] = 1;
                    break;
                case ScriptSettingType.ColorList:
                {
                    var colors = (IReadOnlyList<Color32>)values[setting.Key];
                    var slots = new Slot[colors.Count];
                    for (int k = 0; k < slots.Length; k++) slots[k] = new(colors[k], Gradient(ScriptColorSlots.Item(setting.Key, k)));
                    _slots[i] = slots;
                    _counts[i] = colors.Count;
                    break;
                }
                case ScriptSettingType.ColorPairs:
                {
                    var pairs = (IReadOnlyList<ScriptColorPair>)values[setting.Key];
                    var slots = new Slot[pairs.Count * 2];
                    for (int k = 0; k < pairs.Count; k++)
                    {
                        slots[k * 2] = new(pairs[k].From, Gradient(ScriptColorSlots.PairItem(setting.Key, k, to: false)));
                        slots[k * 2 + 1] = new(pairs[k].To, Gradient(ScriptColorSlots.PairItem(setting.Key, k, to: true)));
                    }
                    _slots[i] = slots;
                    _counts[i] = pairs.Count;
                    break;
                }
            }
        }
    }

    /// <summary>이 render의 프레임 정보(빠른 경로의 <c>ctx.renderedFrameSource</c>가 읽는다).</summary>
    public ScriptFrameContext Frame { get; }

    /// <summary>효과 원본 자리(<c>ctx.source</c>).</summary>
    public Rectangle Source { get; }

    /// <summary>미루는 스크립트인지(<c>ctx.renderedFrameSource</c>를 줄지).</summary>
    public bool Deferred { get; init; }

    /// <summary>설정 <paramref name="key"/>의 schema 번호(그 종류가 아니면 -1).</summary>
    public int IndexOf(string key, ScriptSettingType type)
    {
        for (int i = 0; i < _schema.Count; i++)
            if (_schema[i].Key == key) return _schema[i].Type == type ? i : -1;
        return -1;
    }

    /// <summary>목록 설정 <paramref name="setting"/>의 항목 수(색은 1).</summary>
    public int Count(int setting) => _counts[setting];

    /// <summary>항목 번호가 그 설정 안인지(목록은 0 ≤ 번호 &lt; 항목 수, 색은 -1).</summary>
    public bool Has(int setting, int item) => _schema[setting].Type == ScriptSettingType.Color ? item == -1 : (uint)item < (uint)_counts[setting];

    /// <summary>슬롯에 그라데이션이 있는지(<see cref="Has"/>가 참인 자리).</summary>
    public bool HasGradient(int setting, int item, bool to) => SlotOf(setting, item, to).Gradient is not null;

    /// <summary>슬롯의 단색(그라데이션과 관계없이 설정 값).</summary>
    public Color32 Solid(int setting, int item, bool to) => SlotOf(setting, item, to).Solid;

    /// <summary>슬롯의 (x, y) 색: 그라데이션이 없으면 단색, 있으면 효과 전 그림의 불투명 범위로 고른 색.</summary>
    public Color32 Sample(int setting, int item, bool to, int x, int y)
    {
        var slot = SlotOf(setting, item, to);
        if (slot.Gradient is null) return slot.Solid;
        if (!slot.HasDefault)
        {
            _bounds ??= _image?.GetOpaqueBounds() ?? Rectangle.Empty;
            slot.Default = EffectGradientSampler.For(slot.Gradient, _bounds.Value, slot.Solid, _sourceX, _sourceY, _time);
            slot.HasDefault = true;
        }
        return slot.Default.Sample(x, y);
    }

    /// <summary>슬롯의 (x, y) 색을 범위 <paramref name="bounds"/>로 고른다(고리·반사 연장 등 기본 효과가 다른 사각형을 쓰는 자리). 마지막 범위의 샘플러는 다시 쓴다.</summary>
    public Color32 Sample(int setting, int item, bool to, int x, int y, Rectangle bounds)
    {
        var slot = SlotOf(setting, item, to);
        if (slot.Gradient is null) return slot.Solid;
        if (!slot.HasLast || slot.LastBounds != bounds)
        {
            slot.Last = EffectGradientSampler.For(slot.Gradient, bounds, slot.Solid, _sourceX, _sourceY, _time);
            slot.LastBounds = bounds;
            slot.HasLast = true;
        }
        return slot.Last.Sample(x, y);
    }

    private Slot SlotOf(int setting, int item, bool to)
        => _schema[setting].Type switch
        {
            ScriptSettingType.Color => _slots[setting]![0],
            ScriptSettingType.ColorList => _slots[setting]![item],
            _ => _slots[setting]![item * 2 + (to ? 1 : 0)],
        };
}
