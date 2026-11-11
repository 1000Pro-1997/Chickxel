using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>
/// 그리기 도구 장(tools-*) 비교 그림: 같은 입력에 설정만 바꿔 Core 알고리즘(연필 획·픽셀 퍼펙트·브러시·밝게/어둡게·디더링·
/// 지우개 강도·채우기·그라데이션·도형 픽셀 잇기)이 실제로 칠한 결과를 한 장에 나란히 놓는다. 글자는 넣지 않고(한국어·영어 공용)
/// 무엇이 어디 있는지는 가이드 그림 설명에 적는다. 투명 칸은 회색 바둑판으로 보인다.
/// </summary>
internal static class GuidePaintToolsCompareShots
{
    private static readonly Color32 Back = new(38, 40, 48);
    private static readonly Color32 CheckA = new(204, 204, 204), CheckB = new(238, 238, 238);
    private static readonly Color32 Paper = new(244, 241, 232);
    private static readonly Color32 Ink = new(29, 43, 83);
    private static readonly Color32 Removed = new(229, 57, 53);
    private const int Pad = 12, Gap = 20;

    /// <summary>
    /// 픽셀 퍼펙트: 같은 손 그림 경로(천천히 끈 마우스 점을 <see cref="Raster.Line"/>으로 이은 크기 1 획)를 끄고(왼쪽) 켜고(오른쪽) 칠한다.
    /// 왼쪽에서 빨간 칸은 켰을 때 <see cref="PixelPerfectStroke"/>가 빼는 ㄱ자 겹 픽셀이다(끄면 그 칸도 같은 색으로 칠해짐).
    /// </summary>
    [Shot("tools-pencil-compare")]
    public static void PixelPerfectCompare(ShotKit k)
    {
        // 천천히 그린 S자 곡선: 마우스 위치를 촘촘히 받아 칸으로 내린 점(가로·세로로 한 칸씩 움직이며 ㄱ자 모서리가 생김).
        var mouse = new List<System.Drawing.Point>();
        for (double t = 0; t <= 1; t += 0.002)
        {
            double x = 2 + 50 * t;
            double y = 12 - 9.5 * Math.Sin(t * 2 * Math.PI) + 4 * (t - 0.5);
            var p = new System.Drawing.Point((int)Math.Floor(x), (int)Math.Floor(y));
            if (mouse.Count == 0 || mouse[^1] != p) mouse.Add(p);
        }
        var path = new List<System.Drawing.Point> { mouse[0] };
        for (int i = 1; i < mouse.Count; i++)
            foreach (var p in Raster.Line(mouse[i - 1], mouse[i]))
                if (path[^1] != p) path.Add(p);
        var kept = PixelPerfect.Filter(path).ToHashSet();

        var off = Blank(55, 25, Paper);
        var on = Blank(55, 25, Paper);
        foreach (var p in path) off.Set(p.X, p.Y, kept.Contains(p) ? Ink : Removed);
        foreach (var p in kept) on.Set(p.X, p.Y, Ink);
        Save(k, "tools-pencil-compare", 8, [[off, on]]);
    }

    /// <summary>
    /// 연필 픽셀 잇기: 빠르게 끈 S자 곡선(마우스 점이 몇 칸씩 떨어짐)을 대각선(<see cref="Raster.Line"/>, 왼쪽)과
    /// ㄱ자(<see cref="Raster.Line4"/>, 오른쪽 — 기본)로 이은 크기 1 획. 픽셀 퍼펙트는 끈 상태(켜면 대각선으로 잇는다).
    /// </summary>
    [Shot("tools-pencil-compare-steps")]
    public static void PencilStepsCompare(ShotKit k)
    {
        var mouse = new List<System.Drawing.Point>();
        for (double t = 0; t <= 1.0001; t += 1 / 12.0)
        {
            double x = 2 + 50 * t;
            double y = 12 - 9.5 * Math.Sin(t * 2 * Math.PI) + 4 * (t - 0.5);
            mouse.Add(new System.Drawing.Point((int)Math.Floor(x), (int)Math.Floor(y)));
        }
        var diagonal = Blank(55, 25, Paper);
        var orthogonal = Blank(55, 25, Paper);
        for (int i = 1; i < mouse.Count; i++)
        {
            foreach (var p in Raster.Line(mouse[i - 1], mouse[i])) diagonal.Set(p.X, p.Y, Ink);
            foreach (var p in Raster.Line4(mouse[i - 1], mouse[i])) orthogonal.Set(p.X, p.Y, Ink);
        }
        Save(k, "tools-pencil-compare-steps", 8, [[diagonal, orthogonal]]);
    }

    /// <summary>브러시 모양: 위에서부터 사각·원·마름모, 왼쪽부터 크기 2·3·4·5·7·9의 도장 하나(<see cref="Raster.Brush"/>).</summary>
    [Shot("tools-brush-compare")]
    public static void BrushShapes(ShotKit k)
    {
        int[] sizes = [2, 3, 4, 5, 7, 9];
        var rows = new List<List<PixelImage>>();
        foreach (var shape in new[] { BrushShape.Square, BrushShape.Circle, BrushShape.Diamond })
        {
            var row = new List<PixelImage>();
            foreach (int size in sizes)
            {
                var img = Blank(11, 11, Paper);
                Raster.Plot(img, Raster.Brush(5, 5, size, shape), Ink);
                row.Add(img);
            }
            rows.Add(row);
        }
        Save(k, "tools-brush-compare", 8, rows, gap: 10);
    }

    /// <summary>
    /// 밝게/어둡게 방식: 위에서부터 색조 이동·밝기만·HSV 명도·흰색·검정 섞기·채도. 색마다 가운데 칸이 원래 색이고,
    /// 왼쪽으로 오른쪽 버튼(어둡게, 채도는 흐리게) 1·2·3번, 오른쪽으로 왼쪽 버튼(밝게, 채도는 진하게) 1·2·3번 문지른 색(세기 20%).
    /// </summary>
    [Shot("tools-dodge-compare")]
    public static void DodgeModes(ShotKit k)
    {
        Color32[] bases = [new(190, 82, 60), new(86, 156, 74), new(66, 108, 196), new(128, 128, 128)];
        const int cell = 6, steps = 3;
        const float amount = 0.2f;
        var rows = new List<List<PixelImage>>();
        foreach (var mode in new[] { DodgeMode.HueShift, DodgeMode.Perceptual, DodgeMode.Value, DodgeMode.TintShade, DodgeMode.Saturation })
        {
            var row = new List<PixelImage>();
            foreach (var c in bases)
            {
                var strip = Blank(cell * (steps * 2 + 1), cell, Back);
                for (int i = -steps; i <= steps; i++)
                {
                    var color = c;
                    for (int n = 0; n < Math.Abs(i); n++) color = DodgeBurn.Apply(color, i > 0, amount, mode);
                    FillRect(strip, (i + steps) * cell, 0, cell, cell, color);
                }
                row.Add(strip);
            }
            rows.Add(row);
        }
        Save(k, "tools-dodge-compare", 4, rows, gap: 6);
    }

    /// <summary>디더링 무늬: 왼쪽부터 체커(50%)·성기게(25%)·촘촘하게(75%) — 기본색 = 진한 남색, 보조색 = 하늘색(<see cref="Raster.DitherUsesPrimary"/>).</summary>
    [Shot("tools-dither-compare")]
    public static void DitherPatterns(ShotKit k)
    {
        var primary = Ink;
        var secondary = new Color32(41, 173, 255);
        var row = new List<PixelImage>();
        foreach (var pattern in new[] { DitherPattern.Checker, DitherPattern.Quarter, DitherPattern.ThreeQuarters })
        {
            // 바깥 한 칸은 종이(무늬가 배경과 섞이지 않게), 안쪽 12×12에 캔버스 좌표 그대로 무늬를 칠한다.
            var img = Blank(14, 14, Paper);
            for (int y = 1; y < 13; y++)
                for (int x = 1; x < 13; x++)
                    img.Set(x, y, Raster.DitherUsesPrimary(x, y, pattern) ? primary : secondary);
            row.Add(img);
        }
        Save(k, "tools-dither-compare", 8, [row]);
    }

    /// <summary>
    /// 색 바꾸기 브러시: 하트(바깥선·빨강·흰 반짝임) 가운데를 크기 3 브러시로 왼쪽에서 오른쪽으로 문지른다.
    /// 왼쪽 = 원래 그림, 가운데 = 연필(닿은 칸을 모두 칠함), 오른쪽 = 색 바꾸기 브러시(처음 닿은 빨강만 바꿈). 칠하는 색은 초록.
    /// </summary>
    [Shot("tools-replace-brush-compare")]
    public static void ReplaceBrush(ShotKit k)
    {
        var heart = Heart();
        var green = new Color32(0, 168, 84);
        var stroke = Raster.Line(new System.Drawing.Point(1, 3), new System.Drawing.Point(13, 3));
        var pencil = heart.Clone();
        foreach (var p in stroke) Raster.Plot(pencil, Raster.Brush(p.X, p.Y, 3, BrushShape.Square), green);
        // 앱의 색 바꾸기 브러시와 같은 규칙: 획을 시작한 칸의 색(획 전 그림 기준)과 보이는 색이 같은 칸만 바꾼다.
        var replace = heart.Clone();
        var target = heart.Get(stroke[0].X, stroke[0].Y);
        foreach (var p in stroke)
            foreach (var m in Raster.Brush(p.X, p.Y, 3, BrushShape.Square))
                if (heart.Contains(m.X, m.Y) && Color32.SameVisible(heart.Get(m.X, m.Y), target)) replace.Set(m.X, m.Y, green);
        Save(k, "tools-replace-brush-compare", 10, [[heart, pencil, replace]]);
    }

    /// <summary>
    /// 지우개 강도·채널: 빨강·초록·파랑·흰색 띠의 가운데 가로줄(높이 4)을 지운다. 왼쪽부터 강도 100%, 50% 한 번, 50% 두 번(손을 뗐다가 다시),
    /// 강도 100%에 채널 R만(<see cref="PartialErase.Apply"/>).
    /// </summary>
    [Shot("tools-eraser-compare")]
    public static void EraserStrength(ShotKit k)
    {
        Color32[] bands = [new(228, 59, 68), new(62, 186, 76), new(41, 120, 230), new(250, 250, 250)];
        PixelImage Source()
        {
            var img = new PixelImage(16, 16);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    img.Set(x, y, bands[x / 4]);
            return img;
        }
        PixelImage Erase(int strength, int times, bool r, bool g, bool b)
        {
            var img = Source();
            for (int t = 0; t < times; t++)
                for (int y = 6; y < 10; y++)
                    for (int x = 0; x < 16; x++)
                        img.Set(x, y, PartialErase.Apply(img.Get(x, y), strength, r, g, b));
            return img;
        }
        Save(k, "tools-eraser-compare", 8,
            [[Erase(100, 1, true, true, true), Erase(50, 1, true, true, true), Erase(50, 2, true, true, true), Erase(100, 1, true, false, false)]]);
    }

    /// <summary>
    /// 채우기 비교 두 장. 1: 같은 노랑 칸 세 개 중 왼쪽 칸을 빨강으로 채움 — 왼쪽 = 이어진 영역만 끔(같은 색 전체), 오른쪽 = 켬.
    /// 2: 조금씩 다른 파랑 띠 다섯 줄의 가운데 줄을 채움 — 왼쪽 = 허용 오차 0%, 오른쪽 = 10%.
    /// </summary>
    [Shot("tools-fill-compare")]
    public static void FillCompare(ShotKit k)
    {
        var red = new Color32(228, 59, 68);
        var boxes = Blank(26, 10, Paper);
        var yellow = new Color32(255, 214, 92);
        foreach (int bx in new[] { 1, 9, 17 })
        {
            FillRect(boxes, bx, 1, 8, 8, Ink);
            FillRect(boxes, bx + 1, 2, 6, 6, yellow);
        }
        var all = boxes.Clone();
        Raster.FloodFill(all, 4, 4, red, contiguous: false);
        var contiguous = boxes.Clone();
        Raster.FloodFill(contiguous, 4, 4, red, contiguous: true);
        Save(k, "tools-fill-compare", 8, [[all, contiguous]]);

        // 채널마다 16씩 다른 파랑 띠 다섯 줄의 가운데를 누른다: 허용 오차 10%(= 채널 26)면 바로 위아래 줄(차이 16)까지 채우고 그 바깥(32)은 남는다.
        var bandsImg = Blank(16, 15, Paper);
        for (int i = 0; i < 5; i++)
        {
            byte d = (byte)(i * 16);
            FillRect(bandsImg, 0, i * 3, 16, 3, new Color32((byte)(40 + d), (byte)(90 + d), (byte)(170 + d)));
        }
        var exact = bandsImg.Clone();
        Raster.FloodFill(exact, 8, 7, red, tolerance: 0);
        var similar = bandsImg.Clone();
        Raster.FloodFill(similar, 8, 7, red, tolerance: (int)Math.Round(10 * 255 / 100.0));
        Save(k, "tools-fill-compare-2", 8, [[exact, similar]]);
    }

    /// <summary>
    /// 채우기 비교 방식: 색상(빨강·주황·초록·파랑·회색 — 위에서부터) × 밝기(왼쪽 어두움 → 오른쪽 밝음) 견본표의 가운데 빨강 칸을
    /// 이어진 영역만 끔, 같은 허용 오차로 채운다. 왼쪽부터 채널 최대 차 · RGB 거리 · OKLab · CIEDE2000 · 색상만(<see cref="FillColorMode"/>).
    /// </summary>
    [Shot("tools-fill-compare-3")]
    public static void FillModeCompare(ShotKit k)
    {
        Color32[] hues = [new(200, 40, 50), new(230, 120, 30), new(60, 170, 70), new(50, 90, 210), new(128, 128, 128)];
        const int cell = 3, steps = 5;
        var chart = Blank(cell * steps, cell * hues.Length, Paper);
        for (int r = 0; r < hues.Length; r++)
            for (int s = 0; s < steps; s++)
            {
                // 가운데(s = 2)가 원래 색, 왼쪽은 검정 쪽, 오른쪽은 흰색 쪽으로 30%씩 섞는다.
                double t = (s - 2) * 0.3;
                var c = hues[r];
                byte Mix(byte v) => (byte)Math.Round(t < 0 ? v * (1 + t) : v + (255 - v) * t);
                FillRect(chart, s * cell, r * cell, cell, cell, new Color32(Mix(c.R), Mix(c.G), Mix(c.B)));
            }
        var mark = new Color32(255, 0, 255);
        int tol = (int)Math.Round(25 * 255 / 100.0);
        var row = new List<PixelImage>();
        foreach (var mode in new[] { FillColorMode.ChannelMax, FillColorMode.RgbDistance, FillColorMode.OkLab, FillColorMode.Ciede2000, FillColorMode.Hue })
        {
            var img = chart.Clone();
            Raster.FloodFill(img, 2 * cell + 1, 1, mark, contiguous: false, tolerance: tol, mode: mode);
            row.Add(img);
        }
        Save(k, "tools-fill-compare-3", 8, [row], gap: 12);
    }

    /// <summary>
    /// 그라데이션 보간·디더링: 같은 세 정지점(남색 → 하늘색 → 크림, OKLCH)을 위에서부터 부드럽게 · 단계 · 부드럽게 + 2×2 Bayer · 부드럽게 + 4×4 Bayer로 칠한다.
    /// </summary>
    [Shot("tools-gradient-compare")]
    public static void GradientCompare(ShotKit k)
    {
        GradientStop[] stops = [new(0, Ink), new(0.5, new Color32(41, 173, 255)), new(1, new Color32(255, 241, 232))];
        PixelImage Bar(GradientInterpolation mode, GradientDither dither)
        {
            var img = new PixelImage(64, 8);
            Gradient.Apply(img, new System.Drawing.Point(0, 0), new System.Drawing.Point(63, 0), stops, mode, dither);
            return img;
        }
        Save(k, "tools-gradient-compare", 6,
        [
            [Bar(GradientInterpolation.Smooth, GradientDither.None)],
            [Bar(GradientInterpolation.Steps, GradientDither.None)],
            [Bar(GradientInterpolation.Smooth, GradientDither.Bayer2)],
            [Bar(GradientInterpolation.Smooth, GradientDither.Bayer4)],
        ], gap: 8);
    }

    /// <summary>도형의 픽셀 잇기: 같은 선 두 개와 곡선 하나를 왼쪽 = 대각선(기본), 오른쪽 = ㄱ자로 그린다(<see cref="Shapes.Connect"/>·<see cref="Shapes.Curve"/>).</summary>
    [Shot("tools-shape-compare")]
    public static void LineSteps(ShotKit k)
    {
        PixelImage Draw(bool orthogonal)
        {
            var img = Blank(30, 22, Paper);
            Raster.Plot(img, Shapes.Connect(new(1, 20), new(12, 2), orthogonal), Ink);
            Raster.Plot(img, Shapes.Connect(new(4, 20), new(26, 12), orthogonal), Ink);
            Raster.Plot(img, Shapes.Curve(new(14, 2), new(28, 9), new(25, 3), orthogonal: orthogonal), Ink);
            return img;
        }
        Save(k, "tools-shape-compare", 8, [[Draw(false), Draw(true)]]);
    }

    // ───────────── 도움 ─────────────

    /// <summary>
    /// 모양 펜 간격: 같은 물결 경로를 나뭇잎 세트(무작위, 씨앗 고정)로 간격 1(왼쪽 — 지나간 모든 픽셀에 찍어 겹친 자국)과
    /// 최적화 간격(오른쪽 — 앞 도장과 겹치지 않는 첫 자리마다)으로 끈 결과. 실제 <see cref="StampTrail"/>·<see cref="ShapeStamp"/>를 쓴다.
    /// </summary>
    [Shot("tools-shape-pen-compare")]
    public static void ShapePenSpacingCompare(ShotKit k)
    {
        var leaves = BuiltIn("leaves");
        var path = WavePath(6, 84, 15, 6);
        var dense = Blank(90, 30, Paper);
        var optimized = Blank(90, 30, Paper);
        StampAlong(dense, leaves, path, optimized: false, seed: 7);
        StampAlong(optimized, leaves, path, optimized: true, seed: 7);
        Save(k, "tools-shape-pen-compare", 4, [[dense, optimized]]);
    }

    /// <summary>모양 펜 기본 세트 다섯 가지: <see cref="StampBuiltIns.Ids"/> 차례로 왼쪽 위부터 한 줄에 두 세트(세트마다 모양을 나란히).</summary>
    [Shot("tools-shape-pen-sets")]
    public static void ShapePenSets(ShotKit k)
    {
        var strips = StampBuiltIns.Sets().Select(set =>
        {
            const int gap = 3;
            int w = set.Shapes.Sum(s => s.Width) + gap * (set.Shapes.Count + 1);
            int h = set.Shapes.Max(s => s.Height) + gap * 2;
            var strip = Blank(w, h, Paper);
            int x = gap;
            foreach (var s in set.Shapes)
            {
                int y = (h - s.Height) / 2;
                for (int sy = 0; sy < s.Height; sy++)
                    for (int sx = 0; sx < s.Width; sx++)
                        strip.Set(x + sx, y + sy, Color32.BlendOver(strip.Get(x + sx, y + sy), s.Get(sx, sy)));
                x += s.Width + gap;
            }
            return strip;
        }).ToList();
        var rows = new List<List<PixelImage>>();
        for (int i = 0; i < strips.Count; i += 2) rows.Add([.. strips.Skip(i).Take(2)]);
        // 줄마다 왼쪽 띠를 같은 너비로 맞춰 오른쪽 세트가 한 세로줄에 서게.
        int leftWidth = rows.Max(r => r[0].Width);
        foreach (var r in rows)
        {
            var padded = Blank(leftWidth, r[0].Height, Back);
            for (int y = 0; y < r[0].Height; y++)
                for (int x = 0; x < r[0].Width; x++) padded.Set(x, y, r[0].Get(x, y));
            r[0] = padded;
        }
        Save(k, "tools-shape-pen-sets", 3, rows, gap: 9);
    }

    /// <summary>모양 펜 예시: 연필 가지를 따라 나뭇잎(최적화 간격), 바닥에 풀(최적화 간격)과 떨어진 단풍잎, 하늘에 별을 찍은 그림.</summary>
    [Shot("tools-shape-pen-example")]
    public static void ShapePenExample(ShotKit k)
    {
        var img = Blank(120, 64, new Color32(214, 234, 246));
        var bark = new Color32(110, 74, 46);
        var branch = WavePath(4, 100, 22, 4);
        foreach (var p in branch) { img.Set(p.X, p.Y, bark); img.Set(p.X, p.Y + 1, bark); }
        StampAlong(img, BuiltIn("leaves"), [.. branch.Where((_, i) => i % 2 == 0).Select(p => new System.Drawing.Point(p.X, p.Y - 2))], optimized: true, seed: 3);
        StampAlong(img, BuiltIn("grass"), WavePath(2, 118, 58, 1), optimized: true, seed: 5);
        var autumn = BuiltIn("autumn");
        var picker = new StampPicker(11);
        int f = picker.First(autumn.Shapes.Count, autumn.Order);
        foreach (var x in new[] { 14, 37, 61, 88, 109 })
        {
            ShapeStamp.Stamp(img, autumn.Shapes[f], new System.Drawing.Point(x, 53));
            f = picker.Next(f, autumn.Shapes.Count, autumn.Order);
        }
        var stars = BuiltIn("stars");
        int s = 0;
        foreach (var (x, y) in new[] { (20, 8), (52, 6), (78, 11), (104, 5), (36, 38), (96, 36) })
        {
            ShapeStamp.Stamp(img, stars.Shapes[s], new System.Drawing.Point(x, y));
            s = (s + 1) % stars.Shapes.Count;
        }
        Save(k, "tools-shape-pen-example", 4, [[img]]);
    }

    private static StampSet BuiltIn(string id) => StampBuiltIns.Sets().First(s => s.Id == id);

    /// <summary>왼쪽 <paramref name="x0"/>에서 오른쪽 <paramref name="x1"/>까지 높이 <paramref name="y"/> 둘레로 <paramref name="amp"/>만큼 물결치는 마우스 점.</summary>
    private static List<System.Drawing.Point> WavePath(int x0, int x1, int y, double amp)
    {
        var path = new List<System.Drawing.Point>();
        for (int x = x0; x <= x1; x += 2)
            path.Add(new System.Drawing.Point(x, (int)Math.Round(y + amp * Math.Sin((x - x0) / 14.0))));
        return path;
    }

    /// <summary>
    /// 앱의 모양 펜 끌기와 같은 길: 첫 점에 찍고 <see cref="StampTrail"/>로 간격 1(<paramref name="optimized"/> 끔) 또는
    /// 최적화 간격(앞 도장과 <see cref="ShapeStamp.Overlaps"/>가 아닌 첫 자리)마다 세트 순서대로 다음 모양을 찍는다.
    /// </summary>
    private static void StampAlong(PixelImage img, StampSet set, List<System.Drawing.Point> path, bool optimized, int seed)
    {
        var picker = new StampPicker(seed);
        int index = picker.First(set.Shapes.Count, set.Order);
        PixelImage? last = null;
        void StampAt(System.Drawing.Point p)
        {
            var shape = set.Shapes[index];
            ShapeStamp.Stamp(img, shape, p);
            last = shape;
            index = picker.Next(index, set.Shapes.Count, set.Order);
        }
        Func<System.Drawing.Point, System.Drawing.Point, bool> due = optimized
            ? (a, b) => last is null || !ShapeStamp.Overlaps(last, a, set.Shapes[index], b)
            : StampTrail.Spacing(1);
        var trail = new StampTrail(path[0]);
        StampAt(path[0]);
        foreach (var p in path.Skip(1))
            foreach (var at in trail.MoveTo(p, due)) StampAt(at);
    }

    private static PixelImage Blank(int w, int h, Color32 fill)
    {
        var img = new PixelImage(w, h);
        img.Clear(fill);
        return img;
    }

    private static void FillRect(PixelImage img, int x, int y, int w, int h, Color32 c)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                img.Set(xx, yy, c);
    }

    /// <summary>11×10 하트(바깥선·빨강·흰 반짝임) — 색 바꾸기 브러시 예시.</summary>
    private static PixelImage Heart()
    {
        string[] rows =
        [
            ".ooo...ooo.....",
            "orrro.orrro....",
            "orwrrorrrro....",
            "orrrrrrrrro....",
            "orrrrrrrrro....",
            ".orrrrrrro.....",
            "..orrrrro......",
            "...orrro.......",
            "....oro........",
            ".....o.........",
        ];
        var img = new PixelImage(15, rows.Length);
        img.Clear(Paper);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                switch (rows[y][x])
                {
                    case 'o': img.Set(x, y, new Color32(59, 23, 38)); break;
                    case 'r': img.Set(x, y, new Color32(228, 59, 68)); break;
                    case 'w': img.Set(x, y, new Color32(255, 214, 214)); break;
                }
        return img;
    }

    /// <summary>
    /// 줄(<paramref name="rows"/>)마다 그림을 왼쪽에서 오른쪽으로 <paramref name="zoom"/>배 최근접으로 키워 늘어놓고(투명은 바둑판)
    /// docs/assets/guide/<paramref name="name"/>.png로 저장한다.
    /// </summary>
    private static void Save(ShotKit k, string name, int zoom, List<List<PixelImage>> rows, int gap = Gap)
    {
        int width = rows.Max(r => r.Sum(i => i.Width * zoom) + gap * (r.Count - 1)) + Pad * 2;
        int height = rows.Sum(r => r.Max(i => i.Height * zoom)) + gap * (rows.Count - 1) + Pad * 2;
        var sheet = Blank(width, height, Back);
        int y = Pad;
        foreach (var row in rows)
        {
            int x = Pad;
            foreach (var img in row)
            {
                for (int py = 0; py < img.Height * zoom; py++)
                    for (int px = 0; px < img.Width * zoom; px++)
                    {
                        var c = img.Get(px / zoom, py / zoom);
                        var check = ((px / 4 + py / 4) & 1) == 0 ? CheckA : CheckB;
                        sheet.Set(x + px, y + py, Color32.BlendOver(check, c));
                    }
                x += img.Width * zoom + gap;
            }
            y += row.Max(i => i.Height * zoom) + gap;
        }
        var file = Path.Combine(ShotKit.OutDir, name + ".png");
        Directory.CreateDirectory(ShotKit.OutDir);
        PngIO.Save(sheet, file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }
}
