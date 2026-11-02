using Chickxel.Core.Assets;

namespace Chickxel.Core.Tests;

/// <summary>홈페이지 "Chickxel에서 열기" 주소(<c>chickxel://asset/ID</c>): 받을 모양과 거절할 모양.</summary>
public class AssetLinksTests
{
    [Theory]
    [InlineData("chickxel://asset/chickxel.teleport", "chickxel.teleport")]
    // 브라우저·OS가 붙이는 끝 '/', 대문자 scheme, query·fragment
    [InlineData("chickxel://asset/chickxel.teleport/", "chickxel.teleport")]
    [InlineData("CHICKXEL://Asset/pixel-cat.neon-pack", "pixel-cat.neon-pack")]
    [InlineData("chickxel://asset/pixel-cat.neon?from=web#x", "pixel-cat.neon")]
    [InlineData(" chickxel://asset/pixel-cat.neon ", "pixel-cat.neon")]
    public void PackageId_ReadsTheIdFromAnAssetLink(string text, string id)
    {
        Assert.Equal(id, AssetLinks.PackageId(text));
        Assert.Equal(id, AssetLinks.PackageId(AssetLinks.Url(id)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://chickxel.com/packages/chickxel.teleport")]
    [InlineData("chickxel://art/chickxel.teleport")]
    [InlineData("chickxel://asset/")]
    // ID 형식이 아님: 조각 하나, 대문자, 경로 넘기기, 조각이 더 있음
    [InlineData("chickxel://asset/teleport")]
    [InlineData("chickxel://asset/Chickxel.Teleport")]
    [InlineData("chickxel://asset/..%2F..%2Fetc.passwd")]
    [InlineData("chickxel://asset/chickxel.teleport/extra")]
    [InlineData(@"C:\Users\me\Downloads\chickxel.teleport.chickxel-asset")]
    public void PackageId_RejectsOtherTexts(string? text) => Assert.Null(AssetLinks.PackageId(text));
}
