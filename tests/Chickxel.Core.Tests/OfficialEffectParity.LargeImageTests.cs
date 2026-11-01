using System.Diagnostics;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Xunit.Abstractions;

namespace Chickxel.Core.Tests;

/// <summary>
/// 공식 스크립트 효과가 Jint의 배열 한도(2048²)보다 큰 그림도 빠른 경로로 끝까지 그리고 기본 효과와 바이트까지 같은지(묶음마다 하나, 4096×1024).
/// 한도는 그림 크기에 비례하므로(<see cref="Scripting.ScriptLimits.ForPixels"/>) 큰 그림에서 멈추지 않는다.
/// </summary>
public class OfficialEffectParityLargeImageTests(ITestOutputHelper output)
{
    private static IReadOnlyList<PixelImage> Frames(int count)
    {
        var frames = new List<PixelImage>();
        for (int k = 0; k < count; k++)
        {
            var image = new PixelImage(4096, 1024);
            for (int y = 40; y < image.Height - 40; y++)
                for (int x = 60 + k * 3; x < image.Width - 60; x++)
                    if ((x * 7 + y * 3) % 17 != 0)
                        image[x, y] = new Color32((byte)(x * 13), (byte)(y * 7), (byte)((x ^ y) & 255), (byte)((x + y) % 9 == 0 ? 140 : 255));
            frames.Add(image);
        }
        return frames;
    }

    [Theory]
    [InlineData("shine")]
    [InlineData("coloradjust")]
    [InlineData("outline")]
    [InlineData("appear")]
    [InlineData("shadow")]
    [InlineData("afterimage")]
    public void Official_port_matches_on_a_4096x1024_image_on_the_fast_path(string id)
    {
        var program = EffectParity.Official(id);
        Assert.True(program.ExecutionPath.Compiled, $"{id}: {program.ExecutionPath.Reason}");
        var builtIn = Effects.EffectRegistry.Find("chickxel.effect." + id)!.Create();
        if (builtIn is AfterimageEffect after) builtIn = after with { Count = 2, Tint = true };
        var script = EffectParity.ToScript(builtIn, program);
        var input = new EffectParity.Input("large", Frames(id == "afterimage" ? 3 : 1));
        const int frameCount = 3, frame = 2;
        var clock = Stopwatch.StartNew();
        var want = EffectParity.RenderFrame(input, [builtIn], frame, frameCount);
        var builtInMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var got = EffectParity.RenderFrame(input, [script], frame, frameCount);
        var scriptMs = clock.Elapsed.TotalMilliseconds;
        output.WriteLine($"{id}: C# {builtInMs:0} ms, script {scriptMs:0} ms");
        Assert.Equal((want.OffsetX, want.OffsetY), (got.OffsetX, got.OffsetY));
        Assert.True(want.Image.ContentEquals(got.Image), id);
    }
}
