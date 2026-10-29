import { useMemo, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ExternalLink, Plus, RefreshCw, Trash2 } from 'lucide-react';
import { Line, LineChart, ResponsiveContainer, YAxis } from 'recharts';
import clsx from 'clsx';
import { api, unwrap, type Schemas } from '../api/client';
import { Empty, ErrorBox, Loading, PageTitle } from '../components/ui';
import { formatDay, formatMoney } from '../format';
import { useI18n } from '../i18n';
import { useCategories } from '../queries';

type Tracked = Schemas['TrackedUrlDto'];
type Store = Schemas['StoreDto'];
type Status = Schemas['ScrapeStatus'];

const STATUS_STYLE: Record<Status, string> = {
  pending: 'bg-muted/10 text-muted',
  ok: 'bg-success/10 text-success',
  noPrice: 'bg-warning/10 text-warning',
  notFound: 'bg-danger/10 text-danger',
  blocked: 'bg-warning/10 text-warning',
  robotsDisallowed: 'bg-danger/10 text-danger',
  error: 'bg-danger/10 text-danger',
};

/** Product links the polite scraper checks on a schedule; each feeds the same price history lists use. */
export function TrackedPage() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const stores = useQuery({ queryKey: ['stores'], queryFn: () => unwrap(api.GET('/api/stores')) });
  const tracked = useQuery({
    queryKey: ['tracked'],
    queryFn: () => unwrap(api.GET('/api/tracked')),
    // While a link waits for its first check, poll so the price shows up without a reload.
    refetchInterval: (q) => (q.state.data?.some((x) => x.status === 'pending') ? 15_000 : false),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['tracked'] });

  return (
    <>
      <PageTitle subtitle={t('tracked.subtitle')}>{t('tracked.title')}</PageTitle>

      {stores.data && <StoreStrip stores={stores.data} />}
      <AddForm tracked={tracked.data ?? []} onAdded={refresh} />

      {tracked.isLoading && <Loading />}
      {tracked.error && <ErrorBox error={tracked.error} />}
      {tracked.data?.length === 0 && <Empty>{t('tracked.empty')}</Empty>}

      <div className="flex flex-col gap-3">
        {tracked.data?.map((item) => <TrackedCard key={item.id} item={item} onChanged={refresh} />)}
      </div>
    </>
  );
}

function StoreStrip({ stores }: { stores: Store[] }) {
  const { t, locale } = useI18n();
  const [now] = useState(Date.now); // when the page opened is precise enough for "still backing off?"
  return (
    <div className="mb-4 flex flex-wrap items-center gap-2 text-sm">
      <span className="caps">{t('tracked.stores')}</span>
      {stores.map((s) => {
        const blocked = !!s.blockedUntil && Date.parse(s.blockedUntil) > now;
        const note = blocked
          ? `${s.blockReason ?? ''} ${t('tracked.blockedUntil', { time: new Date(s.blockedUntil!).toLocaleString(locale) })}`
          : (s.knownLimitation ?? undefined);
        return (
          <span
            key={s.key}
            title={note}
            className={clsx('pill-muted gap-1', !s.enabled && 'line-through', (blocked || s.knownLimitation) && 'text-warning')}
          >
            {s.name}
            {(blocked || s.knownLimitation) && <span aria-label={t('tracked.limited')}>⚠</span>}
          </span>
        );
      })}
    </div>
  );
}

function AddForm({ tracked, onAdded }: { tracked: Tracked[]; onAdded: () => void }) {
  const { t } = useI18n();
  const categories = useCategories();
  const [url, setUrl] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [productId, setProductId] = useState('');

  // Only leaf categories: a product needs one with attributes so a list requirement can pin it.
  const leaves = useMemo(
    () => (categories.data ?? []).filter((c) => c.attributes.length > 0).sort((a, b) => a.name.localeCompare(b.name)),
    [categories.data],
  );
  // "Same item as": products already tracked in this category, once each.
  const sameAs = useMemo(() => {
    const seen = new Map<string, string>();
    for (const x of tracked) if (x.productId && x.categoryId === categoryId && !seen.has(x.productId)) seen.set(x.productId, x.title ?? x.url);
    return [...seen.entries()];
  }, [tracked, categoryId]);

  const add = useMutation({
    mutationFn: () => unwrap(api.POST('/api/tracked', { body: { url: url.trim(), categoryId, productId: productId || null } })),
    onSuccess: () => {
      setUrl('');
      setProductId('');
      onAdded();
    },
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    add.mutate();
  }

  return (
    <form className="card mb-6 p-5" onSubmit={submit}>
      <div className="grid gap-3 md:grid-cols-[1fr_14rem]">
        <label className="block min-w-0">
          <span className="label">{t('tracked.url')}</span>
          <input className="input" type="url" required maxLength={2048} value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://www.x-kom.pl/p/…" />
        </label>
        <label className="block">
          <span className="label">{t('tracked.category')}</span>
          <select className="input" required value={categoryId} onChange={(e) => { setCategoryId(e.target.value); setProductId(''); }}>
            <option value="" disabled>{t('tracked.pickCategory')}</option>
            {leaves.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
      </div>
      {sameAs.length > 0 && (
        <label className="mt-3 block">
          <span className="label">{t('tracked.sameAs')}</span>
          <select className="input" value={productId} onChange={(e) => setProductId(e.target.value)}>
            <option value="">{t('tracked.sameAsNone')}</option>
            {sameAs.map(([id, title]) => <option key={id} value={id}>{title}</option>)}
          </select>
        </label>
      )}
      {add.error && <p className="mt-3 text-sm text-danger" role="alert">{add.error.message}</p>}
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs muted">{t('tracked.politeNote')}</p>
        <button type="submit" className="btn-primary" disabled={add.isPending || !categoryId}>
          <Plus className="h-4 w-4" /> {t('tracked.add')}
        </button>
      </div>
    </form>
  );
}

function TrackedCard({ item, onChanged }: { item: Tracked; onChanged: () => void }) {
  const { t, locale } = useI18n();
  const check = useMutation({
    mutationFn: () => unwrap(api.POST('/api/tracked/{id}/check', { params: { path: { id: item.id } } })),
    onSuccess: onChanged,
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/tracked/{id}', { params: { path: { id: item.id } } })),
    onSuccess: onChanged,
  });
  const url = new URL(item.url);
  const when = (iso: string) => new Date(iso).toLocaleString(locale, { dateStyle: 'short', timeStyle: 'short' });

  return (
    <article className="card flex flex-col gap-3 px-4 py-4 sm:flex-row sm:items-center sm:px-6">
      <div className="min-w-0 flex-1">
        <h3 className="truncate font-semibold" title={item.title ?? item.url}>
          {item.title ?? `${url.host}${url.pathname}`}
        </h3>
        <div className="mt-1 flex flex-wrap items-center gap-2 text-sm">
          <span className="chip cursor-default">{item.storeName}</span>
          <span className={clsx('inline-flex items-center rounded px-2 py-0.5 text-xs', STATUS_STYLE[item.status])} title={item.lastError ?? undefined}>
            {t(`tracked.status.${item.status}`)}
          </span>
          <span className="text-xs muted">
            {item.lastCheckedAt ? t('tracked.checked', { time: when(item.lastCheckedAt) }) : t('tracked.notChecked')}
            {' · '}
            {t('tracked.next', { time: when(item.nextCheckAt) })}
          </span>
        </div>
        {item.lastError && item.status !== 'ok' && <p className="mt-1 truncate text-xs muted" title={item.lastError}>{item.lastError}</p>}
      </div>

      {item.history.length > 1 && <Sparkline history={item.history} currency={item.currency ?? 'PLN'} />}

      <div className="flex shrink-0 items-center gap-3 sm:flex-col sm:items-end sm:gap-0.5">
        {item.price != null && item.currency ? (
          <strong className={clsx('text-lg tabular-nums', item.inStock === false && 'text-muted line-through')}>
            {formatMoney(item.price, item.currency, locale)}
          </strong>
        ) : (
          <span className="muted">—</span>
        )}
        {item.inStock === false && <span className="text-xs text-warning">{t('tracked.outOfStock')}</span>}
      </div>

      <div className="flex shrink-0 items-center gap-1">
        <a className="btn-ghost px-2" href={item.url} target="_blank" rel="noopener noreferrer" title={t('tracked.open')} aria-label={t('tracked.open')}>
          <ExternalLink className="h-4 w-4" />
        </a>
        <button type="button" className="btn-ghost px-2" onClick={() => check.mutate()} disabled={check.isPending} title={t('tracked.checkNow')} aria-label={t('tracked.checkNow')}>
          <RefreshCw className={clsx('h-4 w-4', check.isPending && 'animate-spin')} />
        </button>
        <button
          type="button"
          className="btn-ghost px-2 hover:text-danger"
          onClick={() => window.confirm(t('common.confirmDelete', { name: item.title ?? url.host })) && remove.mutate()}
          title={t('tracked.remove')}
          aria-label={t('tracked.remove')}
        >
          <Trash2 className="h-4 w-4" />
        </button>
      </div>
      {check.error && <p className="text-xs text-danger sm:basis-full" role="alert">{check.error.message}</p>}
    </article>
  );
}

/** A small trend line with first/last day in its label; detail lives in the price, not the chart. */
function Sparkline({ history, currency }: { history: Schemas['TrackedPricePointDto'][]; currency: string }) {
  const { t, locale } = useI18n();
  const first = history[0];
  const last = history[history.length - 1];
  const low = Math.min(...history.map((p) => p.price));
  const label = t('tracked.trend', {
    from: formatDay(first.day, locale),
    to: formatDay(last.day, locale),
    low: formatMoney(low, currency, locale),
  });
  return (
    <div className="h-10 w-full shrink-0 sm:w-36" role="img" aria-label={label} title={label}>
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={history} margin={{ top: 4, right: 2, bottom: 4, left: 2 }}>
          <YAxis hide domain={['dataMin', 'dataMax']} />
          <Line type="stepAfter" dataKey="price" stroke="var(--accent)" strokeWidth={2} dot={false} isAnimationActive={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
