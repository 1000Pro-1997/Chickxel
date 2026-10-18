using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>
/// 사각 선택 영역과 떠 있는(옮기는 중인) 픽셀.
/// <para>
/// 옮기기·뒤집기·회전은 먼저 선택 영역 픽셀을 이미지에서 떼어 <see cref="Floating"/>으로 만들고(원래 자리는 투명),
/// <see cref="Anchor"/>로 다시 이미지에 내려놓는다. 캔버스는 이미지 위에 <see cref="Floating"/>을
/// <see cref="Bounds"/> 위치에 겹쳐 그린다. 실행취소 단위로 묶으려면 떼기 전에
/// <see cref="SpriteProject.BeginCelStroke"/>로 받은 셀 이미지에 대해 동작하고, 내려놓은 뒤 <see cref="SpriteProject.CommitCelStroke"/>를 부른다.
/// </para>
/// </summary>
public sealed class Selection
{
    /// <summary>선택 사각형(이미지 좌표). 떠 있는 동안은 이미지 밖으로 나갈 수 있다. 비었으면 선택 없음.</summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>떠 있는 픽셀(크기 = <see cref="Bounds"/> 크기). 없으면 null.</summary>
    public PixelImage? Floating { get; private set; }

    public bool HasSelection => Bounds.Width > 0 && Bounds.Height > 0;
    public bool IsFloating => Floating is not null;

    /// <summary>선택이나 떠 있는 픽셀이 바뀌면 발생.</summary>
    public event EventHandler? Changed;

    /// <summary>사각형을 선택한다(이미지 안으로 잘림). 떠 있는 픽셀이 있으면 먼저 내려놓는다.</summary>
    public void Select(PixelImage image, Rectangle rect)
    {
        Anchor(image);
        Bounds = Rectangle.Intersect(rect, image.Bounds);
        OnChanged();
    }

    public void SelectAll(PixelImage image) => Select(image, image.Bounds);

    /// <summary>선택 해제(떠 있는 픽셀은 내려놓음).</summary>
    public void Deselect(PixelImage image)
    {
        Anchor(image);
        Bounds = Rectangle.Empty;
        OnChanged();
    }

    /// <summary>선택이 없으면 모든 점, 있으면 선택 안의 점만 true(그리기 제한용).</summary>
    public bool Contains(int x, int y) => !HasSelection || Bounds.Contains(x, y);

    /// <summary>그리기를 제한할 영역(선택이 없으면 null).</summary>
    public Rectangle? Clip => HasSelection ? Bounds : null;

    /// <summary>선택 내용을 복사한다(선택이 없으면 null).</summary>
    public PixelImage? Copy(PixelImage image)
    {
        if (Floating is not null) return Floating.Clone();
        return HasSelection ? image.Crop(Bounds) : null;
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
        else if (HasSelection) image.FillRect(Bounds, Color32.Transparent);
        OnChanged();
    }

    /// <summary>선택 영역 픽셀을 떼어 띄운다(원래 자리는 투명). 이미 떠 있으면 아무것도 안 함.</summary>
    public void Lift(PixelImage image)
    {
        if (Floating is not null || !HasSelection) return;
        Floating = image.Crop(Bounds);
        image.FillRect(Bounds, Color32.Transparent);
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
        Bounds = Rectangle.Intersect(Bounds, image.Bounds);
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
    /// 붙여넣기: 떠 있는 픽셀을 내려놓고 <paramref name="clip"/>을 <paramref name="at"/> 위치에 띄운다.
    /// </summary>
    public void Paste(PixelImage image, PixelImage clip, Point at)
    {
        Anchor(image);
        Floating = clip.Clone();
        Bounds = new Rectangle(at, clip.Size);
        OnChanged();
    }

    public void FlipHorizontal(PixelImage image) => TransformFloating(image, ImageTransform.FlipHorizontal);

    public void FlipVertical(PixelImage image) => TransformFloating(image, ImageTransform.FlipVertical);

    /// <summary>선택 내용을 90° 회전한다. 중심을 유지하며 가로·세로가 바뀐다.</summary>
    public void Rotate90(PixelImage image, bool clockwise = true)
        => TransformFloating(image, f => ImageTransform.Rotate90(f, clockwise));

    private void TransformFloating(PixelImage image, Func<PixelImage, PixelImage> transform)
    {
        if (!HasSelection) return;
        Lift(image);
        var result = transform(Floating!);
        int cx = Bounds.X + Bounds.Width / 2, cy = Bounds.Y + Bounds.Height / 2;
        Floating = result;
        Bounds = result.Width == Bounds.Width
            ? new Rectangle(Bounds.Location, result.Size)
            : new Rectangle(cx - result.Width / 2, cy - result.Height / 2, result.Width, result.Height);
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
