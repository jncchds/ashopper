# Price sources research (2026-10-28)

Which aggregators could feed real prices, by market. Re-check before building any of these; terms and access change.

## Poland
- **Ceneo**:
  - The Partner Program API is a REST/OData API with OAuth 2.0 client credentials and Bearer tokens. It is limited to members of the Ceneo affiliate program, and some categories are restricted.
  - Separately, there is a Business API for shops and manufacturers (competitor offers, listing operations). Its commercial terms need to be confirmed with Ceneo.
  - **Next step:** apply to the affiliate program. This is the most legitimate PL source.
- **Allegro**:
  - Since March 2021, `GET /offers/listing` (public offer search) has been available only to *verified* applications.
  - Verification goes through Allegro's contact form, or through an account manager for Seller Partner Program members.
  - Registering an app requires a fully activated account with two-step login enabled.

## Ukraine
- **Hotline.ua / ek.ua (E-Katalog)**:
  - No public API or partner terms were found.
  - Only third-party scrapers exist: Apify actors, Price Control's paid service, and the `marketua` MCP project.
  - **Next step:** contact them about a partnership, or check their terms before any scraping.
- **Rozetka / Price.ua**: not yet researched.

## Sources
- https://www.itechguides.com/?p=876762 (Ceneo Partner API overview; unofficial)
- https://developer.allegro.pl/news/get-offers-listing-tylko-dla-zweryfikowanych-aplikacji-GRax4oVgrs1
- https://developer.allegro.pl/news/zmienilismy-regulamin-rest-api-v8Wdyj7mWfn
- https://apify.com/skipper_lume/hotline-ua-scraper
- https://www.pricecontrol.com.ua/en/hotline-price-parsing/
