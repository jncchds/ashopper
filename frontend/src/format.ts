const formatters = new Map<string, Intl.NumberFormat>();

/** Money in the market's currency and locale, e.g. 1 234,99 zł. */
export function formatMoney(value: number, currency: string, locale?: string): string {
  const key = `${locale ?? ''}|${currency}`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.NumberFormat(locale, { style: 'currency', currency, maximumFractionDigits: 2 });
    formatters.set(key, f);
  }
  return f.format(value);
}

/**
 * Money for chart axes: whole units (PLN 5,100), compact only for big values (PLN 120K).
 * Compact notation with one decimal collapses close ticks into duplicates (5,350 and 5,400 → 5.4K).
 */
export function formatMoneyAxis(value: number, currency: string, locale?: string): string {
  const big = Math.abs(value) >= 100_000;
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency,
    notation: big ? 'compact' : 'standard',
    maximumFractionDigits: big ? 1 : 0,
  }).format(value);
}

export function formatDay(isoDay: string, locale?: string, opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' }): string {
  return new Date(`${isoDay}T00:00:00Z`).toLocaleDateString(locale, { ...opts, timeZone: 'UTC' });
}

export function isoDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}
