// EsclDeviceSimulator - a fake HP ScanJet Pro 4500 fn1 Network Scanner for end-to-end testing.
//
// Hosts a real eSCL server (NAPS2.Escl.Server) that advertises itself over mDNS exactly like the
// real device does (_uscan._tcp, uuid/ty/rs TXT records), so the app's full discovery and scanning
// pipeline can be validated without any physical hardware:
//
//   dotnet run --project tools/EsclDeviceSimulator -- --port 8080
//
// Then GET /api/scanners in the app: the simulated device appears with driver "Escl" and can be
// scanned like a real network scanner. Feeder jobs return --pages pages (multi-page zip path).
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using NAPS2.Escl;
using NAPS2.Escl.Server;

const string DefaultName = "HP ScanJet Pro 4500 fn1";
const string DefaultUuid = "53494d31-3234-3335-3637-383941424344";

int port = 8080;
int adfPort = 8081;
int feederPages = 3;
string name = DefaultName;
string uuid = DefaultUuid;

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--port":
            port = int.Parse(args[i + 1]);
            break;
        case "--adf-port":
            adfPort = int.Parse(args[i + 1]);
            break;
        case "--pages":
            feederPages = int.Parse(args[i + 1]);
            break;
        case "--name":
            name = args[i + 1];
            break;
        case "--uuid":
            uuid = args[i + 1];
            break;
    }
}

// NAPS2 quirk (client writes scan:InputSource, its server parser reads pwg:InputSource), so the
// posted input source always arrives as Platen. To still emulate an ADF, two devices are hosted:
// a platen device (1 page per job) and an ADF-only device (N pages per job; NAPS2 automatically
// switches scans to the feeder when a device has no platen).
var platenCapabilities = new EsclCapabilities
{
    Version = "2.0",
    MakeAndModel = name,
    Manufacturer = "HP (simulated)",
    Uuid = uuid,
    SerialNumber = "SIM0000001",
    PlatenCaps = BuildInputCaps()
};
var adfCapabilities = new EsclCapabilities
{
    Version = "2.0",
    MakeAndModel = name + " ADF",
    Manufacturer = "HP (simulated)",
    Uuid = uuid + "-adf",
    SerialNumber = "SIM0000002",
    AdfSimplexCaps = BuildInputCaps()
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new EsclServer
{
    SecurityPolicy = EsclSecurityPolicy.ServerDisableHttps
};
server.AddDevice(new EsclDeviceConfig
{
    Capabilities = platenCapabilities,
    Port = port,
    CreateJob = settings => new SimulatedScanJob(settings, 1, name)
});
server.AddDevice(new EsclDeviceConfig
{
    Capabilities = adfCapabilities,
    Port = adfPort,
    CreateJob = settings => new SimulatedScanJob(settings, feederPages, name + " ADF")
});

await server.Start();
Console.WriteLine($"eSCL simulator listening on port {port} (http), advertising mDNS _uscan._tcp");
Console.WriteLine($"  device : {name}");
Console.WriteLine($"  uuid   : {uuid}");
Console.WriteLine($"  pages  : 1 per platen job, {feederPages} per feeder job");
Console.WriteLine("Press Ctrl+C to stop.");
try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
}

await server.Stop();
Console.WriteLine("Simulator stopped.");
return;

static EsclInputCaps BuildInputCaps()
{
    return new EsclInputCaps
    {
        MinWidth = 0,
        MaxWidth = 2550, // A4 portrait at 300 dpi, units of 1/300 inch
        MinHeight = 0,
        MaxHeight = 3508,
        SettingProfiles =
        [
            new EsclSettingProfile
            {
                Name = "Default",
                ColorModes = [EsclColorMode.RGB24, EsclColorMode.Grayscale8, EsclColorMode.BlackAndWhite1],
                DocumentFormats = ["image/jpeg"],
                DiscreteResolutions =
                [
                    new DiscreteResolution(75, 75),
                    new DiscreteResolution(150, 150),
                    new DiscreteResolution(200, 200),
                    new DiscreteResolution(300, 300),
                    new DiscreteResolution(600, 600)
                ],
                XResolutionRange = new EsclRange(50, 600, 300, 1),
                YResolutionRange = new EsclRange(50, 600, 300, 1)
            }
        ]
    };
}

internal sealed class SimulatedScanJob : IEsclScanJob
{
    private readonly EsclScanSettings _settings;
    private readonly string _deviceName;
    private readonly int _feederPages;
    private int _dpi;
    private int _pagesRemaining;
    private int _pageCounter;
    private int _scanCompleteFired;
    private Action<StatusTransition>? _statusTransition;

    public SimulatedScanJob(EsclScanSettings settings, int feederPages, string deviceName)
    {
        _settings = settings;
        _deviceName = deviceName;
        _feederPages = feederPages;
        _dpi = settings.XResolution > 0 ? Math.Clamp(settings.XResolution, 75, 600) : 200;
        _pagesRemaining = settings.InputSource == EsclInputSource.Feeder ? feederPages : 1;
    }

    public string ContentType => "image/jpeg";

    public void Cancel()
    {
    }

    public void RegisterStatusTransitionCallback(Action<StatusTransition> callback)
    {
        _statusTransition = callback;
    }

    public Task<bool> WaitForNextDocument(CancellationToken cancelToken)
    {
        return Task.FromResult(Volatile.Read(ref _pagesRemaining) > 0);
    }

    public async Task WriteDocumentTo(Stream stream)
    {
        // The server may call this concurrently or retry (e.g. when a client aborts a response), so
        // the page counter is interlocked and ScanComplete is fired exactly once as soon as the page
        // budget is exhausted - a missed ScanComplete would leave the scanner stuck in Processing.
        int pageNumber = Interlocked.Increment(ref _pageCounter);
        int remaining = Interlocked.Decrement(ref _pagesRemaining);
        byte[] jpeg = RenderPage(pageNumber);
        await stream.WriteAsync(jpeg);
        if (remaining <= 0 && Interlocked.Exchange(ref _scanCompleteFired, 1) == 0)
        {
            _statusTransition?.Invoke(StatusTransition.ScanComplete);
        }
        else
        {
            _statusTransition?.Invoke(StatusTransition.PageComplete);
        }
    }

    public Task WriteProgressTo(Stream stream)
    {
        return Task.CompletedTask;
    }

    public Task WriteErrorDetailsTo(Stream stream)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    private byte[] RenderPage(int pageNumber)
    {
        // Page size in pixels at the requested dpi (A4 portrait), capped so generation stays fast.
        int width = Math.Clamp(850 * _dpi / 200, 850, 2550);
        int height = Math.Clamp(1100 * _dpi / 200, 1100, 3508);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var borderPen = new Pen(Color.Black, 3);
            graphics.DrawRectangle(borderPen, 20, 20, width - 40, height - 40);
            using var font = new Font("Arial", _dpi / 10f, FontStyle.Bold);
            using var smallFont = new Font("Arial", _dpi / 16f);
            using var brush = Brushes.Black;
            graphics.DrawString("SIMULATED SCAN", font, brush, 60, 80);
            graphics.DrawString(_deviceName, smallFont, brush, 60, 80 + _dpi / 4f);
            graphics.DrawString($"Page {pageNumber}", smallFont, brush, 60, 80 + _dpi / 2f);
            graphics.DrawString($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}", smallFont, brush, 60, 80 + 3 * _dpi / 4f);
            graphics.DrawString($"dpi: {_dpi}  job source: {_settings.InputSource}", smallFont, brush, 60, 80 + _dpi);
            for (int y = 0; y < 10; y++)
            {
                using var linePen = new Pen(Color.FromArgb(40 + y * 20, Color.Black), 1);
                graphics.DrawLine(linePen, 60, 80 + (5 + y) * _dpi / 4f, width - 60, 80 + (5 + y) * _dpi / 4f);
            }
        }

        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 75L);
        var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, jpegEncoder, encoderParameters);
        return memoryStream.ToArray();
    }
}
