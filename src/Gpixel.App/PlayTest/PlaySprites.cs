using Gpixel.Core.Model;

namespace Gpixel.App.PlayTest;

/// <summary>화면에 그릴 한 프레임: 칸을 잘라 낸 비트맵 + 피벗 + 불투명 영역(모두 칸 기준 픽셀).</summary>
internal sealed class SpriteFrame(WriteableBitmap bitmap, float pivotX, float pivotY, Rectangle opaque) : IDisposable
{
    public WriteableBitmap Bitmap { get; } = bitmap;
    public int Width => Bitmap.PixelSize.Width;
    public int Height => Bitmap.PixelSize.Height;

    /// <summary>피벗 위치(칸 왼쪽 위 기준, y 아래가 +). 반전된 프레임이면 반전된 값.</summary>
    public float PivotX { get; } = pivotX;
    public float PivotY { get; } = pivotY;

    /// <summary>불투명 픽셀을 감싸는 상자(칸 기준). 완전히 투명하면 칸 전체.</summary>
    public Rectangle Opaque { get; } = opaque;

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// 움직임 테스트용 문서 스냅숏: 메모리에 있는 픽셀 복사본 + 애니메이션 복사본 + 프레임이 쓰는 칸 영역·피벗.
/// 편집기에서 문서를 고치는 동안에도 그리기가 흔들리지 않도록 <see cref="Load"/> 시점의 내용을 고정한다.
/// </summary>
internal sealed class PlaySprites : IDisposable
{
    /// <summary>Single 모드 이미지 전체(또는 애니메이션이 없을 때 첫 칸)를 가리키는 내부 이름.</summary>
    private const string StillKey = "\0still";

    private readonly Color32[] _pixels;
    private readonly int _imageWidth;
    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.Ordinal);
    private readonly List<string> _clipNames = new();
    private readonly Dictionary<string, (Rectangle Rect, Pivot Pivot)> _slices = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, bool Flip), SpriteFrame?> _cache = new();

    private PlaySprites(Color32[] pixels, int imageWidth, int ppu, string documentName, SpriteMode mode)
    {
        _pixels = pixels;
        _imageWidth = imageWidth;
        PixelsPerUnit = ppu;
        DocumentName = documentName;
        Mode = mode;
    }

    /// <summary>문서가 없을 때 쓰는 빈 스냅숏.</summary>
    public static PlaySprites Empty { get; } = new([], 0, (int)SpriteSettings.DefaultPixelsPerUnit, "", SpriteMode.Single) { HasDocument = false };

    public bool HasDocument { get; private init; } = true;
    public string DocumentName { get; }
    public SpriteMode Mode { get; }

    /// <summary>격자 한 칸 크기(픽셀, 정수로 반올림한 PPU).</summary>
    public int PixelsPerUnit { get; }

    /// <summary>프레임이 하나 이상 있는 애니메이션 이름(문서 순서).</summary>
    public IReadOnlyList<string> ClipNames => _clipNames;

    /// <summary>
    /// 애니메이션을 하나도 고를 수 없을 때 보여 줄 정지 프레임 이름(Single 모드면 이미지 전체, 아니면 첫 칸). 없으면 null.
    /// </summary>
    public string? StillFrame { get; private set; }

    /// <summary>정지 프레임 설명(상태 표시용).</summary>
    public string StillDescription { get; private set; } = "";

    public static PlaySprites Load(Document? doc)
    {
        if (doc is null) return Empty;
        var image = doc.Image;
        int ppu = Math.Max(1, (int)MathF.Round(doc.Sprite.PixelsPerUnit));
        var sprites = new PlaySprites((Color32[])image.Pixels.Clone(), image.Width, ppu, doc.DisplayName, doc.Sprite.Mode);
        var bounds = image.Bounds;

        foreach (var anim in doc.Animations)
        {
            if (anim.Frames.Count == 0 || sprites._clips.ContainsKey(anim.Name)) continue;
            var clip = anim.Clone();
            sprites._clips.Add(clip.Name, clip);
            sprites._clipNames.Add(clip.Name);
            foreach (var frame in clip.Frames)
            {
                if (sprites._slices.ContainsKey(frame.Slice)) continue;
                if (!doc.TryGetSliceRect(frame.Slice, out var rect, out var pivot)) continue;
                rect.Intersect(bounds);
                if (rect.Width > 0 && rect.Height > 0) sprites._slices.Add(frame.Slice, (rect, pivot));
            }
        }

        if (doc.Sprite.Mode == SpriteMode.Single)
        {
            sprites._slices[StillKey] = (bounds, doc.Sprite.DefaultPivot);
            sprites.StillFrame = StillKey;
            sprites.StillDescription = "이미지 전체";
        }
        else if (doc.Slices.Count > 0)
        {
            var first = doc.Slices[0];
            var rect = Rectangle.Intersect(first.Rect, bounds);
            if (rect.Width > 0 && rect.Height > 0)
            {
                sprites._slices[StillKey] = (rect, first.Pivot);
                sprites.StillFrame = StillKey;
                sprites.StillDescription = $"첫 칸 '{first.Name}'";
            }
        }
        return sprites;
    }

    /// <summary>프레임이 있는 애니메이션인지(<see cref="Gpixel.Core.Play.AnimationSelector"/>용).</summary>
    public bool HasClip(string name) => _clips.ContainsKey(name);

    public AnimationClip? GetClip(string name) => _clips.GetValueOrDefault(name);

    /// <summary>칸 이름(또는 <see cref="StillFrame"/>)의 비트맵. 칸을 찾을 수 없으면 null.</summary>
    public SpriteFrame? GetFrame(string sliceName, bool flipX)
    {
        var key = (sliceName, flipX);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var frame = _slices.TryGetValue(sliceName, out var s) ? Build(s.Rect, s.Pivot, flipX) : null;
        _cache[key] = frame;
        return frame;
    }

    private unsafe SpriteFrame Build(Rectangle rect, Pivot pivot, bool flipX)
    {
        int w = rect.Width, h = rect.Height;
        var bmp = ImageUtil.NewBitmap(w, h);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        using (var fb = bmp.Lock())
        {
            for (int y = 0; y < h; y++)
            {
                // BGRA(직선 알파) 비트맵은 Color32와 메모리 배치가 같다.
                var row = new Span<Color32>((byte*)fb.Address + (long)y * fb.RowBytes, w);
                _pixels.AsSpan((rect.Y + y) * _imageWidth + rect.X, w).CopyTo(row);
                if (flipX) row.Reverse();
                for (int x = 0; x < w; x++)
                {
                    if (row[x].A == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
            }
        }

        var opaque = maxX < 0 ? new Rectangle(0, 0, w, h) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        var (px, py) = pivot.ToLocalPixel(w, h);
        if (flipX) px = w - px;
        return new SpriteFrame(bmp, px, py, opaque);
    }

    public void Dispose()
    {
        foreach (var frame in _cache.Values) frame?.Dispose();
        _cache.Clear();
    }
}
