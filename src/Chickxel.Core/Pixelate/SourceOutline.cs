using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Pixelate;

/// <summary>
/// 원본 외곽선의 사용자 고침(원본 px 좌표 — <see cref="PixelateSource.Cleaned"/>·<see cref="PixelateSource.Original"/>와 같은 크기)을 담은 바뀌지 않는 묶음.
/// 값은 1 = 외곽선으로, −1 = 외곽선 아님, 0 = 자동. <see cref="Paint"/>는 새 묶음을 돌려주고 이 묶음은 그대로라(되돌리기에 그대로 둘 수 있음),
/// 64×64 조각마다 따로 두어 바뀐 조각만 새로 만든다(큰 원본에서도 붓질마다 전체를 베끼지 않음).
/// </summary>
public sealed class OutlineEdits
{
    private const int TileShift = 6, TileSize = 1 << TileShift, TileMask = TileSize - 1;

    private readonly sbyte[]?[] _tiles;
    private readonly int _tilesX;
    private readonly int _count;

    private OutlineEdits(int width, int height, int tilesX, sbyte[]?[] tiles, int count)
    {
        Width = width;
        Height = height;
        _tilesX = tilesX;
        _tiles = tiles;
        _count = count;
    }

    /// <summary>고침이 없는(모두 자동) 묶음.</summary>
    public static OutlineEdits Empty(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        int tx = (width + TileMask) >> TileShift, ty = (height + TileMask) >> TileShift;
        return new OutlineEdits(width, height, tx, new sbyte[]?[tx * ty], 0);
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>자동이 아닌 픽셀이 하나도 없음.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>(x, y)의 값: 1 = 외곽선으로, −1 = 외곽선 아님, 0 = 자동(그림 밖도 0).</summary>
    public sbyte At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        var tile = _tiles[(y >> TileShift) * _tilesX + (x >> TileShift)];
        return tile is null ? (sbyte)0 : tile[((y & TileMask) << TileShift) | (x & TileMask)];
    }

    /// <summary>
    /// 중심마다 반지름 <paramref name="radius"/>의 꽉 찬 원(픽셀 중심이 중심 픽셀 중심에서 반지름 + ½ 안 — 0이면 한 픽셀, 그림 밖은 잘림)을
    /// <paramref name="value"/>(1 = 외곽선으로, −1 = 외곽선 아님, 0 = 자동으로 되돌림)로 칠한 새 묶음. 바뀐 픽셀이 없으면 이 묶음 그대로.
    /// </summary>
    public OutlineEdits Paint(IReadOnlyList<(int X, int Y)> centers, int radius, sbyte value)
    {
        ArgumentNullException.ThrowIfNull(centers);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, (sbyte)-1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, (sbyte)1);
        radius = Math.Max(0, radius);
        sbyte[]?[]? tiles = null;
        bool[]? owned = null; // 이번 칠하기에서 새로 만든 조각(다시 베끼지 않음)
        int count = _count;
        long reach = (long)radius * radius + radius;
        foreach (var (cx, cy) in centers)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = cy + dy;
                if ((uint)y >= (uint)Height) continue;
                int span = (int)Math.Sqrt(reach - (long)dy * dy);
                while ((long)(span + 1) * (span + 1) + (long)dy * dy <= reach) span++;
                while (span > 0 && (long)span * span + (long)dy * dy > reach) span--;
                int x0 = Math.Max(0, cx - span), x1 = Math.Min(Width - 1, cx + span);
                for (int x = x0; x <= x1; x++)
                {
                    int t = (y >> TileShift) * _tilesX + (x >> TileShift), k = ((y & TileMask) << TileShift) | (x & TileMask);
                    var tile = (tiles ?? _tiles)[t];
                    sbyte old = tile is null ? (sbyte)0 : tile[k];
                    if (old == value) continue;
                    tiles ??= (sbyte[]?[])_tiles.Clone();
                    owned ??= new bool[tiles.Length];
                    if (!owned[t])
                    {
                        tile = tile is null ? new sbyte[TileSize * TileSize] : (sbyte[])tile.Clone();
                        tiles[t] = tile;
                        owned[t] = true;
                    }
                    tile![k] = value;
                    count += (old == 0 ? 1 : 0) - (value == 0 ? 1 : 0);
                }
            }
        return tiles is null ? this : new OutlineEdits(Width, Height, _tilesX, tiles, count);
    }

    /// <summary>자동 외곽선 <paramref name="mask"/>에 고침을 덧씌운다 — 외곽선으로는 프레임 안 불투명 픽셀만(투명은 외곽선이 될 수 없음).</summary>
    internal void ApplyTo(bool[] mask, Color32[] pixels, int[] labels)
    {
        for (int t = 0; t < _tiles.Length; t++)
        {
            if (_tiles[t] is not { } tile) continue;
            int ox = (t % _tilesX) << TileShift, oy = (t / _tilesX) << TileShift;
            int xe = Math.Min(TileSize, Width - ox), ye = Math.Min(TileSize, Height - oy);
            for (int y = 0; y < ye; y++)
                for (int x = 0; x < xe; x++)
                {
                    sbyte v = tile[(y << TileShift) | x];
                    if (v == 0) continue;
                    int i = (oy + y) * Width + ox + x;
                    mask[i] = v > 0 && pixels[i].A != 0 && labels[i] >= 0;
                }
        }
    }
}

/// <summary>
/// 원본 외곽선 찾기(<see cref="PixelateSource.AutoOutline"/>)와 결과 칸으로 옮기기(<see cref="Cells"/>) — 줄인 그림 하나만 보고 외곽선을 가르면
/// 넓은 검은 면(검은 반바지)이 작은 투명 틈(다리 사이 배경)에 닿기만 해도 두꺼운 외곽선의 안쪽 겹으로 보여 살색으로 벗겨지므로,
/// '외곽선 1px로 얇게'(<see cref="PixelCleanup.ThinOutline"/>)와 그 점수는 원본에서 한 번 가른 외곽선을 따른다(<see cref="CleanupSettings.SourceOutline"/>).
/// </summary>
public static class SourceOutline
{
    /// <summary>
    /// 외곽선 띠로 볼 최대 두께 = 잰 원본 외곽선 두께(<see cref="GridDetector.OutlineThickness"/>)의 이 배 — 거리를 상하좌우 걸음으로 재므로
    /// 45° 외곽선은 두께의 √2배로 재지는 것(1.41)과 두께가 곳마다 조금 다른 것을 받아 준다.
    /// </summary>
    public const double BandFactor = 1.6;
    /// <summary>외곽선 띠 최대 두께의 하한(원본 px) — 외곽선을 못 재거나 1px 외곽선이어도 2px 띠까지는 외곽선.</summary>
    public const double MinBand = 2;
    /// <summary>
    /// 결과 칸 하나를 원본 외곽선으로 볼 비율: 칸 색을 고르는 안쪽 픽셀(<see cref="Pixelator.SampleArea"/>) 중 어둡거나 외곽선인 픽셀의 절반 이상이 외곽선.
    /// 칸이 어두워진 것은 그 어두운 픽셀들 때문이므로, 그중 다수가 외곽선 띠면 외곽선 칸이고 다수가 넓은 검은 면이면 면 칸이다 — 외곽선이 칸을 비스듬히
    /// 지나 일부만 덮는 칸도 어두운 픽셀은 대부분 외곽선이라 들어가고, 외곽선 띠가 검은 면 모서리를 조금 덮는 칸은 빠진다.
    /// </summary>
    public const double CellCoverage = 0.5;
    /// <summary>원본 외곽선 밝기 재기(<see cref="MeasureLightness"/>): 둘레 픽셀 밝기에서 볼 분위(아래에서 이 비율).</summary>
    public const double RingQuantile = 0.25;
    /// <summary>원본 외곽선 밝기 재기: 분위 값에 더할 여유(OKLab L) — 외곽선 띠 자체의 밝기 폭(가운데 → 안쪽 번진 가장자리).</summary>
    public const double RingMargin = 0.05;
    /// <summary>원본 외곽선 밝기 상한(OKLab L) — 둘레가 거의 밝은 그림(외곽선 없음)에서 밝은 면까지 외곽선으로 보지 않게.</summary>
    public const double MaxLightness = 0.45;

    /// <summary>
    /// 원본 외곽선으로 볼 밝기(OKLab L 상한, 설정의 외곽선 밝기와 상관없이 원본마다 한 번): 프레임 둘레 픽셀(프레임 안 불투명 픽셀 중 상하좌우에 투명·다른 프레임이
    /// 있는 것 — 그림 가장자리에 잘린 자리는 빼서 탁자처럼 캔버스에 닿은 면은 세지 않음)의 밝기 아래 <see cref="RingQuantile"/> 분위 + <see cref="RingMargin"/>,
    /// <see cref="PaletteSettings.DefaultOutlineDarkness"/>~<see cref="MaxLightness"/>로 자름. 둘레는 대개 외곽선이지만 AI 그림은 짙은 옷·머리·탁자도 배경에 닿아
    /// 중앙값이 그쪽으로 끌리므로(신고된 그림: 둘레 중앙값 0.31, 외곽선 띠 0.22~0.29, 반바지 0.38) 아래 사분위로 외곽선 가운데 밝기를 잡고 띠의 밝기 폭만큼 더한다.
    /// 외곽선이 아주 검으면(L 0.1 남짓) 하한 0.24 — 예전 외곽선 밝기 기본값과 같다. 둘레 픽셀이 없으면 하한.
    /// </summary>
    public static double MeasureLightness(PixelImage cleaned, Lab[] lab, FrameSplit split)
    {
        int w = cleaned.Width, h = cleaned.Height;
        var px = cleaned.Pixels;
        var labels = split.Labels;
        // 밝기를 0.005 칸으로 세어 분위를 찾는다(정렬 없이, 큰 그림도 둘레 픽셀 수만큼만).
        const int Bins = 200;
        Span<int> hist = stackalloc int[Bins + 1];
        hist.Clear();
        int total = 0;
        bool Outside(int i, int label) => px[i].A == 0 || labels[i] != label;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, label = labels[i];
                if (label < 0 || px[i].A == 0) continue;
                if (!((x > 0 && Outside(i - 1, label)) || (x + 1 < w && Outside(i + 1, label)) || (y > 0 && Outside(i - w, label)) || (y + 1 < h && Outside(i + w, label))))
                    continue;
                hist[Math.Clamp((int)(lab[i].L * Bins), 0, Bins)]++;
                total++;
            }
        if (total == 0) return PaletteSettings.DefaultOutlineDarkness;
        int want = (int)(total * RingQuantile), seen = 0, bin = 0;
        for (; bin < Bins; bin++)
            if ((seen += hist[bin]) > want) break;
        double quantile = (bin + 0.5) / Bins;
        return Math.Clamp(quantile + RingMargin, PaletteSettings.DefaultOutlineDarkness, MaxLightness);
    }

    private const byte Out = 0, Bright = 1, Dark = 2, Speck = 3;

    /// <summary>
    /// 자동 원본 외곽선(길이 = 폭 × 높이, 행 우선). 프레임마다(<see cref="FrameSplit.Labels"/> — 프레임 밖·다른 프레임·투명은 바깥) 어두운 픽셀
    /// (불투명, OKLab L ≤ <paramref name="darkness"/>)에서 상하좌우로 어두운 픽셀만 따라 잰 두 거리 — 바깥과 맞닿은 어두운 픽셀부터 dT,
    /// 밝은 픽셀과 맞닿은 어두운 픽셀부터 dB(둘 다 맞닿은 픽셀이 1) — 로 가른다.
    /// <list type="bullet">
    /// <item>외곽선 띠: dT·dB가 모두 있고 바깥 → 이 픽셀 → 밝은 쪽 두께 dT + dB − 1이 최대 두께(외곽선 두께 × <see cref="BandFactor"/>, 최소 <see cref="MinBand"/>) 이하.
    /// 넓은 검은 면은 바깥에 닿아도 안쪽 밝은 색이 멀어 들어가지 않는다.</item>
    /// <item>밝은 쪽이 없는 어두운 부분(최대 두께 안에 밝은 픽셀이 없음 — 머리카락 끝·가는 선): 그런 픽셀끼리 이은 덩어리가 바깥과 모두 최대 두께 안이고
    /// 가로지른 폭(2 × 가장 큰 dT − 1)도 최대 두께 이하일 때만. 넓은 검은 면의 가장자리는 넓은 안쪽과 한 덩어리라 빠진다.</item>
    /// </list>
    /// 넓이가 외곽선 두께² 미만인 밝은 점(검은 면의 작은 반사광·잡티)은 밝은 쪽으로 보지 않는다 — 넓은 검은 면 가운데의 점 하나가 둘레를 외곽선 띠로 만들지 않게.
    /// 바깥에 닿지 않은 어두운 덩어리(눈 등)는 외곽선이 아니다.
    /// </summary>
    public static bool[] Detect(PixelImage cleaned, Lab[] lab, FrameSplit split, double darkness)
    {
        var mask = new bool[cleaned.Width * cleaned.Height];
        for (int f = 0; f < split.Frames.Count; f++)
            DetectFrame(cleaned, lab, split.Labels, f, Rectangle.Intersect(split.Frames[f].Bounds, cleaned.Bounds), darkness, mask);
        return mask;
    }

    private static void DetectFrame(PixelImage cleaned, Lab[] lab, int[] labels, int frame, Rectangle b, double darkness, bool[] mask)
    {
        int w = cleaned.Width, bw = b.Width, bh = b.Height, n = bw * bh;
        if (n <= 0) return;
        var px = cleaned.Pixels;
        // 지역 번호 k = (y − 위) × 범위 폭 + (x − 왼쪽).
        var kind = new byte[n];
        bool anyDark = false;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                int i = (y + b.Top) * w + x + b.Left;
                if (labels[i] != frame || px[i].A == 0) continue;
                bool dark = lab[i].L <= darkness;
                kind[y * bw + x] = dark ? Dark : Bright;
                anyDark |= dark;
            }
        if (!anyDark) return;
        var (thickness, samples) = GridDetector.OutlineThickness(cleaned, labels, frame, b, darkness);
        if (samples == 0) thickness = 0;
        double limit = Math.Max(MinBand, BandFactor * thickness);
        int cap = (int)Math.Min(limit, ushort.MaxValue - 1);
        var queue = new int[n];
        MarkSpecks(kind, bw, bh, Math.Max(2, (int)Math.Ceiling(thickness * thickness)), queue);
        var dT = new ushort[n];
        var dB = new ushort[n];
        Spread(kind, bw, bh, dT, queue, cap, Out);
        Spread(kind, bw, bh, dB, queue, cap, Bright);

        void Mark(int k) => mask[(k / bw + b.Top) * w + k % bw + b.Left] = true;
        for (int k = 0; k < n; k++)
            if (kind[k] == Dark && dT[k] > 0 && dB[k] > 0 && dT[k] + dB[k] - 1 <= limit) Mark(k);

        // 밝은 쪽이 없는 어두운 부분: 덩어리마다 가로지른 폭으로(본 픽셀은 dB를 ushort.MaxValue로 표시).
        const ushort Seen = ushort.MaxValue;
        for (int start = 0; start < n; start++)
        {
            if (kind[start] < Dark || dB[start] != 0) continue;
            int count = 0, maxT = 0;
            bool far = false;
            dB[start] = Seen;
            queue[count++] = start;
            for (int q = 0; q < count; q++)
            {
                int k = queue[q];
                if (dT[k] == 0) far = true;
                else maxT = Math.Max(maxT, dT[k]);
                int x = k % bw, y = k / bw;
                void Visit(int j)
                {
                    if (kind[j] < Dark || dB[j] != 0) return;
                    dB[j] = Seen;
                    queue[count++] = j;
                }
                if (x > 0) Visit(k - 1);
                if (x + 1 < bw) Visit(k + 1);
                if (y > 0) Visit(k - bw);
                if (y + 1 < bh) Visit(k + bw);
            }
            if (far || 2 * maxT - 1 > limit) continue;
            for (int q = 0; q < count; q++)
                if (kind[queue[q]] == Dark) Mark(queue[q]);
        }
    }

    /// <summary>넓이가 <paramref name="minArea"/> 미만인 밝은 덩어리(상하좌우로 이음)를 <see cref="Speck"/>로 — 거리 재기에서 어두운 픽셀처럼 지나간다.</summary>
    private static void MarkSpecks(byte[] kind, int bw, int bh, int minArea, int[] stack)
    {
        // 본 밝은 픽셀은 Speck + 1로 두고(다시 퍼뜨리지 않게) 작은 덩어리만 바로 Speck으로, 끝에 나머지를 Bright로 되돌린다.
        const byte Visited = Speck + 1;
        for (int start = 0; start < kind.Length; start++)
        {
            if (kind[start] != Bright) continue;
            int count = 0;
            kind[start] = Visited;
            stack[count++] = start;
            for (int q = 0; q < count; q++)
            {
                int k = stack[q], x = k % bw, y = k / bw;
                void Visit(int j)
                {
                    if (kind[j] != Bright) return;
                    kind[j] = Visited;
                    stack[count++] = j;
                }
                if (x > 0) Visit(k - 1);
                if (x + 1 < bw) Visit(k + 1);
                if (y > 0) Visit(k - bw);
                if (y + 1 < bh) Visit(k + bw);
            }
            if (count < minArea)
                for (int q = 0; q < count; q++) kind[stack[q]] = Speck;
        }
        for (int k = 0; k < kind.Length; k++)
            if (kind[k] == Visited) kind[k] = Bright;
    }

    /// <summary>
    /// 어두운 픽셀(작은 밝은 점 포함)만 따라 상하좌우로 잰 거리: <paramref name="from"/>(<see cref="Out"/> = 바깥·범위 밖, <see cref="Bright"/> = 밝은 픽셀)과
    /// 맞닿은 픽셀이 1, <paramref name="cap"/>보다 먼 픽셀은 0(닿지 않음).
    /// </summary>
    private static void Spread(byte[] kind, int bw, int bh, ushort[] dist, int[] queue, int cap, byte from)
    {
        int tail = 0;
        for (int k = 0; k < kind.Length; k++)
        {
            if (kind[k] < Dark) continue;
            int x = k % bw, y = k / bw;
            bool touches = from == Out
                ? x == 0 || y == 0 || x + 1 == bw || y + 1 == bh || kind[k - 1] == Out || kind[k + 1] == Out || kind[k - bw] == Out || kind[k + bw] == Out
                : (x > 0 && kind[k - 1] == Bright) || (x + 1 < bw && kind[k + 1] == Bright) || (y > 0 && kind[k - bw] == Bright) || (y + 1 < bh && kind[k + bw] == Bright);
            if (!touches) continue;
            dist[k] = 1;
            queue[tail++] = k;
        }
        for (int head = 0; head < tail; head++)
        {
            int k = queue[head];
            int d = dist[k];
            if (d >= cap) continue;
            int x = k % bw, y = k / bw;
            void Visit(int j)
            {
                if (kind[j] < Dark || dist[j] != 0) return;
                dist[j] = (ushort)(d + 1);
                queue[tail++] = j;
            }
            if (x > 0) Visit(k - 1);
            if (x + 1 < bw) Visit(k + 1);
            if (y > 0) Visit(k - bw);
            if (y + 1 < bh) Visit(k + bw);
        }
    }

    /// <summary>자동 외곽선 <paramref name="auto"/>에 고침 <paramref name="edits"/>를 덧씌운 새 배열.</summary>
    internal static bool[] Apply(bool[] auto, OutlineEdits edits, PixelImage cleaned, int[] labels)
    {
        var mask = (bool[])auto.Clone();
        edits.ApplyTo(mask, cleaned.Pixels, labels);
        return mask;
    }

    /// <summary>
    /// 결과 칸마다(<paramref name="layout"/>의 열 × 행, 행 우선) 원본 외곽선인지: 칸 색을 고르는 안쪽 픽셀(<see cref="Pixelator.SampleArea"/> — 칸 색 고르기와 같은 자리)
    /// 중 이 프레임의 불투명 픽셀에서 어둡거나(<see cref="PixelateSource.OutlineLightness"/> — 자동 외곽선과 같은 기준) 외곽선인 픽셀의 <see cref="CellCoverage"/> 이상이
    /// 외곽선(<paramref name="outline"/>).
    /// </summary>
    public static bool[] Cells(bool[] outline, PixelateSource src, int frame, CellLayout layout, DownsampleSettings s)
    {
        int cols = layout.Columns, rows = layout.Rows;
        var cells = new bool[Math.Max(0, cols * rows)];
        var cleaned = src.Cleaned;
        int w = cleaned.Width, h = cleaned.Height;
        var px = cleaned.Pixels;
        var labels = src.Split.Labels;
        var lab = src.Lab;
        double border = PixelateRanges.BorderFraction.Clamp(s.BorderFraction);
        double darkness = src.OutlineLightness;
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                var (xa, ya, xb, yb) = Pixelator.SampleArea(layout, cx, cy, border, w, h);
                int counted = 0, inside = 0;
                for (int y = ya; y < yb; y++)
                    for (int x = xa; x < xb; x++)
                    {
                        int i = y * w + x;
                        if (labels[i] != frame || px[i].A == 0) continue;
                        if (outline[i]) { inside++; counted++; }
                        else if (lab[i].L <= darkness) counted++;
                    }
                cells[cy * cols + cx] = inside > 0 && inside >= CellCoverage * counted;
            }
        return cells;
    }

    /// <summary>칸 표 <paramref name="cells"/>(열 <paramref name="columns"/>개)를 캔버스 크기로 옮긴다(칸 (c, r) → 캔버스 (c + 가로 자리, r + 세로 자리) — <see cref="PixelateFrameMap"/>).</summary>
    internal static bool[] ToCanvas(bool[] cells, int columns, int rows, PixelImage canvas, int offsetX, int offsetY)
    {
        int w = canvas.Width, h = canvas.Height;
        var mask = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            int r = y - offsetY;
            if ((uint)r >= (uint)rows) continue;
            for (int x = 0; x < w; x++)
            {
                int c = x - offsetX;
                if ((uint)c < (uint)columns) mask[y * w + x] = cells[r * columns + c];
            }
        }
        return mask;
    }
}
