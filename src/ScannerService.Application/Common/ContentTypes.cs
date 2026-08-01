namespace ScannerService.Application.Common;

/// <summary>
/// Provides MIME content type mappings for file extensions.
/// </summary>
public static class ContentTypes
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".tiff"] = "image/tiff",
        [".tif"] = "image/tiff",
        [".bmp"] = "image/bmp",
        [".zip"] = "application/zip"
    };

    /// <summary>
    /// Gets the MIME content type for a file extension.
    /// </summary>
    /// <param name="extension">File extension including the dot (e.g., ".jpg", ".pdf")</param>
    /// <returns>MIME content type string, or "application/octet-stream" if not found</returns>
    public static string GetContentType(string extension)
    {
        return MimeTypes.TryGetValue(extension, out var mimeType) ? mimeType : "application/octet-stream";
    }

    /// <summary>
    /// Gets the MIME content type for a file based on its path.
    /// </summary>
    /// <param name="filePath">Full path to the file</param>
    /// <returns>MIME content type string, or "application/octet-stream" if not found</returns>
    public static string GetContentTypeFromPath(string filePath)
    {
        var extension = System.IO.Path.GetExtension(filePath);
        return GetContentType(extension);
    }
}
