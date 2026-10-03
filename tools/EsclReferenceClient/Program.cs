namespace EsclReferenceClient;

using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EsclReferenceClient.Discovery;
using EsclReferenceClient.Escl;

/// <summary>
/// Minimal localhost scan agent: loopback-only Kestrel, CORS for the web frontend origin,
/// GET /scanners (config + mDNS) and POST /scan (eSCL job, base64 pages back).
/// </summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:9375"); // loopback only — nothing remote reaches the agent
        ConfigureServices(builder.Services, builder.Configuration);
        WebApplication app = builder.Build();
        ConfigurePipeline(app);
        await app.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        string allowedOrigins = configuration["Cors:AllowedOrigin"]
            ?? throw new InvalidOperationException("Cors:AllowedOrigin must list the web frontend origin(s).");
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .AllowAnyHeader()
            .AllowAnyMethod()));
        services.AddHttpClient();
        services.AddSingleton<ScannerDirectory>();
        services.AddSingleton<ScanOrchestrator>();
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        app.Use(ApplyLegacyPrivateNetworkPreflight); // must run before the CORS middleware
        app.UseCors();
        app.MapGet("/scanners", ListScannersAsync);
        app.MapPost("/scan", ScanAsync);
    }

    // Historical note (verified 2026-10): Chrome's Private Network Access PREFLIGHT never shipped;
    // its successor, Local Network Access, is a per-site user permission prompt with no extra
    // response headers, so nothing is required here for current Chrome/Edge/Firefox. The header
    // echo below stays only as a hedge for engines that may revive the preflight mechanism.
    private static async Task ApplyLegacyPrivateNetworkPreflight(HttpContext context, Func<Task> next)
    {
        if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
        {
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        }
        await next();
    }

    private static async Task<IResult> ListScannersAsync(ScannerDirectory directory, CancellationToken cancellationToken)
    {
        return Results.Ok(await directory.ListAsync(cancellationToken));
    }

    private static async Task<IResult> ScanAsync(ScanJobDto job, ScanOrchestrator orchestrator, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(job.ScannerUrl, UriKind.Absolute, out Uri? scannerUri)
            || (scannerUri.Scheme != Uri.UriSchemeHttp && scannerUri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(new { error = "scannerUrl must be an absolute http(s) URL." });
        }
        try
        {
            XDocument settings = ScanSettingsBuilder.Build(job.ToRequest());
            ScanResult result = await orchestrator.ScanAsync(job.ScannerUrl, settings, cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (EsclProtocolException exception)
        {
            return Results.Problem(title: "Scan failed", detail: exception.Message, statusCode: 502);
        }
    }

    private static object ToResponse(ScanResult result)
    {
        return new
        {
            pageCount = result.PageCount,
            pages = result.Pages.Select(p => new
            {
                contentType = p.ContentType,
                data = Convert.ToBase64String(p.Data)
            }).ToList()
        };
    }
}

public sealed class ScanJobDto
{
    public string ScannerUrl { get; set; } = string.Empty;
    public string Source { get; set; } = "platen";
    public int Resolution { get; set; } = 300;
    public string ColorMode { get; set; } = "RGB24";
    public string Format { get; set; } = "image/jpeg";
    public int? WidthHundredths { get; set; }
    public int? HeightHundredths { get; set; }

    public ScanRequest ToRequest()
    {
        return new ScanRequest(Source.ToLowerInvariant(), Resolution, ColorMode, Format, WidthHundredths, HeightHundredths);
    }
}
