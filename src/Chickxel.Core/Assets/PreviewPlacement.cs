using System.Drawing;
using System.Globalization;

namespace Chickxel.Core.Assets;

/// <summary>
/// 선언의 미리보기 자리·피벗(효과 만들기 미리보기·검증·올릴 때 미리보기만 — 문서와는 상관없음): 끝 그림 병아리를 제자리에서 옮기는 양(<c>previewEndOffset</c>),
/// 시작 병아리 칸들의 피벗을 기본 자리에서 옮기는 양(<c>previewPivot</c>), 끝 그림 병아리 칸의 피벗을 기본 자리에서 옮기는 양(<c>previewEndPivot</c>).
/// 값은 px(오른쪽·아래가 +), x·y 각각 ±<see cref="MaxOffset"/>. 끝 그림 병아리가 없으면 끝 값 둘은 쓰지 않는다.
/// 기본 피벗은 끝 그림 병아리를 옮기기 전의 병아리 자리 가운데라 끝 그림 병아리만 옮기면 피벗은 그대로다(문서에서 프레임마다 피벗이 따로 있는 것처럼 피벗은 피벗 값으로만 옮김).
/// </summary>
public sealed record PreviewPlacement(Point EndOffset, Point Pivot, Point EndPivot)
{
    /// <summary>값 하나(x, y 각각)의 절댓값 한도(px).</summary>
    public const int MaxOffset = 64;

    /// <summary>선언 키(원문 차례).</summary>
    public const string PivotKey = "previewPivot", EndOffsetKey = "previewEndOffset", EndPivotKey = "previewEndPivot";

    /// <summary>옮기지 않음(선언에 없을 때).</summary>
    public static PreviewPlacement None { get; } = new(Point.Empty, Point.Empty, Point.Empty);

    /// <summary>
    /// 끝 그림 병아리가 있는지(<paramref name="hasEnd"/>)에 따라 실제로 쓰는 값: 끝 그림 병아리가 없으면 끝 값 둘을 지운 것.
    /// </summary>
    public PreviewPlacement For(bool hasEnd) => hasEnd || EndOffset.IsEmpty && EndPivot.IsEmpty ? this : this with { EndOffset = Point.Empty, EndPivot = Point.Empty };

    /// <summary>아무것도 옮기지 않는지.</summary>
    public bool IsNone => EndOffset.IsEmpty && Pivot.IsEmpty && EndPivot.IsEmpty;

    /// <summary>피벗을 옮기는지(<see cref="Pivot"/>·<see cref="EndPivot"/> 중 하나라도 0이 아님).</summary>
    public bool MovesPivot => !Pivot.IsEmpty || !EndPivot.IsEmpty;

    /// <summary><paramref name="steps"/>칸 미리보기의 <paramref name="frame"/>칸 피벗을 기본 자리에서 옮기는 양(끝 그림 병아리가 있으면 마지막 칸은 <see cref="EndPivot"/>).</summary>
    public Point PivotAt(int frame, int steps, bool hasEnd) => hasEnd && frame >= Math.Max(1, steps) - 1 ? EndPivot : Pivot;

    /// <summary>값 하나가 한도 안인지.</summary>
    public static bool IsValid(Point offset) => Math.Abs(offset.X) <= MaxOffset && Math.Abs(offset.Y) <= MaxOffset;

    /// <summary>원문에 적는 JS 값 <c>[x, y]</c>. 0이면 null(그 키를 지움).</summary>
    public static string? Literal(Point offset)
        => offset.IsEmpty ? null : string.Create(CultureInfo.InvariantCulture, $"[{offset.X}, {offset.Y}]");
}
