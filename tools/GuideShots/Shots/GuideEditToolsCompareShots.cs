using System.Drawing;
using Chickxel.App;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>
/// 편집 도구 쪽(tools-move·tools-transform·tools-nine-slice)의 비교 그림: 같은 원본을 앱·Core의 실제 코드로 두 번 처리해
/// 왼쪽(끔·다른 방식)과 오른쪽(켬·기본 방식)을 한 장에 크게(픽셀 그대로) 나란히 놓는다. 글자는 넣지 않는다(가이드 그림 설명에 적음).
/// </summary>
internal static class GuideEditToolsCompareShots
{
    private static readonly Color32 Back = new(32, 32, 36);
    private static readonly Color32 CheckA = new(206, 206, 212);
    private static readonly Color32 CheckB = new(234, 234, 238);
    private static readonly Color32 OutA = new(84, 84, 92);
    private static readonly Color32 OutB = new(100, 100, 108);
    private static readonly Color32 CanvasLine = new(60, 210, 255);
    private const int Pad = 16, Gap = 24, Check = 8;

    /// <summary>
    /// 이동 도구의 순환: 예시 그림 레이어를 오른쪽으로 20px 민 결과. 왼쪽 = 순환 끔(일반 레이어 — 밖으로 나간 부분은 캔버스 밖 그림으로 남아
    /// 이동 도구로 볼 때처럼 흐리게), 오른쪽 = 순환 켬(밖으로 나간 부분이 왼쪽으로 돌아옴). 하늘색 선 = 캔버스 테두리.
    /// </summary>
    [Shot("tools-move-compare")]
    public static void MoveWrap(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        var p = ToolFileHelp.Ctx(m).Project ?? throw new InvalidOperationException("no project");
        const int dx = 20, margin = 24, scale = 4;
        var original = (p.GetCel(0, 0) ?? throw new InvalidOperationException("empty sample")).Clone();
        var size = new Size(p.Width, p.Height);

        // 순환 켬: 이동 도구가 셀을 미는 코드(PixelOps.Shift, wrap).
        var wrapped = new PixelImage(size.Width, size.Height);
        PixelOps.Shift(original, wrapped, new Rectangle(Point.Empty, size), dx, 0, wrap: true);

        // 순환 끔: 일반 레이어 셀을 캔버스 밖 그림까지 통째로 미는 코드(SpriteProject.ShiftCels).
        if (p.ShiftCels("guide", [(0, 0)], dx, 0) == 0) throw new InvalidOperationException("nothing shifted");
        var inside = p.GetCel(0, 0) ?? throw new InvalidOperationException("empty cel");
        var outside = p.GetCelOverflow(0, 0) ?? throw new InvalidOperationException("no pixels outside the canvas");

        var area = new Rectangle(-margin, 0, size.Width + 2 * margin, size.Height);
        var left = CanvasPanel(area, size, scale, inside, outside);
        var right = CanvasPanel(area, size, scale, wrapped, null);
        Write(k, "tools-move-compare", left, right);
    }

    /// <summary>
    /// 변형 모드의 리샘플링: 예시 그림의 머리(7,3 ~ 42,36)를 변형 모드와 같은 코드(FreeTransform)로 30° 돌린 결과.
    /// 왼쪽 = 최근접, 오른쪽 = RotSprite(기본).
    /// </summary>
    [Shot("tools-transform-compare")]
    public static void Resampling(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        var p = ToolFileHelp.Ctx(m).Project ?? throw new InvalidOperationException("no project");
        var cel = p.GetCel(0, 0) ?? throw new InvalidOperationException("empty sample");
        var rect = new Rectangle(7, 3, 35, 33);
        var t = new FreeTransform(cel.Crop(rect), new PixelMask(rect));
        var param = t.Identity with { Angle = 30 };
        var nearest = t.Render(param, ResampleMode.Nearest);
        var rotSprite = t.Render(param, ResampleMode.RotSprite);
        var bounds = Rectangle.Union(nearest.Region.Bounds, rotSprite.Region.Bounds);
        bounds.Inflate(2, 2);
        const int scale = 6;
        Write(k, "tools-transform-compare", ImagePanel(bounds, scale, nearest), ImagePanel(bounds, scale, rotSprite));
    }

    /// <summary>
    /// 9-slice: 예시 UI 단추(32×20, 테두리 L5·T5·R5·B6)를 72×28로 늘인 결과(NineSlice.Scale — 캔버스 크기 모드의 9-slice로 늘리기와 같은 코드).
    /// 왼쪽 = 테두리 없이(그림 전체를 최근접), 오른쪽 = 9-slice.
    /// </summary>
    [Shot("tools-nine-slice-compare")]
    public static void NineSliceStretch(ShotKit k)
    {
        var src = PngIO.Load(ToolFileHelp.ButtonPng(Path.Combine(k.WorkDir, "button.png")));
        const int w = 72, h = 28, scale = 5;
        var plain = NineSlice.Scale(src, w, h, SpriteBorder.None, NineSliceFill.Stretch);
        var sliced = NineSlice.Scale(src, w, h, new SpriteBorder(5, 5, 5, 6), NineSliceFill.Stretch);
        var area = new Rectangle(-2, -2, w + 4, h + 4);
        Write(k, "tools-nine-slice-compare", ImagePanel(area, scale, new TransformResult(plain, new PixelMask(new Rectangle(0, 0, w, h)))),
            ImagePanel(area, scale, new TransformResult(sliced, new PixelMask(new Rectangle(0, 0, w, h)))));
    }

    /// <summary>캔버스(0,0 ~ <paramref name="canvas"/>)를 담은 <paramref name="area"/> 칸: 캔버스 안은 밝은 체크, 밖은 어두운 체크와 흐린(40%) 캔버스 밖 그림, 하늘색 캔버스 테두리.</summary>
    private static PixelImage CanvasPanel(Rectangle area, Size canvas, int scale, PixelImage cel, (PixelImage Image, Point Origin)? outside)
    {
        var img = new PixelImage(area.Width * scale, area.Height * scale);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int sx = area.X + x / scale, sy = area.Y + y / scale;
                bool inCanvas = sx >= 0 && sy >= 0 && sx < canvas.Width && sy < canvas.Height;
                bool odd = ((x / Check) + (y / Check)) % 2 == 1;
                img.Pixels[y * img.Width + x] = inCanvas ? (odd ? CheckA : CheckB) : (odd ? OutA : OutB);
            }
        var layer = new PixelImage(area.Width, area.Height);
        layer.Blit(cel, -area.X, -area.Y);
        var faded = new PixelImage(area.Width, area.Height);
        if (outside is { } o) faded.Blit(o.Image, o.Origin.X - area.X, o.Origin.Y - area.Y);
        img.Composite(Enlarge(faded, scale), 0, 0, 102);
        img.Composite(Enlarge(layer, scale), 0, 0, 255);
        var border = new Rectangle(-area.X * scale - 2, -area.Y * scale, canvas.Width * scale + 4, canvas.Height * scale);
        Frame(img, border, 2, CanvasLine);
        return img;
    }

    /// <summary><paramref name="area"/>(캔버스 좌표) 칸에 변형 결과를 체크 바탕 위에 놓는다.</summary>
    private static PixelImage ImagePanel(Rectangle area, int scale, TransformResult result)
    {
        var img = new PixelImage(area.Width * scale, area.Height * scale);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                img.Pixels[y * img.Width + x] = ((x / Check) + (y / Check)) % 2 == 1 ? CheckA : CheckB;
        var layer = new PixelImage(area.Width, area.Height);
        var at = result.Region.Bounds.Location;
        layer.Blit(result.Image, at.X - area.X, at.Y - area.Y);
        img.Composite(Enlarge(layer, scale), 0, 0, 255);
        return img;
    }

    /// <summary>최근접으로 정수배 확대(픽셀 그대로).</summary>
    private static PixelImage Enlarge(PixelImage src, int scale)
    {
        var big = new PixelImage(src.Width * scale, src.Height * scale);
        for (int y = 0; y < big.Height; y++)
            for (int x = 0; x < big.Width; x++)
                big.Pixels[y * big.Width + x] = src.Pixels[y / scale * src.Width + x / scale];
        return big;
    }

    /// <summary><paramref name="r"/> 둘레에 두께 <paramref name="width"/>의 선(그림 밖은 자름).</summary>
    private static void Frame(PixelImage img, Rectangle r, int width, Color32 color)
    {
        img.FillRect(new Rectangle(r.X, r.Y, r.Width, width), color);
        img.FillRect(new Rectangle(r.X, r.Bottom - width, r.Width, width), color);
        img.FillRect(new Rectangle(r.X, r.Y, width, r.Height), color);
        img.FillRect(new Rectangle(r.Right - width, r.Y, width, r.Height), color);
    }

    /// <summary>두 칸을 어두운 바탕에 여백·간격을 두고 나란히 놓아 <c>docs/assets/guide/&lt;name&gt;.png</c>로 저장한다.</summary>
    private static void Write(ShotKit k, string name, PixelImage left, PixelImage right)
    {
        int h = Math.Max(left.Height, right.Height);
        var sheet = new PixelImage(Pad * 2 + left.Width + Gap + right.Width, Pad * 2 + h);
        sheet.Clear(Back);
        sheet.Blit(left, Pad, Pad + (h - left.Height) / 2);
        sheet.Blit(right, Pad + left.Width + Gap, Pad + (h - right.Height) / 2);
        var file = Path.Combine(ShotKit.OutDir, name + ".png");
        Directory.CreateDirectory(ShotKit.OutDir);
        using (var bmp = ImageUtil.ToBitmap(sheet)) bmp.Save(file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }
}
