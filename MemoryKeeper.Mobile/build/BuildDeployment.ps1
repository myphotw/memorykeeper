[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$AndroidSdkDirectory = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$mobileProjectRoot = Join-Path $projectRoot "MemoryKeeper.Mobile"
$comparison = [System.StringComparison]::OrdinalIgnoreCase

function ConvertTo-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    $expandedPath = [Environment]::ExpandEnvironmentVariables($Path.Trim())
    if ([System.IO.Path]::IsPathRooted($expandedPath)) {
        return [System.IO.Path]::GetFullPath($expandedPath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $expandedPath))
}

function Resolve-AndroidSdkDirectory {
    param(
        [string]$ExplicitPath,
        [Parameter(Mandatory = $true)]
        [string]$BasePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolvedExplicitPath = ConvertTo-AbsolutePath -Path $ExplicitPath -BasePath $BasePath
        if (-not (Test-Path -LiteralPath $resolvedExplicitPath -PathType Container)) {
            throw "The explicitly configured Android SDK directory does not exist: '$resolvedExplicitPath'."
        }

        return $resolvedExplicitPath
    }

    $candidates = @(
        [pscustomobject]@{ Name = "ANDROID_HOME"; Path = $env:ANDROID_HOME },
        [pscustomobject]@{ Name = "ANDROID_SDK_ROOT"; Path = $env:ANDROID_SDK_ROOT }
    )

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates += [pscustomobject]@{
            Name = "LOCALAPPDATA default"
            Path = Join-Path $env:LOCALAPPDATA "Android\Sdk"
        }
    }

    $checkedPaths = New-Object System.Collections.Generic.List[string]
    $seenPaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate.Path)) {
            continue
        }

        $candidatePath = ConvertTo-AbsolutePath -Path $candidate.Path -BasePath $BasePath
        if (-not $seenPaths.Add($candidatePath)) {
            continue
        }

        $checkedPaths.Add(("{0}: {1}" -f $candidate.Name, $candidatePath))
        if (Test-Path -LiteralPath $candidatePath -PathType Container) {
            return $candidatePath
        }
    }

    $checkedDescription = if ($checkedPaths.Count -gt 0) {
        $checkedPaths -join "; "
    }
    else {
        "no environment or default SDK paths were available"
    }
    throw "Android SDK was not found. Specify -AndroidSdkDirectory or configure ANDROID_HOME/ANDROID_SDK_ROOT. Checked: $checkedDescription."
}

function Get-ApkSignerPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SdkDirectory
    )

    $buildToolsDirectory = Join-Path $SdkDirectory "build-tools"
    if (-not (Test-Path -LiteralPath $buildToolsDirectory -PathType Container)) {
        throw "Android SDK build-tools were not found under '$SdkDirectory'."
    }

    $apkSigners = @(Get-ChildItem `
        -LiteralPath $buildToolsDirectory `
        -Filter "apksigner.bat" `
        -File `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Sort-Object -Property @(
            @{ Expression = { $_.Directory.Name }; Descending = $true },
            @{ Expression = { $_.FullName }; Descending = $true }
        ))
    if ($apkSigners.Count -eq 0) {
        throw "Android SDK apksigner was not found under '$buildToolsDirectory'."
    }

    return $apkSigners[0].FullName
}

function Test-ApkSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApkSigner,
        [Parameter(Mandatory = $true)]
        [string]$ApkPath
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $null = & $ApkSigner verify --verbose $ApkPath 2>&1
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "publish\mobile"
}

$outputPath = ConvertTo-AbsolutePath -Path $OutputDirectory -BasePath $projectRoot
$expectedPublishRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $projectRoot "publish"))
if (-not $outputPath.StartsWith(
        $expectedPublishRoot + [System.IO.Path]::DirectorySeparatorChar,
        $comparison)) {
    throw "Mobile deployment output must remain under the repository publish directory."
}

$sdkPath = Resolve-AndroidSdkDirectory `
    -ExplicitPath $AndroidSdkDirectory `
    -BasePath $projectRoot
$apkSignerPath = Get-ApkSignerPath -SdkDirectory $sdkPath
$operationId = [Guid]::NewGuid().ToString("N")
$operationRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $mobileProjectRoot "obj\MobileDeployment\$operationId"))
$expectedOperationRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $mobileProjectRoot "obj\MobileDeployment"))
if (-not $operationRoot.StartsWith(
        $expectedOperationRoot + [System.IO.Path]::DirectorySeparatorChar,
        $comparison)) {
    throw "Mobile deployment staging must remain under the project obj directory."
}

$stagingPath = Join-Path $operationRoot "publish"
$finalApkPath = Join-Path $outputPath "MemoryKeeperMobile.apk"
$temporaryFinalPath = Join-Path $outputPath (".MemoryKeeperMobile.{0}.tmp.apk" -f $operationId)
$backupApkPath = Join-Path $outputPath (".MemoryKeeperMobile.{0}.backup.apk" -f $operationId)

New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

try {
    Push-Location $projectRoot
    try {
        $publishArguments = @(
            "publish",
            "MemoryKeeper.Mobile\MemoryKeeper.Mobile.csproj",
            "-f", "net8.0-android",
            "-c", $Configuration,
            "-p:AndroidPackageFormats=apk",
            ("-p:AndroidSdkDirectory={0}" -f $sdkPath),
            "-o", $stagingPath
        )
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            throw "MemoryKeeper Mobile publish failed."
        }
    }
    finally {
        Pop-Location
    }

    $currentBuildApks = @(Get-ChildItem `
        -LiteralPath $stagingPath `
        -Filter "*.apk" `
        -File `
        -Recurse)
    if ($currentBuildApks.Count -eq 0) {
        throw "Android publish completed without producing an APK in the current staging directory."
    }

    $verifiedApks = @(
        $currentBuildApks | Where-Object {
            Test-ApkSignature -ApkSigner $apkSignerPath -ApkPath $_.FullName
        }
    )
    $preferredSignedApks = @(
        $verifiedApks | Where-Object { $_.Name -like "*-Signed.apk" }
    )
    if ($preferredSignedApks.Count -eq 1) {
        $sourceApk = $preferredSignedApks[0]
    }
    elseif ($verifiedApks.Count -eq 1) {
        $sourceApk = $verifiedApks[0]
    }
    elseif ($verifiedApks.Count -eq 0) {
        throw "No APK produced by the current publish operation passed Android signature verification. The existing deployment was preserved."
    }
    else {
        $verifiedNames = ($verifiedApks.Name | Sort-Object) -join ", "
        throw "Multiple signed APKs were produced by the current publish operation ($verifiedNames). The existing deployment was preserved."
    }

    Copy-Item -LiteralPath $sourceApk.FullName -Destination $temporaryFinalPath
    if (-not (Test-ApkSignature -ApkSigner $apkSignerPath -ApkPath $temporaryFinalPath)) {
        throw "The staged final APK failed signature verification. The existing deployment was preserved."
    }

    if (Test-Path -LiteralPath $finalApkPath -PathType Leaf) {
        try {
            [System.IO.File]::Replace(
                $temporaryFinalPath,
                $finalApkPath,
                $backupApkPath,
                $true)
        }
        catch {
            if (-not (Test-Path -LiteralPath $finalApkPath -PathType Leaf) -and
                (Test-Path -LiteralPath $backupApkPath -PathType Leaf)) {
                Move-Item -LiteralPath $backupApkPath -Destination $finalApkPath
            }

            throw
        }

        if (Test-Path -LiteralPath $backupApkPath -PathType Leaf) {
            try {
                Remove-Item -LiteralPath $backupApkPath -Force
            }
            catch {
                Write-Warning "The previous APK backup could not be removed: '$backupApkPath'."
            }
        }
    }
    else {
        Move-Item -LiteralPath $temporaryFinalPath -Destination $finalApkPath
    }

    $finalApk = Get-Item -LiteralPath $finalApkPath
    Write-Host ("Android SDK: {0}" -f $sdkPath)
    Write-Host ("Final APK: {0}" -f $finalApk.FullName)
    Write-Host ("Final APK size: {0} bytes" -f $finalApk.Length)
    Write-Host ("Final APK created: {0}" -f $finalApk.CreationTime.ToString("o"))
}
finally {
    if (Test-Path -LiteralPath $temporaryFinalPath -PathType Leaf) {
        try {
            Remove-Item -LiteralPath $temporaryFinalPath -Force
        }
        catch {
            Write-Warning "The temporary final APK could not be removed: '$temporaryFinalPath'."
        }
    }

    if ((Test-Path -LiteralPath $operationRoot -PathType Container) -and
        $operationRoot.StartsWith(
            $expectedOperationRoot + [System.IO.Path]::DirectorySeparatorChar,
            $comparison)) {
        try {
            Remove-Item -LiteralPath $operationRoot -Recurse -Force
        }
        catch {
            Write-Warning "The deployment staging directory could not be removed: '$operationRoot'."
        }
    }
}
