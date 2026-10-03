using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ScannerService.Application.Common;
using ScannerService.Application.DTOs;
using Serilog;

namespace ScannerService.TrayApp.Configurations;

/// <summary>
/// Persistence for API-managed settings: the appsettings.local.json sidecar file next to the
/// executable. The installer owns appsettings.json and overwrites it on every upgrade, so user
/// changes must live in this never-shipped sidecar. The file is read ONLY here as a typed
/// overrides DTO (explicit merge via ScannerSettingsMapper) and never fed into the configuration
/// system, so unknown or read-only keys hand-edited into it (e.g. apiPort) can never take effect.
/// </summary>
public class LocalSettingsStore
{
    /// <summary>Sidecar file name, relative to the executable directory.</summary>
    public const string FileName = "appsettings.local.json";

    private const string TempSuffix = ".tmp";
    private const string InvalidSuffix = ".invalid";
    private const int MaxWriteAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    // Serializes sidecar IO. The write path deliberately uses synchronous file IO inside the
    // lock (the payloads are tiny), so the retry delay between attempts happens OUTSIDE the
    // lock and a plain monitor suffices — no IDisposable gate that the host's container could
    // dispose while TrayApp keeps using the store across host restarts.
    private readonly object _fileGate = new object();

    public LocalSettingsStore(ScannerServiceConfiguration baseConfig)
    {
        BaseConfig = baseConfig;
        _filePath = Path.Combine(AppContext.BaseDirectory, FileName);
        SweepStaleTempFile();
    }

    /// <summary>Immutable appsettings.json base snapshot that the overrides overlay.</summary>
    public ScannerServiceConfiguration BaseConfig { get; }

    /// <summary>True when a sidecar file with overrides currently exists on disk.</summary>
    public bool SidecarExists
    {
        get
        {
            lock (_fileGate)
            {
                return File.Exists(_filePath);
            }
        }
    }

    /// <summary>Deletes a leftover .tmp from a crashed write attempt; harmless litter otherwise.</summary>
    private void SweepStaleTempFile()
    {
        try
        {
            string tempPath = _filePath + TempSuffix;
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not sweep the stale settings sidecar temp file");
        }
    }

    /// <summary>
    /// Loads the installer-owned base configuration from appsettings.json. Malformed JSON or a
    /// missing file propagates like the historical inline TrayApp loading did (fatal at startup).
    /// </summary>
    public static ScannerServiceConfiguration LoadBaseConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        return configuration.GetSection("ScannerService").Get<ScannerServiceConfiguration>() ?? new ScannerServiceConfiguration();
    }

    /// <summary>
    /// Loads the effective configuration (this store's base snapshot + sidecar overrides,
    /// validated) for the host restart path. Uses the same base snapshot and the same file gate
    /// as PUT/GET, so a restart can never validate against a different base than the API did.
    /// On any failure the error names which part went wrong; callers keep the running host.
    /// </summary>
    public Result<ScannerServiceConfiguration> LoadEffectiveConfiguration()
    {
        try
        {
            (bool BaseValid, List<string> BaseErrors) baseValidation = ConfigurationValidator.ValidateScannerServiceConfiguration(BaseConfig);
            if (!baseValidation.BaseValid)
            {
                return Result<ScannerServiceConfiguration>.Failure("appsettings.json: " + string.Join("; ", baseValidation.BaseErrors));
            }

            Result<ScannerSettingsOverridesDto> overrides = ReadOverrides();
            if (overrides.IsFailure)
            {
                return Result<ScannerServiceConfiguration>.Failure("appsettings.local.json: " + overrides.Error);
            }

            ScannerServiceConfiguration merged = ScannerSettingsMapper.Merge(BaseConfig, overrides.Value!);
            (bool MergedValid, List<string> MergedErrors) mergedValidation = ConfigurationValidator.ValidateScannerServiceConfiguration(merged);
            if (!mergedValidation.MergedValid)
            {
                return Result<ScannerServiceConfiguration>.Failure("merged settings: " + string.Join("; ", mergedValidation.MergedErrors));
            }

            return Result<ScannerServiceConfiguration>.Success(merged);
        }
        catch (Exception ex)
        {
            return Result<ScannerServiceConfiguration>.Failure("Failed to load settings: " + ex.Message);
        }
    }

    /// <summary>
    /// Reads the sparse overrides from the sidecar. No file (or empty JSON) yields an all-null
    /// overrides DTO; unparsable content yields a Failure — the caller decides between
    /// quarantining (startup) and keeping the running host (restart).
    /// </summary>
    public Result<ScannerSettingsOverridesDto> ReadOverrides()
    {
        lock (_fileGate)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return Result<ScannerSettingsOverridesDto>.Success(CreateEmptyOverrides());
                }

                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Result<ScannerSettingsOverridesDto>.Success(CreateEmptyOverrides());
                }

                ScannerSettingsOverridesDto? overrides = JsonSerializer.Deserialize<ScannerSettingsOverridesDto>(json, JsonOptions);
                return overrides is null
                    ? Result<ScannerSettingsOverridesDto>.Failure("The file is empty or contains no overrides object")
                    : Result<ScannerSettingsOverridesDto>.Success(overrides);
            }
            catch (JsonException ex)
            {
                // Genuinely corrupt content: quarantine it so it cannot shadow future starts.
                // Transient IO failures (AV lock, ACL) must NOT take this path — the file stays
                // on disk and the next successful read applies it.
                QuarantineCorruptFile();
                return Result<ScannerSettingsOverridesDto>.Failure("The file is not valid JSON and was quarantined: " + ex.Message);
            }
            catch (Exception ex)
            {
                return Result<ScannerSettingsOverridesDto>.Failure("The file could not be read (kept on disk): " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Persists the overrides atomically (temp file + move), retrying transient locks
    /// (antivirus, indexer) a few times before surfacing a failure.
    /// </summary>
    public async Task<Result> WriteOverrides(ScannerSettingsOverridesDto overrides, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(overrides, JsonOptions);
        for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            try
            {
                lock (_fileGate)
                {
                    string tempPath = _filePath + TempSuffix;
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _filePath, true);
                }

                return Result.Success();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                TryDeleteTempFile();
                if (attempt >= MaxWriteAttempts)
                {
                    // Last attempt also failed: report a failure instead of letting the
                    // exception escape (the caller maps Result.Failure to a clean 500).
                    return Result.Failure($"Could not write {FileName} after {MaxWriteAttempts} attempts: {ex.Message}");
                }

                Log.Warning(ex, "Writing {SettingsFile} failed (attempt {Attempt} of {MaxAttempts}); retrying", FileName, attempt, MaxWriteAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }

        return Result.Failure($"Could not write {FileName} after {MaxWriteAttempts} attempts (file locked or access denied)");
    }

    /// <summary>
    /// Raw sidecar content (null when absent), used by the restart path to detect settings writes
    /// that landed DURING an in-flight restart — such a write's restart raise dies with the old
    /// host, so the restart re-checks the snapshot after each swap and runs once more if changed.
    /// </summary>
    public string? ReadSidecarSnapshot()
    {
        lock (_fileGate)
        {
            return File.Exists(_filePath) ? File.ReadAllText(_filePath) : null;
        }
    }

    /// <summary>Deletes the sidecar, reverting every setting to its appsettings.json value.</summary>
    public Result DeleteOverrides()
    {
        lock (_fileGate)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return Result.Success();
                }

                File.Delete(_filePath);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure($"Could not delete {FileName}: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Renames a corrupt sidecar out of the way (to .invalid) so the app can start with the base
    /// configuration; the corrupt content stays on disk for inspection.
    /// </summary>
    public static void QuarantineCorruptFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string invalidPath = path + InvalidSuffix;
            File.Move(path, invalidPath, true);
            Log.Warning("Quarantined corrupt settings sidecar {SettingsFile} to {InvalidPath}", FileName, invalidPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not quarantine corrupt {SettingsFile}; it will be re-reported on every start", FileName);
        }
    }

    private static ScannerSettingsOverridesDto CreateEmptyOverrides()
    {
        return new ScannerSettingsOverridesDto(
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }

    private void TryDeleteTempFile()
    {
        try
        {
            string tempPath = _filePath + TempSuffix;
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not delete the settings sidecar temp file");
        }
    }
}
