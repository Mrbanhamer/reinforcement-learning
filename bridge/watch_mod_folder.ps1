$folder = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\DefectAI'
$doc = Join-Path $env:USERPROFILE 'Documents'
$seen = @{}
Write-Host "Watching for sts2_state*.json in:`n  $folder`n  $doc"
while ($true) {
    foreach ($path in @($folder, $doc)) {
        if (-not (Test-Path $path)) { continue }
        Get-ChildItem -Path $path -Filter 'sts2_state*.json' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            if (-not $seen.ContainsKey($_.FullName)) {
                $seen[$_.FullName] = $true
                Write-Host "FOUND: $($_.FullName)"
                Write-Host "LASTWRITE: $($_.LastWriteTime)"
                try { Get-Content $_.FullName -Tail 100 | Write-Host } catch {}
                exit 0
            }
        }
    }
    Start-Sleep -Seconds 1
}
