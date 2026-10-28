import type { Schemas } from './api/client';

type Def = Schemas['AttributeDefinition'];
type Constraint = Schemas['SpecConstraint'];

/** Editor state for one attribute. Which fields are used depends on the attribute type. */
export interface FieldState {
  min: string;
  max: string;
  values: string[];
  bool: '' | 'true' | 'false';
  text: string;
}

export const emptyField = (): FieldState => ({ min: '', max: '', values: [], bool: '', text: '' });

/** Editor state → API constraints. Empty fields mean "don't care" and produce nothing. */
export function toConstraints(defs: Def[], state: Record<string, FieldState>): Constraint[] {
  const out: Constraint[] = [];
  for (const def of defs) {
    const f = state[def.key];
    if (!f) continue;
    switch (def.type) {
      case 'number': {
        const min = f.min.trim().replace(',', '.');
        const max = f.max.trim().replace(',', '.');
        if (min && max) out.push(min === max ? { key: def.key, op: 'eq', values: [min] } : { key: def.key, op: 'between', values: [min, max] });
        else if (min) out.push({ key: def.key, op: 'gte', values: [min] });
        else if (max) out.push({ key: def.key, op: 'lte', values: [max] });
        break;
      }
      case 'enum':
        if (f.values.length === 1) out.push({ key: def.key, op: 'eq', values: f.values });
        else if (f.values.length > 1) out.push({ key: def.key, op: 'in', values: f.values });
        break;
      case 'bool':
        if (f.bool) out.push({ key: def.key, op: 'eq', values: [f.bool] });
        break;
      case 'text':
        if (f.text.trim()) out.push({ key: def.key, op: 'contains', values: [f.text.trim()] });
        break;
    }
  }
  return out;
}

/** API constraints → editor state (inverse of toConstraints). */
export function fromConstraints(constraints: readonly Constraint[]): Record<string, FieldState> {
  const state: Record<string, FieldState> = {};
  for (const c of constraints) {
    const f = (state[c.key] ??= emptyField());
    const [a = '', b = ''] = c.values;
    switch (c.op) {
      case 'gte': f.min = a; break;
      case 'lte': f.max = a; break;
      case 'between': f.min = a; f.max = b; break;
      case 'in': f.values = [...c.values]; break;
      case 'contains': f.text = a; break;
      case 'eq':
        if (a === 'true' || a === 'false') f.bool = a;
        else if (/^-?\d+(\.\d+)?$/.test(a)) { f.min = a; f.max = a; }
        else f.values = [a];
        break;
    }
  }
  return state;
}

/** Short human summary, e.g. "Type: DDR5 · Total capacity: 32 GB · CAS latency ≤ 30 CL". */
export function describeConstraints(constraints: readonly Constraint[], defs: Def[]): string {
  return constraints
    .map((c) => {
      const def = defs.find((d) => d.key === c.key);
      const label = def?.label ?? c.key;
      const unit = def?.unit ? ` ${def.unit}` : '';
      const [a, b] = c.values;
      switch (c.op) {
        case 'eq':
          if (def?.type === 'bool') return a === 'true' ? label : `no ${label}`;
          return `${label}: ${a}${unit}`;
        case 'neq': return `${label} ≠ ${c.values.join(' / ')}`;
        case 'gte': return `${label} ≥ ${a}${unit}`;
        case 'lte': return `${label} ≤ ${a}${unit}`;
        case 'between': return `${label} ${a}–${b}${unit}`;
        case 'in': return `${label}: ${c.values.join(' / ')}`;
        case 'contains': return `${label} ~ “${a}”`;
      }
    })
    .join(' · ');
}
