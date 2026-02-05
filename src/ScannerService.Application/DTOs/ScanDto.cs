using System;
using System.Collections.Generic;

namespace ScannerService.Application.DTOs;

public record ScanRequest(
    int ProfileId,
    string? OutputPath = null,
    string? OutputFormat = null
);

public record ScanResult(
    bool Success,
    List<string> Files,
    string? ErrorMessage,
    TimeSpan Duration
);