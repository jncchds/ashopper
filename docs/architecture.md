# Architecture

```
Browser ──> ui  (Container App, public ingress, nginx: static SPA + reverse proxy)
              ├─ /assets/*                cache-control: immutable (hashed file names)
              ├─ /*                       index.html, no-cache (SPA routes)
              └─ /api/*, /auth/*  ──>  api (Container App, INTERNAL ingress, 0..N replicas)
                                          │ EF Core
collector (Container Apps Job, cron) ─────┴──> Azure SQL Database (free offer, auto-pause)
migrate   (Container Apps Job, per deploy) ┘
```

## Projects

| Project | Responsibility |
|---|---|
| `Ashopper.Domain` | Pure model and rules, no I/O: markets, categories with attribute schemas, products, offers, prices, lists, requirements. `SpecMatcher` (does a product satisfy constraints?) and `PriceHistoryCalculator` (cheapest offer per requirement per day, discounts, carry-forward). |
| `Ashopper.Infrastructure` | EF Core `AppDbContext` and migrations, reference data seeding, price sources, collector pipeline, read queries. |
| `Ashopper.Api` | HTTP: auth, catalog, lists, discounts. Caching headers and rate limiting. |
| `Ashopper.Collector` | Console host: `migrate`, `collect` and `scrape`. |
| `frontend/` | React SPA plus its nginx image. |

## Core idea: requirements, not products

- A **category** declares its attributes, for example RAM has `memory_type` (enum), `capacity_gb` (number), `cas_latency` (number), `rgb` (bool). Products store attribute values under those keys.
- A **requirement** is a category plus constraints (`eq`, `neq`, `gte`, `lte`, `between`, `in`, `contains`). It can also pin one product.
- **RequirementMatch** stores which products satisfy each requirement. It is recomputed when a requirement changes and after every collector run, so price queries are plain joins.
- **DailyPrice** holds each offer's lowest price per day, maintained by the collector. Charts read only this table; raw `PriceObservation`s are kept for audit and re-aggregation.
- History for a list: for each day and requirement, take the cheapest **in-stock** offer from the allowed shops after the user's shop discount, multiply by quantity, and sum. Days with a missing item are marked incomplete and drawn as gaps. A reading carries forward at most 2 days, so one failed collector run doesn't punch a hole in the chart. A newer out-of-stock reading always wins over an older in-stock one.

## Markets and currencies

`Market` (code, currency, locale) is data. Shops belong to a market, a list belongs to a market, and prices are only compared within the list's market and currency. To add Ukraine:
1. Add it to `ReferenceData.Markets`. It is already there.
2. Register a price source whose `Markets` contains `"UA"`.

The UI formats money with the list's currency and the user's language.

## Adding a price source

1. Implement `IPriceSource` in `Infrastructure/Sources/<Name>/`:
   - `Key` is stored on offers. `Markets` and `CategorySlugs` say what the source covers.
   - `DiscoverAsync(market, category)` yields `SourceProduct`s with their `SourceOffer`s.
   - **Map the source's categories and spec names to the canonical slugs and attribute keys inside the source.** This is where most real-world work lives, for example "Pojemność: 32 GB" becomes `capacity_gb = "32"`. Nothing outside the source should ever see source-specific formats.
   - Provide EAN/MPN when the source has them. `CatalogIngestor` merges products across sources by EAN, then MPN, then normalized brand+model.
2. Register it in `DependencyInjection.AddAshopperCollector` behind `Sources:<Name>:Enabled`. Use `AddHttpClient(...).AddStandardResilienceHandler()` for retries, timeouts and circuit breaking.
3. Put credentials in configuration: env vars locally, Container Apps secrets on Azure.
4. Optionally implement `IHistoricalPriceSource` if the source can return past prices. New offers are then backfilled with `--backfill-days`.

## Polite scraper (tracked links)

Until aggregator access arrives, users can paste a product link from a supported store and its price is tracked.
Details and the reasoning are in `docs/plans/03-polite-scraper.md`.

- **Stores** are `StoreAdapter`s in `Infrastructure/Scraping/Stores`: domain, market, URL canonicalization (tracking
  parameters removed) and page parsing. The default parser reads schema.org JSON-LD anywhere in the page and falls back
  to OpenGraph price tags. Today: x-kom and MediaMarkt work. Media Expert and Allegro answer bots with a captcha, so
  their checks end as *blocked*.
- **Adding a store:** subclass `StoreAdapter` (usually only key, name, market and domain), add it to
  `StoreRegistry.Default`, save a trimmed page under `tests/.../Unit/Scraping/Fixtures/` and add a parsing test. First
  check the store's robots.txt and that it serves product pages to an honestly identified client.
- **Politeness rules** live in `PoliteFetcher`, the only code that talks to shops:
  - an honest `User-Agent` (`Scraper:UserAgent`, set a contact);
  - robots.txt is obeyed, including `Crawl-delay`;
  - requests to one host are one at a time and spaced (≥ 20 s + jitter), with the slot reserved in `ScrapeHosts`, so
    parallel processes share it;
  - no retries;
  - `429`/`503` and challenge pages put the whole host on a back-off;
  - it never tries to get around a bot challenge.
- **Data:** `TrackedUrl` (one row per canonical URL, shared by users through `TrackedUrlSubscription`) feeds an `Offer`
  with `SourceKey = "scraper"`. Prices go to `PriceObservation`/`DailyPrice` like any other source, so a scraped product
  can be pinned in a list requirement. "Same item as" attaches a second store's link to an existing product.
- **Stores that block bots:** the user can send a price from their own browser instead. On a blocked link, the refresh
  button opens the page; the "Send to ashopper" bookmarklet (`frontend/src/bookmarklet.ts`) sends the page's structured
  data to `/capture` → `POST /api/tracked/capture`, which parses it with the same adapter. The scraper never gets around
  a challenge; a person visiting the page does.
- **Running:** `Ashopper.Collector scrape` does one pass over due links; `--watch` repeats every minute (Docker Compose
  runs the `scraper` service this way). Each pass records a `CollectorRun` and rebuilds matches. Pages are rechecked
  about every 12 h, and failures back off exponentially up to 7 days. Not yet scheduled on Azure.

## Auth

- **Identity core:** users, PBKDF2 password hashing, lockout after 5 failures, email-confirmation and reset tokens.
- **Access token:** a JWT signed with HS256 and a `kid` header, valid for 15 min. It is validated on every replica without a database call. The SPA keeps it in memory only.
- **Refresh token:** 256 random bits in an `HttpOnly; Secure; SameSite=Strict; Path=/auth` cookie, valid for 14 days. Only its SHA-256 hash is stored.
  - It is rotated on every refresh.
  - Presenting an already-rotated token revokes the whole token family, which signals likely theft. One exception: within 30 s of rotation it answers `409`, so parallel tabs just retry instead of logging the user out.
  - Logout revokes the family, and a password reset revokes all families.
- **Same origin:** the UI's nginx proxies `/api` and `/auth`, so there is no CORS. `SameSite=Strict` covers CSRF for the cookie-authenticated `/auth/refresh`.
- **Rotating signing keys:** add a key to `Auth:Jwt:SigningKeys`, switch `ActiveKeyId`, and remove the old key after 15 minutes.
- **External login (later)** becomes another way to obtain the same JWT and refresh-token pair.

## Caching

| What | Where | How |
|---|---|---|
| JS/CSS bundles | browser, any CDN | `public, max-age=31536000, immutable`; file names are content hashes |
| `index.html` | browser | `no-cache`, so new deploys are picked up immediately |
| `/api/markets`, `/categories`, `/shops` | API output cache (10 min, per replica) + browser | `public, max-age=300` + ETag (app version + last collector run) → `304` |
| `/api/lists/{id}/history`, `/current` | browser | `private, no-cache` + ETag (list version, discounts, date range, last collector run) → `304` until something changes |

## Scaling path

Today everything fits the free tier. When it doesn't, scale in this order:

1. **API:** already stateless; Container Apps adds replicas on HTTP concurrency. The output cache is per replica; add Redis only if hit rates matter.
2. **Collector:** work is already split into `CollectorWorkItem`s (source × market × category) behind `IWorkDispatcher`. Swap `InProcessWorkDispatcher` for a queue-backed one (Azure Storage Queue) and run the job with parallel replicas. Each item uses its own DbContext.
3. **Matching:** done in C# over one category's products. Fine up to tens of thousands per category. Beyond that, translate constraints to SQL `OPENJSON` predicates or move products into a search index such as Azure AI Search, which has a free tier.
4. **History queries:** they read `DailyPrice × matches`. If that gets heavy, precompute a per-requirement daily minimum table in the collector.
5. **Data growth:** `PriceObservation` is append-only. Prune or archive it after N months; `DailyPrice` is enough for charts.

## Free-tier guardrails

- **No database health probes:** the API's liveness probe doesn't touch SQL, so the free Azure SQL database can auto-pause.
- **Free-limit behavior:** Azure SQL is set to `AutoPause` when free limits are exhausted. It pauses instead of billing.
- **Startup retries:** EF uses retry on failure, so the first request after an auto-pause waits for the resume (up to about a minute).
- **Log cap:** Log Analytics has a daily cap.
- **Scale to zero:** every Container App and job can scale to zero. The UI's `minReplicas: 0` means a cold start of a second or two on the first visit.
