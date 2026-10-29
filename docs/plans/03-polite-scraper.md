# Polite scraper: track item prices by URL (2026-10-29)

> Working document kept for history. Implemented on branch `worktree-polite-scraper`.

## Context
Aggregator access (Ceneo, Allegro API) isn't available yet. Until it is, the user wants to paste a product URL from a
supported store and have its price tracked over time, so the app can run locally with real prices.
Stores get added one at a time. The first four: **x-kom, MediaMarkt, Media Expert, Allegro**.

## What "polite" means here
- **Honest identity.** One `User-Agent` naming the bot and how to reach its owner (`Scraper:UserAgent`). No browser
  impersonation, no rotating agents or proxies, no headless browsers, and no attempt to solve or bypass bot challenges.
- **robots.txt is obeyed.** It is fetched per host, cached for 24 h and matched on our product token (longest rule wins,
  `*` and `$` supported). A `Crawl-delay` stretches the spacing between requests. If robots.txt can't be read (5xx,
  network error, challenge), the host is treated as off limits until a later run.
- **One request at a time per host**, at least `MinDelaySeconds` plus random jitter apart. The spacing is reserved in the
  database (`ScrapeHosts.NextRequestAt`), so several collector processes still share one budget per host.
- **Only pages users asked for.** No crawling, no following links, no sitemaps. Each URL is rechecked about every
  `RecheckHours` (12 h by default), and failures back off exponentially (up to 7 days).
- **Back off when told to.** `429`/`503` honor `Retry-After` (1 h if absent). A captcha or challenge page blocks the
  whole host for 24 h and shows the store as "blocked" in the UI.
- **Small footprint.** Responses are capped at 4 MB, compression is on, and each run fetches at most
  `MaxPerHostPerRun` pages per host.

## Probe results (2026-10-29, one request each, honest User-Agent)
| Store | robots.txt for product pages | Product page | Data |
|---|---|---|---|
| x-kom.pl | allowed | 200 | JSON-LD `Product` with `offers`, `mpn`, `sku`; OpenGraph `product:price:amount` |
| mediamarkt.pl | allowed | 200 | JSON-LD `BuyAction.object` → `Product` with `gtin13`, `offers[]` (marketplace seller named) |
| mediaexpert.pl | allowed (`/product/` is disallowed, the SEO paths aren't) | 403 + captcha | — |
| allegro.pl | allowed (`/offer/` disallowed; `/produkt/`, `/oferta/` aren't) | 403 DataDome captcha | — |

So x-kom and MediaMarkt work today. Media Expert and Allegro are registered, and their URLs can be tracked, but each
check ends as **Blocked** and the host backs off. Legitimate ways to get them later: Allegro's REST API for a verified
app, the Ceneo partner API, or a user-assisted capture (a bookmarklet that sends the page the user is already looking
at). These are listed as follow-ups, not built.

## Design
- **Store adapter** (`Infrastructure/Scraping/Stores`): key, name, market, hosts, URL canonicalization (drops tracking
  parameters such as `utm_*` and `ceneo_*`; Allegro keeps `offerId`), and page parsing. The default parser reads
  schema.org JSON-LD anywhere in the page (including `@graph` and nested `BuyAction.object`) and falls back to OpenGraph
  price meta tags. A store only overrides what differs. **Adding a store** = one adapter class plus a fixture test.
- **Data**:
  - `TrackedUrl`: canonical URL (unique by hash), store, category, optional "same item as" product, status, last
    error, next check and failure count. It links to the `Offer` it feeds once the first check succeeds.
  - `TrackedUrlSubscription`: user ↔ tracked URL. The same URL tracked by two users is fetched once. URLs with no
    subscribers aren't fetched.
  - `ScrapeHost`: per-host spacing reservation, cached robots.txt and block state.
- **Prices go into the existing tables.** A successful check gets or creates the `Shop` (store domain) and the
  `Product` (merged by EAN, then MPN within the category, or the product the user picked) and an `Offer` with
  `SourceKey = "scraper"`. It then appends a `PriceObservation` and folds the price into `DailyPrice`. Lists, charts,
  discounts and requirement matching work unchanged: pin the scraped product in a list requirement.
- **Runs**: `Ashopper.Collector scrape [--watch] [--interval 60]` checks due URLs, records a `CollectorRun` (so ETags
  change) and rebuilds requirement matches. Docker Compose runs it with `--watch` locally.
- **API**: `GET /api/stores`, `GET/POST /api/tracked`, `POST /api/tracked/{id}/check` (queues a check; at most once per
  10 min), `DELETE /api/tracked/{id}` (unsubscribe).
- **UI**: a "Tracked links" page to paste a URL, pick a category (optionally "same item as" an already tracked
  product), and see status, current price and a small price history per link.

## Out of scope / follow-ups
- Allegro and Media Expert through legitimate channels (see above).
- An Azure job for `scrape` (the cron collector job runs `collect` only).
- Extracting category attributes from pages (scraped products only carry name, brand, EAN/MPN, so they're meant for
  pinning, not spec matching).

## Addendum (2026-10-29): prices from the user's own browser
Requested: "on manual refresh, bot challenges may be presented to the user somehow".

Handing a captcha the user solved back to the server, so the scraper could carry on, would be getting around the
store's bot check, so it isn't done. Instead the **user** visits the page and shares what they see:
- On a link whose status is *blocked*, the refresh button opens the product page in a new tab, and the card explains
  the next step.
- The user gets past any captcha as a normal visitor, then clicks the **"Send to ashopper" bookmarklet** (installed
  from the Tracked links page).
- The bookmarklet collects only the page's structured data (JSON-LD without reviews, `og:`/`product:` meta tags,
  `itemprop` price/currency/availability). It opens `/capture#<data>` in the app. A URL fragment works from an https
  store to an http LAN app and stays out of server logs.
- `/capture` posts it to `POST /api/tracked/capture`. That endpoint parses it with the same store adapter and records
  it through `ScrapeRecorder` with `LastSuccessVia = "browser"`, then adds a `CollectorRun` so cached list prices
  refresh. The page must already be tracked by that user. Nothing on the server contacts the store.
