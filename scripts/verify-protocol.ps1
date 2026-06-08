param(
    [Parameter(Mandatory = $true)]
    [string]$ArchiveUrl,
    [string]$ModType = "Character",
    [int]$ModId = 0,
    [string]$ExePath = ""
)

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $root "bin\Release\net8.0-windows\SSF2ModManager.exe"
    if (-not (Test-Path $ExePath)) {
        $ExePath = Join-Path $root "bin\Debug\net8.0-windows\SSF2ModManager.exe"
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Build the app first: dotnet build -c Release"
    exit 1
}

Write-Host "Executable: $ExePath"
Write-Host "Registering ssf2mm: protocol..."
& $ExePath --register-protocol --start-hidden
Start-Sleep -Seconds 1

& $ExePath --diagnostics

$url = if ($ModId -gt 0) { "ssf2mm:$ArchiveUrl,$ModType,$ModId" } else { "ssf2mm:$ArchiveUrl,$ModType" }
Write-Host ""
Write-Host "Launching test URL:"
Write-Host $url
Start-Process $ExePath -ArgumentList "`"$url`""
