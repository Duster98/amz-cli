# AMZ CLI — developer reference

> Technical reference for the source tree. Installation and release
> downloads are in the repository [README](../README.md).

A .NET 10 port of the `amazon_scraper` Python CLI, with **Spectre.Console** rendering and
multi-marketplace support: **Amazon.it**, **Amazon.fr**, **Amazon.co.uk**,
**Amazon.com**, **Amazon.co.jp**, **Amazon.es**, and **Amazon.de** are available.
It includes a `monitor` command for periodic observations, price thresholds,
group totals, alerts, and JSONL/CSV saving.

## Requirements

- .NET SDK 10
- Linux x64 or Windows x64. The official release artifacts are x64; other
  self-contained RIDs are not published or tested.
- **Linux**: `curl-impersonate` command in `PATH` (on Arch:
  `sudo pacman -S curl-impersonate`). All domains use this command.
- **Windows**: no external dependencies. Official release packages include an
  updated `libcurl-impersonate.dll`; source builds use the DLL shipped by the
  `CurlImpersonate.Http` package.
- **Only needed to build the web UI**: Node.js 20.19+ and npm. Not required at runtime:
  the SPA is embedded in the executable.

## Build and test

```bash
dotnet build AmazonScraper.slnx -c Release
dotnet test AmazonScraper.slnx -c Release
```

> On Linux, `dotnet run` uses the system `curl-impersonate` command. Make sure
> it is installed and reachable in `PATH`; otherwise set
> `AMZ_CURL_IMPERSONATE` (or the legacy alias `AMZ_UK_CURL`).

## Usage

```bash
amz-cli search "rtx 5080"
amz-cli search "rtx 5080" --json
amz-cli search "rtx 5080" --tk-opt
amz-cli search "cuffie" --deals
amz-cli deals
amz-cli deals --domain uk --deal-type today
amz-cli deals --domain uk --pages 2 --limit 20
amz-cli details B0DTJDR3V9
amz-cli details B0DTJDR3V9 B0FMRWBK4R --compare
amz-cli details B0DTJDR3V9 --json
amz-cli monitor B07Q6Y5SC7 --timer 3600 --save-format json --save-path ./monitor
amz-cli monitor --library-product B0H25NG1JK --max-price 600 --timer 3600
amz-cli monitor --library-group g_laptops --total-max 1200 --currency EUR --fx GBP=1.17
amz-cli monitor --config monitor.json --once
amz-cli monitor --config monitor.json --detach
amz-cli monitor --config monitor.json --startup
amz-cli monitor --config monitor.json --startup-remove
amz-cli monitor --list
amz-cli monitor --list --verify
amz-cli monitor --stats
amz-cli monitor --restart
amz-cli history list
amz-cli history list --kind search --source agent --json
amz-cli history remove h_413bde1a13fb
amz-cli history clear --yes
amz-cli paths --json --all
amz-cli domains
amz-cli self-test
amz-cli doctor
amz-cli doctor --json
amz-cli --skill
amz-cli skill
amz-cli skill --install
amz-cli skill --check
amz-cli web --open
amz-cli web --desktop
amz-cli web --remote --port 5210
amz-cli web --status
amz-cli web --stop
```

## Local web interface

The CLI includes a React/TypeScript SPA served by Kestrel on `127.0.0.1` by
default. Use `--remote` to expose it on the LAN. It is used for **search and
product details**; monitoring stays in the CLI. Node is not required at
runtime: TypeScript and Vite are build tools only.

The main page is **search-first**: a 68 px side rail expands
to 264 px on hover or focus, can be pinned with **Pin**, and hosts Search,
Deals, Saved products, History, and Status; monitoring, comparisons, and
settings are planned. At the bottom, below the separator, are version, language,
theme, and close; there is no separate top bar.

The search field has **Search** and **Filters**. The filter panel lets you
choose one or more marketplaces for the same query, sort with Amazon's criteria,
and filter by price, minimum rating, deals, free shipping, and
Scelta Amazon; options for excluding sponsored results, plus delivery estimates and
reference prices, also remain available. The last five recent searches remain
below the field, with individual removal and **Clear all**; the full shared
history is available from the History section.
An ASIN is opened directly only if it has a recognizable format
(B-prefixed with at least one digit or ISBN-10): 10-letter words such as
`smartwatch` remain normal searches.

Results can be viewed as a **list** or **grid** with the switcher in the
top right; the choice is remembered. During a new search the previous list
stays visible and dimmed, with the progress bar
in the header, then gives way to the new results without reloading the
page, resetting scroll, or passing through an empty state. Switching to
**Deals** and back to **Search** keeps the query, results, and scroll
position; an ongoing search continues in the background and is not
restarted.

While scrolling, the search bar stays stuck to the top and
compacts: reduced height, hidden marketplace, and smaller buttons. At the
bottom of the list are the **Amazon pages** buttons; the total count
is read from the HTML paginator of the search page (up to 20) and each
button actually loads the corresponding page. “Pages” is no longer inside the
filters. During loading, the progress bar stays below 100%
until price enrichment has also finished.

Filters for price, rating, deals, free shipping, Scelta Amazon,
sponsored results, and sorting by price or reviews are applied immediately
to the already loaded list. Removing a marketplace that is already part of the search filters the
results locally; adding a new one shows a warning and enables
**Refresh** in the panel, because a new request is needed. The same warning
appears for options that require data not yet available.

Already visited pages stay in an in-memory cache for 15 minutes and are
reopened without network. There is no refresh button: pressing
**Search** again invalidates the cache for that query and restarts from page 1.

The page and panels always reserve scrollbar space
(`scrollbar-gutter: stable`), so opening filters or menus does not shift
content horizontally.

At the page edges, scrolling has a damped elastic bounce: the content
follows the mouse wheel and springs back with a short rebound. Scrollbars are
thin (7 px) and customized per page, dropdown, filter panel, and rail.

If a search result card does not expose the price, the UI tries to complete it from the
details and shows the condition (`Usato`, `Nuovo`) next to the price. For
Amazon Renewed offers the UI also shows the grade (`Premium`, `Excellent`,
`Good`, `Acceptable`) as a distinct, localized tag. The product card also shows
the **Available offers** section with seller, condition, grade, shipping, and
price for each offer returned by Amazon.

When Amazon groups multiple ASINs as variants (for example `Style`), the product card
shows a selector with all variants. Selecting one loads its
ASIN, so price, availability, and offers are those of that child
product.

Secondary tables on the page (manufacturer, product type, dimensions,
ranking, etc.) do not end up in `specs`: they are exposed as `amazon_details` and
shown in the separate **Amazon details** section, so the main
specs stay clean.

When Amazon shows a product-safety notice (CPSIA card or the “important
information” summary), the detail JSON exposes it as `safety_warnings`, a list
of localized strings covering choking hazards, small parts, magnets, and age
suitability. Duplicate notices are removed and the longest version of the same
hazard is kept. The CLI prints a **Safety warnings** block and the Web UI shows
a warning banner above the offers.

Product details use an in-memory cache shared between the UI and
search enrichment: the same domain/ASIN pair within 15 minutes is not requested
from Amazon again. The card shows the age of the data and the **Refresh details**
button forces a new request (`POST /api/v1/products/{asin}/refresh`). Variant
prices are fetched on demand through `GET /api/v1/products/{asin}/variants` and
cached with the detail; the variant dropdown shows them when ready. The cache
holds at most 100 ASINs and does not store errors.

Photos attached to reviews open in a panel above the page,
without opening new tabs: you can move from one image to another in the same
review with the arrows and close with the button, by clicking the background, or
with `Esc`.

While typing in the search field, Amazon suggestions appear. The
`↑`/`↓` arrows fill the field with the highlighted entry; `Enter` or a click
starts the search immediately, `Esc` closes the list. Suggestions use the
public endpoints `completion.amazon.<dominio>` with a local 10-minute cache;
if Amazon does not respond, normal search keeps working.

The **Filters** panel lets you choose one or more marketplaces for the
same query, sort results with native Amazon criteria (price,
reviews, latest arrivals, best sellers), and filter by price, minimum rating,
deals, free shipping, and Scelta Amazon. Filters for price, rating,
and deals act immediately on pages already loaded; marketplace, sorting,
and search options are applied with **Search**. In a
multi-marketplace search results are grouped by marketplace and opening a
card uses the correct domain. Requests are sequential with 3 seconds
between one domain and the next; if an HTTP search is blocked, it is
retried once with `curl-impersonate` after 4 seconds.

While scrolling, the search bar, the **Results** summary
(query, count, duration, and list/grid switcher), and
the active marketplace header remain anchored. When moving to the next group,
the new header takes the place of the previous one; offsets adapt to the
compact bar height and the mobile layout.

Preferences and history do not use browser storage. The Web UI loads and
saves `web/ui-state.json` in the state directory:

- Linux: `~/.local/state/amz-cli/web/ui-state.json`
- Windows: `%LOCALAPPDATA%\AmzCLI\web\ui-state.json`

The file is versioned (`schema: 4`), written atomically with
`0600` permissions, and contains `preferences` (language, delivery country and
postal code, theme, marketplace, pinned rail, list/grid view, filters),
`recent_searches`, and the reserved sections `search_history`,
`saved_searches`, `comparisons`, `monitoring`, and `settings`. Saved products
and groups live in the separate shared `library.json`; searches and opened
products live in shared `history.json`.
The APIs are `GET /api/v1/ui-state`, `PUT /api/v1/ui-state/preferences`,
`PUT /api/v1/ui-state/recent-searches`, the atomic `/api/v1/library`
product/group endpoints, and the `/api/v1/history` endpoints.

The language selector is built from the languages of the available marketplaces:
Italian, German, French, English, Spanish, and Japanese. `uk` and `com` share
English. The choice persists in `preferences.language` and changes only the
interface; Amazon titles, descriptions, reviews, and specifications remain in
the marketplace language.

The **Deliver to** control accepts an automatic marketplace location or a
specific destination. The country field filters as you type and lists
countries alphabetically. Amazon uses the country and, where supported, the
postal code for its GLOW delivery flow; city and region are inferred. On
Amazon.com and Amazon.co.jp, international destinations use the country only:
the ZIP code is required for US addresses and the Japanese postal code for JP
addresses; neither is sent for other countries. The saved destination is used by
search, deals, product details, and refresh requests. After changing it, refresh
the search explicitly to recalculate delivery estimates. The GLOW flow keeps
Amazon session cookies per domain and expiry, so changing the destination works
with the automatic anonymous session; if Amazon still rejects it, AMZ CLI
reports the error and keeps the automatic location. Search and deals use their
results page as the GLOW seed because the marketplace homepage can be protected
by an AWS WAF challenge.

### Saved products

The saved products library is shared by the CLI and the web interface. Use the
bookmark on a search result or on the product page, or save a product from the
CLI:

```bash
amz-cli library add B0H25NG1JK
amz-cli library add https://www.amazon.it/dp/B0H25NG1JK --group g_laptops
amz-cli library list
amz-cli library list --json
amz-cli library group create "Laptop ufficio" --accent blue \
  --description "Portatili per il lavoro"
amz-cli library group edit "Laptop ufficio" --accent violet \
  --description "Portatili aziendali e accessori"
amz-cli library group add g_laptops B0H25NG1JK
amz-cli library group show g_laptops --json
```

The **Saved products** page has two tabs, **Groups** and **Products**. Products
can be organized into groups, a product can belong to multiple groups, a group
can be empty, and deleting a group keeps its products in the library. Groups
created with `library group create` do not start monitoring. Groups can have an
optional description (up to 500 characters); use `--description` on `group
create`, `group rename`, or `group edit`, and `--clear-description` on `group
edit` to remove it. Group cards and the group page show the description, and the
CLI JSON/table output includes it.

The page supports list and grid layouts, shows the price difference since saving,
opens the normal product detail page, and allows removing products from the
library. The header search button expands an inline field that filters products
and groups while typing: products match title, ASIN, and marketplace; groups
match name, description, and their contents. The search combines with the
**All** / **Only singles** filter and works without additional server requests.
Group cards show a contact-sheet cover and the current total value of the group;
the group page adds product count, total value, lowest price, and the same visual
cover. Totals are calculated per currency and multiplied by the item quantity.
Groups support preset colors (`accent`, `blue`, `gold`, `rust`, `green`, `teal`,
`violet`, `rose`, `slate`, `orange`, `plum`, `sky`) plus a native custom color
picker; custom colors are stored as `#rrggbb`.

Saved products keep both the original save baseline and the latest verified
data: `saved_at` and `price_at_save` never change, while `price_last`,
`snapshot`, and `last_checked_at` follow the latest product detail. Whenever a
detail is fetched for a product already in the library (Web detail, `amz-cli
details`, or an agent), the saved record is synchronized without making an
additional Amazon request. Cache hits within the product-cache TTL also refresh
`last_checked_at` from the cached fetch time. When the buybox has no direct
price, the lowest buying-option price is used, so saved RAM/offer listings update
from the price actually shown on the detail page. Products older than 7 days show
a **Check needed** marker and are rechecked when opened; the default 30-minute
product-cache TTL already forces a live check sooner. The group page has
**Refresh prices**: it checks only products that were not checked in the last 24
hours, one request at a time, with progress and cancellation.

CLI and Web read and write the same file:

```text
<state root>/library.json
```

On Linux this is usually `~/.local/state/amz-cli/library.json`; on Windows it is
`%LOCALAPPDATA%\AmzCLI\library.json`. The old library embedded in
`web/ui-state.json` (schema 3) is migrated automatically with a backup. The
sidebar bookmark icon and the **Products** tab show a small orange dot when a
product is added; the dot clears when you open the **Products** tab. The Web UI
receives library updates over SignalR, so changes made from the CLI appear
without reloading or restarting the server.

### History

The **History** page records searches and opened products from both the Web UI
and the CLI. It stores only metadata: query, marketplaces, all search filters,
timestamps, source, status, and a product snapshot for opened products. Search
results are not stored in the history file.

```bash
amz-cli search "ssd portatile" --agent
amz-cli details B0H25NG1JK --agent
```

The `--agent` flag marks entries as agent-generated; without it, CLI entries are
marked as CLI. Web entries are marked as Web. Every new search and product
opening is a separate history entry. Reopening an existing entry from the
history never creates a duplicate: cached data is shown directly, and when the
cache is gone the search or detail is fetched again into the same entry.

History is stored in `<state root>/history.json` with no automatic entry limit.
A search entry can be `running`, `succeeded`, `failed`, `cancelled`, or
`interrupted`. While a search is running, the history page shows it as in
progress and does not open it.

Search results and opened product details are cached only for the lifetime of
the Web server process in `<state root>/web/history-cache/`. The directory is
cleared when `amz-cli web` starts, so after closing and reopening the interface
an old search re-runs and an old product detail is fetched again. When cached
data is available, the history page opens it instantly: searches show
**Results from the cache** with **Refresh**, while product pages show the
normal **From cache** status and keep their **Refresh details** action. A
product detail opened from the history is not added to the history again; the
refresh action updates its cached payload.

History changes are sent over SignalR with `HistoryChanged` events, so an open
history page shows agent searches appearing and completing in real time.

The same history is available from the CLI:

```bash
amz-cli history list
amz-cli history list --kind search --source agent --status succeeded
amz-cli history list --tk-opt
amz-cli history remove h_413bde1a13fb
amz-cli history clear --yes
```

`history remove` accepts one or more ids, deletes the matching session cache
files, and returns a non-zero exit code if an id does not exist. `history clear`
requires `--yes` and removes every entry and cached result. Both operations are
picked up by an open Web page in real time over SignalR. `--json` is available
on every history command; `list` also supports `--kind`, `--source`, and
`--status` filters.

The real-time endpoint is `/api/v1/hubs/library`; it stays behind the same
session cookie authentication as the rest of `/api/v1`. The browser connects
after the UI boot and reconnects automatically. Library events are sent as:

```json
{
  "type": "library_changed",
  "revision": 12,
  "changed_at": "2026-11-20T13:00:00Z"
}
```

The payload deliberately does not contain the full library: the client fetches
`GET /api/v1/library` after an event and deduplicates by `revision`.

### Comparison tabs

The **Comparisons** page keeps up to six products in closable tabs. On desktop up
to three compact product dossiers are visible side by side; on phones a
scrollable tab strip shows one dossier at a time. Each dossier includes the
image, title, price, rating, availability, seller, delivery, returns and the
main specifications. Values that differ between products are marked, and the
lowest price is labelled when all products use the same currency. **Open detail** opens the full product page.

From a product detail, the **Compare** button adds or removes that product and
lights the dot on the Comparisons rail item. The comparison is stored in
`<state root>/web/compare.json` and is shared with the CLI:

```bash
amz-cli compare add it:B0H25NG1JK de:B0FL85P2QS
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

`compare show` fetches every product and prints a compact commerce table. Add
`--diff` to transpose the comparison: every product field and every
specification becomes a row, missing values are shown as `—`, and a `●` marks
rows that differ. This is the terminal equivalent of the Web dossier.
`--json` returns the full product details; `--diff --json` returns the attribute
matrix with `difference_count`. `--tk-opt` returns TSV; `--refresh` bypasses the
product cache.

`compare add` accepts ASINs, `domain:ASIN` pairs, or Amazon URLs. It writes the
shared file; when the Web UI is open, a `CompareChanged` SignalR event updates
the tabs and badge immediately. The maximum is six products.

The Web APIs are `GET /api/v1/compare`, `POST /api/v1/compare/items` (one or
more products), `DELETE /api/v1/compare/items/{domain}/{asin}`, and
`DELETE /api/v1/compare`. Mutations use the same session cookie and
`X-Amz-Cli-Web` CSRF header as the rest of the local API.

In the **New search** page each recent entry has an X to remove it
individually; the section header offers **Clear all**. Changes
are saved to the same file and persist after restart.

The **Deals** entry in the rail opens a dedicated page with one marketplace at a
time. The **All**, **Today**, and **Coupons** tabs appear only when the domain
supports them; categories are read from the Amazon Deals page and shown
as horizontal chips. The **Today's Deals** shortcut on the search page
opens the Today tab directly. While scrolling, the selectors and the results
summary with count and list/grid view stay fixed.
The APIs are `GET /api/v1/deals/options` and `POST /api/v1/deals`; the job uses
`GET /api/v1/jobs/{id}` and the same Amazon pagination as search. Results
are cached for 2 minutes per marketplace, type, category, and
page: returning to the page does not start a new request. **Refresh**
ignores the cache and forces loading. The last marketplace visited in
Deals is remembered separately from search and restored even after
a full restart; type and category are not persisted.

The **Monitoring** page is a first-class Web view over the same monitor state
used by the CLI. It lists saved bindings and external CLI runs, shows whether a
monitor is running, stopped, stale, or configured at login, and lets you create,
edit, start, stop, restart, and delete saved-product and saved-group monitors.
The same data files are used by both interfaces: no Web-only monitor database is
created.

A monitor binding now stores its runtime defaults (`timer_seconds`,
`request_delay_seconds`, `save_path`, `save_file`, `save_format`, `layout`, and
notification settings). The Web starts a detached process with the same
`--binding` command used by the CLI; the process runs after the browser is
closed. Runs are linked to the binding by `binding_id`, which is also exposed by
`monitor --list --json`. `monitor --binding` uses the stored runtime defaults,
with explicit CLI flags taking precedence.

The monitoring list can be shown as a grid or a list; the choice is stored in
the Web UI preferences (`monitoring_layout`). The target picker is a
constrained-width popover: long product names are ellipsized instead of
expanding the editor or the modal. Product cards and the detail header show the
Amazon marketplace (`Amazon.de`, `Amazon.it`, ...) and a direct link to the
product page. Group targets show their marketplace list; a single product target
also exposes its ASIN and `https://www.amazon.<domain>/dp/<ASIN>` URL. Cards use
a product column, a facts line, a right-hand status/price rail, and a dedicated
action bar. In list view the action bar moves to a side rail so each monitor
reads as a compact row.

The detail page reads the monitor's JSON Lines or CSV observations and renders a
full-width, chart-first view with range controls, an adaptive time axis, alert
markers, an accessible data table, and server-side downsampling. Hovering the
plot updates a persistent price/date readout above the chart instead of relying
on a clipped floating tooltip. A statistics strip sits below the chart, followed
by configuration, automatic startup, and alerts. The detail header keeps one
**Open on Amazon** link, shows the marketplace and ASIN as metadata, and groups
the process actions in a control bar (`Start`, `Resume` after a previous run,
`Stop`, `Restart` while running, and delete in the overflow menu). Product
bindings show their price series; group bindings show the group total. The
editor supports max price, percentage drops, and percentage rises, so a monitor
can alert both when the price falls by 5% and when it rises by 10% against the
same baseline. Monitor data is read locally and never triggers an Amazon request.

The editor is organized in sections: target, rules, per-product overrides for
groups, runtime, notifications, and automatic startup. Rules include
`baseline`, `baseline_price`, and `cooldown_seconds`; groups expose `total_max`,
`allow_incomplete`, currency, FX rates, and an optional override table for each
product. The runtime section exposes timer, request delay, jitter, save
path/file/format, layout, notification events, notification cooldown, and
`no_auto_update`. Less common options are collapsed under **Advanced** and keep
their defaults. HTTP transport, browser profile, user agent, cookies, CAP/country
and anti-bot tuning remain per-run CLI options; they are not stored in
`monitor-bindings.json`, so secrets never enter the Web binding file.

Automatic startup is managed from the same binding: the Web installs or removes
the same systemd user unit or Windows Task Scheduler entry used by
`monitor --startup`, shows whether it is configured and registered, and can
verify it. Stopping a monitor does not disable startup. The API also exposes
`POST /api/v1/monitors/{id}/startup`, `DELETE /api/v1/monitors/{id}/startup`,
`POST /api/v1/monitors/{id}/startup/verify`, and
`POST /api/v1/monitors/{id}/notify-test`.

```bash
# From the repository, build the UI and embed it in the CLI.
cd web
./build.sh          # Windows: .\build.ps1

# Start in the foreground, opening the browser.
cd ..
dotnet run --project src/AmazonScraper.Cli -- web --open

# App mode: hidden console on Windows, logs to file.
amz-cli web --desktop

# Status and stop.
amz-cli web --status
amz-cli web --stop
```

Useful commands:

| Command | Effect |
|---|---|
| `amz-cli web` | starts the local server without opening the browser |
| `amz-cli web --open` | starts and opens the browser |
| `amz-cli web --desktop` | double-click mode, logs to file, closes after inactivity |
| `amz-cli web --port 5210` | fixed port (default: automatic) |
| `amz-cli web --remote` | listens on all interfaces so the UI is reachable on the LAN |
| `amz-cli web --remote --port 5210` | LAN access on a stable fixed port |
| `amz-cli web --dev --web-dir web/dist` | serves a Vite build from disk |
| `amz-cli web --status` | shows PID, port, and version |
| `amz-cli web --stop` | stops the active instance |

Access is protected by a one-time token passed in the URL fragment and
exchanged for an `HttpOnly`/`SameSite=Strict` cookie. By default the server
listens only on loopback. With `--remote` it listens on all interfaces and
prints the tokenized LAN URL; the `Host`/`Origin` checks still accept only the
machine's own interface addresses, loopback, and its host name, so arbitrary
Host headers are rejected. The CSP allows the WebSocket connection to the LAN
origin for SignalR. The session is in `~/.local/state/amz-cli/web/` with `0600`
permissions.

```bash
# From another device on the same LAN, open the URL printed by:
amz-cli web --remote --port 5210
# LAN: http://192.168.1.10:5210/#t=...
```

If the page is not reachable, allow the selected port in the host firewall.

The **Status** page also shows the **curl-impersonate** runtime dependency:
on Linux the system command, its path, and version; on Windows the bundled
`libcurl-impersonate.dll`, `curl_shim.dll`, and `zlib.dll`, with their presence
and the loaded version. The same page contains the update center. **Check for
updates** reads the signed release feed; when a newer version exists, the page
shows the release notes and an **Update** action. The update runs through the same signed
`UpdateService` used by `amz-cli update`, including the backup and the new-binary
verification. The Web interface is closed and restarted automatically: the
existing page shows live download/preparation progress, then waits for the
server to resume on the same port, token, and session cookie. After the restart
the page reloads into the new version. Any active monitors are stopped and
restarted; monitors that cannot be restarted must be started manually. Active
searches must be cancelled before starting the update; the server refuses the
request otherwise.

The update APIs are `GET /api/v1/update`, `POST /api/v1/update/check`, and
`POST /api/v1/update/apply`. Progress and state changes are sent over the
existing SignalR hub as `UpdateProgress` and `UpdateChanged`.

Development with Vite and HMR:

```bash
# terminal 1: .NET API on 5210
amz-cli web --dev --port 5210 --no-open

# terminal 2: Vite on 5173, with /api proxy to 5210
cd web && npm run dev
# open the URL with the token printed by amz-cli web (http://localhost:5173/#t=...)
```

To update the UI after a change:

```bash
./web/build.sh
```

The full skill for agents is in `src/AmazonScraper.Cli/Skill/SKILL.md`,
included in the executable. `amz-cli --skill` (or `amz-cli skill`) prints it as
Markdown to stdout, with no network. To install or verify it in the copy
loaded by the agent:

```bash
amz-cli skill --install
amz-cli skill --check
```

Default path: `~/.agents/skills/amz-cli/SKILL.md` (Windows:
`%USERPROFILE%\.agents\skills\amz-cli\SKILL.md`). `--dir` chooses another
directory, `--force` replaces an unrecognized file, and `--json` produces
structured output.

Options common to `search`, `details`, and `monitor` (where applicable):

| Option | Description |
|---|---|
| `--domain <it\|de\|fr\|uk\|com\|jp\|es>` | Marketplace; `it` is the default, the others are available |
| `--timeout <SECONDS>` | HTTP timeout, default 30 or the saved setting |
| `--width <COLUMNS>` | Table width, default 200 |
| `--transport curl\|http` | HTTP transport; default `curl`, but `http` for `.com` |
| `--browser <PROFILE>` | curl-impersonate TLS profile; default `Firefox147`, except `.com` (`Safari260Ios`) |
| `--user-agent <UA>` | Explicit HTTP User-Agent; overrides the curl-impersonate profile UA |
| `--browser-headers` | Full browser headers from the curl-impersonate profile |
| `--cookie <HEADER>` | Explicit HTTP cookies (session/WAF), e.g. `session-id=...; bm_sv=...` |
| `--json` | JSON output with a schema compatible with the Python version |
| `--tk-opt` | Tab-separated output |
| `--no-ads` | Excludes sponsored results from `search` and `deals` |
| `--enrich` | Opens details for products without a price or without a strikethrough price/discount |
| `--enrich-delay <MS>` | Delay between enrich requests, default 1500 ms |
| `--html <FILE>` | Reads HTML from a file, without network (debug/test) |
| `--compare` | `details` only: one row per product, star on the lowest price |

On Linux `--transport curl` uses the system `curl-impersonate` command, while
`--transport http` uses `SocketsHttpHandler`. `.com` uses `http` as the default;
the other domains use `curl`. In search, if the HTTP transport is
blocked, the command waits 4 seconds and retries once with
`curl-impersonate`.

With `curl`, the Safari profiles use the User-Agent built into the
impersonation profile (for example iPhone Safari 26 for `Safari260Ios`), so it
matches the TLS/HTTP2 fingerprint. Firefox profiles keep a current Firefox UA,
because Amazon rejects the 147 string shipped by curl-impersonate. `--user-agent`
overrides both; forcing a UA that does not match the profile can trigger
Amazon's "Continue shopping" anti-bot page. The plain HTTP transport, which has
no impersonation profile, always sends a browser UA.

Product details use the desktop layout by default: Amazon's mobile pages omit
`colorImages` (the image gallery) and the technical specifications. Use
`--browser Safari260Ios` to request the mobile layout explicitly; the CLI then
keeps the profile's iPhone User-Agent instead of overriding it.

The HTTP block is remembered per marketplace: for the **next 2 hours**
automatic requests go directly to `curl-impersonate`, without
retrying HTTP. The state is persistent and shared between CLI, Web UI, and monitor:

- Linux: `~/.local/state/amz-cli/transport/<domain>.json`
- Windows: `%LOCALAPPDATA%\AmzCLI\transport\<dominio>.json`

After the 2 hours expire, the client retries HTTP once; if it is blocked again,
the cooldown restarts. `--transport http` still forces a manual HTTP
attempt. `amz-cli self-test` shows active cooldowns.

### Persistent HTTP settings

Transport, curl-impersonate profile, User-Agent, browser headers and timeout can
be saved as defaults shared by the CLI, the Web UI and the monitors:

```bash
amz-cli settings show
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

Precedence is:

```text
command-line flag  >  http-settings.json  >  marketplace default
```

`amz-cli settings show` prints the saved values, the effective configuration for
each marketplace and the active HTTP cooldowns. `--json` is available for
scripts. The Web Settings page under **Advanced** reads and writes the same file.
Monitors load it when they start, so restart a running monitor to apply changes.

### Pagination (only `search`)

| Option | Description |
|---|---|
| `--page <SPEC>` | Specific pages: `2`, `1,3,5`, `3-5`, `1,3-5,8` |
| `--pages <N>` | Downloads the first `N` pages (`1`-`20`) |
| `--page-delay <MS>` | Delay between pages, default `2000` ms |

Examples:

```bash
amz-cli search 'asus' --domain it --page 2
amz-cli search 'asus' --domain it --page 1,3,5
amz-cli search 'asus' --domain it --page 3-5
amz-cli search 'asus' --domain it --pages 4
```

`--page` and `--pages` are mutually exclusive. Page results are merged and
deduplicated by ASIN; JSON/TSV/table remain unchanged. If a page is empty or
blocked after the first, the command stops, keeps the results already obtained, and
prints a warning on stderr. With `--save-html` and multiple pages the files become
`nome-p1.html`, `nome-p3.html`, etc. With `--html` only one page is allowed.

> `--page 2` on `.com` may receive `503 — Sorry! Something went wrong!` due to
> Amazon's bot mitigation: use a higher `--page-delay`, a browser `--cookie`,
> or retry later. The `.com` domain uses HTTP by default.

### Deals in search (`--deals`)

`--deals` adds Amazon's internal `p_n_deal_type` filter to search:

```bash
amz-cli search "cuffie" --deals
amz-cli search "cuffie" --deals --no-ads
amz-cli search "cuffie" --deals --json
amz-cli search "monitor" --deals --deal-type today
amz-cli search "monitor" --deals --deal-type coupons --domain it
```

- `--deals` alone uses `all` (all discounts/deals).
- `--deal-type <TYPE>` requires `--deals` and accepts `all`, `today`, `coupons`.
- `--deals` is not compatible with `--html`: the filter is applied to the
  network request.
- The table shows `Was`, `Disc.`, and `Badge` when data is available;
  JSON and TSV expose `original_price`, `discount`, `deal`, and `deal_id`. If
  Amazon exposes the deal ID but not the badge text, `deal` becomes `Deal`.

Filter coverage per marketplace:

| Domain | `all` | `today` | `coupons` |
|---|---|---|---|
| `it` | yes | yes | yes |
| `de` | yes | yes | yes |
| `fr` | yes | yes (`Ventes Flash`) | yes |
| `es` | yes | no | no |
| `uk` | yes | yes | no |
| `com` | yes | yes | no |
| `jp` | yes | yes | no |

The `p_n_deal_type` values are internal and marketplace-specific; they may
change. If a combination is not available the CLI returns an explicit error
instead of showing unfiltered results. Amazon may insert
sponsored listings even in the deal filter: the `Badge` column distinguishes
results with a badge. The table also shows `Was` and `Disc.` when the card
exposes a reference price; `--enrich` also retrieves the price from details when
the card does not expose it (for example products with only a used offer) and adds the `condition` field (`Usato`, `D'occasion: Très bon`, ...).

### `deals` command

To view active deals without a query:

```bash
amz-cli deals
amz-cli deals --domain uk --deal-type today
amz-cli deals --domain uk --deal-type today --category Electronics
amz-cli deals --domain it --deal-type today --category Elettronica
amz-cli deals --domain uk --pages 2 --limit 20 --json
amz-cli deals --domain uk --no-ads
```

It uses the same `p_n_deal_type` filter and the same support matrix as
`search --deals`. `--limit` only trims the output; requested pages are
still downloaded. `--no-ads` excludes sponsored results. `--enrich`
opens details for products without a strikethrough price/discount and is slower;
`--enrich-delay` controls the pause. `--html` lets you parse a
local page.

`--category <NAME>` filters by department/category. Names are the
marketplace-localized ones (`Elettronica`, `Casa e cucina`, `Electronics`,
etc.): the CLI reads departments dynamically from the domain's deals page,
so no static map is needed. The filter requires network, is not
compatible with `--html`, and adds a request for category discovery;
if the name does not exist, the available list is shown.

The deals page always uses the desktop layout (Firefox profile and current
Firefox User-Agent), including on `.it`, `.fr`, and `.jp` where product details
use the mobile Safari profile. Amazon exposes the department refinements and the
full deal set only in the desktop layout; the mobile page returns fewer deals
and no categories. The Web **Offerte** page uses the same layout.

### Delivery data from search

With `--delivery` search shows the delivery block already present in the cards
(cost and date/estimate), without changing location:

```bash
amz-cli search 'monitor' --domain uk --delivery
amz-cli search 'monitor' --domain uk --delivery --tk-opt
```

Adds the `Delivery` column to table/TSV and the JSON fields `delivery_text` /
`delivery_country`. It is the data for the session's default location
(IP/locale), not a specific postcode; for precise, localized data use
`details --cap`/`--country`. Not all cards show the block.

With Amazon's Choice, the table adds a `Choice` (`✓`) column only when
at least one card has the badge. JSON and TSV expose `amazon_choice`,
`amazon_choice_text`, and `amazon_choice_for`; the text may be localized or
appear as `Overall Pick` on Amazon.com. The search badge is independent of the
detail page badge; the product detail page now displays its own Amazon's Choice
badge when the detail page exposes it.

### Delivery (only `details`)

| Option | Description |
|---|---|
| `--cap <POSTAL>` | Delivery postcode, e.g. `20121`, `75001`, `E1 6AN`. On Amazon.com it is US-only; omit it for international destinations. |
| `--country <ISO>` | 2-letter ISO delivery country; default: marketplace country |

Examples:

```bash
# same country
amz-cli details B0CX9CT3ZF --domain it --cap 20121
amz-cli details B0CX9CT3ZF --domain uk --cap "E1 6AN"
amz-cli details B0CX9CT3ZF --domain es --cap 28010

# cross-border
amz-cli details B0CX9CT3ZF --domain it --country FR --cap 75001
amz-cli details B0CX9CT3ZF --domain uk --country IT --cap 20121
amz-cli details B0CX9CT3ZF --domain es --country IT --cap 20121

# Amazon.com: US ZIP or country-only international destination
amz-cli details B0CX9CT3ZF --domain com --cap 10001
amz-cli details B0CX9CT3ZF --domain com --country IT
amz-cli details B0CX9CT3ZF --domain com --country AL

# Amazon.co.jp: JP postal code or country-only international destination
amz-cli details B0CX9CT3ZF --domain jp --cap 100-0001
amz-cli details B0CX9CT3ZF --domain jp --country IT
```

The command sets the location with Amazon's GLOW flow, then reloads the
details. Delivery is shown:

- in JSON, in the `delivery` object (`location`, `country`, `postal_code`,
  `type`, `cost`, `time`, `cutoff`, `raw`, `options`);
- in TSV, in the `Delivery*` and `DeliveryOption N` rows when there are multiple
  alternatives;
- in the table and in `--compare`.

On Amazon.com and Amazon.co.jp the GLOW flow distinguishes domestic addresses
from international ones: the domestic country uses `--cap` (ZIP for the US,
postal code for Japan), while any other country uses the country picker without
a postal code. Supplying `--cap` with a non-domestic country is accepted but
ignored. Delivery-location requests on Amazon.com use the curl-impersonate
transport because the HTTP transport is frequently challenged by AWS WAF.

Changing location requires network and is not compatible with `--html`. For multiple ASINs,
the location change is performed only once per command. Cross-border depends on the product:
some items cannot be shipped abroad. Costs and dates are dynamic.

The `cutoff` is read from both the main delivery block and the
secondary one (“fastest delivery … Order within 8 hours 56 min”); when there are
multiple alternatives, `delivery.options` preserves the type, cost, date, and cutoff
of each. If Amazon does not expose the `data-csa-c-delivery-*` attributes, the CLI
tries the localized text fallback (`Order within`, `Ordina entro`, etc.).

On Amazon.es the GLOW flow uses the product page modal URL
(real `pageType`/`storeContext`) and sends `address-change` as JSON with
`Origin`, `Referer`, and `anti-csrftoken-a2z`; the old `Gateway`/`generic` form
on ES was rejected with HTTP 200 and an empty body. If the JSON is not accepted
(for example on Amazon.it), the CLI automatically retries the old form.
The ES postcode set uses the HTTP transport; on Amazon.es specify
`--transport http` when using `--country`/`--cap`.

### Buying options (only `details`)

When the buybox has no featured offer or the page shows a summary such as
“Nuovo (7) da 87,25 €”, `details` also loads the AOD list
(`/gp/product/ajax/aodAjaxMain`) and shows the individual options with the offer
code:

```bash
amz-cli details B07R3YS8R2 --json
amz-cli details B07R3YS8R2 --tk-opt
amz-cli details B07R3YS8R2 --no-buying-options   # skip the AOD request
amz-cli details B07R3YS8R2 --options-delay 3000  # pause before AOD
amz-cli details B07R3YS8R2 --smid A131AJORA55M3D # analyze the first option
```

| Option | Description |
|---|---|
| `--no-buying-options` | Does not load options and does not make the AOD request |
| `--options-delay <MS>` | Delay before each AOD request, default `1500` ms |
| `--all-options` | Loads all AOD pages (max 10) |
| `--options-pages <N>` | Loads at most `N` AOD pages (1-10) |
| `--smid <SELLER_ID>` | Analyzes the seller's offer (`?smid=...&psc=1`) |
| `--save-aod-html <FILE>` | Saves the AOD HTML; pages 2+ in `-p2`, `-p3`, ... |

Each option contains `offer_listing_id` (decoded once; it may contain
`+` and `/`, so it must be re-encoded if sent back in a form/URL), `seller`,
`seller_id`, `price`, `shipping`, `condition`, `condition_type`,
`condition_grade`, `delivery`, and `detail_url`
(`?smid=<seller_id>&psc=1`). In JSON it is the `buying_options` object with `count`,
`lowest_price`, `condition`, and `options`; in TSV the rows are
`BuyingOptionsCount`, `BuyingOptionsFrom`, and one `BuyingOption` row per offer.
`condition_type` is one of `new`, `used`, or `refurbished`;
`condition_grade` is one of `premium`, `excellent`, `good`, or `acceptable`
when Amazon exposes a Renewed grade. The raw localized `condition` remains
available for compatibility.

On pages with used-only offers (the `usedOnlyBuybox` layout, common on
Amazon.fr) the CLI reads `usedOfferListingID` and `usedMerchantID`, the condition
(`D'occasion: Très bon`), and the price from the used block, then still loads the AOD
to get the count and other offers. The same condition is
shown by the Web UI next to the price and in the “Available offers” section.

Additional product tables (for example `メーカー名`, `Amazon 売れ筋ランキング`
on Amazon.co.jp) are collected in `amazon_details` as a list of
`{name, value}`, separate from `specs`. The Web UI shows them in the
**Amazon details** section.

When the buybox has a featured offer, the AOD lists only the other
sellers: the CLI reads `offerListingID` and `merchantID` from the product page and puts
the featured offer at the top of the list. If the same seller/price/
condition also appears in the AOD with a different code, the AOD entry is kept,
so there are no duplicates.

On marketplaces with new and used items the summary may be mixed, for example
`新品＆中古品 (50)件の出品：￥6,100` on Amazon.co.jp: the featured
offer condition is read from the active buybox row (`newAccordionRow`/
`usedAccordionRow`), not from the summary. If the accordion is missing (e.g. Amazon.fr),
`buyingOptionType` (`NEW`/`USED`) is used to choose the part of the mixed
summary (`Neuf & D'occasion` + `NEW` → `Neuf`).

AOD is paginated: Amazon returns 10 offers per page. By default
`options` contains the first page plus any featured offer, while
`count` is the total. With `--all-options` subsequent pages are loaded
up to the total (max 10 pages / 100 offers); with `--options-pages N` you are
limited to `N` pages. Pages 2+ use `ref=aod_page_N`, `pageno=N`, and
`isonlyrenderofferlist=true`, with `--options-delay` between pages.
If a page fails, the options already collected remain valid and the warning goes to
stderr. If the AOD is blocked, the product is still returned. With
`--html` no AOD request is made: only the count, the
“from” price, and any featured offer present in the file are shown.

To analyze a single option, copy `seller_id` from the output and use
`--smid`: the CLI opens `detail_url` and shows price, seller, shipping,
availability, and delivery for that offer. It requires a single ASIN and is not
compatible with `--html`/`--compare`; it works with `--cap`.

### Variants and frequent returns (only `details`)

The detail page exposes variants through the Amazon twister (sizes,
colors, styles). Even without `--variants`, the main table shows the
count (`Variants 9 (use --variants for the list)`); TSV adds
`VariantsCount` and `VariantsParent`. JSON always includes `variants` when the
twister is present:

```json
{
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
}
```

Options:

```bash
amz-cli details B0C9C4GHGN --variants
amz-cli details B0C9C4GHGN --variants --variant-prices
amz-cli details B0C9C4GHGN --variant-prices --variant-delay 2000 --json
```

| Option | Description |
|---|---|
| `--variants` | Shows the variants table |
| `--variant-prices` | Retrieves price and availability of each variant |
| `--variant-delay <MS>` | Delay between variant requests, default `1500` ms |

`--variant-prices` also includes the selected variant and requires network; with
`--html` only `--variants` is available. Each variant can expose `price`,
`original_price`, `discount`, and `availability`.

The `frequently_returned` field contains the full localized text of the
“Frequently returned” badge when present; the table shows it as `Alert`
and TSV as `FreqReturned`.

The `charger` field exposes Amazon's “Senza caricatore” / “Charger not included”
box shown under the product images:

```json
"charger": {
  "included": false,
  "label": "Senza caricatore",
  "note": "CARICATORE NON INCLUSO: ...",
  "box_contents": "Certificato di garanzia"
}
```

`included` comes from Amazon's hidden `buffet-red-atf-is-charger-included`
input; the note is the matching description bullet and `box_contents` is the
localized “Contenuto confezione” value. The table adds `Charger`,
`Charger note`, and `Box contents`; TSV uses `ChargerIncluded`, `Charger`,
`ChargerNote`, and `BoxContents`. The field is omitted when the page has no
charger information. The Web UI shows a warning when the charger is not
included.

### Rating and reviews (only `details`)

The average rating and review count are already present by default
(`rating`, `reviews`, `Rating`/`Reviews` rows). The flags add detail:

```bash
amz-cli details B0C9C4GHGN --rating
amz-cli details B0C9C4GHGN --reviews
amz-cli details B0C9C4GHGN --reviews --review-limit 5
amz-cli details B0C9C4GHGN --reviews --review-images --json
amz-cli details B0C9C4GHGN --rating --reviews --html pagina.html
```

| Option | Description |
|---|---|
| `--rating` | Adds numeric average, count, and 5→1 histogram |
| `--reviews` | Adds the reviews present on the detail page |
| `--review-limit <N>` | Limits to `N` reviews (1-50; default: all) |
| `--review-images` | Includes URLs of images attached to reviews |

JSON adds:

```json
{
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
  }
}
```

`rating_details` and `top_reviews` are omitted if the flags are not active.
Reviews are only those already rendered on the detail page:
normally 8 local + 5 international. The full reviews page requires
login/challenge, so it is not used and no additional request is needed.
The text is what Amazon serves and may be machine-translated. With
`--reviews` TSV adds `ReviewsAvailable`, `ReviewsLocal`,
`ReviewsInternational`, one `Review` row per item and, with `--review-images`,
the `ReviewImage` rows. The flags are not compatible with `--compare`.
On Amazon.com the page may show “Sign in to see customer reviews”: in that
case `top_reviews` is omitted, a warning is printed, and rating and histogram
remain available. With authenticated cookies supplied via `--cookie`, the parser
uses any reviews rendered on the page.

### Amazon's Choice and import fees (only `details`)

The Amazon's Choice badge and import fees are read from the same
detail page, without additional requests and even with `--html`.

```json
{
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
  }
}
```

- `amazon_choice`, `amazon_choice_text`, and `amazon_choice_for` appear only if
  the badge is present. The text is localized (`Scelta Amazon`, `Choix
  d'Amazon`, `Opción Amazon`, `Amazon おすすめ`, etc.).
- `import_fees` appears when Amazon shows the block for the selected
  destination. On US it may contain the estimated amount and landed total; on UK
  it may be only the “Import Charges applied at checkout” disclosure, without
  amounts (`at_checkout: true`).
- `price` is the price Amazon uses in the import table and `price_basis`
  indicates whether it matches the current price (`current`), the list
  price (`list`), or neither (`other`). Amazon may calculate the
  total on the list price even when the buybox shows a discount.
- If the block is not present, `import_fees` is omitted. No amount is
  invented.
- The table shows `Import price` when the basis is not the current price,
  in addition to `Import fees`, `Import charges`, and `Landed total`; TSV adds
  `AmazonChoice*`, `ImportPrice*`, and `Import*`.
- In `--compare` the `Choice` and `Landed` columns appear only when at least one
  product has the data; `Landed` has a `*` when the basis is the list price, with a note
  at the bottom of the table. TSV compare adds `AmazonChoice`, `ImportPrice`,
  `ImportPriceBasis`, `ImportCharges`, `LandedTotal`, `ImportAtCheckout`, and
  `ImportText`.

The import block is generic: if Amazon exposes it on another domain, it is
read with the same logic (numeric table or disclosure only). US sales
taxes, however, are not on the product page and remain available only
at checkout.

### Prime prices

When Amazon shows a price reserved for Prime customers, the main `price` is
the standard offer and `prime` holds the Prime price:

- `prime.price`: the Prime price;
- `prime.original_price`: the reference price for the Prime discount;
- `prime.discount`: the Prime discount (for example `-13%`);
- `prime.only`: true when the page has no regular offer.

The Web UI shows the Prime price separately and marks a Prime-only offer. The
comparison uses the standard price for the best-price label and excludes
Prime-only products. Monitors record `prime_price`, `prime_price_amount`, and
`prime_only`.

### Discounts, coupons, and deal badges

`details` exposes:

- `discount`: the percentage shown by Amazon or, if missing, the one
  calculated from `price` and `original_price` (e.g. `89,99 €` → `24,99 €` = `-72%`);
- `coupon`: the normalized coupon percentage (e.g. `-30%`), not the promotion's
  internal code;
- `deal`: the badge text when the page exposes it (e.g. `Black Friday
  anticipado`, `Limited time deal`, `Offerta a tempo`); omitted if absent;
- `deal_id`: Amazon's promotion identifier (`amzn1.deal.…`), useful
  for distinguishing two different offers on the same product;
- `deal_ends_text`: the localized terms sentence, for example
  `Questa offerta termina il 18 novembre 2026`;
- `deal_ends_on`: the normalized date `YYYY-MM-DD`, when recognizable;
- `deal_ends_in_days`: whole days remaining until the end date;
- `deal_has_countdown`: `true` when Amazon shows countdown labels.

Example:

```json
{
  "deal": "Offerta a tempo",
  "deal_id": "amzn1.deal.86ef82ae",
  "deal_ends_text": "Questa offerta termina il 18 novembre 2026",
  "deal_ends_on": "2026-11-18",
  "deal_ends_in_days": 2,
  "deal_has_countdown": true
}
```

Amazon inserts only the end date and countdown templates
(`NO_OF_HOURS`, `NO_OF_MINUTES`, `NO_OF_SECONDS`) into the server HTML; the seconds are
calculated by JavaScript in the browser. The CLI therefore does not invent an
`HH:MM:SS` timer: it shows the date, remaining days, and whether a countdown
is present.

In `search` the `Badge` column appears when at least one result has a deal
badge; JSON and TSV expose `deal`, `deal_id`, `original_price`, and `discount`.
In TSV these columns are at the end; in the table they appear only when
present. Values are localized and dynamic.

The `AMZ_DOMAIN` variable sets the default domain. Also accepted are
`amazon.it`, `www.amazon.it`, `.it`, `amazon.de`, `www.amazon.de`, `.de`,
`amazon.fr`, `www.amazon.fr`, and `.fr`.

`--domain com` uses `en-US`, `/-/en/` paths, `i18n-prefs=USD` cookie, `$`
format (USD forced even from an Italian IP), and HTTP transport by default (with curl,
details may receive WAF/503). `--domain de` uses Amazon.de with EUR, stable
English UI (`lc-acbde=en_GB`), Firefox, and standard `s`/`dp` paths; delivery and
cross-border work like the other domains. `--domain uk` uses `en-GB`,
`/-/en-gb/` paths, `i18n-prefs=GBP` cookie, and `£` format; the `/-/en-gb/` warm-up
is enabled and creates the session needed for search. On Linux requests use the
system `curl-impersonate` command; on Windows they use `CurlHandler` with the
`libcurl-impersonate.dll` included in the release package (or the one from the
`CurlImpersonate.Http` package in a source build). `--domain jp` uses `ja-JP`,
`/-/ja/` paths, `i18n-prefs=JPY` cookie, whole-yen format `￥2,399`, and
Japanese labels. `--domain es` uses `es-ES`, `/-/es/` paths, `i18n-prefs=EUR`
cookie, Spanish format `1.930,40 €`, and curl-impersonate with the Firefox147
profile. Warm-up remains disabled. In multi-marketplace search wait at least 3
seconds between one domain and the next; the client does this automatically.
COM, JP, and ES disable warm-up because on those domains it can trigger
HTTP 503/WAF; UK prices are in pounds (`£237.37`, `Was: £265.82`).

Operational note: on `.fr` the standard paths `/` and `/s?k=` may be
intercepted by **AWS WAF** (HTTP 202 and `challenge.js`). The FR profile uses
localized paths `/-/fr/` and `/-/fr/s?k=`, which on the current edge return
HTTP 200. If an **Akamai** interstitial still appears
(`bm-verify`/`_sec/verify`), the client solves it; the
`FranceAkamaiChallengeTests` test uses the exact Akamai HTML from the HAR
`www.amazon.fr_Archive [26-11-13 22-01-02]` and verifies PoW and POST. If AWS
WAF appears on the requested page, live search fails with an explicit anti-bot
error: in that case cookies from a valid browser session or a
WAF solver are required. Warm-up is best-effort: if the home page responds with
AWS WAF or HTTP 503, the client ignores it and tries search/detail directly (on `.it`
the home page often returns HTTP 202 + `challenge.js` even when search is
reachable). An ASIN unavailable in the marketplace is reported as `HTTP 404` ("Page not
found"), not as an anti-bot block.

### Monitoring (`monitor`)

`monitor` runs sequential cycles on the same products, saves observations,
and generates alerts when price or group total thresholds are met.

```bash
# one product, every hour
amz-cli monitor B07Q6Y5SC7 --timer 3600 --save-format json --save-path ./monitor

# multiple domains in the same run, with postcode per domain
amz-cli monitor it:B07Q6Y5SC7 de:B0DP1VG1DZ \
  --timer 3600 --cap it:20121 --cap de:10115 --save-path ./monitor

# list from a file, a single cycle for cron/systemd
amz-cli monitor --asin-file asins.txt --once --save-format csv --save-path ./monitor

# configuration with thresholds and groups
amz-cli monitor --config monitor.json
```

#### Input

Each target can be:

| Form | Domain |
|---|---|
| `B07Q6Y5SC7` | `--domain` (default `it`) |
| `it:B07Q6Y5SC7` | explicit |
| `us:B07Q6Y5SC7` | `com` |
| `https://www.amazon.co.uk/dp/B0D9YDFGHD` | inferred from the host |
| `https://www.amazon.es/dp/B0CGVQNB9F` | inferred from the host |

`--asin-file` accepts text with one target per line, `#` comments, commas, or
CSV/TSV with a `domain,asin` header:

```csv
domain,asin
it,B07Q6Y5SC7
de,B0DP1VG1DZ
```

The same ASIN on different domains is monitored twice. Deduplication uses the
`(domain, asin)` key. In a monitor JSON configuration a product can belong to
only one group. In the shared saved-products library a product can belong to
multiple groups; each group can be monitored by its own binding.

#### Monitoring saved products and groups

Saved-library monitoring is an explicit opt-in: saving a product or creating a
group never starts a monitor. The target is stored in a monitor binding, so the
same thresholds can be reused by `--binding` and by startup.

```bash
# monitor one saved product
amz-cli monitor --library-product B0H25NG1JK \
  --max-price 600 --drop-percent 5 --rise-percent 10 \
  --baseline first_seen --timer 600

# monitor a saved group, live
amz-cli monitor --library-group "Laptop ufficio" \
  --total-max 1200 --max-price 900 --timer 3600

# multi-currency group: convert every price to EUR first
amz-cli monitor --library-group "Laptop ufficio" \
  --currency EUR --fx GBP=1.17 --total-max 2500

# reuse a saved binding
amz-cli monitor --binding group:g_laptops --timer 3600

# manage bindings
amz-cli monitor --list-bindings
amz-cli monitor --list-bindings --json
amz-cli monitor --remove-binding group:g_laptops
```

The group composition is reloaded on every cycle. Adding or removing products
from the saved group is picked up without restarting the monitor. A group can be
empty in the library, but monitoring it requires at least one product. Library
monitoring currently uses timer mode; the adaptive schedule remains available
for legacy ASIN/config targets.

A product that belongs to a group cannot also be monitored individually. The
command reports the group name and suggests either removing the product from
the group or monitoring the group. The same check is repeated live: if a
product is added to a group while its individual monitor is running, the
monitor pauses and reports the conflict until it is resolved.

Multi-currency groups are converted to one target currency using manual
multipliers:

```bash
amz-cli monitor --library-group g_laptops --currency EUR \
  --fx GBP=1.17 --fx USD=0.92 --total-max 2500
```

The group total and `--total-max` are in the target currency. Per-product
`--max-price`, `--drop-percent`, and `--rise-percent` remain in each product's
marketplace currency. A missing FX rate blocks the plan with a clear error.

Bindings are stored in `<state root>/monitor-bindings.json`. They also store
runtime defaults (timer, request delay, save path/file, format, layout, and
notifications); `--binding` uses them unless explicit flags override them.
`monitor --list --json` exposes `binding_id`, which links a running process to
the same binding used by the Web Monitoring page. `--startup` stores
`--binding <id>` so threshold changes apply at the next login without
reinstalling the startup entry. The old monitor configuration format remains
supported. Import its groups and items explicitly:

```bash
amz-cli library import-monitor monitor.json
amz-cli library import-monitor monitor.json --dry-run --json
```

The import creates minimal library products (ASIN/title), groups, and bindings,
preserving thresholds. It does not start monitoring and does not delete the old
configuration.

#### JSON configuration

```json
{
  "schema_version": 1,
  "domain": "it",
  "timer_seconds": 3600,
  "request_delay_seconds": 10,
  "save": {
    "path": "./monitor",
    "format": "json",
    "layout": "auto"
  },
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

`--timer` is the interval between the start of one cycle and the next; minimum 10
seconds, default 3600. Use `--timer 600` for a check every 10 minutes.
`--request-delay` is the pause between two product requests;
minimum 6 seconds, default 10. The monitor is always sequential, even with multiple
domains.

With multiple domains, postcodes and cookies must be specified per domain:
`--cap it:20121 --cap de:10115` and
`--domain-cookie it:"session-id=..." --domain-cookie de:"..."`. A bare `--cookie`
is allowed only with a single domain.

#### Adaptive scheduling

The classic mode with `timer_seconds` remains the default. A `schedule`
block enables a per-window budget with randomized gaps:

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

The model is per unit:

- a product outside a group is a unit costing **1 request** and a min/max gap
  between two checks of the same ASIN;
- a group is a unit costing the **number of items in the group** and a gap
  between two cycle starts;
- `quantity` changes the group's monetary total, not the request cost.

`max_requests` is mandatory and counts all product detail requests,
including those inside groups. The budget is distributed with a token bucket:
a group of 8 starts only when 8 tokens are available. This way the cap is not
consumed entirely in the first hours.

Example: with `max_requests: 500` and `window_seconds: 86400`, a group of 8
consumes at most 500/8 = 62 cycles per day, so about one cycle every 23
minutes. The minimum gap is a lower bound, not a frequency guarantee.

The window is anchored to monitor startup, not to the clock. On restart:

- budget state is resumed if the window is the same;
- late units run only if they respect the minimum gap and
  finish before the window ends;
- excess units are discarded for that window, without bursts.

CLI:

```bash
amz-cli monitor --config monitor.json --schedule adaptive \
  --window 86400 --max-requests 500 --min-gap 600 --max-gap 1200
```

`--timer` and adaptive options are not compatible. `--once`, `--cycles`, and
`--html` continue to use cycle mode and ignore adaptive scheduling.
Gaps can be overridden for a single item or group:

```json
{ "asin": "B000000001", "min_gap_seconds": 900, "max_gap_seconds": 1800 }
```

```json
{ "id": "pc-build", "min_gap_seconds": 600, "max_gap_seconds": 900 }
```

Per-item gaps are not allowed inside groups: they are configured on the group.

`--dry-run` shows units, cost, budget, and a window simulation without
network. Configurations with `schedule` require this version; earlier
configurations without `schedule` continue to work.

#### Rules

- `max_price`: alert if `price <= max_price`.
- `drop_percent`: alert if `price <= baseline * (1 - drop_percent / 100)`.
- `rise_percent`: alert if `price >= baseline * (1 + rise_percent / 100)`.
  `drop_percent` and `rise_percent` can be active together.
- `baseline`:
  - `original`: strikethrough price on the page;
  - `first_seen`: first observed price;
  - `max_seen`: highest observed price;
  - `last_seen`: price from the previous cycle;
  - `fixed`: uses explicit `baseline_price`.
- `cooldown_seconds`: default 21600 (6 hours). An alert fires on the
  `false -> true` transition; if the condition remains true, it repeats at most every
  cooldown.

The group calculates:

```text
totale = somma(prezzo_corrente * quantity)
alert se totale <= total_max
```

If a product in the group has no price, the group is `incomplete` and does not fire,
unless `allow_incomplete: true`. The total uses the buybox current price:
it does not include coupons, shipping, or FX conversion. A group can contain different
domains only if they share the same currency (IT/DE/FR/ES are all EUR).

#### Files and layout

`--layout auto` uses `per-group` when the configuration has groups, otherwise
`combined`.

`combined`: separate global files.

```text
monitor/
├── amz-monitor.items.jsonl
├── amz-monitor.groups.jsonl
├── amz-monitor.alerts.jsonl
├── amz-monitor.state.json
└── amz-monitor.lock
```

`per-group` with JSONL: one file per group with `item`, `group`, and `alert`
records; items outside groups go to `ungrouped.jsonl`.

```text
monitor/amz-monitor/
├── pc-build.jsonl
└── ungrouped.jsonl
```

`per-group` with CSV: one folder per group with `items.csv`, `groups.csv`,
`alerts.csv`.

`single`: a single global JSONL with all records, JSONL format only.

Records keep `domain`, `group_id`, `triggered_rules`, `price_amount`,
`currency`, `deal`, `deal_id`, `deal_ends_text`, `deal_ends_on`,
`deal_ends_in_days`, `deal_has_countdown`, `coupon`, and `error`. Alerts are
append-only and contain `rule_id`, `threshold`, `observed`, `message`, and the
reference to the product or group.

#### Background

```bash
amz-cli monitor --config monitor.json --detach
amz-cli monitor --config monitor.json --status
amz-cli monitor --config monitor.json --stop
```

`--detach` creates a separate process with a pidfile and log; on Linux it uses
`setsid` when available. The pidfile does not contain cookies. Alternatively, run the
monitor in the foreground with systemd or cron using `--once`.

#### Active monitors

`--list` shows all monitors started by the user, even those started from other
folders or with `--detach`:

```bash
amz-cli monitor --list
amz-cli monitor --list --json
amz-cli monitor --list --tk-opt
amz-cli monitor --list --all
amz-cli monitor --list --prune
amz-cli monitor --list --verify
amz-cli monitor --list --save-path ./monitor
amz-cli monitor --stats --save-path ./monitor
amz-cli monitor --stats --save-path ./monitor --json
amz-cli monitor --restart
amz-cli monitor --restart --json
```

Each run records metadata in a per-user registry:

```text
Linux:   $XDG_STATE_HOME/amz-cli/monitors/*.json
         fallback ~/.local/state/amz-cli/monitors/*.json
Windows: %LOCALAPPDATA%\AmzCLI\monitors\*.json
```

The entry is removed on normal exit, with Ctrl+C or SIGTERM. If the process
is killed with SIGKILL, crashes, or its Windows task is terminated, the
entry remains. On the next start the registry is reconciled: stale entries
for the same monitor (same startup id or same `--save-path`/
`--save-file`) are removed automatically. The check uses the PID and process start
time; if the time is unavailable it also compares the executable path,
so it does not mistake a reused PID.

- default: active monitors only;
- a configured binding that is not running is not a process: use
  `monitor --list-bindings`, whose `Status` column shows `running`, `stopped`,
  `stale`, or `configured`;
- `--all`: includes stale entries from other monitors; duplicates of an
  active monitor remain hidden;
- `--prune`: removes registry and pidfile for stale entries, including duplicates;
  if the pidfile has already been reused by another monitor, it is not deleted;
- `--verify`: verifies that the Windows task or systemd unit actually exists and
  adds `startup_registered` to JSON/TSV output;
- `--save-path <DIR>`: also scans that folder for unregistered pidfiles
  (useful for monitors started with earlier versions);
- `--verbose`: shows full log and pidfile paths;
- `--json` / `--tk-opt`: output for scripts.

#### Price summary (`--stats`)

`--stats` reads local state, without network:

```bash
amz-cli monitor --stats --save-path ./monitor
amz-cli monitor --stats --save-path ./monitor --json
```

Shows first/minimum/maximum/last price per target, last cycle, number of
requests/observations, and rules with alerts. The count combines the counter
in state with `item` rows from the recorded JSONL history. For groups it
shows name, items, total/minimum/maximum, last cycle, requests, and alerts.
`--json` returns `state_path`, `targets`, `groups`, `alerted_rules`,
`target_rows`, and `group_rows`; each row includes `observations`.

#### Restart (`--restart`)

```bash
amz-cli monitor --restart
amz-cli monitor --restart --save-path ./monitor --save-file amz-monitor
amz-cli monitor --restart --json
```

Restarts the monitors of the current installation. It rejects foreground monitors or
detached ones without restart metadata. `--json` returns `stopped`, `restarted`,
and `errors`.

The table shows PID, status, mode (`foreground`/`detach`), domains, number
of targets, timer, start, last cycle, and data files. The last cycle is the maximum
`last_seen_at` in `amz-monitor.state.json`.

If there is nothing:

```text
No active monitors.
```

With stale entries and without `--all` or `--prune`:

```text
1 voce stale. Usa --all per vederla o --prune per rimuoverla.
```

`--list` does not require ASINs or config and is not compatible with `--status`,
`--stop`, `--detach`, `--once`, `--cycles`, `--html`, `--dry-run`, or
`--notify-test`.

#### Automatic startup (`--startup`)

`--startup` installs automatic startup for the user and starts the monitor immediately:

```bash
amz-cli monitor --config monitor.json --startup
amz-cli monitor it:B07Q6Y5SC7 --timer 3600 --save-path ./monitor --startup
```

To remove it:

```bash
amz-cli monitor --config monitor.json --startup-remove
amz-cli monitor --save-path ./monitor --startup-remove
```

If you run the removal from another folder, use an absolute `--save-path`.

Automatic startup runs **at user logon**, not as a system service: it is
meant to preserve the user environment, cookies, and desktop notifications.

**Linux**: a systemd user service is created:

```text
~/.config/systemd/user/amz-monitor-<hash>.service
~/.local/state/amz-cli/startups/<hash>.json
```

Useful commands:

```bash
systemctl --user status amz-monitor-<hash>.service
systemctl --user restart amz-monitor-<hash>.service
systemctl --user stop amz-monitor-<hash>.service
journalctl --user -u amz-monitor-<hash>.service -n 50
```

To start at boot even without login:

```bash
loginctl enable-linger "$USER"
```

Desktop notifications still arrive only after graphical login.

**Windows**: a Task Scheduler task is created for the current user, without
administrator rights:

```text
Name: AmzCLI-<hash>
Trigger: at user logon
Action: amz-notify.exe --startup-launch <hash>
```

The `amz-notify.exe` helper is a WinExe already included in the publish: it starts
`amz-cli.exe` without opening a console window. In this mode it does not send
notifications: it is reused only as a graphical launcher. Stop and removal also
first try to stop the monitor with `amz-cli monitor --stop` and only then
terminate the task. The task contains neither cookies nor the full command line;
the arguments remain in the spec:

```text
%LOCALAPPDATA%\AmzCLI\startups\<hash>.json
```

On Windows startup is at logon: a service in session 0 could not show
toasts or use the user environment.

The list shows the `Startup` column:

```text
┌───────┬────────────┬───────────┬─────────┐
│ PID   │ Status     │ Mode      │ Startup │
├───────┼────────────┼───────────┼─────────┤
│ 12345 │ running    │ startup   │ systemd │
│ —     │ configured │ startup   │ systemd │
└───────┴────────────┴───────────┴─────────┘
```

- `--list`: active monitors only, with `Startup` filled in.
- `--list --all`: also includes configured startups that are not active.
- In configured rows the PID is `0` and the JSON status is `configured`.
- `--status`: shows unit/task and monitor startup status.
- `--stop`: stops only the current instance; it does **not** disable startup.
- `--startup-remove`: disables and removes unit/task; stops the monitor if it
  was started by startup.

If you change `--config`, `--save-path`, or `--save-file`, reinstall with `--startup`.
If you move the binary folder, reinstall automatic startup.

`--startup` is not compatible with `--detach`, `--status`, `--stop`, `--list`,
`--once`, `--cycles`, `--html`, `--dry-run`, `--notify-test`, or
`--exit-on-alert`.

#### Exit codes

- `0`: no errors and no alerts with `--exit-on-alert`.
- `2`: cycle completed with partial errors.
- `3`: alert generated and `--exit-on-alert`.
- `1`: fatal error.
- `130`: interruption.

`--dry-run` validates configuration, domains, postcodes, groups, thresholds, files, and request
budget without network. `--exit-on-alert` makes alerts usable from cron.

#### Desktop notifications

Desktop notifications are opt-in. They can be enabled from the CLI or in JSON:

```json
{
  "notifications": {
    "enabled": true,
    "on": ["alerts", "errors", "blocks", "fatal"],
    "cooldown_seconds": 3600
  }
}
```

```bash
amz-cli monitor --config monitor.json
amz-cli monitor --config monitor.json --notify-on errors
amz-cli monitor --notify-test
```

CLI flags take precedence over config values. `notifications.enabled: true`
enables notifications even without `--notify`; `--notify-on` and
`--notify-cooldown` override `on` and `cooldown_seconds`.

| Option | Description |
|---|---|
| `--notify` | enables notifications |
| `--notify-on <EVENTS>` | `alerts`, `errors`, `blocks`, `fatal`, `all`; default `alerts` |
| `--notify-cooldown <SECONDS>` | cooldown for errors/blocks, default 3600 |
| `--notify-test` | sends a test notification and exits, without network |

**Linux**: uses `notify-send` (libnotify), with `zenity`/`kdialog` fallback. On
Debian/Ubuntu install `libnotify-bin`. If the monitor runs as a system service
or without a graphical session, `notify-send` cannot reach the desktop:
use a systemd user service or a webhook in the future.

**Windows**: the publish includes `amz-notify.exe`, a native self-contained
helper based on CommunityToolkit. It sends the toast, so
Windows attributes it to **AMZ CLI Monitor**, not to PowerShell. There is no need to
install the .NET runtime or the Windows App SDK: the helper is in the same folder as `amz-cli.exe`.

If the helper is missing or fails, the CLI automatically falls back to PowerShell:
first BurntToast, then the WinRT toast, and finally `msg.exe`. `--notify-test` shows
which backend was used:

```text
Backend: windows/native
Esito: notifica inviata
Dettaglio: communitytoolkit
```

Alternatively you can install BurntToast for the fallback:

```powershell
Install-Module BurntToast -Scope CurrentUser
```

The toast must run in the interactive user session: with Task Scheduler
choose “Run only when user is logged on”; a Windows service in
session 0 cannot show desktop notifications.

**Clicking the Windows notification**: the toast contains a structured payload with
product, current price, previous price, discount, threshold, coupon, and badge.
Clicking it opens a terminal window with the details in Spectre.Console.
The click **does not send the notification again**: the helper recognizes the activation and
shows only the details. For the test notification, a window opens explaining
that it is a test and closes when a key is pressed. Payloads are saved in
`%LOCALAPPDATA%\AmzCLI\notifications` and cleaned up after 7 days.

The events:

- `alerts`: a price/percentage/group alert actually emitted;
- `errors`: cycle error digest (404, timeout, network);
- `blocks`: Amazon/WAF block, domain in cooldown or disabled;
- `fatal`: monitor stopped due to a fatal error.

Price alerts use rule cooldowns. Errors and blocks use
`--notify-cooldown` and persistent state in `amz-monitor.state.json`, so they
are not repeated every cycle. If the backend is unavailable, the monitor
continues and writes a warning to the log; `--notify-test` verifies the setup.

#### Installing `amz-cli` in PATH (Windows only)

From the `publish\win-x64` folder:

```powershell
.\amz-cli --install-env-var
```

Adds the current folder to the **user PATH** (`HKCU\Environment`), without
requiring administrator rights. Then reopen the terminal and verify:

```powershell
where amz-cli
amz-cli --version
```

Variants:

```powershell
# system PATH, for all users (administrator terminal)
.\amz-cli --install-env-var system

# status: user PATH, system PATH, and where amz-cli
.\amz-cli --status-env-var

# removal
.\amz-cli --uninstall-env-var
.\amz-cli --uninstall-env-var system
```

On Windows `amz-cli --help` also shows the equivalent command:

```powershell
amz-cli env-var --install
amz-cli env-var --install --system
amz-cli env-var --status
amz-cli env-var --uninstall
```

The PATH change does not update already open terminals: close and reopen them.
If you move the `publish\win-x64` folder, remove the old entry and install the
new one. On Linux the flag is not available and the `env-var` command does not appear
in help.

## Version

`--version` shows a compact line with channel and release date:

```bash
amz-cli --version
# amz-cli 3.2.2 (stable, 2026-11-23)

amz-cli --version --short
# amz-cli 3.2.2

amz-cli --version --json
amz-cli --version --verbose
```

The release date comes from the signed manifest of the last update or from
`release_published_at` in `install.json`; for a source build the channel is
`dev` and the date may be unavailable. There are no hardcoded dates in the
code.

## Paths and diagnostics (`paths`)

`paths` shows the files, folders, and external registrations used by the CLI:

```bash
amz-cli paths
amz-cli paths --all
amz-cli paths --json
amz-cli paths --install
amz-cli paths --save-path ./monitor
amz-cli paths --verbose --ascii
```

- default: existing paths only;
- `--all`: includes known missing paths and empty folders;
- `--json`: structured output, useful for agents and scripts;
- `--install`: expands installation files;
- `--save-path`: adds monitors present in an unregistered folder;
- `--verbose`: also shows modification time;
- `--ascii`: tree without Unicode characters.

The command covers:

- installation (`amz-cli`, `amz-cli.exe`, `amz-notify.exe`, DLLs);
- per-user state (`monitors`, `startups`, `updates`, Windows notifications);
- monitor files (`state`, `pid`, `log`, lock, JSONL/CSV);
- configured systemd units or Windows tasks.

File contents are not read or printed: no cookies, tokens, or
sensitive arguments. Example for an agent:

```bash
amz-cli paths --json --all > /tmp/amazon-paths.json
```

## Doctor

`doctor` is the single health check for the installation:

```bash
amz-cli doctor
amz-cli doctor --json
amz-cli doctor --offline
amz-cli doctor --channel beta
```

It checks:

- runtime, transport, and `curl`/DLL;
- `install.json`, version, platform, and missing files;
- PATH/symlink on Linux and user PATH on Windows;
- available update and manifest signature;
- monitor registry (active, stale, configured);
- actual presence of the Windows task or systemd unit;
- `amz-notify.exe` helper: GUI subsystem (`2`) required on Windows;
- installed skill aligned with the embedded version;
- state directory.

`--offline` skips only the online release check. The command is
read-only: it does not update, prune, or reinstall anything. It exits with `1` if it finds
errors; warnings do not block.

## Installation

The standalone installer creates a per-user folder, downloads the latest signed
release, verifies signature and hash, copies files, and configures PATH/symlink.

The `install.sh`/`install.ps1` bootstrap scripts first verify the RSA
PKCS#1/SHA-256 signature of `installer.json` and `installer.sha256`, then the SHA-256
of the downloaded installer. On Linux `openssl` is required; on Windows verification uses
.NET/PowerShell 5.1 or 7.

### Linux x64

```bash
curl -fsSL https://github.com/Duster98/amz-cli/releases/download/installer-stable/install.sh | bash
```

Or download and run `amz-cli-installer`:

```bash
./amz-cli-installer --dir ~/.local/share/amz-cli
```

### Windows x64

```powershell
irm https://github.com/Duster98/amz-cli/releases/download/installer-stable/install.ps1 | iex
```

Or run `amz-cli-installer.exe`.

### Behavior

- Linux: `$XDG_DATA_HOME/amz-cli` or `~/.local/share/amz-cli`;
  symlink in `~/.local/bin/amz-cli` and application-menu entry
  `~/.local/share/applications/amz-cli.desktop` to open the web interface
  without a terminal. The Linux installer does not create a desktop shortcut.
- Windows: `%LOCALAPPDATA%\Programs\AmzCLI`; adds the folder to the user PATH
  and creates an AMZ CLI shortcut (Start Menu and Desktop) that starts
  `amz-cli web --desktop`.
- `--no-path` disables symlink/PATH and application shortcuts.
- `--dir <PATH>` chooses another folder.
- `--channel beta` uses the beta channel.
- `--dry-run` shows the plan without writing.
- `--force` reinstalls or replaces an existing installation.
- `--uninstall` removes only installation files; monitor state
  remains.
- `--purge` with `--uninstall` also removes the folder.

The installer installs the latest CLI release; subsequent updates
continue to use `amz-cli update`.

## Updates

The CLI can check for and apply signed updates. The Web **Status** page exposes
the same signed update flow with a **Check for updates** button and real-time
progress.

On Linux, curl-impersonate is a system dependency: the updater reports it in
the Status page but does not replace it; use the package manager (for example
`pacman -S curl-impersonate`) or `AMZ_CURL_IMPERSONATE`. On Windows the native
DLLs are part of the signed package, are included in the backup, and are
replaced by the update. Windows update verification now always requires the
bundled curl-impersonate files to load successfully.

```bash
amz-cli update --check
amz-cli update --check --json
amz-cli update
amz-cli update --yes
amz-cli update --status
amz-cli update --rollback --yes
amz-cli update --delete-backups
amz-cli update --delete-backups --dry-run
amz-cli update --delete-backups --yes
amz-cli update --enable-auto
amz-cli update --disable-auto
```

The check reads the signed platform feed from GitHub Releases
(`linux-x64.json` or `win-x64.json`). The feed contains the manifest, signature, and
direct asset URLs; the client does not use the GitHub API or tokens.

The release is protected by:

- **RSA-PSS/SHA-256** signature of the manifest, with the public key included in the
  binary and `signing_key_id` for rotation;
- SHA-256 and size of both the compressed and decompressed file;
- blocking of absolute paths, `..`, symlinks, and redirects outside GitHub;
- rejection of downgrades and `min_supported_version` check.

Before modifying the installation, a `.tar.gz` backup is created in:

- Linux: `~/.local/state/amz-cli/updates/backups/`
- Windows: `%LOCALAPPDATA%\AmzCLI\updates\backups\`

The backup excludes `data/`, `config/`, and monitor state. After
replacement the CLI runs `__update-verify` on the new binary: only a positive
result marks the update as `completed`; otherwise the previous files
are restored and the journal moves to `rolled_back`.

During atomic replacement, temporary folders are created next to the
installation (`.amazon-update-*`, `.amazon-update-rollback-*`,
`.amazon-restore-*`). On Linux the `.` prefix hides them; on Windows the
`Hidden` attribute is applied. If they remain after an error, they are removed on the
next update or with `--delete-backups`.

To delete all backups of the current installation and reset updater
state:

```bash
amz-cli update --delete-backups
amz-cli update --delete-backups --dry-run
amz-cli update --delete-backups --yes
```

The command deletes `.tar.gz` archives and `.json` sidecars only for
the current installation, clears the journal (so `--status` returns to
“No update recorded”), removes sibling temporary folders, and
cleans `updates/downloads` and `updates/plans`. It requires interactive confirmation
or `--yes`; `--json` returns the summary. After deletion
`--rollback` is no longer available.

Backups are associated with the installation directory: `--rollback` and
`--status` consider only backups of the current installation. The private
signing key is never included in the binary.

**2.26.0** is the first version with the updater; **2.34.0** is the last legacy
release. **3.2.2** is the current source version; **3.2.1** is the latest
published release.
**2.27.0** introduces the automatic check described below. Versions earlier than 2.26.0
do not have the `update` command, so they must be installed manually once.

## Auto-update

Auto-update is **enabled by default** on the `stable` channel and checks at
most once per hour:

- at the start of a normal command (`search`, `details`, `deals`) a background
  check starts, without slowing the command;
- if a monitor is active, its internal cycle checks every hour;
- if the monitor is `--startup` or `--detach`, it is stopped only after
  download, hash, and backup are ready, then restarted with the new
  version;
- a monitor started manually in the foreground is **not touched**: the update
  is postponed and notified, because it cannot be resumed in the original
  terminal.

Global configuration:

- Linux: `~/.local/state/amz-cli/updates/config.json`
- Windows: `%LOCALAPPDATA%\AmzCLI\updates\config.json`

```json
{
  "enabled": true,
  "channel": "stable",
  "interval_seconds": 3600,
  "auto_apply": true,
  "restart_monitors": true,
  "defer_foreground_monitor": true,
  "notify": true
}
```

The monitor timer can be disabled for a single monitor:

```bash
amz-cli monitor --no-auto-update ...
```

Auto-update can be disabled globally with
`amz-cli update --disable-auto` or with `AMZ_NO_AUTO_UPDATE=1`.

After a result an informational notification is sent. On Windows clicking opens
the details viewer; on Linux it uses `notify-send --action` and opens
`zenity`/`kdialog` if available. The click **does not** decide whether to update: the
decision is already automatic. The panel shows version, result, backup, log, and
monitor status; for full details:

```bash
amz-cli update --status
```

The notification details also show the release `notes` field, i.e. the
plain-text fallback. Markdown (`notes_markdown`) remains
only in the `amz-cli update --check` console.

Automatic update may be postponed if:

- a foreground monitor is active;
- a `--detach` monitor started with a previous version lacks restart
  metadata (it must be restarted manually once);
- the directory is not writable or there is not enough space;
- `min_supported_version` requires manual intervention;
- the release is beta (betas remain manual with `--channel beta`).

The actual update is performed by a temporary helper copy
of the executable on both platforms. On Windows this is necessary because the
running binary is locked; on Linux it prevents the running process from loading
assemblies from the just-replaced file. If a monitor is active from the same
folder, the automatic update stops and restarts it; the manual update without
auto-update keeps the previous behavior.

Backups are associated with the installation directory: `--rollback` and
`--status` consider only backups of the current installation. The private
signing key is never included in the binary.

## Multi-marketplace

The code contains no parsing logic tied to a single domain outside the
`ItalyMarketplace`, `FranceMarketplace`, `UnitedKingdomMarketplace`,
`UnitedStatesMarketplace`, `GermanyMarketplace`, `JapanMarketplace`, and
`SpainMarketplace` profiles:

- `IMarketplaceProfile` provides base URL, `Accept-Language`, localized patterns,
  price/rating formatting, and URL generation;
- `IMarketplacePatterns` contains localized regexes (rating, reviews, coupons,
  seller/shipping/returns labels, specs, byline);
- `AmazonPatterns` contains only common structural patterns;
- `MarketplaceRegistry` lists available and planned domains.

To add another domain (for example Amazon.de) you will need:

1. a new implementation of `IMarketplaceProfile` and `IMarketplacePatterns`
   with local labels and currency formats;
2. a real HTML fixture and its golden JSON;
3. registration in `MarketplaceRegistry` with `Available = true`.

HTTP client, CLI commands, renderers, JSON/TSV, and publish do not need to change.

## License

AMZ CLI is licensed under the **Apache License, Version 2.0**. See
[`LICENSE`](LICENSE) for the full text and [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)
for the third-party components included in the binaries.

The license applies to the AMZ CLI code and release artifacts. It does not
grant rights to Amazon content or trademarks, and it does not override Amazon's
terms of use.

## Notes

- Amazon HTML changes frequently; patterns are deliberately isolated in
  `AmazonPatterns`/`ItalyPatterns`/`FrancePatterns`/`UnitedKingdomPatterns` so
  they can be updated with new fixtures.
- “Lazy” ad cards (`asin-ajax-lazy-loader-spinner`) are skipped:
  their content arrives via AJAX and is not a complete search result.
- This tool is for personal/educational use: respect Amazon's terms of service,
  do not make massive requests, and do not automate access to protected content.
- `self-test` makes no network requests: it verifies version, RID, .NET runtime,
  and loading of the curl-impersonate transport.
