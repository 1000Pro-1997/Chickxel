using Chickxel.Core.IO;

namespace Chickxel.Core.Tests;

public class LocalPathCipherTests
{
    [Fact]
    public void Protected_path_reads_back_on_the_same_install_even_after_restart()
    {
        using var dir = new TempDir();
        var keyPath = dir.File("local-path-key.bin");
        const string folder = @"C:\Users\someone\Game\Assets\Sprites";

        var sealedValue = new LocalPathCipher(keyPath).Protect(folder);

        Assert.True(LocalPathCipher.IsProtected(sealedValue));
        Assert.DoesNotContain("someone", sealedValue);
        // 다시 켠 앱(새 인스턴스)도 같은 열쇠 파일로 푼다.
        Assert.Equal(folder, new LocalPathCipher(keyPath).Unprotect(sealedValue));
    }

    [Fact]
    public void Another_install_cannot_read_the_path_and_gets_nothing()
    {
        using var dir = new TempDir();
        var sealedValue = new LocalPathCipher(dir.File("mine.bin")).Protect("../sheets");

        // 열쇠가 없는 설치는 열쇠를 만들지 않고 비운다.
        var stranger = dir.File("theirs.bin");
        Assert.Null(new LocalPathCipher(stranger).Unprotect(sealedValue));
        Assert.False(File.Exists(stranger));

        // 다른 열쇠가 있는 설치도 비운다.
        var other = new LocalPathCipher(stranger);
        other.Protect("x");
        Assert.Null(other.Unprotect(sealedValue));
    }

    [Fact]
    public void Same_value_seals_to_the_same_text_so_resaving_does_not_change_the_file()
    {
        using var dir = new TempDir();
        var cipher = new LocalPathCipher(dir.File("key.bin"));

        Assert.Equal(cipher.Protect("out"), cipher.Protect("out"));
        Assert.NotEqual(cipher.Protect("out"), cipher.Protect("out2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_values_stay_empty_and_plain_values_from_older_files_pass_through(string? empty)
    {
        using var dir = new TempDir();
        var cipher = new LocalPathCipher(dir.File("key.bin"));

        Assert.Equal(empty, cipher.Protect(empty));
        Assert.Equal("sheets", cipher.Unprotect("sheets"));
        Assert.Null(cipher.Unprotect(LocalPathCipher.Prefix + "not base64!"));
    }
}
