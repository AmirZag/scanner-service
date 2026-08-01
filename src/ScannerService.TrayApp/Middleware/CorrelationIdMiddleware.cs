using System.Globalization;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Context;

namespace ScannerService.TrayApp.Middleware;

/// <summary>
/// Middleware that adds a correlation ID to each request for tracing.
/// Ensures all log entries for a single request share the same correlation ID.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private const string CorrelationIdLogPropertyName = "CorrelationId";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Try to get correlation ID from header, otherwise generate new one
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var value)
            ? value.ToString()
            : Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        // Add to response header
        context.Response.Headers.Append(CorrelationIdHeaderName, correlationId);

        // Add to log context for this request
        using (LogContext.PushProperty(CorrelationIdLogPropertyName, correlationId))
        {
            await _next(context);
        }
    }
}
