# Build amz-cli from source into self-contained executables.
#
#   .\compile.ps1
#   .\compile.ps1 -Rid linux-x64
#   .\compile.ps1 -Test
#   .\compile.ps1 -NoWeb
#   .\compile.ps1 -NoZip
#
# Output: dist\linux-x64\amz-cli, dist\win-x64\amz-cli.exe and, by default,
# amz-cli-<version>-<rid>.zip in dist\.

[CmdletBinding()]
param(
    [ValidateSet("linux-x64", "win-x64")]
    [string[]] $Rid = @("linux-x64", "win-x64"),

    [switch] $Test,
    [switch] $NoWeb,
    [switch] $NoZip,
    [switch] $Help
)

$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot

$Root = (Get-Location).Path
$Dist = Join-Path $Root "dist"
$App = "amz-cli"
$CliProj = Join-Path $Root "src/AmazonScraper.Cli/AmazonScraper.Cli.csproj"
$NotifyProj = Join-Path $Root "src/AmazonScraper.Notifications.Windows/AmazonScraper.Notifications.Windows.csproj"

function Show-Usage {
    @"
Build amz-cli from source.

Usage:
  .\compile.ps1 [options]

Options:
  -Rid <linux-x64|win-x64>  Build only one target (comma-separated for both).
  -Test                     Run the .NET test suite before publishing.
  -NoWeb                    Do not rebuild the Web UI; use the current
                            src\AmazonScraper.Cli\WebAssets\web.zip.
  -NoZip                    Do not create ZIP archives.
  -Help                     Show this help.

Requirements:
  .NET SDK 10, Node.js 20.19+ and npm.
  On Linux, the generated executable also needs curl-impersonate in PATH at
  runtime. On Windows, the dependencies are included in the output folder.

Output:
  dist\linux-x64\amz-cli
  dist\win-x64\amz-cli.exe
  dist\amz-cli-<version>-linux-x64.zip
  dist\amz-cli-<version>-win-x64.zip
"@
}

function Stop-WithError {
    param([string] $Message)
    Write-Error $Message
    exit 1
}

function Assert-Command {
    param([string] $Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Stop-WithError "'$Name' is required but was not found in PATH."
    }
}

function Invoke-Checked {
    param(
        [string] $Description,
        [scriptblock] $Command
    )
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "$Description failed (exit $LASTEXITCODE)."
    }
}

if ($Help) {
    Show-Usage
    exit 0
}

Assert-Command "dotnet"
Assert-Command "node"
Assert-Command "npm"

$dotnetVersion = ""
try {
    $dotnetVersion = (& dotnet --version 2>$null).Trim()
} catch {
    Stop-WithError "The .NET 10 SDK is required."
}
if (-not $dotnetVersion.StartsWith("10.")) {
    Stop-WithError "The .NET 10 SDK is required (found '$dotnetVersion')."
}

$nodeVersion = (& node --version).Trim().TrimStart("v")
$nodeParts = $nodeVersion.Split(".")
if ($nodeParts.Count -lt 2) {
    Stop-WithError "Unable to read the Node.js version."
}
$nodeMajor = [int] $nodeParts[0]
$nodeMinor = [int] $nodeParts[1]
if ($nodeMajor -lt 20 -or ($nodeMajor -eq 20 -and $nodeMinor -lt 19)) {
    Stop-WithError "Node.js 20.19+ is required (found $nodeVersion)."
}

$version = ""
$projectText = Get-Content -LiteralPath $CliProj -Raw
$versionMatch = [regex]::Match($projectText, "<Version>([^<]+)</Version>")
if ($versionMatch.Success) {
    $version = $versionMatch.Groups[1].Value.Trim()
}
if ([string]::IsNullOrWhiteSpace($version)) {
    Stop-WithError "Unable to read the project version from $CliProj."
}

Write-Host "AMZ CLI $version"
Write-Host "Targets: $($Rid -join ', ')"
Write-Host ""

if ($NoWeb) {
    $webZip = Join-Path $Root "src/AmazonScraper.Cli/WebAssets/web.zip"
    if (-not (Test-Path -LiteralPath $webZip)) {
        Write-Host "Warning: -NoWeb was used but WebAssets\web.zip does not exist."
        Write-Host "         The executable will not include the Web UI."
    } else {
        Write-Host "Web UI: using existing src\AmazonScraper.Cli\WebAssets\web.zip"
    }
} else {
    Write-Host "Building Web UI..."
    & (Join-Path $Root "web/build.ps1")
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Web UI build failed."
    }
}

if ($Test) {
    Write-Host ""
    Write-Host "Running tests..."
    & dotnet test (Join-Path $Root "AmazonScraper.slnx") -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Tests failed."
    }
}

function Copy-WindowsNativeLibraries {
    param([string] $Out)

    $packagesText = Get-Content -LiteralPath (Join-Path $Root "Directory.Packages.props") -Raw
    $packageMatch = [regex]::Match(
        $packagesText,
        'CurlImpersonate\.Http.*?Version="([^"]+)"'
    )
    if (-not $packageMatch.Success) {
        Write-Host "Warning: CurlImpersonate.Http version not found; native libraries were not copied."
        return
    }
    $packageVersion = $packageMatch.Groups[1].Value.Trim()

    $lines = & dotnet nuget locals global-packages --list
    $line = $lines | Where-Object { $_ -like "global-packages:*" } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($line)) {
        Write-Host "Warning: NuGet global-packages folder not found; native libraries were not copied."
        return
    }
    $nugetRoot = ($line -replace "^global-packages:\s*", "").Trim()
    $nativeDir = Join-Path $nugetRoot ("curlimpersonate/{0}/runtimes/win-x64/native" -f $packageVersion)

    if (Test-Path -LiteralPath $nativeDir) {
        Copy-Item -Path (Join-Path $nativeDir "*") -Destination $Out -Force
        Write-Host "Windows native libraries copied from NuGet."
    } else {
        Write-Host "Warning: Windows native libraries were not found in the NuGet cache."
        Write-Host "         The executable may rely on its extracted native assets."
    }
}

function Remove-SingleFileArtifacts {
    param([string] $Out)

    $names = @(
        "$App.dll",
        "$App.pdb",
        "$App.deps.json",
        "$App.runtimeconfig.json",
        "$App.exe.deps.json",
        "$App.exe.pdb",
        "$App.exe.runtimeconfig.json"
    )
    foreach ($name in $names) {
        Remove-Item -LiteralPath (Join-Path $Out $name) -Force -ErrorAction SilentlyContinue
    }
}

function Publish-Cli {
    param(
        [string] $TargetRid,
        [string] $Out
    )

    & dotnet publish $CliProj `
        -c Release `
        -r $TargetRid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:IncludeSourceRevisionInInformationalVersion=false `
        -o $Out
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Publishing $TargetRid failed."
    }
}

function Publish-WindowsNotifier {
    param([string] $Out)

    if (-not (Test-Path -LiteralPath $NotifyProj)) {
        Write-Host "Warning: Windows notifier project not found; desktop notifications"
        Write-Host "         will not be available in this package."
        return
    }

    & dotnet publish $NotifyProj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $Out
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "Publishing the Windows notifier failed."
    }

    Remove-Item -LiteralPath (Join-Path $Out "amz-notify.pdb") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $Out "amz-notify.deps.json") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $Out "amz-notify.runtimeconfig.json") -Force -ErrorAction SilentlyContinue
}

function New-ZipArchive {
    param(
        [string] $Out,
        [string] $TargetRid
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = Join-Path $Dist ("amz-cli-{0}-{1}.zip" -f $version, $TargetRid)
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $Out,
        $archive,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )
    $sizeMb = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1)
    Write-Host "ZIP: $archive ($sizeMb MB)"
}

New-Item -ItemType Directory -Force -Path $Dist | Out-Null

foreach ($targetRid in $Rid) {
    $out = Join-Path $Dist $targetRid
    Write-Host ""
    Write-Host "Publishing $targetRid..."
    if (Test-Path -LiteralPath $out) {
        Remove-Item -LiteralPath $out -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $out | Out-Null

    Publish-Cli -TargetRid $targetRid -Out $out

    if ($targetRid -eq "win-x64") {
        Publish-WindowsNotifier -Out $out
        Copy-WindowsNativeLibraries -Out $out
    }

    Remove-SingleFileArtifacts -Out $out
    Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination $out -Force
    Copy-Item -LiteralPath (Join-Path $Root "NOTICE") -Destination $out -Force
    Copy-Item -LiteralPath (Join-Path $Root "THIRD_PARTY_NOTICES.md") -Destination $out -Force
    Copy-Item -LiteralPath (Join-Path $Root "README.md") -Destination $out -Force

    if ($targetRid -eq "linux-x64" -and $IsLinux) {
        & chmod +x (Join-Path $out $App)
    }

    if (-not $NoZip) {
        New-ZipArchive -Out $out -TargetRid $targetRid
    }
}

Write-Host ""
Write-Host "Build complete: $Dist"
foreach ($targetRid in $Rid) {
    if ($targetRid -eq "win-x64") {
        $executable = Join-Path (Join-Path $Dist $targetRid) "$App.exe"
    } else {
        $executable = Join-Path (Join-Path $Dist $targetRid) $App
    }
    Write-Host "  ${targetRid}: $executable"
}

Write-Host ""
foreach ($targetRid in $Rid) {
    if ($targetRid -eq "win-x64") {
        Write-Host "Run Windows: $(Join-Path (Join-Path $Dist $targetRid) "$App.exe") --version"
    } else {
        Write-Host "Run Linux:   $(Join-Path (Join-Path $Dist $targetRid) $App) --version"
    }
}
Write-Host ""
foreach ($targetRid in $Rid) {
    if ($targetRid -eq "win-x64") {
        Write-Host "Windows includes its runtime dependencies in the output folder."
    } else {
        Write-Host "Linux requires curl-impersonate in PATH."
    }
}
