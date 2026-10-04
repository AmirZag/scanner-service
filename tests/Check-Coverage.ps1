# Asserts 100% line coverage for every production module in the latest coverage run, except a
# short, explicit allowlist of lines that are unreachable in automated tests. Any uncovered line
# NOT on the allowlist fails the gate; any allowlisted line that becomes covered is reported so
# stale entries can be pruned.
#
# Usage:
#   dotnet test tests/ScannerService.UnitTests --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
#   powershell -NoProfile -File tests/Check-Coverage.ps1

$ErrorActionPreference = 'Stop'

# Lines that cannot execute under automated tests. Keys are module name -> file name suffix;
# each entry lists the uncovered line numbers and the reason. Keep this list SHORT and honest -
# every entry here is a line the suite does not exercise.
$allowlist = @{
    'ScannerService.Infrastructure.dll' = @{
        'ScannerDriverFactory.cs'  = @{ Lines = @(25, 26, 27, 28, 29, 30, 31, 32); Reason = 'Linux/OSX platform branches; runtime OS-gated and the test TFM is net10.0-windows' }
        'RecentScansService.cs'    = @{ Lines = @(155, 156, 157, 173, 174, 175, 176, 197, 198, 199); Reason = '155-157 dead defensive guard (enumeration pre-caps at MaxFiles); 173-176 and 197-199 TOCTOU/mid-walk-cancellation catches with no deterministic seam' }
        'ScanJobService.cs'        = @{ Lines = @(64, 65, 66, 67, 68, 299, 300, 301, 306, 307, 308); Reason = 'TOCTOU sweep catch (delete between enumeration and stat), an OS-level write-failure catch-all, and the ArgumentException catch shadowed by the earlier explicit invalid-character guard (dead-defensive, Batch-5 candidate); no hermetic trigger' }
        'ScannerInitializer.cs'    = @{ Lines = @(94, 95); Reason = 'In-lock double-check after the wait: only reachable in the race window where initialization completes while a second caller waits on the lock' }
        'ScannerService.cs'        = @{ Lines = @(114, 115, 116, 117, 118, 420, 421, 422, 423); Reason = '114-118 BuildManualEsclScanDevices: its only caller is the hardware-excluded FindDeviceAsync; 420-423 defensive catch around SemaphoreSlim.Dispose, which no reachable state makes throw' }
    }
    'ScannerService.TrayApp.dll' = @{
        'ApiListenerFirewall.cs'        = @{ Lines = @(119, 156, 157, 158, 178, 179, 180); Reason = '119: fires only when netsh add fails (covered on unelevated hosts; this automation host runs elevated); 156-158 netsh-hang kill branch; 178-180 elevate branch is only reachable from the excluded AddRuleElevated' }
        'NetworkDiscoveryFirewall.cs'   = @{ Lines = @(98, 132, 133, 134, 154, 155, 156); Reason = 'Same shape as ApiListenerFirewall: netsh-failure throw (environment-dependent), hang-kill branch, elevate-only branch' }
        'LocalSettingsStore.cs'         = @{ Lines = @(210); Reason = 'Structurally unreachable post-loop return: every loop iteration returns Success, Failure, or propagates' }
        'WebApiHostService.cs'          = @{ Lines = @(344, 345, 346, 347, 348, 349, 351, 352, 353, 354, 355, 356, 443, 444, 445, 446, 447, 474, 475, 476, 498, 499, 501); Reason = '344-356 bind-failure catch: unreachable per Phase 1 audit B-2 (RunAsync is not awaited) until that lifecycle fix lands, then cover it and remove this entry; 443-447 outer catch around the bounded stop has no hermetic trigger; 474-476 Dispose bounded-timeout branch is structurally unreachable (3x bound vs 1x internal wait, same config); 498-501 catch around the OS TCP-listener enumerator has no deterministic trigger' }
    }
}

$runRoot = Join-Path $PSScriptRoot 'ScannerService.UnitTests\TestResults'
if (-not (Test-Path $runRoot)) {
    Write-Error "No TestResults found under $runRoot. Run the coverage command first."
    exit 1
}

$runDirs = Get-ChildItem -Path $runRoot -Directory | Sort-Object LastWriteTime -Descending
$coverageFiles = @()
foreach ($dir in $runDirs) {
    $coverageFiles = Get-ChildItem -Path $dir.FullName -Filter 'coverage.json' -ErrorAction SilentlyContinue
    if ($coverageFiles.Count -gt 0) { break }
}
if ($coverageFiles.Count -eq 0) {
    Write-Error 'No coverage.json found in the latest TestResults run. Was the coverage.runsettings collector active?'
    exit 1
}

# Coverlet's JSON schema: module -> document -> class -> method -> Lines { lineNumber: hitCount }.
$modules = @{}
foreach ($file in $coverageFiles) {
    $data = Get-Content -Raw $file.FullName -Encoding UTF8 | ConvertFrom-Json
    foreach ($module in $data.PSObject.Properties) {
        $moduleName = $module.Name
        if (-not $modules.ContainsKey($moduleName)) {
            $modules[$moduleName] = @{}
        }
        $documents = $modules[$moduleName]
        foreach ($document in $module.Value.PSObject.Properties) {
            $fileName = [System.IO.Path]::GetFileName($document.Name)
            if (-not $documents.ContainsKey($fileName)) {
                $documents[$fileName] = @{ Total = 0; Covered = 0; Uncovered = @() }
            }
            $stats = $documents[$fileName]
            foreach ($class in $document.Value.PSObject.Properties) {
                foreach ($method in $class.Value.PSObject.Properties) {
                    $lines = $method.Value.Lines
                    if ($null -eq $lines) { continue }
                    foreach ($line in $lines.PSObject.Properties) {
                        $stats.Total++
                        if ([int]$line.Value -gt 0) {
                            $stats.Covered++
                        }
                        else {
                            $stats.Uncovered += [int]$line.Name
                        }
                    }
                }
            }
        }
    }
}

$failed = $false
$allowedTotal = 0
foreach ($moduleEntry in $modules.GetEnumerator() | Sort-Object Name) {
    $moduleName = $moduleEntry.Name
    $moduleTotal = 0
    $moduleCovered = 0
    $staleAllowlist = @()
    $unexpected = @()
    $allowedHere = 0

    foreach ($fileEntry in $moduleEntry.Value.GetEnumerator() | Sort-Object Name) {
        $fileName = $fileEntry.Name
        $stats = $fileEntry.Value
        $moduleTotal += $stats.Total
        $moduleCovered += $stats.Covered

        $fileAllowRules = $null
        if ($allowlist.ContainsKey($moduleName) -and $allowlist[$moduleName].ContainsKey($fileName)) {
            $fileAllowRules = $allowlist[$moduleName][$fileName]
        }

        foreach ($line in $stats.Uncovered) {
            if ($fileAllowRules -and ($fileAllowRules.Lines -contains $line)) {
                $allowedHere++
            }
            else {
                $unexpected += "$fileName`:$line"
            }
        }

        if ($fileAllowRules) {
            foreach ($allowedLine in $fileAllowRules.Lines) {
                if ($stats.Uncovered -notcontains $allowedLine) {
                    $staleAllowlist += "$fileName`:$allowedLine"
                }
            }
        }
    }
    $allowedTotal += $allowedHere

    if ($moduleTotal -eq 0) {
        Write-Output ("[FAIL] {0,-50} no instrumented lines found" -f $moduleName)
        $failed = $true
        continue
    }

    $effectiveTotal = $moduleTotal - $allowedHere
    $percent = if ($effectiveTotal -gt 0) { 100.0 * $moduleCovered / $effectiveTotal } else { 100.0 }
    $ok = ($unexpected.Count -eq 0)
    if (-not $ok) { $failed = $true }
    $tag = if ($ok) { 'OK  ' } else { 'FAIL' }
    Write-Output ("[{0}] {1,-50} line coverage = {2:N2}%  ({3}/{4} lines; {5} documented exclusions)" -f $tag, $moduleName, $percent, $moduleCovered, $effectiveTotal, $allowedHere)

    if ($unexpected.Count -gt 0) {
        Write-Output ("       uncovered lines not on the allowlist: {0}" -f ($unexpected -join ', '))
    }
    if ($staleAllowlist.Count -gt 0) {
        Write-Output ("       NOTE: allowlist entries now covered (prune them): {0}" -f ($staleAllowlist -join ', '))
    }
}

Write-Output ("Total documented exclusions: {0}" -f $allowedTotal)

if ($failed) {
    Write-Error 'Line coverage gate failed: uncovered lines outside the documented allowlist.'
    exit 1
}

Write-Output 'Coverage gate passed: 100% of coverable lines, minus the documented allowlist.'
exit 0
