using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Tests;

/// <summary>원본에서 가른 외곽선(<see cref="SourceOutline"/>, <see cref="OutlineEdits"/>)과 그것을 따르는 '외곽선 1px로 얇게'.</summary>
public class SourceOutlineTests
{
    /// <summary>원본 칸 크기(원본 px).</summary>
    private const int S = 4;
    private static readonly Color32 Black = new(0x28, 0x18, 0x2D);
    private static readonly Color32 Skin = new(0xFB, 0xC6, 0xCA);
    private static readonly Color32 Shirt = new(60, 110, 200);

    private static double L(Color32 c) => OkLab.FromColor(c).L;
    private static bool IsDark(Color32 c) => c.A != 0 && L(c) <= PaletteSettings.DefaultOutlineDarkness;

    /// <summary>칸 그림을 칸마다 <see cref="S"/>px로 키운 원본.</summary>
    private static PixelImage Upscale(PixelImage cells)
    {
        var img = new PixelImage(cells.Width * S, cells.Height * S);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                img[x, y] = cells[x / S, y / S];
        return img;
    }

    /// <summary>
    /// 신고된 그림 흉내(24×24칸): 위는 1칸 검은 외곽선을 두른 셔츠, 가운데는 넓은 검은 반바지(x 2~21, y 8~13 — 양옆은 바깥 외곽선과 한 덩어리),
    /// 아래는 1칸 외곽선을 두른 살색 다리 둘, 다리 사이는 반바지 안으로 두 줄 파고든 2칸 폭 투명 틈(x 11~12, y 12~20 — 아래 외곽선 줄로 막힘).
    /// </summary>
    private static PixelImage ShortsCells()
    {
        var img = new PixelImage(24, 24);
        img.FillRect(new Rectangle(2, 2, 20, 20), Black);
        img.FillRect(new Rectangle(3, 3, 18, 5), Shirt);
        img.FillRect(new Rectangle(3, 14, 7, 7), Skin);
        img.FillRect(new Rectangle(14, 14, 7, 7), Skin);
        img.FillRect(new Rectangle(11, 12, 2, 9), Color32.Transparent);
        return img;
    }

    /// <summary>20×20칸: 2칸 두께 검은 외곽선을 두른 살색 사각형.</summary>
    private static PixelImage ThickCells()
    {
        var img = new PixelImage(20, 20);
        img.FillRect(new Rectangle(2, 2, 16, 16), Black);
        img.FillRect(new Rectangle(4, 4, 12, 12), Skin);
        return img;
    }

    private static PixelateSettings Settings(bool sourceOutline) => new()
    {
        Grid = new PixelGridSettings { Mode = GridMode.Manual, Scale = S, LocalCorrection = false },
        Palette = new PaletteSettings { Colors = 3 },
        Cleanup = new CleanupSettings { SourceOutline = sourceOutline },
    };

    /// <summary>결과에서 원본 칸 (<paramref name="cx"/>, <paramref name="cy"/>)의 픽셀.</summary>
    private static Color32 At(PixelateResult r, int cx, int cy)
    {
        var map = r.Maps[0];
        Assert.True(map.Layout().Locate(cx * S + S / 2, cy * S + S / 2, out int c, out int row));
        return r.Frames[0].Get(c + map.OffsetX, row + map.OffsetY);
    }

    private static bool Outlined(PixelateSource src, int x, int y) => src.Outline()[y * src.Cleaned.Width + x];

    /// <summary>반바지 칸 중 결과에서 어둡지 않게 바뀐 칸.</summary>
    private static List<(int X, int Y)> RecoloredShorts(PixelateResult r)
    {
        var lost = new List<(int, int)>();
        for (int y = 8; y <= 13; y++)
            for (int x = 2; x <= 21; x++)
            {
                if (y >= 12 && x is 11 or 12) continue;
                if (!IsDark(At(r, x, y))) lost.Add((x, y));
            }
        return lost;
    }

    [Fact]
    public void ThickOutlineStillThinsToOnePixelWithSourceOutline()
    {
        var src = PixelateSource.Prepare(Upscale(ThickCells()), Settings(true));
        // 원본 외곽선: 2칸(8px) 띠 전체 — 모서리도 — 이고 안쪽 살색은 아님.
        foreach (var (x, y) in new[] { (8, 40), (15, 40), (40, 8), (40, 15), (8, 8), (12, 12), (71, 71), (64, 40) })
            Assert.True(Outlined(src, x, y), $"({x}, {y})");
        Assert.False(Outlined(src, 16, 40));
        Assert.False(Outlined(src, 40, 40));

        var on = Pixelator.Run(src, Settings(true));
        var off = Pixelator.Run(src, Settings(false));
        Assert.Equal(S, on.Scale);
        var (edge, thick) = PixelCleanup.OutlineThickness(on.Frames[0], PaletteSettings.DefaultOutlineDarkness);
        Assert.True(edge > 0);
        Assert.Equal(0, thick);
        Assert.Equal(1, on.Metrics.OutlineThin);
        for (int i = 4; i < 16; i++)
        {
            Assert.True(IsDark(At(on, 2, i)) && IsDark(At(on, i, 2)) && IsDark(At(on, 17, i)) && IsDark(At(on, i, 17)));
            Assert.False(IsDark(At(on, 3, i)));
            Assert.False(IsDark(At(on, i, 16)));
        }
        // 띠가 모두 원본 외곽선이면 예전과 같은 결과.
        Assert.Equal(off.Frames[0].Pixels, on.Frames[0].Pixels);
    }

    [Fact]
    public void WideBlackFillTouchingSmallGapKeepsItsBlack()
    {
        var src = PixelateSource.Prepare(Upscale(ShortsCells()), Settings(true));
        // 원본: 셔츠·다리 둘레의 1칸 외곽선은 외곽선, 반바지(틈 옆·한가운데·바깥에 닿은 옆)는 아님.
        Assert.True(Outlined(src, 9, 20));   // 셔츠 왼쪽 외곽선
        Assert.True(Outlined(src, 40, 9));   // 셔츠 위 외곽선
        Assert.True(Outlined(src, 42, 70));  // 왼쪽 다리의 틈 쪽 외곽선
        Assert.False(Outlined(src, 38, 49)); // 틈 바로 왼쪽 위 반바지(예전에 살색이 된 칸)
        Assert.False(Outlined(src, 40, 40)); // 반바지 한가운데
        Assert.False(Outlined(src, 9, 44));  // 바깥에 닿은 반바지 옆

        var on = Pixelator.Run(src, Settings(true));
        var off = Pixelator.Run(src, Settings(false));
        Assert.Equal(S, on.Scale);
        Assert.Empty(RecoloredShorts(on));
        // 예전(줄인 그림만 봄): 틈에 닿은 반바지 칸 안쪽이 두꺼운 외곽선의 안쪽 겹으로 보여 밝은 색(살색·셔츠)으로 벗겨졌다.
        var lost = RecoloredShorts(off);
        Assert.Contains((9, 12), lost);
        Assert.Contains((14, 12), lost);
        Assert.True(L(At(off, 9, 12)) > 0.6, $"{At(off, 9, 12)}");
        // 반바지를 벗기지 않으므로 '외곽선 1px' 점수도 그 자리를 두껍다고 세지 않는다.
        Assert.True(on.Metrics.OutlineThin > off.Metrics.OutlineThin - 1e-9);
        Assert.Equal(1, on.Metrics.OutlineThin);
    }

    [Fact]
    public void OutlineLightnessIsMeasuredFromSource()
    {
        // 2칸 외곽선이 L 0.27(설정의 기본 외곽선 밝기 0.24보다 밝음), 안쪽은 L 0.36 짙은 회색 면.
        var ink = new Color32(46, 30, 52);
        var fill = new Color32(66, 58, 72);
        var cells = new PixelImage(20, 20);
        cells.FillRect(new Rectangle(2, 2, 16, 16), ink);
        cells.FillRect(new Rectangle(4, 4, 12, 12), fill);
        var src = PixelateSource.Prepare(Upscale(cells), Settings(true));
        Assert.InRange(src.OutlineLightness, L(ink) + 0.01, L(fill) - 0.01);
        Assert.True(Outlined(src, 8, 40));
        Assert.True(Outlined(src, 15, 40));
        Assert.False(Outlined(src, 16, 40));
        Assert.False(Outlined(src, 40, 40));
        // 설정 밝기(0.24)로 갈랐다면 이 외곽선은 하나도 어둡지 않다.
        Assert.DoesNotContain(true, SourceOutline.Detect(src.Cleaned, src.Lab, src.Split, PaletteSettings.DefaultOutlineDarkness));

        // 아주 검은 외곽선이면 하한(예전 기본 외곽선 밝기).
        var black = new Color32(10, 10, 10);
        cells.FillRect(new Rectangle(2, 2, 16, 2), black);
        cells.FillRect(new Rectangle(2, 16, 16, 2), black);
        cells.FillRect(new Rectangle(2, 2, 2, 16), black);
        cells.FillRect(new Rectangle(16, 2, 2, 16), black);
        Assert.Equal(PaletteSettings.DefaultOutlineDarkness, PixelateSource.Prepare(Upscale(cells), Settings(true)).OutlineLightness);
    }

    [Fact]
    public void EditsChangeSourceOutlineAndCleanup()
    {
        var src = PixelateSource.Prepare(Upscale(ShortsCells()), Settings(true));
        int w = src.Cleaned.Width, h = src.Cleaned.Height;
        var auto = src.AutoOutline();
        // 칸 (9, 12) — 원본 (36~39, 48~51) — 를 외곽선으로 칠하면 그 칸은 벗겨진다.
        var cell = new List<(int X, int Y)>();
        for (int y = 48; y < 52; y++)
            for (int x = 36; x < 40; x++) cell.Add((x, y));
        var add = OutlineEdits.Empty(w, h).Paint(cell, 0, 1);
        src.OutlineEdits = add;
        Assert.True(Outlined(src, 37, 49));
        Assert.False(Outlined(src, 35, 49));
        Assert.False(auto[49 * w + 37]); // 자동 외곽선은 그대로
        Assert.Same(auto, src.AutoOutline());
        var added = Pixelator.Run(src, Settings(true));
        Assert.Equal(new[] { (9, 12) }, RecoloredShorts(added));

        // 셔츠 왼쪽 외곽선을 외곽선 아님으로 칠하면 원본 외곽선이 아니게 되고, 투명은 외곽선으로 칠해도 외곽선이 아니다. 앞 고침은 남는다.
        var removed = add.Paint([(9, 20), (0, 0)], 2, -1).Paint([(1, 1)], 0, 1);
        src.OutlineEdits = removed;
        Assert.False(Outlined(src, 9, 20));
        Assert.False(Outlined(src, 1, 1));
        Assert.True(Outlined(src, 37, 49));

        // 2칸 외곽선의 왼쪽 안쪽 겹을 외곽선 아님으로 칠하면 그 칸들은 벗기지 않고, 점수도 그 자리를 두껍다고 세지 않는다.
        var thickSrc = PixelateSource.Prepare(Upscale(ThickCells()), Settings(true));
        var inner = new List<(int X, int Y)>();
        for (int y = 16 + S; y < 64 - S; y++)
            for (int x = 12; x < 16; x++) inner.Add((x, y));
        var before = Pixelator.Run(thickSrc, Settings(true));
        thickSrc.OutlineEdits = OutlineEdits.Empty(thickSrc.Cleaned.Width, thickSrc.Cleaned.Height).Paint(inner, 0, -1);
        var kept = Pixelator.Run(thickSrc, Settings(true));
        for (int i = 5; i < 15; i++)
        {
            Assert.False(IsDark(At(before, 3, i)));
            Assert.True(IsDark(At(kept, 3, i)), $"row {i}");
            Assert.False(IsDark(At(kept, 16, i)));
        }
        Assert.Equal(0, PixelCleanup.OutlineThickness(before.Frames[0], PaletteSettings.DefaultOutlineDarkness).Thick);
        Assert.True(PixelCleanup.OutlineThickness(kept.Frames[0], PaletteSettings.DefaultOutlineDarkness).Thick > 0);
        Assert.Equal(1, kept.Metrics.OutlineThin);

        // 크기가 다른 고침은 받지 않는다.
        Assert.Throws<ArgumentException>(() => src.OutlineEdits = OutlineEdits.Empty(w + 1, h));
    }

    [Fact]
    public void EditsAreImmutableSnapshots()
    {
        var empty = OutlineEdits.Empty(130, 130);
        Assert.True(empty.IsEmpty);
        var one = empty.Paint([(5, 5)], 1, 1);
        Assert.True(empty.IsEmpty);
        Assert.Equal(0, empty.At(5, 5));
        Assert.False(one.IsEmpty);
        // 반지름 1 = 픽셀 중심이 1.5 안 — 3×3.
        Assert.Equal(1, one.At(5, 5));
        Assert.Equal(1, one.At(6, 6));
        Assert.Equal(1, one.At(4, 4));
        Assert.Equal(0, one.At(7, 5));
        Assert.Equal(0, one.At(-1, 5));

        var two = one.Paint([(5, 5)], 0, -1);
        Assert.Equal(1, one.At(5, 5));
        Assert.Equal(-1, two.At(5, 5));
        Assert.Equal(1, two.At(4, 5));

        // 64px 조각 경계를 넘는 원: 네 조각 모두 칠해지고 앞 묶음은 그대로.
        var cross = two.Paint([(63, 63)], 2, 1);
        foreach (var (x, y) in new[] { (63, 63), (64, 64), (61, 63), (65, 63), (63, 65), (64, 62) })
            Assert.Equal(1, cross.At(x, y));
        Assert.Equal(0, cross.At(61, 61)); // 2² + 2² > 2² + 2
        Assert.Equal(0, two.At(64, 64));
        Assert.Equal(-1, cross.At(5, 5));

        // 바뀐 것이 없으면 같은 묶음, 모두 자동으로 되돌리면 비었음.
        Assert.Same(cross, cross.Paint([(64, 64)], 0, 1));
        Assert.Same(cross, cross.Paint([(500, 500)], 3, -1));
        var reset = cross.Paint([(5, 5), (63, 63)], 3, 0);
        Assert.True(reset.IsEmpty);
        Assert.False(cross.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Paint([(1, 1)], 0, 2));
    }
}
