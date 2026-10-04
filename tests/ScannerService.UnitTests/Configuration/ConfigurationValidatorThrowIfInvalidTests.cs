using System;
using System.Collections.Generic;
using ScannerService.TrayApp.Configurations;
using Xunit;

namespace ScannerService.UnitTests.Configuration;

/// <summary>
/// Covers ConfigurationValidator.ThrowIfInvalid. The extension is currently referenced by no
/// production caller (Phase 1 audit C-3 dead-code list, removed in the dead-code batch) — the
/// test pins its contract until that deletion and goes away with it.
/// </summary>
public class ConfigurationValidatorThrowIfInvalidTests
{
    [Fact]
    public void ThrowIfInvalid_WithValidResult_DoesNotThrow()
    {
        (bool IsValid, List<string> Errors) valid = (true, new List<string>());

        Exception? failure = Record.Exception(() => valid.ThrowIfInvalid("test-config"));

        Assert.Null(failure);
    }

    [Fact]
    public void ThrowIfInvalid_WithInvalidResult_ThrowsWithConfigNameAndAllErrors()
    {
        (bool IsValid, List<string> Errors) invalid = (false, new List<string> { "first error", "second error" });

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => invalid.ThrowIfInvalid("scanner-service"));

        Assert.Contains("scanner-service validation failed", failure.Message);
        Assert.Contains("first error", failure.Message);
        Assert.Contains("second error", failure.Message);
    }
}
