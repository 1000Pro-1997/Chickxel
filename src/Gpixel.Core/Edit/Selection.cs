using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>
/// 선택 영역(사각형 또는 원·자유·스마트 선택의 픽셀 모양)과 떠 있는(옮기는 중인) 픽셀.
/// <para>
/// 옮기기·뒤집기·회전은 먼저 선택 영역 픽셀을 이미지에서 떼어 <see cref="Floating"/>으로 만들고(원래 자리는 투명),
/// <see cref="Anchor"/>로 다시 이미지에 내려놓는다. 캔버스는 이미지 위에 <see cref="Floating"/>을
/// <see cref="Bounds"/> 위치에 겹쳐 그린다. 실행취소 단위로 묶으려면 떼기 전에
/// <see cref="SpriteProject.BeginCelStroke"/>로 받은 셀 이미지에 대해 동작하고, 내려놓은 뒤 <see cref="SpriteProject.CommitCelStroke"/>를 부른다.
/// </para>
/// <para>
/// 모양 선택이면 영역 밖 칸은 떼지도 지우지도 복사하지도 않는다(떠 있는 그림에서 투명). 모양은 옮기기·뒤집기·회전을 함께 따라간다.
/// </para>
/// </summary>
public sealed class Selection
{
    /// <summary>모양 선택의 칸 비트(<see cref="Bounds"/> 크기, 행 우선). null = 사각형 전체.</summary>
    private bool[]? _mask;

    /// <summary>선택을 감싸는 사각형(이미지 좌표). 떠 있는 동안은 이미지 밖으로 나갈 수 있다. 비었으면 선택 없음.</summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>떠 있는 픽셀(크기 = <see cref="Bounds"/> 크기, 모양 밖은 투명). 없으면 null.</summary>
    public PixelImage? Floating { get; private set; }

    public bool HasSelection => Bounds.Width > 0 && Bounds.Height > 0;
    public bool IsFloating => Floating is not null;

    /// <summary>사각형 선택인지(모양 비트 없음). 선택이 없어도 true.</summary>
    public bool IsRectangle => _mask is null;

    /// <summary>선택 영역(모양 포함). 선택이 없으면 빈 영역.</summary>
    public PixelMask Region => new(Bounds, _mask);

    /// <summary>선택이나 떠 있는 픽셀이 바뀌면 발생.</summary>
    public event EventHandler? Changed;

    /// <summary>사각형을 선택한다(이미지 안으로 잘림). 떠 있는 픽셀이 있으면 먼저 내려놓는다.</summary>
    public void Select(PixelImage image, Rectangle rect) => Select(image, new PixelMask(rect));

    /// <summary>
    /// 모양(원·자유·스마트 선택)을 선택한다. 이미지 안으로 잘리고 들어간 칸을 꼭 맞게 감싼다(모든 칸이 차면 사각형 선택).
    /// 떠 있는 픽셀이 있으면 먼저 내려놓는다.
    /// </summary>
    public void Select(PixelImage image, PixelMask region)
    {
        Anchor(image);
        SetRegion(region.Trim(image.Bounds));
        OnChanged();
    }

    public void SelectAll(PixelImage image) => Select(image, image.Bounds);

    /// <summary>선택 해제(떠 있는 픽셀은 내려놓음).</summary>
    public void Deselect(PixelImage image)
    {
        Anchor(image);
        SetRegion(default);
        OnChanged();
    }

    /// <summary>선택이 없으면 모든 점, 있으면 선택 안의 점만 true(그리기 제한용).</summary>
    public bool Contains(int x, int y) => !HasSelection || Region.Contains(x, y);

    /// <summary>선택 영역이 있고 그 안의 점인지(안쪽을 끌어 옮기기 등).</summary>
    public bool IsInside(Point p) => HasSelection && Region.Contains(p);

    /// <summary>그리기를 제한할 영역(선택이 없으면 null).</summary>
    public PixelMask? Clip => HasSelection ? Region : null;

    /// <summary>선택 내용을 복사한다(모양 밖은 투명, 선택이 없으면 null).</summary>
    public PixelImage? Copy(PixelImage image)
    {
        if (Floating is not null) return Floating.Clone();
        if (!HasSelection) return null;
        var copy = image.Crop(Bounds);
        if (_mask is not null)
            for (int i = 0; i < _mask.Length; i++)
                if (!_mask[i]) copy.Pixels[i] = Color32.Transparent;
        return copy;
    }

    /// <summary>복사한 뒤 선택 내용을 지운다.</summary>
    public PixelImage? Cut(PixelImage image)
    {
        var copy = Copy(image);
        if (copy is not null) Delete(image);
        return copy;
    }

    /// <summary>선택 내용을 지운다(떠 있으면 버리고, 아니면 영역을 투명으로). 선택은 유지된다.</summary>
    public void Delete(PixelImage image)
    {
        if (Floating is not null) Floating = null;
        else if (HasSelection) ClearRegion(image);
        OnChanged();
    }

    /// <summary>선택 영역 픽셀을 떼어 띄운다(원래 자리는 투명). 이미 떠 있으면 아무것도 안 함.</summary>
    public void Lift(PixelImage image)
    {
        if (Floating is not null || !HasSelection) return;
        Floating = Copy(image);
        ClearRegion(image);
        OnChanged();
    }

    /// <summary>떠 있는 픽셀을 현재 위치에 내려놓는다(투명 픽셀은 아래를 덮지 않음).</summary>
    public void Anchor(PixelImage image)
    {
        if (Floating is null) return;
        var f = Floating;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                var c = f.Pixels[y * f.Width + x];
                if (c.A != 0) image.Set(Bounds.X + x, Bounds.Y + y, c);
            }
        Floating = null;
        SetRegion(Region.Trim(image.Bounds));
        OnChanged();
    }

    /// <summary>선택 내용을 (dx,dy)만큼 옮긴다(필요하면 먼저 뗀다).</summary>
    public void MoveBy(PixelImage image, int dx, int dy)
    {
        if (!HasSelection || (dx == 0 && dy == 0)) return;
        Lift(image);
        Bounds = new Rectangle(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height);
        OnChanged();
    }

    /// <summary>
    /// 붙여넣기: 떠 있는 픽셀을 내려놓고 <paramref name="clip"/>을 <paramref name="at"/> 위치에 띄운다(사각형 선택).
    /// </summary>
    public void Paste(PixelImage image, PixelImage clip, Point at)
    {
        Anchor(image);
        Floating = clip.Clone();
        Bounds = new Rectangle(at, clip.Size);
        _mask = null;
        OnChanged();
    }

    public void FlipHorizontal(PixelImage image) => TransformFloating(image, ImageTransform.FlipHorizontal);

    public void FlipVertical(PixelImage image) => TransformFloating(image, ImageTransform.FlipVertical);

    /// <summary>선택 내용을 90° 회전한다. 중심을 유지하며 가로·세로가 바뀐다.</summary>
    public void Rotate90(PixelImage image, bool clockwise = true)
        => TransformFloating(image, f => ImageTransform.Rotate90(f, clockwise));

    /// <summary>
    /// 떠 있는 픽셀과 그 영역을 통째로 바꾼다(자유 변형 미리보기·결과). <paramref name="floating"/> 크기는 영역 사각형 크기와 같아야 한다.
    /// 이미지는 건드리지 않으므로 먼저 <see cref="Lift"/>로 떼어 둔다. 그림은 복사하지 않고 그대로 쓴다.
    /// </summary>
    public void SetFloating(PixelImage floating, PixelMask region)
    {
        if (region.Bounds.Width != floating.Width || region.Bounds.Height != floating.Height)
            throw new ArgumentException("떠 있는 픽셀 크기가 영역 사각형과 다릅니다.", nameof(floating));
        Floating = floating;
        SetRegion(region);
        OnChanged();
    }

    /// <summary>지금 선택 상태(영역·모양·떠 있는 픽셀)의 사본. 이미지는 담지 않는다.</summary>
    public SelectionState Save() => new(Bounds, (bool[]?)_mask?.Clone(), Floating?.Clone());

    /// <summary><see cref="Save"/>로 저장한 선택 상태로 되돌린다(이미지는 건드리지 않음). 같은 상태를 여러 번 되돌려도 된다.</summary>
    public void Restore(SelectionState state)
    {
        Bounds = state.Bounds;
        _mask = (bool[]?)state.Mask?.Clone();
        Floating = state.Floating?.Clone();
        OnChanged();
    }

    private void TransformFloating(PixelImage image, Func<PixelImage, PixelImage> transform)
    {
        if (!HasSelection) return;
        Lift(image);
        var result = transform(Floating!);
        // 모양도 그림과 똑같이 바꾼다.
        var shape = _mask is null ? null : transform(Region.ToImage());
        int cx = Bounds.X + Bounds.Width / 2, cy = Bounds.Y + Bounds.Height / 2;
        Floating = result;
        Bounds = result.Width == Bounds.Width
            ? new Rectangle(Bounds.Location, result.Size)
            : new Rectangle(cx - result.Width / 2, cy - result.Height / 2, result.Width, result.Height);
        if (shape is not null) _mask = PixelMask.FromImage(shape, Bounds.Location).Bits;
        OnChanged();
    }

    /// <summary>선택 영역 칸을 투명으로.</summary>
    private void ClearRegion(PixelImage image)
    {
        if (_mask is null)
        {
            image.FillRect(Bounds, Color32.Transparent);
            return;
        }
        for (int y = 0; y < Bounds.Height; y++)
            for (int x = 0; x < Bounds.Width; x++)
                if (_mask[y * Bounds.Width + x]) image.Set(Bounds.X + x, Bounds.Y + y, Color32.Transparent);
    }

    private void SetRegion(PixelMask region)
    {
        Bounds = region.Bounds;
        _mask = region.Bits;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary><see cref="Selection.Save"/>로 저장한 선택 상태: 영역 사각형, 모양 비트(null = 사각형), 떠 있는 픽셀(null = 없음).</summary>
public sealed record SelectionState(Rectangle Bounds, bool[]? Mask, PixelImage? Floating);
