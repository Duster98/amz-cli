# AMZ CLI releases

This repository contains only release artifacts and signed update feeds for
AMZ CLI. The source code is not hosted here.

## Update feeds

- Linux: `releases/latest/download/linux-x64.json`
- Windows: `releases/latest/download/win-x64.json`

Each feed contains the signed manifest, RSA-PSS/SHA-256 signature, and the
download package for that platform. The CLI verifies the signature and every
file hash before applying an update.

## Installer

Bootstrap scripts and signed installer assets live in the
`installer-stable` prerelease tag.

## Verification

Release manifests are signed with RSA-PSS/SHA-256. The public key is embedded
in the CLI and can only verify releases; the private signing key is never
included in this repository or in any artifact.
