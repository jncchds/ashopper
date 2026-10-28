import { describe, expect, it } from 'vitest';
import { niceAxis } from './chartAxis';

describe('niceAxis', () => {
  it('snaps to round steps around the data', () => {
    expect(niceAxis(5102.93, 5411.93)).toEqual({ min: 5100, max: 5500, ticks: [5100, 5200, 5300, 5400, 5500] });
  });

  it('handles a flat series without a zero-width axis', () => {
    const axis = niceAxis(399.99, 399.99);
    expect(axis.max).toBeGreaterThan(axis.min);
    expect(axis.ticks.length).toBeGreaterThanOrEqual(2);
  });

  it('works for large totals', () => {
    const axis = niceAxis(41_250, 47_900);
    expect(axis.ticks.every((t) => t % 1000 === 0)).toBe(true);
    expect(axis.min).toBeLessThanOrEqual(41_250);
    expect(axis.max).toBeGreaterThanOrEqual(47_900);
  });
});
