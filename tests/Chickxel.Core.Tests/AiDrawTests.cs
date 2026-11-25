using Chickxel.Core.AiDraw;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;
using Chickxel.Core.References;

namespace Chickxel.Core.Tests;

public class AiDrawTests
{
    private static readonly Color32 Red = new(210, 40, 40), Dark = new(30, 15, 15), White = new(255, 255, 255);

    // ───────────── 설정 ─────────────

    [Fact]
    public void Settings_auto_generates_unless_current_file_only_and_manual_modes_override_it()
    {
        var s = new AiDrawSettings();
        Assert.Equal(AiDrawMode.Auto, s.Mode);
        // 자동: "지금 그림만"이 꺼져 있으면 생성 먼저, 켜져 있으면 직접 찍기. Claude는 늘 직접 찍기.
        Assert.Equal(AiDrawMode.Generate, s.EffectiveMode("codex", currentFileOnly: false));
        Assert.Equal(AiDrawMode.Direct, s.EffectiveMode("codex", currentFileOnly: true));
        Assert.Equal(AiDrawMode.Direct, s.EffectiveMode("claude", currentFileOnly: false));
        // 직접 고른 방식은 "지금 그림만"과 관계없이 그대로.
        s.Mode = AiDrawMode.Generate;
        Assert.Equal(AiDrawMode.Generate, s.EffectiveMode("codex", currentFileOnly: true));
        s.Mode = AiDrawMode.Direct;
        Assert.Equal(AiDrawMode.Direct, s.EffectiveMode("codex", currentFileOnly: false));
    }

    [Fact]
    public void Settings_frames_use_defaults_and_clamp()
    {
        var s = new AiDrawSettings();
        Assert.Equal(6, s.FramesFor(AiDrawActions.Walk));
        Assert.Equal(4, s.FramesFor(AiDrawActions.Idle));
        Assert.Equal(4, s.FramesFor("wave"));
        s.FrameCounts[AiDrawActions.Walk] = 40;
        Assert.Equal(AiDrawSettings.MaxFrames, s.FramesFor(AiDrawActions.Walk));
        s.FrameCounts.Remove(AiDrawActions.Attack);
        Assert.Equal(6, s.FramesFor(AiDrawActions.Attack));
    }

    // ───────────── 프레임 계획 ─────────────

    [Fact]
    public void Plan_without_action_is_one_frame()
    {
        var plan = AiDrawPlanner.Plan(null, 6, ["", "waving"]);
        var f = Assert.Single(plan);
        Assert.Equal(1, f.Count);
        Assert.Equal("waving", f.Pose);
    }

    [Fact]
    public void Plan_walk_six_uses_each_key_pose_once()
    {
        var plan = AiDrawPlanner.Plan("walk", 6, null);
        Assert.Equal(6, plan.Count);
        Assert.Equal(AiDrawActions.KeyPoses("walk"), plan.Select(f => f.Pose));
        Assert.All(plan, f => Assert.Equal(6, f.Count));
        Assert.Equal(Enumerable.Range(0, 6), plan.Select(f => f.Index));
    }

    [Fact]
    public void Plan_samples_key_poses_with_in_betweens_and_loops()
    {
        var keys = AiDrawActions.KeyPoses("walk");
        var plan = AiDrawPlanner.Plan("walk", 4, null);
        Assert.Equal(keys[0], plan[0].Pose);
        Assert.StartsWith("in-between", plan[1].Pose);
        Assert.Equal(keys[3], plan[2].Pose);
        Assert.Contains(keys[4], plan[3].Pose);
        Assert.Contains(keys[5], plan[3].Pose);
        // 되풀이 동작: 마지막 사이 자세는 첫 자세로 되돌아간다(대기 4열쇠, 7프레임 → 마지막 t = 3.43).
        var idle = AiDrawActions.KeyPoses("idle");
        var last = AiDrawPlanner.Plan("idle", 7, null)[^1].Pose;
        Assert.Equal($"in-between pose halfway from \"{idle[3]}\" to \"{idle[0]}\"", last);
    }

    [Fact]
    public void Plan_one_shot_runs_from_first_to_last_key()
    {
        var keys = AiDrawActions.KeyPoses("attack");
        var plan = AiDrawPlanner.Plan("attack", 3, null);
        Assert.Equal(keys[0], plan[0].Pose);
        Assert.Equal(keys[^1], plan[^1].Pose);
    }

    [Fact]
    public void Plan_given_poses_win_and_frame_count_is_clamped()
    {
        var plan = AiDrawPlanner.Plan("jump", 3, ["crouch low", "", "in the air"]);
        Assert.Equal("crouch low", plan[0].Pose);
        Assert.NotEqual("", plan[1].Pose);
        Assert.Equal("in the air", plan[2].Pose);
        Assert.Equal(AiDrawSettings.MaxFrames, AiDrawPlanner.Plan("walk", 99, null).Count);
        Assert.Equal(AiDrawSettings.MinFrames, AiDrawPlanner.Plan("walk", 1, null).Count);
    }

    [Fact]
    public void Plan_unknown_action_describes_frames()
    {
        var plan = AiDrawPlanner.Plan("wave", 3, null);
        Assert.Contains("frame 1 of 3", plan[0].Pose);
        Assert.Contains("\"wave\"", plan[2].Pose);
        Assert.Contains("looping", plan[2].Pose);
    }

    // ───────────── 스타일 참고 고르기 ─────────────

    private static ReferenceItem Ref(string id, string kind, int size, bool builtIn = false, int colors = 8)
        => new() { Id = id, Kind = kind, Width = size, Height = size, Colors = colors, BuiltIn = builtIn, Frames = 1 };

    [Fact]
    public void Style_pick_prefers_kind_then_size()
    {
        var items = new[] { Ref("sword", "item", 32), Ref("hero16", "character", 16), Ref("hero32", "character", 32), Ref("note", "other", 0) };
        Assert.Equal("hero32", AiDrawStyle.Pick(items, null, "character", 0)!.Id);
        Assert.Equal("hero16", AiDrawStyle.Pick(items, null, "character", 16)!.Id);
        Assert.Equal("sword", AiDrawStyle.Pick(items, null, "item", 0)!.Id);
    }

    [Fact]
    public void Style_pick_uses_text_scores_and_can_require_them()
    {
        var items = new[] { Ref("builtin/hero", "character", 32, builtIn: true), Ref("my/slime", "character", 16) };
        var scores = new Dictionary<string, double> { ["my/slime"] = 1.0 };
        Assert.Equal("my/slime", AiDrawStyle.Pick(items, scores, "character", 32)!.Id);
        Assert.Equal("my/slime", AiDrawStyle.Pick(items, scores, "character", 32, requireText: true)!.Id);
        Assert.Null(AiDrawStyle.Pick(items, null, "character", 32, requireText: true));
        Assert.Null(AiDrawStyle.Pick([], null, "character", 32));
    }

    [Fact]
    public void Style_pick_prefers_user_reference_on_tie()
    {
        var items = new[] { Ref("builtin/a", "character", 32, builtIn: true), Ref("user/b", "character", 32) };
        Assert.Equal("user/b", AiDrawStyle.Pick(items, null, "character", 32)!.Id);
    }

    // ───────────── 프레임 잇기·변환 ─────────────

    [Fact]
    public void Combine_pads_frames_to_common_size_side_by_side()
    {
        var a = new PixelImage(4, 4);
        a.Clear(White);
        var b = new PixelImage(2, 6);
        b.Pixels[1] = Red;
        var strip = AiDrawConversion.Combine([a, b]);
        Assert.Equal(8, strip.Width);
        Assert.Equal(6, strip.Height);
        // a(흰 바탕)는 가운데에, 남는 곳도 흰색(왼쪽 위 픽셀 색).
        Assert.Equal(White, strip.Get(0, 0));
        Assert.Equal(White, strip.Get(2, 3));
        // b(투명 바탕)는 가로 가운데(4 + 1): 빨강이 (6, 0), 남는 곳은 투명.
        Assert.Equal(Red, strip.Get(6, 0));
        Assert.Equal(0, strip.Get(4, 3).A);
        Assert.Same(a, AiDrawConversion.Combine([a]));
    }

    [Fact]
    public void Downscale_averages_blocks_weighted_by_alpha()
    {
        var img = new PixelImage(4, 2);
        img.Pixels[0] = Red;
        img.Pixels[1] = Red;
        img.Pixels[4] = Red;
        img.Pixels[5] = Red;
        img.Pixels[2] = Red; // 반쯤 칠한 칸
        var small = AiDrawConversion.Downscale(img, 2);
        Assert.Equal(2, small.Width);
        Assert.Equal(1, small.Height);
        Assert.Equal(Red, small.Pixels[0]);
        Assert.Equal(Red.R, small.Pixels[1].R);
        Assert.Equal(63, small.Pixels[1].A);
        Assert.Same(img, AiDrawConversion.Downscale(img, 8));
    }

    [Fact]
    public void Combine_keeps_full_size_and_shrinks_only_past_image_limit()
    {
        var big = new PixelImage(1024, 1024);
        var full = AiDrawConversion.Combine([big, big, big, big]);
        Assert.Equal((4096, 1024), (full.Width, full.Height));

        // 2000px 프레임 9장 = 18000px 띠 > PixelImage.MaxSize: 모든 프레임을 같은 정수 배(×½)로 줄인다.
        var wide = new PixelImage(2000, 2);
        wide.Pixels.AsSpan().Fill(Red);
        var strip = AiDrawConversion.Combine(Enumerable.Repeat(wide, 9).ToList());
        Assert.Equal((9000, 1), (strip.Width, strip.Height));
        Assert.Equal(Red, strip.Get(8999, 0));
    }

    /// <summary>16×16 원 스프라이트(빨강 + 1px 어두운 외곽선, 흰 바탕)를 <paramref name="scale"/>배로 키운 그림 — 생성 그림 흉내.</summary>
    private static PixelImage Generated(int scale, int shift)
    {
        var img = new PixelImage(24 * scale, 24 * scale);
        img.Clear(White);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                double d = Math.Sqrt((x - 7.5) * (x - 7.5) + (y - 7.5 - shift) * (y - 7.5 - shift));
                if (d > 7) continue;
                var c = d > 6 ? Dark : Red;
                img.FillRect(new System.Drawing.Rectangle((x + 4) * scale, (y + 4) * scale, scale, scale), c);
            }
        return img;
    }

    [Fact]
    public void Generated_gif_frames_convert_in_order_with_shared_canvas()
    {
        using var dir = new TempDir();
        var gif = Path.Combine(dir.Path, "animation.gif");
        var originals = new[] { Generated(4, -2), Generated(4, 2) };
        Chickxel.Core.IO.GifWriter.Save(originals.Select(f => new Chickxel.Core.IO.GifFrame(f, 120)).ToArray(),
            gif, new Chickxel.Core.IO.GifOptions());
        var decoded = Chickxel.Core.IO.GifReader.Load(gif, originals.Length);
        Assert.Equal(2, decoded.Images.Count);
        Assert.Equal(new[] { 120, 120 }, decoded.DurationsMs);
        var source = AiDrawConversion.Combine(decoded.Images);
        // 정리 창은 이은 원본을 칸마다 한 프레임(가로 2칸 격자)으로 나눈다.
        var settings = new PixelateSettings { Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 2, Rows = 1 } };
        var result = Pixelator.Run(PixelateSource.Prepare(source, settings), settings);
        var frames = result.CommonFrames();
        Assert.False(result.Failed);
        Assert.Equal(2, frames.Count);
        Assert.Equal(frames[0].Size, frames[1].Size);
        Assert.False(frames[0].ContentEquals(frames[1]));
    }

    [Fact]
    public void Gif_frame_prompt_describes_motion_and_single_image_output()
    {
        var request = new AiDrawRequest { Subject = "red fox with long ears", Action = "walk", SizeTarget = 32 };
        var frames = AiDrawPlanner.Plan("walk", 2, null);
        var prompt = AiDrawPrompts.GifFrame(request, frames[^1], [AiDrawImageRole.FirstFrame]);
        Assert.Contains("frame 2 of 2", prompt);
        Assert.Contains("seamless pixel-art animated GIF", prompt);
        Assert.Contains("individual still image", prompt);
        Assert.Contains("last frame back to the first", prompt);
        Assert.Contains("Image 1: frame 1", prompt);
    }

    // ───────────── 스프라이트 시트 ─────────────

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(6, 6, 1)]
    [InlineData(7, 4, 2)]
    [InlineData(8, 4, 2)]
    [InlineData(12, 6, 2)]
    public void Sheet_layout_uses_one_row_up_to_six_then_two_rows(int frames, int columns, int rows)
        => Assert.Equal(new AiDrawSheetLayout(columns, rows), AiDrawSheetLayout.For(frames));

    // ───────────── 생성 기록 ─────────────

    [Fact]
    public void Job_store_saves_loads_and_prunes()
    {
        using var dir = new TempDir();
        var store = new AiDrawJobStore(dir.Path);
        var source = Generated(2, 0);
        string first = store.Create();
        Assert.False(store.Exists(first));
        store.Save(new AiDrawJob { Id = first, Name = "knight", Frames = 3, Columns = 4, Rows = 2, Action = "walk" }, source);
        Assert.True(store.Exists(first));
        var loaded = store.Load(first);
        Assert.NotNull(loaded);
        Assert.Equal("knight", loaded.Value.Job.Name);
        Assert.Equal(3, loaded.Value.Job.Frames);
        Assert.Equal((4, 2), (loaded.Value.Job.Columns, loaded.Value.Job.Rows));
        Assert.True(source.ContentEquals(loaded.Value.Source));
        Assert.Null(store.Load("missing"));

        // 이름 순(만든 때)으로 오래된 것부터 지우고 방금 것은 남긴다.
        Directory.CreateDirectory(Path.Combine(dir.Path, "00000000-old"));
        store.Prune(1, first);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "00000000-old")));
        Assert.True(store.Exists(first));
        Assert.Throws<ArgumentException>(() => store.DirectoryOf("../x"));
    }
}
