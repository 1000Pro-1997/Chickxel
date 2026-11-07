using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Chickxel.App;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>
/// 화면 구성 장의 비교 그림: 같은 장면을 설정만 바꿔 실제 앱(캔버스)이나 Core 알고리즘으로 그린 결과를 한 장에 나란히 놓는다
/// (왼쪽 = 끔, 오른쪽 = 켬). 빨간 번호는 붙이지 않는다 — 가이드 그림 설명에 "왼쪽: … · 오른쪽: …"으로 적는다.
/// </summary>
internal static class GuideScreenUiCompareShots
{
    private static readonly IBrush Gap = new SolidColorBrush(Color.FromRgb(24, 24, 27));
    private const int GapWidth = 16;

    /// <summary>양파 껍질: 몸 레이어 5번 프레임(hit 흔들림)을 양파 껍질 끔 / 켬(앞 4번 빨강, 뒤 6번 파랑으로 비침)으로.</summary>
    [Shot("screen-top-bar-compare-onion")]
    public static void Onion(ShotKit k)
    {
        var main = Open(k, height: 1100);
        Build(k, main);
        var canvas = ShotKit.Get<PixelCanvas>(main, "_canvas");
        var ctx = Ctx(main);
        var p = Project(main);
        ctx.SetCurrent(p.IndexOf(p.Layers.First(l => l.Name == "Body")), 4);
        canvas.SetZoom(5);
        var area = CanvasPixels(main, canvas, 0, 0, p.Width, p.Height).Inflate(10);
        ctx.OnionSkin = false;
        k.Settle(500);
        var off = k.Capture(main);
        ctx.OnionSkin = true;
        canvas.InvalidateVisual();
        k.Settle(500);
        var on = k.Capture(main);
        Compose(k, k.Id, [(off, area), (on, area)]);
    }

    /// <summary>픽셀 격자·타일 격자: 같은 확대(8배)에서 둘 다 끔 / 둘 다 켬. 얼굴·몸 둘레 32×32 픽셀(타일 격자 16 → 2×2칸).</summary>
    [Shot("screen-top-bar-compare-grid")]
    public static void Grid(ShotKit k)
    {
        var main = Open(k, height: 1100);
        Build(k, main);
        var canvas = ShotKit.Get<PixelCanvas>(main, "_canvas");
        ShotKit.Call(main, "SetPixelGrid", false);
        ShotKit.Call(main, "SetTileGrid", false);
        canvas.SetZoom(8);
        k.Settle(500);
        var area = CanvasPixels(main, canvas, 8, 32, 32, 32);
        var off = k.Capture(main);
        ShotKit.Call(main, "SetPixelGrid", true);
        ShotKit.Call(main, "SetTileGrid", true);
        k.Settle(500);
        var on = k.Capture(main);
        Compose(k, k.Id, [(off, area), (on, area)]);
    }

    /// <summary>
    /// 팔레트 색으로 변환의 디더링: 부드럽게 칠한 공(원본) → DB32의 파란 계열 6색으로 '없음' / '순서 디더링(Bayer 4×4)'.
    /// 변환은 대화상자와 같은 <see cref="PaletteRemap"/>로 하고 6배(최근접)로 키워 놓는다.
    /// </summary>
    [Shot("screen-palette-compare-dither")]
    public static void Dither(ShotKit k)
    {
        const int size = 40, zoom = 6;
        var src = new PixelImage(size, size);
        double c = (size - 1) / 2.0, r = size / 2.0 - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x - c) / r, dy = (y - c) / r, d2 = dx * dx + dy * dy;
                if (d2 > 1) continue;
                double dz = Math.Sqrt(1 - d2);
                // 왼쪽 위에서 오는 빛(램버트) + 어두운 바탕 빛.
                double light = Math.Clamp(0.15 + 0.85 * Math.Max(0, -0.5 * dx - 0.55 * dy + 0.67 * dz), 0, 1);
                static byte L(double a, double b, double t) => (byte)Math.Round(a + (b - a) * t);
                src.Pixels[y * size + x] = new Color32(L(20, 215, light), L(24, 228, light), L(60, 255, light));
            }
        Color32[] palette = new[] { "222034", "3F3F74", "306082", "5B6EE1", "639BFF", "CBDBFC" }
            .Select(h => Color32.TryParseHex(h, out var col) ? col : throw new InvalidOperationException(h)).ToArray();
        var remap = new PaletteRemap(palette);
        var none = src.Clone();
        remap.Apply(none);
        var bayer = src.Clone();
        remap.Apply(bayer, PaletteDither.Bayer4);
        var images = new[] { src, none, bayer }.Select(img => (Bitmap)ImageUtil.ToBitmap(img)).ToList();
        var area = new Rect(0, 0, size, size);
        Compose(k, k.Id, images.Select(b => (b, area)).ToList(), zoom);
    }

    /// <summary>색 패널 맨 아래 ⑪ 레이어 색: 예시 그림(몸 레이어)에 쓰인 색을 많이 쓰인 순으로(색 고르기 쪽 번호에 이어서).</summary>
    [Shot("screen-color-picker-5")]
    public static void LayerColors(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var colors = ShotKit.Get<ColorPanel>(main, "_colors");
        colors.LayerColorsCollapsed = false;
        k.Settle(800);
        var editor = ShotKit.Get<Avalonia.Controls.Control>(colors, "_editor");
        var layer = ShotKit.Get<Avalonia.Controls.Control>(colors, "_layerColors");
        var area = ShotKit.Bounds(editor, main).Union(ShotKit.Bounds(layer, main)).Inflate(2);
        ScreenShot.Shot(k, main, null, area, [ScreenShot.Left(ShotKit.Bounds(layer, main))], 11, only: [area]);
    }

    /// <summary>캔버스 픽셀 사각형(지금 보이는 대상 기준) → 창 좌표.</summary>
    private static Rect CanvasPixels(MainForm main, PixelCanvas canvas, double x, double y, double w, double h)
    {
        var off = ShotKit.Get<System.Drawing.Point>(canvas, "_offset");
        int z = canvas.Zoom;
        var c = ShotKit.Bounds(canvas, main);
        return new Rect(c.X + off.X + x * z, c.Y + off.Y + y * z, w * z, h * z);
    }

    /// <summary>그림마다 <c>area</c>를 잘라 <paramref name="scale"/>배(최근접)로 왼쪽부터 나란히 놓고 사이를 어두운 띠로 나눠 저장한다.</summary>
    private static void Compose(ShotKit k, string name, IReadOnlyList<(Bitmap Image, Rect Area)> panels, int scale = 1)
    {
        var rects = panels.Select(p => new Rect(Math.Floor(p.Area.X), Math.Floor(p.Area.Y), Math.Round(p.Area.Width), Math.Round(p.Area.Height))).ToList();
        int width = (int)rects.Sum(r => r.Width * scale) + GapWidth * (panels.Count + 1);
        int height = (int)rects.Max(r => r.Height * scale) + GapWidth * 2;
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            ctx.FillRectangle(Gap, new Rect(0, 0, width, height));
            double x = GapWidth;
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                for (int i = 0; i < panels.Count; i++)
                {
                    var r = rects[i];
                    ctx.DrawImage(panels[i].Image, r, new Rect(x, GapWidth, r.Width * scale, r.Height * scale));
                    x += r.Width * scale + GapWidth;
                }
        }
        k.Write(target, name);
    }
}
