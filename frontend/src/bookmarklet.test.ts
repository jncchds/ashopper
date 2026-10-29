import { describe, expect, it, vi } from 'vitest';
import { buildBookmarklet, readCapture, sendPage } from './bookmarklet';

function storePage(body: string, href = 'https://www.mediaexpert.pl/agd/pralka?utm_source=x') {
  document.head.innerHTML = '';
  document.body.innerHTML = body;
  const open = vi.fn();
  const win = { document, location: { href }, open } as unknown as Window;
  return { win, open };
}

describe('bookmarklet', () => {
  it('sends JSON-LD without reviews, price meta tags and microdata to the app', () => {
    const { win, open } = storePage(`
      <script type="application/ld+json">{"@type":"Product","name":"Pralka","review":[{"text":"long"}],"offers":{"price":1899,"priceCurrency":"PLN"}}</script>
      <meta property="product:price:amount" content="1899.00">
      <meta name="description" content="ignored">
      <span itemprop="price" content="1899.00">1 899 zł</span>
      <link itemprop="availability" href="https://schema.org/InStock">`);

    sendPage('http://192.168.2.214:8090', win);

    expect(open).toHaveBeenCalledOnce();
    const target = open.mock.calls[0][0] as string;
    expect(target.startsWith('http://192.168.2.214:8090/capture#')).toBe(true);
    const payload = readCapture(target.slice(target.indexOf('#')))!;
    expect(payload.url).toBe('https://www.mediaexpert.pl/agd/pralka?utm_source=x');
    expect(payload.html).toContain('"name":"Pralka"');
    expect(payload.html).not.toContain('review');
    expect(payload.html).toContain('<meta property="product:price:amount" content="1899.00">');
    expect(payload.html).not.toContain('ignored');
    expect(payload.html).toContain('<meta itemprop="price" content="1899.00">');
    expect(payload.html).toContain('<meta itemprop="availability" content="https://schema.org/InStock">');
  });

  it('builds a javascript: URL that runs on its own', () => {
    const url = buildBookmarklet('http://localhost:8090');
    expect(url.startsWith('javascript:')).toBe(true);
    expect(url).not.toMatch(/[\s\n]/); // survives browsers stripping whitespace from URLs

    const { open } = storePage('<script type="application/ld+json">{"@type":"Product","name":"A"}</script>');
    window.open = open;
    // Same as the browser does when the bookmark is clicked.
    new Function(decodeURIComponent(url.slice('javascript:'.length)))();
    expect(open).toHaveBeenCalledOnce();
    expect(open.mock.calls[0][0]).toMatch(/^http:\/\/localhost:8090\/capture#/);
  });

  it('rejects malformed capture data', () => {
    expect(readCapture('')).toBeNull();
    expect(readCapture('#not-json')).toBeNull();
    expect(readCapture('#' + encodeURIComponent('{"url":1}'))).toBeNull();
  });
});
