$paths = @(
    "$env:USERPROFILE\Documents",
    "E:\\SteamLibrary\\steamapps\\common\\Slay the Spire 2",
    "E:\\SteamLibrary\\steamapps\\common\\Slay the Spire 2\\mods\\DefectAI",
    "$env:APPDATA",
    "$env:LOCALAPPDATA"
)
$filter = 'sts2_state*.json'
$watchers = @()

foreach ($p in $paths) {
    if (-not (Test-Path $p)) { continue }
    try {
        $fsw = New-Object System.IO.FileSystemWatcher $p, $filter -Property @{IncludeSubdirectories=$true; EnableRaisingEvents=$true}
        Register-ObjectEvent $fsw Created -Action {
            $path = $Event.SourceEventArgs.FullPath
            Write-Host "FOUND: $path"
            try { Get-Content $path -Tail 200 | Write-Host } catch {}
            exit 0
        } | Out-Null
        $watchers += $fsw
    } catch {}
}
Write-Host 'Watching for sts2_state*.json in common locations. Trigger an in-game action now to create the file.'
while ($true) { Start-Sleep -Seconds 1 }
