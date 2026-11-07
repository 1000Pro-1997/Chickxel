using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 피벗 도구의 대상: 보통은 지금 레이어(일반·그룹·부위)의 피벗, 타임라인 캔버스 칩의 글자를 누르면 캔버스 피벗(내보내기 피벗 —
// 대표 레이어가 있으면 그 레이어의 피벗을 고친다). 레이어 행을 누르거나 지금 레이어가 바뀌면 다시 레이어 대상.
public sealed partial class EditorContext
{
    private bool _pivotCanvas;

    /// <summary>피벗 도구가 캔버스 피벗을 고치는지(타임라인 캔버스 칩의 글자로 켬, 레이어 행을 누르거나 지금 레이어가 바뀌면 꺼짐).</summary>
    public bool IsPivotCanvasTarget => _pivotCanvas && Project is not null;

    /// <summary>피벗 대상을 캔버스(<paramref name="on"/>) 또는 지금 레이어로 둔다.</summary>
    public void SetPivotCanvasTarget(bool on)
    {
        if (_pivotCanvas == on) return;
        _pivotCanvas = on;
        _pivotPreview = null;
        // 도구 설정·타임라인·캔버스는 화면 갱신으로 대상을 다시 읽는다.
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 피벗 도구가 고치는 레이어: 캔버스 대상이면 대표 레이어(<see cref="SpriteProject.PivotLayer"/> — 없으면 null = 캔버스 자신의 피벗),
    /// 아니면 지금 레이어.
    /// </summary>
    public Layer? PivotTargetLayer => Project is not { } p ? null : _pivotCanvas ? p.PivotLayer : CurrentLayerObject;

    /// <summary>피벗 대상 공간의 픽셀 크기: 부위면 부위 크기, 그 밖에는 캔버스 크기.</summary>
    public Size PivotTargetSize => PivotTargetLayer is { IsPart: true } part ? part.PartSize : Project?.Size ?? Size.Empty;

    /// <summary>대상의 저장된 현재 프레임 피벗(그 대상의 공간): 레이어면 그 레이어 피벗, 캔버스 자신이면 캔버스 자신의 피벗.</summary>
    public Pivot StoredPivot
        => Project is not { } p ? Pivot.Center : PivotTargetLayer is { } layer ? p.GetLayerPivot(layer, _frame) : p.GetCanvasOwnPivot(_frame);

    /// <summary>피벗 대상의 지금 피벗(끄는 중이면 미리보기 값, 대상의 공간).</summary>
    public Pivot Pivot => _pivotPreview ?? StoredPivot;

    /// <summary>대상이 프레임 <paramref name="frame"/>에 따로 둔 피벗이 있는지(부위는 옮긴 위치 포함 — 도구 설정의 "현재 프레임부터").</summary>
    public bool PivotTargetOwnsFrame(int frame)
    {
        if (Project is not { } p || (uint)frame >= (uint)p.Frames.Count) return false;
        return PivotTargetLayer is { } layer ? p.HasLayerFrameOverride(layer, frame) : p.Frames[frame].Pivot is not null;
    }

    /// <summary>대상 공간의 피벗을 캔버스 픽셀 좌표로(부위는 지금 프레임의 부위 위치를 더함).</summary>
    public System.Drawing.PointF PivotToCanvasPixel(Pivot pivot)
    {
        if (Project is not { } p) return System.Drawing.PointF.Empty;
        if (PivotTargetLayer is { IsPart: true } part)
        {
            var (x, y) = pivot.ToLocalPixel(part.PartSize.Width, part.PartSize.Height);
            var o = p.GetPartOffset(part, _frame);
            return new System.Drawing.PointF(o.X + x, o.Y + y);
        }
        var (cx, cy) = pivot.ToLocalPixel(p.Width, p.Height);
        return new System.Drawing.PointF(cx, cy);
    }

    /// <summary>캔버스 픽셀 좌표를 대상 공간의 피벗으로(<see cref="PivotToCanvasPixel"/>의 반대).</summary>
    public Pivot PivotFromCanvasPixel(float x, float y)
    {
        if (Project is not { } p) return Pivot.Center;
        if (PivotTargetLayer is { IsPart: true } part)
        {
            var o = p.GetPartOffset(part, _frame);
            return Pivot.FromLocalPixel(x - o.X, y - o.Y, part.PartSize.Width, part.PartSize.Height);
        }
        return Pivot.FromLocalPixel(x, y, p.Width, p.Height);
    }

    /// <summary>피벗 대상의 지금 피벗(미리보기 포함)의 캔버스 픽셀 좌표 — 캔버스 피벗 표시.</summary>
    public System.Drawing.PointF PivotPixel => PivotToCanvasPixel(Pivot);

    /// <summary>
    /// 지금 레이어의 피벗(<see cref="SpriteProject.LayerPivotPixel"/>, 그 레이어를 끄는 중이면 미리보기 값)을 지금 그리는 그림 좌표로(부위 화면이면 부위 기준) —
    /// 대칭 그리기 축과 변형 회전 중심.
    /// </summary>
    private (float X, float Y) CurrentLayerPivotLocal()
    {
        if (Project is not { } p || CurrentLayerObject is not { } layer) return (CanvasWidth / 2f, CanvasHeight / 2f);
        var at = _pivotPreview is { } preview && ReferenceEquals(layer, PivotTargetLayer) ? PivotToCanvasPixel(preview) : p.LayerPivotPixel(layer, _frame);
        if (!layer.IsPart) return (at.X, at.Y);
        var o = p.GetPartOffset(layer, _frame);
        return (at.X - o.X, at.Y - o.Y);
    }

    /// <summary>피벗 끄는 중: 기록 없이 화면만 바꾼다.</summary>
    public void PreviewPivot(Pivot pivot)
    {
        _pivotPreview = pivot;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>끌기를 마친 피벗을 대상에 기록한다(<see cref="WritePivot"/>).</summary>
    public void CommitPivot(Pivot before)
    {
        if (Project is not { } p || _pivotPreview is not { } after) return;
        _pivotPreview = null;
        if (after == before) { ViewChanged?.Invoke(this, EventArgs.Empty); return; }
        WritePivot(p, after);
    }

    /// <summary>도구 설정(숫자·5×5 단추)에서 고른 피벗을 대상에 기록한다(실행취소 한 단계).</summary>
    public void ApplyPivot(Pivot pivot)
    {
        if (Project is not { } p) return;
        Structural(() => WritePivot(p, pivot));
    }

    /// <summary>
    /// 대상 피벗을 바꾼다: 레이어면 그 레이어가 이 프레임에 따로 둔 것이 있으면 이 프레임부터 끝까지, 없으면 레이어 피벗을,
    /// 캔버스 자신이면 이 프레임에 따로 둔 피벗이 있으면 이 프레임부터 끝까지, 없으면 프로젝트 기본 피벗을 바꾼다.
    /// </summary>
    private void WritePivot(SpriteProject p, Pivot pivot)
    {
        if (PivotTargetLayer is { } layer)
        {
            int index = p.IndexOf(layer);
            if (p.HasLayerFrameOverride(layer, _frame)) p.SetLayerPivotFrom(index, _frame, pivot);
            else if (layer.Pivot != pivot) p.SetLayerPivot(index, pivot);
        }
        else if (p.Frames[_frame].Pivot is not null) p.SetFramePivotFrom(_frame, pivot);
        else if (p.Sprite.DefaultPivot != pivot)
            p.Edit(UiStrings.S_EditorContext_CommitPivot_463E10A971, ProjectChange.Settings, () => p.Sprite.DefaultPivot = pivot);
    }

    /// <summary>"현재 프레임부터": 대상을 이 프레임부터 끝까지 따로 두거나(<paramref name="own"/>) 되돌린다(실행취소 한 단계).</summary>
    public void SetPivotTargetFramesFrom(bool own)
    {
        if (Project is not { } p) return;
        int f = _frame;
        if (PivotTargetLayer is { } layer)
        {
            int index = p.IndexOf(layer);
            Structural(() => p.SetLayerFramesFrom(index, f, own));
        }
        else Structural(() => p.SetFramePivotsFrom(f, own));
    }

    /// <summary>타임라인 프레임 메뉴 "피벗을 기본값으로": 대상이 프레임 <paramref name="frame"/>에 따로 둔 피벗을 지운다.</summary>
    public void ResetPivotFrame(int frame)
    {
        if (Project is not { } p) return;
        if (PivotTargetLayer is { } layer)
        {
            int index = p.IndexOf(layer);
            Structural(() => p.SetLayerFramePivot(index, frame, null));
        }
        else Structural(() => p.SetFramePivot(frame, null));
    }

    /// <summary>타임라인 프레임 메뉴 "이 프레임만 피벗 따로 쓰기": 대상의 프레임 <paramref name="frame"/> 피벗을 그 프레임만의 피벗으로 둔다(모습은 그대로).</summary>
    public void KeepPivotFrame(int frame)
    {
        if (Project is not { } p) return;
        if (PivotTargetLayer is { } layer)
        {
            int index = p.IndexOf(layer);
            Structural(() => p.SetLayerFramePivot(index, frame, p.GetLayerPivot(layer, frame)));
        }
        else Structural(() => p.SetFramePivot(frame, p.GetCanvasOwnPivot(frame)));
    }

    /// <summary>캔버스 피벗의 대표 레이어를 고르거나(<paramref name="layer"/>) 끈다(null). 실행취소 한 단계.</summary>
    public void SetPivotLayer(Layer? layer)
    {
        if (Project is not { } p || ReferenceEquals(p.PivotLayer, layer)) return;
        _pivotPreview = null;
        Structural(() => p.SetPivotLayer(layer));
    }
}
