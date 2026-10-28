import { useMemo, useState } from 'react';
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import type { Schemas } from '../api/client';
import { formatDay, formatMoney, formatMoneyAxis } from '../format';
import { useI18n } from '../i18n';
import { niceAxis } from './chartAxis';

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

/** One series (the list total) → no legend; the section title names it. Crosshair tooltip breaks it down. */
export function PriceChart({ history, requirements, shops }: Props) {
  const { t, locale } = useI18n();
  const [showTable, setShowTable] = useState(false);
  const currency = history.currency;
  const money = (v: number) => formatMoney(v, currency, locale);

  const data = useMemo<Point[]>(
    () => history.points.map((p) => ({ day: p.day, total: p.complete ? p.total : null, items: p.items })),
    [history],
  );
  const complete = data.filter((p): p is Point & { total: number } => p.total !== null);
  if (complete.length === 0) return <p className="py-8 text-center muted">{t('list.noOffer')}</p>;

  const low = complete.reduce((a, b) => (b.total < a.total ? b : a));
  const high = complete.reduce((a, b) => (b.total > a.total ? b : a));
  const axis = niceAxis(low.total, high.total);
  const titleOf = (id: string) => requirements.find((r) => r.id === id)?.title ?? '—';
  const shopOf = (id?: string | null) => shops.find((s) => s.id === id)?.name ?? '';

  return (
    <figure className="m-0 space-y-3">
      <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm muted">
        <span>
          {t('list.lowest')} <strong className="text-fg tabular-nums">{money(low.total)}</strong> · {formatDay(low.day, locale)}
        </span>
        <span>
          {t('list.highest')} <strong className="text-fg tabular-nums">{money(high.total)}</strong> · {formatDay(high.day, locale)}
        </span>
      </div>

      <div className="h-[280px] w-full" role="img" aria-label={`${t('list.priceHistory')}: ${t('list.lowest')} ${money(low.total)}`}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke="var(--border)" strokeOpacity={0.7} />
            <XAxis
              dataKey="day"
              tickFormatter={(d: string) => formatDay(d, locale)}
              tick={{ fill: 'var(--text-muted)', fontSize: 12 }}
              axisLine={{ stroke: 'var(--border)' }}
              tickLine={false}
              minTickGap={32}
            />
            <YAxis
              domain={[axis.min, axis.max]}
              ticks={axis.ticks}
              tickFormatter={(v: number) => formatMoneyAxis(v, currency, locale)}
              tick={{ fill: 'var(--text-muted)', fontSize: 12 }}
              axisLine={false}
              tickLine={false}
              width={84}
            />
            <ReferenceLine y={low.total} stroke="var(--text-muted)" strokeDasharray="3 4" />
            <Tooltip
              cursor={{ stroke: 'var(--text-muted)', strokeWidth: 1, strokeOpacity: 0.5 }}
              content={({ active, payload }) => {
                const p = active ? (payload?.[0]?.payload as Point | undefined) : undefined;
                if (!p) return null;
                return (
                  <div className="card min-w-56 p-3 text-xs shadow-lg">
                    <div className="muted">{formatDay(p.day, locale, { day: 'numeric', month: 'long', year: 'numeric' })}</div>
                    <div className="mt-0.5 mb-2 text-base font-bold tabular-nums">{p.total === null ? t('list.incomplete') : money(p.total)}</div>
                    <div className="space-y-1">
                      {p.items.map((i) => (
                        <div key={i.requirementId} className="grid grid-cols-[1fr_auto] gap-x-3">
                          <span className="truncate">{titleOf(i.requirementId)}</span>
                          <span className="num">{i.linePrice == null ? '—' : money(i.linePrice)}</span>
                          <span className="col-span-2 text-[0.7rem] muted">{shopOf(i.shopId)}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                );
              }}
            />
            <Line
              type="monotone"
              dataKey="total"
              stroke="var(--accent)"
              strokeWidth={2}
              dot={false}
              activeDot={{ r: 5, fill: 'var(--accent)', stroke: 'var(--surface)', strokeWidth: 2 }}
              connectNulls={false}
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>

      <button type="button" className="btn-ghost px-2 py-1 text-xs" onClick={() => setShowTable((s) => !s)}>
        {showTable ? t('common.hideTable') : t('common.showTable')}
      </button>
      {showTable && (
        <div className="max-h-72 overflow-y-auto rounded-md border border-line">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-surface">
              <tr className="border-b border-line">
                <th className="px-3 py-2 text-left caps">{t('list.day')}</th>
                <th className="px-3 py-2 text-right caps">{t('list.total')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {[...data].reverse().map((p) => (
                <tr key={p.day}>
                  <td className="px-3 py-1.5">{formatDay(p.day, locale, { day: 'numeric', month: 'short', year: 'numeric' })}</td>
                  <td className="num px-3 py-1.5">{p.total === null ? '—' : money(p.total)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </figure>
  );
}
