# Bulk prices via referral (affiliate) programs (research, 2026-10-28)

Question: which referral/affiliate services can give ashopper prices **in bulk**, for Poland now and Ukraine later?

## How affiliate data works (and why it fits)
Affiliate networks give publishers (us) each advertising shop's **product feed**: the whole catalogue with price, availability, URL, image, often EAN and category. Feeds are typically XML/CSV (Google Merchant format, or YML in UA/RU-origin networks) and are refreshed about daily. That is exactly "bulk prices per shop". Network terms usually allow price comparison as a use case, but each program's terms must be checked, especially about **storing price history**.

Limits:
- You only get shops that joined that network **and** accepted you as a publisher. Each program is approved separately.
- Specs are usually not structured. Expect a product name plus, at best, `<param>` tags (YML) or `product_detail` (Google format). Attribute mapping and EAN matching are the real work.
- Approval usually needs a live site with some traffic and a real description of how it promotes the offers.

## Poland
| Service | What you get | Notes / verified? |
|---|---|---|
| **Ceneo Partner Program** | REST/OData **aggregated across all shops listed on Ceneo** (products, offers, prices) | Best fit: it's already an aggregator. Access only for Ceneo affiliate members, and some categories are restricted. *(already in price-sources.md; unofficial source)* |
| **webePartners** (PL network) | Links, widgets and **XML product feeds** of member shops | Verified: XML feeds are offered. Which shops (x-kom, morele, …) are members is **not verified**; check the advertiser list after sign-up. |
| **Convertiser** (PL network) | Product feeds of member shops | **Not verified** in this research. |
| **Awin** | Per-advertiser product feeds and a Product (Enhanced/Google-format) API, up to ~2M products per feed | API verified. Polish advertisers / `pl_PL` locale **not verified**. |
| **Tradedoubler** | Products API (REST): advertisers push catalogues, and approved publishers read them | API verified; Polish advertisers not verified. |
| **Admitad** | Advertiser feeds (Google Merchant / CSV, historically YML), per GEO/currency | Verified feeds exist; check the PL advertisers. |
| **Amazon Associates (amazon.pl)** | Product/price lookups via API | PA-API 5 is **being retired in 2026** (Apr 30 or May 15, sources disagree) in favour of the **Creators API**. amazon.pl support not confirmed. Rate-limited, lookup-style, not a full feed. |
| **Allegro Polecam** | Referral links only | No product feed found. Bulk Allegro data needs the verified-app REST API (`/offers/listing`) instead. |

## Ukraine
| Service | What you get | Notes |
|---|---|---|
| **Admitad** | Product feeds (CSV/Google, YML) from member shops | Reported as the most common affiliate network in UA (third-party estimate). Best first stop for UA. |
| **Rozetka** | — | Only *seller* feeds (shops pushing to Rozetka) were found, not an affiliate feed for publishers. Re-check Rozetka's own partner program. |
| **Hotline.ua / ek.ua** | — | No public API or affiliate feed (see price-sources.md). |

## Recommendation
1. **Apply to Ceneo's affiliate program** (PL). One integration covers most Polish shops, already aggregated.
2. **Join webePartners and Admitad** (PL + UA), and apply to the electronics and home-appliance shops there. Each approved shop feed becomes bulk daily prices.
3. Awin and Tradedoubler are worth a look once you know which PL shops they carry. Skip Amazon PA-API (being retired) unless the Creators API proves useful.

## Access for a proof of concept (checked 2026-10-28)
Short version: you can sign up to the networks as a private person with a small site, but **feeds only come from shops whose programs approve you**. So a POC needs a public site to show them. Details:

| Service | Can a hobby/POC get in? | Gate |
|---|---|---|
| **webePartners** | Likely yes. They say almost anyone with a site or social channel can be a publisher, with **no minimum traffic**, and beginners are accepted. | Whether a private person without a registered business (*działalność*) can get **paid** isn't documented. That doesn't matter for a POC with no earnings. Each shop's program still approves you. |
| **Admitad** | Likely yes. Open self-registration (form or social login) and email confirmation. | Publisher applications are reviewed. Each advertiser's program allows, forbids or manually moderates your traffic source. |
| **Awin** | Yes, with a small **refundable deposit** (£1–£5 or equivalent, by card). | Application lists the sites you'll promote on. Per-advertiser approval follows. |
| **Tradedoubler** | Not checked. | Account plus per-program approval. |
| **Ceneo** | **Unclear for data.** The public affiliate program is **CPC (0.20 PLN per click to Ceneo.pl, 90-day cookie)**, meant for sending traffic *to* Ceneo. Whether that grants the product/offer **Partner API** isn't documented publicly. | Ask Ceneo's partner team directly: "Can a price-history comparison project use the Partner API?" |
| **Amazon (PA-API / Creators API)** | Associates usually need qualifying sales before API access. Not worth it for a POC (API being retired in 2026). | — |

What a POC realistically needs:
1. **A public site to name in applications.** Deploying ashopper itself on the free Azure tier counts: a price-comparison or price-history site that links to shops is a standard, accepted affiliate traffic source. Add a short "about" page and outbound links (offer URLs with the network's tracking parameters).
2. **Expect partial approval.** Some shops will say no to a brand-new site. Start with the network-level sign-up and apply to 5–10 electronics and home-appliance shops. Even 2–3 approved feeds give real price history.
3. **Keep the fake source on** until feeds arrive. Nothing else in the app changes.

Shops' own XML exports for comparison engines (Ceneo/Skąpiec format) are sometimes publicly reachable. They are made for those engines, not for third parties, so treat them as off-limits unless the shop agrees.

### Sources (access)
- Ceneo affiliate terms (aggregator listing): https://affi.io/m/ceneo
- webePartners publisher eligibility: https://webepartners.pl/?p=61393 · https://webepartners.pl/en
- Admitad publisher sign-up: https://admitad.useresponse.com/knowledge-base/article/how-to-sign-up-for-admitad-as-a-publisher_6 · traffic source rules: https://support.mitgo.com/hc/en-us/articles/4405920514193
- Awin deposit and application: https://success.awin.com/articles/en_US/Knowledge/Why-do-I-have-to-pay-the-sign-up-fee · https://www.awin.com/gb/compliance-and-regulations/application-process-and-joining-fee

## Fit with ashopper
- **One source per network:** each network is one `IPriceSource` (`webepartners`, `admitad`, `ceneo`, …). A feed source needs only `DiscoverAsync`: download the shop feeds, filter by category, map to canonical keys.
- **Merging:** EAN in feeds makes `CatalogIngestor`'s merge reliable across networks. Feeds without EAN fall back to brand+model.
- **Shop matching:** the shop is identified by its domain, so the same shop seen via two networks still lands on one `Shop`.
- **Schedule:** feeds are big but daily, so run the collector job once a day for feeds and keep the 6-hour schedule for API sources.

## Sources
- Admitad product feeds: https://support.mitgo.com/hc/en-us/articles/4405920538897 · https://admitad.useresponse.com/knowledge-base/article/product-feed-what-it-is-and-how-to-upload-it-to-affiliate-program_3
- Admitad in Ukraine (third-party estimate): https://www.wmtips.com/technologies/affiliate-marketing/admitad/
- Amazon PA-API deprecation: https://webservices.amazon.com/paapi5/documentation/ · https://webservices.amazon.co.jp/paapi5/documentation/common-request-parameters.html
- webePartners: https://webepartners.pl/en · https://webepartners.pl/?p=47929
- Awin product feeds: https://developer.awin.com/docs/product-feed-guide-introduction-1 · https://help.awin.com/apidocs/retail-advertiser-productapidocumentation
- Tradedoubler Products API: https://dev.tradedoubler.com/products/advertiser/
- Allegro Polecam: https://spolecznosc.allegro.pl/t5/dyskusje-kupuj%C4%85cych/zapytaj-eksperta-allegro-polecam/m-p/208350
- Rozetka seller feeds: https://garazd.biz/blog/odoo-integration-5/odoo-rozetka-marketplace-integration-xml-product-feeds-39
