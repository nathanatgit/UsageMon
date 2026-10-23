$ErrorActionPreference = 'Stop'
try {
    $incoming = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $limits = [ordered]@{}
    foreach ($name in @('five_hour', 'seven_day', 'spend_limit')) {
        $window = $incoming.rate_limits.$name
        if ($null -ne $window.used_percentage) {
            $limits[$name] = @{ used_percentage = $window.used_percentage; resets_at = $window.resets_at }
        }
    }
    $folder = Join-Path $env:LOCALAPPDATA 'UsageMon'
    [System.IO.Directory]::CreateDirectory($folder) | Out-Null
    $snapshot = @{ captured_at = [DateTimeOffset]::UtcNow.ToString('o'); rate_limits = $limits }
    $destination = Join-Path $folder 'claude-statusline.json'
    $temporary = Join-Path $folder (([Guid]::NewGuid().ToString('N')) + '.tmp')
    [System.IO.File]::WriteAllText($temporary, ($snapshot | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding $false))
    if ([System.IO.File]::Exists($destination)) { [System.IO.File]::Replace($temporary, $destination, [NullString]::Value) }
    else { [System.IO.File]::Move($temporary, $destination) }
    $parts = @()
    if ($limits.Contains('five_hour')) { $parts += '5h {0:0}%' -f $limits.five_hour.used_percentage }
    if ($limits.Contains('seven_day')) { $parts += '7d {0:0}%' -f $limits.seven_day.used_percentage }
    if ($parts.Count -gt 0) { [Console]::Out.WriteLine(($parts -join ' | ')) }
    else { [Console]::Out.WriteLine('UsageMon: quota not supplied') }
} catch {
    # Never print stdin, credentials, session text, or raw errors into Claude's status line.
    [Console]::Out.WriteLine('UsageMon: usage unavailable')
} finally {
    if ($temporary -and [System.IO.File]::Exists($temporary)) { [System.IO.File]::Delete($temporary) }
}
