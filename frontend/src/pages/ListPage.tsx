import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '../api/client';
import { PriceChart } from '../components/PriceChart';
import { RequirementEditor } from '../components/RequirementEditor';
import { formatMoney, isoDay } from '../format';
import { useI18n } from '../i18n';
import { useCategories, useCurrent, useHistory, useList, useMarkets, useShops } from '../queries';
import { describeConstraints } from '../specs';

const RANGES = [30, 90, 180] as const;

export function ListPage() {
  const { id = '' } = useParams();
  const { t, locale } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [range, setRange] = useState<(typeof RANGES)[number]>(90);
  const [editing, setEditing] = useState<Schemas['RequirementDto'] | 'new' | null>(null);
  const [today] = useState(() => new Date());

  const { from, to } = useMemo(() => {
    const start = new Date(today);
    start.setUTCDate(start.getUTCDate() - (range - 1));
    return { from: isoDay(start), to: isoDay(today) };
  }, [range, today]);

  const list = useList(id);
  const current = useCurrent(id);
  const history = useHistory(id, from, to);
  const categories = useCategories();
  const shops = useShops(list.data?.marketCode);
  const market = useMarkets().data?.find((m) => m.code === list.data?.marketCode);
  const currency = market?.currency ?? 'PLN';
  // Currency comes from the list's market; number formatting follows the UI language, everywhere.
  const moneyLocale = locale;

  /** Anything that changes prices (items, shops) invalidates the list's derived queries. */
  const refresh = (updated?: Schemas['ListDto']) => {
    if (updated) queryClient.setQueryData(['list', id], updated);
    void queryClient.invalidateQueries({ queryKey: ['list', id] });
    void queryClient.invalidateQueries({ queryKey: ['lists'] });
  };

  const updateShops = useMutation({
    mutationFn: (allowedShopIds: string[]) =>
      unwrap(api.PUT('/api/lists/{id}', { params: { path: { id } }, body: { name: list.data!.name, allowedShopIds } })),
    onSuccess: refresh,
  });

  const removeItem = useMutation({
    mutationFn: (requirementId: string) =>
      unwrap(api.DELETE('/api/lists/{id}/requirements/{requirementId}', { params: { path: { id, requirementId } } })),
    onSuccess: () => refresh(),
  });

  const removeList = useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/lists/{id}', { params: { path: { id } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['lists'] });
      navigate('/');
    },
  });

  if (list.isLoading) return <main className="page" aria-busy="true" />;
  if (!list.data) return <main className="page"><p className="error">{list.error?.message}</p></main>;

  const l = list.data;
  const best = new Map(current.data?.items.map((i) => [i.requirementId, i.best]) ?? []);
  const toggleShop = (shopId: string) => {
    const set = new Set(l.allowedShopIds);
    if (set.has(shopId)) set.delete(shopId);
    else set.add(shopId);
    updateShops.mutate([...set]);
  };

  return (
    <main className="page">
      <Link to="/" className="small muted">← {t('lists')}</Link>
      <div className="page-head">
        <h1>{l.name}</h1>
        {current.data && (
          <div className="headline">
            <span className="muted small">{t('totalNow')}</span>
            <strong>{formatMoney(current.data.total, currency, moneyLocale)}</strong>
            {!current.data.complete && l.requirements.length > 0 && <span className="warn small">{t('incomplete')}</span>}
          </div>
        )}
      </div>

      <section className="card">
        <div className="section-head">
          <h2>{t('priceHistory')}</h2>
          <div className="segmented" role="group">
            {RANGES.map((r) => (
              <button key={r} type="button" className={r === range ? 'on' : ''} aria-pressed={r === range} onClick={() => setRange(r)}>
                {r} {t('days')}
              </button>
            ))}
          </div>
        </div>
        {history.data && l.requirements.length > 0 && <PriceChart history={history.data} requirements={l.requirements} shops={shops.data ?? []} />}
      </section>

      <section className="card">
        <div className="section-head">
          <h2>{t('requirements')}</h2>
          {editing === null && (
            <button type="button" className="primary" onClick={() => setEditing('new')}>
              {t('addItem')}
            </button>
          )}
        </div>

        {editing !== null && (
          <RequirementEditor
            list={l}
            currency={currency}
            requirement={editing === 'new' ? undefined : editing}
            onCancel={() => setEditing(null)}
            onSaved={(updated) => {
              setEditing(null);
              refresh(updated);
            }}
          />
        )}

        <div className="table-scroll">
          <table className="items">
            <thead>
              <tr>
                <th>{t('item')}</th>
                <th>{t('bestOffer')}</th>
                <th className="num">{t('total')}</th>
                <th aria-label="actions" />
              </tr>
            </thead>
            <tbody>
              {l.requirements.map((r) => {
                const offer = best.get(r.id);
                const defs = categories.data?.find((c) => c.id === r.categoryId)?.attributes ?? [];
                return (
                  <tr key={r.id}>
                    <td>
                      <strong>{r.quantity > 1 ? `${r.quantity}× ` : ''}{r.title}</strong>
                      <div className="muted small">
                        {r.pinnedProductId ? t('pinned') : describeConstraints(r.constraints, defs) || t('any')} · {r.matchCount} {t('matches')}
                      </div>
                    </td>
                    <td>
                      {offer ? (
                        <>
                          <a href={offer.url} target="_blank" rel="noreferrer noopener">{offer.productName}</a>
                          <div className="muted small">
                            {offer.shopName}
                            {offer.effectivePrice < offer.price && <> · <s>{formatMoney(offer.price, currency, moneyLocale)}</s></>}
                          </div>
                        </>
                      ) : (
                        <span className="muted">{t('noOffer')}</span>
                      )}
                    </td>
                    <td className="num">{offer ? formatMoney(offer.linePrice, currency, moneyLocale) : '—'}</td>
                    <td className="row-actions">
                      <button type="button" className="link" onClick={() => setEditing(r)}>{t('edit')}</button>
                      <button
                        type="button"
                        className="link danger"
                        onClick={() => window.confirm(t('confirmDelete')) && removeItem.mutate(r.id)}
                      >
                        {t('delete')}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card">
        <h2>{t('shops')}</h2>
        <p className="muted small">{t('shopsHint')}</p>
        <div className="chips">
          {shops.data?.map((s) => {
            const on = l.allowedShopIds.includes(s.id);
            return (
              <button key={s.id} type="button" className={on ? 'chip on' : 'chip'} aria-pressed={on} onClick={() => toggleShop(s.id)}>
                {s.name}
              </button>
            );
          })}
          {l.allowedShopIds.length > 0 && (
            <button type="button" className="link" onClick={() => updateShops.mutate([])}>{t('allShops')}</button>
          )}
        </div>
      </section>

      <button type="button" className="link danger" onClick={() => window.confirm(t('confirmDelete')) && removeList.mutate()}>
        {t('delete')} “{l.name}”
      </button>
    </main>
  );
}
