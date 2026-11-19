# AMZ CLI

**A local, unofficial CLI and Web UI for Amazon search, product details, and price monitoring.**

AMZ CLI runs on your machine, uses no Amazon API and no account, and keeps its
data local. It reads public product pages, so it works without cookies or login.

> AMZ CLI is an independent project. It is not affiliated with, endorsed by, or
> sponsored by Amazon.

## Install

### Linux x64

```bash
curl -fsSL https://github.com/Duster98/amz-cli/releases/download/installer-stable/install.sh | bash
```

### Windows x64

```powershell
irm https://github.com/Duster98/amz-cli/releases/download/installer-stable/install.ps1 | iex
```

The installer verifies the project RSA signature and the SHA-256 of every
downloaded file before running. On Linux, `openssl` is required for that check.

After installation:

```bash
amz-cli --version
amz-cli self-test
```

Default locations:

| Platform | Installation |
|---|---|
| Linux | `~/.local/share/amz-cli` |
| Windows | `%LOCALAPPDATA%\Programs\AmzCLI` |

On Linux the installer also creates `~/.local/bin/amz-cli` and an application-menu
entry. On Windows it adds the folder to the user PATH and creates Start Menu and
Desktop shortcuts.

### Requirements

- Linux x64 or Windows x64.
- Linux: `openssl` for the bootstrap; `curl-impersonate` for the default
  browser-like transport. Set `AMZ_CURL_IMPERSONATE` if it is not in `PATH`.
- Windows: self-contained, no .NET runtime required.
- Network access to Amazon and GitHub (for updates).

The downloaded installer supports `--dir`, `--no-path`, `--dry-run`,
`--channel beta`, and `--uninstall`.

## Quick start

```bash
# Search a marketplace
amz-cli search "mechanical keyboard" --domain it

# Search with filters and pagination
amz-cli search "usb-c hub" --deals --no-ads --pages 2

# Product details, rating, and reviews
amz-cli details B07Q6Y5SC7 --rating --reviews

# Compare products
amz-cli details B07Q6Y5SC7 B08N5WRWNW --compare

# Today's deals
amz-cli deals --domain it --deal-type today

# Monitor a price every hour
amz-cli monitor B07Q6Y5SC7 --timer 3600 --save-path ./monitor

# Open the local Web UI
amz-cli web --open
```

## Features

- **Search** across Amazon with filters, pagination, deals, delivery estimates,
  sponsored-result exclusion, and JSON/TSV output.
- **Product details**: price, coupons, availability, seller, shipping, variants,
  buying options, rating, and reviews.
- **Multiple marketplaces**: `it`, `de`, `fr`, `uk`, `com`/`us`, `jp`, and `es`.
- **Price monitoring**: timer or adaptive budget, groups, price thresholds,
  drop percentages, CSV/JSON/JSONL history, and optional desktop notifications.
- **Local Web UI**: search, deals, product cards, filters, and persistent state,
  with no browser storage.
- **Signed updates**: the CLI verifies every update manifest and file hash before
  applying it.
- **English interface**. Amazon-provided marketplace labels and localized
  product data remain in their original language.

## Monitoring

The monitor runs sequential cycles, saves observations, and triggers alerts when
your rules match.

```bash
# One product every hour
amz-cli monitor B07Q6Y5SC7 --timer 3600 --save-path ./monitor

# Several marketplaces in one run
amz-cli monitor it:B07Q6Y5SC7 de:B0DP1VG1DZ \
  --timer 3600 --cap it:20121 --cap de:10115 --save-path ./monitor

# A single cycle for cron or systemd
amz-cli monitor --asin-file asins.txt --once --save-format csv --save-path ./monitor

# Thresholds and groups from a JSON configuration
amz-cli monitor --config monitor.json
```

Adaptive scheduling spreads a fixed request budget over a time window:

```bash
amz-cli monitor --config monitor.json --schedule adaptive \
  --window 86400 --max-requests 500 --min-gap 600 --max-gap 1200
```

Use `amz-cli monitor --help` for all options, and `amz-cli monitor --stats` to
inspect saved data.

## Updates and diagnostics

```bash
amz-cli update --check
amz-cli update
amz-cli doctor
amz-cli self-test
```

Updates come from the signed feeds published in this repository. `self-test`
makes no network requests.

## Respect for Amazon

AMZ CLI is intended for **personal, low-volume use**. It does not sign in, does
not access private data, and does not solve CAPTCHAs. Please use it responsibly:

- Keep the built-in delays at their defaults or higher: `--page-delay 2000`,
  `--enrich-delay 1500`, `--variant-delay 1500`.
- Do not run multiple searches or monitor instances against the same marketplace
  in parallel.
- Use reasonable monitor intervals: `--timer` defaults to 3600 seconds and has a
  minimum of 10 seconds. For long-running monitors, prefer `--schedule adaptive`
  with a daily `--max-requests`.
- Do not lower `--request-delay` below its minimum (6 seconds).
- Stop when Amazon returns blocks or rate limits. The client applies a cooldown
  automatically; do not work around it.
- Do not use AMZ CLI for mass scraping, resale of data, or access to protected
  content. You are responsible for complying with Amazon's Conditions of Use,
  `robots.txt`, and applicable law.

Amazon and all related marks are trademarks of Amazon.com, Inc. or its
affiliates. All product data belongs to its respective owners.

## About this repository

This repository hosts the signed AMZ CLI release artifacts and update feeds. The
source code is not hosted here.

- Releases: <https://github.com/Duster98/amz-cli/releases>
- Installer assets: <https://github.com/Duster98/amz-cli/releases/tag/installer-stable>
