namespace EsclReferenceClient.Escl;

using System.Xml.Linq;

public sealed record ScanRequest(
    string Source,            // platen | adf | adf-duplex
    int Resolution,           // dpi, e.g. 300
    string ColorMode,         // RGB24 | Grayscale8 | Grayscale16 | BlackAndWhite1
    string Format,            // image/jpeg | application/pdf | image/png
    int? WidthHundredths,     // region in 1/300 inch (ContentRegionUnits); null = full bed default
    int? HeightHundredths);

public static class ScanSettingsBuilder
{
    // Verified: the eSCL spec, Apple's own client, sane-airscan, sane-backends, NAPS2, go-mfp and
    // real device XML all use .../2011/05/03. The /05 variant floats around vendor documents but no
    // implementation ships it. PWG namespace is http://www.pwg.org/schemas/2010/12/sm everywhere.
    public const string ScanNamespace = "http://schemas.hp.com/imaging/escl/2011/05/03";
    public const string PwgNamespace = "http://www.pwg.org/schemas/2010/12/sm";

    public static XDocument Build(ScanRequest request)
    {
        Validate(request);
        XNamespace scan = ScanNamespace;
        XNamespace pwg = PwgNamespace;

        // Element order and prefixes follow sane-airscan / sane-backends: pwg: carries the
        // PWG-standardized elements (Version, ScanRegions, InputSource, DocumentFormat),
        // scan: carries the eSCL-specific ones (Intent, resolution, ColorMode, Duplex).
        XElement settings = new XElement(scan + "ScanSettings",
            new XAttribute(XNamespace.Xmlns + "scan", ScanNamespace),
            new XAttribute(XNamespace.Xmlns + "pwg", PwgNamespace),
            new XElement(pwg + "Version", "2.0"),
            new XElement(scan + "Intent", "Document"),
            BuildRegion(pwg, request),
            new XElement(pwg + "InputSource", request.Source == "platen" ? "Platen" : "Feeder"),
            new XElement(scan + "XResolution", request.Resolution),
            new XElement(scan + "YResolution", request.Resolution),
            new XElement(scan + "ColorMode", request.ColorMode),
            new XElement(pwg + "DocumentFormat", request.Format),
            BuildDuplex(scan, request.Source));
        return new XDocument(settings);
    }

    // Region units are 1/300 inch; the literal value "escl:ThreeHundredthsOfInches" (with the
    // undeclared 'escl:' prefix) is what both reference implementations send verbatim — a known quirk.
    private static XElement BuildRegion(XNamespace pwg, ScanRequest request)
    {
        int width = request.WidthHundredths ?? 2550;
        int height = request.HeightHundredths ?? 3300;
        return new XElement(pwg + "ScanRegions",
            new XElement(pwg + "ScanRegion",
                new XElement(pwg + "ContentRegionUnits", "escl:ThreeHundredthsOfInches"),
                new XElement(pwg + "Width", width),
                new XElement(pwg + "Height", height),
                new XElement(pwg + "XOffset", 0),
                new XElement(pwg + "YOffset", 0)));
    }

    private static XElement? BuildDuplex(XNamespace scan, string source)
    {
        // Reference clients signal duplex as InputSource=Feeder + scan:Duplex=true rather than the
        // alternative "Feeder-Duplex" InputSource value; this is the widely-compatible form.
        return source == "adf-duplex" ? new XElement(scan + "Duplex", "true") : null;
    }

    private static void Validate(ScanRequest request)
    {
        string[] validSources = { "platen", "adf", "adf-duplex" };
        if (!validSources.Contains(request.Source))
        {
            throw new ArgumentException($"Unknown source '{request.Source}'. Expected platen|adf|adf-duplex.");
        }
        if (request.Resolution is < 75 or > 1200)
        {
            throw new ArgumentException("Resolution must be between 75 and 1200 dpi.");
        }
    }
}
