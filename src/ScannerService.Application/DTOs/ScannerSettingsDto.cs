using System.Collections.Generic;

namespace ScannerService.Application.DTOs;

/// <summary>
/// One manually configured eSCL (driverless network) scanner entry.
/// </summary>
public sealed record EsclManualDeviceSettingDto
{
    /// <summary>Optional display name shown in the UI; defaults to the host part of <see cref="Address"/> when omitted.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Scanner location: a bare host name or IP (the HP default endpoint http://&lt;host&gt;:8080/eSCL
    /// is then assumed) or a full eSCL root URL such as "http://192.168.1.50:8080/eSCL".
    /// Bare IPv6 is not accepted — use the bracketed URL form (http://[fe80::1]:8080/eSCL).
    /// </summary>
    public string Address { get; set; } = string.Empty;
}

/// <summary>
/// The ScannerService settings section. GET /api/settings returns the currently effective values;
/// PUT /api/settings accepts the same shape as a FULL replacement (send every field). Changing any
/// value via PUT persists it to appsettings.local.json and restarts the API host in-process
/// (same port; in-flight scans are aborted). ApiPort/ApiHost are intentionally absent — they are
/// read-only because the frontend addresses the agent at a fixed host:port.
/// </summary>
public sealed record ScannerSettingsDto
{
    /// <summary>How often (ms) the tray icon polls the API health endpoint. Range 1000–300000. Default 5000.</summary>
    public int StatusCheckInterval { get; set; } = 5000;

    /// <summary>Timeout (ms) for the tray's own health-check HTTP calls. Range 100–60000. Default 2000.</summary>
    public int HttpTimeout { get; set; } = 2000;

    /// <summary>Delay (ms) after the API host starts before the tray polls its health. Range 0–60000. Default 2000.</summary>
    public int StartupDelay { get; set; } = 2000;

    /// <summary>Total budget (ms) for one device-enumeration pass across all drivers. Range 1000–120000. Default 15000.</summary>
    public int DriverTimeoutMs { get; set; } = 15000;

    /// <summary>Time (ms) the eSCL (network) discovery search may run within the driver budget. Range 500–DriverTimeoutMs. Default 8000.</summary>
    public int EsclSearchTimeoutMs { get; set; } = 8000;

    /// <summary>Safety margin (ms) kept between the eSCL search window and the overall driver budget. Range 500–10000. Default 2000.</summary>
    public int EsclSearchMarginMs { get; set; } = 2000;

    /// <summary>Initial cool-down (ms) applied to a driver after a timeout before it is retried. Range 5000–1800000. Default 60000.</summary>
    public int DriverCooldownMs { get; set; } = 60000;

    /// <summary>Upper bound (ms) for the exponential per-driver cool-down. Range DriverCooldownMs–3600000. Default 600000.</summary>
    public int DriverCooldownMaxMs { get; set; } = 600000;

    /// <summary>Max (ms) a scan may wait in the internal scan queue before failing. Range 0–60000. Default 5000.</summary>
    public int ScanQueueTimeoutMs { get; set; } = 5000;

    /// <summary>Total budget (ms) for one scan job end to end. Range 30000–3600000. Default 600000.</summary>
    public int ScanOverallTimeoutMs { get; set; } = 600000;

    /// <summary>Max (ms) a scan may make no progress (no new page/document) before failing. Range 10000–ScanOverallTimeoutMs. Default 120000.</summary>
    public int ScanNoProgressTimeoutMs { get; set; } = 120000;

    /// <summary>Grace period (ms) for in-flight requests and worker teardown on shutdown/restart. Range 1000–30000. Default 5000.</summary>
    public int ShutdownTimeoutMs { get; set; } = 5000;

    /// <summary>HTTP request-timeout policy (s) for GET /api/scanners. Must exceed DriverTimeoutMs + 5s. Range to 300. Default 25.</summary>
    public int ScannersRequestTimeoutSeconds { get; set; } = 25;

    /// <summary>HTTP request-timeout policy (s) for POST /api/scan. Must exceed ScanOverallTimeoutMs + 30s. Range to 7200. Default 660.</summary>
    public int ScanRequestTimeoutSeconds { get; set; } = 660;

    /// <summary>Manually configured eSCL network scanners (used when mDNS discovery cannot reach the device). Replaces the whole list.</summary>
    public List<EsclManualDeviceSettingDto> EsclManualDevices { get; set; } = [];
}

/// <summary>PUT /api/settings and DELETE /api/settings/overrides response: the change was persisted
/// and the API host is restarting in-process to apply it (same port; in-flight scans are aborted).</summary>
public sealed record SettingsUpdateResponseDto(bool Restarting, string Warning);
