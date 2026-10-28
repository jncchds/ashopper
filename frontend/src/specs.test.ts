import { describe, expect, it } from 'vitest';
import type { Schemas } from './api/client';
import { describeConstraints, emptyField, fromConstraints, toConstraints } from './specs';

const defs: Schemas['AttributeDefinition'][] = [
  { key: 'memory_type', label: 'Type', type: 'enum', allowedValues: ['DDR4', 'DDR5'] },
  { key: 'capacity_gb', label: 'Capacity', type: 'number', unit: 'GB' },
  { key: 'cas_latency', label: 'CAS latency', type: 'number' },
  { key: 'speed_mts', label: 'Speed', type: 'number' },
  { key: 'rgb', label: 'RGB', type: 'bool' },
  { key: 'name', label: 'Name', type: 'text' },
];

describe('toConstraints', () => {
  it('turns editor fields into the narrowest operator', () => {
    const constraints = toConstraints(defs, {
      memory_type: { ...emptyField(), values: ['DDR5'] },
      capacity_gb: { ...emptyField(), min: '32', max: '32' },
      cas_latency: { ...emptyField(), max: '30' },
      speed_mts: { ...emptyField(), min: '5600', max: '6400' },
      rgb: { ...emptyField(), bool: 'false' },
      name: { ...emptyField(), text: ' fury ' },
    });

    expect(constraints).toEqual([
      { key: 'memory_type', op: 'eq', values: ['DDR5'] },
      { key: 'capacity_gb', op: 'eq', values: ['32'] },
      { key: 'cas_latency', op: 'lte', values: ['30'] },
      { key: 'speed_mts', op: 'between', values: ['5600', '6400'] },
      { key: 'rgb', op: 'eq', values: ['false'] },
      { key: 'name', op: 'contains', values: ['fury'] },
    ]);
  });

  it('skips empty fields and accepts decimal commas', () => {
    expect(toConstraints(defs, { capacity_gb: emptyField() })).toEqual([]);
    expect(toConstraints(defs, { capacity_gb: { ...emptyField(), min: '1,5' } })).toEqual([{ key: 'capacity_gb', op: 'gte', values: ['1.5'] }]);
  });

  it('uses "in" for several enum values', () => {
    expect(toConstraints(defs, { memory_type: { ...emptyField(), values: ['DDR4', 'DDR5'] } })).toEqual([
      { key: 'memory_type', op: 'in', values: ['DDR4', 'DDR5'] },
    ]);
  });
});

describe('fromConstraints', () => {
  it('round-trips with toConstraints', () => {
    const original: Schemas['SpecConstraint'][] = [
      { key: 'memory_type', op: 'in', values: ['DDR4', 'DDR5'] },
      { key: 'capacity_gb', op: 'eq', values: ['32'] },
      { key: 'speed_mts', op: 'gte', values: ['6000'] },
      { key: 'rgb', op: 'eq', values: ['true'] },
    ];
    expect(toConstraints(defs, fromConstraints(original))).toEqual(original);
  });
});

describe('describeConstraints', () => {
  it('reads like a spec line', () => {
    expect(
      describeConstraints(
        [
          { key: 'memory_type', op: 'eq', values: ['DDR5'] },
          { key: 'cas_latency', op: 'lte', values: ['30'] },
          { key: 'rgb', op: 'eq', values: ['false'] },
        ],
        defs,
      ),
    ).toBe('Type: DDR5 · CAS latency ≤ 30 · no RGB');
  });
});
