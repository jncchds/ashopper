/** Round axis bounds and ~4–6 ticks on a 1/2/5×10ⁿ step, so labels read 5,100 / 5,200 instead of 5,185 / 5,270. */
export function niceAxis(low: number, high: number): { min: number; max: number; ticks: number[] } {
  const span = Math.max(high - low, Math.abs(high) * 0.01, 1);
  const raw = span / 4;
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 5, 10].map((m) => m * magnitude).find((s) => s >= raw)!;
  const min = Math.floor(low / step) * step;
  const max = Math.ceil(high / step) * step;
  const ticks: number[] = [];
  for (let v = min; v <= max + step / 2; v += step) ticks.push(Math.round(v * 100) / 100);
  return { min, max, ticks };
}
