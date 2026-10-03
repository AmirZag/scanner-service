namespace EsclReferenceClient.Escl;

using System.Net.Http;
using System.Xml.Linq;

public sealed record ScanPage(string ContentType, byte[] Data);

public sealed record ScanResult(int PageCount, IReadOnlyList<ScanPage> Pages);

/// <summary>
/// Drives one scan job end to end: POST ScanJobs, collect documents, always DELETE the job.
/// The loop is uniform for ADF-multipage (one fetch per page) and PDF (whole stack in one
/// fetch): ImagesToTransfer is treated as a fetch HINT only — never a termination condition —
/// because with format=application/pdf many devices report the page count but return the
/// entire stack from the first NextDocument, and some models (Xerox) use 404 for "not ready".
/// Termination = JobState Completed AND a final 404 (or a fetched page with nothing left).
/// </summary>
public sealed class ScanOrchestrator
{
    public ScanOrchestrator(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ScanResult> ScanAsync(string scannerUrl, XDocument settings, CancellationToken cancellationToken)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient("escl");
        httpClient.BaseAddress = new Uri(UrlTool.WithTrailingSlash(scannerUrl));
        httpClient.Timeout = Timeout.InfiniteTimeSpan; // long ADF jobs; the per-request token governs instead
        EsclClient client = new EsclClient(httpClient);
        string jobPath = await client.CreateScanJobAsync(settings, cancellationToken);
        try
        {
            List<ScanPage> pages = await CollectPagesAsync(client, jobPath, cancellationToken);
            return new ScanResult(pages.Count, pages);
        }
        finally
        {
            // ALWAYS delete the job — even on cancellation/abort — so the shared MFP's job queue
            // never wedges for other users. Token-free: cleanup must survive a client disconnect.
            await client.DeleteJobAsync(jobPath, CancellationToken.None);
        }
    }

    private async Task<List<ScanPage>> CollectPagesAsync(EsclClient client, string jobPath, CancellationToken cancellationToken)
    {
        List<ScanPage> pages = new List<ScanPage>();
        for (int poll = 0; poll < MaxPolls; poll++)
        {
            PollOutcome outcome = await PollOnceAsync(client, jobPath, pages, cancellationToken);
            if (outcome == PollOutcome.Done)
            {
                return pages;
            }
            if (outcome == PollOutcome.Wait)
            {
                await Task.Delay(PollIntervalMs, cancellationToken);
            }
        }
        throw new EsclProtocolException($"Scan job did not complete within {MaxPolls * PollIntervalMs / 1000} seconds.");
    }

    private async Task<PollOutcome> PollOnceAsync(EsclClient client, string jobPath, List<ScanPage> pages, CancellationToken cancellationToken)
    {
        (string state, int imagesRemaining) = await client.GetJobAsync(jobPath, cancellationToken);
        EnsureNotFailed(state);
        bool completed = state == "Completed";
        if (completed && imagesRemaining <= 0 && pages.Count > 0)
        {
            return PollOutcome.Done;
        }
        if (imagesRemaining > 0 || (completed && pages.Count == 0))
        {
            ScanPage? page = await TryFetchDocumentAsync(client, jobPath, cancellationToken);
            if (page is not null)
            {
                pages.Add(page);
                return PollOutcome.Fetched;
            }
            if (completed)
            {
                return pages.Count == 0 ? PollOutcome.Empty : PollOutcome.Done;
            }
        }
        return PollOutcome.Wait;
    }

    private async Task<ScanPage?> TryFetchDocumentAsync(EsclClient client, string jobPath, CancellationToken cancellationToken)
    {
        try
        {
            byte[] data = await client.GetNextDocumentAsync(jobPath, cancellationToken);
            return new ScanPage(DetectContentType(data), data);
        }
        catch (EsclDocumentNotFoundException)
        {
            return null;
        }
    }

    private static void EnsureNotFailed(string state)
    {
        // JobState value set is exactly {Pending, Processing, Completed, Canceled, Aborted};
        // there is no "Ready" state in eSCL.
        if (state == "Aborted")
        {
            throw new EsclProtocolException("The scanner aborted the scan job (jam, door open, or out of paper). See {job}/ErrorDetails on the device.");
        }
        if (state == "Canceled")
        {
            throw new EsclProtocolException("The scan job was canceled on the scanner.");
        }
    }

    private static string DetectContentType(byte[] data)
    {
        if (data.Length > 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
        {
            return "application/pdf"; // "%PDF"
        }
        return "image/jpeg"; // eSCL's default document format
    }

    private enum PollOutcome { Fetched, Done, Empty, Wait }

    private const int PollIntervalMs = 400;
    private const int MaxPolls = 1500; // ~10 minutes ceiling for very large ADF batches

    private readonly IHttpClientFactory _httpClientFactory;
}

public static class UrlTool
{
    public static string WithTrailingSlash(string url)
    {
        return url.EndsWith('/') ? url : url + "/";
    }
}
