using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// 합성 순서표(<see cref="RenderPlan"/>)를 다른 방식으로 그려도 결과가 같은지: 효과 결과 캐시를 쓸 때, 효과 1~k 중간 결과에서 이어 그릴 때,
/// 셀 썸네일 순서표, 진행·중간 그림 알림, 취소.
/// </summary>
public class RenderPlanTests
{
    private static readonly Color32 Red = new(220, 40, 40), Blue = new(40, 60, 230), Green = new(40, 200, 80);

    /// <summary>
    /// 프레임 6개: 맨 아래 움직이는 일반 레이어(외곽선·그림자·클리핑된 잔상), 효과 켜진 그룹(부위 하나는 자기 잔상, 그룹은 외곽선·잔상),
    /// 맨 위 일반 레이어(흔들기 + 잔상).
    /// </summary>
    private static SpriteProject Scene()
    {
        var p = new SpriteProject(40, 30);
        for (int f = 1; f < 6; f++) p.AddFrame();
        for (int f = 0; f < 6; f++)
        {
            var cel = new PixelImage(40, 30);
            cel.FillRect(new Rectangle(4 + f * 3, 6, 8, 10), Red);
            cel.FillRect(new Rectangle(6 + f * 3, 8, 3, 3), Green);
            p.SetCel(0, f, cel);
        }
        p.SetLayerEffects(0,
        [
            new OutlineEffect { Color = Blue, Thickness = 2 },
            new ShadowEffect { OffsetX = 2, OffsetY = 1, ClipToPrevious = true },
            new AfterimageEffect { ClipToPrevious = true, Count = 2, Opacity = 70 },
        ]);

        var g = p.AddGroup("G");
        var body = p.AddPart(g, "Body", 10, 8, new Point(18, 12));
        var arm = p.AddPart(g, "Arm", 6, 4, new Point(14, 14));
        for (int f = 0; f < 6; f++)
        {
            var b = new PixelImage(10, 8);
            b.FillRect(new Rectangle(f % 3, 1, 6, 6), Blue);
            p.SetCel(p.IndexOf(body), f, b);
            var a = new PixelImage(6, 4);
            a.FillRect(new Rectangle(0, f % 2, 5, 2), Green);
            p.SetCel(p.IndexOf(arm), f, a);
        }
        p.SetLayerEffects(p.IndexOf(arm), [new AfterimageEffect { Count = 1, Opacity = 50, Tint = true, TintColor = Red }]);
        p.SetLayerEffects(p.IndexOf(g),
        [
            new OutlineEffect { Color = Red },
            new BobEffect { Height = 2 },
            new AfterimageEffect { Count = 3, Interval = 1, Opacity = 60 },
        ]);

        var top = p.AddLayer(name: "위");
        for (int f = 0; f < 6; f++)
        {
            var cel = new PixelImage(40, 30);
            cel.FillRect(new Rectangle(30 - f * 2, 20, 4, 4), Green);
            p.SetCel(p.IndexOf(top), f, cel);
        }
        p.SetLayerEffects(p.IndexOf(top), [new ShakeEffect { Amount = 2 }, new AfterimageEffect { Count = 2, Interval = 2, Opacity = 80 }]);
        return p;
    }

    [Fact]
    public void Cached_renders_match_uncached_renders_across_frames()
    {
        var p = Scene();
        var cache = new EffectRenderCache(256L << 20);
        var expected = Enumerable.Range(0, p.Frames.Count).Select(f => p.Flatten(f)).ToList();
        // 앞으로 한 번, 거꾸로 한 번 — 두 번째는 이웃 프레임이 담아 둔 잔상 원본을 다시 쓴다.
        foreach (int f in Enumerable.Range(0, p.Frames.Count).Concat(Enumerable.Range(0, p.Frames.Count).Reverse()))
            Assert.True(p.PlanFrame(f).Render(new RenderPlanOptions { Cache = cache }).ContentEquals(expected[f]), $"frame {f}");
        Assert.True(cache.Bytes > 0);
    }

    [Fact]
    public void Resuming_from_a_cached_effect_prefix_matches_a_full_render()
    {
        var p = Scene();
        int layer = p.IndexOf(p.Layers.First(l => l.Name == "위"));
        var target = p.Layers[layer];
        p.SetLayerEffects(layer,
        [
            new OutlineEffect { Color = Blue },
            new ShadowEffect { OffsetX = 1, OffsetY = 2 },
            new ShineEffect { Strength = 80 },
            new ColorOverlayEffect { Color = Red, Amount = 30 },
        ]);
        var cache = new EffectRenderCache(256L << 20);
        var options = new RenderPlanOptions { Cache = cache, CheckpointLayer = target };
        Assert.True(p.PlanFrame(3).Render(options).ContentEquals(p.Flatten(3)));

        // 마지막 효과 값만 바꾸면 앞 세 효과의 중간 결과에서 이어 그린다
        p.SetLayerEffects(layer, [.. target.Effects.Take(3), new ColorOverlayEffect { Color = Green, Amount = 70 }]);
        Assert.True(p.PlanFrame(3).Render(options).ContentEquals(p.Flatten(3)));

        // 가운데 효과를 끄면 그 앞(외곽선)까지만 같다
        p.SetLayerEffects(layer, [target.Effects[0], target.Effects[1] with { Enabled = false }, .. target.Effects.Skip(2)]);
        Assert.True(p.PlanFrame(3).Render(options).ContentEquals(p.Flatten(3)));

        // 셀 픽셀이 바뀌면(부르는 쪽이 캐시를 비움) 다시 처음부터
        cache.Clear();
        p.BeginCelStroke(layer, 3).FillRect(new Rectangle(0, 0, 5, 5), Blue);
        p.CommitCelStroke("칠");
        Assert.True(p.PlanFrame(3).Render(options).ContentEquals(p.Flatten(3)));
    }

    [Fact]
    public void Results_computed_across_a_cache_clear_are_not_stored()
    {
        // 계산하는 동안 셀이 바뀌어 비운 캐시에, 바뀌기 전 픽셀로 만든 결과가 다시 들어가면 안 된다
        var cache = new EffectRenderCache(1 << 20);
        var key = new CacheKey("ghost", 1);
        var image = new PixelImage(2, 2);
        cache.Ghost(key, _ =>
        {
            cache.Clear();
            return (image, 0, 0);
        });
        Assert.Equal(0, cache.Bytes);

        cache.Ghost(key, _ => (image, 0, 0));
        Assert.True(cache.Bytes > 0);
    }

    [Fact]
    public void Cel_plans_match_cels_and_thumbnails_show_the_raw_cel()
    {
        var p = Scene();
        // 효과를 모두 뺀 같은 장면: 그룹 썸네일은 이 장면의 그룹 합성(부위·그룹 효과 없이)
        var plain = Scene();
        for (int l = 0; l < plain.Layers.Count; l++) plain.SetLayerEffects(l, []);
        for (int l = 0; l < p.Layers.Count; l++)
            for (int f = 0; f < p.Frames.Count; f++)
            {
                var layer = p.Layers[l];
                var plan = p.PlanCel(l, f);
                PixelImage? expected = layer.IsGroup ? p.FlattenGroup(layer, f)
                    : layer.IsPart ? p.GetCel(l, f)
                    : p.GetCel(l, f) is { } cel ? LayerEffects.RenderClipped(cel, p.EffectsAt(layer, f)) : null;
                Assert.Equal(expected is null, plan is null);
                if (expected is not null) Assert.True(plan!.Render().ContentEquals(expected), $"{layer.Name} frame {f}");
                // 타임라인 썸네일: 효과 없는 고칠 그림(일반 레이어는 셀, 부위는 부위 셀, 그룹은 효과 없는 부위 합성)
                var thumb = p.PlanCelThumbnail(l, f);
                var thumbExpected = layer.IsGroup ? (plan is null ? null : plain.FlattenGroup(plain.Layers[l], f)) : p.GetCel(l, f);
                Assert.Equal(thumbExpected is null, thumb is null);
                if (thumbExpected is not null) Assert.True(thumb!.Render().ContentEquals(thumbExpected), $"{layer.Name} frame {f} thumbnail");
            }
    }

    [Fact]
    public void Raw_frame_plan_matches_the_scene_without_effects()
    {
        var p = Scene();
        var plain = Scene();
        for (int l = 0; l < plain.Layers.Count; l++) plain.SetLayerEffects(l, []);
        for (int f = 0; f < p.Frames.Count; f++)
        {
            Assert.True(p.PlanFrameRaw(f).Render().ContentEquals(plain.Flatten(f)), $"frame {f}");
            // 레이어 하나를 빼면 그 레이어만 빠진다
            var bottom = p.Layers[0];
            Assert.True(p.PlanFrameRaw(f, l => !ReferenceEquals(l, bottom)).Render()
                .ContentEquals(plain.PlanFrame(f, layerFilter: l => !ReferenceEquals(l, plain.Layers[0])).Render()), $"frame {f} without bottom");
        }
    }

    [Fact]
    public void Thumbnail_bounds_cover_the_layer_in_every_frame_and_cropped_plans_match()
    {
        var p = Scene();
        int moving = 0;
        // 움직이는 레이어: 모든 프레임의 칠한 자리를 합친 곳(썸네일이 효과 없는 그림이라 효과 여백 없이)
        var area = p.ThumbnailBounds(moving);
        var painted = p.LayerBounds(moving);
        Assert.Equal(painted, area);
        Assert.True(new Rectangle(0, 0, p.Width, p.Height).Contains(area));
        for (int f = 0; f < p.Frames.Count; f++)
        {
            var full = p.PlanCelThumbnail(moving, f)!.Render();
            Assert.True(p.PlanCelThumbnail(moving, f)!.Cropped(area).Render().ContentEquals(full.Crop(area)), $"frame {f}");
        }

        // 그룹: 부위 영역을 합친 자리(효과 여백 없이), 부위는 자르지 않음
        int group = p.IndexOf(p.Layers.First(l => l.IsGroup));
        var groupArea = p.ThumbnailBounds(group);
        Assert.False(groupArea.IsEmpty);
        Assert.True(p.PlanCel(group, 2)!.Cropped(groupArea).Render().ContentEquals(p.FlattenGroup(p.Layers[group], 2).Crop(groupArea)));
        Assert.Equal(Rectangle.Empty, p.ThumbnailBounds(p.IndexOf(p.Layers.First(l => l.IsPart))));
        // 타임라인 칸·스킨 카드가 같이 쓰는 썸네일 그림: 효과 없는 그룹 합성을 그 자리로 자른 것(스킨을 주면 그 스킨)
        var plain = Scene();
        for (int l = 0; l < plain.Layers.Count; l++) plain.SetLayerEffects(l, []);
        var groupLayer = p.Layers[group];
        var expected = plain.FlattenGroup(plain.Layers[group], 2).Crop(groupArea);
        Assert.True(p.RenderCelThumbnail(group, 2)!.ContentEquals(expected));
        Assert.True(p.RenderCelThumbnail(group, 2, groupLayer.BaseSkin)!.ContentEquals(expected));

        // 칠한 픽셀이 없는 레이어는 자르지 않음
        Assert.Equal(Rectangle.Empty, p.ThumbnailBounds(p.IndexOf(p.AddLayer(name: "빈"))));
    }

    private sealed class Recorder(bool snapshots) : IRenderObserver
    {
        public List<(int Done, int Total)> Calls { get; } = [];
        public List<PixelImage> Snapshots { get; } = [];
        public void Progress(int done, int total) => Calls.Add((done, total));
        public bool WantsSnapshot => snapshots;
        public void Snapshot(PixelImage image) => Snapshots.Add(image);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Progress_counts_every_unit_and_the_last_snapshot_is_the_result(int frame)
    {
        var p = Scene();
        var plan = p.PlanFrame(frame);
        var recorder = new Recorder(snapshots: true);
        var result = plan.Render(new RenderPlanOptions { Observer = recorder });

        Assert.True(recorder.Calls.Count >= 2);
        // 마지막은 끝 알림, 그 앞까지 센 값이 전체 단위와 정확히 같아야 진행 표시가 끝에서 멈추지 않는다
        Assert.Equal((plan.WorkUnits, plan.WorkUnits), recorder.Calls[^1]);
        Assert.Equal(plan.WorkUnits, recorder.Calls[^2].Done);
        for (int i = 1; i < recorder.Calls.Count; i++) Assert.True(recorder.Calls[i].Done >= recorder.Calls[i - 1].Done);
        Assert.True(recorder.Snapshots[^1].ContentEquals(result));
        Assert.True(result.ContentEquals(p.Flatten(frame)));
    }

    [Fact]
    public void Cancelled_render_stops()
    {
        var p = Scene();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => p.PlanFrame(3).Render(new RenderPlanOptions { Cancellation = cts.Token }));
    }
}
