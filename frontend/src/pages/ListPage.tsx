import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ExternalLink, Pencil, Plus, Trash2 } from 'lucide-react';
import clsx from 'clsx';
import { api, unwrap, type Schemas } from '../api/client';
import { PriceChart } from '../components/PriceChart';
import { RequirementEditor } from '../components/RequirementEditor';
import { Empty, ErrorBox, Loading, Modal, PageTitle, Section, Segmented } from '../components/ui';
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

  if (list.isLoading) return <Loading />;
  if (!list.data) return <ErrorBox error={list.error ?? 'Not found'} />;

  const l = list.data;
  const best = new Map(current.data?.items.map((i) => [i.requirementId, i.best]) ?? []);
  const money = (v: number) => formatMoney(v, currency, locale);
  const toggleShop = (shopId: string) => {
    const set = new Set(l.allowedShopIds);
    if (set.has(shopId)) set.delete(shopId);
    else set.add(shopId);
    updateShops.mutate([...set]);
  };

  return (
    <>
      <PageTitle
        subtitle={market ? `${market.name} · ${market.currency}` : undefined}
        actions={
          <>
            <button type="button" className="btn-primary" onClick={() => setEditing('new')}>
              <Plus className="h-4 w-4" /> {t('list.addItem')}
            </button>
            <button
              type="button"
              className="btn-danger"
              title={t('list.deleteList')}
              onClick={() => window.confirm(t('common.confirmDelete', { name: l.name })) && removeList.mutate()}
            >
              <Trash2 className="h-4 w-4" />
              <span className="max-sm:sr-only">{t('list.deleteList')}</span>
            </button>
          </>
        }
      >
        {l.name}
      </PageTitle>

      <div className="space-y-6">
        {current.data && l.requirements.length > 0 && (
          <div className="card flex flex-wrap items-baseline gap-x-4 gap-y-1 p-4">
            <span className="caps">{t('list.totalNow')}</span>
            <span className="text-3xl font-semibold tabular-nums">{money(current.data.total)}</span>
            {!current.data.complete && <span className="text-sm text-warning">⚠ {t('list.incomplete')}</span>}
          </div>
        )}

        {l.requirements.length > 0 && (
          <Section
            title={t('list.priceHistory')}
            actions={
              <Segmented
                value={range}
                onChange={setRange}
                options={RANGES.map((r) => ({ value: r, label: t('list.days', { count: r }) }))}
                label={t('list.priceHistory')}
              />
            }
          >
            {history.isLoading && <Loading />}
            {history.data && <PriceChart history={history.data} requirements={l.requirements} shops={shops.data ?? []} />}
          </Section>
        )}

        <Section title={t('list.items')}>
          {l.requirements.length === 0 ? (
            <Empty>
              <p>{t('list.noItems')}</p>
              <button type="button" className="btn-primary mt-4" onClick={() => setEditing('new')}>
                <Plus className="h-4 w-4" /> {t('list.addItem')}
              </button>
            </Empty>
          ) : (
            <ul className="divide-y divide-line rounded-md border border-line">
              {l.requirements.map((r) => {
                const offer = best.get(r.id);
                const defs = categories.data?.find((c) => c.id === r.categoryId)?.attributes ?? [];
                const specs = r.pinnedProductId ? t('list.pinned') : describeConstraints(r.constraints, defs);
                return (
                  <li key={r.id} className="flex flex-wrap items-start gap-x-4 gap-y-2 p-3 sm:flex-nowrap">
                    <div className="min-w-0 flex-1 basis-full sm:basis-auto">
                      <div className="font-semibold">
                        {r.quantity > 1 && <span className="mr-1 text-accent">{r.quantity}×</span>}
                        {r.title}
                      </div>
                      <div className="mt-1 flex flex-wrap gap-1.5">
                        {(specs || t('list.anySpec')).split(' · ').map((s) => (
                          <span key={s} className="pill-muted">{s}</span>
                        ))}
                        <span className={clsx('pill-muted', r.matchCount === 0 && 'text-danger')}>{t('list.matches', { count: r.matchCount })}</span>
                      </div>
                    </div>
                    <div className="min-w-0 flex-1 text-sm">
                      {offer ? (
                        <>
                          <a href={offer.url} target="_blank" rel="noreferrer noopener" className="link flex min-w-0 items-center gap-1" title={offer.productName}>
                            <span className="truncate">{offer.productName}</span>
                            <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                          </a>
                          <div className="truncate text-xs muted">
                            {offer.shopName}
                            {offer.effectivePrice < offer.price && <> · <s>{money(offer.price)}</s></>}
                          </div>
                        </>
                      ) : (
                        <span className="muted">{t('list.noOffer')}</span>
                      )}
                    </div>
                    <div className="num w-28 shrink-0 font-semibold">{offer ? money(offer.linePrice) : '—'}</div>
                    <div className="flex shrink-0 gap-1">
                      <button type="button" className="btn-ghost px-2" title={t('common.edit')} aria-label={t('common.edit')} onClick={() => setEditing(r)}>
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        type="button"
                        className="btn-ghost px-2 hover:text-danger"
                        title={t('common.delete')}
                        aria-label={t('common.delete')}
                        onClick={() => window.confirm(t('common.confirmDelete', { name: r.title })) && removeItem.mutate(r.id)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
        </Section>

        <Section title={t('list.shops')} hint={t('list.shopsHint')}>
          <div className="flex flex-wrap items-center gap-2">
            {shops.data?.map((s) => {
              const on = l.allowedShopIds.includes(s.id);
              return (
                <button key={s.id} type="button" className={clsx('chip px-3 py-1 text-sm', on && 'chip-on')} aria-pressed={on} onClick={() => toggleShop(s.id)}>
                  {s.name}
                </button>
              );
            })}
            {l.allowedShopIds.length > 0 && (
              <button type="button" className="btn-ghost py-1 text-xs" onClick={() => updateShops.mutate([])}>
                {t('list.allShops')}
              </button>
            )}
          </div>
        </Section>
      </div>

      <Modal open={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? t('editor.addItem') : t('editor.editItem')} wide>
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
      </Modal>
    </>
  );
}
