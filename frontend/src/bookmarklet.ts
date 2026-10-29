/**
 * "Send to ashopper": a bookmark the user clicks on a store's product page in their own browser. For stores that
 * answer bots with a captcha, the user is the visitor: they open the page (solving any captcha themselves), and this
 * sends what the page shows to the app. It reads only the page's structured data (JSON-LD, price meta tags and
 * microdata), which the server parses with the same store adapter the scraper uses.
 *
 * Data travels in the URL fragment of a new app window (/capture#...), so it works from https store pages to an
 * http app on the LAN (a top-level navigation isn't blocked as mixed content) and never reaches the app's server logs.
 */

/** Runs on the store page. Must be self-contained: it is serialized with toString(). */
export function sendPage(origin: string, win: Window = window): void {
  const doc = win.document;
  const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
  const parts: string[] = [];
  // Reviews, descriptions and images make JSON-LD big without saying anything about the price.
  const drop = (k: string, v: unknown) => (k === 'review' || k === 'reviews' || k === 'description' || k === 'image' ? undefined : v);
  doc.querySelectorAll('script[type="application/ld+json"]').forEach((s) => {
    let text = s.textContent || '';
    try {
      text = JSON.stringify(JSON.parse(text), drop);
    } catch {
      /* keep the raw text; the server skips blocks it can't parse */
    }
    parts.push('<script type="application/ld+json">' + text + '</' + 'script>');
  });
  doc.querySelectorAll('meta[property],meta[name]').forEach((m) => {
    const key = m.getAttribute('property') || m.getAttribute('name') || '';
    if (/^(og:|product:)/.test(key)) parts.push('<meta property="' + esc(key) + '" content="' + esc(m.getAttribute('content') || '') + '">');
  });
  doc.querySelectorAll('[itemprop="price"],[itemprop="priceCurrency"],[itemprop="availability"]').forEach((el) => {
    const value = el.getAttribute('content') || el.getAttribute('href') || el.textContent || '';
    parts.push('<meta itemprop="' + el.getAttribute('itemprop') + '" content="' + esc(value.trim()) + '">');
  });
  const html = parts.join('\n').slice(0, 300_000);
  const payload = encodeURIComponent(JSON.stringify({ url: win.location.href, html }));
  win.open(origin + '/capture#' + payload, '_blank');
}

/**
 * The javascript: URL for a bookmark that sends the current page to the app at `origin`. Percent-encoded, because
 * browsers strip raw newlines from URLs, which would break line comments and template literals in the code.
 */
export function buildBookmarklet(origin: string): string {
  return 'javascript:' + encodeURIComponent(`(${sendPage.toString()})(${JSON.stringify(origin)});void 0`);
}

export interface CapturePayload {
  url: string;
  html: string;
}

/** Reads what the bookmarklet put in /capture#..., or null when it isn't there or is malformed. */
export function readCapture(hash: string): CapturePayload | null {
  if (hash.length < 2) return null;
  try {
    const data = JSON.parse(decodeURIComponent(hash.slice(1))) as Partial<CapturePayload>;
    return typeof data.url === 'string' && typeof data.html === 'string' ? { url: data.url, html: data.html } : null;
  } catch {
    return null;
  }
}
