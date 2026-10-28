import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '../api/client';
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
    <main className="page">
      <div className="page-head">
        <h1>{t('lists')}</h1>
        {!creating && (
          <button type="button" className="primary" onClick={() => setCreating(true)}>
            {t('newList')}
          </button>
        )}
      </div>

      {creating && (
        <form className="card form-row" onSubmit={submit}>
          <label className="grow">
            {t('listName')}
            <input autoFocus required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} placeholder="Gaming PC 2026" />
          </label>
          <label>
            {t('market')}
            <select value={market} onChange={(e) => setMarket(e.target.value)}>
              {markets.data?.map((m) => (
                <option key={m.code} value={m.code}>
                  {m.name} ({m.currency})
                </option>
              ))}
            </select>
          </label>
          <div className="actions">
            <button type="button" onClick={() => setCreating(false)}>
              {t('cancel')}
            </button>
            <button type="submit" className="primary" disabled={create.isPending}>
              {t('create')}
            </button>
          </div>
          {create.error && <p className="error">{create.error.message}</p>}
        </form>
      )}

      {lists.data?.length === 0 && !creating && <p className="muted">{t('noLists')}</p>}
      <div className="list-grid">
        {lists.data?.map((l) => (
          <Link key={l.id} to={`/lists/${l.id}`} className="card list-card">
            <strong>{l.name}</strong>
            <span className="muted small">
              {l.marketCode} · {l.requirementCount} {t('items')} · {new Date(l.updatedAt).toLocaleDateString(locale)}
            </span>
          </Link>
        ))}
      </div>
    </main>
  );
}
