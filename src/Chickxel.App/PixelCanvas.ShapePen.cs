using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 모양 펜: 누른 곳 가운데에 고른 세트의 다음 모양을 찍고(알파 합성), 끌면 간격마다(최적화 간격이면 앞 도장과 겹치지 않는 첫 자리마다) 찍는다. 대칭·타일 모드·선택 영역·캔버스 밖 그림은 연필과 같은 길
/// (<see cref="Targets"/>·<see cref="CanPaint"/>·<see cref="Put"/>)을 쓰고, 한 획이 실행취소 한 단계다. 커서 아래에는 다음 모양을 반투명하게 미리 보인다.
/// </summary>
public sealed partial class PixelCanvas
{
    /// <summary>미리보기 불투명도(모양 알파에 곱함).</summary>
    private const double ShapePenGhostOpacity = 0.6;
    /// <summary>물들인 모양 사본을 이만큼 넘게 모으면 비운다.</summary>
    private const int MaxStampTints = 32;

    /// <summary>모양 펜 획의 끌기 자리(모양 펜 획이 아니면 null).</summary>
    private StampTrail? _stampTrail;
    /// <summary>이번 획의 "여기 찍을까" 물음(간격 또는 최적화 간격 — 누를 때 정함).</summary>
    private Func<Point, Point, bool>? _stampDue;
    /// <summary>이번 획이 모양 불투명도만큼 지우는지(오른쪽 버튼 = 지우개 설정).</summary>
    private bool _stampErase;
    /// <summary>이번 획에서 물들일 색(물들이기를 끄면 null).</summary>
    private Color32? _stampTint;
    /// <summary>앞 도장 모양(최적화 간격이 겹침을 잴 때).</summary>
    private PixelImage? _stampLast;
    /// <summary>물들인 모양 사본(획과 미리보기가 함께 씀 — 원본·색이 같으면 다시 만들지 않음).</summary>
    private readonly Dictionary<(PixelImage Shape, Color32 Color), PixelImage> _stampTints = new();
    /// <summary>미리보기로 그리는 모양과 그 비트맵.</summary>
    private (PixelImage Look, WriteableBitmap Bitmap)? _stampGhost;

    /// <summary>모양 펜 획을 시작하고 누른 곳에 첫 도장을 찍는다. 세트가 비었으면 알리고 false.</summary>
    private bool BeginShapeStamps(MouseButton button, Point px)
    {
        if (_ctx.ShapePenShape is null)
        {
            Message?.Invoke(this, UiStrings.ShapePen_EmptySet);
            return false;
        }
        BeginPixelStroke(ToolKind.ShapePen, button, px);
        _stampErase = button == MouseButton.Right && _ctx.RightButtonErases;
        _stampTint = _ctx.ShapePenTint ? _strokeColor : null;
        _stampLast = null;
        _stampTrail = new StampTrail(px);
        _stampDue = _ctx.ShapePenOptimizedSpacing ? ShapeTouches : StampTrail.Spacing(_ctx.ShapePenSpacing);
        StampShapeAt(px);
        return true;
    }

    /// <summary>최적화 간격: 다음 모양을 <paramref name="p"/>에 놓으면 앞 도장(<paramref name="last"/>)과 불투명 픽셀이 겹치지 않는지(겹치지 않는 첫 자리 = 붙은 자리).</summary>
    private bool ShapeTouches(Point last, Point p)
        => _stampLast is not { } previous || _ctx.ShapePenShape is not { } next || !ShapeStamp.Overlaps(previous, last, next, p);

    /// <summary>다음 모양을 <paramref name="p"/> 가운데에 찍고(대칭 자리는 뒤집혀 찍힘) 다음 모양으로 넘긴다.</summary>
    private void StampShapeAt(Point p)
    {
        if (_ctx.ShapePenShape is not { } shape) return;
        var img = _ctx.EditImage!;
        var clip = _ctx.Selection.Clip;
        foreach (var (m, c) in ShapeStamp.Pixels(_stampErase ? shape : StampLook(shape, _stampTint), p))
            foreach (var t in Targets([m]))
            {
                if (clip is { } k && !k.Contains(t) || !CanPaint(img, t)) continue;
                var below = PixelAt(img, t);
                Put(img, t, _stampErase ? ShapeStamp.Erase(below, c.A) : Color32.BlendOver(below, c));
                RecordOp(img, t, _stampErase ? new PixelOp(PixelOpKind.StampErase, c) : new PixelOp(PixelOpKind.BlendOver, c));
            }
        _stampLast = shape;
        _ctx.AdvanceShapePen();
    }

    /// <summary>찍을 모양: 물들이지 않으면 원본, 아니면 그 색으로 물들인 사본(모아 둠).</summary>
    private PixelImage StampLook(PixelImage shape, Color32? tint)
    {
        if (tint is not { } color) return shape;
        if (_stampTints.TryGetValue((shape, color), out var look)) return look;
        if (_stampTints.Count >= MaxStampTints) _stampTints.Clear();
        return _stampTints[(shape, color)] = ShapeStamp.Tint(shape, color);
    }

    /// <summary>모양 펜 획 상태를 비운다(획을 마치거나 버릴 때).</summary>
    private void EndShapeStamps()
    {
        _stampTrail = null;
        _stampDue = null;
        _stampLast = null;
    }

    /// <summary>
    /// 모양 펜 커서: 다음에 찍을 모양을 커서 아래에 반투명하게 그리고 둘레에 윤곽을, 대칭 자리에는 회색 윤곽을 그린다.
    /// 끄는 중에는 그리지 않는다(찍힌 그림이 보이게).
    /// </summary>
    private void DrawShapePenGhost(DrawingContext g)
    {
        if (_ctx.Tool != ToolKind.ShapePen || _cursorPx is not { } p || _drag != DragMode.None
            || _ctx.IsGroupSelected || _ctx.IsTilemapSelected || _ctx.IsTransforming || _ctx.IsCanvasResizing || _ctx.ShapePenShape is not { } shape) return;
        var look = StampLook(shape, _ctx.ShapePenTint ? _ctx.Primary : null);
        if (_stampGhost is not { } ghost || !ReferenceEquals(ghost.Look, look))
        {
            _stampGhost?.Bitmap.Dispose();
            ghost = (look, ImageUtil.ToBitmap(look));
            _stampGhost = ghost;
        }
        var o = ShapeStamp.Origin(p, look.Width, look.Height);
        var dest = ToRect(ImageToScreen(new Rectangle(o.X, o.Y, look.Width, look.Height)));
        using (g.PushOpacity(ShapePenGhostOpacity))
        using (ImageUtil.Pixelated(g))
            g.DrawImage(ghost.Bitmap, new Rect(0, 0, look.Width, look.Height), dest);
        var cells = new HashSet<Point>(ShapeStamp.Pixels(look, p).Select(x => x.At));
        DrawMirrorGhosts(g, cells);
        DrawCellOutline(g, cells, CursorOutlineBackPen, CursorOutlineFrontPen);
    }
}
