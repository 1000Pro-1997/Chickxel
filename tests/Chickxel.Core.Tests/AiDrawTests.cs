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
        s.SizeTarget = 3;
        Assert.Equal(AiDrawSettings.MinSize, s.SizeTargetClamped);
        s.SizeTarget = 0;
        Assert.Equal(AiDrawSettings.DefaultSize, s.SizeTargetClamped);
    }

    [Fact]
    public void Sizes_are_hints_unless_result_size_correction_is_on()
    {
        var s = new AiDrawSettings { SizeTarget = 48 };
        Assert.False(s.ResizeResult);
        // 끔: 채팅 크기는 프롬프트 힌트만, 변환은 대표 프리셋 크기(0). 설정 크기는 쓰지 않음.
        Assert.Equal((64, 0), s.SizesFor(64));
        Assert.Equal((0, 0), s.SizesFor(null));
        Assert.Equal((AiDrawSettings.MaxSize, 0), s.SizesFor(999));
        // 켬: 채팅 크기, 없으면 설정 크기로 고정.
        s.ResizeResult = true;
        Assert.Equal((64, 64), s.SizesFor(64));
        Assert.Equal((48, 48), s.SizesFor(null));
        s.SizeTarget = 0;
        Assert.Equal((AiDrawSettings.DefaultSize, AiDrawSettings.DefaultSize), s.SizesFor(null));
    }

    [Fact]
    public void Draw_options_override_preset_only_when_turned_on()
    {
        var preset = new PixelateSettings { Background = new BackgroundSettings { Mode = BackgroundMode.None } };
        // 둘 다 끔(기본): 대표 프리셋 그대로.
        Assert.False(new AiDrawSettings().ForceRemoveBackground);
        Assert.Same(preset, AiDrawConversion.WithDrawOptions(preset, 0, removeBackground: false));
        var both = AiDrawConversion.WithDrawOptions(preset, 48, removeBackground: true);
        Assert.Equal(BackgroundMode.Auto, both.Background.Mode);
        Assert.Equal((OutputSizeMode.FixedSize, 48, 48, true), (both.Size.Mode, both.Size.Width, both.Size.Height, both.Size.KeepAspect));
        Assert.Equal(preset with { Background = both.Background, Size = both.Size }, both);
    }

    [Fact]
    public void Old_remove_background_value_is_not_read()
    {
        // 예전 기본 켬이던 RemoveBackground 값은 새 배경 지우기(기본 끔)로 옮기지 않는다.
        var s = System.Text.Json.JsonSerializer.Deserialize<AiDrawSettings>("{\"RemoveBackground\":true}")!;
        Assert.False(s.ForceRemoveBackground);
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
    public void Conversion_keeps_preset_values_and_splits_by_generated_frame_count()
    {
        var strip = AiDrawConversion.Combine([Generated(8, 0), Generated(8, 1)]);
        // 대표 프리셋이 다른 그림용 격자(2×3)로 저장돼 있어도 프레임은 생성한 수(2)대로, 나머지 값은 프리셋 그대로.
        var preset = new PixelateSettings
        {
            Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 2, Rows = 3, Align = FrameAlign.Center },
            Palette = new PaletteSettings { Shared = false },
        };
        var (result, used) = AiDrawConversion.Run(strip, preset, 2, new AiDrawSheetLayout(2, 1), optimize: false, new PixelateOptimizeOptions());
        var frames = result.CommonFrames();
        Assert.Equal(2, frames.Count);
        Assert.Equal(frames[0].Size, frames[1].Size);
        Assert.InRange(frames[0].Width, 14, 22);
        Assert.Equal((FrameSplitMode.Auto, 2, FrameAlign.Center), (used.Frames.Mode, used.Frames.ExpectedFrames, used.Frames.Align));
        Assert.Equal(preset with { Frames = used.Frames }, used);
        // 배경은 프리셋의 자동 지우기로 지워지고 스프라이트 색이 남는다.
        Assert.Equal(0, frames[0].Get(0, 0).A);
        Assert.Contains(frames[0].Pixels, p => p.A == 255 && p.R > 150 && p.G < 100);

        // 한 장이면 프리셋이 격자여도 나누지 않는다.
        var (single, singleUsed) = AiDrawConversion.Run(Generated(8, 0), preset, 1, new AiDrawSheetLayout(1, 1), optimize: false, new PixelateOptimizeOptions());
        Assert.Equal(FrameSplitMode.Single, singleUsed.Frames.Mode);
        Assert.Single(single.CommonFrames());
    }

    [Fact]
    public void Conversion_falls_back_to_layout_grid_when_frames_touch()
    {
        var sheet = AiDrawConversion.Combine([Generated(4, 0), Generated(4, 0), Generated(4, 0), Generated(4, 0)]);
        // 모든 프레임을 잇는 바닥 띠: 빈 열이 없어 자동 나누기는 한 덩어리로 본다.
        sheet.FillRect(new System.Drawing.Rectangle(0, 20 * 4, sheet.Width, 8), Dark);
        var (result, used) = AiDrawConversion.Run(sheet, new PixelateSettings(), 4, AiDrawSheetLayout.For(4), optimize: false, new PixelateOptimizeOptions());
        Assert.Equal((FrameSplitMode.Grid, 4, 1), (used.Frames.Mode, used.Frames.Columns, used.Frames.Rows));
        Assert.Equal(4, result.CommonFrames().Count);
    }

    [Fact]
    public void Conversion_without_sprite_throws()
    {
        var blank = new PixelImage(40, 40);
        blank.Clear(White);
        Assert.Throws<InvalidOperationException>(() =>
            AiDrawConversion.Run(blank, new PixelateSettings(), 1, new AiDrawSheetLayout(1, 1), false, new PixelateOptimizeOptions()));
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
        var (result, _) = AiDrawConversion.Run(source, new PixelateSettings(), 2, new AiDrawSheetLayout(2, 1), optimize: false, new PixelateOptimizeOptions());
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
        var settings = new PixelateSettings { Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 3 } };
        string first = store.Create();
        Assert.False(store.Exists(first));
        store.Save(new AiDrawJob { Id = first, Name = "knight", Frames = 3, Action = "walk", Settings = PixelatePresets.SerializeSettings(settings, false) }, source);
        Assert.True(store.Exists(first));
        var loaded = store.Load(first);
        Assert.NotNull(loaded);
        Assert.Equal("knight", loaded.Value.Job.Name);
        Assert.Equal(3, loaded.Value.Job.Frames);
        Assert.Equal(settings.Key(), loaded.Value.Job.ReadSettings().Key());
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
