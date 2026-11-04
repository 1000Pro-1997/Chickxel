using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 피벗 같이 옮기기(<see cref="LayerEffect.MovePivot"/>): 그림을 옮긴 효과가 <c>ctx.movePivot</c>으로 알린 거리만큼 뒤 효과의 피벗이 따라가는지,
/// 끄면 그대로인지, 중간 결과 캐시에서도 같은지, 저장되는지.
/// </summary>
public class EffectMovePivotTests
{
    private static readonly Color32 White = new(255, 255, 255);

    private static string Unique(string source, [CallerMemberName] string test = "") => source + "\n// " + test + "\n";

    /// <summary>그림을 오른쪽으로 2px 옮기고 그만큼 알리는 스크립트.</summary>
    private static ScriptEffect Slide([CallerMemberName] string test = "")
    {
        var program = EffectParity.Trusted(Unique("""
            effect({ name: "slide", padding: [0, 0, 2, 0],
              render(img, ctx, s) {
                const w = img.width, h = img.height, d = img.data;
                for (let y = 0; y < h; y++)
                  for (let x = w - 1; x >= 0; x--)
                    for (let c = 0; c < 4; c++) {
                      const o = (y * w + x) * 4 + c;
                      d[o] = x >= 2 ? d[o - 8] : 0;
                    }
                ctx.movePivot(2, 0);
              } });
            """, test));
        Assert.NotNull(program.Fast); // ctx.movePivot도 빠른 경로로 돈다
        return new ScriptEffect { ScriptHash = program.ContentHash };
    }

    /// <summary>피벗을 기준으로 시계 방향 90°(공식 회전 스크립트).</summary>
    private static LayerEffect Rotate90() => LegacyEffects.ToScript(new RotateEffect { Angle = 90 }, EffectParity.Official("rotate"));

    /// <summary>9×9 원본의 (<paramref name="x"/>, <paramref name="y"/>)에 흰 점 하나.</summary>
    private static PixelImage Dot(int x, int y)
    {
        var image = new PixelImage(9, 9);
        image[x, y] = White;
        return image;
    }

    /// <summary>효과를 마지막 프레임(진행 1)으로 그린 뒤 하나뿐인 점의 자리(원본 왼쪽 위 기준).</summary>
    private static Point DotAfter(PixelImage source, IReadOnlyList<LayerEffect> effects)
    {
        var (image, ox, oy) = LayerEffects.Render(source, LayerEffects.At(effects, 1, 2));
        var b = image.GetOpaqueBounds();
        Assert.Equal(new Size(1, 1), b.Size);
        return new Point(b.X + ox, b.Y + oy);
    }

    [Fact]
    public void MovedSubject_LaterRotationTurnsAroundTheMovedPivot_UnlessMovePivotIsOff()
    {
        // 점은 원본 가운데(피벗 = 가운데 4.5, 4.5) — 옮긴 뒤 (6, 4)
        var slide = Slide();
        var rotate = Rotate90();

        // 켜짐(기본): 피벗도 2px 옮겨져 점이 회전 중심이라 제자리
        Assert.Equal(new Point(6, 4), DotAfter(Dot(4, 4), [slide, rotate]));
        using (ScriptEffectProgram.UseJintOnly())
            Assert.Equal(new Point(6, 4), DotAfter(Dot(4, 4), [slide, rotate]));

        // 꺼짐: 그림만 옮기고 회전은 원래 피벗 — 옮겨 둔 점을 회전만 한 것과 같다
        var original = DotAfter(Dot(6, 4), [rotate]);
        Assert.NotEqual(new Point(6, 4), original);
        Assert.Equal(original, DotAfter(Dot(4, 4), [slide with { MovePivot = false }, rotate]));

        // 꺼진 효과는 그리지도 옮기지도 않는다
        Assert.Equal(new Point(4, 4), DotAfter(Dot(4, 4), [slide with { Enabled = false }, rotate]));
    }

    /// <summary>여백 없이 그림을 오른쪽으로 6px 옮기는 스크립트(<paramref name="report"/>면 ctx.movePivot으로 알림).</summary>
    private static ScriptEffect Hop(bool report, [CallerMemberName] string test = "")
    {
        var program = EffectParity.Trusted(Unique($$"""
            effect({ name: "hop", padding: [0, 0, 0, 0],
              render(img, ctx, s) {
                const w = img.width, h = img.height, d = img.data;
                for (let y = 0; y < h; y++)
                  for (let x = w - 1; x >= 0; x--)
                    for (let c = 0; c < 4; c++) {
                      const o = (y * w + x) * 4 + c;
                      d[o] = x >= 6 ? d[o - 24] : 0;
                    }
                {{(report ? "ctx.movePivot(6, 0);" : "")}}
              } });
            """, test + report));
        return new ScriptEffect { ScriptHash = program.ContentHash };
    }

    [Fact]
    public void SubjectMovedPastThePadding_IsDrawnInAWiderArea_OnlyWhenTheMoveIsReported()
    {
        // 알리면 옮겨 간 만큼 여백을 넓혀 다시 그려 점이 잘리지 않는다(원본 (4,4) → (10,4), 원본 폭 9 밖)
        Assert.Equal(new Point(10, 4), DotAfter(Dot(4, 4), [Hop(report: true)]));
        // 알리지 않으면 정한 여백(0) 그대로라 원본 밖으로 나간 점은 잘린다
        var (image, _, _) = LayerEffects.Render(Dot(4, 4), LayerEffects.At([Hop(report: false)], 1, 2));
        Assert.True(image.GetOpaqueBounds().IsEmpty);
    }

    [Fact]
    public void RestoredCheckpoint_KeepsThePivotShiftOfTheEffectsBeforeIt()
    {
        var effects = LayerEffects.At([Slide(), Rotate90()], 1, 2);
        var source = Dot(4, 4);
        var padding = LayerEffects.Padding(effects, source.Width, source.Height);
        var cache = new EffectRenderCache(1 << 24);
        var scope = new object();
        PixelImage Run() => LayerEffects.Render(source, effects, padding, new EffectRunHooks { Cache = cache, CheckpointScope = scope }).Image;

        var first = Run();
        var rect = new Rectangle(padding.Left, padding.Top, source.Width, source.Height);
        Assert.True(cache.TryCheckpoint(new CacheKey("prefix", scope, first.Width, first.Height, rect, new EffectListKey(effects, 1)), out _, out var moves));
        Assert.Equal(new PointF(2, 0), moves.Shift);
        // 두 번째는 옮기기까지의 중간 결과를 꺼내 회전만 다시 한다 — 피벗 이동도 이어받아 결과가 같다
        Assert.True(first.ContentEquals(Run()));
        Assert.True(first.ContentEquals(LayerEffects.Render(source, effects, padding).Image));
    }

    [Fact]
    public void OfficialBobAndShake_ReportTheOffsetTheyApply()
    {
        var source = new Rectangle(0, 2, 8, 8);
        var image = new PixelImage(8, 12);
        image[3, 5] = White;
        // 둥실둥실 높이 2, 진행 ¼ = 위로 2px
        Assert.Equal(new PointF(0, -2), EffectParity.Official("bob").Render(image, source, new ScriptFrameContext { Progress = 0.25 }));
        Assert.Equal(White, image[3, 3]);

        // 규칙적인 가로 흔들기 세기 2·횟수 1, 진행 ¼ = 오른쪽으로 2px
        var shake = new Dictionary<string, object> { ["regular"] = true, ["decay"] = false, ["count"] = 1 };
        var wide = new PixelImage(12, 8);
        wide[5, 3] = White;
        Assert.Equal(new PointF(2, 0), EffectParity.Official("shake").Render(wide, new Rectangle(2, 0, 8, 8), new ScriptFrameContext { Progress = 0.25 }, shake));
        Assert.Equal(White, wide[7, 3]);
    }

    /// <summary>하나뿐인 점의 자리(그림 기준).</summary>
    private static Point OnlyDot(PixelImage image)
    {
        var b = image.GetOpaqueBounds();
        Assert.Equal(new Size(1, 1), b.Size);
        return b.Location;
    }

    [Fact]
    public void HeldEnd_OnLinkedCelsAfterRange_KeepsMovingTheLaterEffectsPivot()
    {
        // 프레임 0~3이 한 그림(점 (4,4)). 옮기기 0~1(끝 모습 이어 가기), 회전 2~3 — 링크된 2~3에도 옮긴 자리가 이어져 회전 중심이 따라간다
        PixelImage Frame3(bool movePivot, out PointF shift)
        {
            var p = new SpriteProject(9, 9);
            for (int i = 0; i < 3; i++) p.AddFrame();
            p.SetCel(0, 0, Dot(4, 4));
            p.LinkCels([(0, 0), (0, 1), (0, 2), (0, 3)]);
            p.SetLayerEffects(0, [Slide() with { Frames = new FrameRange(0, 1), HoldEnd = true, MovePivot = movePivot }, Rotate90() with { Frames = new FrameRange(2, 3) }]);
            Assert.Equal(movePivot ? new PointF(2, 0) : PointF.Empty, p.EffectPivotShift(p.Layers[0], 1));
            shift = p.EffectPivotShift(p.Layers[0], 3);
            return p.PlanCel(0, 3)!.Render();
        }

        Assert.Equal(new Point(6, 4), OnlyDot(Frame3(movePivot: true, out var held)));
        Assert.Equal(new PointF(2, 0), held);
        // 끄면 이어 가는 끝 모습은 옮긴 그림 그대로지만 회전은 원래 피벗(가운데)을 돈다 — 옮겨 둔 점을 회전만 한 것과 같다
        Assert.Equal(DotAfter(Dot(6, 4), [Rotate90()]), OnlyDot(Frame3(movePivot: false, out var still)));
        Assert.Equal(PointF.Empty, still);
    }

    [Fact]
    public void HeldEnd_OnUnlinkedCelsAfterRange_KeepsOnlyTheMovedPosition()
    {
        // 프레임 0~1이 한 그림(점 (4,4)), 옮기기 0~1(끝 모습 이어 가기). 2는 다른 그림(점 (4,2)) — 링크가 아니어도 옮긴 자리(+2,0)와 피벗 이동이 이어진다
        var p = new SpriteProject(9, 9);
        for (int i = 0; i < 3; i++) p.AddFrame();
        p.SetCel(0, 0, Dot(4, 4));
        p.LinkCels([(0, 0), (0, 1), (0, 3)]);
        p.SetCel(0, 2, Dot(4, 2));
        var slide = Slide() with { Frames = new FrameRange(0, 1), HoldEnd = true };
        p.SetLayerEffects(0, [slide]);
        Assert.Equal(new Point(6, 2), OnlyDot(p.PlanCel(0, 2)!.Render()));
        Assert.Equal(new PointF(2, 0), p.EffectPivotShift(p.Layers[0], 2));

        // 뒤 효과(회전, 프레임 2에서 진행 1)는 옮긴 자리를 돈다 — 피벗 같이 옮기기를 끄면 원래 피벗을 돈다
        var rotate = Rotate90() with { Frames = new FrameRange(1, 2) };
        p.SetLayerEffects(0, [slide, rotate]);
        var rotatedAt = OnlyDot(p.PlanCel(0, 2)!.Render());
        p.SetLayerEffects(0, [slide with { MovePivot = false }, rotate]);
        var still = OnlyDot(p.PlanCel(0, 2)!.Render());
        Assert.Equal(DotAfter(Dot(6, 2), [Rotate90()]), still);
        Assert.NotEqual(still, rotatedAt);
        Assert.Equal(PointF.Empty, p.EffectPivotShift(p.Layers[0], 2));

        // 효과를 끄면 원래 자리
        p.SetLayerEffects(0, [slide with { Enabled = false }]);
        Assert.Equal(new Point(4, 2), OnlyDot(p.PlanCel(0, 2)!.Render()));

        // 3을 풀면 끝 모습 대신 옮긴 자리만 남는다 — 그림에 넣지 않으므로 두 번 옮겨지지 않는다
        p.SetLayerEffects(0, [slide]);
        Assert.Equal(new Point(6, 4), OnlyDot(p.PlanCel(0, 3)!.Render()));
        Assert.Equal(new UnlinkResult(1, 0), p.UnlinkCels([(0, 3)]));
        Assert.Equal(new Point(4, 4), OnlyDot(p.GetCel(0, 3)!));
        Assert.Equal(new Point(6, 4), OnlyDot(p.PlanCel(0, 3)!.Render()));
    }

    [Fact]
    public void PartInFrame_DrawsOnlyThatPartWithPartAndGroupEffects_AtFullOpacity()
    {
        var p = new SpriteProject(12, 12);
        var group = p.AddGroup("몸");
        var part = p.AddPart(group, "공", 4, 4, new Point(2, 2));
        var other = p.AddPart(group, "다른", 2, 2, new Point(8, 8));
        var cel = new PixelImage(4, 4);
        cel[1, 1] = White;
        p.SetCel(p.IndexOf(part), 0, cel);
        var blue = new PixelImage(2, 2);
        blue[0, 0] = new Color32(0, 0, 255);
        p.SetCel(p.IndexOf(other), 0, blue);
        p.SetLayerProperties(p.IndexOf(part), opacity: 128);
        p.SetLayerEffects(p.IndexOf(part), [Slide()]);

        // 부위 효과: 부위 자리 (3,3)에서 2px 옮겨 프레임 좌표 (5,3), 다른 부위는 없고 불투명도는 보지 않는다
        var alone = p.PlanPartInFrame(part, 0).Render();
        Assert.Equal(new Size(12, 12), alone.Size);
        Assert.Equal(new Point(5, 3), OnlyDot(alone));
        Assert.Equal(White, alone[5, 3]);
        Assert.Equal(new PointF(2, 0), p.EffectPivotShift(part, 0));

        // 그룹 효과도 입힌다(이 부위만 든 그룹 합성) — 피벗 이동도 부위 + 그룹
        p.SetLayerEffects(p.IndexOf(group), [Slide()]);
        Assert.Equal(new Point(7, 3), OnlyDot(p.PlanPartInFrame(part, 0).Render()));
        Assert.Equal(new PointF(4, 0), p.EffectPivotShift(part, 0));
        Assert.Equal(new PointF(2, 0), p.EffectPivotShift(group, 0));
    }

    [Fact]
    public void MovePivot_SavedOnlyWhenOff_OldFilesReadAsOn()
    {
        var on = EffectCodec.Write(new ShadowEffect());
        Assert.False(on.ContainsKey(EffectCodec.MovePivotKey));
        Assert.True(EffectCodec.Read(on).MovePivot);

        var off = EffectCodec.Write(new ShadowEffect { MovePivot = false });
        Assert.False(off[EffectCodec.MovePivotKey]!.GetValue<bool>());
        // 예전 기본 효과는 공식 스크립트로 바뀌어 읽혀도 값이 남는다
        Assert.False(EffectCodec.Read(off).MovePivot);

        var p = new SpriteProject(4, 4);
        p.SetLayerEffects(0, [new OutlineEffect(), new ShadowEffect { MovePivot = false }]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        Assert.Equal([true, false], ProjectIO.Load(stream).Layers[0].Effects.Select(e => e.MovePivot));

        // 모르는 효과도 값을 지킨다
        var unknown = EffectCodec.Read((JsonObject)JsonNode.Parse("""{ "type": "example.unknown", "movePivot": false }""")!);
        Assert.IsType<MissingEffect>(unknown);
        Assert.False(unknown.MovePivot);
        Assert.True(EffectCodec.Write(unknown with { MovePivot = true })[EffectCodec.MovePivotKey]!.GetValue<bool>());
    }

    [Fact]
    public void DeclaredDefaults_StartNewEffects_AndDeclarationKeyIsNotAPivotReport()
    {
        const string body = "render(img, ctx, s) {} });";
        var declared = ScriptEffectProgram.Parse(Unique("effect({ name: 'x', animated: true, repeat: 'PingPong', repeatCount: 3, movePivot: false, " + body)).Program!;
        var effect = declared.CreateEffect();
        Assert.Equal((EffectRepeat.PingPong, 3, false), (effect.Repeat, effect.RepeatCount, effect.MovePivot));
        var plain = ScriptEffectProgram.Parse(Unique("effect({ name: 'y', animated: true, " + body)).Program!.CreateEffect();
        Assert.Equal((EffectRepeat.Once, 1, true), (plain.Repeat, plain.RepeatCount, plain.MovePivot));
        foreach (var wrong in new[] { "repeat: 'loop'", "repeatCount: 0", "repeatCount: 9", "repeatCount: 1.5", "movePivot: 0" })
            Assert.Contains(ScriptEffectProgram.Parse("effect({ name: 'x', " + wrong + ", " + body).Errors, e => e.Kind == ScriptErrorKind.Declaration);

        // 선언 키 movePivot만으로는 옮긴 거리를 알리는 효과가 아니다(설정 줄·pivot-move 태그 없음)
        var hash = ScriptLibrary.Add(declared.Source);
        Assert.False(ScriptLibrary.InfoOf(hash)!.MovesPivot);
        Assert.True(ScriptLibrary.InfoOf(ScriptLibrary.Add(Unique("effect({ name: 'z', render(img, ctx) { ctx.movePivot(1, 0); } });")))!.MovesPivot);
    }

    [Fact]
    public void SetEffectDefaults_WritesChangedKeys_AndRemovesAppDefaults()
    {
        Assert.Equal("effect({ name: 'x', animated: true, repeat: \"Loop\", repeatCount: 2, movePivot: false, render() {} });",
            ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', animated: true, render() {} });",
                repeat: EffectRepeat.Loop, repeatCount: 2, movePivot: false));
        Assert.Equal("effect({ name: 'x', animated: true, render() {} });",
            ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', animated: true, easing: \"EaseIn\", repeat: \"Loop\", movePivot: false, render() {} });",
                easing: EffectEasing.Linear, repeat: EffectRepeat.Once, movePivot: true));
        Assert.Equal("effect({\n  name: 'x',\n  movePivot: false,\n  render() {},\n});",
            ScriptDeclarationEdit.SetEffectDefaults("effect({\n  name: 'x',\n  render() {},\n});", movePivot: false));
        Assert.Null(ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', render() {", movePivot: false));
    }
}
