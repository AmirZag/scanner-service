using System;
using System.Net;
using ScannerService.TrayApp;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.EndToEnd;

[Collection("BinState")]
public class WebApiHostServiceParseApiHostTests : IDisposable
{
    public WebApiHostServiceParseApiHostTests()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    public void Dispose()
    {
        EndToEndBinState.CleanSharedBinStateFiles();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("scanner.example")]
    public void ParseApiHost_NullEmptyWhitespaceOrUnknownText_FallsBackToLoopback(string? apiHost)
    {
        WebApiHostService.ApiBindTarget target = WebApiHostService.ParseApiHost(apiHost);

        Assert.Equal(WebApiHostService.ApiBindKind.Loopback, target.Kind);
        Assert.Null(target.Address);
        Assert.Equal("localhost", target.UrlHost);
        Assert.Equal(IPAddress.Loopback, target.ProbeAddress);
        Assert.False(target.BindsBeyondLoopback);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("loopback")]
    public void ParseApiHost_LocalhostAndLoopbackTokens_MapToLoopbackBind(string apiHost)
    {
        WebApiHostService.ApiBindTarget target = WebApiHostService.ParseApiHost(apiHost);

        Assert.Equal(WebApiHostService.ApiBindKind.Loopback, target.Kind);
        Assert.Null(target.Address);
        Assert.Equal("localhost", target.UrlHost);
        Assert.Equal(IPAddress.Loopback, target.ProbeAddress);
        Assert.False(target.BindsBeyondLoopback);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void ParseApiHost_WildcardTokens_MapToAnyIpBind(string apiHost)
    {
        WebApiHostService.ApiBindTarget target = WebApiHostService.ParseApiHost(apiHost);

        Assert.Equal(WebApiHostService.ApiBindKind.AnyIp, target.Kind);
        Assert.Null(target.Address);
        Assert.Equal("localhost", target.UrlHost);
        Assert.Equal(IPAddress.Any, target.ProbeAddress);
        Assert.True(target.BindsBeyondLoopback);
    }

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50", true)]
    [InlineData("127.0.0.1", "127.0.0.1", false)]
    [InlineData("::1", "[::1]", false)]
    public void ParseApiHost_CanonicalIpLiteral_MapsToSpecificAddressBind(string apiHost, string expectedUrlHost, bool expectedBindsBeyondLoopback)
    {
        WebApiHostService.ApiBindTarget target = WebApiHostService.ParseApiHost(apiHost);

        Assert.Equal(WebApiHostService.ApiBindKind.SpecificAddress, target.Kind);
        Assert.NotNull(target.Address);
        Assert.Equal(IPAddress.Parse(apiHost), target.Address);
        Assert.Equal(expectedUrlHost, target.UrlHost);
        Assert.Equal(IPAddress.Parse(apiHost), target.ProbeAddress);
        Assert.Equal(expectedBindsBeyondLoopback, target.BindsBeyondLoopback);
    }

    [Theory]
    [InlineData("127.0.0")]
    [InlineData("0")]
    public void ParseApiHost_AbbreviatedIpv4Text_FallsBackToLoopback(string apiHost)
    {
        // IPAddress.TryParse accepts abbreviated IPv4 forms (they re-expand to a different address,
        // e.g. "127.0.0" -> 127.0.0.0), so the parser's exact-text round-trip check rejects them
        // instead of silently binding a different address than the one typed.
        WebApiHostService.ApiBindTarget target = WebApiHostService.ParseApiHost(apiHost);

        Assert.Equal(WebApiHostService.ApiBindKind.Loopback, target.Kind);
        Assert.Null(target.Address);
        Assert.Equal("localhost", target.UrlHost);
        Assert.False(target.BindsBeyondLoopback);
    }

    [Fact]
    public void ParseApiHost_BracketedIpv6Text_FallsBackToLoopback()
    {
        // IPAddress.TryParse rejects bracketed text outright, and even if it parsed, ToString()
        // would drop the brackets so the exact-text round-trip check would still reject the value.
        WebApiHostService.ApiBindTarget target = WebApiHostService.ParseApiHost("[::1]");

        Assert.Equal(WebApiHostService.ApiBindKind.Loopback, target.Kind);
        Assert.Null(target.Address);
        Assert.Equal("localhost", target.UrlHost);
        Assert.False(target.BindsBeyondLoopback);
    }

    [Fact]
    public void Constructor_WithLoopbackConfiguration_InitializesPortsAndLocalUrlHost()
    {
        var configuration = new ScannerServiceConfiguration { ApiPort = 58472, ApiHost = "localhost" };
        var host = new WebApiHostService(configuration, new LocalSettingsStore(configuration));
        try
        {
            Assert.False(host.IsRunning);
            Assert.Equal(58472, host.ActualPort);
            Assert.Equal(58472, host.ConfiguredPort);
            Assert.Equal("localhost", host.LocalUrlHost);
        }
        finally
        {
            host.Dispose();
        }
    }
}
