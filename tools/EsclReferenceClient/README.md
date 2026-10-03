# EsclReferenceClient

A standalone, minimal **eSCL scan agent** — the reference for how a small localhost agent talks
to a network eSCL scanner (AirPrint/AirScan/Mopria eSCL protocol). It is **not part of the main
solution** (like `EsclDeviceSimulator`); build it on demand:

```powershell
dotnet run --project tools/EsclReferenceClient
# list scanners:   curl http://127.0.0.1:9375/scanners
# scan:            curl -X POST http://127.0.0.1:9375/scan -H "Content-Type: application/json" ^
#   -d "{\"scannerUrl\":\"http://<ip>:8080/eSCL\",\"source\":\"adf\",\"resolution\":300,\"colorMode\":\"RGB24\",\"format\":\"application/pdf\"}"
```

Works against a real network scanner (e.g. HP ScanJet Pro 4500 fn1 at `http://<ip>:8080/eSCL`)
or against `tools/EsclDeviceSimulator`.

## Layout

| File | Role |
|---|---|
| `Escl/EsclClient.cs` | eSCL REST wrapper: capabilities / ScanJobs / job status / NextDocument / DELETE |
| `Escl/ScanSettingsBuilder.cs` | Builds the `ScanSettings` XML (namespaces, prefixes, region units, duplex) |
| `Escl/ScanOrchestrator.cs` | Job lifecycle: create → poll/fetch loop → always DELETE |
| `Discovery/ScannerDirectory.cs` | Configured scanner URL + mDNS `_uscan._tcp` browse (Zeroconf, pure managed) |
| `Program.cs` | Loopback-only Kestrel host, CORS, endpoints |

## Verified protocol facts baked into the code

Established 2026-10 against sane-airscan, sane-backends, NAPS2 and go-mfp sources plus real
device captures — if you extend this tool, keep these invariants:

- eSCL XML namespace is `http://schemas.hp.com/imaging/escl/2011/05/03` (not `/05`); PWG namespace
  is `http://www.pwg.org/schemas/2010/12/sm`. `pwg:` carries Version/ScanRegions/InputSource/
  DocumentFormat; `scan:` carries Intent/resolution/ColorMode/Duplex.
- `POST eSCL/ScanJobs` with Content-Type **`text/xml`** → `201 Created` + `Location`. The Location
  hostname is untrustworthy (HP returns serial-number hosts, Xerox malformed IPv6) — keep only
  path+query and re-host on the connection's base address.
- JobState values are exactly `Pending, Processing, Completed, Canceled, Aborted` — there is no
  "Ready". `Aborted` = device-side failure (see `{job}/ErrorDetails`).
- **End of job = `404` on `NextDocument`**, not `ImagesToTransfer == 0` — the counter is advisory
  (whole PDF stacks arrive as one document; some Xerox models 404 for "not ready"). `503` = the
  page is still being produced → retry after 1–2 s, never re-POST.
- Duplex = `InputSource: Feeder` + `scan:Duplex: true` (not a "Feeder-Duplex" input source).
- Region units are 1/300 inch; the literal `"escl:ThreeHundredthsOfInches"` is sent verbatim
  (undeclared prefix is a known device quirk).
- Always `DELETE` the job — even after completion (404 there is fine). Un-deleted jobs linger on
  shared MFPs and can block other users.

## Frontend contract (for reference)

```js
const scanners = await (await fetch('http://127.0.0.1:9375/scanners')).json();
const scan = await fetch('http://127.0.0.1:9375/scan', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ scannerUrl: scanners[0].url, source: 'adf-duplex',
                         resolution: 300, colorMode: 'RGB24', format: 'application/pdf' }),
}).then(r => r.json());
// scan = { pageCount, pages: [{ contentType, data(base64) }] }
```

Browser notes: `http://127.0.0.1` is mixed-content exempt (callable from HTTPS pages). Chrome's
Local Network Access (2025+) may show a one-time per-site permission prompt for intranet→loopback
fetches — expect it, no server-side header is needed.
