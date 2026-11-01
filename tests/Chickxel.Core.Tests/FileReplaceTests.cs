using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class FileReplaceTests
{
    private static byte[] Png(byte red)
    {
        var img = new PixelImage(2, 2);
        img.Clear(new Color32(red, 0, 0, 255));
        using var ms = new MemoryStream();
        PngIO.Save(img, ms);
        return ms.ToArray();
    }

    [Fact]
    public void Overwrites_a_file_another_program_holds_open_with_shared_write()
    {
        using var dir = new TempDir();
        string path = dir.File("frame.png");
        File.WriteAllBytes(path, Png(10));
        // 탐색기 미리보기·동기화 앱처럼 쓰기·지우기를 함께 허용하고 열어 둔 파일(Windows에서는 이름 바꾸기 덮어쓰기가 막힘)
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var img = new PixelImage(2, 2);
            img.Clear(new Color32(200, 0, 0, 255));
            PngIO.Save(img, path);
        }
        Assert.Equal(200, PngIO.Load(path)[0, 0].R);
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Read_only_and_locked_targets_fail_with_a_reason_and_keep_the_old_file()
    {
        if (!OperatingSystem.IsWindows()) return; // 읽기 전용 속성·열린 파일이 덮어쓰기를 막는 것은 Windows뿐
        using var dir = new TempDir();
        string path = dir.File("frame.png");
        var old = Png(10);
        File.WriteAllBytes(path, old);
        var img = new PixelImage(2, 2);

        File.SetAttributes(path, FileAttributes.ReadOnly);
        var readOnly = Assert.Throws<UnauthorizedAccessException>(() => PngIO.Save(img, path));
        Assert.Contains("frame.png", readOnly.Message, StringComparison.Ordinal);
        File.SetAttributes(path, FileAttributes.Normal);

        // 쓰기를 함께 허용하지 않고 연 파일(일부 이미지 뷰어)
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var inUse = Assert.Throws<UnauthorizedAccessException>(() => PngIO.Save(img, path));
            Assert.NotEqual(readOnly.Message, inUse.Message);
        }
        Assert.Equal(old, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }
}
