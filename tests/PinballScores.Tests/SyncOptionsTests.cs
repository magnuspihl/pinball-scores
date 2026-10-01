using PinballScores.Core;
using Xunit;

namespace PinballScores.Tests;

public class SyncOptionsTests
{
    [Theory]
    [InlineData("http://scores.example.lan/api")]
    [InlineData("https://scores.example.test/api")]
    public void AcceptsAnHttpOrHttpsApi(string url) =>
        Assert.Empty(new SyncOptions { NvramPath = "nvram", ApiBaseUrl = url }.Validate());

    [Theory]
    [InlineData("")]
    [InlineData("scores.example.lan/api")]
    [InlineData("ftp://scores.example.lan/api")]
    [InlineData("file:///C:/scores")]
    public void RejectsAMissingOrNonHttpApi(string url) =>
        Assert.Single(new SyncOptions { NvramPath = "nvram", ApiBaseUrl = url }.Validate());

    [Theory]
    [InlineData("https://foundryappsstaginggreg6bwp-pinball.functions.fnc.fr-par.scw.cloud/api", true)]
    [InlineData("http://scores.example.lan/api", false)]
    [InlineData("", false)]
    public void RecognisesTheRetiredFoundryApi(string url, bool retired) =>
        Assert.Equal(retired, new SyncOptions { ApiBaseUrl = url }.UsesRetiredFoundryApi);
}
