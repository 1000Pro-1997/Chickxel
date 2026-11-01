using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

internal static class TestUtil
{
    /// <summary>출력 폴더에 복사된 Unity 참조 파일 폴더.</summary>
    public static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string Fixture(string name) => Path.Combine(FixturesDir, name);

    /// <summary>테스트용 무작위(고정 시드) RGBA 이미지. <paramref name="opaque"/>면 알파 255.</summary>
    public static PixelImage RandomImage(int w, int h, int seed = 1, bool opaque = false)
    {
        var rng = new Random(seed);
        var img = new PixelImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var c = new Color32((uint)rng.NextInt64(0, 1L << 32));
            img.Pixels[i] = opaque ? c.WithAlpha(255) : c;
        }
        return img;
    }

    /// <summary>예전 버전이 PNG 옆에 쓰던 사이드카(<c>.png.gpixel.json</c>)를 흉내 내어 쓴다(가져오기 호환 시험용).</summary>
    public static void WriteLegacySidecar(Chickxel.Core.Model.Document doc, string pngPath)
    {
        var dto = new
        {
            version = 1,
            spriteMode = doc.Sprite.Mode.ToString(),
            pixelsPerUnit = doc.Sprite.PixelsPerUnit,
            defaultPivot = new { x = doc.Sprite.DefaultPivot.X, y = doc.Sprite.DefaultPivot.Y },
            slices = doc.Slices.Select(s => new
            {
                name = s.Name, x = s.Rect.X, y = s.Rect.Y, w = s.Rect.Width, h = s.Rect.Height,
                pivot = new { x = s.Pivot.X, y = s.Pivot.Y },
                border = new[] { s.Border.Left, s.Border.Top, s.Border.Right, s.Border.Bottom },
            }),
            animations = doc.Animations.Select(a => new
            {
                name = a.Name, loop = a.Loop, fps = a.Fps,
                frames = a.Frames.Select(f => new { slice = f.Slice, ms = f.Ms }),
            }),
        };
        System.IO.File.WriteAllText(Chickxel.Core.IO.SidecarIO.GetPath(pngPath), System.Text.Json.JsonSerializer.Serialize(dto));
    }

    /// <summary>효과들의 저장 JSON(그라데이션 사전은 참조로 비교되므로 글로 비교한다).</summary>
    public static string[] EffectJson(IEnumerable<LayerEffect> effects)
        => [.. effects.Select(e => Chickxel.Core.Effects.EffectCodec.Write(e).ToJsonString())];

    /// <summary>
    /// 앱 길(프로젝트·레시피·패키지 읽기)로 읽은 뒤의 저장 JSON: 예전 기본 효과는 같은 그림의 공식 스크립트 효과로 바뀐다
    /// (<see cref="Chickxel.Core.Effects.LegacyEffects.Convert(LayerEffect)"/>).
    /// </summary>
    public static string[] ConvertedJson(IEnumerable<LayerEffect> effects)
        => EffectJson(effects.Select(e => Chickxel.Core.Effects.LegacyEffects.Convert(e)));
}

/// <summary>테스트마다 쓰고 지우는 임시 폴더.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chickxel-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
