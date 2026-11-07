using Chickxel.App;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace GuideShots;

/// <summary>
/// AI 그림 픽셀로 정리 가이드의 비교 그림: "AI가 만든 듯한" 원본(<see cref="PixelateKit.MakeAiImage(ShotKit)"/>)을 실제 정리 파이프라인
/// (<see cref="Pixelator.Run(PixelImage, PixelateSettings, CancellationToken)"/>)으로 설정 하나만 바꿔 돌린 결과를 한 장에 나란히 놓는다
/// (왼쪽 = 끈 값·다른 값, 오른쪽 = 켠 값·기본값). 결과는 최근접으로 정수 배 확대해 픽셀이 또렷하다.
/// </summary>
internal static class GuidePixelateCompareShots
{
    private static readonly Color32 Back = new(43, 43, 43);
    private static readonly Color32 CheckerLight = new(204, 204, 204);
    private static readonly Color32 CheckerDark = new(170, 170, 170);
    private const int Margin = 16, Gap = 24, Checker = 8;
    /// <summary>얼굴 언저리(결과 49×98 기준) — 칸 색·팔레트 차이가 잘 보이는 곳.</summary>
    private static readonly (int X, int Y, int W, int H) Face = (5, 5, 40, 40);
    /// <summary>얼굴 아래부터 윗옷까지 — 디더 무늬가 잘 보이는 곳.</summary>
    private static readonly (int X, int Y, int W, int H) Chest = (5, 30, 40, 40);

    [Shot("pixelate-compare")]
    public static void Compare(ShotKit k)
    {
        var src = Decode(PixelateKit.MakeAiImage(k));
        var b = Base();
        PixelImage Run(PixelImage source, PixelateSettings s) => Pixelator.Run(source, s).CommonFrames()[0];

        // 장 첫머리: AI가 만든 원본 → 기본 프리셋 결과.
        var result = Run(src, b);
        Save(k, Row(Fit(src, result.Height * 4), Scale(result, 4)), "pixelate-compare");

        // 칸 색 고르기: 평균 ↔ 가장 많은 색(기본).
        Save(k, Row(Scale(Crop(Run(src, b with { Downsample = b.Downsample with { Mode = DownsampleMode.Mean } }), Face), 8), Scale(Crop(result, Face), 8)),
            "pixelate-palette-compare");
        // 팔레트 색: 평균 색 ↔ 원본 색(기본).
        Save(k, Row(Scale(Crop(Run(src, b with { Palette = b.Palette with { ColorSource = PaletteColorSource.Mean } }), Face), 8), Scale(Crop(result, Face), 8)),
            "pixelate-palette-compare-2");
        // 디더링: 6색 k-평균에서 없음 ↔ Bayer 4×4.
        var six = b with { Palette = b.Palette with { Method = PaletteMethod.KMeans, Colors = 6 } };
        Save(k, Row(Scale(Crop(Run(src, six), Chest), 8), Scale(Crop(Run(src, six with { Palette = six.Palette with { Dither = PaletteDither.Bayer4 } }), Chest), 8)),
            "pixelate-palette-compare-3");

        // 외곽선 1px 맞추기: 외곽선이 두 칸 두께인 원본에서 끔 ↔ 외곽선만 얇게.
        var thick = ThickSource(k);
        Save(k, Row(Scale(Run(thick, b with { Cleanup = b.Cleanup with { OutlineFit = OutlineFit.Off } }), 4),
                Scale(Run(thick, b with { Cleanup = b.Cleanup with { OutlineFit = OutlineFit.Thin } }), 4)),
            "pixelate-cleanup-compare");
    }

    /// <summary>예시 그림 바깥에 외곽선 색 한 겹을 더 둘러 외곽선이 두 칸 두께인 그림을 AI 그림처럼 키운 원본(두꺼운 외곽선의 AI 그림 흉내).</summary>
    private static PixelImage ThickSource(ShotKit k)
    {
        var art = PngIO.Load(ShotKit.SamplePng);
        var counts = new Dictionary<Color32, int>();
        bool Edge(int x, int y) => art[x, y].A > 0 && (x == 0 || y == 0 || x == art.Width - 1 || y == art.Height - 1
            || art[x - 1, y].A == 0 || art[x + 1, y].A == 0 || art[x, y - 1].A == 0 || art[x, y + 1].A == 0);
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
                if (Edge(x, y)) counts[art[x, y]] = counts.GetValueOrDefault(art[x, y]) + 1;
        var outline = counts.MaxBy(kv => kv.Value).Key;
        var grown = new PixelImage(art.Width + 2, art.Height + 2);
        for (int y = 0; y < grown.Height; y++)
            for (int x = 0; x < grown.Width; x++)
            {
                int sx = x - 1, sy = y - 1;
                if (sx >= 0 && sy >= 0 && sx < art.Width && sy < art.Height && art[sx, sy].A > 0) { grown[x, y] = art[sx, sy]; continue; }
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int nx = sx + dx, ny = sy + dy;
                    if (nx >= 0 && ny >= 0 && nx < art.Width && ny < art.Height && art[nx, ny].A > 0) grown[x, y] = outline;
                }
            }
        return Decode(PixelateKit.MakeAiImage(k, grown, 7.4, "Thick outline AI"));
    }

    private static PixelImage Decode(string path)
        => ImageUtil.DecodeAnimation(path, maxFrames: 1)?.Images[0] ?? throw new InvalidOperationException($"couldn't decode {path}");

    /// <summary>기본 프리셋의 시작 설정에 배경 지우기 '자동(테두리 색)'만 켠 한 장 설정(원본 배경이 단색이라).</summary>
    private static PixelateSettings Base()
    {
        var s = OptimizePreset.BuiltIns().First(p => p.BuiltIn == "plain").StartSettings();
        return s with
        {
            Background = s.Background with { Mode = BackgroundMode.Auto },
            Frames = s.Frames with { Mode = FrameSplitMode.Single },
        };
    }

    private static PixelImage Crop(PixelImage img, (int X, int Y, int W, int H) r)
    {
        var o = new PixelImage(r.W, r.H);
        for (int y = 0; y < r.H; y++)
            for (int x = 0; x < r.W; x++)
                if (r.X + x < img.Width && r.Y + y < img.Height) o[x, y] = img[r.X + x, r.Y + y];
        return o;
    }

    /// <summary>최근접 정수 배 확대.</summary>
    private static PixelImage Scale(PixelImage img, int k)
    {
        var o = new PixelImage(img.Width * k, img.Height * k);
        for (int y = 0; y < o.Height; y++)
            for (int x = 0; x < o.Width; x++)
                o[x, y] = img[x / k, y / k];
        return o;
    }

    /// <summary>높이 <paramref name="height"/>로 줄인다(칸 평균 — 원본 사진처럼 큰 그림용).</summary>
    private static PixelImage Fit(PixelImage img, int height)
    {
        int width = (int)Math.Round(img.Width * (double)height / img.Height);
        var o = new PixelImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int x0 = x * img.Width / width, x1 = Math.Max(x0 + 1, (x + 1) * img.Width / width);
                int y0 = y * img.Height / height, y1 = Math.Max(y0 + 1, (y + 1) * img.Height / height);
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = y0; sy < y1; sy++)
                    for (int sx = x0; sx < x1; sx++)
                    {
                        var c = img[sx, sy];
                        double w = c.A / 255.0;
                        r += c.R * w; g += c.G * w; b += c.B * w; a += w;
                    }
                int n = (x1 - x0) * (y1 - y0);
                o[x, y] = a <= 0 ? Color32.Transparent
                    : new Color32((byte)Math.Round(r / a), (byte)Math.Round(g / a), (byte)Math.Round(b / a), (byte)Math.Round(255 * a / n));
            }
        return o;
    }

    /// <summary>그림들을 위 맞춤으로 체커 칸 위에 나란히 놓은 한 장(어두운 바탕·여백).</summary>
    private static PixelImage Row(params PixelImage[] panels)
    {
        int height = panels.Max(p => p.Height);
        var img = new PixelImage(Margin * 2 + panels.Sum(p => p.Width) + Gap * (panels.Length - 1), Margin * 2 + height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                img[x, y] = Back;
        int left = Margin;
        foreach (var p in panels)
        {
            for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                    img[left + x, Margin + y] = Over(p[x, y], (x / Checker + y / Checker) % 2 == 0 ? CheckerLight : CheckerDark);
            left += p.Width + Gap;
        }
        return img;
    }

    private static Color32 Over(Color32 c, Color32 under)
    {
        if (c.A == 255) return c;
        double a = c.A / 255.0;
        byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a));
        return new Color32(Mix(c.R, under.R), Mix(c.G, under.G), Mix(c.B, under.B));
    }

    private static void Save(ShotKit k, PixelImage image, string name)
    {
        var file = Path.Combine(ShotKit.OutDir, name + ".png");
        Directory.CreateDirectory(ShotKit.OutDir);
        PngIO.Save(image, file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }
}
