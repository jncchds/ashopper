import { useMemo, useState } from 'react';
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import type { Schemas } from '../api/client';
import { formatDay, formatMoney, formatMoneyCompact } from '../format';
import { useI18n } from '../i18n';

interface Props {
  history: Schemas['HistoryDto'];
  requirements: Schemas['RequirementDto'][];
  shops: Schemas['ShopDto'][];
}

interface Point {
  day: string;
  /** Null on days where some item had no offer: drawn as a gap, not a misleading dip. */
  total: number | null;
  items: Schemas['HistoryItemDto'][];
}

/** One series (the list total) → no legend box; the card title names it. Crosshair tooltip breaks it down. */
export function PriceChart({ history, requirements, shops }: Props) {
  const { t, locale } = useI18n();
  const [showTable, setShowTable] = useState(false);
  const currency = history.currency;

  const data = useMemo<Point[]>(
    () => history.points.map((p) => ({ day: p.day, total: p.complete ? p.total : null, items: p.items })),
    [history],
  );
  const complete = data.filter((p): p is Point & { total: number } => p.total !== null);
  const low = complete.length ? complete.reduce((a, b) => (b.total < a.total ? b : a)) : null;
  const high = complete.length ? complete.reduce((a, b) => (b.total > a.total ? b : a)) : null;

  if (complete.length === 0) return <p className="muted">{t('noOffer')}</p>;

  const titleOf = (id: string) => requirements.find((r) => r.id === id)?.title ?? '—';
  const shopOf = (id?: string | null) => shops.find((s) => s.id === id)?.name ?? '';

  return (
    <figure className="chart">
      <div className="chart-stats">
        <span>
          {t('lowest')} <strong>{formatMoney(low!.total, currency, locale)}</strong> · {formatDay(low!.day, locale)}
        </span>
        <span>
          {t('highest')} <strong>{formatMoney(high!.total, currency, locale)}</strong> · {formatDay(high!.day, locale)}
        </span>
      </div>

      <div className="chart-plot" role="img" aria-label={`${t('priceHistory')}: ${t('lowest')} ${formatMoney(low!.total, currency, locale)}`}>
        <ResponsiveContainer width="100%" height={280}>
          <LineChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: 4 }}>
            <CartesianGrid vertical={false} stroke="var(--grid)" />
            <XAxis
              dataKey="day"
              tickFormatter={(d: string) => formatDay(d, locale)}
              tick={{ fill: 'var(--text-muted)', fontSize: 12 }}
              axisLine={{ stroke: 'var(--axis)' }}
              tickLine={false}
              minTickGap={32}
            />
            <YAxis
              domain={['auto', 'auto']}
              tickFormatter={(v: number) => formatMoneyCompact(v, currency, locale)}
              tick={{ fill: 'var(--text-muted)', fontSize: 12 }}
              axisLine={false}
              tickLine={false}
              width={72}
            />
            <ReferenceLine y={low!.total} stroke="var(--text-muted)" strokeDasharray="3 4" />
            <Tooltip
              cursor={{ stroke: 'var(--axis)', strokeWidth: 1 }}
              content={({ active, payload }) => {
                const p = active ? (payload?.[0]?.payload as Point | undefined) : undefined;
                if (!p) return null;
                return (
                  <div className="tooltip">
                    <div className="tooltip-head">{formatDay(p.day, locale, { day: 'numeric', month: 'long', year: 'numeric' })}</div>
                    <div className="tooltip-total">{p.total === null ? t('incomplete') : formatMoney(p.total, currency, locale)}</div>
                    {p.items.map((i) => (
                      <div key={i.requirementId} className="tooltip-row">
                        <span>{titleOf(i.requirementId)}</span>
                        <span>{i.linePrice == null ? '—' : formatMoney(i.linePrice, currency, locale)}</span>
                        <span className="muted">{shopOf(i.shopId)}</span>
                      </div>
                    ))}
                  </div>
                );
              }}
            />
            <Line
              type="monotone"
              dataKey="total"
              stroke="var(--series-1)"
              strokeWidth={2}
              dot={false}
              activeDot={{ r: 5, fill: 'var(--series-1)', stroke: 'var(--surface)', strokeWidth: 2 }}
              connectNulls={false}
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>

      <button type="button" className="link" onClick={() => setShowTable((s) => !s)}>
        {showTable ? t('hideTable') : t('showTable')}
      </button>
      {showTable && (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{t('day')}</th>
                <th className="num">{t('total')}</th>
              </tr>
            </thead>
            <tbody>
              {[...data].reverse().map((p) => (
                <tr key={p.day}>
                  <td>{formatDay(p.day, locale, { day: 'numeric', month: 'short', year: 'numeric' })}</td>
                  <td className="num">{p.total === null ? '—' : formatMoney(p.total, currency, locale)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </figure>
  );
}
