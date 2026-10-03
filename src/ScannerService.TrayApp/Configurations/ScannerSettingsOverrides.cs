using System.Collections.Generic;
using ScannerService.Application.DTOs;

namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// The appsettings.local.json sidecar file model: sparse, nullable fields where null means
/// "not overridden — fall back to the appsettings.json base value". This is internal to the
/// settings persistence; the API contract is the flat ScannerSettingsDto (a full PUT writes
/// every field here, so the API never exposes the sparse semantics).
/// </summary>
public sealed record ScannerSettingsOverridesDto(
    int? StatusCheckInterval,
    int? HttpTimeout,
    int? StartupDelay,
    int? DriverTimeoutMs,
    int? EsclSearchTimeoutMs,
    int? EsclSearchMarginMs,
    int? DriverCooldownMs,
    int? DriverCooldownMaxMs,
    int? ScanQueueTimeoutMs,
    int? ScanOverallTimeoutMs,
    int? ScanNoProgressTimeoutMs,
    int? ShutdownTimeoutMs,
    int? ScannersRequestTimeoutSeconds,
    int? ScanRequestTimeoutSeconds,
    List<EsclManualDeviceSettingDto>? EsclManualDevices);
