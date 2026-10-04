# ResaaScanner — Phase 1 Audit (Read-Only)

**Date:** 2026-10-03 · **Branch state:** clean working tree @ `9ca1bd5` · **Solution:** `ScannerService.slnx` (4 projects, ~8.2k LOC)

## 1. Method & Evidence

- **Build evidence:** `dotnet build ScannerService.slnx` → **0 warnings, 0 errors**. With `TreatWarningsAsErrors` + `AnalysisMode=All` + SonarAnalyzer 10.35 + `EnforceCodeStyleInBuild`, this proves **zero analyzer-visible Sonar violations**. The repo is already at the brief's "zero debt" static-analysis bar.
- **Suppressions inventory:** 7 documented `#pragma` lines (S8949 ×4, S5332 ×3, all with justification comments) + **1 undocumented global `NoWarn CA1873`** (`Directory.Build.props:19` — F1) + 1 mislabeled suppression comment (`.editorconfig:406` — F20).
- **Process:** 126-agent workflow — 7 parallel finders (Domain+Application, Infrastructure ×2 lenses, TrayApp ×2 lenses, architecture/config, .NET-10 standards sweep) → 82 raw findings → 78 deduped → **adversarial verification per finding** (a refuter lens that must re-derive every claim from source, plus an app-impact lens for resource/concurrency/security/high-severity items) → completeness critic. 64 confirmed, 7 downgraded, 7 refuted.
- **Git-history hotspots** (regression-risk signal for Phase 2): `WebApiHostService.cs` 17 commits, `TrayApp.cs` 12, `ScannerService.cs` 11, `ScanJobService.cs` 10.

## 2. Executive Summary

The codebase is in **unusually good shape** for its class of app: the strict-build bar is genuinely met, the Clean Architecture layering is clean (verified project-reference graph, no layer leaks), the settings/config/validator chain is field-by-field consistent, the scan watchdog and single-flight cache are correct, and the standards sweep found **no** async void, no blocking waits, no Thread.Sleep, structured logging throughout, UTC discipline on data paths, and culture-safe formatting. The brief's heavyweight prescriptions (SafeHandle wrappers, Channel pipelines, Polly, LoggerMessage source-gen, ArrayPool) are **not applicable here** — NAPS2.Sdk owns the drivers, the flow is request/response, and logging volume doesn't justify source-gen.

The real debt is **semantic**, concentrated in four clusters:

1. **Lifecycle orderings on failure paths** — dispose-before-outcome-known (restart-as-admin), unobserved bind failures, leaked hosts on failed starts, restart without a catch.
2. **Temp-file / image lifetimes on failure paths** — multi-page-TIFF BMPs, orphaned ZIPs, unguarded sidecar reads.
3. **Contract mismatches** — validators accept what parsers reject (BitDepth casing), documented 503 that never happens, dead config keys the README documents, validator NREs → 500.
4. **Dead code** (~600 lines): `ITimeProvider`, `ResultExtensions`, `RepositoryBase` scaffolding, `OriginChecker`, `DependencyHealthDto`, duplicated `CreateEmptyOverrides`, `ThrowIfInvalid`.

**Counts: 4 high · 13 medium · ~21 low · 4 reviewed-and-declined · 7 refuted.**

---

## 3. Category A — Resource & Memory Safety

### A-1 · HIGH · Restart-as-admin disposes the app before elevation succeeds → invisible zombie process
- **Where:** `src/ScannerService.TrayApp/TrayApp.cs:753-764` (Dispose at 756, `Program.RestartAsAdministrator()` at 758), `Program.cs:39-62`
- **Rule:** destructive action before fallible operation
- **Detail:** Declining the UAC prompt (Win32Exception 1223 — a routine path; the menu item only exists on non-admin instances) returns false *after* `Dispose()` has already set `_isDisposed=true`, stopped the host, hidden the NotifyIcon, and flushed Serilog. No recovery code exists (the failure path is a bare comment), `Application.Exit()` only runs on success, and there is no MainForm — so `Application.Run` keeps a message loop alive with no icon and a stopped API. (Triple-verified by independent lenses.)
- **Fix:** Reorder — `if (Program.RestartAsAdministrator()) { Dispose(); Application.Exit(); }`. Success path byte-identical; failure path leaves the app fully alive.

### A-2 · HIGH · Same-day PDF / multi-page-TIFF scans merge into one phantom scan group
- **Where:** `src/ScannerService.Infrastructure/Services/RecentScansService.cs:299-308` (`ExtractScanId`), regex at 321 (`^(.+?)_\d+$`), grouping at 250-294
- **Rule:** scan-group id extraction vs `SaveAsync` file naming
- **Detail:** PDF (ScannerService.cs:435) and multi-page TIFF (:442) are saved **without** a page suffix; only the per-image branch (:454) appends `_{i+1}`. On read, the lazy regex strips one trailing `_digits` from `scan_20261003_142530` → base `scan_20261003`, consuming `HHmmss` as a phantom page index. Because PDF is the **default** format, 2+ same-day scans collapse into one bogus multi-page group (timestamp = oldest of day, pageCount = file count), displacing real groups from `Take(count)`. Second facet (verified): the grouping key ignores extension *and* directory, so `report_2026.pdf` and `report.pdf`, or same-name files in different subfolders, merge too.
- **Fix:** Only strip a suffix when the remainder still ends with the datetime token — e.g. `^(?<base>.+_\d{8}_\d{6})(?:_(?<page>\d{1,4}))?$`, return the full name when unmatched. Key grouping on (scanId, extension) at minimum.

### A-3 · HIGH · Validators accept BitDepth case-insensitively; `ParseBitDepth` is case-sensitive → silent Color scans
- **Where:** `src/ScannerService.Infrastructure/Services/ScannerService.cs:464-472` vs `UpsertProfileValidator.cs:58` / `UpdateProfileValidator.cs:69`
- **Rule:** validator-pipeline contract mismatch
- **Detail:** Both validators accept `{Color, Grayscale, BlackAndWhite}` under `StringComparer.OrdinalIgnoreCase`; the raw string is stored verbatim (Profile → ScanJobConfiguration → `ParseBitDepth`), whose constant-pattern switch is ordinal case-sensitive. `"grayscale"` → `_ => BitDepth.Color`, silently, with the debug log printing the already-parsed (wrong) enum. Every sibling parser (`ParsePaperSource`:476, format checks:433/440) uses `OrdinalIgnoreCase` — this is the lone deviator, on exactly the field that changes scan output.
- **Fix:** Make `ParseBitDepth` case-insensitive (mirror `ParsePaperSource`). Behavior-preserving for canonical inputs.

### A-4 · HIGH · Multi-page-TIFF temp BMPs accumulate for process lifetime; permanently orphaned after a crash
- **Where:** `ScannerService.cs:527-533` (`GetBitmapViaTempFile`), field at :31, sole cleanup in `DisposeAsync` :393-409
- **Rule:** temp-file lifetime / cleanup on failure paths
- **Detail:** Each page is materialized as an **uncompressed BMP** (25-100 MB/page at Color) in %TEMP%, tracked in an instance bag on a DI **singleton**, deleted only at host disposal. No startup sweep exists, so crash-killed processes orphan them forever.
- **Fix:** Delete each temp BMP in a `finally` after its bitmap's using scope (files unlock once the `Bitmap` is disposed); keep the DisposeAsync sweep as backstop. Optionally sweep stale `scan_*.bmp` at startup.

### A-5 · MEDIUM · Client-abort path skips consumer observation → device-gate race + completed-scan image leak
- **Where:** `ScannerService.cs:229-233` (abort catch does bare `throw;`) vs timeout catch :218-227 (does both cancel + `ObserveAbortedConsumerAsync`); gate release :135-138; image-ownership flag :191-202
- **Rule:** device-session lifecycle / disposal-invariant asymmetry
- **Detail:** Drivers have no native cancellation (the code says so), so after the abort rethrow the abandoned consumer can still be inside native scan I/O — yet the gate opens immediately and the next scan can reach the same device. And when the consumer *completed successfully* during the abort, the ownership flag skips disposal — leaking the whole `ProcessedImage` set, breaking the code's own "caller never leaks ProcessedImage" invariant. One fix closes both.
- **Fix:** In the abort catch, before `throw;`: `await watchdogCts.CancelAsync(); await ObserveAbortedConsumerAsync(consumeTask, images);` — reuses the existing 2s-bounded helper whose completed-branch disposes.

### A-6 · MEDIUM · Response ZIP orphaned (never swept) when zip creation fails or is cancelled
- **Where:** `src/ScannerService.Infrastructure/Services/ScanJobService.cs:213-230` (tracked only at 227-230, after the `await using`)
- **Rule:** track-at-creation for temp files
- **Detail:** `FileMode.CreateNew` creates the file at :217 but tracking happens only on success; client abort (token is wired to RequestAborted), disk-full, or a vanished source file leaves a partial zip that no sweep ever sees (the shutdown sweep iterates only the tracked set).
- **Fix:** Move `TempFilesToDelete.Add(zipPath)` before the creation block (matches the existing retry-sweep semantics; mirrors the bitmap tracking pattern).

### A-7 · MEDIUM · `ReadSidecarSnapshot` unguarded, called only from a catch-less fire-and-forget restart
- **Where:** `src/ScannerService.TrayApp/Configurations/LocalSettingsStore.cs:218-224`; `TrayApp.RestartHostAsync` :527-585 (fire-and-forget at :508)
- **Rule:** unguarded IO in fire-and-forget
- **Detail:** A transient IO lock (AV/indexer — a class of failure the codebase explicitly retries elsewhere in the same file) escapes both the snapshot read and the catch-less `RestartHostAsync`; no `UnobservedTaskException` handler exists repo-wide. Net effect: the settings PUT returned 200, the restart silently never happens. (Confirmed by three independent lenses.)
- **Fix:** Wrap the two snapshot reads in try/catch (log Warning, keep running host — next PUT retries), or add a top-level catch to `RestartHostAsync` that logs + posts the warning balloon. The log line is the essential part.

### A-8 · MEDIUM · No single-instance guard; port fallback masks a double launch
- **Where:** `src/ScannerService.TrayApp/Program.cs:11-22`; masking in `WebApiHostService.FindAvailablePortAsync` :471-514
- **Rule:** lifecycle guard absent
- **Detail:** Double launch deterministically produces a second full app on port+1 (two scanner stacks, SQLite contention, two tray icons), while the `AddressInUse` handler whose message says "close any other instance" is unreachable. `Installer.iss:19`'s `AppMutex` directive is inert — the app never creates that mutex.
- **Fix:** Named system Mutex (e.g. `Global\ResaaScannerService`) at the top of `Main`; show the existing Persian "already running" notification or exit silently.

### A-9 · LOW · Dispose coordination with in-flight scans (`Release()` on a disposed gate; double dispose of `ScannerInitializer`)
- **Where:** `ScannerService.cs:135-138` vs `DisposeAsync` :389-426; `ScannerInitializer` disposed at ScannerService.cs:420-23 **and** again by the container (WebApiHostService.cs:209-210)
- **Rule:** async-dispose correctness / dispose ownership
- **Detail:** Shutdown drain is bounded at 5s while scans run up to 600s, so dispose-under-scan is reachable; the unprotected `finally { _scanGate.Release(); }` then throws `ObjectDisposedException` (caught by the outer catch-all, so impact is a misleading error log — verified). `ScannerInitializer` is disposed twice on every exit/restart (both singletons, no idempotence guard).
- **Fix:** Guard `Release()` with try/catch (`ObjectDisposedException`) or a disposed flag; brief bounded wait for the gate in `DisposeAsync`. Delete the manual initializer-dispose block (the container owns it).

### A-10 · LOW · Four endpoints drop the abort signal
- **Where:** `src/ScannerService.TrayApp/Configurations/EndpointConfigurationExtensions.cs:134, 141, 190, 253` (GET profiles, GET profile by id, DELETE profile, GET export-settings)
- **Rule:** CancellationToken propagation
- **Detail:** Sibling endpoints all declare and forward `ct`; interfaces accept it with `= default` and implementations genuinely forward it to EF Core. One-line each.
- **Fix:** Add `CancellationToken ct` to those four lambdas and pass through.

### A-11 · LOW · Client cancellation converted into an Error-level "Scan failed" log + Failure result
- **Where:** `ScanJobService.cs:243-248` (catch-all)
- **Rule:** OCE swallowed by broad catch
- **Detail:** `ScannerService` deliberately rethrows client-abort OCE (:229-233); this is the one catch on that path without the `when (cancellationToken.IsCancellationRequested)` guard that exists in three sibling services. Pure log-fidelity fix; wire behavior unchanged (client is gone either way).
- **Fix:** `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` before the generic catch.

---

## 4. Category B — Concurrency & Async Hazards

### B-1 · MEDIUM · Start path has no reentrancy guard (two layers)
- **Where:** `TrayApp.StartService` :383-444 (the `Interlocked.CompareExchange(ref _isRestarting, 0, 0)` at :391/:401 is a **read**, not a claim — only restart claims the flag) and `WebApiHostService.StartAsync` :82-87 (`IsRunning=true` only at :338)
- **Detail:** The Start menu item stays enabled through a genuinely multi-second window (netsh firewall call with 10s bound + possible UAC prompt, port probe, `Build()`, `EnsureCreatedAsync`), so consecutive clicks run two concurrent `StartAsync` calls on one host instance.
- **Fix:** CAS an `_isHostTransitioning` flag 0→1 inside the `Task.Run` body (try/finally reset), mirroring the existing `_isRestarting` pattern. Optionally disable the Start item synchronously before `Task.Run`.

### B-2 · MEDIUM · `_runTask = _app.RunAsync(token)` is never awaited → bind failure unobserved, `IsRunning=true` for a dead host
- **Where:** `WebApiHostService.cs:337` (catches at :343-376)
- **Detail:** `RunAsync` is async — C# captures **all** exceptions (including Kestrel's synchronous bind failure) into the task, never throwing to the caller. Nothing before line 337 binds a socket, so the `AddressInUse` catch (whose message says "close any other instance") is unreachable and `IsRunning` flips true for a host that failed to listen. Self-corrects only via the tray health poll; the dedicated "started" log is wrong.
- **Fix:** `await _app.StartAsync(_cts.Token);` (throws on bind failure → existing catches + TrayApp rollback work) then `_runTask = _app.WaitForShutdownAsync(_cts.Token);`. Stop/Dispose behavior is identical.

### B-3 · LOW · Failed starts and failed restart rollbacks never dispose the built host/CTS
- **Where:** `WebApiHostService.cs:370-376` (catch → `StopAsync()`, which early-returns on `!IsRunning` :381-384 — false until :338, i.e. always on failure); `TrayApp.RollbackApiHostAsync` :589-615 never disposes `failedHost`
- **Fix:** In the catch, dispose directly (`if (_app != null) { await _app.DisposeAsync(); _app = null; }` + `_cts?.Dispose()`), or key StopAsync's gate on `_app`/`_runTask` instead of `IsRunning`; dispose `failedHost` in the rollback path. Pairs naturally with B-2.

### B-4 · LOW · `MaxRestartPasses` exhaustion is silent
- **Where:** `TrayApp.cs:529-579`
- **Detail:** Settings written during the final restart pass persist to the sidecar but never apply (no file watcher; the "applying once more" log's promise is false on the last pass) — applied only at next process start, with no log or balloon.
- **Fix:** After the loop, log a warning + post the existing warning balloon. Observability only.

### B-5 · LOW · `TrayApp.Dispose` does not fence an in-flight `RestartHostAsync`
- **Where:** `TrayApp.cs:180-216` vs `:520-585` (`_isDisposed` checked once at :503, never re-checked)
- **Detail:** A restart that passed its check can stop a host being disposed and build/start a brand-new host after exit began. (Downgraded by the impact lens — window is narrow — but the fence is three one-line checks.)
- **Fix:** Re-check `_isDisposed` after the `_isRestarting` CAS and at the top of each loop pass.

### B-6 · LOW · Rate-limit `AsyncLock` lifecycle: release-without-acquire + cleanup dispose race
- **Where:** `src/ScannerService.TrayApp/Middleware/RateLimitMiddleware.cs:43-47, 73-83, 88-99, 124-150`
- **Detail:** (1) If `AcquireAsync` throws (e.g. cancellation before a permit), the `finally` still calls `Release()` — injecting a phantom permit that later surfaces as `SemaphoreFullException`. (2) `CleanupExpiredLocks` can dispose a lock another request just obtained from `GetOrAdd` → sporadic 500s after idle periods. (3) See also SEC-1: the lock *key* is attacker-controlled (unbounded growth).
- **Fix:** Track acquisition (only Release when acquired); stop disposing removed locks (a SemaphoreSlim without waiters needs no deterministic dispose — GC reclaims).

### B-7 · NOTE · No global unhandled-exception safety net
- **Where:** repo-wide — zero `Application.ThreadException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` (grep-verified).
- **Detail:** WinForms' default UI-thread dialog covers menu handlers; the *actual* exposure is fire-and-forget paths (A-7 is the one confirmed instance). A one-time `Log.UnobservedTaskException`-style handler is optional hardening, not required.

---

## 5. Security

### S-1 · MEDIUM · Rate limiter keys on client-controlled `X-Forwarded-For` (no proxy exists)
- **Where:** `src/ScannerService.TrayApp/Middleware/RateLimitMiddleware.cs:101-111` (XFF branch at :104-107), keyed at :39-40
- **Detail:** Kestrel *is* the edge (no `UseForwardedHeaders` anywhere), so no legitimate traffic carries XFF; each unique header value gets a fresh 100-req/min bucket (bypass by rotation — a page can set XFF given the current CORS policy) and attacker-proportional `_ipLocks`/counter growth (purged only 1-in-100 requests, O(n) each). Matters exactly when the bind is widened beyond loopback (documented, opt-in, warned).
- **Fix:** Delete the XFF branch; key on `context.Connection.RemoteIpAddress` only. Behavior-identical for every current client.

### S-2 · MEDIUM · Default CORS policy reflects any origin with `AllowCredentials` on an unauthenticated API; `OriginChecker` written to restrict it is dead code
- **Where:** `WebApiHostService.cs:242-247` (+ `UseCors()` :321); `OriginChecker` :643-697 (zero call sites repo-wide; its doc comment implies validation that does not exist)
- **Detail:** Any web page can read responses cross-origin and preflight-approve PUT/DELETE. No consumer uses credentials, so `AllowCredentials` adds risk and nothing else.
- **Fix:** Minimum: drop `.AllowCredentials()`. Better: bind allowed origins from config. Delete `OriginChecker` (with the dead-code batch) and leave a one-line comment that allow-all is deliberate.

### S-3 · LOW · Raw `ex.Message` returned to API clients
- **Where:** Result errors interpolate exception text at ~8 sites (`ScanJobService.cs:247`, `ScannerService.cs:245/258`, `ExportSettingRepository.cs:57/85`, `LocalSettingsStore.cs:129/166/202`) and endpoints return it verbatim (`EndpointConfigurationExtensions.cs:218/255/273`)
- **Detail:** Leaks internal paths, SQLite constraint text, driver internals — meaningful only when the bind is widened. No stable error codes exist for clients.
- **Fix:** Map known exception classes to friendly Persian messages; log the full exception server-side. Defer-able; do alongside F-9 (duplicate-name 500) which is the visible instance.

---

## 6. Architecture & Contracts

### AR-1 · MEDIUM · `GET /api/health/detailed` never returns 503 — `TypedResults.Ok` overwrites the manual status
- **Where:** `src/ScannerService.TrayApp/Configurations/EndpointConfigurationExtensions.cs:92-93` (`.Produces(503)` metadata at :98)
- **Detail:** The handler sets `Response.StatusCode = 503` then returns `TypedResults.Ok(result)`; ASP.NET Core's `Ok<T>.ExecuteAsync` unconditionally assigns `StatusCode=200` before writing the body (verified against the 10.0 source path by two independent agents). The documented 503 contract is never met.
- **Fix:** `return TypedResults.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);` in the unhealthy branch.

### AR-2 · MEDIUM · Dead config keys, one documented by the README
- **Where:** `src/ScannerService.TrayApp/appsettings.json:21` (`DatabasePath`), `:23-27` (`Logging:LogLevel`); `WebApiHostService.cs:159` hardcodes `scanner.db`; Serilog level hard-coded (`SerilogConfigurationExtensions.cs:40`)
- **Detail:** `DatabasePath` binds to nothing (and README:415 documents it as configurable — F-18); `Logging:LogLevel` binds to nothing (only the `File` model is read).
- **Fix:** Delete both from appsettings.json + the README line. (Wiring them is the alternative; removal is the KISS fix.)

### AR-3 · MEDIUM · Same-second scan output overwrite is silent data loss
- **Where:** `ScannerService.SaveAsync` :431-455 — `{datetime}` = `yyyyMMdd_HHmmss`, `File.Exists` never checked (spot-verified)
- **Detail:** Two scans of the same profile within one second (rapid rescan, client retry) silently overwrite the earlier PDF/TIFF of scanned documents.
- **Fix:** Uniquify on collision (`name_2`, `_3`, …) before the first write — behavior-preserving in every non-collision case.

### AR-4 · LOW · Timestamp semantics inconsistent across the scan pipeline
- **Where:** output filenames use `DateTime.Now` (`ScannerService.cs:431`), ZIP names use `DateTime.UtcNow` (`ScanJobService.cs:233`), grouping uses `CreationTimeUtc` (`RecentScansService.cs:170`)
- **Fix:** Pick one policy for user-facing names (local is correct for a single-timezone desktop app — verified) and one for internal comparisons; make the ZIP name match the file names. Decide before touching A-2/B-2 code.

---

## 7. Category C — Structural Smells & Dead Code

### C-1 · MEDIUM · Validator contract gaps (batch)
- **`ExportSettingValidator.cs:20`** — `PUT /api/export-settings` with `"fileName": null` → NRE inside the `Must` predicate → **500** (STJ does not enforce non-nullable annotations; `RespectNullableAnnotations` is not enabled anywhere).
- **`ScannerSettingsValidator.cs:29`** — `PUT /api/settings` with `"esclManualDevices": null` → same NRE→500, contradicting the validator's own NRE-avoidance design comment.
- **`UpsertProfileValidator.cs`** — no `DeviceId` rule (the PATCH validator has one at `UpdateProfileValidator.cs:17-19`), so `POST /api/profiles` with `deviceId: ""` → **201** with a profile that can never scan.
- **`ScanRequestValidator.cs:15-17`** — checks only `ProfileId`; `Format` is unvalidated, so an arbitrary request string becomes the output **file extension**, bypassing the whitelist the settings path enforces.
- **Fix (one batch):** null-tolerant `Must` predicates (or `NotNull` + `When`/`DependentRules`); add the `DeviceId` rule to Upsert; add the Format whitelist rule reusing `ExportSettingValidator`'s set.

### C-2 · LOW · Allowed-value HashSets + messages duplicated between the two profile validators
- **Where:** `UpdateProfileValidator.cs:68-72` ≡ `UpsertProfileValidator.cs:56-64` (five sets + five messages verbatim; `ScannerConstants` already defines the canonical values)
- **Why it matters:** this duplication is exactly what bred A-3 (casing drift).
- **Fix:** One internal static class in Application referencing `ScannerConstants`.

### C-3 · LOW · Dead-code inventory (delete-only batch, ~600 lines, all grep-verified zero-references)
| Item | Where |
|---|---|
| `ITimeProvider` / `SystemTimeProvider` / `TestTimeProvider` (test double for a test project that does not exist; production code calls `DateTime.UtcNow` directly in 31 sites) | `Application/Common/ITimeProvider.cs` (whole file) |
| `ResultExtensions` (Map/Bind/Tap/…) + `Result`'s implicit bool operator | `Application/Common/Result.cs:67, 73-161` |
| `RepositoryBase` unused helpers (`EntityExistsAsync`, both `GetByIdAsync` overloads, `ExistsAsync`, `CountAsync`, `ListAsync`, `PaginatedAsync`, `AddAsync(T)`, `AddRangeAsync`, `Remove`, `RemoveRange`, `SaveChangesAsync`) + `PaginatedResult<T>`; latent Skip/Take bugs die with it | `Infrastructure/Repositories/RepositoryBase.cs:27-174` |
| `ProfileRepository.GetByIdAsync` `new`-shadowing an unused base member | `ProfileRepository.cs:25` |
| `DependencyHealthDto` | `Application/DTOs/DependencyHealthDto.cs:6` |
| `OriginChecker` (69 lines; misleads about the real CORS policy — see S-2) | `WebApiHostService.cs:643-697` |
| Duplicate `CreateEmptyOverrides` (private, zero refs; canonical one lives in `LocalSettingsStore`) | `EndpointConfigurationExtensions.cs:413-417` |
| `ThrowIfInvalid` extension (zero refs) | `ConfigurationValidator.cs:256` |
| `CleanupThreshold`, `ScannerConstants.Scale` / `.Driver`, one unused `AddHttpClient()` registration | per F-42 evidence |
| Debug request log that can never emit (sink min level hard-coded Information) | `WebApiHostService.cs:300` vs `SerilogConfigurationExtensions.cs:40` — delete the line |

### C-4 · LOW · The two firewall helpers are ~130 lines of copy-paste
- **Where:** `NetworkDiscoveryFirewall.cs:82-156` ≡ `ApiListenerFirewall.cs:103-180` (four methods character-identical)
- **Fix:** Extract one internal `NetshFirewallRule` helper (rule name/protocol/port as parameters); the two facades keep their signatures and log wording. Do it together with any other firewall touch so both files move once.

### C-5 · LOW · Documented-behavior mismatches (docs batch)
- `ScannerSettingsValidator.cs:12-13` — doc promises "missing numeric → 0 → rejected"; reality is silent default substitution (DTO initializers, e.g. `= 600000`). Fix the comment.
- `ProfileDto.cs:37` — doc points to a nonexistent `PUT /api/profiles/{id}`; also no API path can clear a profile's `DeviceId`. Fix the doc.
- `Profile.PageSize/HorizontalAlign/Scale` — stored, validated, echoed, but **never consumed** by the scan pipeline (`ScanJobConfiguration` has no such members). Document as UI-only/not-yet-wired; do not remove.
- README accuracy debt: stale package versions (claims EF Core 8 / NAPS2 1.2.1 / C# 12; actual EF 10.0.12 / NAPS2 1.4.0 / C# 14), stale `dotnet test` section, wrong HTTP verb (PUT→PATCH), 3 missing endpoints, wrong file tree, nonexistent auto-elevation claim.

---

## 8. Standards & Tooling (STD)

| # | Sev | Finding | Fix |
|---|-----|---------|-----|
| F-1 | LOW | `Directory.Build.props:19` — global `NoWarn CA1873` with **no justifying comment** (violates the repo's own suppression convention) | Add the one-line reason, or scope the suppression to the offending call sites |
| F-20 | LOW | `.editorconfig:406-407` — comment says "S2094: Utility classes…" above `S1118.severity = none` (copy-paste label) | Fix the comment |
| F-22 | LOW | Sole repo-declared P/Invoke is classic `[DllImport]` (`TrayApp.cs:26-28`, `DestroyIcon`) — the brief's .NET-10 rule says `[LibraryImport]` (SYSLIB1054 is suggestion-level, so the strict build doesn't catch it) | Make `TrayApp` partial; mechanical swap; keep `DefaultDllImportSearchPaths` |
| F-23 | LOW | Documented "git unavailable" version fallback actually fails: MSBuild `Exec` with `ConsoleToMSBuild` captures **stderr into ConsoleOutput**, so git's error text becomes the version string | Capture `ExitCode` and gate on `!= '' AND exit == 0` |
| F-24 | LOW | Duplicate profile name hits the unique index → raw 500 | Pre-check + friendly failure (or catch `DbUpdateException`); endpoint maps to 400/409 |
| F-25 | LOW | Installer uninstall: "remove all user data (scans, database, logs)" only `DelTree({app})`, but scans default to `%USERPROFILE%\Documents\Scans` (`ScanJobService.cs:165`) — user believes documents were deleted; they remain (spot-verified) | Delete the scans folder too when Yes is chosen (with an explicit path in the prompt text) |
| F-26 | LOW | Installer `IsAppRunning` matches a **generic WinForms window class** present in any same-runtime app, and `KillApp` ignores taskkill's ResultCode → false "still running" gates and failed force-kills treated as success | Match by exe name/path; honor ResultCode |
| F-27 | LOW | Log-content policy undecided: user-chosen absolute paths (`ExportPath`/`OutputPath`) logged at Information into `{app}\logs` | Decide + document what may appear in logs; for a document-scanning app this is worth one sentence in CLAUDE.md |

## 9. Brief-vs-reality checklist verdicts (the brief's .NET-10 standards)

| Brief item | Verdict |
|---|---|
| Zero active Sonar violations, `TreatWarningsAsErrors` passes | **Already met** (build green under `AnalysisMode=All`); remaining work = the 2 suppression-hygiene findings |
| `IDisposable`/`IAsyncDisposable`/SafeHandle for scanner handles | Ownership patterns verified sound in the main paths; **SafeHandle not applicable** — NAPS2.Sdk owns TWAIN/WIA/eSCL handles (the brief's own no-over-engineering rule wins) |
| `[LibraryImport]` | One finding (F-22); NAPS2-internal P/Invokes out of scope |
| Span/ArrayPool/MemoryPool, zero-alloc hot paths | **Not applicable** — code never touches raw pixel buffers; page loop runs at human rates |
| async Task everywhere / no blocking | **Already met** — zero `async void`; the one sync-over-async is the deliberate bounded `Dispose` (documented S8949) |
| `Channel<T>` pipelines | **Not applicable** — request/response scan flow, no capture→process pipeline exists |
| Result pattern for expected hardware conditions | **Already implemented** (`Result`/`Result<T>`, invariants verified); `ResultExtensions` dead → delete |
| LoggerMessage source generators | **Not justified** — logging is already structured templates + CompositeFormat; Information-level 7×10MB file sink |
| Polly resilience pipelines | **Not applicable** — `DriverHealthTracker` already implements bounded exponential cool-downs (verified correct) |
| CancellationToken everywhere | Nearly complete; gaps are A-10/A-11 + the two examined-and-accepted ones (SaveAsync token-free is arguably correct for a scan appliance) |
| xUnit tests for every changed module | **Gap — no test projects exist** (documented in CLAUDE.md). Decision needed for Phase 2 (see §12) |

## 10. Reviewed and declined (verified non-issues — do not "fix")

- **`EncoderParameter` disposal in `SaveMultiPageTiffAsync`** — pattern is real but the leak claim is false: `EncoderParameter` has a finalizer that reclaims the ~8-byte HGlobal block; the reassign pattern is Microsoft's own documented canonical example. No action.
- **Tray-icon rebuild every 5s tick** — facts right, harm premise wrong for WinForms (`NotifyIcon.Icon` setter early-outs; GDI churn at 0.2 Hz is negligible). No action.
- **`CachedScannerService` generation check-to-Set race vs `ClearScannerListCache`** — mechanics real, worst case is a ≤1-TTL (30s) stale list, self-healing, refresh endpoint exists. Accepted.
- **ExportSetting default-creation race** — unreachable: `HasData` seeds row Id=1 at DB creation; no delete endpoint.
- Refuted by verification (7): export-settings server-faults-as-400 (fix disproven), `ScanningContext` leak on failed init (finalizer covers), Serilog logger-swap "leak" (`FileShare.ReadWrite`; GC reclaims; no functional impact), firewall delete-before-add refresh (diagnosis right, proposed fix wrong — delete-then-add is the *working* idempotent path for netsh), sidecar write durability (fsync-level durability overkill for a settings sidecar), restart-vs-Start/Stop TOCTOU remnant (real mechanics, accepted within the documented restart design), UAC prompt killed by the 10s netsh timeout (prompt completes before `WaitForExit` is ever called).

## 11. Proposed Phase 2 sequence (minimize regression risk)

**Batch 0 — Test foundation (decision point).** The brief mandates xUnit tests per changed module; the repo has none. Recommend: add `tests/ScannerService.UnitTests` (xUnit + FluentAssertions or plain xUnit asserts) covering the pure logic the fixes touch — `ExtractScanId` regex, `ParseBitDepth`, validators (FluentValidation.TestHelper), `ScannerSettingsMapper`, `ScannerServiceConfiguration` defaults. Hardware paths stay behind existing interfaces with in-memory fakes. **Needs your call** — it adds a project to a repo whose convention is "no tests".

**Batch 1 — High-severity A, small and isolated (lowest risk, highest value).** A-1 zombie reorder → A-3 ParseBitDepth casing → A-2 grouping regex → A-4 temp-BMP cleanup. Each is behavior-preserving, unit-testable, and none touches hardware code paths.

**Batch 2 — Failure-path temp files & abort path.** A-6 zip track-before-create → A-5 abort-catch observation (fixes device-gate race *and* image leak with one call) → A-11 OCE rethrow → A-10 ct forwarding → AR-3 collision-safe naming (+ AR-4 timestamp policy decision).

**Batch 3 — Host lifecycle (highest-churn files — build evidence first, smoke-test via EsclDeviceSimulator).** B-2 await `StartAsync` → B-3 failed-start disposal → B-1 start reentrancy flag → A-7 restart catch + snapshot guard → B-4 exhaustion log → B-5 disposed-fence → A-9 dispose coordination → A-8 single-instance mutex.

**Batch 4 — Security & contracts.** S-1 XFF → S-2 CORS minimum → AR-1 health 503 → C-1 validator batch → F-24 duplicate-name 400.

**Batch 5 — Dead code & docs.** C-3 inventory (one delete-only commit) → C-2 validator-set dedup → C-4 firewall helper extraction → C-5 + F-1/F-20/F-22/F-23/F-25/F-26/F-27 docs & tooling.

Each batch: `dotnet build` at the 0-warning bar + targeted tests; runtime smoke for endpoint-visible changes via the dev instance (the tray app must be stopped before building — MSB3027 file-lock gotcha).

## 12. Decisions needed before Phase 2

1. **Test project:** create `ScannerService.UnitTests` (recommended) or proceed test-less and rely on the strict build + runtime smoke?
2. **Scope:** all five batches, or a subset (e.g. A+B only)?
3. **Behavior-visible fixes** to confirm explicitly, since they change observable outputs: AR-1 (503 now actually returned), C-1 (nulls now 400; `deviceId:""` now rejected on POST), A-2/A-3 (scan output/grouping changes), AR-3 (collision suffix).
