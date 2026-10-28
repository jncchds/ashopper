import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { PageTitle, Section, Segmented } from '../components/ui';
import { THEMES, useTheme } from '../hooks/useTheme';
import { LANGUAGES, rememberLanguage, useI18n } from '../i18n';
import { useMarkets, useShops } from '../queries';

export function SettingsPage() {
  const { t } = useI18n();
  const { i18n } = useTranslation();
  const { user } = useAuth();
  const { theme, setTheme } = useTheme();
  const queryClient = useQueryClient();
  const markets = useMarkets();
  const [market, setMarket] = useState('PL');
  const shops = useShops(market);
  const discounts = useQuery({ queryKey: ['discounts'], queryFn: () => unwrap(api.GET('/api/me/shop-discounts')) });
  const [savedShop, setSavedShop] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: async ({ shopId, percent }: { shopId: string; percent: number }) => {
      if (percent > 0) await unwrap(api.PUT('/api/me/shop-discounts/{shopId}', { params: { path: { shopId } }, body: { percent } }));
      else await unwrap(api.DELETE('/api/me/shop-discounts/{shopId}', { params: { path: { shopId } } }));
    },
    onSuccess: (_, { shopId }) => {
      setSavedShop(shopId);
      void queryClient.invalidateQueries({ queryKey: ['discounts'] });
      void queryClient.invalidateQueries({ queryKey: ['list'] }); // prices change everywhere
    },
  });

  const percentOf = (shopId: string) => discounts.data?.find((d) => d.shopId === shopId)?.percent ?? 0;

  return (
    <div className="mx-auto max-w-3xl">
      <PageTitle>{t('settings.title')}</PageTitle>
      <div className="space-y-6">
        <Section
          title={t('settings.discounts')}
          hint={t('settings.discountsHint')}
          actions={
            <select className="input w-auto py-1" value={market} onChange={(e) => setMarket(e.target.value)} aria-label={t('settings.market')}>
              {markets.data?.map((m) => (
                <option key={m.code} value={m.code}>{m.name}</option>
              ))}
            </select>
          }
        >
          <div className="divide-y divide-line rounded-md border border-line">
            {shops.data?.map((s) => (
              <div key={s.id} className="flex flex-wrap items-center gap-3 px-3 py-2.5">
                <div className="min-w-0 flex-1">
                  <div className="font-medium">{s.name}</div>
                  <div className="text-xs muted">{s.domain}</div>
                </div>
                {savedShop === s.id && !save.isPending && <span className="text-xs text-success">{t('common.saved')}</span>}
                <label className="flex items-center gap-1.5 text-sm muted">
                  <input
                    key={`${s.id}-${percentOf(s.id)}`}
                    className="input w-20 text-right"
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
                  />
                  %
                </label>
              </div>
            ))}
          </div>
          {save.error && <p className="mt-2 text-sm text-danger" role="alert">{save.error.message}</p>}
        </Section>

        <Section title={t('settings.appearance')} hint={t('settings.appearanceHint')}>
          <div className="flex flex-wrap items-end gap-6">
            <div>
              <span className="label">{t('theme.label')}</span>
              <Segmented value={theme} onChange={setTheme} options={THEMES.map((th) => ({ value: th, label: t(`theme.${th}`) }))} label={t('theme.label')} />
            </div>
            <label className="block">
              <span className="label">{t('settings.language')}</span>
              <select
                className="input w-auto"
                value={i18n.language}
                onChange={(e) => {
                  void i18n.changeLanguage(e.target.value);
                  rememberLanguage(e.target.value);
                }}
              >
                {LANGUAGES.map((l) => (
                  <option key={l.code} value={l.code}>{l.name}</option>
                ))}
              </select>
            </label>
          </div>
        </Section>

        <Section title={t('settings.account')}>
          <p className="text-sm">{user?.email}</p>
        </Section>
      </div>
    </div>
  );
}
