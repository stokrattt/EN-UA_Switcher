[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $repoRoot "src\Switcher.App\Switcher.App.csproj"
$directoryBuildPropsPath = Join-Path $repoRoot "Directory.Build.props"

if (-not (Test-Path $projectPath)) {
    throw "Could not find project file at '$projectPath'."
}

[xml]$projectXml = Get-Content $projectPath
$projectVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($projectVersion) -and (Test-Path $directoryBuildPropsPath)) {
    [xml]$directoryBuildPropsXml = Get-Content $directoryBuildPropsPath
    $projectVersion = $directoryBuildPropsXml.Project.PropertyGroup.SwitcherVersion | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $resolvedVersion = $projectVersion
}
else {
    $resolvedVersion = $Version.Trim()
}

if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
    throw "Could not resolve an application version. Pass -Version or define <Version> in the project file."
}

if ($resolvedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $resolvedVersion = $resolvedVersion.Substring(1)
}

$numericVersionCore = ($resolvedVersion -split '-', 2)[0]
$versionParts = $numericVersionCore.Split('.')
if ($versionParts.Count -eq 0 -or ($versionParts | Where-Object { $_ -notmatch '^\d+$' })) {
    throw "Resolved version '$resolvedVersion' is not a valid semantic version."
}

while ($versionParts.Count -lt 4) {
    $versionParts += "0"
}

$assemblyVersion = ($versionParts | Select-Object -First 4) -join '.'

$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseRoot = Join-Path $artifactsRoot "release"
$stagingRoot = Join-Path $releaseRoot "staging"
$fullPublishDir = Join-Path $stagingRoot "publish-full"
$smallPublishDir = Join-Path $stagingRoot "publish-small"
$dotnetCliHome = Join-Path $artifactsRoot ".dotnet"

$mainAssetName = "EN-UA-Switcher.exe"
$runtimeDependentAssetName = "EN-UA-Switcher-runtime-dependent.exe"
$checksumsFile = Join-Path $releaseRoot "SHA256SUMS.txt"
$manifestFile = Join-Path $releaseRoot "release-manifest.txt"

if (Test-Path $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $fullPublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $smallPublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $dotnetCliHome -Force | Out-Null

$env:DOTNET_CLI_HOME = $dotnetCliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

$restoreArgs = @(
    "restore",
    $projectPath,
    "-r", $Runtime,
    "--ignore-failed-sources"
)

Write-Host "Restoring packages for $Runtime..."
& dotnet @restoreArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

$fullPublishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "--ignore-failed-sources",
    "--no-restore",
    "/p:Version=$resolvedVersion",
    "/p:InformationalVersion=$resolvedVersion",
    "/p:AssemblyVersion=$assemblyVersion",
    "/p:FileVersion=$assemblyVersion",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:DebugType=None",
    "/p:DebugSymbols=false",
    "-o", $fullPublishDir
)

Write-Host "Publishing standalone self-contained build..."
& dotnet @fullPublishArgs

if ($LASTEXITCODE -ne 0) {
    throw "Self-contained dotnet publish failed with exit code $LASTEXITCODE."
}

$smallPublishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "false",
    "--ignore-failed-sources",
    "--no-restore",
    "/p:Version=$resolvedVersion",
    "/p:InformationalVersion=$resolvedVersion",
    "/p:AssemblyVersion=$assemblyVersion",
    "/p:FileVersion=$assemblyVersion",
    "/p:PublishSingleFile=true",
    "/p:DebugType=None",
    "/p:DebugSymbols=false",
    "-o", $smallPublishDir
)

Write-Host "Publishing smaller runtime-dependent build..."
& dotnet @smallPublishArgs

if ($LASTEXITCODE -ne 0) {
    throw "Runtime-dependent dotnet publish failed with exit code $LASTEXITCODE."
}

$fullPublishedExe = Join-Path $fullPublishDir "EN-UA-Switcher.exe"
$smallPublishedExe = Join-Path $smallPublishDir "EN-UA-Switcher.exe"
$fullReleaseExe = Join-Path $releaseRoot $mainAssetName
$smallReleaseExe = Join-Path $releaseRoot $runtimeDependentAssetName

if (-not (Test-Path $fullPublishedExe)) {
    throw "Expected self-contained executable at '$fullPublishedExe', but it was not found."
}

if (-not (Test-Path $smallPublishedExe)) {
    throw "Expected runtime-dependent executable at '$smallPublishedExe', but it was not found."
}

Copy-Item -LiteralPath $fullPublishedExe -Destination $fullReleaseExe
Copy-Item -LiteralPath $smallPublishedExe -Destination $smallReleaseExe

$fullFileVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($fullReleaseExe)
$smallFileVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($smallReleaseExe)
$fullProductVersionMatches = -not [string]::IsNullOrWhiteSpace($fullFileVersionInfo.ProductVersion) `
    -and $fullFileVersionInfo.ProductVersion.StartsWith($resolvedVersion, [System.StringComparison]::Ordinal)
$smallProductVersionMatches = -not [string]::IsNullOrWhiteSpace($smallFileVersionInfo.ProductVersion) `
    -and $smallFileVersionInfo.ProductVersion.StartsWith($resolvedVersion, [System.StringComparison]::Ordinal)

if (-not $fullProductVersionMatches) {
    throw "Published self-contained executable reports ProductVersion '$($fullFileVersionInfo.ProductVersion)', expected '$resolvedVersion'."
}

if (-not $smallProductVersionMatches) {
    throw "Published runtime-dependent executable reports ProductVersion '$($smallFileVersionInfo.ProductVersion)', expected '$resolvedVersion'."
}

$fullHash = (Get-FileHash -LiteralPath $fullReleaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
$smallHash = (Get-FileHash -LiteralPath $smallReleaseExe -Algorithm SHA256).Hash.ToLowerInvariant()

@(
    "$fullHash *$mainAssetName"
    "$smallHash *$runtimeDependentAssetName"
) | Set-Content -LiteralPath $checksumsFile -Encoding ascii

@(
    "Version: $resolvedVersion"
    "AssemblyVersion: $assemblyVersion"
    "Runtime: $Runtime"
    ""
    "$mainAssetName ProductVersion: $($fullFileVersionInfo.ProductVersion)"
    "$runtimeDependentAssetName ProductVersion: $($smallFileVersionInfo.ProductVersion)"
    ""
    "$mainAssetName - main self-contained Windows build, no .NET install required"
    "$runtimeDependentAssetName - smaller runtime-dependent build, requires .NET 8 Desktop Runtime"
) | Set-Content -LiteralPath $manifestFile -Encoding ascii

$fullSizeMb = [Math]::Round((Get-Item -LiteralPath $fullReleaseExe).Length / 1MB, 2)
$smallSizeKb = [Math]::Round((Get-Item -LiteralPath $smallReleaseExe).Length / 1KB, 0)

Write-Host ""
Write-Host "Release artifacts created:"
Write-Host "  FULL : $fullReleaseExe ($fullSizeMb MB)"
Write-Host "  SMALL: $smallReleaseExe ($smallSizeKb KB)"
Write-Host "  SHA  : $checksumsFile"
