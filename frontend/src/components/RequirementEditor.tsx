import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Pin } from 'lucide-react';
import clsx from 'clsx';
import { api, errorMessage, unwrap, type Schemas } from '../api/client';
import { formatMoney } from '../format';
import { useI18n } from '../i18n';
import { useCategories } from '../queries';
import { emptyField, fromConstraints, toConstraints, type FieldState } from '../specs';
import { Segmented, Spinner } from './ui';

interface Props {
  list: Schemas['ListDto'];
  currency: string;
  /** Existing requirement to edit; undefined = add a new one. */
  requirement?: Schemas['RequirementDto'];
  onSaved(list: Schemas['ListDto']): void;
  onCancel(): void;
}

/** Debounces a value, so the live preview doesn't query on every keystroke. */
function useDebounced<T>(value: T, ms = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return debounced;
}

/** Body of the add/edit item modal: category, specs as constraints, and a live preview of what matches. */
export function RequirementEditor({ list, currency, requirement, onSaved, onCancel }: Props) {
  const { t, locale } = useI18n();
  const categories = useCategories();
  const leaves = useMemo(() => (categories.data ?? []).filter((c) => c.attributes.length > 0), [categories.data]);

  const [categoryId, setCategoryId] = useState(requirement?.categoryId ?? '');
  const [title, setTitle] = useState(requirement?.title ?? '');
  const [quantity, setQuantity] = useState(requirement?.quantity ?? 1);
  const [fields, setFields] = useState<Record<string, FieldState>>(() => fromConstraints(requirement?.constraints ?? []));
  const [pinned, setPinned] = useState<string | null>(requirement?.pinnedProductId ?? null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const category = leaves.find((c) => c.id === categoryId);
  const defs = useMemo(() => category?.attributes ?? [], [category]);
  const constraints = useMemo(() => toConstraints(defs, fields), [defs, fields]);
  const debounced = useDebounced(constraints);

  const preview = useQuery({
    queryKey: ['search', categoryId, list.marketCode, list.allowedShopIds, debounced],
    queryFn: () =>
      unwrap(api.POST('/api/products/search', {
        body: { categoryId, marketCode: list.marketCode, constraints: debounced, shopIds: list.allowedShopIds, take: 8 },
      })),
    enabled: !!categoryId,
    placeholderData: (prev) => prev,
  });

  const setField = (key: string, patch: Partial<FieldState>) =>
    setFields((f) => ({ ...f, [key]: { ...(f[key] ?? emptyField()), ...patch } }));

  async function save() {
    setSaving(true);
    setError(null);
    const body = { title: title.trim() || category?.name || '', categoryId, quantity, constraints, pinnedProductId: pinned };
    const res = requirement
      ? await api.PUT('/api/lists/{id}/requirements/{requirementId}', { params: { path: { id: list.id, requirementId: requirement.id } }, body })
      : await api.POST('/api/lists/{id}/requirements', { params: { path: { id: list.id } }, body });
    setSaving(false);
    // Error bodies aren't in the OpenAPI contract (problem details), so read them untyped.
    const problem: unknown = 'error' in res ? res.error : undefined;
    if (!res.response.ok || !res.data) setError(errorMessage(problem));
    else onSaved(res.data);
  }

  return (
    <div className="space-y-5">
      <div className="grid gap-3 sm:grid-cols-[14rem_1fr_6rem]">
        <label className="block">
          <span className="label">{t('editor.category')}</span>
          <select
            className="input"
            value={categoryId}
            autoFocus={!requirement}
            onChange={(e) => {
              setCategoryId(e.target.value);
              setFields({});
              setPinned(null);
            }}
          >
            <option value="" disabled>{t('editor.pickCategory')}</option>
            {leaves.map((c) => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="label">{t('editor.title')}</span>
          <input className="input" value={title} placeholder={category?.name} onChange={(e) => setTitle(e.target.value)} maxLength={200} />
        </label>
        <label className="block">
          <span className="label">{t('editor.quantity')}</span>
          <input className="input" type="number" min={1} max={100} value={quantity} onChange={(e) => setQuantity(Number(e.target.value) || 1)} />
        </label>
      </div>

      {category && (
        <div>
          <div className="mb-2 flex flex-wrap items-baseline gap-2">
            <h3 className="font-semibold">{t('editor.specs')}</h3>
            <span className="text-xs muted">{t('editor.specsHint')}</span>
          </div>
          <div className="grid gap-x-5 gap-y-4 sm:grid-cols-2">
            {defs.map((d) => {
              const f = fields[d.key] ?? emptyField();
              return (
                <div key={d.key}>
                  <span className="label">
                    {d.label}
                    {d.unit && <span className="font-normal"> ({d.unit})</span>}
                  </span>
                  {d.type === 'number' && (
                    <div className="flex items-center gap-2">
                      <input className="input" inputMode="decimal" placeholder={t('editor.min')} value={f.min} onChange={(e) => setField(d.key, { min: e.target.value })} aria-label={`${d.label} ${t('editor.min')}`} />
                      <span className="muted">–</span>
                      <input className="input" inputMode="decimal" placeholder={t('editor.max')} value={f.max} onChange={(e) => setField(d.key, { max: e.target.value })} aria-label={`${d.label} ${t('editor.max')}`} />
                    </div>
                  )}
                  {d.type === 'enum' && (
                    <div className="flex flex-wrap gap-1.5">
                      {(d.allowedValues ?? []).map((v) => {
                        const on = f.values.includes(v);
                        return (
                          <button
                            key={v}
                            type="button"
                            className={clsx('chip px-2.5 py-1 text-sm', on && 'chip-on')}
                            aria-pressed={on}
                            onClick={() => setField(d.key, { values: on ? f.values.filter((x) => x !== v) : [...f.values, v] })}
                          >
                            {v}
                          </button>
                        );
                      })}
                    </div>
                  )}
                  {d.type === 'bool' && (
                    <Segmented
                      value={f.bool}
                      onChange={(bool) => setField(d.key, { bool })}
                      options={[
                        { value: '' as const, label: t('editor.any') },
                        { value: 'true' as const, label: t('editor.yes') },
                        { value: 'false' as const, label: t('editor.no') },
                      ]}
                      label={d.label}
                    />
                  )}
                  {d.type === 'text' && (
                    <input className="input" placeholder={t('editor.contains')} value={f.text} onChange={(e) => setField(d.key, { text: e.target.value })} />
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {category && (
        <div>
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <h3 className="font-semibold">{t('editor.matching', { count: preview.data?.total ?? 0 })}</h3>
            {preview.isFetching && <Spinner className="h-4 w-4" />}
            {pinned && (
              <button type="button" className="btn-ghost ml-auto py-0.5 text-xs" onClick={() => setPinned(null)}>
                {t('editor.unpin')}
              </button>
            )}
          </div>
          <ul className="divide-y divide-line rounded-md border border-line">
            {preview.data?.items.map((p) => (
              <li key={p.id} className={clsx('flex items-center gap-3 px-3 py-2 text-sm', pinned === p.id && 'bg-accent-faint')}>
                <span className="min-w-0 flex-1 truncate">{p.name}</span>
                <span className="num font-medium">{p.lowestPrice == null ? '—' : formatMoney(p.lowestPrice, currency, locale)}</span>
                <button
                  type="button"
                  className={clsx('btn-ghost px-2 py-0.5 text-xs', pinned === p.id && 'text-accent')}
                  onClick={() => setPinned(p.id)}
                  disabled={pinned === p.id}
                >
                  <Pin className="h-3.5 w-3.5" /> {pinned === p.id ? t('editor.pinned') : t('editor.pin')}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {error && <p className="text-sm text-danger" role="alert">{error}</p>}
      <div className="flex justify-end gap-2 border-t border-line pt-4">
        <button type="button" className="btn-ghost" onClick={onCancel}>{t('common.cancel')}</button>
        <button type="button" className="btn-primary" onClick={save} disabled={!categoryId || saving}>{t('common.save')}</button>
      </div>
    </div>
  );
}
