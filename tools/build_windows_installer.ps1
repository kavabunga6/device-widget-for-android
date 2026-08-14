[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publish = [System.IO.Path]::GetFullPath($PublishDirectory)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$script = Join-Path $repoRoot 'installer\windows\DeviceWidget.iss'
$icon = Join-Path $repoRoot 'installer\windows\DeviceWidget.ico'
$license = Join-Path $repoRoot 'LICENSE'

if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    throw "Publish directory does not exist: $publish"
}
foreach ($required in @('DeviceWidget.exe', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'SOURCE_OFFER.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) {
        throw "Installer payload is missing $required."
    }
}
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
    throw "Installer icon does not exist: $icon"
}

$compilerCandidates = @(
    [Environment]::GetEnvironmentVariable('INNO_SETUP_COMPILER'),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $compiler = $command.Source }
}
if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install JRSoftware.InnoSetup or set INNO_SETUP_COMPILER.'
}

$versionMatch = [regex]::Match($Version, '^(\d+)\.(\d+)\.(\d+)')
$numericVersion = '{0}.{1}.{2}.0' -f $versionMatch.Groups[1].Value,
    $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value
New-Item -ItemType Directory -Path $output -Force | Out-Null

$arguments = @(
    "/DMyAppVersion=$Version",
    "/DMyNumericVersion=$numericVersion",
    "/DMySourceDir=$publish",
    "/DMyOutputDir=$output",
    "/DMyArchitecture=$Rid",
    "/DMyIconFile=$icon",
    "/DMyLicenseFile=$license",
    $script
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $output "DeviceWidget-for-Android-$Version-$Rid-Setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer was not produced: $installer"
}
if ((Get-Item -LiteralPath $installer).Length -lt 1MB) {
    throw 'Installer is unexpectedly small.'
}
Write-Output $installer
