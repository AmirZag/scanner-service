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
    /// Updates the profile with the provided values. Only non-null parameters update the entity.
    /// </summary>
    public void Update(
        string? name = null,
        string? deviceId = null,
        string? paperSource = null,
        string? bitDepth = null,
        string? pageSize = null,
        string? horizontalAlign = null,
        int? resolution = null,
        string? scale = null,
        int? brightness = null,
        int? contrast = null,
        int? imageQuality = null)
    {
        var hasChanges = false;

        if (name != null)
        {
            Name = name;
            hasChanges = true;
        }

        if (deviceId != null)
        {
            DeviceId = deviceId;
            hasChanges = true;
        }

        if (paperSource != null)
        {
            PaperSource = paperSource;
            hasChanges = true;
        }

        if (bitDepth != null)
        {
            BitDepth = bitDepth;
            hasChanges = true;
        }

        if (pageSize != null)
        {
            PageSize = pageSize;
            hasChanges = true;
        }

        if (horizontalAlign != null)
        {
            HorizontalAlign = horizontalAlign;
            hasChanges = true;
        }

        if (resolution != null)
        {
            Resolution = resolution.Value;
            hasChanges = true;
        }

        if (scale != null)
        {
            Scale = scale;
            hasChanges = true;
        }

        if (brightness != null)
        {
            Brightness = brightness.Value;
            hasChanges = true;
        }

        if (contrast != null)
        {
            Contrast = contrast.Value;
            hasChanges = true;
        }

        if (imageQuality != null)
        {
            ImageQuality = imageQuality.Value;
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
