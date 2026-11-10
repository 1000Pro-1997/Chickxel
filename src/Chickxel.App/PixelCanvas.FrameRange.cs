using System.Globalization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 적용 프레임(<see cref="EditorContext.StrokeFrames"/>): 그리기 도구의 획을 지금 프레임에 그린 뒤 범위 안 다른 프레임의 같은 레이어에도 같은 자리로
/// 다시 한다(지금 프레임과 함께 실행취소 한 단계). 붓 도구는 획이 픽셀마다 한 일(색 넣기·밝게/어둡게·지우기·색 바꾸기·모양 합성)을 기록해 그 프레임의
/// 픽셀에 다시 하고, 채우기는 같은 자리를 그 프레임에서 다시 채우고, 그라데이션은 같은 시작·끝으로 다시 칠한다.
/// 링크된 셀(그림 하나를 같이 씀)은 한 번만 고치고, 캔버스 밖 그림(일반 레이어)도 같은 자리로 고친다.
/// </summary>
public sealed partial class PixelCanvas
{
    /// <summary>획이 한 픽셀에 한 일의 종류(다른 프레임의 그 픽셀에 다시 할 때).</summary>
    private enum PixelOpKind : byte
    {
        /// <summary>색을 넣는다(<see cref="PixelOp.Color"/>).</summary>
        Set,
        /// <summary>밝게/어둡게(획의 방향·세기·방식).</summary>
        Dodge,
        /// <summary>강도·채널 지우기(지우개 설정).</summary>
        PartialErase,
        /// <summary>획을 시작한 색과 같으면 <see cref="PixelOp.Color"/>로(색 바꾸기 브러시).</summary>
        Replace,
        /// <summary>모양 펜: <see cref="PixelOp.Color"/>를 위에 합성.</summary>
        BlendOver,
        /// <summary>모양 펜 지우기: <see cref="PixelOp.Color"/>의 불투명도만큼 지움.</summary>
        StampErase,
    }

    private readonly record struct PixelOp(PixelOpKind Kind, Color32 Color = default);

    /// <summary>한 픽셀에 한 일들: 보통 하나, 모양 펜처럼 겹쳐 찍으면 차례대로 더.</summary>
    private struct OpChain(PixelOp first)
    {
        public PixelOp First = first;
        public List<PixelOp>? More;
    }

    /// <summary>이번 획을 함께 적용할 다른 프레임(없으면 null — 기록하지 않음).</summary>
    private List<int>? _frameTargets;
    /// <summary>획을 시작한 셀 그림(링크된 셀은 이 그림을 같이 쓰므로 건너뜀).</summary>
    private PixelImage? _frameStrokeCel;
    /// <summary>픽셀(편집 좌표)마다 이번 획이 한 일.</summary>
    private Dictionary<Point, OpChain>? _frameOps;
    /// <summary>채우기를 누른 자리들(대칭 자리 포함).</summary>
    private List<Point>? _frameFillSeeds;

    /// <summary>획을 시작할 때: 적용 프레임에 다른 프레임이 있으면 기록을 준비한다.</summary>
    private void BeginFrameRecord(PixelImage cel)
    {
        EndFrameRecord();
        if (_ctx.Project is null || _ctx.IsGroupSelected) return;
        var targets = _ctx.StrokeFrameTargets();
        if (targets.Count == 0) return;
        _frameTargets = targets;
        _frameStrokeCel = cel;
        _frameOps = new Dictionary<Point, OpChain>();
        _frameFillSeeds = new List<Point>();
    }

    private void EndFrameRecord()
    {
        _frameTargets = null;
        _frameStrokeCel = null;
        _frameOps = null;
        _frameFillSeeds = null;
    }

    /// <summary>픽셀 <paramref name="m"/>에 한 일을 기록한다(칠할 수 없는 칸은 무시). 모양 펜 합성·지우기는 앞 일 뒤에 잇고, 나머지는 앞 일을 갈아 끼운다.</summary>
    private void RecordOp(PixelImage img, Point m, PixelOp op)
    {
        if (_frameOps is null || !CanPaint(img, m)) return;
        if (op.Kind is PixelOpKind.BlendOver or PixelOpKind.StampErase && _frameOps.TryGetValue(m, out var chain))
        {
            (chain.More ??= new List<PixelOp>()).Add(op);
            _frameOps[m] = chain;
        }
        else _frameOps[m] = new OpChain(op);
    }

    /// <summary>픽셀 퍼펙트로 획 전 색으로 되돌린 픽셀은 한 일이 없다.</summary>
    private void ForgetOp(Point m) => _frameOps?.Remove(m);

    private Color32 ApplyOp(PixelOp op, Color32 c) => op.Kind switch
    {
        PixelOpKind.Set => op.Color,
        PixelOpKind.Dodge => DodgeBurn.Apply(c, _strokeLighten, _ctx.DodgeStrength / 100f, _ctx.DodgeMode),
        PixelOpKind.PartialErase => PartialErase.Apply(c, _ctx.EraserStrength, _ctx.EraserRed, _ctx.EraserGreen, _ctx.EraserBlue),
        PixelOpKind.Replace => Color32.SameVisible(c, _replaceTarget) ? op.Color : c,
        PixelOpKind.BlendOver => Color32.BlendOver(c, op.Color),
        PixelOpKind.StampErase => ShapeStamp.Erase(c, op.Color.A),
        _ => c,
    };

    private Color32 ApplyChain(in OpChain chain, Color32 c)
    {
        c = ApplyOp(chain.First, c);
        if (chain.More is { } more)
            foreach (var op in more) c = ApplyOp(op, c);
        return c;
    }

    /// <summary>
    /// 획을 확정한다: 적용 프레임에 다른 프레임이 있으면 지금 프레임의 획과 그 프레임들의 같은 자리 고침을 실행취소 한 단계로 묶는다
    /// (이미 열린 묶음 — AI 차례 등 — 안이면 그 묶음에 넣음). 함께 고친 프레임 수를 상태 표시줄에 알린다.
    /// </summary>
    private void CommitStrokeWithFrames(string label)
    {
        if (_frameTargets is not { Count: > 0 } targets || _ctx.Project is not { } p)
        {
            _ctx.CommitStroke(label);
            return;
        }
        // 그라데이션 연필은 획이 끝날 때의 색이 그 픽셀에 넣은 색이다(픽셀 퍼펙트로 뺀 픽셀은 기록에도 없음).
        if (_gradientStroke is { } gradient && _ctx.EditImage is { } img)
            foreach (var m in gradient.Pixels.Keys) RecordOp(img, m, new PixelOp(PixelOpKind.Set, PixelAt(img, m)));
        bool own = !p.History.InGroup;
        if (own) p.History.BeginGroup(label);
        int others;
        try
        {
            _ctx.CommitStroke(label);
            others = ApplyToFrames(p, targets, label);
        }
        finally
        {
            if (own) p.History.EndGroup();
        }
        if (others > 0) Message?.Invoke(this, string.Format(CultureInfo.CurrentCulture, UiStrings.FrameRange_Applied, others));
    }

    /// <summary>이번 획을 <paramref name="targets"/> 프레임의 같은 레이어에 같은 자리로 다시 한다(편집 하나). 바꾼 프레임 수.</summary>
    private int ApplyToFrames(SpriteProject p, List<int> targets, string label)
    {
        int layer = _ctx.CurrentLayer;
        if (layer < 0 || layer >= p.Layers.Count || p.Layers[layer].IsGroup || p.IsLayerLocked(layer)) return 0;
        bool normal = p.Layers[layer].Kind == LayerKind.Normal;
        var size = p.CelSize(p.Layers[layer]);
        var seen = new HashSet<PixelImage>(ReferenceEqualityComparer.Instance);
        if (_frameStrokeCel is { } start) seen.Add(start);
        var changes = new List<(int Frame, PixelImage Cel, (PixelImage Image, Point Origin)? Full)>();
        foreach (int f in targets)
        {
            if (f < 0 || f >= p.Frames.Count) continue;
            var cel = p.GetCel(layer, f);
            if (cel is not null && !seen.Add(cel)) continue;
            if (ReplayOn(p, layer, f, cel, size, normal) is { } change) changes.Add(change);
        }
        if (changes.Count == 0) return 0;
        p.Edit(label, ProjectChange.Pixels, () =>
        {
            foreach (var (f, cel, full) in changes)
                if (full is { } whole) p.SetFullCel(layer, f, whole.Image, whole.Origin, label);
                else p.SetCel(layer, f, cel, label);
        });
        return changes.Count;
    }

    /// <summary>
    /// 프레임 <paramref name="frame"/>의 셀에 이번 획을 다시 한 결과: 새 셀 그림과, 캔버스 밖도 고쳤으면 셀 + 캔버스 밖 그림을 합친 그림(캔버스 좌표 왼쪽 위).
    /// 바뀐 것이 없으면 null.
    /// </summary>
    private (int Frame, PixelImage Cel, (PixelImage Image, Point Origin)? Full)? ReplayOn(SpriteProject p, int layer, int frame, PixelImage? cel, Size size, bool normal)
    {
        var work = cel?.Clone() ?? new PixelImage(size.Width, size.Height);
        var clip = _ctx.Selection.Clip;
        var overflow = normal ? p.GetCelOverflow(layer, frame) : null;
        List<(Point At, Color32 Color)>? outside = null;
        switch (_strokeTool)
        {
            case ToolKind.Gradient:
                Gradient.Apply(work, _startPx, _gradientEnd, _ctx.GradientStops, _ctx.GradientInterpolation, _ctx.GradientDither, clip,
                    _ctx.GradientAlphaStops, _ctx.GradientColorSpace);
                break;
            case ToolKind.Fill:
            {
                var sample = _ctx.FillSampleVisible ? _ctx.VisibleImage(frame) : null;
                if (sample is not null && (sample.Width != work.Width || sample.Height != work.Height)) sample = null;
                foreach (var m in _frameFillSeeds ?? [])
                    if (work.Contains(m.X, m.Y))
                        Raster.FloodFill(work, m.X, m.Y, _strokeColor, _ctx.FillContiguous, clip, _ctx.FillToleranceChannels, _ctx.FillDiagonal, sample, _ctx.FillColorMode);
                    else if (overflow is { } o && _strokeOutside.Contains(m))
                        foreach (var at in OutsideFillRegion(o.Image, o.Origin, m)) (outside ??= []).Add((at, _strokeColor));
                break;
            }
            default:
                if (_frameOps is { } ops)
                    foreach (var (m, chain) in ops)
                        if (work.Contains(m.X, m.Y)) work.Set(m.X, m.Y, ApplyChain(chain, work.Get(m.X, m.Y)));
                        else if (normal) (outside ??= []).Add((m, ApplyChain(chain, OverflowColor(overflow, m))));
                break;
        }
        bool celChanged = cel is null ? !work.GetOpaqueBounds().IsEmpty : !work.ContentEquals(cel);
        if (outside is null) return celChanged ? (frame, work, null) : null;

        // 캔버스 밖도 칠함: 셀과 캔버스 밖 그림을 한 그림으로 합쳐 고친다(남기는 범위 밖은 버림).
        var area = p.OverflowBounds;
        int left = 0, top = 0, right = size.Width, bottom = size.Height;
        if (overflow is { } ov)
        {
            left = Math.Min(left, ov.Origin.X);
            top = Math.Min(top, ov.Origin.Y);
            right = Math.Max(right, ov.Origin.X + ov.Image.Width);
            bottom = Math.Max(bottom, ov.Origin.Y + ov.Image.Height);
        }
        foreach (var (at, _) in outside)
            if (area.Contains(at))
            {
                left = Math.Min(left, at.X);
                top = Math.Min(top, at.Y);
                right = Math.Max(right, at.X + 1);
                bottom = Math.Max(bottom, at.Y + 1);
            }
        var full = new PixelImage(right - left, bottom - top);
        full.Blit(work, -left, -top);
        if (overflow is { } existing) full.Blit(existing.Image, existing.Origin.X - left, existing.Origin.Y - top, alphaBlend: true);
        bool outsideChanged = false;
        foreach (var (at, color) in outside)
        {
            if (!area.Contains(at) || full.Get(at.X - left, at.Y - top) == color) continue;
            full.Set(at.X - left, at.Y - top, color);
            outsideChanged = true;
        }
        if (!celChanged && !outsideChanged) return null;
        return (frame, work, (full, new Point(left, top)));
    }

    /// <summary>캔버스 밖 그림의 칸 색(그림이 없거나 그 밖이면 투명).</summary>
    private static Color32 OverflowColor((PixelImage Image, Point Origin)? overflow, Point m)
        => overflow is { } o && o.Image.Contains(m.X - o.Origin.X, m.Y - o.Origin.Y) ? o.Image.Get(m.X - o.Origin.X, m.Y - o.Origin.Y) : Color32.Transparent;
}
