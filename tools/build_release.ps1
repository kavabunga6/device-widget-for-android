[CmdletBinding()]
param(
    [string]$Version = "0.1.6",
    [switch]$AllowUnsignedAndroid
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot "artifacts\releases\$Version"
$packageRoot = Join-Path $artifactRoot "packages"
$sourceRoot = Join-Path $repoRoot "artifacts\compliance\sources"
$localDotnet = Join-Path $repoRoot ".tools\dotnet10\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
if ($dotnet -eq $localDotnet) {
    $localDotnetHome = Join-Path $repoRoot ".tools\dotnet-home10"
    if (Test-Path -LiteralPath $localDotnetHome) { $env:DOTNET_CLI_HOME = $localDotnetHome }
}

if (Test-Path -LiteralPath $artifactRoot) {
    throw "Release directory already exists: $artifactRoot. Remove that exact version directory before rebuilding."
}

$sourceHashes = [ordered]@{
    "scrcpy-4.0.tar.gz" = "A62BC2639E1D56B3E7EBAA20D8DEB4947DD02954B3362BDEBE2EF9F7EAE41B00"
    "ffmpeg-8.1.1.tar.xz" = "B6863ADDE98898F42602017462871B5F6333E65AEC803FDD7A6308639C52EDF3"
    "libusb-1.0.29.tar.gz" = "7C2DD39C0B2589236E48C93247C986AE272E27570942B4163CB00A060FCF1B74"
}

foreach ($entry in $sourceHashes.GetEnumerator()) {
    $path = Join-Path $sourceRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing corresponding source: $path"
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $entry.Value) {
        throw "SHA-256 mismatch for $($entry.Key): $actual"
    }
}

$signingVariables = @(
    "DEVICE_WIDGET_ANDROID_KEYSTORE",
    "DEVICE_WIDGET_ANDROID_STORE_PASSWORD",
    "DEVICE_WIDGET_ANDROID_KEY_ALIAS",
    "DEVICE_WIDGET_ANDROID_KEY_PASSWORD"
)
$hasAndroidSigning = ($signingVariables | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }).Count -eq 0
if (-not $hasAndroidSigning -and -not $AllowUnsignedAndroid) {
    throw "Android release signing variables are missing. Set $($signingVariables -join ', ') or pass -AllowUnsignedAndroid for a local test package."
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$androidUserHome = Join-Path $repoRoot ".tools\android-home"
New-Item -ItemType Directory -Path $androidUserHome -Force | Out-Null
$previousAndroidUserHome = [Environment]::GetEnvironmentVariable("ANDROID_USER_HOME", "Process")
$previousAndroidSdkHome = [Environment]::GetEnvironmentVariable("ANDROID_SDK_HOME", "Process")
try {
    $env:ANDROID_USER_HOME = $androidUserHome
    Remove-Item Env:ANDROID_SDK_HOME -ErrorAction SilentlyContinue
    Push-Location (Join-Path $repoRoot "companion-android")
    try {
        & .\gradlew.bat --no-daemon --console=plain "-Pkotlin.compiler.execution.strategy=in-process" clean lint test assembleRelease
        if ($LASTEXITCODE -ne 0) { throw "Android build failed." }
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($null -eq $previousAndroidUserHome) { Remove-Item Env:ANDROID_USER_HOME -ErrorAction SilentlyContinue }
    else { $env:ANDROID_USER_HOME = $previousAndroidUserHome }
    if ($null -eq $previousAndroidSdkHome) { Remove-Item Env:ANDROID_SDK_HOME -ErrorAction SilentlyContinue }
    else { $env:ANDROID_SDK_HOME = $previousAndroidSdkHome }
}

$apkCandidates = @(
    (Join-Path $repoRoot "companion-android\app\build\outputs\apk\release\app-release.apk"),
    (Join-Path $repoRoot "companion-android\app\build\outputs\apk\release\app-release-unsigned.apk")
)
$apk = $apkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $apk) { throw "Android release APK was not produced." }
if (-not $hasAndroidSigning -and $apk -notmatch "unsigned") {
    throw "Expected an unsigned APK for this test build."
}
Copy-Item -LiteralPath $apk -Destination (Join-Path $packageRoot "DeviceWidget-Companion-$Version.apk")

$desktopTargets = @(
    @{ Rid = "win-x64"; Project = "src\AndroidWidget.Desktop\AndroidWidget.Desktop.csproj" },
    @{ Rid = "win-arm64"; Project = "src\AndroidWidget.Desktop\AndroidWidget.Desktop.csproj" }
)

foreach ($target in $desktopTargets) {
    $publish = Join-Path $artifactRoot "publish\$($target.Rid)"
    & $dotnet publish (Join-Path $repoRoot $target.Project) -c Release -r $target.Rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($target.Rid)." }

    $archive = Join-Path $packageRoot "DeviceWidget-for-Android-$Version-$($target.Rid).zip"
    Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $archive -CompressionLevel Optimal
}

foreach ($entry in $sourceHashes.GetEnumerator()) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $entry.Key) -Destination $packageRoot
}
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "SOURCE_OFFER.md") -Destination $packageRoot

$sumLines = Get-ChildItem -LiteralPath $packageRoot -File |
    Where-Object Name -ne "SHA256SUMS.txt" |
    Sort-Object Name |
    ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[System.IO.File]::WriteAllLines((Join-Path $packageRoot "SHA256SUMS.txt"), $sumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Release packages: $packageRoot"
Write-Host "Linux and macOS packages must be produced natively with the platform build scripts or GitHub Actions workflows."
