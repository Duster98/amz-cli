# Bootstrap installer for Windows x64.
# Verifies the signed installer manifest and checksum with the embedded public
# key, then downloads and runs the installer. The key material below is public;
# never put the private signing key in this file.
$ErrorActionPreference = "Stop"

if ($env:PROCESSOR_ARCHITECTURE -ne "AMD64") {
    Write-Error "Automatic installation is supported only on Windows x64."
    exit 1
}

$base = if ($env:AMZ_INSTALLER_BASE) {
    $env:AMZ_INSTALLER_BASE
} else {
    "https://github.com/Duster98/amz-cli/releases/download/installer-stable"
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("amz-cli-installer-" + [guid]::NewGuid().ToString("N"))
$exe = Join-Path $tmp "amz-cli-installer.exe"

$modulusB64 = @"
pkzs+7aP+j8pjFfbH6vP4O2BIf6rS3yYVk3JxKdAkpDItK5q4/pnWhqd6Mfnfr6XTpdKQ1mORKaIF1+dYIahlEah2MO3JZMbUWaZmesVpQymkVmm1UmuoMaPne7KTo0OynI87B90rgEYbA7OHab22fP54FIv9lLgw+ImUIlRN5TMj1InyUE5TG1ibYOtJgeEncVGATqzSSqL5/jJC6HDy16XhhDHzs2g/X+qBrFuPz9YmWxihUjHdJ8MTpLjIjzWGGwP+2PbJxp0kyOj/BmhCRRQM3V77mSWgl4Vmwdvodhb4BCB07TKLuSI0X6qFq+oTO5CbgssfeyFVaKsGNGVjRU1CKu4/gwf/7AP1LltYlQVLMbDPKdTQUm2razkJVGpQt8eT5yOXncqeDrGwjLIRyvFoT5NcG9Nzibsl+n4lH4EWHPzl7Hf4bPiKca/dOYmFpBSj5M+FVLfv62sE1Iu4bEAbeS7AVHRlB2VDQD8bedCLc/nJ+QcNaJfw+0Fuqi7rT25p/VeDgKVmqhqXo40yoMI52V+me5I3aKPS/gIrA2aDAnbYShj/BKRw0FVOM23p5qCQqkFwpIExkcS5gPTxLL1A7Lnc2VhoOdtMpd/KIupuFIbFRH5yUhdrIM1MDNbaEaamcIdQm1xAAdexZfLf6U/Sxu3tIfusO1kb75Jkw0=
"@
$modulusB64 = $modulusB64 -replace '\s', ''
$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportParameters([System.Security.Cryptography.RSAParameters]@{
    Modulus = [Convert]::FromBase64String($modulusB64)
    Exponent = [Convert]::FromBase64String("AQAB")
})

function Assert-InstallerSignature {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $true)][string]$Signature
    )

    $data = [System.IO.File]::ReadAllBytes($File)
    $signatureBytes = [System.IO.File]::ReadAllBytes($Signature)
    $verified = $false
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        # PowerShell 7 / .NET Core path.
        try {
            $verified = $rsa.VerifyData(
                $data,
                $signatureBytes,
                [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        }
        catch {
            $verified = $false
        }
    }

    if (-not $verified) {
        # Windows PowerShell 5.1 / .NET Framework path.
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $verified = $rsa.VerifyData($data, $sha, $signatureBytes)
        }
        catch {
            $verified = $false
        }
        finally {
            $sha.Dispose()
        }
    }

    if (-not $verified) {
        throw ("Invalid signature for " + [System.IO.Path]::GetFileName($File) + ".")
    }
}

try {
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Write-Host "Downloading and verifying the AMZ CLI installer..."

    $manifest = Join-Path $tmp "installer.json"
    $manifestSig = Join-Path $tmp "installer.json.sig"
    $checksums = Join-Path $tmp "installer.sha256"
    $checksumsSig = Join-Path $tmp "installer.sha256.sig"

    Invoke-WebRequest -UseBasicParsing -Uri "$base/installer.json" -OutFile $manifest
    Invoke-WebRequest -UseBasicParsing -Uri "$base/installer.json.sig" -OutFile $manifestSig
    Invoke-WebRequest -UseBasicParsing -Uri "$base/installer.sha256" -OutFile $checksums
    Invoke-WebRequest -UseBasicParsing -Uri "$base/installer.sha256.sig" -OutFile $checksumsSig

    Assert-InstallerSignature -File $manifest -Signature $manifestSig
    Assert-InstallerSignature -File $checksums -Signature $checksumsSig

    $line = Get-Content -LiteralPath $checksums |
        Where-Object { $_ -match '  (win-x64/(amz-cli-installer\.exe|amazon-installer\.exe)|(amz-cli|amazon)-installer-win-x64)$' } |
        Select-Object -First 1
    if (-not $line) {
        throw "Windows installer hash was not found in the signed manifest."
    }
    $expected = ($line -split '\s+')[0].ToLowerInvariant()

    $downloaded = $false
    foreach ($asset in @("amz-cli-installer-win-x64", "amazon-installer-win-x64")) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset" -OutFile $exe
            $downloaded = $true
            break
        }
        catch {
            # Try the legacy asset name.
        }
    }
    if (-not $downloaded) {
        throw "Installer binary not found in the GitHub release."
    }
    $actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Invalid installer hash: expected $expected, got $actual."
    }

    & $exe @args
    exit $LASTEXITCODE
}
finally {
    if ($null -ne $rsa) {
        $rsa.Dispose()
    }
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
