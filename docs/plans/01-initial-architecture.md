# ashopper — initial plan (approved 2026-10-28)

> Working document kept for history. Implemented on branch `scaffold`. Two deviations, both recorded below: the `IPriceSource` interface has a single `DiscoverAsync` method, and the UI was later restyled (see `02-ui-design-language.md`).

## Context
A learning project that's also useful: track prices over time for **loosely specified items**, such as "any 2x16GB DDR5 CL30 kit" or "any AM5 motherboard with WiFi". It should show what a set of items (a PC build) costs over time, so you can tell when it's a good moment to buy. The same model should later work for furnishing a flat ("any no-frost fridge ≥ 300 L"). Users can limit which shops count, for example the ones where they have a discount.

Decisions made with the user:
- **Stack:** .NET 10 + React/TypeScript.
- **Cloud:** Azure, staying within the free tier.
- **Market:** Poland first, Ukraine later. Adding a country must be data plus one plugin, not a refactor.
- **Prices:** from aggregator APIs.
- **Auth:** email/password from the start. External login comes later.
- **Hosting:** "make the API scalable and put the UI separately + serve it through a static file cache". This means a separate deployable, not separate hosting: one origin, with nginx in front.
- **Tokens:** JWT + refresh token.

## Data-source findings (affect the order of work)
- **Ceneo (PL):** the Partner Program API (REST/OData, OAuth2 client credentials) is available only to members of the Ceneo affiliate program. It is the most legitimate PL source; apply first.
- **Allegro (PL):** `GET /offers/listing` has required verifying the app with Allegro since 2021.
- **Hotline.ua / ek.ua (UA):** no public API was found. Only third-party scrapers or a direct partnership.
- So: a **pluggable `IPriceSource` per market** plus a **Fake source** (realistic random-walk history), so everything works before any API key arrives.

## Architecture
```
Browser ──> ui  (Container App, external ingress, nginx image: static SPA + reverse proxy)
              ├─ /assets/*, /index.html   served from the image, cache headers
              └─ /api/*, /auth/*  ──proxy──> api (Container App, INTERNAL ingress only, 0..N replicas)
                                               │ EF Core
Ashopper.Collector (Container Apps Job, cron) ─┴──> Azure SQL Database (free offer)
   └─ IPriceSource plugins: Fake, Ceneo, Allegro, (later) Hotline…
```
- **UI:** its own nginx image and deploy workflow.
  - Hashed `assets/*` get `immutable`, `index.html` gets `no-cache`, and other paths fall back to `index.html` (SPA routes).
  - `/api` and `/auth` are proxied to the internal API app, with `X-Forwarded-*` headers.
- **API:** stateless, internal ingress only, HTTP scale rule.
  - Auth uses JWT access tokens (HS256, `kid`, 15 min, in memory only on the client).
  - Refresh tokens are rotating, live in an HttpOnly SameSite=Strict cookie scoped to /auth, and are stored as SHA-256 hashes. Reusing a rotated token revokes its whole family.
  - Data-protection keys are stored in the DB.
- **API caching:**
  - Catalog endpoints: Cache-Control + ETag + output cache.
  - History: `private, no-cache` + an ETag built from list version, discounts, range and last collector run.
- **Collector:** a run-once console app (`collect`, `migrate`) running as a Container Apps Job on a cron. Work is split per source × market × category behind `IWorkDispatcher`, so it can later fan out through a queue.
- **Database:** Azure SQL Database free offer, with SQL Server 2025 locally. Attributes are stored as JSON columns and matching runs in C#.
- **Hosting:** a single Container Apps environment (free grant, scales to zero). Images go to GHCR, infrastructure is Bicep, and deploys run on GitHub Actions with OIDC.

## Domain model
- **Market:** code, currency, locale.
- **Category:** a tree with `AttributeDefinition`s (key, type Number/Enum/Bool/Text, unit, allowed values).
- **Product:** canonical attributes, plus EAN/MPN for merging duplicates.
- **ProductSourceRef:** links a product to its id in a source.
- **Shop:** belongs to a market.
- **Offer:** product × shop × source.
- **PriceObservation:** append-only price readings.
- **DailyPrice:** the lowest price per offer per day.
- **ShoppingList:** owner, market, allowed shops.
- **Requirement:** category, quantity, constraints (eq/neq/gte/lte/between/in/contains), or a pinned product.
- **RequirementMatch:** precomputed requirement → product links.
- **UserShopDiscount:** a user's percentage discount at a shop.
- **Pure logic:** `SpecMatcher` and `PriceHistoryCalculator` (cheapest in-stock offer per requirement per day, discounts, quantity, carry-forward, incomplete days).

## Price sources
- `IPriceSource { Key; Markets; DiscoverAsync(...) }`. The plan also had `FetchOffersAsync`; implementation kept just `DiscoverAsync` plus an optional `IHistoricalPriceSource`.
- Each source maps its own categories and spec names to the canonical slugs and keys.
- `CatalogIngestor` merges products by EAN, then MPN, then brand+model.
- HttpClient goes through the standard resilience handler. Secrets come from environment variables.

## API
- **Auth:** `/auth/register|login|refresh|logout|confirm-email|forgot-password|reset-password|me`, rate limited.
- **Catalog:** `/api/markets`, `/api/categories`, `/api/shops?market=`, `POST /api/products/search`.
- **Lists:** CRUD `/api/lists` and `/api/lists/{id}/requirements`, plus `/history` and `/current`.
- **Discounts:** CRUD `/api/me/shop-discounts`.

## Frontend
- **Stack:** Vite + React + TS, React Router, TanStack Query, typed client generated from OpenAPI, Recharts, i18n (pl/en).
- **Pages:** login/register, lists, list detail (chart, items, shop filter), requirement editor with live match preview, discounts settings.

## Milestones
1. Foundation
2. Core domain
3. Fake data path
4. API + auth (with integration tests)
5. SPA
6. Azure (Bicep + deploy workflows)
7. Real sources (Ceneo, then Allegro)
8. Later: UA source, price alerts, queue fan-out, external login

## Verification
- `dotnet test` and `npm test`.
- `docker compose up`, then collect with `--backfill-days`.
- Manual flow in the UI.
- `curl -I` checks for the caching headers.
- After deploying: confirm the API has no public address, and trigger the collector job.
