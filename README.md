# Amazon CLI releases

Questo repository contiene esclusivamente artefatti di release e feed di
aggiornamento firmati per Amazon CLI. Il codice sorgente non è ospitato qui.

## Feed aggiornamenti

- Linux: `releases/latest/download/linux-x64.json`
- Windows: `releases/latest/download/win-x64.json`

Ogni feed contiene manifest firmato, firma RSA-PSS/SHA-256 e URL degli asset
della release. Il client verifica firma e hash prima di applicare un update.

## Installer

Bootstrap e installer sono nel tag prerelease `installer-stable`.

## Sicurezza

La chiave privata di firma non è inclusa nel repository né negli asset. La
chiave pubblica è incorporata nella CLI e verifica soltanto.
