[CmdletBinding()]
param(
    [string]$Version = "0.1.1",
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

Push-Location (Join-Path $repoRoot "companion-android")
try {
    & .\gradlew.bat clean lint test assembleRelease
    if ($LASTEXITCODE -ne 0) { throw "Android build failed." }
}
finally {
    Pop-Location
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
    @{ Rid = "win-x64"; Project = "AndroidWidget.csproj"; Kind = "windows" },
    @{ Rid = "win-arm64"; Project = "AndroidWidget.csproj"; Kind = "windows" },
    @{ Rid = "osx-x64"; Project = "src\AndroidWidget.Desktop\AndroidWidget.Desktop.csproj"; Kind = "macos" },
    @{ Rid = "osx-arm64"; Project = "src\AndroidWidget.Desktop\AndroidWidget.Desktop.csproj"; Kind = "macos" },
    @{ Rid = "linux-x64"; Project = "src\AndroidWidget.Desktop\AndroidWidget.Desktop.csproj"; Kind = "linux" },
    @{ Rid = "linux-arm64"; Project = "src\AndroidWidget.Desktop\AndroidWidget.Desktop.csproj"; Kind = "linux" }
)

foreach ($target in $desktopTargets) {
    $publish = Join-Path $artifactRoot "publish\$($target.Rid)"
    & $dotnet publish (Join-Path $repoRoot $target.Project) -c Release -r $target.Rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($target.Rid)." }

    if ($target.Kind -eq "windows") {
        $archive = Join-Path $packageRoot "DeviceWidget-for-Android-$Version-$($target.Rid).zip"
        Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $archive -CompressionLevel Optimal
        continue
    }

    if ($target.Kind -eq "macos") {
        $stage = Join-Path $artifactRoot "stage\$($target.Rid)\Device Widget.app"
        $macos = Join-Path $stage "Contents\MacOS"
        $resources = Join-Path $stage "Contents\Resources"
        New-Item -ItemType Directory -Path $macos,$resources -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $publish "DeviceWidget.Desktop") -Destination (Join-Path $macos "DeviceWidget")
        Copy-Item -LiteralPath (Join-Path $publish "LICENSE") -Destination $resources
        Copy-Item -LiteralPath (Join-Path $publish "THIRD_PARTY_NOTICES.md") -Destination $resources
        Copy-Item -LiteralPath (Join-Path $publish "SOURCE_OFFER.md") -Destination $resources
        Copy-Item -LiteralPath (Join-Path $publish "licenses") -Destination $resources -Recurse
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>DeviceWidget</string>
<key>CFBundleIdentifier</key><string>dev.devicewidget.desktop</string>
<key>CFBundleName</key><string>Device Widget</string>
<key>CFBundleDisplayName</key><string>Device Widget for Android</string>
<key>CFBundleShortVersionString</key><string>$Version</string>
<key>CFBundleVersion</key><string>$Version</string>
<key>LSMinimumSystemVersion</key><string>10.15</string>
</dict></plist>
"@
        [System.IO.File]::WriteAllText((Join-Path $stage "Contents\Info.plist"), $plist, [System.Text.UTF8Encoding]::new($false))
        $archive = Join-Path $packageRoot "DeviceWidget-for-Android-$Version-$($target.Rid).tar.gz"
        & tar -czf $archive -C (Split-Path -Parent $stage) (Split-Path -Leaf $stage)
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $($target.Rid)." }
        continue
    }

    $linuxStage = Join-Path $artifactRoot "stage\$($target.Rid)\DeviceWidget"
    New-Item -ItemType Directory -Path $linuxStage -Force | Out-Null
    Copy-Item -Path (Join-Path $publish "*") -Destination $linuxStage -Recurse
    $archive = Join-Path $packageRoot "DeviceWidget-for-Android-$Version-$($target.Rid).tar.gz"
    & tar -czf $archive -C (Split-Path -Parent $linuxStage) (Split-Path -Leaf $linuxStage)
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $($target.Rid)." }
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
