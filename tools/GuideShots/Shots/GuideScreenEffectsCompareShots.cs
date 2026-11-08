using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Chickxel.App;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using GradientStop = Chickxel.Core.Edit.GradientStop;

namespace GuideShots;

/// <summary>
/// 레이어 효과 쪽들의 비교 그림: 미리보기 병아리(1번)에 공식 스크립트 효과를 앱과 같은 길(<see cref="LayerEffects.Render(PixelImage, IReadOnlyList{LayerEffect})"/>,
/// 움직이는 효과는 <see cref="AssetPreview.Render"/>)로 입혀 설정만 바꾼 결과를 한 장에 나란히 놓는다(왼쪽 = 끔·원본, 오른쪽 = 켬).
/// 빨간 번호는 붙이지 않는다 — 가이드 그림 설명에 "왼쪽: … · 오른쪽: …"으로 적는다.
/// </summary>
internal static class GuideScreenEffectsCompareShots
{
    private static readonly IBrush Gap = new SolidColorBrush(Color.FromRgb(24, 24, 27));
    private const int GapWidth = 16, Zoom = 10, Margin = 4;

    /// <summary>쌓이는 순서: 외곽선 → 그림자(그림자가 외곽선까지 따라 깔림) / 그림자 → 외곽선(외곽선이 그림자 둘레까지 두름).</summary>
    [Shot("screen-layer-effects-compare-order")]
    public static void Order(ShotKit k)
    {
        var outline = Effect("outline");
        var shadow = Effect("shadow", ("color", "#5a6eb4"), ("offsetX", 2), ("offsetY", 2));
        Compose(k, k.Id, [[Static([outline, shadow]), Static([shadow, outline])]]);
    }

    /// <summary>외곽선: 원본 / 기본(4방향, 검정 1px) / 8방향 (모서리 포함) / 인접 픽셀 기준 명암.</summary>
    [Shot("screen-effect-list-compare-outline")]
    public static void Outline(ShotKit k)
    {
        Compose(k, k.Id,
        [[
            Static([]),
            Static([Effect("outline")]),
            Static([Effect("outline", ("diagonal", true))]),
            Static([Effect("outline", ("colorMode", "Shade"))]),
        ]]);
    }

    /// <summary>그림자: 원본 / 기본 색(반투명) X·Y 2 / 같은 그림자에 디더 50% (바둑판).</summary>
    [Shot("screen-effect-list-compare-shadow")]
    public static void Shadow(ShotKit k)
    {
        Compose(k, k.Id,
        [[
            Static([]),
            Static([Effect("shadow", ("offsetX", 2), ("offsetY", 2))]),
            Static([Effect("shadow", ("offsetX", 2), ("offsetY", 2), ("dither", "Half50"))]),
        ]]);
    }

    /// <summary>
    /// 클리핑: 외곽선(8방향, 2px) 아래에 무지개 직선 그라데이션 색 덮기. 클리핑 끔 = 그림과 외곽선 전체가 그라데이션 실루엣 /
    /// 클리핑 켬 = 외곽선만 그라데이션.
    /// </summary>
    [Shot("screen-effect-colors-compare-clip")]
    public static void Clip(ShotKit k)
    {
        var outline = Effect("outline", ("thickness", 2), ("diagonal", true));
        var gradient = new EffectColorGradient
        {
            Shape = EffectGradientShape.Linear,
            Stops =
            [
                new GradientStop(0, new Color32(255, 70, 70)), new GradientStop(0.5, new Color32(255, 220, 60)),
                new GradientStop(1, new Color32(70, 140, 255)),
            ],
        };
        var overlay = Effect("coloroverlay") with
        {
            ColorGradients = new Dictionary<string, EffectColorGradient> { ["color"] = gradient },
        };
        Compose(k, k.Id, [[Static([outline, overlay]), Static([outline, overlay with { ClipToPrevious = true }])]]);
    }

    /// <summary>빠르기 곡선: 같은 충격파 고리를 6프레임 범위에 — 위 줄 일정하게(Linear), 아래 줄 처음 빠르게(EaseOut). 왼쪽부터 범위의 1~6번째 프레임.</summary>
    [Shot("screen-effect-settings-compare-easing")]
    public static void Easing(ShotKit k)
    {
        const int steps = 6;
        var ring = Effect("shockwave", ("radius", 15), ("color", "#e03c3c"), ("fade", false));
        var chick = PreviewChicks.Load(PreviewChicks.Default);
        List<PixelImage> Row(EffectEasing easing)
        {
            LayerEffect effect = ring with { Easing = easing };
            return [.. Enumerable.Range(0, steps).Select(step => AssetPreview.Render([effect], chick, step, steps))];
        }
        Compose(k, k.Id, [Row(EffectEasing.Linear), Row(EffectEasing.EaseOut)], zoom: 4);
    }

    /// <summary>
    /// 미리보기 자리·피벗: 효과 만들기의 변신 예(<see cref="EffectMakerShots.MorphScript"/>, 1번 병아리 → 끝 그림 병아리 마법사, 6칸)의 첫·가운데·마지막 칸.
    /// 왼쪽 = 끝 그림 병아리 자리 0(제자리에서 바뀜) / 오른쪽 = 끝 그림 병아리 자리 (16, 0)(바뀌면서 오른쪽으로 옮겨 감).
    /// 앱 미리보기와 같은 <see cref="AssetPreview.Render"/>로 그린다. 오른쪽 칸은 옮긴 만큼 캔버스가 넓으므로, 모든 칸을 넓은 쪽 너비의 칸에 왼쪽(첫 병아리 자리)을
    /// 맞춰 놓아 두 쪽의 첫 병아리가 같은 자리에 오게 하고, 한 쪽의 칸은 어두운 1px 줄로 나눠 한 띠로 붙인다.
    /// </summary>
    [Shot("screen-effect-maker-compare-end-offset")]
    public static void EndOffset(ShotKit k)
    {
        const int steps = 6;
        string hash = ScriptLibrary.Add(EffectMakerShots.MorphScript);
        ScriptLibrary.SetTrust(hash, true);
        var program = ScriptLibrary.Runnable(hash) ?? throw new InvalidOperationException("morph script didn't parse");
        LayerEffect morph = program.CreateEffect();
        var chick = PreviewChicks.Load(program.PreviewChick);
        var end = AssetPreview.EndChick(program.PreviewEndChick);
        List<PixelImage> Frames(PreviewPlacement placement)
            => [.. new[] { 0, steps / 2, steps - 1 }.Select(step => AssetPreview.Render([morph], chick, step, steps, end, PreviewMotion.None, placement))];
        var moved = new PreviewPlacement(new System.Drawing.Point(16, 0), System.Drawing.Point.Empty, System.Drawing.Point.Empty);
        List<PixelImage>[] sides = [Frames(PreviewPlacement.None), Frames(moved)];
        int slot = sides.SelectMany(s => s).Max(f => f.Width), height = sides.SelectMany(s => s).Max(f => f.Height);
        PixelImage Strip(List<PixelImage> frames)
        {
            var strip = new PixelImage(frames.Count * (slot + 1) - 1, height);
            var line = new Color32(24, 24, 27);
            for (int i = 0; i < frames.Count; i++)
            {
                int x = i * (slot + 1);
                if (i > 0)
                    for (int y = 0; y < height; y++) strip[x - 1, y] = line;
                strip.Blit(frames[i], x, height - frames[i].Height);
            }
            return strip;
        }
        Compose(k, k.Id, [[Strip(sides[0]), Strip(sides[1])]], zoom: 3);
    }

    /// <summary>공식 효과 <paramref name="id"/>를 기본값에 <paramref name="values"/>만 바꿔 만든다(앱이 효과 창에서 넣을 때와 같은 <see cref="ScriptEffectProgram.CreateEffect"/>).</summary>
    private static ScriptEffect Effect(string id, params (string Key, object Value)[] values)
        => OfficialEffects.Program(id).CreateEffect(values.ToDictionary(v => v.Key, v => v.Value));

    /// <summary>병아리 둘레에 여백을 두고 효과를 입혀 원래(여백 포함) 크기로 자른다 — 모든 칸이 같은 크기.</summary>
    private static PixelImage Static(IReadOnlyList<LayerEffect> effects)
    {
        var chick = PreviewChicks.Load(PreviewChicks.Default);
        var padded = new PixelImage(chick.Width + Margin * 2, chick.Height + Margin * 2);
        padded.Blit(chick, Margin, Margin);
        return effects.Count == 0 ? padded : LayerEffects.RenderClipped(padded, effects);
    }

    /// <summary>줄마다 그림을 <paramref name="zoom"/>배(최근접)로 왼쪽부터 나란히 놓고 칸 사이를 어두운 띠로 나눠 저장한다(바탕은 밝은 회색 판).</summary>
    private static void Compose(ShotKit k, string name, IReadOnlyList<IReadOnlyList<PixelImage>> rows, int zoom = Zoom)
    {
        int cellW = rows.SelectMany(r => r).Max(i => i.Width) * zoom, cellH = rows.SelectMany(r => r).Max(i => i.Height) * zoom;
        int columns = rows.Max(r => r.Count);
        int width = columns * cellW + GapWidth * (columns + 1), height = rows.Count * cellH + GapWidth * (rows.Count + 1);
        var board = new SolidColorBrush(Color.FromRgb(200, 200, 206));
        var bitmaps = new List<Bitmap>();
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            ctx.FillRectangle(Gap, new Rect(0, 0, width, height));
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                for (int row = 0; row < rows.Count; row++)
                    for (int col = 0; col < rows[row].Count; col++)
                    {
                        var image = rows[row][col];
                        var cell = new Rect(GapWidth + col * (cellW + GapWidth), GapWidth + row * (cellH + GapWidth), cellW, cellH);
                        ctx.FillRectangle(board, cell);
                        var bitmap = ImageUtil.ToBitmap(image);
                        bitmaps.Add(bitmap);
                        var at = new Rect(cell.X + (cellW - image.Width * zoom) / 2, cell.Y + (cellH - image.Height * zoom) / 2,
                            image.Width * zoom, image.Height * zoom);
                        ctx.DrawImage(bitmap, new Rect(0, 0, image.Width, image.Height), at);
                    }
        }
        k.Write(target, name);
        foreach (var bitmap in bitmaps) bitmap.Dispose();
    }
}
