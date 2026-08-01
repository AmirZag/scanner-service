using ScannerService.Application.DTOs;
using ScannerService.Domain.Common;

namespace ScannerService.Application.Common;

/// <summary>
/// Extension methods for Profile update operations.
/// </summary>
public static class ProfileUpdateExtensions
{
    /// <summary>
    /// Creates a ProfileUpdateOptions from an UpdateProfileDto.
    /// </summary>
    public static ProfileUpdateOptions ToUpdateOptions(this UpdateProfileDto dto)
    {
        return new ProfileUpdateOptions
        {
            Name = dto.Name,
            DeviceId = dto.DeviceId,
            PaperSource = dto.PaperSource,
            BitDepth = dto.BitDepth,
            PageSize = dto.PageSize,
            HorizontalAlign = dto.HorizontalAlign,
            Resolution = dto.Resolution,
            Scale = dto.Scale,
            Brightness = dto.Brightness,
            Contrast = dto.Contrast,
            ImageQuality = dto.ImageQuality
        };
    }
}
