$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$testDirectory = Join-Path $workspace '.tools/bridge-test'
New-Item -ItemType Directory -Force $testDirectory | Out-Null
$startInfo = [System.Diagnostics.ProcessStartInfo]::new('powershell.exe')
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.Environment['LOCALAPPDATA'] = $testDirectory
foreach ($argument in @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $workspace 'Integrations/claude-statusline.ps1'))) {
    $startInfo.ArgumentList.Add($argument)
}
$bridge = [System.Diagnostics.Process]::Start($startInfo)
try {
    $bridge.StandardInput.WriteLine('{"rate_limits":{"five_hour":{"used_percentage":0,"resets_at":1900000000},"seven_day":{"used_percentage":12.5,"resets_at":1900000100}},"session_id":"must-not-be-copied","transcript_path":"must-not-be-copied","prompt":"must-not-be-copied"}')
    $bridge.StandardInput.Close()
    $output = $bridge.StandardOutput.ReadToEnd()
    $errorOutput = $bridge.StandardError.ReadToEnd()
    if (-not $bridge.WaitForExit(10000)) { $bridge.Kill($true); throw 'Bridge timed out.' }
    if ($bridge.ExitCode -ne 0 -or $errorOutput) { throw 'Bridge process failed.' }
    $snapshotText = Get-Content (Join-Path $testDirectory 'UsageMon/claude-statusline.json') -Raw
    $snapshot = $snapshotText | ConvertFrom-Json
    if ($snapshot.rate_limits.five_hour.used_percentage -ne 0 -or $snapshot.rate_limits.seven_day.used_percentage -ne 12.5) { throw 'Bridge changed usage values.' }
    if ($snapshotText.Contains('must-not-be-copied') -or $snapshotText.Contains('session_id') -or $snapshotText.Contains('transcript_path')) { throw 'Bridge copied non-usage fields.' }
    if ([DateTimeOffset]::Parse($snapshot.captured_at) -lt [DateTimeOffset]::UtcNow.AddSeconds(-15)) { throw 'Bridge did not replace its previous snapshot.' }
    if ($output -notmatch '5h 0%') { throw 'Bridge status line did not render zero correctly.' }
    'Passed Claude bridge execution, quota capture, zero usage, and private-field filtering checks.'
} finally { $bridge.Dispose() }
