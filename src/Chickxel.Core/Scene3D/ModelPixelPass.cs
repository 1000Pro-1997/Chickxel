using System.Numerics;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// 3D 모델을 그린 결과 픽셀 다듬기(<see cref="ModelRenderSettings"/>의 픽셀 그림 다듬기): 반투명 없애기, 여러 그림이 함께 쓰는 팔레트로 색 줄이기, 외곽선.
/// <para>
/// 외곽선 규칙(모두 결과 픽셀 단위, 이웃은 상하좌우 넷 — 모서리 대각선 칸을 칠하지 않아 손으로 찍은 픽셀 선처럼 깔끔하다):
/// <list type="bullet">
/// <item>모델 픽셀 = 덮음(<see cref="ModelRenderLayers.Coverage"/>) 128 이상. 반투명 없애기·바탕 색과 상관없다.</item>
/// <item>겉 외곽선: 바깥이면 모델에서 상하좌우로 <see cref="ModelRenderSettings.OutlineWidth"/>걸음 안(맨해튼 거리)의 빈 픽셀을, 안쪽이면 빈 픽셀(그림 밖 포함)에서
/// 그만큼 안의 모델 픽셀을 칠한다.</item>
/// <item>안쪽 겹침선: 이웃한 두 모델 픽셀 중 먼 쪽 면을 그 법선 기울기대로 이어도 가까운 쪽 깊이에 못 미치면(맞춤 반지름의 <see cref="InnerThreshold"/> +
/// 픽셀 <see cref="InnerThresholdPixels"/>칸 넘게 앞) 먼 쪽 픽셀을 칠한다 — 앞 물체의 윤곽은 그대로 남는다. 둥근 몸은 바깥 픽셀일수록 면이 가팔라
/// 먼 쪽 기울기가 늘 깊이 차이를 설명하므로 선이 생기지 않고, 몸 앞의 팔은 뒤 몸통 면이 평평해 선이 생긴다.</item>
/// <item>각진 곳 선: 겹침이 아닌 이웃 모델 픽셀의 법선 사이 각도가 <see cref="ModelRenderSettings.OutlineCrease"/>보다 크면 카메라를 덜 보는(법선 Z가 작은) 쪽을 칠한다.</item>
/// <item>안쪽 겹침선·각진 곳 선은 늘 1px(두께는 겉 외곽선만).</item>
/// <item>색: 고정이면 <see cref="ModelRenderSettings.OutlineColor"/>(불투명), 어둡게면 선 자리 모델 픽셀(바깥 선이면 붙은 모델 픽셀) 색의 OKLab 밝기에
/// 1 − <see cref="ModelRenderSettings.OutlineDarken"/>를 곱한 불투명 색.</item>
/// <item>선 자리는 모두 칠하기 전 값(덮음·깊이·법선)으로 먼저 정하고, 색은 칠하기 전 그림에서 읽는다 — 그린 선이 다른 선을 만들지 않는다.</item>
/// </list>
/// </para>
/// </summary>
public static class ModelPixelPass
{
    /// <summary>모델 픽셀로 볼 덮음.</summary>
    private const byte ModelCoverage = 128;
    /// <summary>안쪽 겹침선 문턱(맞춤 반지름 비율).</summary>
    private const float InnerThreshold = 0.05f;
    /// <summary>안쪽 겹침선 문턱에 더할 픽셀 크기 배수(작은 그림에서 오목한 곳·굽은 면의 픽셀 계단을 선으로 보지 않게).</summary>
    private const float InnerThresholdPixels = 1.5f;
    /// <summary>면 기울기를 셀 때 법선 Z의 하한(화면과 거의 나란한 면도 기울기를 유한하게).</summary>
    private const float MinFacing = 0.05f;
    private const int MaxOutlineWidth = 3;

    /// <summary>반투명 없애기: 알파 128 이상은 255, 나머지는 완전 투명(색도 0).</summary>
    public static void HardAlpha(PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            px[i] = c.A >= 128 ? c.WithAlpha(255) : Color32.Transparent;
        }
    }

    /// <summary>
    /// 그림들의 보이는 픽셀(알파 0 아님) 모두로 팔레트 하나(<see cref="PaletteBuilder.Histogram"/> 픽셀 수 + <see cref="PaletteBuilder.KMeans"/>, 2~64색).
    /// <paramref name="colors"/>가 0 이하이거나 보이는 픽셀이 없으면 빈 목록.
    /// </summary>
    public static IReadOnlyList<Color32> Palette(IEnumerable<PixelImage> images, int colors)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (colors <= 0) return [];
        var hist = PaletteBuilder.Histogram(images);
        if (hist.Count == 0) return [];
        return PaletteBuilder.KMeans(hist, Math.Clamp(colors, 2, PaletteBuilder.MaxColors)).Palette;
    }

    /// <summary>보이는 픽셀마다 OKLab에서 가장 가까운 팔레트 색으로(알파 그대로). 팔레트가 비면 그대로.</summary>
    public static void Remap(PixelImage image, IReadOnlyList<Color32> palette)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0) return;
        var labs = new Lab[palette.Count];
        for (int i = 0; i < labs.Length; i++) labs[i] = OkLab.FromColor(palette[i]);
        var cache = new Dictionary<uint, Color32>();
        var px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.A == 0) continue;
            uint key = c.Argb & 0x00FFFFFFu;
            if (!cache.TryGetValue(key, out var mapped))
            {
                var lab = OkLab.FromColor(c);
                int best = 0;
                float bestD = float.MaxValue;
                for (int k = 0; k < labs.Length; k++)
                {
                    float d = Lab.DistanceSquared(lab, labs[k]);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
                cache[key] = mapped = palette[best];
            }
            px[i] = mapped.WithAlpha(c.A);
        }
    }

    /// <summary>설정대로 외곽선을 <see cref="ModelRenderLayers.Image"/>에 그린다(선 자리는 <paramref name="layers"/>의 덮음·깊이·법선, 색은 지금 그림에서).</summary>
    public static void Outline(ModelRenderLayers layers, ModelRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(layers);
        PaintOutline(layers.Image, FindOutline(layers, settings), settings);
    }

    /// <summary>
    /// 외곽선 자리: [칠할 픽셀, 색을 가져올 픽셀] 쌍을 이어 붙인 배열. 덮음·깊이·법선만 보므로 그 뒤 색을 바꿔도(반투명 없애기·색 줄이기) 그대로 쓸 수 있다 —
    /// 프레임이 많을 때 층 전체 대신 이것만 들고 있는다.
    /// </summary>
    internal static int[] FindOutline(ModelRenderLayers layers, ModelRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(settings);
        int w = layers.Width, h = layers.Height, n = w * h;
        var coverage = layers.Coverage;
        var model = new bool[n];
        for (int i = 0; i < n; i++) model[i] = coverage[i] >= ModelCoverage;
        var source = new int[n];
        Array.Fill(source, -1);
        if (settings.OutlineOuter)
        {
            int width = Math.Clamp(settings.OutlineWidth, 1, MaxOutlineWidth);
            if (settings.OutlineOutside) GrowOutside(model, source, w, h, width);
            else GrowInside(model, source, w, h, width);
        }
        float crease = float.IsFinite(settings.OutlineCrease) ? Math.Min(settings.OutlineCrease, 180) : 0;
        if (settings.OutlineInner || crease > 0) FindEdges(layers, model, source, settings.OutlineInner, crease);
        int count = 0;
        for (int i = 0; i < n; i++)
            if (source[i] >= 0) count++;
        var marks = new int[count * 2];
        int at = 0;
        for (int i = 0; i < n; i++)
        {
            if (source[i] < 0) continue;
            marks[at++] = i;
            marks[at++] = source[i];
        }
        return marks;
    }

    /// <summary><see cref="FindOutline"/>의 자리를 칠한다(색은 모두 칠하기 전 그림에서 읽음).</summary>
    internal static void PaintOutline(PixelImage image, int[] marks, ModelRenderSettings settings)
    {
        if (marks.Length == 0) return;
        var px = image.Pixels;
        var colors = new Color32[marks.Length / 2];
        if (settings.OutlineColorMode == ModelOutlineColor.Darken)
        {
            float amount = float.IsFinite(settings.OutlineDarken) ? Math.Clamp(settings.OutlineDarken, 0, 1) : 0.5f;
            var cache = new Dictionary<uint, Color32>();
            for (int k = 0; k < colors.Length; k++)
            {
                var c = px[marks[2 * k + 1]].WithAlpha(255);
                if (!cache.TryGetValue(c.Argb, out var dark)) cache[c.Argb] = dark = OkLab.ShiftLightness(c, -amount).WithAlpha(255);
                colors[k] = dark;
            }
        }
        else
        {
            Array.Fill(colors, settings.OutlineColor.WithAlpha(255));
        }
        for (int k = 0; k < colors.Length; k++) px[marks[2 * k]] = colors[k];
    }

    /// <summary>바깥 겉 외곽선: 한 걸음마다 상하좌우 이웃(왼·오·위·아래 순)이 이미 닿은 빈 픽셀을 더하고, 색은 그 이웃이 가져온 모델 픽셀에서.</summary>
    private static void GrowOutside(bool[] model, int[] source, int w, int h, int width)
    {
        int n = w * h;
        var owner = new int[n];
        for (int i = 0; i < n; i++) owner[i] = model[i] ? i : -1;
        var next = new int[n];
        for (int step = 0; step < width; step++)
        {
            Array.Copy(owner, next, n);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    if (owner[i] >= 0) continue;
                    int o = -1;
                    if (x > 0 && owner[i - 1] >= 0) o = owner[i - 1];
                    else if (x < w - 1 && owner[i + 1] >= 0) o = owner[i + 1];
                    else if (y > 0 && owner[i - w] >= 0) o = owner[i - w];
                    else if (y < h - 1 && owner[i + w] >= 0) o = owner[i + w];
                    if (o < 0) continue;
                    next[i] = o;
                    source[i] = o;
                }
            }
            (owner, next) = (next, owner);
        }
    }

    /// <summary>안쪽 겉 외곽선: 첫 걸음은 상하좌우에 빈 픽셀(그림 밖 포함)이 있는 모델 픽셀, 다음 걸음은 앞 걸음 픽셀과 맞닿은 모델 픽셀.</summary>
    private static void GrowInside(bool[] model, int[] source, int w, int h, int width)
    {
        var ring = new byte[w * h];
        for (int step = 1; step <= width; step++)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    if (!model[i] || ring[i] != 0) continue;
                    bool hit = step == 1
                        ? x == 0 || y == 0 || x == w - 1 || y == h - 1 || !model[i - 1] || !model[i + 1] || !model[i - w] || !model[i + w]
                        : (x > 0 && ring[i - 1] == step - 1) || (x < w - 1 && ring[i + 1] == step - 1)
                            || (y > 0 && ring[i - w] == step - 1) || (y < h - 1 && ring[i + w] == step - 1);
                    if (!hit) continue;
                    ring[i] = (byte)step;
                    source[i] = i;
                }
            }
        }
    }

    /// <summary>오른쪽·아래 이웃 쌍마다 안쪽 겹침선·각진 곳 선.</summary>
    private static void FindEdges(ModelRenderLayers layers, bool[] model, int[] source, bool inner, float crease)
    {
        int w = layers.Width, h = layers.Height;
        var depth = layers.Depth;
        var normal = layers.Normal;
        float cosLimit = crease > 0 ? MathF.Cos(crease * (MathF.PI / 180)) : float.NegativeInfinity;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (!model[i]) continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    int sx = dir == 0 ? 1 : 0, sy = 1 - sx;
                    if (x + sx >= w || y + sy >= h) continue;
                    int j = i + sx + sy * w;
                    if (!model[j]) continue;
                    // 먼 쪽 → 가까운 쪽 걸음(화면 x 오른쪽, y 아래).
                    bool iNear = depth[i] <= depth[j];
                    int near = iNear ? i : j, far = iNear ? j : i;
                    int stepX = iNear ? -sx : sx, stepY = iNear ? -sy : sy;
                    if (DepthEdge(layers, far, near, stepX, stepY))
                    {
                        if (inner) source[far] = far;
                        continue;
                    }
                    if (crease > 0 && Vector3.Dot(normal[i], normal[j]) < cosLimit)
                    {
                        int p = normal[i].Z < normal[j].Z ? i : j;
                        source[p] = p;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 먼 픽셀 <paramref name="far"/>의 면(법선이 정하는 평면)을 화면 한 칸(<paramref name="stepX"/>, <paramref name="stepY"/>)만큼 이었을 때의 깊이보다
    /// 가까운 픽셀 <paramref name="near"/>가 문턱 넘게 앞이면 겹침.
    /// </summary>
    private static bool DepthEdge(ModelRenderLayers layers, int far, int near, int stepX, int stepY)
    {
        var nf = layers.Normal[far];
        float ps = layers.PixelSize(far);
        // 평면 n·Δ = 0에서 깊이(카메라 반대쪽) 변화 = (nx Δx + ny Δy) / nz, 화면 y 아래 = 카메라 Y 반대.
        float predicted = layers.Depth[far] + (nf.X * stepX - nf.Y * stepY) * ps / MathF.Max(nf.Z, MinFacing);
        return layers.Depth[near] < predicted - (InnerThreshold + InnerThresholdPixels * ps);
    }
}
