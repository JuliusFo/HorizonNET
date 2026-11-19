using HorizonNET.Shared.Transfer.Update;
using HorizonNET.Updater;

namespace HorizonNET.Server.Tests.Updater;

public class UpdatePackageTests
{
    [Theory]
    [InlineData("v1.3.0", true)]
    [InlineData("v10.20.300", true)]
    [InlineData("1.3.0", false)]
    [InlineData("v1.3", false)]
    [InlineData("v1.3.0-beta", false)]
    [InlineData("v1.3.0/../x", false)]
    [InlineData(null, false)]
    public void TryParseTag_AcceptsOnlyPlainSemVer(string? tag, bool valid) =>
        Assert.Equal(valid, UpdatePackage.TryParseTag(tag, out _));

    [Fact]
    public void TryParseInformational_IgnoresCommitSuffix()
    {
        Assert.True(UpdatePackage.TryParseInformational("1.4.2+6c70e21", out var version));
        Assert.Equal(new Version(1, 4, 2), version);
    }

    [Fact]
    public void Names_AndDownloadUri_FollowReleaseWorkflow()
    {
        var version = new Version(1, 4, 0);
        Assert.Equal("HorizonNET-1.4.0-win-x64.zip", UpdatePackage.ZipName(version));
        Assert.Equal("HorizonNET-1.4.0-win-x64.zip.sha256", UpdatePackage.ChecksumName(version));
        Assert.Equal("https://github.com/JuliusFo/HorizonNET/releases/download/v1.4.0/HorizonNET-1.4.0-win-x64.zip",
            UpdatePackage.DownloadUri(version, UpdatePackage.ZipName(version)).AbsoluteUri);
    }

    [Fact]
    public void Evaluate_CompleteRelease_IsCandidate()
    {
        var candidate = UpdatePackage.Evaluate(Release("v1.4.0"));

        Assert.NotNull(candidate);
        Assert.Equal(new Version(1, 4, 0), candidate.Version);
        Assert.Equal("Notes", candidate.Notes);
    }

    [Fact]
    public void Evaluate_IgnoresDraftsPrereleasesAndOddTags()
    {
        var draft = Release("v1.4.0");
        draft.Draft = true;
        var pre = Release("v1.4.0");
        pre.Prerelease = true;

        Assert.Null(UpdatePackage.Evaluate(draft));
        Assert.Null(UpdatePackage.Evaluate(pre));
        Assert.Null(UpdatePackage.Evaluate(Release("release-1.4.0")));
        Assert.Null(UpdatePackage.Evaluate(null));
    }

    [Fact]
    public void Evaluate_WithoutChecksumOrWhileUploading_IsNoCandidate()
    {
        var noChecksum = Release("v1.4.0");
        noChecksum.Assets!.RemoveAt(1);
        var uploading = Release("v1.4.0");
        uploading.Assets![0].State = "starter";
        var wrongName = Release("v1.4.0");
        wrongName.Assets![0].Name = "HorizonNET-1.3.0-win-x64.zip";

        Assert.Null(UpdatePackage.Evaluate(noChecksum));
        Assert.Null(UpdatePackage.Evaluate(uploading));
        Assert.Null(UpdatePackage.Evaluate(wrongName));
    }

    [Theory]
    [InlineData("http://localhost:5000", "http://localhost:5000/")]
    [InlineData("http://+:5080", "http://localhost:5080/")]
    [InlineData("https://localhost:5001;http://*:5002", "http://localhost:5002/")]
    [InlineData("http://0.0.0.0:6000/", "http://localhost:6000/")]
    [InlineData(null, "http://localhost:5000/")]
    [InlineData("https://localhost:5001", "http://localhost:5000/")]
    public void ServiceUrls_MapToLocalHttp(string? urls, string expected) =>
        Assert.Equal(expected, ServiceUrls.ToLocalBaseUri(urls).AbsoluteUri);

    private static GitHubRelease Release(string tag)
    {
        var version = tag.TrimStart('v');
        return new GitHubRelease
        {
            TagName = tag,
            Body = "Notes",
            Assets =
            [
                new GitHubAsset { Name = $"HorizonNET-{version}-win-x64.zip", Size = 100, State = "uploaded" },
                new GitHubAsset { Name = $"HorizonNET-{version}-win-x64.zip.sha256", Size = 1, State = "uploaded" },
            ],
        };
    }
}
