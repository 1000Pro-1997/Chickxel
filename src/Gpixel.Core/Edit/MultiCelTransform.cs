using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>
/// 여러 셀(타임라인에서 고른 셀 묶음)에 같은 자유 변형을 입힌다. 셀마다 뗄 영역(선택 영역, 없으면 그 셀의 칠한 픽셀 전체)을 따로 떼어
/// 원본으로 삼고, 기준 변형(<see cref="FreeTransform"/> + <see cref="TransformParams"/>)과 똑같은 행렬(같은 회전 중심·각도·배율·옮김)로
/// 다시 그려 한꺼번에 내려놓는다(실행취소 한 단계). 셀 자체는 <see cref="Commit"/> 때까지 건드리지 않는다.
/// <para>
/// 좌표는 기준 레이어 공간이다. 부위 셀은 부위 자기 공간에서 변형하되, 기준과 자리가 다른 레이어(부위 위치 차이)의 셀은 그 차이만큼 맞춰
/// 화면에서 같은 자리를 기준으로 돈다. 그룹·빈 셀(뗄 픽셀 없음)·잠긴 레이어(잠긴 그룹의 부위 포함)는 건너뛴다.
/// 캔버스(부위면 부위) 밖으로 나간 픽셀은 내려놓을 때 잘린다 — 그 전까지 원본은 잘리지 않은 채로 있다.
/// </para>
/// </summary>
public sealed class MultiCelTransform
{
    /// <summary>변형할 셀 하나: 레이어·프레임, 떼어 낸 원본, 기준 공간과의 차이.</summary>
    public sealed class Target
    {
        internal Target(Layer layer, Frame frame, FreeTransform source, PixelImage rest, Point offset)
        {
            Layer = layer;
            Frame = frame;
            Source = source;
            Rest = rest;
            Offset = offset;
        }

        public Layer Layer { get; }
        public Frame Frame { get; }

        /// <summary>떼어 낸 원본(이 셀 좌표).</summary>
        public FreeTransform Source { get; }

        /// <summary>이 셀 좌표 + <see cref="Offset"/> = 기준 레이어 좌표.</summary>
        public Point Offset { get; }

        /// <summary>원본을 떼어 낸 뒤의 셀(뗀 자리 투명) — 결과를 이 위에 내려놓는다.</summary>
        internal PixelImage Rest { get; }
    }

    private readonly SpriteProject _project;
    private readonly List<Target> _targets;

    private MultiCelTransform(SpriteProject project, List<Target> targets, int lockedSkipped)
    {
        _project = project;
        _targets = targets;
        LockedSkipped = lockedSkipped;
    }

    /// <summary>변형할 셀들(빈 셀·잠긴 레이어는 빠져 있음).</summary>
    public IReadOnlyList<Target> Targets => _targets;

    /// <summary>그림이 있지만 레이어가 잠겨 건너뛴 셀 수.</summary>
    public int LockedSkipped { get; }

    /// <summary>모든 원본을 감싸는 사각형(기준 레이어 좌표). 셀이 없으면 <see cref="Rectangle.Empty"/>.</summary>
    public Rectangle Bounds
    {
        get
        {
            var union = Rectangle.Empty;
            foreach (var t in _targets)
            {
                var r = t.Source.SourceBounds;
                r.Offset(t.Offset);
                union = union.IsEmpty ? r : Rectangle.Union(union, r);
            }
            return union;
        }
    }

    /// <summary>
    /// 셀들(레이어 번호, 프레임 번호; 겹치면 한 번)에서 원본을 뗀다. <paramref name="referenceLayer"/>는 좌표 기준 레이어(캔버스에 보이는 레이어),
    /// <paramref name="selection"/>은 그 레이어 좌표의 선택 영역(null = 셀마다 칠한 픽셀 전체).
    /// </summary>
    public static MultiCelTransform Create(SpriteProject project, int referenceLayer, IEnumerable<(int Layer, int Frame)> cels, PixelMask? selection = null)
    {
        var origin = Origin(project.Layers[referenceLayer]);
        var targets = new List<Target>();
        var seen = new HashSet<(int, int)>();
        int locked = 0;
        foreach (var (l, f) in cels)
        {
            if (!seen.Add((l, f)) || l < 0 || l >= project.Layers.Count || f < 0 || f >= project.Frames.Count) continue;
            var layer = project.Layers[l];
            if (layer.IsGroup || project.IsCelEmpty(l, f)) continue;
            if (project.IsLayerLocked(l))
            {
                locked++;
                continue;
            }
            var at = Origin(layer);
            var offset = new Point(at.X - origin.X, at.Y - origin.Y);
            var rest = project.GetCel(l, f)!.Clone();
            var region = selection is { } s ? s.Offset(-offset.X, -offset.Y) : new PixelMask(rest.GetOpaqueBounds());
            var lift = new Selection();
            lift.Select(rest, region);
            if (!lift.HasSelection) continue;
            lift.Lift(rest);
            var floating = lift.Floating!;
            if (floating.GetOpaqueBounds().IsEmpty) continue;
            targets.Add(new Target(layer, project.Frames[f], new FreeTransform(floating, lift.Region), rest, offset));
        }
        return new MultiCelTransform(project, targets, locked);
    }

    /// <summary>레이어 좌표 → 프레임 좌표 차이(부위면 부위 위치).</summary>
    private static Point Origin(Layer layer) => layer.IsPart ? layer.PartOffset : Point.Empty;

    /// <summary>
    /// 셀 하나를 기준 변형 <paramref name="reference"/>의 값 <paramref name="p"/>와 같은 행렬로 다시 그린 새 셀 이미지(셀 밖은 잘림, 투명 픽셀은 아래를 덮지 않음).
    /// </summary>
    public PixelImage Render(Target target, FreeTransform reference, TransformParams p, ResampleMode mode)
    {
        var r = target.Source.Render(reference.ParamsFor(target.Source, p, target.Offset.X, target.Offset.Y), mode);
        var image = target.Rest.Clone();
        var drop = new Selection();
        drop.SetFloating(r.Image, r.Region);
        drop.Anchor(image);
        return image;
    }

    /// <summary>
    /// 모든 셀에 변형을 적용한다(실행취소 한 단계 <paramref name="label"/>). <paramref name="also"/>는 같은 단계 안에서 함께 할 일(지금 셀 등).
    /// 그동안 없어졌거나 잠긴 레이어·프레임의 셀은 건너뛴다. 바꾼 셀 수를 돌려준다(셀도 <paramref name="also"/>도 없으면 기록 없음).
    /// </summary>
    public int Commit(string label, FreeTransform reference, TransformParams p, ResampleMode mode, Action? also = null)
    {
        var changes = new List<(int Layer, int Frame, PixelImage Image)>();
        foreach (var t in _targets)
        {
            int l = _project.IndexOf(t.Layer), f = _project.IndexOf(t.Frame);
            if (l < 0 || f < 0 || _project.IsLayerLocked(l)) continue;
            changes.Add((l, f, Render(t, reference, p, mode)));
        }
        if (changes.Count == 0 && also is null) return 0;
        _project.Edit(label, ProjectChange.Pixels, () =>
        {
            also?.Invoke();
            foreach (var (l, f, image) in changes) _project.SetCel(l, f, image);
        });
        return changes.Count;
    }
}
