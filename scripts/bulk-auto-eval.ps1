[CmdletBinding()]
param(
    [int]$EnLimit = 50000,
    [int]$UaLimit = 50000,
    [int]$ContextSample = 1500,
    [string]$EnPath,
    [string]$UaPath,
    [switch]$FetchLargeWordLists
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$bulkAuditDir = Join-Path $repoRoot 'artifacts\bulk-audit'
$projectPath = Join-Path $repoRoot 'tools\BulkEval\BulkEval.csproj'
$defaultEnPath = Join-Path $bulkAuditDir 'en-top50000.txt'
$fallbackEnPath = Join-Path $bulkAuditDir 'en-top10000.txt'
$defaultUaPath = Join-Path $bulkAuditDir 'uk-top50000.txt'
$fallbackUaPath = Join-Path $bulkAuditDir 'uk-top10000.txt'

function Get-PreferredCorpusPath {
    param(
        [string]$Preferred,
        [string]$Fallback,
        [string]$Dictionary
    )

    if (Test-Path $Preferred) { return $Preferred }
    if (Test-Path $Fallback) { return $Fallback }
    return $Dictionary
}

function Write-LargeWordLists {
    param([string]$OutputDirectory)

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

    $en50Path = Join-Path $OutputDirectory 'en-top50000.txt'
    $uk50Path = Join-Path $OutputDirectory 'uk-top50000.txt'
    $en10Path = Join-Path $OutputDirectory 'en-top10000.txt'
    $uk10Path = Join-Path $OutputDirectory 'uk-top10000.txt'
    $en50Tmp = "$en50Path.tmp"
    $uk50Tmp = "$uk50Path.tmp"
    $en10Tmp = "$en10Path.tmp"
    $uk10Tmp = "$uk10Path.tmp"

    $en50 = (Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_full.txt').Content -split "`n" |
        ForEach-Object { ($_ -split '\s+')[0].Trim().ToLowerInvariant() } |
        Where-Object { $_ -match '^[a-z]+$' } |
        Select-Object -Unique -First 50000

    $uk50 = (Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/uk/uk_full.txt').Content -split "`n" |
        ForEach-Object { ($_ -split '\s+')[0].Trim().ToLowerInvariant() } |
        Where-Object { $_ -match '[\u0400-\u052F]' } |
        Where-Object { $_ -match "^[\u0400-\u052F'’ʼ-]+$" } |
        Select-Object -Unique -First 50000

    if ($en50.Count -lt 10000) {
        throw "English 50k fetch looks broken: only $($en50.Count) entries matched"
    }

    if ($uk50.Count -lt 10000) {
        throw "Ukrainian 50k fetch looks broken: only $($uk50.Count) entries matched"
    }

    Set-Content -Path $en50Tmp -Encoding utf8 $en50
    Set-Content -Path $uk50Tmp -Encoding utf8 $uk50
    Set-Content -Path $en10Tmp -Encoding utf8 ($en50 | Select-Object -First 10000)
    Set-Content -Path $uk10Tmp -Encoding utf8 ($uk50 | Select-Object -First 10000)

    Move-Item -Force $en50Tmp $en50Path
    Move-Item -Force $uk50Tmp $uk50Path
    Move-Item -Force $en10Tmp $en10Path
    Move-Item -Force $uk10Tmp $uk10Path

    Write-Host "Fetched EN=$($en50.Count) UA=$($uk50.Count) into $OutputDirectory"
}

if ($FetchLargeWordLists -or -not (Test-Path $defaultEnPath) -or -not (Test-Path $defaultUaPath)) {
    Write-LargeWordLists -OutputDirectory $bulkAuditDir
}

if (-not $EnPath) {
    $EnPath = Get-PreferredCorpusPath -Preferred $defaultEnPath -Fallback $fallbackEnPath -Dictionary (Join-Path $repoRoot 'src\Switcher.Core\Dictionaries\en-common.txt')
}

if (-not $UaPath) {
    $UaPath = Get-PreferredCorpusPath -Preferred $defaultUaPath -Fallback $fallbackUaPath -Dictionary (Join-Path $repoRoot 'src\Switcher.Core\Dictionaries\ua-common.txt')
}

$arguments = @(
    'run',
    '--project', $projectPath,
    '--',
    '--en-path', $EnPath,
    '--ua-path', $UaPath,
    '--en', $EnLimit,
    '--ua', $UaLimit,
    '--context-sample', $ContextSample
)

Write-Host "Running BulkEval with EN=$EnLimit UA=$UaLimit ContextSample=$ContextSample"
Write-Host "EN source: $EnPath"
Write-Host "UA source: $UaPath"

& dotnet @arguments
