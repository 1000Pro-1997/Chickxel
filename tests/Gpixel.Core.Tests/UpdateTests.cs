using System.Runtime.InteropServices;
using Gpixel.Core.IO;

namespace Gpixel.Core.Tests;

public class UpdateTests
{
    private static SemVersion V(string s) => SemVersion.TryParse(s, out var v) ? v : throw new ArgumentException(s);

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData(" V10.0.1 ", 10, 0, 1, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1.0.0+abc123def", 1, 0, 0, "")]
    [InlineData("v2.0.0-beta.1+build.7", 2, 0, 0, "beta.1")]
    [InlineData("0.0.0-ci.42", 0, 0, 0, "ci.42")]
    public void TryParse_ReadsTagsAndInformationalVersions(string text, int major, int minor, int patch, string pre)
    {
        Assert.True(SemVersion.TryParse(text, out var v));
        Assert.Equal(new SemVersion(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.-2.3")]
    [InlineData("1..3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3-be_ta")]
    [InlineData("release-1.2.3")]
    public void TryParse_RejectsNonVersions(string? text) => Assert.False(SemVersion.TryParse(text, out _));

    [Theory]
    // SemVer 2.0 명세의 우선순위 예: 1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta < 1.0.0-beta.2 < 1.0.0-beta.11 < 1.0.0-rc.1 < 1.0.0
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.9.9", "1.10.0")]
    [InlineData("1.2.3", "1.2.10")]
    [InlineData("1.99.99", "2.0.0-alpha")]
    [InlineData("0.0.0-ci.9", "0.0.0-ci.10")]
    public void Compare_FollowsSemVerPrecedence(string lower, string higher)
    {
        Assert.True(V(lower) < V(higher));
        Assert.True(V(higher) > V(lower));
        Assert.Equal(-1, V(lower).CompareTo(V(higher)));
    }

    [Fact]
    public void Compare_IgnoresBuildMetadataAndTagPrefix()
    {
        Assert.Equal(0, V("v1.2.3").CompareTo(V("1.2.3+abc")));
        Assert.Equal("1.2.3", V("1.2.3+abc").ToString());
        Assert.Equal("2.0.0-rc.1", V("v2.0.0-rc.1").ToString());
    }

    private const string LatestJson = """
        {
          "html_url": "https://github.com/1000Pro-1997/GPixel/releases/tag/v1.3.0",
          "tag_name": "v1.3.0",
          "name": "Gpixel v1.3.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-10-01T12:34:56Z",
          "body": "## 바뀐 점\r\n- 자동 업데이트",
          "assets": [
            { "name": "Gpixel-macos-arm64.zip", "size": 52000000,
              "browser_download_url": "https://github.com/1000Pro-1997/GPixel/releases/download/v1.3.0/Gpixel-macos-arm64.zip" },
            { "name": "Gpixel-macos-x64.zip", "size": 53000000,
              "browser_download_url": "https://github.com/1000Pro-1997/GPixel/releases/download/v1.3.0/Gpixel-macos-x64.zip" },
            { "name": "Gpixel-windows-x64.zip", "size": 48000000,
              "browser_download_url": "https://github.com/1000Pro-1997/GPixel/releases/download/v1.3.0/Gpixel-windows-x64.zip" },
            { "name": "SHA256SUMS.txt", "size": 300,
              "browser_download_url": "https://github.com/1000Pro-1997/GPixel/releases/download/v1.3.0/SHA256SUMS.txt" },
            { "name": "evil.zip", "size": 1, "browser_download_url": "http://example.com/evil.zip" },
            { "name": "", "size": 1, "browser_download_url": "https://example.com/noname.zip" }
          ]
        }
        """;

    [Fact]
    public void ParseRelease_ReadsLatestReleaseJson()
    {
        var r = AppUpdate.ParseRelease(LatestJson)!;
        Assert.Equal("v1.3.0", r.Tag);
        Assert.Equal(new SemVersion(1, 3, 0), r.Version);
        Assert.Equal("Gpixel v1.3.0", r.Name);
        Assert.Equal("## 바뀐 점\n- 자동 업데이트", r.Notes);
        Assert.Equal("https://github.com/1000Pro-1997/GPixel/releases/tag/v1.3.0", r.PageUrl.ToString());
        Assert.False(r.IsDraft);
        Assert.False(r.IsPrerelease);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 34, 56, TimeSpan.Zero), r.PublishedAt);
        // http 주소·이름 없는 파일은 버린다.
        Assert.Equal(["Gpixel-macos-arm64.zip", "Gpixel-macos-x64.zip", "Gpixel-windows-x64.zip", "SHA256SUMS.txt"], r.Assets.Select(a => a.Name));
        Assert.Equal(48000000, r.Asset("gpixel-windows-x64.zip")!.Size);
    }

    [Fact]
    public void ParseRelease_PrereleaseFromFlagOrTag_DraftKept()
    {
        var flagged = AppUpdate.ParseRelease(LatestJson.Replace("\"prerelease\": false", "\"prerelease\": true"))!;
        Assert.True(flagged.IsPrerelease);
        var tagged = AppUpdate.ParseRelease(LatestJson.Replace("\"tag_name\": \"v1.3.0\"", "\"tag_name\": \"v1.3.0-beta.2\""))!;
        Assert.True(tagged.IsPrerelease);
        Assert.Equal(new SemVersion(1, 3, 0, "beta.2"), tagged.Version);
        var draft = AppUpdate.ParseRelease(LatestJson.Replace("\"draft\": false", "\"draft\": true"))!;
        Assert.True(draft.IsDraft);
    }

    [Fact]
    public void ParseRelease_NullForNonVersionTagOrMissingPage_ThrowsForNonJson()
    {
        Assert.Null(AppUpdate.ParseRelease(LatestJson.Replace("\"tag_name\": \"v1.3.0\"", "\"tag_name\": \"nightly\"")));
        Assert.Null(AppUpdate.ParseRelease("""{ "tag_name": "v1.0.0" }"""));
        Assert.Throws<FormatException>(() => AppUpdate.ParseRelease("<html>rate limited</html>"));
        Assert.Throws<FormatException>(() => AppUpdate.ParseRelease("[]"));
    }

    private static ReleaseInfo Release(string tag, bool prerelease = false, bool draft = false)
        => new(tag, V(tag), tag, "", new Uri("https://github.com/x/y/releases/tag/" + tag), prerelease || V(tag).IsPrerelease, draft, null, []);

    [Fact]
    public void IsNewer_IgnoresDraftsAndOlder_PrereleaseOnlyForPrereleaseUsers()
    {
        var stable = V("1.2.0");
        Assert.True(AppUpdate.IsNewer(stable, Release("v1.3.0")));
        Assert.False(AppUpdate.IsNewer(stable, Release("v1.2.0")));
        Assert.False(AppUpdate.IsNewer(stable, Release("v1.1.9")));
        Assert.False(AppUpdate.IsNewer(stable, Release("v1.3.0", draft: true)));
        Assert.False(AppUpdate.IsNewer(stable, Release("v1.3.0", prerelease: true)));
        Assert.False(AppUpdate.IsNewer(stable, Release("v1.3.0-rc.1")));
        Assert.False(AppUpdate.IsNewer(stable, null));

        var beta = V("1.3.0-beta.1");
        Assert.True(AppUpdate.IsNewer(beta, Release("v1.3.0-rc.1")));
        Assert.True(AppUpdate.IsNewer(beta, Release("v1.3.0")));
        Assert.False(AppUpdate.IsNewer(beta, Release("v1.3.0-alpha")));
    }

    [Theory]
    [InlineData("https://github.com/1000Pro-1997/GPixel/releases/tag/v1.2.3", "1.2.3", false)]
    [InlineData("https://github.com/1000Pro-1997/GPixel/releases/tag/v2.0.0-beta.1", "2.0.0-beta.1", true)]
    public void LatestRedirect_TagPage_GivesVersionAndDownloadUrls(string location, string version, bool pre)
    {
        var r = AppUpdate.ReleaseFromLatestRedirect(new Uri(location))!;
        Assert.Equal(version, r.Version.ToString());
        Assert.Equal(pre, r.IsPrerelease);
        Assert.Equal(new Uri(location), r.PageUrl);
        Assert.Equal($"https://github.com/1000Pro-1997/GPixel/releases/download/{r.Tag}/Gpixel-macos-arm64.zip",
            r.Asset("Gpixel-macos-arm64.zip")!.DownloadUrl.AbsoluteUri);
        Assert.NotNull(r.Asset(AppUpdate.ChecksumsAssetName));
    }

    [Theory]
    [InlineData("https://github.com/1000Pro-1997/GPixel/releases")]          // 릴리스 없음
    [InlineData("https://github.com/1000Pro-1997/GPixel/releases/tag/nightly")] // 버전 아닌 태그
    [InlineData("http://github.com/1000Pro-1997/GPixel/releases/tag/v1.0.0")]   // https 아님
    public void LatestRedirect_NotAReleaseTag_IsNull(string location)
        => Assert.Null(AppUpdate.ReleaseFromLatestRedirect(new Uri(location)));

    [Theory]
    [InlineData(UpdateOs.Windows, Architecture.X64, "Gpixel-windows-x64.zip")]
    [InlineData(UpdateOs.Windows, Architecture.Arm64, "Gpixel-windows-x64.zip")]
    [InlineData(UpdateOs.MacOS, Architecture.Arm64, "Gpixel-macos-arm64.zip")]
    [InlineData(UpdateOs.MacOS, Architecture.X64, "Gpixel-macos-x64.zip")]
    [InlineData(UpdateOs.Windows, Architecture.X86, null)]
    [InlineData(UpdateOs.Other, Architecture.X64, null)]
    public void SelectAsset_PicksZipForOsAndArch(UpdateOs os, Architecture arch, string? expected)
    {
        var r = AppUpdate.ParseRelease(LatestJson)!;
        Assert.Equal(expected, AppUpdate.SelectAsset(r, os, arch)?.Name);
    }

    [Fact]
    public void SelectAsset_NullWhenReleaseLacksPlatformZip()
    {
        var r = AppUpdate.ParseRelease(LatestJson)! with { Assets = [] };
        Assert.Null(AppUpdate.SelectAsset(r, UpdateOs.MacOS, Architecture.Arm64));
    }

    [Fact]
    public void ParseChecksums_ReadsShasumOutput()
    {
        const string a = "3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b";
        const string b = "B5BB9D8014A0F9B1D61E21E796D78DCCDF1352F23CD32812F4850B878AE4944C";
        var text = $"{a}  Gpixel-macos-arm64.zip\r\n"
            + $"{b} *Gpixel-windows-x64.zip\n"
            + "# comment\n"
            + "not a checksum line\n"
            + $"{a[..63]}  short.zip\n"
            + $"{a}  ./Gpixel-macos-x64.zip\n"
            + $"{b}  Gpixel-macos-arm64.zip\n"
            + "\n";
        var map = AppUpdate.ParseChecksums(text);
        Assert.Equal(3, map.Count);
        Assert.Equal(a, map["Gpixel-macos-arm64.zip"]);
        Assert.Equal(b.ToLowerInvariant(), map["gpixel-windows-x64.zip"]);
        Assert.Equal(a, map["Gpixel-macos-x64.zip"]);
    }

    private const string Changelog = """
        # 바뀐 점

        설명 문단.

        ## v1.13.0 (2026-10-21)
        - 홀로그램

        ## v1.12.0 (2026-10-21)
        - 실행취소 기록

        ## v1.10.x (2026-10-20)
        - 저장 단추

        ## v1.0.0 ~ v1.5.x (2026-10-20)
        - 첫 공개
        """;

    [Fact]
    public void Changelog_Between_PicksSectionsAboveCurrentUpToLatest_NewestFirst()
    {
        var notes = ChangelogNotes.Between(Changelog, new SemVersion(1, 11, 0), new SemVersion(1, 13, 0));
        Assert.Equal("## v1.13.0 (2026-10-21)\n- 홀로그램\n\n## v1.12.0 (2026-10-21)\n- 실행취소 기록", notes);
        Assert.DoesNotContain("설명 문단", notes);
    }

    [Fact]
    public void Changelog_RangeHeaders_CountAsTheirHighestVersion()
    {
        Assert.Contains("저장 단추", ChangelogNotes.Between(Changelog, new SemVersion(1, 10, 3), new SemVersion(1, 12, 0)));
        Assert.Equal(new SemVersion(1, 5, int.MaxValue), ChangelogNotes.HeaderVersion("v1.0.0 ~ v1.5.x (2026-10-20)"));
        Assert.Equal("", ChangelogNotes.Between(Changelog, new SemVersion(1, 13, 0), new SemVersion(1, 13, 0)));
        Assert.Equal("", ChangelogNotes.Between(null, new SemVersion(1, 0, 0), new SemVersion(2, 0, 0)));
    }

    private const string EnglishChangelog = """
        # Changes

        Intro.

        ## 미배포
        ### Unreleased
        - Not yet

        ## v1.13.0 (2026-10-21)
        ### New **Hologram** effect
        - Tint and scanlines.
        - Glitch.

        ### Faster saving
        - Saves in the background.
        """;

    [Fact]
    public void Changelog_SubheadingsStayInsideTheirVersionSection_MarkdownKept()
    {
        var notes = ChangelogNotes.Between(EnglishChangelog, new SemVersion(1, 12, 0), new SemVersion(1, 13, 0));
        Assert.Equal("## v1.13.0 (2026-10-21)\n### New **Hologram** effect\n- Tint and scanlines.\n- Glitch.\n\n### Faster saving\n- Saves in the background.", notes);
        Assert.Equal("", ChangelogNotes.Between(EnglishChangelog, new SemVersion(1, 13, 0), new SemVersion(2, 0, 0)));
    }

    [Fact]
    public void Changelog_Between_WithFallback_UsesPreferredPerVersion_ElseFallback_NewestFirst()
    {
        var notes = ChangelogNotes.Between(EnglishChangelog, new SemVersion(1, 10, 3), new SemVersion(1, 13, 0), Changelog);
        Assert.StartsWith("## v1.13.0 (2026-10-21)\n### New **Hologram** effect\n", notes);
        Assert.EndsWith("- Saves in the background.\n\n## v1.12.0 (2026-10-21)\n- 실행취소 기록\n\n## v1.10.x (2026-10-20)\n- 저장 단추", notes);
        Assert.DoesNotContain("홀로그램", notes);
        Assert.DoesNotContain("Unreleased", notes);
    }

    [Fact]
    public void Changelog_Between_WithFallback_MissingPreferredFile_UsesFallbackOnly()
    {
        Assert.Equal(ChangelogNotes.Between(Changelog, new SemVersion(1, 11, 0), new SemVersion(1, 13, 0)),
            ChangelogNotes.Between(null, new SemVersion(1, 11, 0), new SemVersion(1, 13, 0), Changelog));
        Assert.Equal(ChangelogNotes.Between(EnglishChangelog, new SemVersion(1, 12, 0), new SemVersion(1, 13, 0)),
            ChangelogNotes.Between(EnglishChangelog, new SemVersion(1, 12, 0), new SemVersion(1, 13, 0), null));
    }

    [Fact]
    public void Changelog_AssetNames_EnglishIsMainFile_KoreanIsKoFile()
    {
        // 릴리스 워크플로가 올리는 파일 이름과 같아야 한다(.github/workflows/release.yml).
        Assert.Equal("CHANGELOG.md", ChangelogNotes.EnglishAssetName);
        Assert.Equal("CHANGELOG.ko.md", ChangelogNotes.KoreanAssetName);
    }
}
