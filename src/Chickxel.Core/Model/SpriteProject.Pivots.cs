using System.Drawing;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 피벗: 레이어마다의 피벗(기본 + 프레임마다, 부위는 부위 기준·그 밖에는 캔버스 기준)과 캔버스 피벗(내보내기 — 자신의 값 또는 대표 레이어의 피벗),
// 그리고 그림을 옮기거나 바꿀 때 피벗을 같은 그림 지점에 맞추는 대응.
public sealed partial class SpriteProject
{
    /// <summary>
    /// 캔버스 피벗의 대표 레이어(대표 레이어): 있으면 프레임마다의 캔버스 피벗(<see cref="GetFramePivot"/> — 시트·Unity 내보내기)이 그 레이어의 그 프레임 피벗을
    /// 캔버스 기준으로 바꾼 것이 된다. null이면 캔버스 자신의 피벗(<see cref="GetCanvasOwnPivot"/>). 바꿀 때는 <see cref="SetPivotLayer"/>로(실행취소됨),
    /// 레이어를 지우면 없어진다.
    /// </summary>
    public Layer? PivotLayer { get; private set; }

    /// <summary>
    /// 예전 형식 파일을 열며 바꿔 읽었으면 그 파일의 형식 버전(<see cref="IO.ProjectIO.CurrentVersion"/>보다 작음), 아니면 null. 저장하지 않는다 —
    /// 앱이 연 뒤 "예전 형식에서 바꿈"을 알리는 데 쓴다(저장하면 새 형식으로 쓴다).
    /// </summary>
    public int? ConvertedFromVersion { get; internal set; }

    /// <summary>
    /// 프레임의 캔버스 피벗(내보내기 피벗, 캔버스 기준 0~1, 아래 왼쪽 원점): 대표 레이어(<see cref="PivotLayer"/>)가 있으면 그 레이어의 그 프레임 피벗을
    /// 캔버스 기준으로 바꾼 것, 없으면 캔버스 자신의 피벗(<see cref="GetCanvasOwnPivot"/>).
    /// </summary>
    public Pivot GetFramePivot(int frameIndex)
    {
        if (PivotLayer is not { } layer) return GetCanvasOwnPivot(frameIndex);
        var at = LayerPivotPixel(layer, frameIndex);
        return new Pivot(at.X / _width, 1f - at.Y / _height);
    }

    /// <summary>캔버스 자신의 피벗: 그 프레임만의 피벗(<see cref="Frame.Pivot"/>)이 있으면 그것, 없으면 기본 피벗(<see cref="SpriteSettings.DefaultPivot"/>).</summary>
    public Pivot GetCanvasOwnPivot(int frameIndex) => _frames[frameIndex].Pivot ?? Sprite.DefaultPivot;

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 프레임 <paramref name="frameIndex"/> 피벗(그 레이어의 공간 — 부위는 부위 기준, 그 밖에는 캔버스 기준):
    /// 그 프레임만의 피벗이 있으면 그것, 없으면 레이어 피벗(<see cref="Layer.Pivot"/>). 범위 밖 프레임은 레이어 피벗.
    /// </summary>
    public Pivot GetLayerPivot(Layer layer, int frameIndex)
        => (uint)frameIndex < (uint)_frames.Count ? PivotOf(layer, _frames[frameIndex]) : layer.Pivot;

    /// <summary>레이어 <paramref name="layer"/>가 프레임 <paramref name="frameIndex"/>에서 다른 프레임과 다른 피벗을 쓰는지(<see cref="SetLayerFramePivot"/>).</summary>
    public bool HasLayerFramePivot(Layer layer, int frameIndex)
        => (uint)frameIndex < (uint)_frames.Count && _frames[frameIndex].LayerPivots.ContainsKey(layer);

    /// <summary>
    /// 부위 <paramref name="part"/>가 프레임 <paramref name="frameIndex"/>에서 따로 둔 피벗이나 옮긴 위치가 있는지(도구 설정의 "현재 프레임부터" —
    /// <see cref="SetLayerFramesFrom"/>·<see cref="SetLayerPivotFrom"/>·<see cref="MovePartFrom"/>).
    /// </summary>
    public bool HasPartFrameOverride(Layer part, int frameIndex)
        => (uint)frameIndex < (uint)_frames.Count && (_frames[frameIndex].LayerPivots.ContainsKey(part) || _frames[frameIndex].PartShifts.ContainsKey(part));

    /// <summary>
    /// 레이어가 프레임 <paramref name="frameIndex"/>에서 따로 둔 것이 있는지(도구 설정의 "현재 프레임부터"): 부위는 <see cref="HasPartFrameOverride"/>,
    /// 일반 레이어·그룹은 <see cref="HasLayerFramePivot"/>.
    /// </summary>
    public bool HasLayerFrameOverride(Layer layer, int frameIndex)
        => layer.IsPart ? HasPartFrameOverride(layer, frameIndex) : HasLayerFramePivot(layer, frameIndex);

    /// <summary>
    /// 레이어 피벗의 캔버스 픽셀 좌표(왼쪽 위 기준, 연속): 부위는 그 프레임의 부위 위치(<see cref="GetPartOffset"/>) + 부위 안 피벗 픽셀,
    /// 일반 레이어·그룹은 캔버스 기준 피벗 픽셀. 캔버스 피벗 표시·거울 그리기·변형 가운데·대표 레이어 바꾸기에 쓴다.
    /// </summary>
    public PointF LayerPivotPixel(Layer layer, int frameIndex)
    {
        var pivot = GetLayerPivot(layer, frameIndex);
        if (!layer.IsPart)
        {
            var (cx, cy) = pivot.ToLocalPixel(_width, _height);
            return new PointF(cx, cy);
        }
        var (x, y) = pivot.ToLocalPixel(layer.PartSize.Width, layer.PartSize.Height);
        var offset = GetPartOffset(layer, frameIndex);
        return new PointF(offset.X + x, offset.Y + y);
    }

    /// <summary>효과의 "피벗" 자리(효과 원본 왼쪽 위 기준 px): 그 레이어의 그 프레임 피벗 — 부위는 부위 셀 기준, 일반 레이어·그룹은 캔버스 기준.</summary>
    public PointF EffectPivot(Layer layer, int frameIndex)
    {
        var pivot = GetLayerPivot(layer, Math.Clamp(frameIndex, 0, _frames.Count - 1));
        var (x, y) = layer.IsPart ? pivot.ToLocalPixel(layer.PartSize.Width, layer.PartSize.Height) : pivot.ToLocalPixel(_width, _height);
        return new PointF(x, y);
    }

    // ───────────── 레이어 피벗 편집 ─────────────

    /// <summary>레이어 피벗(그 레이어의 공간 0~1, 아래 왼쪽 원점) — 그 프레임만의 피벗이 없는 프레임에 쓴다. 효과의 피벗, 부위면 부위 아틀라스 내보내기 피벗.</summary>
    public void SetLayerPivot(int layerIndex, Pivot pivot)
    {
        var layer = _layers[layerIndex];
        Edit(DomainStrings.Project_LayerPivot, ProjectChange.Layers | RepresentativeChange(layer), () => layer.Pivot = pivot);
    }

    /// <summary>
    /// 레이어의 프레임 <paramref name="frameIndex"/>만의 피벗(그 레이어의 공간 0~1): 다른 프레임과 다르게 둔다. null이면 그 프레임도 레이어 피벗(<see cref="Layer.Pivot"/>)을 따른다.
    /// </summary>
    public void SetLayerFramePivot(int layerIndex, int frameIndex, Pivot? pivot)
    {
        var layer = _layers[layerIndex];
        var frame = _frames[frameIndex];
        if (pivot is { } p ? frame.LayerPivots.TryGetValue(layer, out var cur) && cur == p : !frame.LayerPivots.ContainsKey(layer)) return;
        Edit(DomainStrings.Project_LayerFramePivot, ProjectChange.Layers | RepresentativeChange(layer), () =>
        {
            if (pivot is { } value) frame.LayerPivots[layer] = value;
            else frame.LayerPivots.Remove(layer);
        });
    }

    /// <summary>
    /// 레이어를 프레임 <paramref name="fromFrame"/>부터 끝까지 따로 두거나(<paramref name="own"/> — 그 프레임들의 지금 피벗을 그 프레임만의 피벗으로, 모습은 그대로)
    /// 되돌린다(그 프레임들에 따로 둔 피벗을 지워 레이어 피벗을 따름 — 부위는 옮긴 위치도 지워 기준 위치를 따름). 도구 설정의 "현재 프레임부터".
    /// 실행취소 한 단계, 바뀌는 것이 없으면 기록 없음.
    /// </summary>
    public void SetLayerFramesFrom(int layerIndex, int fromFrame, bool own)
    {
        var layer = _layers[layerIndex];
        var frames = FramesFrom(fromFrame);
        bool moves = !own && layer.IsPart && frames.Any(f => f.PartShifts.ContainsKey(layer));
        if (own ? frames.All(f => f.LayerPivots.ContainsKey(layer)) : !moves && !frames.Any(f => f.LayerPivots.ContainsKey(layer))) return;
        var label = layer.IsPart ? DomainStrings.Project_PartFramesFrom : DomainStrings.Project_LayerFramePivot;
        Edit(label, ProjectChange.Layers | ProjectChange.Frames | (moves ? ProjectChange.Pixels : ProjectChange.None), () =>
        {
            foreach (var f in frames)
            {
                if (own) f.LayerPivots.TryAdd(layer, layer.Pivot);
                else
                {
                    f.LayerPivots.Remove(layer);
                    f.PartShifts.Remove(layer);
                }
            }
        });
    }

    /// <summary>
    /// 레이어 피벗을 프레임 <paramref name="fromFrame"/>부터 끝까지 바꾼다(실행취소 한 단계): <paramref name="fromFrame"/>의 피벗이 <paramref name="pivot"/>이 되고,
    /// 뒤 프레임들도 같은 만큼 옮긴다(프레임마다 달랐던 차이는 그대로). 그 프레임들은 모두 그 프레임만의 피벗을 갖는다. 같으면 기록 없음.
    /// </summary>
    public void SetLayerPivotFrom(int layerIndex, int fromFrame, Pivot pivot)
    {
        var layer = _layers[layerIndex];
        var frames = FramesFrom(fromFrame);
        var start = GetLayerPivot(layer, fromFrame);
        float dx = pivot.X - start.X, dy = pivot.Y - start.Y;
        if (dx == 0 && dy == 0 && frames.All(f => f.LayerPivots.ContainsKey(layer))) return;
        Edit(DomainStrings.Project_LayerFramePivot, ProjectChange.Layers | ProjectChange.Frames, () =>
        {
            foreach (var f in frames)
            {
                var cur = PivotOf(layer, f);
                f.LayerPivots[layer] = ReferenceEquals(f, frames[0]) ? pivot : new Pivot(cur.X + dx, cur.Y + dy);
            }
        });
    }

    /// <summary>
    /// 캔버스 피벗의 대표 레이어를 고르거나(<paramref name="layer"/>) 끈다(null — 캔버스 자신의 피벗). 실행취소 한 단계, 같으면 기록 없음.
    /// 프로젝트에 없는 레이어면 <see cref="ArgumentException"/>.
    /// </summary>
    public void SetPivotLayer(Layer? layer)
    {
        if (layer is not null && !_layers.Contains(layer)) throw new ArgumentException(DomainStrings.Project_LayerNotInProject, nameof(layer));
        if (ReferenceEquals(layer, PivotLayer)) return;
        Edit(DomainStrings.Project_PivotLayer, ProjectChange.Layers | ProjectChange.Frames, () => PivotLayer = layer);
    }

    /// <summary>파일 읽기용: 대표 레이어를 기록 없이 둔다.</summary>
    internal void SetPivotLayerRaw(Layer? layer) => PivotLayer = layer;

    // ───────────── 캔버스 자신의 피벗 편집 ─────────────

    /// <summary>캔버스 자신의 프레임 피벗 재정의(null = 기본 피벗 사용).</summary>
    public void SetFramePivot(int index, Pivot? pivot)
    {
        var frame = _frames[index];
        Edit(DomainStrings.Project_FramePivot, ProjectChange.Frames, () => frame.Pivot = pivot);
    }

    /// <summary>
    /// 프레임 <paramref name="fromFrame"/>부터 끝까지 캔버스 자신의 프레임 피벗을 따로 두거나(<paramref name="own"/> — 지금 피벗을 그 프레임만의 피벗으로, 모습은 그대로)
    /// 되돌린다(기본 피벗을 따름). 도구 설정의 "현재 프레임부터". 실행취소 한 단계, 바뀌는 것이 없으면 기록 없음.
    /// </summary>
    public void SetFramePivotsFrom(int fromFrame, bool own)
    {
        var frames = FramesFrom(fromFrame);
        if (frames.All(f => (f.Pivot is not null) == own)) return;
        Edit(DomainStrings.Project_FramePivot, ProjectChange.Frames, () =>
        {
            foreach (var f in frames) f.Pivot = own ? f.Pivot ?? Sprite.DefaultPivot : null;
        });
    }

    /// <summary>
    /// 캔버스 자신의 프레임 피벗을 프레임 <paramref name="fromFrame"/>부터 끝까지 바꾼다(실행취소 한 단계): <paramref name="fromFrame"/>의 피벗이 <paramref name="pivot"/>이 되고,
    /// 뒤 프레임들도 같은 만큼 옮긴다(프레임마다 달랐던 차이는 그대로). 그 프레임들은 모두 그 프레임만의 피벗을 갖는다. 같으면 기록 없음.
    /// </summary>
    public void SetFramePivotFrom(int fromFrame, Pivot pivot)
    {
        var frames = FramesFrom(fromFrame);
        var start = GetCanvasOwnPivot(fromFrame);
        float dx = pivot.X - start.X, dy = pivot.Y - start.Y;
        if (dx == 0 && dy == 0 && frames.All(f => f.Pivot is not null)) return;
        Edit(DomainStrings.Project_FramePivot, ProjectChange.Frames, () =>
        {
            foreach (var f in frames)
            {
                var cur = f.Pivot ?? Sprite.DefaultPivot;
                f.Pivot = ReferenceEquals(f, frames[0]) ? pivot : new Pivot(cur.X + dx, cur.Y + dy);
            }
        });
    }

    // ───────────── 피벗 대응(그림을 옮기거나 바꿀 때, Edit 안에서) ─────────────

    /// <summary>프레임 객체 기준 레이어 피벗(그 프레임만의 피벗, 없으면 레이어 피벗).</summary>
    private static Pivot PivotOf(Layer layer, Frame frame) => frame.LayerPivots.TryGetValue(layer, out var own) ? own : layer.Pivot;

    /// <summary>대표 레이어면 캔버스 피벗도 바뀌므로 프레임 변경도 알린다.</summary>
    private ProjectChange RepresentativeChange(Layer layer) => ReferenceEquals(layer, PivotLayer) ? ProjectChange.Frames : ProjectChange.None;

    /// <summary>레이어 피벗과 그 레이어의 프레임별 피벗을 모두 <paramref name="map"/>으로 바꾼다(그림 크기·자리를 바꿀 때 같은 그림 지점을 가리키게). Edit 안에서.</summary>
    private void MapLayerPivots(Layer layer, Func<Pivot, Pivot> map)
    {
        layer.Pivot = map(layer.Pivot);
        foreach (var f in _frames)
            if (f.LayerPivots.TryGetValue(layer, out var own)) f.LayerPivots[layer] = map(own);
    }

    /// <summary>
    /// 캔버스 기준 피벗 모두(캔버스 자신의 기본·프레임 피벗, 일반 레이어·그룹의 피벗과 프레임별 피벗)를 <paramref name="map"/>으로 바꾼다
    /// (캔버스 뒤집기·회전·크기 바꾸기 — 부위 피벗은 부위 기준이라 부르는 쪽이 따로). Edit 안에서.
    /// </summary>
    private void MapCanvasPivots(Func<Pivot, Pivot> map)
    {
        Sprite.DefaultPivot = map(Sprite.DefaultPivot);
        foreach (var f in _frames)
            if (f.Pivot is { } fp) f.Pivot = map(fp);
        foreach (var layer in _layers)
            if (!layer.IsPart) MapLayerPivots(layer, map);
    }

    /// <summary>
    /// 캔버스 기준 피벗 대응: <paramref name="from"/> 크기 캔버스의 피벗 픽셀을 <paramref name="map"/>으로 옮겨 <paramref name="to"/> 크기 캔버스의 피벗으로.
    /// </summary>
    private static Func<Pivot, Pivot> CanvasPivotMap(Size from, Size to, Func<PointF, PointF> map) => p =>
    {
        var (x, y) = p.ToLocalPixel(from.Width, from.Height);
        var at = map(new PointF(x, y));
        return new Pivot(at.X / to.Width, 1f - at.Y / to.Height);
    };

    /// <summary>
    /// 일반 레이어·그룹의 피벗을 프레임 <paramref name="frames"/>에서만 캔버스에서 (<paramref name="dx"/>, <paramref name="dy"/>)px 옮긴다(그 프레임들의 그림을 옮길 때).
    /// 모든 프레임이면 레이어 피벗과 프레임별 피벗을 같이 옮기고, 아니면 그 프레임들에 옮긴 피벗을 그 프레임만의 피벗으로 둔다. Edit 안에서.
    /// </summary>
    private void ShiftLayerPivots(Layer layer, HashSet<Frame> frames, int dx, int dy)
    {
        float sx = (float)dx / _width, sy = (float)dy / _height;
        Pivot Shift(Pivot p) => new(p.X + sx, p.Y - sy);
        if (_frames.All(frames.Contains))
        {
            MapLayerPivots(layer, Shift);
            return;
        }
        foreach (var f in frames) f.LayerPivots[layer] = Shift(PivotOf(layer, f));
    }

    /// <summary>
    /// 캔버스 기준 피벗(일반 레이어·그룹)을 <paramref name="area"/>(캔버스 좌표) 부위 기준 피벗으로 바꾼다(같은 그림 지점). 일반 레이어를 부위로 바꿀 때.
    /// </summary>
    private Pivot CanvasPivotToPart(Pivot p, Rectangle area)
    {
        var (x, y) = p.ToLocalPixel(_width, _height);
        return new Pivot((x - area.X) / area.Width, 1f - (y - area.Y) / area.Height);
    }

    /// <summary>부위 기준 피벗을 캔버스 기준으로 바꾼다(부위 크기 <paramref name="size"/>, 부위 왼쪽 위 <paramref name="offset"/> — 같은 그림 지점).</summary>
    private Pivot PartPivotToCanvas(Pivot p, Size size, Point offset)
    {
        var (x, y) = p.ToLocalPixel(size.Width, size.Height);
        return new Pivot((offset.X + x) / _width, 1f - (offset.Y + y) / _height);
    }
}
