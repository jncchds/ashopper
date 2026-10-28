import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '../api/client';
import { useI18n } from '../i18n';
import { useMarkets, useShops } from '../queries';

export function SettingsPage() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const markets = useMarkets();
  const [market, setMarket] = useState('PL');
  const shops = useShops(market);
  const discounts = useQuery({ queryKey: ['discounts'], queryFn: () => unwrap(api.GET('/api/me/shop-discounts')) });

  const save = useMutation({
    mutationFn: async ({ shopId, percent }: { shopId: string; percent: number }) => {
      if (percent > 0) await unwrap(api.PUT('/api/me/shop-discounts/{shopId}', { params: { path: { shopId } }, body: { percent } }));
      else await unwrap(api.DELETE('/api/me/shop-discounts/{shopId}', { params: { path: { shopId } } }));
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['discounts'] });
      void queryClient.invalidateQueries({ queryKey: ['list'] }); // prices change everywhere
    },
  });

  const percentOf = (shopId: string) => discounts.data?.find((d) => d.shopId === shopId)?.percent ?? 0;

  return (
    <main className="page">
      <h1>{t('settings')}</h1>
      <section className="card">
        <div className="section-head">
          <h2>{t('discounts')}</h2>
          <select value={market} onChange={(e) => setMarket(e.target.value)} aria-label={t('market')}>
            {markets.data?.map((m) => (
              <option key={m.code} value={m.code}>{m.name}</option>
            ))}
          </select>
        </div>
        <p className="muted small">{t('discountsHint')}</p>
        <table>
          <tbody>
            {shops.data?.map((s) => (
              <tr key={s.id}>
                <td>
                  {s.name} <span className="muted small">{s.domain}</span>
                </td>
                <td className="num">
                  <input
                    key={`${s.id}-${percentOf(s.id)}`}
                    className="percent"
                    type="number"
                    min={0}
                    max={90}
                    step={0.5}
                    defaultValue={percentOf(s.id) || ''}
                    placeholder="0"
                    aria-label={`${s.name} %`}
                    onBlur={(e) => {
                      const percent = Number(e.target.value) || 0;
                      if (percent !== percentOf(s.id)) save.mutate({ shopId: s.id, percent });
                    }}
                  />{' '}
                  %
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {save.error && <p className="error">{save.error.message}</p>}
      </section>
    </main>
  );
}
