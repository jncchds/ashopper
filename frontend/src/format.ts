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

/** Short money for axes: 1,2 tys. zł / 1.2K PLN. */
export function formatMoneyCompact(value: number, currency: string, locale?: string): string {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 }).format(value);
}

export function formatDay(isoDay: string, locale?: string, opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' }): string {
  return new Date(`${isoDay}T00:00:00Z`).toLocaleDateString(locale, { ...opts, timeZone: 'UTC' });
}

export function isoDay(date: Date): string {
  return date.toISOString().slice(0, 10);
}
