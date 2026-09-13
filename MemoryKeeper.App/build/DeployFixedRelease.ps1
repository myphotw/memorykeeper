[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string] $DestinationDirectory
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$appRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$sourcePath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourceDirectory).Path)
$destinationPath = [System.IO.Path]::GetFullPath($DestinationDirectory)
$expectedDestination = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "Release"))
$comparison = [System.StringComparison]::OrdinalIgnoreCase

if (-not $sourcePath.StartsWith($appRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "Fixed Release source must remain under the MemoryKeeper.App directory."
}

if (-not $destinationPath.Equals($expectedDestination, $comparison)) {
    throw "Fixed Release destination must be the repository Release directory."
}

if ($sourcePath.Equals($destinationPath, $comparison)) {
    throw "Fixed Release source and destination must be different directories."
}

$operationId = [Guid]::NewGuid().ToString("N")
$operationRoot = [System.IO.Path]::GetFullPath((Join-Path $appRoot "obj\FixedRelease\$operationId"))
$stagingPath = Join-Path $operationRoot "staging"
$backupPath = Join-Path $operationRoot "backup"
$backupMustBePreserved = $false
$hadExistingRelease = Test-Path -LiteralPath $destinationPath -PathType Container

New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $sourcePath -Force |
        Where-Object {
            $_.Name -ne "Diagnostics" -and
            $_.Name -notlike "*.WebView2"
        } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $stagingPath -Recurse -Force
        }

    $requiredPaths = @(
        "MemoryKeeper.exe",
        "MemoryKeeper.dll",
        "MemoryKeeper.deps.json",
        "MemoryKeeper.runtimeconfig.json",
        "resources.pri",
        "App.xaml",
        "appsettings.json",
        "Microsoft.ui.xaml.dll",
        "WebView2Loader.dll",
        "Views"
    )
    $missingPaths = @(
        $requiredPaths | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $stagingPath $_))
        }
    )
    if ($missingPaths.Count -gt 0) {
        throw "Fixed Release staging is incomplete: $($missingPaths -join ', ')"
    }

    if ($hadExistingRelease) {
        Move-Item -LiteralPath $destinationPath -Destination $backupPath
    }

    try {
        Move-Item -LiteralPath $stagingPath -Destination $destinationPath
    }
    catch {
        if ($hadExistingRelease -and
            -not (Test-Path -LiteralPath $destinationPath) -and
            (Test-Path -LiteralPath $backupPath -PathType Container)) {
            try {
                Move-Item -LiteralPath $backupPath -Destination $destinationPath
            }
            catch {
                $backupMustBePreserved = $true
                throw "Fixed Release deployment failed and its previous output remains at '$backupPath'."
            }
        }

        throw
    }

    if (Test-Path -LiteralPath $backupPath -PathType Container) {
        Remove-Item -LiteralPath $backupPath -Recurse -Force
    }

    Write-Host "Fixed Release deployed: $destinationPath"
}
finally {
    if (-not $backupMustBePreserved -and
        (Test-Path -LiteralPath $operationRoot) -and
        $operationRoot.StartsWith($appRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
        Remove-Item -LiteralPath $operationRoot -Recurse -Force
    }
}
