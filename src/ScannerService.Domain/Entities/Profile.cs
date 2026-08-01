using ScannerService.Domain.Common;

namespace ScannerService.Domain.Entities;

public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string PaperSource { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultPaperSource;
    public string BitDepth { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultBitDepth;
    public string PageSize { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultPageSize;
    public string HorizontalAlign { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultHorizontalAlign;
    public int Resolution { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultResolution;
    public string Scale { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultScale;
    public int Brightness { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultBrightness;
    public int Contrast { get; set; } = Common.ApplicationConstants.ProfileDefaults.DefaultContrast;
    public int ImageQuality { get; set; } = Common.ApplicationConstants.ExportDefaults.DefaultImageQuality;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Updates the profile with the provided values.
    /// </summary>
    public void Update(ProfileUpdateOptions options)
    {
        if (options == null)
        {
            return;
        }

        var hasChanges = false;

        if (options.Name != null)
        {
            Name = options.Name;
            hasChanges = true;
        }

        if (options.DeviceId != null)
        {
            DeviceId = options.DeviceId;
            hasChanges = true;
        }

        if (options.PaperSource != null)
        {
            PaperSource = options.PaperSource;
            hasChanges = true;
        }

        if (options.BitDepth != null)
        {
            BitDepth = options.BitDepth;
            hasChanges = true;
        }

        if (options.PageSize != null)
        {
            PageSize = options.PageSize;
            hasChanges = true;
        }

        if (options.HorizontalAlign != null)
        {
            HorizontalAlign = options.HorizontalAlign;
            hasChanges = true;
        }

        if (options.Resolution.HasValue)
        {
            Resolution = options.Resolution.Value;
            hasChanges = true;
        }

        if (options.Scale != null)
        {
            Scale = options.Scale;
            hasChanges = true;
        }

        if (options.Brightness.HasValue)
        {
            Brightness = options.Brightness.Value;
            hasChanges = true;
        }

        if (options.Contrast.HasValue)
        {
            Contrast = options.Contrast.Value;
            hasChanges = true;
        }

        if (options.ImageQuality.HasValue)
        {
            ImageQuality = options.ImageQuality.Value;
            hasChanges = true;
        }

        if (hasChanges)
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Checks if the profile is valid for scanning operations.
    /// </summary>
    public bool IsValidForScanning() => !string.IsNullOrEmpty(DeviceId);

    /// <summary>
    /// Creates a new Profile with the specified values.
    /// </summary>
    public static Profile Create(
        string name,
        string? deviceId,
        string paperSource,
        string bitDepth,
        string pageSize,
        string horizontalAlign,
        int resolution,
        string scale,
        int brightness,
        int contrast,
        int imageQuality)
    {
        return new Profile
        {
            Name = name,
            DeviceId = deviceId,
            PaperSource = paperSource,
            BitDepth = bitDepth,
            PageSize = pageSize,
            HorizontalAlign = horizontalAlign,
            Resolution = resolution,
            Scale = scale,
            Brightness = brightness,
            Contrast = contrast,
            ImageQuality = imageQuality,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
