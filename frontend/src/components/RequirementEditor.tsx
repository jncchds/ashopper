import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, errorMessage, unwrap, type Schemas } from '../api/client';
import { formatMoney } from '../format';
import { useI18n } from '../i18n';
import { useCategories } from '../queries';
import { emptyField, fromConstraints, toConstraints, type FieldState } from '../specs';

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
    <div className="editor card">
      <h3>{requirement ? t('editItem') : t('addItem')}</h3>

      <div className="form-row">
        <label>
          {t('category')}
          <select
            value={categoryId}
            onChange={(e) => {
              setCategoryId(e.target.value);
              setFields({});
              setPinned(null);
            }}
          >
            <option value="" disabled>
              —
            </option>
            {leaves.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label className="grow">
          {t('title')}
          <input value={title} placeholder={category?.name} onChange={(e) => setTitle(e.target.value)} maxLength={200} />
        </label>
        <label className="narrow">
          {t('quantity')}
          <input type="number" min={1} max={100} value={quantity} onChange={(e) => setQuantity(Number(e.target.value) || 1)} />
        </label>
      </div>

      {category && (
        <div className="spec-grid">
          {defs.map((d) => {
            const f = fields[d.key] ?? emptyField();
            return (
              <div key={d.key} className="spec">
                <span className="spec-label">
                  {d.label}
                  {d.unit && <span className="muted"> ({d.unit})</span>}
                </span>
                {d.type === 'number' && (
                  <span className="range">
                    <input inputMode="decimal" placeholder={t('min')} value={f.min} onChange={(e) => setField(d.key, { min: e.target.value })} />
                    <span className="muted">–</span>
                    <input inputMode="decimal" placeholder={t('max')} value={f.max} onChange={(e) => setField(d.key, { max: e.target.value })} />
                  </span>
                )}
                {d.type === 'enum' && (
                  <span className="chips">
                    {(d.allowedValues ?? []).map((v) => {
                      const on = f.values.includes(v);
                      return (
                        <button
                          key={v}
                          type="button"
                          className={on ? 'chip on' : 'chip'}
                          aria-pressed={on}
                          onClick={() => setField(d.key, { values: on ? f.values.filter((x) => x !== v) : [...f.values, v] })}
                        >
                          {v}
                        </button>
                      );
                    })}
                  </span>
                )}
                {d.type === 'bool' && (
                  <select value={f.bool} onChange={(e) => setField(d.key, { bool: e.target.value as FieldState['bool'] })}>
                    <option value="">{t('any')}</option>
                    <option value="true">{t('yes')}</option>
                    <option value="false">{t('no')}</option>
                  </select>
                )}
                {d.type === 'text' && (
                  <input placeholder={t('contains')} value={f.text} onChange={(e) => setField(d.key, { text: e.target.value })} />
                )}
              </div>
            );
          })}
        </div>
      )}

      {category && preview.data && (
        <div className="preview">
          <div className="preview-head">
            <strong>
              {t('matchingProducts')}: {preview.data.total}
            </strong>
            {pinned && (
              <button type="button" className="link" onClick={() => setPinned(null)}>
                {t('unpin')}
              </button>
            )}
          </div>
          <ul>
            {preview.data.items.map((p) => (
              <li key={p.id} className={pinned === p.id ? 'pinned' : undefined}>
                <span className="grow">{p.name}</span>
                <span className="num">{p.lowestPrice == null ? '—' : formatMoney(p.lowestPrice, currency, locale)}</span>
                <button type="button" className="link" onClick={() => setPinned(p.id)} disabled={pinned === p.id}>
                  {pinned === p.id ? t('pinned') : t('onlyThis')}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {error && <p className="error">{error}</p>}
      <div className="actions">
        <button type="button" onClick={onCancel}>
          {t('cancel')}
        </button>
        <button type="button" className="primary" onClick={save} disabled={!categoryId || saving}>
          {t('save')}
        </button>
      </div>
    </div>
  );
}
