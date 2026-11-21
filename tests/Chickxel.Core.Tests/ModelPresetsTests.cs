using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;
using Xunit;

namespace Chickxel.Core.Tests;

public class ModelPresetsTests
{
    [Fact]
    public void UserPresetsRoundTripWithoutBuiltInsOrAnimationIndex()
    {
        var render = new ModelRenderSettings
        {
            Width = 40, Height = 24, Yaw = 45, Pitch = 30, Roll = 75, Projection = ModelProjection.Perspective,
            LightYaw = 35, LightPitch = -25, LightBack = true, Colors = 12, HardAlpha = true,
            OutlineOuter = true, OutlineOutside = false, OutlineInner = true, OutlineCrease = 50, OutlineWidth = 2,
            OutlineColorMode = ModelOutlineColor.Darken, OutlineColor = new Color32(10, 20, 30), OutlineDarken = 0.3f,
        };
        var frames = new ModelFrameOptions { Mode = ModelFrameMode.Animation, Animation = 3, Fps = 10, MaxFrames = 20 };
        var json = ModelPresets.Serialize([ModelPresets.BuiltIns[0], new ModelPreset { Name = " mine ", Render = render, Frames = frames }]);
        var back = Assert.Single(ModelPresets.Parse(json));
        Assert.Equal("mine", back.Name);
        Assert.False(back.BuiltIn);
        Assert.Equal(render, back.Render);
        Assert.Equal(frames with { Animation = 0 }, back.Frames);
        Assert.Contains("#0A141E", json);
    }

    [Fact]
    public void BuiltInsHaveDistinctNamesAndParseRejectsNewerFiles()
    {
        var names = ModelPresets.BuiltIns.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(ModelPresets.BuiltIns, p => Assert.True(p.BuiltIn));
        Assert.Throws<InvalidDataException>(() => ModelPresets.Parse("{\"version\":99,\"presets\":[]}"));
        Assert.Throws<InvalidDataException>(() => ModelPresets.Parse("{not json"));
    }
}
