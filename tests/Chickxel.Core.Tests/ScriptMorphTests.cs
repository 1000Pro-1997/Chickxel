using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Xunit;

namespace Chickxel.Core.Tests;

/// <summary>첫·끝 프레임만 그림이 있고 사이는 빈 셀인 문서에서 다른 프레임을 읽는 변신 스크립트가 사이 프레임을 채운다.</summary>
public class ScriptMorphTests
{
    private static readonly Color32 Red = new(255, 0, 0), Blue = new(0, 0, 255);

    [Fact]
    public void Morph_script_fills_empty_frames_between_range_ends_on_the_fast_path()
    {
        var p = new SpriteProject(8, 8);
        for (int i = 0; i < 4; i++) p.AddFrame();
        var a = new PixelImage(8, 8);
        a[1, 1] = Red;
        a[2, 1] = Red;
        var b = new PixelImage(8, 8);
        b[6, 6] = Blue;
        p.SetCel(0, 0, a);
        p.SetCel(0, 4, b);
        var hash = p.AddScript("""
            effect({ name: "morph", animated: true,
              render(img, ctx, s) {
                const a = ctx.frameSource(ctx.rangeStart), b = ctx.frameSource(ctx.rangeEnd);
                if (!a || !b) return;
                const m = ctx.matchPixels(a, b);
                if (!m) return;
                const d = img.data, w = img.width, p = ctx.progress;
                for (let i = 0; i < d.length; i++) d[i] = 0;
                for (let k = 0; k < m.count; k++) {
                  const ax = m.data[k * 4], ay = m.data[k * 4 + 1], bx = m.data[k * 4 + 2], by = m.data[k * 4 + 3];
                  const x = Math.round(ax + (bx - ax) * p), y = Math.round(ay + (by - ay) * p);
                  const ai = (ay * a.width + ax) * 4, bi = (by * b.width + bx) * 4, o = (y * w + x) * 4;
                  for (let c = 0; c < 4; c++) d[o + c] = a.data[ai + c] + (b.data[bi + c] - a.data[ai + c]) * p;
                }
              } });
            // Morph_script_fills_empty_frames_between_range_ends_on_the_fast_path
            """);
        ScriptLibrary.SetTrust(hash, true);
        var program = ScriptLibrary.Runnable(hash)!;
        Assert.True(program.ExecutionPath.Compiled, program.ExecutionPath.Reason);
        p.SetLayerEffects(0, [new ScriptEffect { ScriptHash = hash, Settings = ScriptSettingValues.Normalized(program, null) }]);

        Assert.Equal(Red, p.Flatten(0)[1, 1]);
        // 빈 셀인 가운데 프레임: 두 빨강 픽셀이 파랑 하나로 모이는 중간(진행 0.5)
        var middle = p.Flatten(2);
        Assert.Equal(new Color32(128, 0, 128), middle[4, 4]);
        Assert.Equal(1, middle.Pixels.Count(c => c.A != 0));
        Assert.Equal(Blue, p.Flatten(4)[6, 6]);
    }
}
