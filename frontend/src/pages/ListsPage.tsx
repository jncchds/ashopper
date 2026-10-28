import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronRight, Plus } from 'lucide-react';
import { api, unwrap } from '../api/client';
import { Empty, ErrorBox, Loading, PageTitle } from '../components/ui';
import { useI18n } from '../i18n';
import { useMarkets } from '../queries';

export function ListsPage() {
  const { t, locale } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const markets = useMarkets();
  const lists = useQuery({ queryKey: ['lists'], queryFn: () => unwrap(api.GET('/api/lists')) });

  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [market, setMarket] = useState('PL');

  const create = useMutation({
    mutationFn: () => unwrap(api.POST('/api/lists', { body: { name, marketCode: market, allowedShopIds: [] } })),
    onSuccess: async (list) => {
      await queryClient.invalidateQueries({ queryKey: ['lists'] });
      navigate(`/lists/${list.id}`);
    },
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  return (
    <>
      <PageTitle
        subtitle={t('lists.subtitle')}
        actions={
          !creating && (
            <button type="button" className="btn-primary" onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" /> {t('lists.new')}
            </button>
          )
        }
      >
        {t('lists.title')}
      </PageTitle>

      {creating && (
        <form className="card mb-6 p-5" onSubmit={submit}>
          <div className="grid gap-3 sm:grid-cols-[1fr_14rem]">
            <label className="block">
              <span className="label">{t('lists.name')}</span>
              <input className="input" autoFocus required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} placeholder={t('lists.namePlaceholder')} />
            </label>
            <label className="block">
              <span className="label">{t('lists.market')}</span>
              <select className="input" value={market} onChange={(e) => setMarket(e.target.value)}>
                {markets.data?.map((m) => (
                  <option key={m.code} value={m.code}>
                    {m.name} ({m.currency})
                  </option>
                ))}
              </select>
            </label>
          </div>
          {create.error && <p className="mt-3 text-sm text-danger" role="alert">{create.error.message}</p>}
          <div className="mt-4 flex justify-end gap-2">
            <button type="button" className="btn-ghost" onClick={() => setCreating(false)}>{t('common.cancel')}</button>
            <button type="submit" className="btn-primary" disabled={create.isPending}>{t('common.create')}</button>
          </div>
        </form>
      )}

      {lists.isLoading && <Loading />}
      {lists.error && <ErrorBox error={lists.error} />}
      {lists.data?.length === 0 && !creating && <Empty>{t('lists.empty')}</Empty>}

      <div className="flex flex-col gap-3">
        {lists.data?.map((l) => (
          <Link key={l.id} to={`/lists/${l.id}`} className="card group flex items-center gap-4 px-4 py-4 transition-colors hover:border-accent sm:px-6">
            <span className="text-2xl" aria-hidden>📋</span>
            <div className="min-w-0 flex-1">
              <h3 className="truncate font-semibold">{l.name}</h3>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-sm">
                <span className="chip">{l.marketCode}</span>
                <span className="pill-muted">{t('lists.items', { count: l.requirementCount })}</span>
                <span className="text-xs muted">{t('lists.updated', { date: new Date(l.updatedAt).toLocaleDateString(locale) })}</span>
              </div>
            </div>
            <span className="hidden items-center gap-1 text-sm font-medium text-accent sm:inline-flex">
              {t('lists.open')} <ChevronRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
            </span>
          </Link>
        ))}
      </div>
    </>
  );
}
