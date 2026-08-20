[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$localConfigPath = Join-Path $projectRoot "config\deployment.env"
$previousValue = [Environment]::GetEnvironmentVariable(
    "MEMORYKEEPER_GOOGLE_MAPS_JAVASCRIPT_API_KEY",
    "Process")

function Read-DeploymentSetting {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
        $separator = $trimmed.IndexOf("=")
        if ($separator -lt 1) { continue }
        $name = $trimmed.Substring(0, $separator).Trim()
        if ($name -eq "MEMORYKEEPER_GOOGLE_MAPS_JAVASCRIPT_API_KEY") {
            return $trimmed.Substring($separator + 1).Trim()
        }
    }

    return $null
}

try {
    $key = $previousValue
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = Read-DeploymentSetting -Path $localConfigPath
    }

    if ([string]::IsNullOrWhiteSpace($key)) {
        throw "Missing deployment Maps credential. Configure CI or config/deployment.env."
    }

    [Environment]::SetEnvironmentVariable(
        "MEMORYKEEPER_GOOGLE_MAPS_JAVASCRIPT_API_KEY",
        $key,
        "Process")

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $projectRoot "publish\win-x64"
    }

    Push-Location $projectRoot
    try {
        dotnet publish "MemoryKeeper.App\MemoryKeeper.App.csproj" `
            -c $Configuration `
            -r win-x64 `
            --self-contained true `
            -p:Platform=x64 `
            -o $OutputDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "MemoryKeeper publish failed."
        }

        $credentialPath = Join-Path $OutputDirectory "MemoryKeeper.maps.key"
        if (-not (Test-Path -LiteralPath $credentialPath -PathType Leaf)) {
            throw "Maps deployment credential was not packaged."
        }

        if ((Get-Content -LiteralPath $credentialPath -Raw).Trim() -ne $key.Trim()) {
            throw "Packaged Maps credential does not match the deployment input."
        }

        Write-Host "MAPS_CREDENTIAL_PACKAGED=true"
        Write-Host ("Deployment output: {0}" -f (Resolve-Path -LiteralPath $OutputDirectory))
    }
    finally {
        Pop-Location
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        "MEMORYKEEPER_GOOGLE_MAPS_JAVASCRIPT_API_KEY",
        $previousValue,
        "Process")
}
