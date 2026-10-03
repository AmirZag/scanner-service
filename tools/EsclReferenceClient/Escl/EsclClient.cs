namespace EsclReferenceClient.Escl;

using System.Net;
using System.Text;
using System.Xml.Linq;

/// <summary>
/// Thin HTTP wrapper over the eSCL REST surface of one scanner:
/// GET ScannerCapabilities / POST ScanJobs / GET job status / GET NextDocument / DELETE job.
/// Protocol facts below were verified against sane-airscan, sane-backends, NAPS2 and go-mfp
/// (the reference open-source eSCL implementations).
/// </summary>
public sealed class EsclClient
{
    public EsclClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<XDocument> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        string xml = await _httpClient.GetStringAsync("eSCL/ScannerCapabilities", cancellationToken);
        return XDocument.Parse(xml);
    }

    public async Task<string> CreateScanJobAsync(XDocument settings, CancellationToken cancellationToken)
    {
        // Reference clients POST the ScanSettings with Content-Type text/xml (sane-airscan,
        // go-mfp and NAPS2 all set exactly that) — not application/xml.
        StringContent content = new StringContent(
            settings.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        HttpResponseMessage response = await _httpClient.PostAsync("eSCL/ScanJobs", content, cancellationToken);
        ThrowIfFailed(response, "create scan job");
        if (response.Headers.Location is null)
        {
            throw new EsclProtocolException("Scan job created (201) but the scanner sent no Location header.");
        }
        return NormalizeJobPath(response.Headers.Location);
    }

    public async Task<(string State, int ImagesRemaining)> GetJobAsync(string jobPath, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.GetAsync(jobPath, cancellationToken);
        ThrowIfFailed(response, "read scan job status");
        XDocument job = XDocument.Load(await response.Content.ReadAsStreamAsync(cancellationToken));
        return ParseJobStatus(job);
    }

    public async Task<byte[]> GetNextDocumentAsync(string jobPath, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.GetAsync(
            jobPath.TrimEnd('/') + "/NextDocument", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        // 404 on NextDocument is the canonical END-OF-JOB signal (go-mfp maps it to EOF, NAPS2
        // treats NotFound as end of scan); 503 means "page still being produced - retry".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new EsclDocumentNotFoundException();
        }
        ThrowIfFailed(response, "fetch scanned document");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteJobAsync(string jobPath, CancellationToken cancellationToken)
    {
        // DELETE is both cancel (while Processing) and cleanup (after completion). It may return
        // 404 when the device already purged the job - that is success for our purposes. Un-deleted
        // jobs linger on shared MFPs and can block other users, so this must always be called.
        HttpResponseMessage response = await _httpClient.DeleteAsync(jobPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }
        ThrowIfFailed(response, "delete scan job");
    }

    // Reference clients (sane-backends escl, sane-airscan) parse the job-status document by
    // LOCAL NAME ONLY — firmwares vary the prefix (pwg:JobState vs scan:JobState). We mirror that.
    private static (string State, int ImagesRemaining) ParseJobStatus(XDocument job)
    {
        XElement? state = job.Descendants().FirstOrDefault(e => e.Name.LocalName == "JobState");
        XElement? remaining = job.Descendants().FirstOrDefault(e => e.Name.LocalName == "ImagesToTransfer");
        if (state is null)
        {
            throw new EsclProtocolException("Job status document contains no JobState element.");
        }
        return (state.Value.Trim(), ParseCount(remaining));
    }

    private static int ParseCount(XElement? element)
    {
        return element is null || !int.TryParse(element.Value.Trim(), out int count) ? 0 : count;
    }

    private static string NormalizeJobPath(Uri location)
    {
        // The hostname inside an absolute Location CANNOT be trusted: real HP Deskjets return a
        // serial-number hostname, Xerox models a malformed IPv6 (sane-airscan/go-mfp both re-host
        // or strip to path). Keep only path+query; the HttpClient re-hosts it on the base address.
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    private static void ThrowIfFailed(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        throw new EsclProtocolException($"eSCL call '{action}' failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private readonly HttpClient _httpClient;
}
