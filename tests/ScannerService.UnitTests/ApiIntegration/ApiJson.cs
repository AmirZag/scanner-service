using System.Text.Json;

namespace ScannerService.UnitTests.ApiIntegration;

/// <summary>
/// JSON options mirroring the minimal-API serialization defaults (camelCase, web defaults) so
/// response bodies deserialize case-insensitively into the production DTO records.
/// </summary>
internal static class ApiJson
{
    public static readonly JsonSerializerOptions Web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
}
