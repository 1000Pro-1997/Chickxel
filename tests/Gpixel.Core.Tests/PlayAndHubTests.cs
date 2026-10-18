using System.Drawing;
using System.Numerics;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.Core.Play;

namespace Gpixel.Core.Tests;

public class PlayAndHubTests
{
    // ───────────── Unity Hub ─────────────

    private const string HubJson = """
        {
          "schema_version": "v1",
          "data": {
            "C:/Projects/Old": { "title": "Old", "path": "C:/Projects/Old", "version": "2022.3.10f1", "lastModified": 1700000000000, "isFavorite": false },
            "C:/Projects/New": { "title": "New", "path": "C:/Projects/New", "version": "6000.0.23f1", "lastModified": 1750000000000, "isFavorite": false },
            "C:/Projects/Fav": { "title": "Fav", "path": "C:/Projects/Fav", "version": "6000.0.1f1", "lastModified": 1600000000000, "isFavorite": true, "architecture": "x86_64" },
            "C:/Projects/Gone": { "title": "Gone", "path": "C:/Projects/Gone", "version": "2021.3.0f1", "lastModified": 1650000000000, "isFavorite": false }
          }
        }
        """;

    [Fact]
    public void Hub_ParsesAndSortsFavoritesThenRecent_FlagsMissing()
    {
        var list = UnityHubProjects.Parse(HubJson, p => !p.EndsWith("Gone"));
        Assert.True(list.Success);
        Assert.Equal(["Fav", "New", "Old", "Gone"], list.Projects.Select(p => p.Title));
        var fav = list.Projects[0];
        Assert.True(fav.IsFavorite);
        Assert.Equal("6000.0.1f1", fav.Version);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1600000000000), fav.LastModified);
        Assert.Equal(@"C:\Projects\Fav".Replace('\\', Path.DirectorySeparatorChar), fav.Path);
        Assert.Equal(Path.Combine(fav.Path, "Assets"), fav.AssetsPath);
        Assert.False(list.Projects.Single(p => p.Title == "Gone").Exists);
        Assert.True(list.Projects.Single(p => p.Title == "Old").Exists);
    }

    [Fact]
    public void Hub_CorruptOrMissing_ReturnsErrorWithoutThrowing()
    {
        var corrupt = UnityHubProjects.Parse("{ not json", _ => true);
        Assert.False(corrupt.Success);
        Assert.Empty(corrupt.Projects);

        var missing = UnityHubProjects.Load([Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")], _ => true);
        Assert.False(missing.Success);
        Assert.Empty(missing.Projects);

        var weird = UnityHubProjects.Parse("42", _ => true);
        Assert.False(weird.Success);
    }

    [Fact]
    public void Hub_LoadFromFile_AndMissingTitleUsesFolderName()
    {
        using var dir = new TempDir();
        var file = dir.File("projects-v1.json");
        File.WriteAllText(file, """{"schema_version":"v1","data":{"D:/Work/MyGame":{"path":"D:/Work/MyGame"}}}""");
        var list = UnityHubProjects.Load([dir.File("nope.json"), file], _ => false);
        var p = Assert.Single(list.Projects);
        Assert.Equal("MyGame", p.Title);
        Assert.Null(p.LastModified);
        Assert.False(p.Exists);
    }

    // ───────────── 애니메이션 재생 ─────────────

    [Fact]
    public void AnimationPlayer_TimeToFrame_LoopAndOnce()
    {
        var clip = new AnimationClip("walk");
        clip.AddFrame("a", 100);
        clip.AddFrame("b", 50);
        clip.AddFrame("c", 100);
        Assert.Equal(0, AnimationPlayer.FrameAt(clip, 0));
        Assert.Equal(0, AnimationPlayer.FrameAt(clip, 99.9));
        Assert.Equal(1, AnimationPlayer.FrameAt(clip, 100));
        Assert.Equal(2, AnimationPlayer.FrameAt(clip, 150));
        Assert.Equal(0, AnimationPlayer.FrameAt(clip, 250)); // 반복
        Assert.Equal(1, AnimationPlayer.FrameAt(clip, 360));

        var player = new AnimationPlayer(clip);
        player.Update(120);
        Assert.Equal("b", player.CurrentFrame!.Slice);
        player.Update(200); // 320 → 70
        Assert.Equal(0, player.FrameIndex);
        player.StepFrame();
        Assert.Equal(1, player.FrameIndex);

        clip.Loop = false;
        player.Play(clip, restart: true);
        player.Update(1000);
        Assert.Equal(2, player.FrameIndex);
        Assert.True(player.IsFinished);
        Assert.Equal(-1, AnimationPlayer.FrameAt(new AnimationClip("empty"), 10));
    }

    // ───────────── 탑뷰 ─────────────

    [Fact]
    public void TopDown_DiagonalSpeedIsNormalized_AndFacingFollowsInput()
    {
        var c = new TopDownController { Speed = 100 };
        c.Update(1f, new DirectionalInput(Left: false, Right: true, Up: false, Down: true));
        Assert.Equal(100f, c.Velocity.Length(), 3);
        Assert.Equal(100f, c.Position.Length(), 3);
        Assert.Equal(Facing.Down, c.Facing); // 대각선: 처음 방향(아래)이 이동 성분이라 유지

        var d = new TopDownController { Speed = 50 };
        d.Update(0.5f, new DirectionalInput(false, false, false, true));
        Assert.Equal(new Vector2(0, 25), d.Position);
        Assert.Equal(Facing.Down, d.Facing);
        d.Update(0.5f, new DirectionalInput(true, false, false, true)); // 대각선: 현재 방향(아래) 유지
        Assert.Equal(Facing.Down, d.Facing);
        d.Update(0.1f, new DirectionalInput(true, false, false, false));
        Assert.Equal(Facing.Left, d.Facing);
        d.Update(0.1f, default);
        Assert.False(d.IsMoving);
        Assert.Equal(Facing.Left, d.Facing);
    }

    [Fact]
    public void TopDown_AnimationSelection_MirrorsLeftForRight()
    {
        var clips = new HashSet<string> { "idle_down", "walk_down", "walk_left", "idle_left" };
        var c = new TopDownController();
        Assert.Equal(new AnimationChoice("idle_down", false), c.SelectAnimation(clips.Contains));
        c.Update(0.1f, new DirectionalInput(false, true, false, false));
        Assert.Equal(new AnimationChoice("walk_left", true), c.SelectAnimation(clips.Contains));
        c.Update(0.1f, new DirectionalInput(false, false, true, false));
        // walk_up 없음 → idle_up 없음 → idle_down
        Assert.Equal(new AnimationChoice("idle_down", false), c.SelectAnimation(clips.Contains));
        c.Update(0.1f, new DirectionalInput(false, true, false, false));
        c.Update(0.1f, default);
        Assert.Equal(new AnimationChoice("idle_left", true), c.SelectAnimation(clips.Contains));
    }

    // ───────────── 플랫포머 ─────────────

    private static PlatformerController MakePlatformer()
    {
        var c = new PlatformerController { Position = new Vector2(50, 0), SpawnPoint = new Vector2(50, 0) };
        c.Platforms.Add(new PlatformBox(new RectangleF(0, 100, 200, 20)));
        return c;
    }

    [Fact]
    public void Platformer_FallsAndLandsOnGround()
    {
        var c = MakePlatformer();
        c.Update(1f / 60, default);
        Assert.Equal(PlatformerState.Fall, c.State);
        for (int i = 0; i < 120; i++) c.Update(1f / 60, default);
        Assert.True(c.IsGrounded);
        Assert.Equal(100f, c.Position.Y, 3);
        Assert.Equal(0f, c.Velocity.Y);
        Assert.Equal(PlatformerState.Idle, c.State);
    }

    [Fact]
    public void Platformer_FallSpeedIsCapped()
    {
        var c = new PlatformerController();
        c.Settings.MaxFallSpeed = 200;
        for (int i = 0; i < 120; i++) c.Update(1f / 60, default);
        Assert.Equal(200f, c.Velocity.Y);
    }

    [Fact]
    public void Platformer_JumpsUpAndComesBack()
    {
        var c = MakePlatformer();
        for (int i = 0; i < 120; i++) c.Update(1f / 60, default);
        Assert.True(c.IsGrounded);

        c.Update(1f / 60, new PlatformerInput(false, false, JumpHeld: true, JumpPressed: true));
        Assert.False(c.IsGrounded);
        Assert.Equal(PlatformerState.Jump, c.State);
        float minY = c.Position.Y;
        bool sawFall = false;
        for (int i = 0; i < 180; i++)
        {
            c.Update(1f / 60, new PlatformerInput(false, false, JumpHeld: true, JumpPressed: false));
            minY = Math.Min(minY, c.Position.Y);
            sawFall |= c.State == PlatformerState.Fall;
        }
        // 최고 높이 ≈ v²/2g = 300²/1800 = 50px
        Assert.InRange(100 - minY, 45, 52);
        Assert.True(sawFall);
        Assert.True(c.IsGrounded);
        Assert.Equal(100f, c.Position.Y, 3);
    }

    [Fact]
    public void Platformer_RunsAndFacesAndMirrorsAnimation()
    {
        var c = MakePlatformer();
        for (int i = 0; i < 120; i++) c.Update(1f / 60, default);
        for (int i = 0; i < 30; i++)
            c.Update(1f / 60, new PlatformerInput(Left: true, Right: false, JumpHeld: false, JumpPressed: false));
        Assert.Equal(PlatformerState.Run, c.State);
        Assert.False(c.FacingRight);
        Assert.InRange(c.Position.X, 4f, 6.5f); // 0.5초 × 90px/s = 45px 왼쪽

        var clips = new HashSet<string> { "idle", "run", "jump" };
        Assert.Equal(new AnimationChoice("run", true), c.SelectAnimation(clips.Contains));
        c.Update(1f / 60, new PlatformerInput(false, true, false, false));
        Assert.Equal(new AnimationChoice("run", false), c.SelectAnimation(clips.Contains));
    }

    [Fact]
    public void Platformer_WallBlocksAndOneWayPlatformPassesFromBelow()
    {
        var c = MakePlatformer();
        c.Platforms.Add(new PlatformBox(new RectangleF(80, 60, 10, 40)));          // 벽
        c.Platforms.Add(new PlatformBox(new RectangleF(0, 70, 60, 4), OneWay: true)); // 머리 위 발판
        for (int i = 0; i < 120; i++) c.Update(1f / 60, default);
        for (int i = 0; i < 120; i++) c.Update(1f / 60, new PlatformerInput(false, true, false, false));
        Assert.Equal(80 - c.ColliderSize.Width / 2, c.Position.X, 3);

        // 왼쪽으로 돌아가 발판 아래에서 점프 → 통과해 발판 위에 선다
        c.Position = new Vector2(30, 100);
        c.Update(1f / 60, new PlatformerInput(false, false, true, true));
        for (int i = 0; i < 180; i++) c.Update(1f / 60, new PlatformerInput(false, false, true, false));
        Assert.True(c.IsGrounded);
        Assert.Equal(70f, c.Position.Y, 3);
    }

    [Fact]
    public void Platformer_RespawnsBelowKillY()
    {
        var c = new PlatformerController { SpawnPoint = new Vector2(5, 5), KillY = 300 };
        for (int i = 0; i < 300; i++) c.Update(1f / 60, default);
        Assert.True(c.Position.Y <= 300);
    }
}
