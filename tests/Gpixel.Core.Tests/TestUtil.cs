using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

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
}

/// <summary>테스트마다 쓰고 지우는 임시 폴더.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gpixel-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
