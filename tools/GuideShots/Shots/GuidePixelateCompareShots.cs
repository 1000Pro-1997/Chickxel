using Chickxel.App;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace GuideShots;

/// <summary>
/// AI 그림 픽셀로 정리 가이드의 비교 그림: "AI가 만든 듯한" 원본(<see cref="PixelateKit.MakeAiImage(ShotKit)"/>)을 실제 정리 파이프라인
/// (<see cref="Pixelator.Run(PixelImage, PixelateSettings, CancellationToken)"/>)으로 설정 하나만 바꿔 돌린 결과를 한 장에 나란히 놓는다
/// (두 장 비교는 왼쪽 = 끈 값·다른 값, 오른쪽 = 켠 값·기본값; 옵션별 여러 장 비교는 드롭다운 순서대로). 결과는 최근접으로 정수 배 확대해 픽셀이 또렷하다.
/// 차이가 원본 그림에서 잘 안 보이는 옵션(칸 대표 색·칸 색 방식·색 거리)은 합성 칸·그러데이션을 실제 함수·파이프라인으로 돌린다.
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

        // 팔레트 색: 평균 색 ↔ 원본 색(기본).
        Save(k, Row(Scale(Crop(Run(src, b with { Palette = b.Palette with { ColorSource = PaletteColorSource.Mean } }), Face), 8), Scale(Crop(result, Face), 8)),
            "pixelate-palette-compare-2");

        // 외곽선 1px 맞추기: 외곽선이 두 칸 두께인 원본에서 끔 ↔ 외곽선만 얇게.
        var thick = ThickSource(k);
        Save(k, Row(Scale(Run(thick, b with { Cleanup = b.Cleanup with { OutlineFit = OutlineFit.Off } }), 4),
                Scale(Run(thick, b with { Cleanup = b.Cleanup with { OutlineFit = OutlineFit.Thin } }), 4)),
            "pixelate-cleanup-compare");
    }

    /// <summary>합성 칸 그림(<see cref="MixedCells"/>)의 칸 크기·열·행.</summary>
    private const int MixCell = 16, MixCols = 3, MixRows = 2;

    /// <summary>
    /// 여러 색이 걸친 AI 그림 칸 여섯(<see cref="MixCell"/>px 칸, 살짝 번지고 잡음 ±3): 왼쪽 위부터 외곽선|살색 반반, 가운데를 지나는 머리칼 위 어두운 가닥,
    /// 모서리에 외곽선이 번진 살색, 밝은|그늘 머리칼, 가운데에 하이라이트 든 눈동자, 고른 살색.
    /// </summary>
    private static PixelImage MixedCells()
    {
        Color32 outline = new(42, 30, 42), skin = new(255, 216, 184), hair = new(212, 212, 234), hairShade = new(138, 138, 176),
            strand = new(58, 52, 80), iris = new(74, 108, 204), white = new(255, 255, 255);
        Func<int, int, Color32>[] designs =
        [
            (x, _) => x < 7 ? outline : skin,
            (x, y) => Math.Abs(x - y) <= 1 ? strand : hair,
            (x, y) => x < 5 || y < 5 ? outline : skin,
            (_, y) => y < 9 ? hair : hairShade,
            (x, y) => x >= 6 && x < 10 && y >= 6 && y < 10 ? white : iris,
            (_, _) => skin,
        ];
        var sharp = new PixelImage(MixCell * MixCols, MixCell * MixRows);
        for (int y = 0; y < sharp.Height; y++)
            for (int x = 0; x < sharp.Width; x++)
                sharp[x, y] = designs[y / MixCell * MixCols + x / MixCell](x % MixCell, y % MixCell);
        // AI 그림처럼 살짝 번지고(가운데 무게 2의 3×3) 잡음 ±3.
        var rnd = new Random(7);
        var src = new PixelImage(sharp.Width, sharp.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                double r = 0, g = 0, bl = 0, sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var c = sharp[Math.Clamp(x + dx, 0, src.Width - 1), Math.Clamp(y + dy, 0, src.Height - 1)];
                        double w = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                        r += c.R * w; g += c.G * w; bl += c.B * w; sum += w;
                    }
                double n = rnd.NextDouble() * 6 - 3;
                src[x, y] = new Color32(Clamp(r / sum + n), Clamp(g / sum + n), Clamp(bl / sum + n));
            }
        return src;
    }

    /// <summary>칸 <paramref name="cell"/>px의 <see cref="MixCols"/>×<see cref="MixRows"/> 고른 칸 배치.</summary>
    private static CellLayout MixLayout(int cell)
        => new(Enumerable.Range(0, MixCols + 1).Select(i => i * cell).ToArray(), Enumerable.Range(0, MixRows + 1).Select(i => i * cell).ToArray());

    /// <summary>
    /// 점수의 '칸 대표 색'(<see cref="CellReference"/>): 합성 칸 여섯(<see cref="MixedCells"/>)을 칸마다 우세 색 묶음 ↔ 칸 평균으로 칠한다.
    /// 대표 색은 점수와 같은 <see cref="Pixelator.ReferenceColor"/>, 표본은 <c>Pixelator.Measure</c>와 같게(칸 안쪽 표본, 가운데 가중 피라미드).
    /// </summary>
    [Shot("pixelate-compare-cellref")]
    public static void CellReferenceCompare(ShotKit k)
    {
        var src = MixedCells();
        var layout = MixLayout(MixCell);
        var judge = FidelityJudge.Default;
        var samples = new Dictionary<(int, int), List<(Lab Color, double Weight)>>();
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                layout.Locate(x, y, out int col, out int row);
                var (x0, y0, x1, y1) = layout.Cell(col, row);
                if (!Inner(x, x0, x1) || !Inner(y, y0, y1)) continue;
                double weight = judge.CenterWeighted ? (x1 - x0 - Math.Abs(2 * x - x0 - x1 + 1)) * (double)(y1 - y0 - Math.Abs(2 * y - y0 - y1 + 1)) : 1;
                if (!samples.TryGetValue((col, row), out var list)) samples[(col, row)] = list = [];
                list.Add((OkLab.FromColor(src[x, y]), Math.Max(1, weight)));
            }
        PixelImage Paint(CellReference reference)
        {
            var refs = samples.ToDictionary(kv => kv.Key, kv => OkLab.ToColor(Pixelator.ReferenceColor(kv.Value, judge with { Reference = reference })));
            var o = new PixelImage(src.Width, src.Height);
            for (int y = 0; y < o.Height; y++)
                for (int x = 0; x < o.Width; x++)
                {
                    layout.Locate(x, y, out int col, out int row);
                    o[x, y] = refs[(col, row)];
                }
            return o;
        }
        const int s = 6;
        Save(k, Row(
                WithGrid(Scale(src, s), layout, s),
                WithGrid(Scale(Paint(CellReference.Dominant), s), layout, s),
                WithGrid(Scale(Paint(CellReference.Mean), s), layout, s)),
            "pixelate-score-compare-cellref");
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>칸 경계 둘레를 뺀 안쪽인지(<c>Pixelator.Measure</c>의 Inner와 같음 — 칸 크기의 1/8, 적어도 1px, 작은 칸은 1/4까지).</summary>
    private static bool Inner(int p, int lo, int hi)
    {
        int size = hi - lo;
        int q = Math.Min(size / 4, Math.Max(1, (int)Math.Round(size / 8.0)));
        return p >= lo + q && p < hi - q;
    }

    /// <summary><paramref name="s"/>배 확대한 칸 그림에 칸 경계를 반투명 흰 선으로 긋는다(체커 바탕 위에 합침).</summary>
    private static PixelImage WithGrid(PixelImage img, CellLayout layout, int s)
    {
        var o = new PixelImage(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int px = x / s, py = y / s;
                layout.Locate(px, py, out int c, out int r);
                bool line = false;
                if (x % s == 0 && x > 0) { layout.Locate(px - 1, py, out int lc, out int lr); line = lc != c || lr != r; }
                if (!line && y % s == 0 && y > 0) { layout.Locate(px, py - 1, out int uc, out int ur); line = uc != c || ur != r; }
                var p = Over(img[x, y], (x / Checker + y / Checker) % 2 == 0 ? CheckerLight : CheckerDark);
                o[x, y] = line ? Over(new Color32(255, 255, 255, 150), p) : p;
            }
        return o;
    }

    /// <summary>
    /// 팔레트 색 거리(<see cref="ColorDistanceMode"/>): 살구색·금색·빨강·파랑·회색·풀색의 밝음 → 어두움 그러데이션을 같은 10색 팔레트로 칸마다 가장 가까운 색으로 바꾼다
    /// (<see cref="ColorMetric.Nearest"/> — 칸이 팔레트 색을 고를 때와 같은 함수, 명암 계열 우선 끔).
    /// </summary>
    [Shot("pixelate-compare-distance")]
    public static void DistanceCompare(ShotKit k)
    {
        string[][] strips =
        [
            ["#fff0e2", "#ffd6b6", "#f2ae8e", "#d8866a", "#a85a44", "#5a2e28"],
            ["#fff4c0", "#f4d070", "#e0a030", "#a87018", "#5a3a10"],
            ["#ffd0d0", "#f07070", "#c83a3a", "#801c2a", "#40101a"],
            ["#d0e0ff", "#8aa8f0", "#4a6ccc", "#2a3c88", "#141a40"],
            ["#ffffff", "#c0c2cc", "#808494", "#404454", "#101014"],
            ["#e0f0c0", "#a0d080", "#5c9a4c", "#305828", "#102010"],
        ];
        const int width = 48, band = 8;
        var canvas = new PixelImage(width, strips.Length * band);
        for (int i = 0; i < strips.Length; i++)
        {
            var stops = strips[i].Select(h => Color32.TryParseHex(h, out var c) ? c : throw new FormatException(h)).ToArray();
            for (int x = 0; x < width; x++)
            {
                double t = x / (double)(width - 1) * (stops.Length - 1);
                int j = Math.Min((int)t, stops.Length - 2);
                double f = t - j;
                var a = stops[j];
                var z = stops[j + 1];
                var c = new Color32(Clamp(a.R + (z.R - a.R) * f), Clamp(a.G + (z.G - a.G) * f), Clamp(a.B + (z.B - a.B) * f));
                for (int y = 0; y < band; y++) canvas[x, i * band + y] = c;
            }
        }
        var palette = new[] { "#1a1820", "#343a58", "#8a8ea6", "#eceef4", "#ffdcc0", "#e09a7c", "#e8b040", "#c83a3a", "#4a6ccc", "#5c9a4c" }
            .Select(h => Color32.TryParseHex(h, out var c) ? c : throw new FormatException(h)).ToArray();
        PixelImage Map(ColorDistanceMode mode)
        {
            var metric = ColorMetric.For(new PaletteSettings { ColorDistance = mode, PreferShadeFamily = false });
            var points = palette.Select(c => metric.Point(c)).ToArray();
            var o = new PixelImage(canvas.Width, canvas.Height);
            for (int y = 0; y < canvas.Height; y++)
                for (int x = 0; x < canvas.Width; x++)
                    o[x, y] = palette[metric.Nearest(metric.Point(canvas[x, y]), points)];
            return o;
        }
        const int s = 6;
        Save(k, Row(Scale(canvas, s), Scale(Map(ColorDistanceMode.OkLab), s), Scale(Map(ColorDistanceMode.Ciede2000), s), Scale(Map(ColorDistanceMode.OkLchHue), s)),
            "pixelate-palette-compare-distance");
    }

    /// <summary>얼굴 가운데(결과 49×98 기준) — 팔레트 방식 차이를 여러 장 나란히 볼 때.</summary>
    private static readonly (int X, int Y, int W, int H) FaceTight = (9, 13, 32, 30);
    /// <summary>치마 아래부터 발까지(결과 칸 기준) — 외곽선 정리 차이를 볼 곳.</summary>
    private static readonly (int X, int Y, int W, int H) Legs = (4, 60, 42, 38);

    /// <summary>설정 하나씩 바꾼 비교: 칸 색 방식 6가지(합성 칸), 팔레트 방식 5가지, 디더 3가지, 외곽선 정리 3가지.</summary>
    [Shot("pixelate-compare-options")]
    public static void OptionsCompare(ShotKit k)
    {
        var src = Decode(PixelateKit.MakeAiImage(k));
        var b = Base();
        PixelateResult Run(PixelateSettings s) => Pixelator.Run(src, s);
        PixelImage Frame(PixelateSettings s) => Run(s).CommonFrames()[0];

        // 칸 색 방식: 합성 칸 여섯을 16px 수동 격자로 나눠 칸 색만 고른 결과(배경 지우기·팔레트·정리 끔 — 칸 색 방식 말고는 바뀌는 것이 없게).
        var mix = MixedCells();
        var cellOnly = b with
        {
            Background = b.Background with { Mode = BackgroundMode.None },
            Grid = b.Grid with { Mode = GridMode.Manual, Scale = MixCell, OffsetX = 0, OffsetY = 0, LocalCorrection = false },
            Size = b.Size with { Mode = OutputSizeMode.FromScale, Padding = 0 },
            Palette = b.Palette with { Method = PaletteMethod.KeepOriginal, SnapOutline = false },
            Cleanup = new CleanupSettings { RemoveOrphans = false, FillHoles = false, OutlineFit = OutlineFit.Off },
        };
        DownsampleMode[] modes = [DownsampleMode.Mode, DownsampleMode.Median, DownsampleMode.Center, DownsampleMode.Mean, DownsampleMode.CenterWeighted, DownsampleMode.ClusterWeighted];
        const int ms = 4;
        var cellPanels = modes.Select(md => WithGrid(Scale(Pixelator.Run(mix, cellOnly with { Downsample = cellOnly.Downsample with { Mode = md } }).CommonFrames()[0], MixCell * ms), MixLayout(1), MixCell * ms));
        Save(k, Row([WithGrid(Scale(mix, ms), MixLayout(MixCell), ms), .. cellPanels]), "pixelate-palette-compare-cellmode");

        // 팔레트 방식: 11색 안팎(k-평균·중앙 분할 12색 → 쓰인 색 11, OKLCH 묶기 범위 1.5배, 색 거리로 묶기 반경 0.09).
        var methods = new[]
        {
            Run(b with { Palette = b.Palette with { Method = PaletteMethod.KMeans, Colors = 12 } }),
            Run(b with { Palette = b.Palette with { Method = PaletteMethod.MedianCut, Colors = 12 } }),
            Run(b with { Palette = b.Palette with { Method = PaletteMethod.KeepOriginal } }),
            Run(b with { Palette = b.Palette with { Method = PaletteMethod.OkLch, OkLchMaxL = 0.15, OkLchMaxC = 0.15, OkLchMaxH = 30 } }),
            Run(b with { Palette = b.Palette with { Method = PaletteMethod.DistanceGroups, GroupRadius = 0.09 } }),
        };
        Save(k, Row(methods.Select(r => Scale(Crop(r.CommonFrames()[0], FaceTight), 7)).ToArray()), "pixelate-palette-compare-method");

        var six = b with { Palette = b.Palette with { Method = PaletteMethod.KMeans, Colors = 6 } };
        PaletteDither[] dithers = [PaletteDither.None, PaletteDither.Bayer2, PaletteDither.Bayer4];
        Save(k, Row(dithers.Select(d => Scale(Crop(Frame(six with { Palette = six.Palette with { Dither = d } }), Chest), 8)).ToArray()),
            "pixelate-palette-compare-dither");

        // 외곽선 정리: 닫기는 바깥에 칸을 더해 캔버스가 커질 수 있어, 칸 자리(Maps 오프셋)로 같은 곳을 자른다.
        var tinted = TintedOutlineSource(k);
        OutlineMode[] outlines = [OutlineMode.None, OutlineMode.UnifyDarkest, OutlineMode.EnsureClosed];
        Save(k, Row(outlines.Select(o =>
            {
                var r = Pixelator.Run(tinted, b with { Cleanup = b.Cleanup with { Outline = o } });
                return Scale(Crop(r.Frames[0], (Legs.X + r.Maps[0].OffsetX, Legs.Y + r.Maps[0].OffsetY, Legs.W, Legs.H)), 6);
            }).ToArray()),
            "pixelate-cleanup-compare-outline");
    }

    /// <summary>
    /// 예시 그림의 바깥 외곽선을 안쪽 칠 색의 어두운 칠(OKLab L 0.30, 색 기운 그대로 — 색마다 다른 외곽선)로 바꾸고 오른쪽 아래(x ≥ 25, y ≥ 60)는 외곽선을 빼
    /// (안쪽 칠 색) AI 그림처럼 키운 원본(외곽선 색이 들쭉날쭉하고 군데군데 빠진 AI 그림 흉내).
    /// </summary>
    private static PixelImage TintedOutlineSource(ShotKit k)
    {
        var art = PngIO.Load(ShotKit.SamplePng);
        bool Edge(int x, int y) => art[x, y].A > 0 && (x == 0 || y == 0 || x == art.Width - 1 || y == art.Height - 1
            || art[x - 1, y].A == 0 || art[x + 1, y].A == 0 || art[x, y - 1].A == 0 || art[x, y + 1].A == 0);
        var tinted = art.Clone();
        for (int y = 1; y < art.Height - 1; y++)
            for (int x = 1; x < art.Width - 1; x++)
            {
                if (!Edge(x, y)) continue;
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    var inner = art[x + dx, y + dy];
                    if (inner.A == 0 || Edge(x + dx, y + dy)) continue;
                    var lab = OkLab.FromColor(inner);
                    tinted[x, y] = x >= 25 && y >= 60 ? inner : OkLab.ToColor(new Lab(0.30f, lab.A, lab.B));
                    break;
                }
            }
        return Decode(PixelateKit.MakeAiImage(k, tinted, 7.4, "Tinted outline AI"));
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
