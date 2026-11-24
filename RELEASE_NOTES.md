# AMZ CLI 3.2.5

This release includes all 3.2.4 features.

## Comparison sections

- The Specifications, Description, and Amazon details sections now start at
  the same vertical position in every comparison column. Shorter sections
  leave blank space so the next section does not move up.
- All specifications are shown, including the ones that only one product has;
  a dash marks a missing value. Descriptions and Amazon details are shown in
  full after the specifications.

## Customer keep notice

- Product details show Amazon's positive "customers usually keep this item"
  notice with a green check.
- The comparison card shows a green "Kept by customers" badge; the CLI exposes
  `usually_kept` in details, JSON, TSV, and the comparison diff.

# AMZ CLI 3.2.4

This release includes all 3.2.3 features.

## Prime price position

- In the Web comparison and product detail, the Prime price line now appears
  immediately under the main price, before the "Lowest price" and other
  badges. This keeps the Prime-only price associated with the price it
  refers to.

# AMZ CLI 3.2.3

This release includes all 3.2.2 features and fixes Prime-only pricing.

## Prime prices

- When Amazon shows a price reserved for Prime customers, the main `price` is
  now the standard offer; the Prime price appears separately with its original
  price and discount (for example "Prime price 129,99 € -13%").
- Products with only a Prime offer are marked "Prime only" and excluded from
  the comparison's regular best-price calculation.
- `details`, `compare show` and the Web product/comparison views expose the
  Prime price; `--json` adds a nested `prime` object and monitors record
  `prime_price`, `prime_price_amount`, and `prime_only`.

# AMZ CLI 3.2.2

This release includes all 3.2.1 features and adds the changes below.

## LAN access

- New `amz-cli web --remote` option: Kestrel binds to all network interfaces,
  so the Web interface can be opened from another device on the same LAN.
- Startup output and `amz-cli web --status` print the tokenized LAN URL
  (`http://<lan-ip>:<port>/#t=...`).
- Remote mode keeps the existing session protection: the URL token is
  exchanged for an `HttpOnly`/`SameSite=Strict` cookie, and `Host`/`Origin`
  checks accept only loopback, the machine's own interface addresses, and its
  host name. Arbitrary Host headers are still rejected.
- The CSP allows the WebSocket connection to the LAN origin, so SignalR
  real-time updates keep working when the UI is opened remotely.
- The `--remote` setting is preserved across update restarts.

## Stable port

- `--port <PORT>` keeps the Web interface on the same port across restarts.
  `0` remains automatic. The flag already existed; it is now documented and
  covered for LAN use.

## Mobile buy box

- On phones the purchase card stays closed with its top border and sits closer
  to the thumbnail strip; the extra separator above the variant header is gone.
- The inner buy box still drops its top rule to avoid a double border.

## Mobile Web fixes

- The phone top bar now shows the current section (for example **Cronologia**
  or **Prodotti salvati**) instead of the AMZ CLI brand.
- The product detail toolbar is compacted to two rows: back and the
  refresh/save actions on the first, cache and marketplace/ASIN on the second.
- Saved products and history headers put their tabs under the title. Saved
  products keeps tabs, search and list/grid on one row with a floating circular
  **+** button; history keeps tabs and list/grid on one row, with **Clear
  history** aligned to the title row.
- The delivery location picker becomes a top sheet on phones and stays open
  when the keyboard resizes the viewport, so the CAP field remains editable.
- Mobile list results keep the price right-aligned and large; the result index
  is hidden on phones and the bookmark moves below the price to avoid overlap.
- The pin control is hidden in the mobile drawer.
- Selecting an item in the phone side menu now closes the drawer immediately;
  the sticky hover/focus state no longer keeps it open.
- The offers, variants, and review dialogs no longer shift the page left when
  they open: the page already reserves the scrollbar gutter, so the dialog no
  longer adds the scrollbar width a second time.
- Dialog bodies reserve their own scrollbar gutter, so long variant or offer
  lists do not move sideways when the inner scrollbar appears.

## Buy box and gallery polish

- The variant plate no longer shows the dimension label or the variant count
  above the price. **View all variants** now sits below the variant chips on
  both desktop and mobile, and appears only when there are more than three
  variants. When the link is shown, the variant count appears on the left of
  the same footer row.
- Variant chips no longer show the checkmark. The selected chip keeps its
  high-contrast plate, and selecting a variant moves it to the first position
  of the strip so it is immediately visible without scrolling.
- The separator above the delivery/returns band now matches the rating
  separator width on desktop, instead of extending past it.
- The small condition and grade labels in the buy box (for example
  **Ricondizionato** and **Eccellente**) are 2% larger.
- The delivery cutoff (“Order within …”) is now a compact clock badge next to
  the **Delivery** label instead of a separate row; the full text is available
  as the badge tooltip.
- The delivery location is plain text with a small pin icon, without the pill
  badge: it shows the CAP when set, otherwise the city or country (for example
  “Italy”). The full “Deliver to …” text is the tooltip.
- On phones and tablets the same location now sits inline next to the
  **Delivery** label, so the CAP stays visible without adding a new row. Desktop
  keeps it in the “Delivery and returns” header.
- Delivery cost and date share one line (“GRATUITA · giovedì, 26 novembre”),
  removing the extra row and giving the layout more air.
- Japanese cutoffs such as “（8 時間 16 分以内にご注文の場合）” are compacted to
  “8 時間 16 分” in the clock badge, with the full sentence in the tooltip.
- The variant name and price share one row on phones. A long model name is
  truncated with an ellipsis so the price, discount, and condition tags stay
  visible and right-aligned. The single/no-variant layout now keeps the same
  divider and top spacing as the multi-variant plate, so the name and price
  always have the same breathing room.
- On phones the selected product name sits closer to the top of the purchase
  card.
- Single-variant products now show the variant name and availability on the
  left of the price row, matching the multi-variant plate. The availability is
  no longer repeated in the purchase meta.
- Product detail shows the deal badge (for example **Offerta Black Friday**)
  using the same accent tag as search results. On desktop it sits at the far
  right of the rating row; on phones it joins the other badges (best seller,
  Amazon's Choice) on the line below the ratings. Hovering it shows the
  localized deal end date or terms.
- Product details expose `model_name` parsed from Amazon's **Model Name** /
  **Model Number** rows (localized keys are recognized). The CLI prints it as a
  **Model** row and the TSV includes `Model`.
- When a product has no variant twister, the Web buy box uses the model name on
  the left of the price, with availability underneath. If neither a variant
  name nor a model name exists, the availability alone still moves to the price
  row instead of the bottom meta.
- The delivery and returns band uses the card background instead of a tinted
  surface. On desktop the purchase card now follows its content instead of
  being forced to the image square: the card no longer scrolls internally, the
  CTA follows the content, and the delivery band sits naturally after the price
  with a normal gap.
- The product image, thumbnail strip, and stage height follow the card height.
  The square image shrinks to the card height and stays centered in the gallery
  column; the thumbnail strip uses the same height and scrolls when needed.
  Desktop keeps the vertical thumbnail strip. Phones and tablets hide it and
  use a swipeable image with a centered dot indicator instead.
- The delivery location line also appears when a manual postal code is set. It
  shows the CAP in that case; Amazon-calculated locations keep the full city
  and CAP.
- The **Delivery options** link lives in the delivery band: at the right of the
  desktop header, and inline next to the **Delivery** label on phones and
  tablets, showing just the count after a dot (for example **· 3 opzioni**,
  fully linked). It is no longer under the seller/shipping line.
- Portrait product images are no longer clipped: the image frame is constrained
  to the square stage and the image uses `object-fit: contain`, so the whole
  product stays visible even when the stage follows a shorter purchase card.
- The purchase footer spacing is tightened: the gap between the brand/sold line
  and the CTA matches the separator spacing above the line. The “Page read
  from …” note under the CTA is gone, so the orange button closes the card with
  the normal bottom padding.
- Product thumbnails use a white hover/preview surface; the selected thumbnail
  keeps the orange frame.
- The image count badge is removed. On phones and tablets the image can be
  swiped left or right; a small translucent dot indicator at the bottom center
  shows the position. Only the short slide/fade transition runs there, so the
  image does not flash twice. Desktop keeps its original image fade/scale
  transition.
- The delivery options dialog no longer repeats a **Close** button at the
  bottom; the X in the header is the single close control.

## Web shell

- The hover-expanded sidebar now overlays the page on product detail too. It no
  longer shifts the workspace, matching search, monitoring, and saved views.

## Saved products and history headers

- The count summaries under **Saved products** and **History** are gone; counts
  stay inside the tabs.
- Saved products keeps its tabs under the title. **New group** is now a floating
  circular **+** button at the bottom right on every screen size; the inline
  header button is removed. Tabs, search and list/grid stay on the header row.
- The group editor dialog now stays within the mobile viewport and keeps its
  footer usable when the keyboard opens.
- In Saved products the **Products** tab is now **All**, and the redundant
  **All / Only singles** filter row is removed.
- History turns **All / Searches / Products** into the same tab style, with the
  list/grid controls on the same row and **Clear history** aligned with the
  title above.
- Clear history is a bordered button with a trash icon and label instead of a
  text link.
- The first row in list mode no longer shows the colored top border; only the
  left type stripe remains.

## Monitoring

- The monitor detail header now mirrors the product detail: back on the left,
  status/marketplace/ASIN at the top right with the same meta badge used on
  product detail, title below, and the action row underneath.
- Stopping a startup-managed monitor now asks for confirmation and removes the
  startup unit/task, so systemd or Task Scheduler cannot restart it immediately.
  The startup panel reflects the removal.

## Comparison tabs

- New **Confronti** page under Esplora: up to six closable tabs, with three
  compact product dossiers side by side on desktop and one at a time on phones.
- Each dossier shows image, title, price, condition and grade, rating,
  availability, seller, delivery, returns and aligned key specifications.
  Differences are marked and the lowest price is labelled when currencies match.
- New **Confronta** button on the product detail adds or removes the product and
  lights the Confronti rail badge.
- New shared `web/compare.json` and SignalR `CompareChanged` event.
- New CLI commands: `amz-cli compare add`, `show`, `list`, `remove`, `clear`.
  Accepts ASINs, `domain:ASIN` pairs and Amazon URLs; agents can push products
  with `--source agent`.
- `amz-cli compare show` prints the shared comparison in the terminal.
  `--diff` transposes all fields and specifications, marks differing rows and
  missing values, and is available as JSON/TSV for agents.

## Settings

- New **Settings** view in the sidebar, with a sticky section index on desktop
  and a scrollable section strip on phones.
- Appearance: theme, language, sidebar pinning, results and monitoring layouts.
- Search defaults: primary/active marketplaces, deals marketplace, sorting,
  rating, price range and the same result filters used by search.
- Delivery: the shared country/postal-code picker.
- Library and monitoring: saved products, groups and history counts plus the
  read-only refresh policy configured by the CLI/cache.
- System and updates: version, paths, update check and auto-update summary.
- Sidebar regrouped into **Esplora**, **Biblioteca** and **Sistema**; Settings
  now uses a gear icon and is the last navigation item.
- UI preferences save automatically; system and cache policy values are shown
  read-only in this release.

## Advanced HTTP settings

- New **Advanced** section in Settings: transport, curl-impersonate profile,
  User-Agent, full browser headers, request timeout and curl fallback.
- The same values are stored in `http-settings.json` in the state directory and
  are shared by the CLI, the Web UI and monitors.
- New CLI commands: `amz-cli settings show`, `settings set`, `settings reset`
  and `settings path`. Command-line flags still take precedence.
- The Settings page reports the effective transport/profile/User-Agent for the
  active marketplaces, the curl-impersonate runtime and the active HTTP
  cooldowns, with a button to clear a cooldown.
- Monitors load the saved configuration at startup; restart a running monitor
  to apply changes.

## Notes

- No breaking changes. Without `--remote`, the server continues to listen on
  loopback only.
- If another instance is already running, `--remote` and a different `--port`
  now produce a clear message instead of silently reusing the existing
  local-only instance.
