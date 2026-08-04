# Fixes WinUI DisableXbfGeneration output that emits invalid closings like:
#   </StackPanel x:ConnectionId='10'>
param(
    [Parameter(Mandatory = $true)]
    [string] $RootList
)

$pattern = [regex]'</([A-Za-z_][\w.]*)(\s+[^>]+)>'
$fixed = 0
$roots = $RootList -split ';' | Where-Object { $_ -and $_.Trim() }

foreach ($root in $roots) {
    $root = $root.Trim().Trim('"')
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Host "Skip missing root: $root"
        continue
    }

    Get-ChildItem -LiteralPath $root -Filter *.xaml -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Microsoft\.UI(\.|\\)' } |
        ForEach-Object {
            $text = [System.IO.File]::ReadAllText($_.FullName)
            $updated = $pattern.Replace($text, '</$1>')
            if ($updated -ne $text) {
                $utf8Bom = New-Object System.Text.UTF8Encoding $true
                [System.IO.File]::WriteAllText($_.FullName, $updated, $utf8Bom)
                $fixed++
                Write-Host "Sanitized $($_.Name)"
            }
        }
}

Write-Host "SanitizeLooseXaml fixed=$fixed"
