using System.Drawing;
using System.Globalization;
using System.Text;

namespace Chickxel.Core.Assets;

/// <summary>
/// 선언의 <c>previewMotion</c>: 미리보기 칸마다 병아리를 옮기는 양(효과 만들기 미리보기·검증·올릴 때 미리보기만 — 문서와는 상관없음).
/// 이름(<see cref="Presets"/> — 칸 k에서 그 쪽으로 k px) 또는 칸마다 적은 <c>[x, y]</c> 목록(목록보다 뒤 칸은 마지막 값, 넘치는 값은 안 씀)이다.
/// 없거나 <c>"none"</c>이면 움직이지 않는다(<see cref="None"/>). 값이 같으면 같은 것으로 본다(<see cref="Literal"/>).
/// </summary>
public sealed class PreviewMotion : IEquatable<PreviewMotion>
{
    /// <summary>목록 값 하나(x, y 각각)의 절댓값 한도(px).</summary>
    public const int MaxOffset = 32;

    /// <summary>이름으로 적는 움직임(차례 = 효과 만들기 고르기 차례).</summary>
    public static IReadOnlyList<string> Presets { get; } = ["none", "right", "left", "up", "down"];

    /// <summary>움직이지 않음(<c>"none"</c>, 선언에 없을 때).</summary>
    public static PreviewMotion None { get; } = new("none", []);

    private PreviewMotion(string? preset, IReadOnlyList<Point> offsets)
    {
        Preset = preset;
        Offsets = offsets;
    }

    /// <summary>이름으로 적은 움직임(<see cref="Presets"/> 중 하나). 목록이면 null.</summary>
    public string? Preset { get; }

    /// <summary>칸마다 적은 자리 목록(이름이면 빈 목록).</summary>
    public IReadOnlyList<Point> Offsets { get; }

    /// <summary>어느 칸에서도 움직이지 않는지(<c>"none"</c> 또는 모두 0인 목록).</summary>
    public bool IsNone => Preset == "none" || Preset is null && Offsets.All(o => o.IsEmpty);

    /// <summary>이름의 움직임. 모르는 이름이면 null.</summary>
    public static PreviewMotion? FromPreset(string name) => name switch
    {
        "none" => None,
        "right" or "left" or "up" or "down" => new(name, []),
        _ => null,
    };

    /// <summary>
    /// 칸마다 적은 자리 목록(1~<see cref="Scripting.ScriptEffectProgram.MaxPreviewFrames"/>개, 값은 ±<see cref="MaxOffset"/>). 아니면 null.
    /// </summary>
    public static PreviewMotion? FromOffsets(IEnumerable<Point> offsets)
    {
        var list = offsets.ToList();
        if (list.Count is < 1 or > Scripting.ScriptEffectProgram.MaxPreviewFrames || !list.All(IsValid)) return null;
        return new(null, list);
    }

    /// <summary>
    /// <paramref name="steps"/>칸 미리보기에서 칸마다 <paramref name="offsets"/>로 움직이는 가장 짧은 선언: 모두 0이면 <see cref="None"/>,
    /// 이름 하나와 칸마다 같으면 그 이름, 아니면 목록(값을 ±<see cref="MaxOffset"/>로 자름, 빈 목록 = <see cref="None"/>).
    /// </summary>
    public static PreviewMotion Simplest(IReadOnlyList<Point> offsets, int steps)
    {
        if (offsets.Count == 0) return None;
        var list = offsets.Take(Scripting.ScriptEffectProgram.MaxPreviewFrames)
            .Select(o => new Point(Math.Clamp(o.X, -MaxOffset, MaxOffset), Math.Clamp(o.Y, -MaxOffset, MaxOffset))).ToList();
        var manual = new PreviewMotion(null, list);
        foreach (var name in Presets)
            if (FromPreset(name) is { } preset && preset.SameAs(manual, steps)) return preset;
        return manual;
    }

    /// <summary><paramref name="steps"/>칸 미리보기의 모든 칸에서 <paramref name="other"/>와 자리가 같은지(적은 모양은 달라도 됨).</summary>
    public bool SameAs(PreviewMotion other, int steps)
    {
        int count = Math.Max(1, steps);
        for (int k = 0; k < count; k++)
            if (OffsetAt(k, count) != other.OffsetAt(k, count)) return false;
        return true;
    }

    /// <summary><paramref name="steps"/>칸 미리보기의 <paramref name="frame"/>칸(0..steps-1로 자름)에서 병아리를 제자리에서 옮기는 양.</summary>
    public Point OffsetAt(int frame, int steps)
    {
        int k = Math.Clamp(frame, 0, Math.Max(1, steps) - 1);
        return Preset switch
        {
            "right" => new Point(k, 0),
            "left" => new Point(-k, 0),
            "up" => new Point(0, -k),
            "down" => new Point(0, k),
            null => Offsets[Math.Min(k, Offsets.Count - 1)],
            _ => Point.Empty,
        };
    }

    /// <summary>원문에 적는 JS 값: 이름은 <c>"right"</c>, 목록은 <c>[[0, 0], [1, 0], [2, -1]]</c>.</summary>
    public string Literal
    {
        get
        {
            if (Preset is { } name) return "\"" + name + "\"";
            var text = new StringBuilder("[");
            for (int i = 0; i < Offsets.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(CultureInfo.InvariantCulture, $"[{Offsets[i].X}, {Offsets[i].Y}]");
            }
            return text.Append(']').ToString();
        }
    }

    private static bool IsValid(Point offset) => Math.Abs(offset.X) <= MaxOffset && Math.Abs(offset.Y) <= MaxOffset;

    public bool Equals(PreviewMotion? other) => other is not null && Literal == other.Literal;

    public override bool Equals(object? obj) => Equals(obj as PreviewMotion);

    public override int GetHashCode() => Literal.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Literal;
}
