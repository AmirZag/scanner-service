namespace EsclReferenceClient.Discovery;

using EsclReferenceClient.Escl;
using Microsoft.Extensions.Configuration;
using Zeroconf;

public sealed record ScannerInfo(string Name, string Url, bool Discovered);

/// <summary>
/// Merges two sources into one scanner list: the statically configured scanner (Scanner:BaseUrl in
/// appsettings.json — the reliable path that works across subnets and firewalled multicast) and
/// live mDNS discovery (_uscan._tcp). Configured entries are listed first.
/// </summary>
public sealed class ScannerDirectory
{
    public ScannerDirectory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<ScannerInfo>> ListAsync(CancellationToken cancellationToken)
    {
        List<ScannerInfo> scanners = new List<ScannerInfo>();
        AddConfiguredScanner(scanners);

        // _uscan._tcp = eSCL over plain HTTP; _uscans._tcp = eSCL over TLS (this reference agent
        // speaks plain HTTP, so TLS advertisements are skipped).
        IReadOnlyList<IZeroconfHost> hosts = await ZeroconfResolver.ResolveAsync(
            new[] { "_uscan._tcp.local." }, TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);
        foreach (IZeroconfHost host in hosts)
        {
            scanners.Add(ToScannerInfo(host));
        }
        return scanners;
    }

    private ScannerInfo ToScannerInfo(IZeroconfHost host)
    {
        // In the Zeroconf package the per-service interface is named IService (not IZeroconfService).
        IService service = host.Services.First(s => s.Key.EndsWith("_uscan._tcp.local.")).Value;
        string url = $"http://{host.IPAddress}:{service.Port}/{ExtractRootPath(service)}";
        return new ScannerInfo(host.DisplayName, url, true);
    }

    private void AddConfiguredScanner(List<ScannerInfo> scanners)
    {
        string? baseUrl = _configuration["Scanner:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return;
        }
        string name = _configuration["Scanner:Name"] ?? "Configured scanner";
        scanners.Add(new ScannerInfo(name, UrlTool.WithTrailingSlash(baseUrl), false));
    }

    // TXT record key "rs" carries the eSCL resource path (usually "eSCL") — take it verbatim when
    // present, it is not guaranteed to be "eSCL"; sane-airscan defaults to that when absent.
    private static string ExtractRootPath(IService service)
    {
        foreach (IReadOnlyDictionary<string, string> recordSet in service.Properties)
        {
            if (recordSet.TryGetValue("rs", out string? rs))
            {
                return rs.Trim('/') + "/";
            }
        }
        return "eSCL/";
    }

    private readonly IConfiguration _configuration;
}
