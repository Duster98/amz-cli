---
name: amz-cli
description: "Search Amazon products and read their details with the local .NET CLI `amz-cli` (scraping, no sign-in). Use it when the user wants to search for a product, compare prices, check discounts, coupons, reviews, seller, shipping, returns, availability, specifications, or delivery for an ASIN or an Amazon link, or monitor prices and groups with thresholds, alerts, and desktop notifications. Supports Amazon.it, Amazon.de, Amazon.fr, Amazon.co.uk, Amazon.com, Amazon.co.jp, and Amazon.es, pagination, and postal-code delivery including cross-border. Keywords: amazon, amz-cli, search, details, compare, monitor, monitoring, library, saved products, paths, debug, file, folders, log, status, price, discounts, coupon, threshold, alert, notifications, desktop, group, reviews, rating, seller, shipping, delivery, postal code, cap, cross-border, returns, availability, specifications, images, gallery, shopping."
---

# AMZ CLI (.NET)

Unofficial local CLI for searching Amazon products and reading their details.
It does not use official APIs and does not sign in: it downloads the public HTML
with `curl-impersonate` or `HttpClient` and parses it.

This skill describes the **multi-domain .NET 10 CLI**. The old Python CLI
(`amazon-scraper` installed with `pip`) has been uninstalled: the `amz-cli`
command in the `PATH` points to the .NET binary.

## Binary

- Linux: `dotnet/publish/linux-x64/amz-cli`
- Windows: `dotnet\publish\win-x64\amz-cli.exe`

If `amz-cli` in the `PATH` is still the Python version, use the full path to the
.NET binary. Verify with:

```bash
amz-cli --version
amz-cli self-test
```

## When to use it

Use it to:

- search for products and get ASIN, title, price, rating, and reviews;
- compare multiple ASINs;
- read the original price, discount, coupon, seller, shipping, returns,
  availability, specifications, description, and images;
- read buying options with `offer_listing_id`, seller, condition,
  price, shipping, and delivery;
- find the delivery cost and date for an Italian or foreign postal code;
- compare marketplaces and currencies;
- save products and organize them into groups shared with the web interface;
- monitor prices, discounts, and group totals with timers, alerts, and desktop
  notifications;
- get an early delivery estimate from search cards.

Do not use it to:

- buy, pay, or check out;
- access personal prices/coupons or restricted offers;
- aggressively bypass CAPTCHAs or WAFs;
- obtain guaranteed real-time data: prices, stock, and delivery change.

## Quick rules for the agent

1. Prefer `--tk-opt` or `--json` over the table: the table contains ANSI codes
   and is 200 columns wide.
2. Make few requests. Leave at least **6 seconds** between different commands
   and do not parallelize.
3. Use `--no-ads` when searching for a specific product or when you want only
   organic results. In JSON you can still filter `sponsored == false`.
4. Do not invent prices or dates: report the values exactly as they are.
5. Use `--cap`/`--country` on `details` and `monitor` for localized delivery.
   On `monitor` with multiple domains use the prefix: `--cap it:20121`.
   `search --delivery` is only an early estimate for the default location.
6. For a comparison use `details --compare --tk-opt ASIN1 ASIN2 ...`.
7. To analyze a single buying option use
   `details --smid <seller_id>` (see "Analyzing a single option").
8. If a domain returns 503/WAF, do not insist: wait, use `--transport http`
   or `--cookie`, or switch domain.
9. For monitoring use `--once` with cron/systemd or `--detach`; minimum timer
   10s, `--request-delay` minimum 6s. For a daily budget with randomized gaps
   use `--schedule adaptive --max-requests N`. Do not run multiple monitors in
   parallel. `--dry-run` validates config and thresholds without network access.
10. For desktop notifications use `--notify`; verify the backend with
    `--notify-test`. On Linux you need `notify-send`; on Windows BurntToast or a
    PowerShell/`msg.exe` fallback.
11. To diagnose missing files, permissions, stale monitors, or failed updates
    use `amz-cli paths --json --all`; you do not need to scan your home directory.
12. For a one-shot check of runtime, installation, PATH, update, monitor,
    startup, Windows helpers, and skill use `amz-cli doctor --json`. To install
    or verify the agent skill use `amz-cli skill --install` /
    `amz-cli skill --check`.
13. Use `amz-cli library` to save products and create groups shared with the
    web UI. Groups created here do not start monitoring.
14. To monitor the library use `monitor --library-product` or
    `monitor --library-group`. A product in a group cannot be monitored
    individually; monitor the group or remove the product from it. Group
    membership is reloaded every cycle.
15. When performing searches or opening product details on behalf of the user,
    add `--agent` so the shared Web history marks the operation as AI-generated.

## Domains

| ID | Marketplace | Currency | Price example | Default transport | Notes |
|---|---|---|---|---|---|
| `it` | Amazon.it | EUR | `1.234,56 €` | curl | default; best-effort warm-up |
| `de` | Amazon.de | EUR | `1.234,56 €` | curl | stable English UI (`lc-acbde=en_GB`); warm-up disabled |
| `fr` | Amazon.fr | EUR | `1.234,56 €` | curl | `/-/fr/` paths; `/-/fr/` warm-up |
| `uk` | Amazon.co.uk | GBP | `£237.37` | curl | `/-/en-gb/` paths; `/-/en-gb/` warm-up; Windows DLL 8.23, Linux system command |
| `com` | Amazon.com | USD | `$429.00` | http | `/-/en/` paths; warm-up disabled |
| `jp` | Amazon.co.jp | JPY | `￥2,399` | curl | `/-/ja/` paths; warm-up disabled |
| `es` | Amazon.es | EUR | `1.930,40 €` | curl | `/-/es/` paths; Firefox147; warm-up disabled |

Accepted aliases: `amazon.it`, `.it`, `amazon.de`, `.de`, `germany`, `amazon.fr`,
`.fr`, `amazon.co.uk`, `co.uk`, `amazon.com`, `us`, `.com`, `amazon.co.jp`,
`co.jp`, `.jp`, `amazon.es`, `spain`, `.es`.

Product details use the desktop Firefox layout by default on every marketplace
except `.com`. Amazon's mobile pages omit `colorImages` (the image gallery) and
the technical specifications. `--browser Safari260Ios` selects the mobile layout
explicitly. The deals page always uses the desktop layout.

Transport:

- **Linux**: `curl` uses the system `curl-impersonate` command
  (`sudo pacman -S curl-impersonate` on Arch); `http` uses `SocketsHttpHandler`.
  `.com` uses `http` by default, the other domains use `curl`. If an HTTP search
  is blocked, retry once with curl after 4 seconds.
- On `curl`, the Safari profiles use the User-Agent built into the impersonation
  profile, which matches its TLS/HTTP2 fingerprint. Firefox profiles keep a
  current Firefox UA because Amazon rejects the 147 string shipped by
  curl-impersonate. `--user-agent` overrides both; do not pair the Safari 26
  profile with an older iPad UA, because Amazon.it can answer with its
  "Continue shopping" anti-bot page. The `http` transport always sends a browser
  UA because it has no impersonation profile. Safari is an explicit choice; the
  default is the desktop Firefox layout (gallery and specifications).
- HTTP blocking is remembered per marketplace for 2 hours in
  `transport/<domain>.json` in the state directory; automatic requests go
  straight to curl-impersonate. `--transport http` forces a manual attempt.
  `amz-cli self-test` shows active cooldowns.
- Transport, profile, User-Agent, browser headers and timeout can be saved as
  shared defaults for the CLI, Web UI and monitors:
  `amz-cli settings show`, `settings set --transport curl --browser Firefox147`,
  `settings reset`, `settings path`. The file is `http-settings.json` in the
  state directory. Command-line flags always win; monitors load it at startup.
- **Windows**: `curl` uses the .NET `CurlHandler` with `libcurl-impersonate.dll` 2.3.2
  included in the publish; `http` uses `SocketsHttpHandler`.
- Linux command override: `AMZ_CURL_IMPERSONATE` (legacy alias
  `AMZ_UK_CURL`).

## Global options

These apply to `search`, `details`, and `monitor`, unless otherwise indicated.

| Option | Description |
|---|---|
| `--domain <ID>` | Marketplace: `it` default, `de`, `fr`, `uk`, `com`, `jp`, `es` |
| `--timeout <SECONDS>` | HTTP timeout, default 30 or the saved setting |
| `--width <COLUMNS>` | Table width, default 200 |
| `--transport curl\|http` | HTTP transport; the default depends on the domain |
| `--browser <PROFILE>` | curl-impersonate TLS profile |
| `--user-agent <UA>` | Explicit HTTP User-Agent; overrides the curl-impersonate profile UA |
| `--browser-headers` | Full browser headers from the curl-impersonate profile |
| `--cookie <HEADER>` | Explicit HTTP cookies, e.g. `session-id=...; bm_sv=...` |
| `--save-html <FILE>` | Saves the HTML; with multiple pages uses `-p1`, `-p2`, ... |
| `--json` | JSON output |
| `--tk-opt` | Tab-separated output |
| `--html <FILE>` | Reads the HTML from a file, without network access |
| `--version` | Prints the version and exits |
| `--skill` | Prints this Markdown skill and exits |

## Commands

### `amz-cli search <QUERY>`

Searches Amazon and returns the first page (or the requested pages).

```bash
amz-cli search "ssd nvme 1tb" --tk-opt
amz-cli search "monitor" --domain de --delivery --tk-opt
amz-cli search "asus" --domain it --page 2 --tk-opt
amz-cli search "cuffie" --deals --deal-type today
```

Options:

| Option | Description |
|---|---|
| `--domain <ID>` | Marketplace: `it` default, `de`, `fr`, `uk`, `com`, `jp`, `es` |
| `--json` | JSON array of results |
| `--tk-opt` | TSV |
| `--delivery` | Adds the `Delivery` column and the `delivery_text` / `delivery_country` JSON fields |
| `--no-ads` | Excludes sponsored results |
| `--enrich` | Opens the detail page for products without a struck-through price/discount |
| `--enrich-delay <MS>` | Delay between enrich requests, default 1500 ms |
| `--deals` | Filters to products with deals/discounts only |
| `--deal-type <TYPE>` | With `--deals`: `all` (default), `today`, `coupons` |
| `--page <SPEC>` | Specific pages: `2`, `1,3,5`, `3-5`, `1,3-5,8` |
| `--pages <N>` | First `N` pages, from 1 to 20 |
| `--page-delay <MS>` | Delay between pages, default 2000 ms |
| `--html <FILE>` | Reads a single page from a file, without network access |
| `--save-html <FILE>` | Saves the received HTML; with multiple pages uses `-p1`, `-p2`, ... |

`--page` and `--pages` are mutually exclusive. Results are deduplicated by ASIN.
If a subsequent page is empty or blocked, the command stops and keeps the
results already obtained, with a warning on stderr.

#### Deals (`--deals`)

```bash
amz-cli search "cuffie" --deals
amz-cli search "cuffie" --deals --json
amz-cli search "monitor" --deals --deal-type today
amz-cli search "monitor" --deals --deal-type coupons --domain it
```

`--deals` uses Amazon's internal `p_n_deal_type` filter. Default `all`; `today`
and `coupons` are optional. `--deals` is not compatible with `--html`, and
`--deal-type` requires `--deals`.

| Domain | `all` | `today` | `coupons` |
|---|---|---|---|
| `it` | yes | yes | yes |
| `de` | yes | yes | yes |
| `fr` | yes | yes (`Ventes Flash`) | yes |
| `es` | yes | no | no |
| `uk` | yes | yes | no |
| `com` | yes | yes | no |
| `jp` | yes | yes | no |

The `p_n_deal_type` IDs are internal and may change; unavailable combinations
return an explicit error. Table, JSON, and TSV show `Badge`/`DealId` as for the
rest of search. If Amazon exposes the deal ID but not the badge text, `deal`
becomes `Deal`. The `Was`/`Disc.` column shows the struck-through price and the
discount calculated from the card, when available. Amazon may insert sponsored
listings even in the deal filter: the `Badge` column distinguishes results with
a badge. With `--enrich`, products without a struck-through price are opened on
the detail page; this is slower and should be used with `--limit` or small page
counts.

TSV search:

```text
ASIN	Title	Price	Rating	Reviews	Coupon	Sponsored	Deal	DealId	OriginalPrice	Discount
```

With `--delivery`:

```text
ASIN	Title	Price	Rating	Reviews	Coupon	Sponsored	Delivery	Deal	DealId	OriginalPrice	Discount	Choice	ChoiceText	ChoiceFor
```

JSON search:

```json
[
  {
    "asin": "B0...",
    "title": "...",
    "price": "129,95 €",
    "rating": "4,4 / 5",
    "reviews": "141",
    "image": "https://m.media-amazon.com/...",
    "coupon": "",
    "sponsored": false,
    "original_price": "149,00 €",
    "discount": "-10%",
    "amazon_choice": true,
    "amazon_choice_text": "Scelta Amazon",
    "amazon_choice_for": "per rtx 5080",
    "delivery_text": "€11.59 delivery Sat 21 Nov",
    "delivery_country": "Italy"
  }
]
```

`amazon_choice`, `amazon_choice_text`, and `amazon_choice_for` appear only when
the card exposes the badge. The text may be localized or `Overall Pick` on
Amazon.com. The table adds the `Choice` column only when needed.

`delivery_text` and `delivery_country` appear only with `--delivery` and only if
Amazon shows the block in the card. They reflect the session's default location
(IP/locale), not a specific postal code.

### `amz-cli deals`

Lists active deals without a search query, using Amazon's internal
`p_n_deal_type` filter.

```bash
amz-cli deals
amz-cli deals --domain uk
amz-cli deals --domain uk --deal-type today
amz-cli deals --domain uk --deal-type today --category Electronics
amz-cli deals --domain it --deal-type today --category Elettronica
amz-cli deals --domain uk --deal-type today --pages 2 --json
amz-cli deals --domain uk --no-ads --limit 20
amz-cli deals --domain it --deal-type coupons --limit 20
```

Specific options:

| Option | Description |
|---|---|
| `--deal-type <TYPE>` | `all` (default), `today`, `coupons` |
| `--category <NAME>` | Filters by localized department/category; requires network access |
| `--page <SPEC>` | Specific pages, as in `search` |
| `--pages <N>` | First `N` pages, from 1 to 20 |
| `--page-delay <MS>` | Delay between pages, default 2000 ms |

The deals page always uses the desktop layout (Firefox profile and current
Firefox User-Agent), including on `.it`, `.fr`, and `.jp` where product details
use the mobile Safari profile. Amazon exposes the department refinements and the
full deal set only in the desktop layout; the mobile page returns fewer deals
and no categories. The Web **Offerte** page uses the same layout.
| `--limit <N>` | Maximum number of deals shown; default all |
| `--no-ads` | Excludes sponsored results |
| `--enrich` | Opens the detail page for products without a struck-through price/discount |
| `--enrich-delay <MS>` | Delay between enrich requests, default 1500 ms |
| `--delivery` | Adds delivery data when present |
| `--json` / `--tk-opt` | JSON or TSV output |
| `--html <FILE>` | Reads a local page, without network access |

The command shows the results returned by the Amazon filter, even when a card
does not expose a `Deal` badge; the badge is shown when available. The filter
matrix is the same as `search --deals`. `--limit` only trims the output: the
requested pages are downloaded anyway.

### Shared history

`search` and `details` write entries to the shared Web history. Use `--agent`
for operations started by an AI agent:

```bash
amz-cli search "ssd portatile" --agent
amz-cli details B0H25NG1JK --agent
```

Each new command creates a separate entry; searches show a live `running`
status in the Web history until they complete. Search results and product
details are cached only while the Web server is running. Product details use a
separate session cache keyed by marketplace and ASIN, shared by Web, CLI, and
agents, so saved products reopen from cache when available. Deleting history
entries or clearing history does not remove the product cache; removing a saved
product does not remove it either. Both session caches are cleared when the Web
server starts. The maximum number of cached products is configured in
`web/cache-settings.json` (`product_cache_max_entries`, default 500); the least
recently used products are evicted first. The same file sets
`product_cache_ttl_minutes` (30), `saved_auto_refresh_after_minutes` (10080),
`saved_group_refresh_cooldown_minutes` (1440), and
`saved_group_refresh_delay_seconds` (6). Cache entries are reused only when the
delivery/options context matches and the TTL has not expired. Reopening an entry
from the Web history never duplicates it: cached data is shown directly, and
when the cache is gone the search or detail runs again into the same entry.

Inspect or remove the shared history from the CLI:

```bash
amz-cli history list
amz-cli history list --kind search --source agent --status succeeded
amz-cli history list --json
amz-cli history list --tk-opt
amz-cli history remove h_413bde1a13fb
amz-cli history clear --yes
```

- `history remove` accepts one or more ids and also deletes their session cache.
  Unknown ids are reported and produce a non-zero exit code.
- `history clear` requires `--yes` and removes all entries and cached results.
- Changes made by these commands appear in an open Web history page in real
  time over SignalR.
- `--json` works on all three commands; `list` filters with `--kind`,
  `--source`, and `--status`.

### `amz-cli details <ASIN...>`

Details for one or more ASINs. A live fetch updates an existing saved product;
`--html` does not modify the saved library.

```bash
amz-cli details B0CX9CT3ZF --tk-opt
amz-cli details B0CX9CT3ZF B0BY2R4BHW --compare --tk-opt
amz-cli details B0CX9CT3ZF --json
amz-cli details B0CX9CT3ZF --domain de --cap 10115 --json
amz-cli details B0CX9CT3ZF --domain it --country FR --cap 75001 --json
amz-cli details B08JH7KQKV --domain fr --cap 75001 --all-options --json
```

Options:

| Option | Description |
|---|---|
| `--json` | JSON array (even for a single ASIN) |
| `--tk-opt` | `Field<TAB>Value` lines for each product |
| `--compare` | One row per product, with comparison columns |
| `--variants` | Shows the variants/sizes table |
| `--variant-prices` | Retrieves the price and availability of each variant |
| `--variant-delay <MS>` | Delay between variant requests, default 1500 ms |
| `--rating` | Adds the numeric average, count, and star histogram |
| `--reviews` | Adds the reviews available on the detail page |
| `--review-limit <N>` | Limits to N reviews (1-50; default all) |
| `--review-images` | Includes review image URLs |
| `--html <FILE>` | Reads a single ASIN from a file, without network access |
| `--cap <POSTAL>` | Delivery postal code; required for domestic US/JP and for marketplaces without country-only delivery |
| `--country <ISO>` | 2-letter ISO delivery country; on Amazon.com and Amazon.co.jp a non-domestic country can be used without `--cap` |
| `--save-html <FILE>` | Saves the detail HTML |
| `--no-buying-options` | Does not load buying options (no additional AOD request) |
| `--options-delay <MS>` | Delay before each AOD request, default 1500 ms |
| `--all-options` | Loads all AOD pages (max 10 pages) |
| `--options-pages <N>` | Loads at most `N` AOD pages (1-10) |
| `--smid <SELLER_ID>` | Analyzes a specific seller's offer; opens `?smid=...&psc=1` |
| `--save-aod-html <FILE>` | Saves the AOD HTML; pages 2+ use `-p2`, `-p3`, ... |

`--cap` and `--country` only work on `details`, not on `search`.
With multiple ASINs, the location is set only once per command.
`--cap` is not compatible with `--html`. With `--html`, no AOD request is made:
only the count, the "from" price, and any featured offer (`offer_listing_id`)
already present in the file are shown; the other options are not.

#### Postal code formats

| Country | Format | Example |
|---|---|---|
| IT | 5 digits | `20121` |
| DE | 5 digits | `10115` |
| FR | 5 digits | `75001` |
| ES | 5 digits | `28010` |
| GB | postcode | `E1 6AN` |
| US | 5-digit ZIP or ZIP+4 | `10001` |
| JP | 7 digits | `100-0001` |

For cross-border delivery you can use a country different from the
marketplace's:

```bash
# Amazon.it -> France
amz-cli details B0CX9CT3ZF --domain it --country FR --cap 75001

# Amazon.de -> Italy
amz-cli details B0FX39Q2TN --domain de --country IT --cap 20121

# Amazon.co.uk -> Italy
amz-cli details B0CX9CT3ZF --domain uk --country IT --cap 20121

# Amazon.com -> Italy (country only, no ZIP)
amz-cli details B0CX9CT3ZF --domain com --country IT

# Amazon.co.jp -> Italy (country only, no postal code)
amz-cli details B0CX9CT3ZF --domain jp --country IT
```

Cross-border delivery depends on the product: some items cannot be shipped
abroad, and delivery may be empty or show a non-shippable message. Costs and
dates are dynamic.

On Amazon.es the GLOW flow uses the product page's modal URL (real
`pageType`/`storeContext` values) and sends `address-change` as JSON with `Origin`,
`Referer`, and `anti-csrftoken-a2z`; the old `Gateway`/`generic` form on ES was
rejected with HTTP 200 and an empty body. If the JSON is not accepted (for
example on Amazon.it), the CLI automatically retries the old form. Setting an ES
postal code uses the HTTP transport; on Amazon.es, specify `--transport http`
when you use `--country`/`--cap`.

#### Detail JSON fields

```json
{
  "asin": "B0...",
  "title": "...",
  "brand": "...",
  "price": "129,95 €",
  "original_price": "136,99 €",
  "discount": "-5%",
  "prime": {
    "price": "119,95 €",
    "original_price": "129,95 €",
    "discount": "-8%",
    "only": false
  },
  "deal": "Offerta a tempo",
  "deal_id": "amzn1.deal.86ef82ae",
  "deal_ends_text": "Questa offerta termina il 18 novembre 2026",
  "deal_ends_on": "2026-11-18",
  "deal_ends_in_days": 2,
  "deal_has_countdown": true,
  "rating": "4,4 / 5",
  "reviews": "141",
  "seller": "Amazon",
  "shipped_by": "Amazon",
  "returns": "...",
  "availability": "In stock",
  "coupon": "",
  "image": "https://m.media-amazon.com/...",
  "frequently_returned": "Restituito frequentemente a causa di problemi di compatibilità",
  "safety_warnings": [
    "RISCHIO DI SOFFOCAMENTO -- Piccole parti. Non adatto ai bambini sotto i 3 anni."
  ],
  "charger": {
    "included": false,
    "label": "Senza caricatore",
    "note": "CARICATORE NON INCLUSO: ...",
    "box_contents": "Certificato di garanzia"
  },
  "variants": {
    "parent_asin": "B0F21C1SY9",
    "selected_asin": "B0C9C4GHGN",
    "dimensions": {"style_name": "Nome stile", "size_name": "Capacità"},
    "items": [
      {
        "asin": "B0C9C4GHGN",
        "selected": true,
        "attributes": {"Nome stile": "EN600 pro", "Capacità": "256GB"},
        "url": "https://www.amazon.it/dp/B0C9C4GHGN"
      }
    ]
  },
  "rating_details": {
    "average": 4.5,
    "count": 1191,
    "histogram": {"5": 77, "4": 11, "3": 6, "2": 1, "1": 5}
  },
  "top_reviews": {
    "count": 13,
    "local_count": 8,
    "international_count": 5,
    "items": [
      {
        "id": "R3OKMMJPHEO04K",
        "author": "StellaCritica",
        "rating": "5 / 5",
        "rating_value": 5,
        "title": "Boost per il PC !",
        "date": "Recensito in Italia il 13 settembre 2024",
        "source": "local",
        "verified_purchase": true,
        "helpful": "9 persone l’hanno trovata utile",
        "variant": "Nome stile: EN600 pro; Capacità: 512GB",
        "body": "Ho recentemente acquistato...",
        "url": "https://www.amazon.it/portal/customer-reviews/srp/-/R3OKMMJPHEO04K"
      }
    ]
  },
  "amazon_choice": true,
  "amazon_choice_text": "Amazon's Choice",
  "amazon_choice_for": "",
  "import_fees": {
    "price": "$39.99",
    "price_basis": "list",
    "destination": "Italy",
    "shipping": "$10.71",
    "import_charges": "$5.51",
    "landed_total": "$56.21",
    "at_checkout": false,
    "text": "$16.22 Shipping & Import Charges to Italy"
  },
  "images": ["https://..."],
  "description": ["..."],
  "specs": {"Brand": "Samsung"},
  "url": "https://www.amazon.de/dp/B0FX39Q2TN",
  "delivery": {
    "location": "Berlin 10115",
    "country": "DE",
    "postal_code": "10115",
    "type": "delivery",
    "cost": "FREE",
    "time": "Friday, 20 November",
    "cutoff": "Order within 1 hr 16 mins",
    "raw": "delivery FREE Friday, 20 November ..."
  },
  "buying_options": {
    "count": 7,
    "lowest_price": "87,25 €",
    "options": [
      {
        "offer_listing_id": "0F67NTGp...J69dC",
        "seller": "Furan",
        "seller_id": "A131AJORA55M3D",
        "price": "87,25 €",
        "shipping": "GRATUITA",
        "condition": "Nuovo",
        "condition_type": "new",
        "condition_grade": "",
        "delivery": "21 - 29 dicembre",
        "detail_url": "https://www.amazon.it/dp/B07R3YS8R2?smid=A131AJORA55M3D&psc=1"
      }
    ]
  }
}
```

`prime` is present only when Amazon shows a Prime-only price. The main `price`
is the standard offer; `prime.only` is true when no regular offer exists.

`delivery` is omitted if Amazon does not expose the block. It includes `cutoff`
and, when Amazon shows multiple alternatives, `options` with the type, cost,
date, and cutoff of each one. The cutoff is also read from the secondary block
and, when the `data-csa-c-delivery-*` attributes are missing, from the localized
text (`Order within`, `Ordina entro`, etc.). The `frequently_returned` field
contains the full localized text when Amazon shows the "Restituito
frequentemente" badge; otherwise it is empty. `charger` is present only when
Amazon shows the charger safety widget or a charger-related description bullet:
`included=false` means the product ships without a charger, `label` is the
localized widget text, `note` is the matching description bullet, and
`box_contents` is the localized "Contenuto confezione" value. TSV uses
`ChargerIncluded`, `Charger`, `ChargerNote`, and `BoxContents`. `variants` is
omitted when the page does not expose the twister. With `--variant-prices`, each variant includes
`price`, `original_price`, `discount`, and `availability`. `buying_options` is
omitted if the page exposes neither an offer summary nor an AOD list.
`safety_warnings` collects the CPSIA/product-safety card and the “important
information” notices (choking, small parts, magnets, age suitability); duplicate
notices are removed and the longest version of each hazard is kept. The CLI
prints a **Safety warnings** block and the Web UI shows a warning banner above
the offers.
`rating_details` and `top_reviews` appear only with `--rating` and `--reviews`:
the former contains the numeric average, count, and 5→1 histogram; the latter
contains the reviews already present on the detail page (usually 8 local + 5
international), with author, stars, title, date, variant, text, verified
purchase, and helpfulness. The full reviews page requires sign-in, so it is not
requested. `--review-limit` limits the items and `--review-images` adds image
URLs. On Amazon.com the page may show "Sign in to see customer reviews": in that
case `top_reviews` is omitted, a warning is printed, and rating and histogram
remain available; with authenticated cookies via `--cookie`, the parser uses any
reviews present. `amazon_choice`, `amazon_choice_text`, and `amazon_choice_for`
appear only if the detail page exposes the badge; the Web detail page displays
it under the rating. `import_fees` contains the
estimated amounts and the landed total, or only the "Import charges applied at
checkout" disclosure; it is omitted when the block is not present. `price` is
the price used in the import table and `price_basis` is `current`, `list`, or
`other`: Amazon may calculate the total on the list price even if the buybox
shows a discount. The table flags `Import price ... (list price)` and the
comparison view uses `Landed` with `*` and a note. `deal` is present only if the
page shows a badge (for example `Black Friday anticipado`, `Limited time deal`,
`Offerta a tempo`). The `deal_id`, `deal_ends_text`, `deal_ends_on`,
`deal_ends_in_days`, and `deal_has_countdown` fields add the identifier, expiry,
and presence of the countdown. Amazon exposes only the end date and the
`NO_OF_HOURS/MINUTES/SECONDS` templates; the countdown seconds are calculated by
the page's JavaScript and are not in the server HTML. If Amazon does not expose
the percentage but shows the current price and original price, `discount` is
calculated from those values. The coupon is normalized to a user-facing
percentage (for example `-30%`), not to the promotion's internal code.

If the buybox shows only **“Visualizza tutte le opzioni di acquisto”** (no
featured offer), `price`, `original_price`, and `discount` remain empty: the
parser does not use prices from carousels or related products. The actual
options are still loaded via AOD, unless `--no-buying-options` or `--html` is
used.

#### Buying options (offer listing ID)

When the product page shows a summary such as "Nuovo (7) da 87,25 €" or when the
buybox has no featured offer, `details` makes an additional request to the AOD
page:

```text
GET /gp/product/ajax/aodAjaxMain/ref=dp_aod_unknown_mbc?asin=<ASIN>&...&pc=dp
X-Requested-With: XMLHttpRequest
Referer: https://www.amazon.it/dp/<ASIN>
```

The request uses the same session/cookie jar as the product, so it also honors
any `--cap`. A `--options-delay` pause (default 1500 ms) is made before the
request. If AOD is blocked or fails, the product is still returned and a warning
appears on stderr. In `--compare` (table/TSV) the AOD request is skipped because
the comparison does not show the options; with `--compare --json` it is
executed.

When the buybox has a featured offer, Amazon lists only the **other** sellers in
AOD. The CLI therefore also reads the featured offer's `offerListingID` and
`merchantID` from the product page and adds it at the top of the list; the other
offers follow in AOD order. Deduplication is by `offer_listing_id`, and if the
same seller/price/condition appears with a different code, the AOD entry is
preferred. So there are no duplicates if Amazon also includes the buybox in the
list.

In marketplaces with new and used items the summary may be mixed, for example
`新品＆中古品 (50)件の出品：￥6,100` on Amazon.co.jp. In that case the featured
offer's condition is read from the active buybox row
(`newAccordionRow`/`usedAccordionRow`), not from the summary: the featured offer
may be `新品` while the summary says `新品＆中古品`. If the accordion is not
present (for example on Amazon.fr), `buyingOptionType` (`NEW`/`USED`) is used to
choose the corresponding part of the mixed summary: `Neuf & D'occasion` + `NEW`
→ `Neuf`.

AOD is paginated: Amazon returns 10 offers per page. By default `options`
contains the first page plus any featured offer, while `count` is the declared
total. For example, in Japan `count` can be 50 and `options` 11 (10 AOD +
featured offer).

With `--all-options` the CLI loads subsequent pages up to the total (max 10
pages / 100 offers); with `--options-pages N` it is limited to `N` pages. Pages
2+ use:

```text
GET /gp/product/ajax/aodAjaxMain/ref=aod_page_N
    ?asin=<ASIN>&...&pc=dp&isonlyrenderofferlist=true&pageno=N
```

Between pages, `--options-delay` is honored. If a page fails, the options
already collected remain valid and a warning appears on stderr. With
`--save-aod-html`, page 1 goes to the specified file and subsequent pages to
`-p2`, `-p3`, ... If the page shows only the "all options" link without a
recognized summary, the CLI still tries to load AOD through that link.

Fields of each option:

| JSON field | Meaning |
|---|---|
| `offer_listing_id` | Offer code decoded once; it is the logical value Amazon uses in `items[0.base][offerListingId]`. It contains literal `+` and `/`: if you resend it in a form/URL, encode it with `Uri.EscapeDataString` |
| `seller` | Seller name |
| `seller_id` | Seller merchant ID (`seller=` / `smid=`) |
| `price` | Offer price |
| `shipping` | Shipping cost shown by Amazon (`GRATUITA`, `a 15€`, ...) |
| `condition` | Raw localized condition (`Nuovo`, `Usato`, `Ricondizionato: premium`, ...) |
| `condition_type` | Canonical type: `new`, `used`, `refurbished` |
| `condition_grade` | Renewed grade: `premium`, `excellent`, `good`, `acceptable` |
| `delivery` | Offer delivery time |
| `detail_url` | Product URL with `?smid=<seller_id>&psc=1`, i.e. the seller's offer |

The Web UI localizes `condition_type` and `condition_grade` and shows the grade
as a separate colored tag; the raw `condition` is kept as a tooltip and fallback.
`condition_grade` is populated for Amazon Renewed offers such as
`Ricondizionato: premium`, `Refurbished - Excellent`, or `整備済み品 - 良い`.

`count` and `lowest_price` come from the product page summary when available;
otherwise `count` comes from the AOD `aod-total-offer-count` field and
`lowest_price` is the minimum option price. `count` may be greater than
`len(options)` because AOD loads only the first page of offers.

`offer_listing_id`, prices, shipping, and availability are runtime values: they
can change between sessions and expire. They are not credentials, but they
should not be treated as permanent.

#### Analyzing a single option

Take `seller_id` from the options output and pass it to `--smid`. The CLI opens
the same URL as `detail_url` (`?smid=<seller_id>&psc=1`) and analyzes that
seller's offer: price, seller, shipping, availability, delivery, and offer code.

```bash
amz-cli details B07R3YS8R2 --smid A131AJORA55M3D --json
amz-cli details B07R3YS8R2 --smid A131AJORA55M3D --tk-opt
```

In this case the detail `url` field contains `?smid=...&psc=1`. `--smid`
requires a single ASIN, is not compatible with `--html` or `--compare`, and
works with `--cap`. If the seller is no longer available, Amazon may show
another offer or none: report what appears on the page.

#### TSV detail

```text
ASIN
URL
Title
Brand
Price
Original
Discount
Deal
DealId
DealEnds
DealEndsOn
DealEndsInDays
DealCountdown
Rating
Reviews
AmazonChoice
AmazonChoiceText
AmazonChoiceFor
ImportPrice
ImportPriceBasis
ImportDestination
ImportShipping
ImportCharges
ImportLandedTotal
ImportAtCheckout
ImportText
Seller
ShippedBy
Returns
Avail
DeliveryLocation
DeliveryCountry
DeliveryPostalCode
DeliveryCost
DeliveryTime
DeliveryCutoff
DeliveryOption N
VariantsCount
VariantsParent
RatingAverage
RatingCount
Rating5
Rating4
Rating3
Rating2
Rating1
ReviewsAvailable
ReviewsLocal
ReviewsInternational
Review
ReviewImage
Coupon
Image
BuyingOptionsCount
BuyingOptionsFrom
BuyingOption
```

The `Delivery*` lines appear only if delivery is available. The `Deal` line
appears only if the page exposes a deal badge; `DealId`, `DealEnds`, `DealEndsOn`,
`DealEndsInDays`, and `DealCountdown` appear when the data is available.
Description, specifications, and gallery are not included in TSV: use `--json`
or the table.

The `Rating*` lines appear with `--rating`; `ReviewsAvailable`, `ReviewsLocal`,
`ReviewsInternational`, and `Review` with `--reviews`. Each `Review` line
contains, separated by TAB: id, rating, author, title, date, verified purchase,
helpful, variant, text, and URL. `ReviewImage` appears only with
`--review-images`.

The `AmazonChoice*` lines appear only if the badge is present; `Import*` appear
when Amazon exposes the import block (numeric amounts or disclosure only).
`ImportAtCheckout` is `true` when the amount is applied only at checkout. The
detail table shows `Choice`, `Import fees`, `Import charges`, and `Landed total`;
`--compare` adds `Choice` and `Landed` when at least one product has the data.

`BuyingOptionsCount` and `BuyingOptionsFrom` appear only if the summary is
available. Each `BuyingOption` line has these fixed fields, separated by TAB:

```text
BuyingOption <indice> <offer_listing_id> <seller> <seller_id> <price> <shipping> <condition> <delivery> <detail_url>
```

Example:

```text
BuyingOptionsCount	7
BuyingOptionsFrom	87,25 €
BuyingOption	1	0F67NTGp...J69dC	Furan	A131AJORA55M3D	87,25 €	GRATUITA	Nuovo	21 - 29 dicembre	https://www.amazon.it/dp/B07R3YS8R2?smid=A131AJORA55M3D&psc=1
```

#### Compare TSV

```text
ASIN	Title	Brand	Price	Original	Discount	Coupon	Rating	Reviews	Seller	ShippedBy	Avail	Returns	DeliveryCost	DeliveryTime	DeliveryLocation	AmazonChoice	ImportPrice	ImportPriceBasis	ImportCharges	LandedTotal	ImportAtCheckout	ImportText
```

### `amz-cli library`

The saved products library is shared with the local web interface. Both read
and write the same state file, so a product saved from the CLI appears in the
web UI and vice versa.

```bash
amz-cli library add B0H25NG1JK
amz-cli library add https://www.amazon.it/dp/B0H25NG1JK --domain it
amz-cli library add B0H25NG1JK --group g_laptops --quantity 2
amz-cli library list
amz-cli library list --group "Laptop ufficio"
amz-cli library list --json
amz-cli library show B0H25NG1JK --json
amz-cli library remove B0H25NG1JK

amz-cli library group create "Laptop ufficio" --accent blue \
  --description "Portatili per il lavoro"
amz-cli library group edit "Laptop ufficio" --accent violet \
  --description "Portatili aziendali"
amz-cli library group list --json
amz-cli library group show g_laptops
amz-cli library group add g_laptops B0H25NG1JK
amz-cli library group remove g_laptops B0H25NG1JK
amz-cli library group rename g_laptops "Laptop casa" --accent rust
amz-cli library group delete g_laptops
```

- `library add` accepts an ASIN, `domain:ASIN`, or Amazon URL; it fetches the
  product details and saves the snapshot (title, image, price, currency,
  delivery, charger).
- Re-adding an existing product refreshes its snapshot without changing the
  original `saved_at`.
- `library group create` creates an empty group and does **not** start
  monitoring. Deleting a group keeps its products in the library.
- Groups can have an optional description (max 500 characters). Use
  `--description` with `group create`, `group rename`, or `group edit`;
  `group edit --clear-description` removes it. Preset colors (`accent`, `blue`,
  `gold`, `rust`, `green`, `teal`, `violet`, `rose`, `slate`, `orange`, `plum`,
  `sky`), custom `#rrggbb` colors, and the Web color picker are stored in the
  same library file.
- Saved products keep `saved_at` and `price_at_save`; `price_last`, `snapshot`,
  and `last_checked_at` follow verified detail fetches. When the buybox has no
  direct price, the lowest buying-option price is used. `details` updates an
  existing saved product after a live fetch (`--html` does not). The Web marks
  products older than seven days as needing a check and rechecks them when
  opened. The group page can refresh prices manually; products checked in the
  last 24 hours are skipped and requests are sequential.
- Groups can be referenced by id or by a unique name. Ambiguous names require
  the id.
- `--json` returns the full library document with `schema`, `revision`,
  `products`, and `groups`; `--tk-opt` returns tab-separated rows, including
  group descriptions.
- The shared file is `library.json` in the per-user state root (see
  `amz-cli paths`). The old library inside `web/ui-state.json` is migrated
  automatically.

### `amz-cli monitor` — library targets

Saved-library monitoring is explicit. Saving a product does not start a
monitor.

```bash
amz-cli monitor --library-product B0H25NG1JK --max-price 600 --timer 3600
amz-cli monitor --library-group "Laptop ufficio" --total-max 1200 --timer 3600
amz-cli monitor --library-group g_laptops --total-max 2500 \
  --currency EUR --fx GBP=1.17
amz-cli monitor --binding group:g_laptops --timer 3600
amz-cli monitor --list-bindings --json
amz-cli monitor --remove-binding group:g_laptops
```

- `--library-product` accepts a saved ASIN, `domain:ASIN`, or URL.
- `--library-group` accepts a group id or unique name.
- The group is rebuilt from the library on every cycle: additions and removals
  are picked up live. Library monitoring uses timer mode; adaptive scheduling is
  currently for legacy targets.
- A product that belongs to a group cannot be monitored individually; the
  command shows the group and suggests monitoring the group instead.
- Multi-currency groups require a target currency and one manual FX multiplier
  per other currency: `--currency EUR --fx GBP=1.17`. `--total-max` is in the
  target currency; item `--max-price` remains in the marketplace currency.
- Bindings are stored in `<state root>/monitor-bindings.json` and include
  runtime defaults (timer, request delay, save path/file, format, layout, and
  notifications). `monitor --binding` uses them; explicit flags override them.
- Runs expose `binding_id` in `monitor --list --json`, so the Web Monitoring
  page can join running processes, bindings, and data files.
- The Web Monitoring page shares the same bindings, registry, and data files.
  It can create/edit/start/stop/restart library monitors, edit all binding
  thresholds (including baseline, per-product overrides, runtime, and
  notifications), install/remove/verify automatic startup, switch the monitor
  list between grid and list, show the Amazon marketplace and a direct product
  link, and displays a full-width local price chart with a persistent hover
  readout and absolute/percentage thresholds. Advanced settings stay
  collapsed and keep their defaults. HTTP/domain/cookie and anti-bot options
  remain per-run CLI options and are not stored in the Web binding file.
  CLI-created runs remain visible.
- `monitor --list` shows active processes. Configured but stopped bindings are
  shown by `monitor --list-bindings`, whose table includes a `Status` column.
- `--startup` with a library target saves `--binding <id>` so threshold changes
  apply at the next login without reinstalling startup.
- `amz-cli library import-monitor monitor.json` imports legacy monitor groups and
  items into the library and preserves their thresholds as bindings. It does not
  start monitoring.

### `amz-cli monitor`

Monitors one or more products in sequential cycles, saves observations, and
generates alerts when price or group-total thresholds are crossed.

```bash
amz-cli monitor B07Q6Y5SC7 --timer 3600 --save-format json --save-path ./monitor
amz-cli monitor it:B07Q6Y5SC7 de:B0DP1VG1DZ --timer 3600 --save-path ./monitor
amz-cli monitor --asin-file asins.txt --once --save-format csv --save-path ./monitor
amz-cli monitor --config monitor.json
amz-cli monitor --config monitor.json --detach
amz-cli monitor --config monitor.json --status
amz-cli monitor --config monitor.json --stop
amz-cli monitor --list
amz-cli monitor --list --json
amz-cli monitor --list --all
amz-cli monitor --list --prune
amz-cli monitor --config monitor.json --startup
amz-cli monitor --config monitor.json --startup-remove
```

#### Targets and domains

| Form | Domain |
|---|---|
| `B07Q6Y5SC7` | `--domain` (default `it`) |
| `it:B07Q6Y5SC7` | explicit |
| `us:B07Q6Y5SC7` | `com` |
| `https://www.amazon.co.uk/dp/B0D9YDFGHD` | inferred from the host |
| `https://www.amazon.es/dp/B0CGVQNB9F` | inferred from the host |

`--asin-file` accepts one target per line, `#` comments, commas, or CSV/TSV with
a `domain,asin` header. The same ASIN on different domains is a distinct target:
the key is `(domain, asin)`. In this version a product can belong to only one
group.

#### Main options

| Option | Description |
|---|---|
| `--timer <SECONDS>` | Interval between cycles; min 10, default 3600 |
| `--schedule adaptive` | Enables the adaptive per-window budget |
| `--window <SECONDS>` | Adaptive window duration, default 86400 |
| `--max-requests <N>` | Maximum product requests per adaptive window |
| `--min-gap <SECONDS>` | Minimum gap per unit, default 600 |
| `--max-gap <SECONDS>` | Maximum randomized gap, default 2×min-gap |
| `--request-delay <SECONDS>` | Pause between product requests; min 6, default 10 |
| `--jitter <SECONDS>` | Extra random wait before the next cycle |
| `--save-format json\|jsonl\|csv` | `json`/`jsonl` = JSON Lines; default json |
| `--save-path <DIR>` | Data folder, default `.` |
| `--save-file <BASE>` | Base name, default `amz-monitor` |
| `--layout auto\|combined\|per-group\|single` | File layout; `auto` uses per-group when there are groups |
| `--config <FILE>` | JSON configuration with items, groups, and thresholds |
| `--asin-file <FILE>` | ASIN list from a file |
| `--once` | Single cycle; for cron/systemd |
| `--cycles <N>` | Number of cycles |
| `--cap <DOMAIN:POSTAL>` | Postal code per domain, repeatable; without a prefix only with one domain |
| `--country <DOMAIN:ISO>` | Delivery country per domain, repeatable |
| `--domain-cookie <DOMAIN:HEADER>` | Cookie per domain, repeatable; requires the prefix |
| `--max-price <AMOUNT>` | Threshold for CLI targets, in the marketplace currency |
| `--drop-percent <PCT>` | Alert on a percentage drop below the baseline, 0-100 |
| `--rise-percent <PCT>` | Alert on a percentage rise above the baseline, 0-100 |
| `--baseline <BASE>` | `original`, `first_seen`, `max_seen`, `last_seen` |
| `--cooldown <SECONDS>` | Alert cooldown for CLI, default 21600 |
| `--exit-on-alert` | Exit code 3 if at least one alert fires |
| `--dry-run` | Validates everything without network access |
| `--detach` | Starts in the background |
| `--status` / `--stop` | Status/stop of the background process |
| `--list` | Lists registered active monitors |
| `--all` | With `--list`, includes stale entries |
| `--prune` | With `--list`, removes stale entries |
| `--json` / `--tk-opt` | With `--list`, JSON/TSV output |
| `--verbose` | With `--list`, full paths |
| `--startup` | Installs autostart and starts the monitor |
| `--startup-remove` | Removes autostart |
| `--notify` | Enables desktop notifications |
| `--notify-on <EVENTS>` | `alerts`, `errors`, `blocks`, `fatal`, `all`; default alerts |
| `--notify-cooldown <SECONDS>` | Error/block cooldown, default 3600 |
| `--notify-test` | Test notification, without network access and without targets |
| `--quiet` | Errors, alerts, and summary only |
| `--log-path <FILE>` | Additional log |

A bare `--cookie` works only with one domain. With multiple domains use
`--domain-cookie it:<HEADER>`, `--domain-cookie de:<HEADER>`, etc.

#### JSON configuration

```json
{
  "schema_version": 1,
  "domain": "it",
  "timer_seconds": 3600,
  "request_delay_seconds": 10,
  "save": { "path": "./monitor", "format": "json", "layout": "auto" },
  "notifications": {
    "enabled": true,
    "on": ["alerts", "errors", "blocks", "fatal"],
    "cooldown_seconds": 3600
  },
  "items": [
    {
      "asin": "B07Q6Y5SC7",
      "max_price": 30.00,
      "drop_percent": 5,
      "rise_percent": 10,
      "baseline": "first_seen",
      "cooldown_seconds": 21600
    }
  ],
  "groups": [
    {
      "id": "pc-build",
      "name": "PC Build",
      "currency": "EUR",
      "total_max": 1200.00,
      "allow_incomplete": false,
      "items": [
        {
          "asin": "B08JH7KQKV",
          "quantity": 1,
          "max_price": 250.00,
          "drop_percent": 5,
          "rise_percent": 10,
          "baseline": "first_seen"
        },
        {
          "asin": "B0DP1VG1DZ",
          "domain": "de",
          "quantity": 2
        }
      ]
    }
  ]
}
```

#### Adaptive scheduling

Timer mode with `timer_seconds` remains the default. The `schedule` block adds a
per-window budget with randomized gaps:

```json
{
  "schedule": {
    "mode": "adaptive",
    "window_seconds": 86400,
    "max_requests": 500,
    "min_gap_seconds": 600,
    "max_gap_seconds": 1200
  }
}
```

- Each product outside a group is a unit with cost 1.
- Each group is a unit with a cost equal to the number of items in the group.
- `max_requests` is mandatory and also counts requests made inside groups.
- The budget is distributed with a token bucket, so it is not exhausted
  immediately.
- The window starts when the monitor starts, not on the wall clock.
- On restart the budget is resumed; excess units are discarded.
- `--once`, `--cycles`, and `--html` use cycle mode and ignore the adaptive
  schedule.

Example: 500 requests/day with a group of 8 → at most ~62 cycles/day, about one
every 23 minutes. The minimum gap does not guarantee a higher frequency.

```bash
amz-cli monitor --config monitor.json --schedule adaptive \
  --window 86400 --max-requests 500 --min-gap 600 --max-gap 1200
```

`--timer` and the adaptive options are not compatible. Gaps can be overridden
per item or group with `min_gap_seconds` and `max_gap_seconds`; inside a group
they are configured on the group.

#### Rules

- `max_price`: fires if `price <= max_price`.
- `drop_percent`: fires if `price <= baseline * (1 - drop_percent / 100)`.
- `rise_percent`: fires if `price >= baseline * (1 + rise_percent / 100)`.
  Drop and rise rules can be active together.
- `baseline`:
  - `original`: struck-through price on the page;
  - `first_seen`: first observed price;
  - `max_seen`: maximum observed;
  - `last_seen`: price from the previous cycle;
  - `fixed`: requires `baseline_price`.
- `cooldown_seconds`: default 21600. Triggers on the `false -> true` transition;
  if the condition remains true, it repeats at most every cooldown.
- Group: `total = sum(price * quantity)`; fires if `total <= total_max`.
- If a product in the group has no price, the group is `incomplete` and does not
  fire, unless `allow_incomplete: true`.
- The total uses the current buybox price: no coupons, shipping, or FX.
- A group can contain different domains only with the same currency (IT/DE/FR/ES
  are EUR). An IT+UK group is rejected.

#### Files and alerts

`--layout auto` chooses `per-group` if there are groups, otherwise `combined`.

```text
combined:
  amz-monitor.items.jsonl
  amz-monitor.groups.jsonl
  amz-monitor.alerts.jsonl
  amz-monitor.state.json

per-group JSONL:
  amz-monitor/pc-build.jsonl
  amz-monitor/ungrouped.jsonl

per-group CSV:
  amz-monitor/pc-build/items.csv
  amz-monitor/pc-build/groups.csv
  amz-monitor/pc-build/alerts.csv
```

The records contain `domain`, `group_id`, `triggered_rules`, `price_amount`,
`currency`, `deal`, `deal_id`, `deal_ends_text`, `deal_ends_on`,
`deal_ends_in_days`, `deal_has_countdown`, `coupon`, `error`. Alerts are
append-only and contain `rule_id`, `threshold`, `observed`, `message`. The
persistent state `amz-monitor.state.json` stores baselines, conditions, and
cooldowns so alerts are not duplicated every cycle.

#### Background

`--detach` starts a separate process with a pidfile and log. On Linux it uses
`setsid` when available. `--status` shows PID, targets, timer, log, and data
files; `--stop` sends SIGTERM and waits for shutdown. The pidfile does not
contain cookies. Alternatively, use `--once` with cron/systemd.

#### Active monitors

`--list` shows all monitors started by the user, even from other folders or with
`--detach`:

```bash
amz-cli monitor --list
amz-cli monitor --list --json
amz-cli monitor --list --tk-opt
amz-cli monitor --list --all
amz-cli monitor --list --prune
amz-cli monitor --list --verify
amz-cli monitor --list --save-path ./monitor
amz-cli monitor --stats
amz-cli monitor --stats --json
amz-cli monitor --restart
amz-cli monitor --restart --json
```

Per-user registry:

```text
Linux:   $XDG_STATE_HOME/amz-cli/monitors/*.json
         fallback ~/.local/state/amz-cli/monitors/*.json
Windows: %LOCALAPPDATA%\AmzCLI\monitors\*.json
```

The entry is removed on normal exit, with Ctrl+C or SIGTERM. If the process is
killed with SIGKILL, crashes, or the Windows task is terminated, the entry
remains. On the next start the registry is reconciled: stale entries for the same
monitor (same startup id or same `--save-path`/`--save-file`) are removed
automatically. The check uses the PID and start time; if the time is unavailable
it also compares the executable path, so it does not mistake a reused PID.

- default: active monitors only;
- `--all`: includes stale entries from other monitors; duplicates of an active
  monitor remain hidden;
- `--prune`: removes the registry and pidfile of stale entries, including
  duplicates; if the pidfile has already been reused by another monitor, it is
  not deleted;
- `--verify`: verifies that the Windows task or systemd unit really exists;
  adds `startup_registered` to JSON/TSV output;
- `--save-path <DIR>`: also scans that folder for unregistered pidfiles (useful
  for monitors started with earlier versions);
- `--verbose`: full log and pidfile paths;
- `--json` / `--tk-opt`: script-friendly output.

The table shows PID, status, mode (`foreground`/`detach`), domains, targets,
timer, start, last cycle, and data files. The last cycle is the maximum
`last_seen_at` in `amz-monitor.state.json`. If there are stale entries and you do
not use `--all`/`--prune`, the CLI warns with "1 stale entry. Use --all to see it
or --prune to remove it." Without monitors:

```text
No active monitors.
```

`--list` does not require an ASIN or config and is not compatible with
`--status`, `--stop`, `--detach`, `--once`, `--cycles`, `--html`, `--dry-run`,
or `--notify-test`.

#### Price summary (`--stats`)

Reads only the local state, without network access:

```bash
amz-cli monitor --stats --save-path ./monitor
amz-cli monitor --stats --save-path ./monitor --json
```

Shows first/minimum/maximum/last price per target, last cycle, number of
requests/observations, and number of rules that generated at least one alert. The
count combines the counter in the state with the `item` rows in the monitor's
JSONL history, when the data file is registered. For groups it shows name, items,
total/minimum/maximum, last cycle, requests, and alerts. With `--json` it returns
`state_path`, `targets`, `groups`, `alerted_rules`, `target_rows`, and
`group_rows`; each row includes `observations`.

#### Restart (`--restart`)

Restarts the monitors of the current installation:

```bash
amz-cli monitor --restart
amz-cli monitor --restart --save-path ./monitor --save-file amz-monitor
amz-cli monitor --restart --json
```

The command blocks the restart if a foreground monitor or a detached one without
restart metadata is active. On Windows it uses the task; on Linux it uses the
pidfile/process. With `--json` it returns `stopped`, `restarted`, and `errors`.

#### Autostart (`--startup`)

`--startup` installs autostart for the user and starts the monitor immediately:

```bash
amz-cli monitor --config monitor.json --startup
amz-cli monitor it:B07Q6Y5SC7 --timer 3600 --save-path ./monitor --startup
amz-cli monitor --config monitor.json --startup-remove
amz-cli monitor --save-path ./monitor --startup-remove
```

If you run the removal from another folder, use an absolute `--save-path`.

Autostart runs at user logon, not as a system service: this preserves the user
environment, cookies, and desktop notifications.

**Linux**: systemd user service:

```text
~/.config/systemd/user/amz-monitor-<hash>.service
~/.local/state/amz-cli/startups/<hash>.json
```

```bash
systemctl --user status amz-monitor-<hash>.service
systemctl --user restart amz-monitor-<hash>.service
systemctl --user stop amz-monitor-<hash>.service
journalctl --user -u amz-monitor-<hash>.service -n 50
```

To start at boot without logging in: `loginctl enable-linger "$USER"`. Desktop
notifications still arrive only after graphical login.

**Windows**: Task Scheduler task for the current user, without admin rights:

```text
Nome: AmzCLI-<hash>
Trigger: all'accesso dell'utente
Azione: amz-notify.exe --startup-launch <hash>
```

`amz-notify.exe` is a WinExe already included in the publish: it starts
`amz-cli.exe` without a console window. In this mode it does not send
notifications: it is reused only as a graphical launcher. Stop and removal also
first try to stop the monitor with `amz-cli monitor --stop` and only then
terminate the task. The task contains no cookies and not the full command line;
the arguments remain in the spec:

```text
%LOCALAPPDATA%\AmzCLI\startups\<hash>.json
```

On Windows, autostart is at logon: a service in session 0 could not show toasts
or use the user environment.

The list shows the `Startup` column:

```text
PID    Status      Mode      Startup
12345  running     startup   systemd
—      configured  startup   systemd
```

- `--list`: active monitors only, with `Startup` filled in.
- `--list --all`: also includes configured but inactive startups.
- In configured rows the PID is `0` and the JSON status is `configured`.
- `--status`: shows the unit/task and the monitor's startup status.
- `--stop`: stops only the current instance; it does not disable startup.
- `--startup-remove`: disables and removes the unit/task; stops the monitor if
  it was started by autostart.

If you change `--config`, `--save-path`, or `--save-file`, reinstall with
`--startup`. If you move the binary folder, reinstall autostart.

`--startup` is not compatible with `--detach`, `--status`, `--stop`, `--list`,
`--once`, `--cycles`, `--html`, `--dry-run`, `--notify-test`, or
`--exit-on-alert`.

#### Desktop notifications

```bash
amz-cli monitor --config monitor.json --notify
amz-cli monitor --config monitor.json --notify --notify-on all
amz-cli monitor --notify-test
```

Or enable `notifications.enabled: true` in the JSON; CLI flags take precedence
over `on` and `cooldown_seconds`.

Events:

- `alerts`: price/percentage/group alert actually emitted;
- `errors`: digest of cycle errors (404, timeout, network);
- `blocks`: Amazon/WAF block, domain in cooldown or disabled;
- `fatal`: monitor stopped due to a fatal error;
- `all`: all events.

Linux uses `notify-send` (libnotify), with `zenity`/`kdialog` fallback. On
Debian/Ubuntu: `sudo apt install libnotify-bin`. If the monitor runs without a
graphical session (system service, headless cron), the desktop notification
cannot be shown: use a systemd user service or, in the future, a webhook.

Windows uses the native `amz-notify.exe` helper included in the Windows publish
(self-contained, no additional runtime): it is the one that sends the toast, so
Windows attributes it to **AMZ CLI Monitor**, not PowerShell. If the helper is
missing or fails, the CLI falls back to PowerShell: BurntToast if installed
(`Install-Module BurntToast -Scope CurrentUser`), then WinRT toast, finally
`msg.exe`. `--notify-test` shows `windows/native` or the fallback used. An
interactive user session is required: with Task Scheduler use "Run only when
user is logged on"; a Windows service in session 0 cannot show toasts.

Clicking a Windows toast opens a Spectre.Console terminal window with the
details: product, current price, previous price, discount, threshold, coupon,
and badge. The click does not resend the notification: the helper recognizes the
activation and only shows the details. The test notification opens a window
explaining that it is a test and closes when a key is pressed: the window shows
"Press any key to close." Payloads are in
`%LOCALAPPDATA%\AmzCLI\notifications` and are cleaned up after 7 days.

Price alerts use the rules' cooldown. Errors and blocks use `--notify-cooldown`
(default 3600s) and persistent state, so they are not repeated every cycle. If
the backend is unavailable the monitor continues and writes a warning;
`--notify-test` verifies the setup and returns an error if the notification does
not start.

#### Exit codes

- `0`: ok;
- `2`: cycle completed with partial errors;
- `3`: alert with `--exit-on-alert`;
- `1`: fatal error;
- `130`: interruption.

### `amz-cli --install-env-var` (Windows only)

Adds the `amz-cli.exe` folder to the user PATH, so the command works from any
terminal:

```powershell
amz-cli --install-env-var
amz-cli --install-env-var system
amz-cli --status-env-var
amz-cli --uninstall-env-var
amz-cli --uninstall-env-var system
```

- default: user PATH (`HKCU\Environment`), no admin;
- `system`: system PATH, requires an administrator terminal;
- `--status-env-var`: shows user PATH, system PATH, and `where amz-cli`;
- after installation, reopen the terminal;
- if you move the folder, remove and reinstall the entry.

On Windows `amz-cli --help` also shows the equivalent command:

```powershell
amz-cli env-var --install
amz-cli env-var --install --system
amz-cli env-var --status
amz-cli env-var --uninstall
```

On Linux the flag returns an error and `env-var` is not registered in the help.

### `amz-cli update`

Checks for and applies signed CLI updates through GitHub Releases.

```bash
amz-cli update --check
amz-cli update --check --json
amz-cli update
amz-cli update --yes
amz-cli update --yes --json
amz-cli update --channel beta
amz-cli update --status
amz-cli update --rollback --yes
amz-cli update --dry-run
amz-cli update --enable-auto
amz-cli update --disable-auto
```

- `--check` checks the version without downloading or modifying anything.
- `--dry-run` also shows the files that would be downloaded.
- `--yes` skips the prompt; it is required with `--json` or non-interactive
  input.
- `--channel` accepts `stable` (default) or `beta`.
- `--status` shows the latest journal and local backups of the current
  installation.
- `--rollback` restores the latest backup of the current installation.
- `--enable-auto` / `--disable-auto` enable or disable auto-update.

The update:

1. reads the signed platform feed from GitHub Releases;
2. verifies the manifest's RSA-PSS signature and each file's SHA-256 hashes;
3. downloads the compressed files from the GitHub assets listed in the feed;
4. creates a `.tar.gz` backup of the installation, excluding `data/`, `config/`,
   and the monitor state;
5. replaces the files and runs the new binary with `__update-verify`;
6. considers the update complete only if the test passes; otherwise it restores
   the previous files.

Backups are in `~/.local/state/amz-cli/updates/backups` on Linux and in
`%LOCALAPPDATA%\AmzCLI\updates\backups` on Windows. The journal is
`updates/journal.json`.

On Linux curl-impersonate comes from the system package manager and is not
replaced by the updater. On Windows the native DLLs are bundled in the signed
package, included in the backup, replaced by the update, and verified after
apply (the update requires curl to load on Windows).

The local Web **Status** page shows the curl-impersonate dependency: on Linux
the system executable, path, and version; on Windows the bundled
`libcurl-impersonate.dll`, `curl_shim.dll`, and `zlib.dll` with their status and
loaded version. It offers the same signed update flow with a **Check for
updates** button and live progress. An update started there closes
and restarts the Web interface automatically, preserving its port and session,
then reloads the page. Active monitors are restarted when possible; monitors
that cannot restart must be started manually. Running searches must be
cancelled before applying.

Release 2.26.0 is the bootstrap: earlier versions do not have the `update`
command, so the first installation must be done manually. From 2.26.1 onward the
update is automatic; 2.27.0 introduces auto-update.

## Auto-update

Auto-update is enabled by default, on the `stable` channel, with an interval of
3600 seconds. When `search`, `details`, or `deals` starts, a background check
runs; if a monitor is active, its cycle checks every hour. A `--startup` or
`--detach` monitor is stopped only after download, hash, and backup, then
restarted. A foreground monitor is not touched: the update is postponed.

```bash
amz-cli update --enable-auto
amz-cli update --disable-auto
amz-cli monitor --no-auto-update ...
```

Configuration: `updates/config.json` under the state directory
(`~/.local/state/amz-cli` on Linux, `%LOCALAPPDATA%\AmzCLI` on Windows).
`AMZ_NO_AUTO_UPDATE=1` disables everything. The notification is informative;
clicking opens the details on Windows and, if available, a `zenity`/`kdialog`
dialog on Linux. It does not ask to confirm the update: the decision is already
automatic.

The actual update is performed by a temporary helper copy of the executable on
both platforms. On Windows this is necessary because the running binary cannot
be replaced; on Linux it prevents the running process from loading assemblies
from the just-replaced file. Backups are filtered by installation directory, so
`--rollback` does not use backups from other copies.

### `amz-cli compare`

Pushes products into the shared comparison tabs used by the Web UI. The
comparison is stored in `<state root>/web/compare.json` and an open Web UI
updates in real time.

```bash
amz-cli compare add it:B0H25NG1JK de:B0FL85P2QS
amz-cli compare add B0H25NG1JK --domain it --source agent
amz-cli compare show
amz-cli compare show --diff --width 220
amz-cli compare show --json
amz-cli compare show --tk-opt
amz-cli compare show --refresh
amz-cli compare list
amz-cli compare list --json
amz-cli compare remove it:B0H25NG1JK
amz-cli compare clear
```

`compare show` fetches the products in the shared comparison and prints a
commerce table. `--diff` transposes it: all fields and specification keys are
rows, missing values are `—`, and `●` marks differences. Use `--json` for full
product details, `--diff --json` for the attribute matrix, `--tk-opt` for TSV,
and `--refresh` to bypass the product cache.

The maximum is six products. The Web page shows up to three compact product
dossiers side by side on desktop and one at a time on phones. Agents should use
`--source agent` when they push products.

### `amz-cli settings`

Saves the HTTP defaults shared by the CLI, the Web UI and monitors. Command-line
flags always win over the saved values.

```bash
amz-cli settings show
amz-cli settings show --json
amz-cli settings set --transport curl --browser Firefox147 --timeout 45
amz-cli settings set --browser auto --user-agent auto
amz-cli settings set --browser-headers
amz-cli settings set --no-fallback
amz-cli settings reset
amz-cli settings path
```

The file is `http-settings.json` in the state directory:

- Linux: `~/.local/state/amz-cli/http-settings.json`
- Windows: `%LOCALAPPDATA%\AmzCLI\http-settings.json`

`settings show` prints the saved values, the effective transport/profile for
each marketplace, the curl-impersonate runtime and the active HTTP cooldowns.
`--transport`, `--browser`, `--user-agent`, `--browser-headers` and `--timeout`
on individual commands remain one-off overrides. Monitors read the file at
startup; restart a running monitor to apply changes.

### `amz-cli paths`

Shows where the CLI has written files, folders, state, monitors, and startup
entries. It is the first command to use when an agent needs to diagnose
permission problems, missing files, stale monitors, or failed updates.

```bash
amz-cli paths
amz-cli paths --all
amz-cli paths --json
amz-cli paths --install
amz-cli paths --save-path ./monitor
amz-cli paths --verbose --ascii
```

- `--all`: also includes known missing paths and empty folders.
- `--json`: structured output with `path`, `kind`, `category`, `status`,
  `exists`, `size`, `modified_at`.
- `--install`: expands the installation files.
- `--save-path`: adds monitors in an unregistered folder.
- `--verbose`: also shows the modification date.
- `--ascii`: tree without Unicode.

It covers installation, per-user state, monitor/startup registry, updater,
Windows notifications, monitor data files, and systemd units/Windows tasks. It
does not read or print file contents: no cookies or sensitive arguments.

Example for agents:

```bash
amz-cli paths --json --all
```

### `amz-cli domains`

Lists the available marketplaces.

### `amz-cli self-test`

Verifies runtime, version, and curl transport. On Linux it shows the version and
path of the system `curl-impersonate` command; on Windows it shows the loaded DLL
version. The `--allow-http` option prevents failure when curl-impersonate is
unavailable.

### `amz-cli --skill` / `amz-cli skill`

Prints this skill in Markdown to stdout, without network access, and exits with
`0`.

To install it in the agent's standard directory:

```bash
amz-cli skill --install
amz-cli skill --check
amz-cli skill --json
```

Default path: `~/.agents/skills/amz-cli/SKILL.md` (Windows:
`%USERPROFILE%\.agents\skills\amz-cli\SKILL.md`). Options:

- `--install`: installs or updates the embedded skill, UTF-8 without BOM;
- `--check`: verifies that the installed copy is up to date; exits with `1` if
  it is missing or different;
- `--dir <DIR>`: skill directory (or `.md` file path);
- `--force`: also replaces a file not recognized as an AMZ CLI skill;
- `--json`: structured output with `path`, `status`, `message`, and `success`.

### `amz-cli doctor`

Local and online diagnostics, without changing state:

```bash
amz-cli doctor
amz-cli doctor --json
amz-cli doctor --offline
amz-cli doctor --channel beta
```

Checks runtime/transport, `install.json`, PATH/symlink, signed update, monitor
registry, actual presence of Windows tasks/systemd units, Windows notification
helper (GUI subsystem), and installed skill. `--offline` skips only the online
release check. The command exits with `1` if it finds errors; warnings do not
block.

### `amz-cli web`

Starts the local web interface (loopback only) for product search and details:

The page is search-first and uses an expandable side rail: it contains search,
future sections, version, theme, and a close button. The field provides
**Search** and **Filters** controls; the filter panel lets you search the same
query on multiple marketplaces, sort with Amazon criteria, and filter by price,
rating, deals, free shipping, and Amazon's Choice. Multi-marketplace results are
grouped by marketplace and cards open on the correct domain. Requests are
sequential with 3 seconds between one domain and the next; an HTTP block is
retried with curl after 4 seconds. While scrolling, the search bar, the results
summary with query/count/view, and the active marketplace header stay fixed.
Results switch between list and grid with the switcher at the top right; a new
search keeps the previous list until the new data arrives. The search bar stays
sticky and compact while scrolling; results have the real Amazon page buttons at
the bottom (read from the HTML paginator), and the old "Pages" control is no
longer in the filters. Viewed pages are cached for 15 minutes; **Search** forces
a new search. The scrollbar gutter is reserved to avoid layout jumps.
Preferences, filters, view, and recent searches are saved in `web/ui-state.json`
in the state directory, not in browser storage. Recent searches are deleted one
at a time with the X next to the item, or all at once with **Clear all**.

The **Deals** item in the rail opens a page for one marketplace at a time, with
tabs **All**/**Today**/**Coupons** (only if supported by the domain), and
categories read in real time from the Amazon page. The **Today's Deals**
shortcut on the search page opens the Today tab directly. The APIs are
`GET /api/v1/deals/options` and `POST /api/v1/deals`, with jobs at
`GET /api/v1/jobs/{id}`. Scrolling has elastic overscroll at the edges and thin
scrollbars throughout the UI.

```bash
amz-cli web --open          # starts and opens the browser
amz-cli web --desktop       # app mode, logs to a file
amz-cli web --status
amz-cli web --stop
```

The URL contains a one-time token in the fragment; the server exchanges it for
an `HttpOnly` cookie. Do not expose the port on a local network. Node.js is not
needed at runtime: the UI is already compiled into the CLI.

The saved products page includes a header search button that expands an inline
field. Products and groups filter while typing; products match title, ASIN, and
marketplace, and groups also match description and contained products. The
filter combines with All / Only singles.

The search field shows Amazon suggestions after a few characters: the arrow keys
fill the field, `Enter` or a click starts the search. Suggestions are optional
and do not block the search if the endpoint does not respond.

### `amz-cli --version`

Prints a compact line with version, channel, and release date:

```bash
amz-cli --version
# amz-cli 3.2.2 (stable, 2026-11-23)

amz-cli --version --short
# amz-cli 3.2.2

amz-cli --version --json
amz-cli --version --verbose
```

The release date comes from the signed manifest of the latest update or from
`release_published_at` in `install.json`. If it is unavailable, the output shows
only the channel; for a source build the channel is `dev`.

## Errors and troubleshooting

| Message / symptom | Cause | What to do |
|---|---|---|
| `HTTP 503` / `challenge AWS WAF` | rate limiting or WAF | wait, increase `--page-delay`, use `--transport http` or `--cookie` |
| `curl-impersonate is not available` | system command missing on Linux | `sudo pacman -S curl-impersonate` or set `AMZ_CURL_IMPERSONATE` |
| `unresolved Akamai challenge` | unresolved challenge | wait a few minutes, do not retry immediately |
| `Page not found on this marketplace (HTTP 404)` | ASIN unavailable | verify the domain or the ASIN |
| `The postal code for IT must have 5 digits` | invalid postal code | use the country format |
| `With --country, also specify --cap` | missing postal code | add `--cap` |
| `Could not set the delivery location` | GLOW flow failed | verify postal code/connection, wait, and retry |
| `--cap requires a network request` | used with `--html` | remove `--html` |
| `With --html you can only read one page` | pagination with a file | use a single page |
| `Buying options unavailable` | AOD blocked or failed | the product is still valid; wait, use `--no-buying-options`, or retry |
| `No results found.` | query with no results or empty page | change query/domain |

Exit codes:

- `0`: success (also with "No results found");
- `1`: fatal command error;
- `255`: Spectre validation error (invalid options).

On `details` with multiple ASINs, a failed ASIN may produce an error on stderr or
a JSON object `{"asin": "...", "error": "..."}` without interrupting the others.

## Rate limiting

- At least **6 seconds** between commands.
- Do not parallelize search/details/monitor.
- `monitor` is always sequential, even with multiple domains: `--request-delay`
  default 10s (minimum 6s) between one product request and the next. `--timer`
  is the interval between cycles, minimum 10s, default 3600.
- With N targets and timer T the budget is about N requests every T seconds. Do
  not lower the timer to recover a missed cycle.
- `details` may make one additional AOD request per product when buying options
  are available; with `--all-options` it can reach 10 AOD requests. Use
  `--no-buying-options` if you do not need them.
- `--options-delay` default 1500 ms between AOD requests; `--page-delay` default
  2000 ms; for `.com`/`.de` use 5000 ms or more.
- `.com` uses HTTP by default; `.es` uses curl-impersonate with Firefox147. If an
  HTTP search is blocked, the client waits 4 seconds and retries with curl; the
  block remains stored for 2 hours in the marketplace state file.
  Multi-marketplace searches wait 3 seconds between one domain and the next.
- If you receive 503, wait and retry; do not increase concurrency. `monitor` puts
  the blocked domain in cooldown and disables it after 3 consecutive errors.

## Installation, build, and publish

Requirements: .NET 10. On Linux the `curl-impersonate` command must be in the
`PATH`; on Windows no external dependencies are needed.

```bash
cd dotnet
dotnet test AmazonScraper.slnx -c Release
./publish.sh
```

Output:

```text
dotnet/publish/linux-x64/amz-cli
dotnet/publish/win-x64/amz-cli.exe
dotnet/publish/win-x64/amz-notify.exe
```

On Windows:

```powershell
.\publish.ps1
```

`publish.sh` supports `--layout single|folder`, `--version`, `--no-smoke`, `-v`.
If single-file does not load the native libs on Windows, use `--layout folder`.
On Linux no helper is included: the dependency is the system package.

### Standalone installer

For a first per-user installation:

```bash
./publish-installer.sh
python3 release-installer.py --channel stable
```

The installer downloads the latest signed release, verifies signature/hash,
creates the folder, and configures the symlink/PATH. Installer assets are in the
GitHub prerelease tag `installer-stable`; CLI releases and feeds use
`v<version>` tags.

```bash
amz-cli-installer --dir ~/.local/share/amz-cli
amz-cli-installer --dry-run
amz-cli-installer --uninstall
```

Linux creates `~/.local/bin/amz-cli`; Windows uses
`%LOCALAPPDATA%\Programs\AmzCLI` and the user PATH.

## Architecture and maintenance

```text
src/AmazonScraper.Core/
  Http/          client, options, curl/http transport, exceptions
  Marketplaces/  domain profiles, patterns, formats, registry
  Parsing/       search/detail parsers, gallery, structural patterns
  Searching/     pagination, postal code validation
  Models/        SearchResult, ProductDetail, DeliveryInfo
  Formatting/    JSON, TSV, cleaner
  Monitoring/    targets, rules, state, JSONL/CSV writer

src/AmazonScraper.Cli/
  Commands/      search, details, monitor, domains, self-test, skill
  Monitoring/    runner, pacer, logger, background
  Environment/   Windows PATH (install/status/uninstall)
  Rendering/     Spectre tables, console
  Skill/         embedded SKILL.md and loader

src/AmazonScraper.Notifications.Windows/
  Program.cs     native amz-notify.exe helper (CommunityToolkit)
tests/
  AmazonScraper.Core.Tests/
  AmazonScraper.Cli.Tests/
```

The canonical skill is `src/AmazonScraper.Cli/Skill/SKILL.md` and is included in
the executable as an embedded resource. The agent copy is
`~/.agents/skills/amz-cli/SKILL.md`; synchronize it with `sync-skill.sh`.

## Security and privacy

- There are no hardcoded tokens in the code: `glowValidationToken`, `CSRF_TOKEN`,
  and `anti-csrftoken-a2z` are read at runtime.
- `--cookie` accepts runtime cookies and does not save them to disk.
- HAR files and captured HTML fixtures may contain `session-id`,
  `session-token`, `aws-waf-token`, `csrfToken`, and `customerId`: do not commit
  them and sanitize them before sharing.
- The skill and tests use synthetic tokens, not real credentials.
- The test AOD fixture has CSRF/tokens zeroed and synthetic `offer_listing_id`
  values, while keeping the real HTML structure.

## Limitations

- Dynamic data: prices, coupons, stock, and dates change.
- By default AOD loads only the first page: `count` may be greater than
  `options`. Use `--all-options` (max 10 pages) to load them all.
- New/used: the summary may be mixed; the buybox condition is read from the
  active row, but the conditions of individual offers remain those shown by
  Amazon.
- Seller-dependent delivery: some sellers do not expose cost/time.
- Cross-border is not guaranteed: the product may not be shippable.
- No FX conversion: prices remain in the marketplace currency.
- `monitor` calculates group totals on the current buybox price: coupons,
  shipping, and different currencies are not included.
- In `monitor` a product can be in only one group; an incomplete group does not
  fire by default (`allow_incomplete: false`).
- `offer_listing_id` values are runtime values: they can change and expire.
- No sign-in, no checkout, no official API.
- Respect Amazon's ToS and do not scrape aggressively.

## Versioning

The correct version is the one from `amz-cli --version`. Do not assume a fixed
number: this skill is updated together with the CLI.
