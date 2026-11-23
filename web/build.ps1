# Compila la SPA e la incorpora nella CLI come WebAssets/web.zip.
$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot

if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "node_modules"))) {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
        throw "npm ci non riuscito"
    }
}

npm run build
if ($LASTEXITCODE -ne 0) {
    throw "npm run build non riuscito"
}

$dest = Join-Path (Split-Path $PSScriptRoot -Parent) "src/AmazonScraper.Cli/WebAssets/web.zip"
$destDirectory = Split-Path $dest -Parent
New-Item -ItemType Directory -Force -Path $destDirectory | Out-Null
if (Test-Path -LiteralPath $dest) {
    Remove-Item -LiteralPath $dest -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    (Join-Path $PSScriptRoot "dist"),
    $dest,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$sizeKb = [math]::Round((Get-Item -LiteralPath $dest).Length / 1KB, 1)
Write-Host ("Web UI: {0} KB -> {1}" -f $sizeKb, $dest)
