import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createAuthFetch, refreshSession, setSession } from './client';

const tokenResponse = (token: string) =>
  new Response(JSON.stringify({ accessToken: token, expiresIn: 900, user: { id: '1', email: 'a@b.c', emailConfirmed: true } }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });

const urlOf = (input: RequestInfo | URL) => (input instanceof Request ? new URL(input.url).pathname : String(input));

describe('refreshSession', () => {
  beforeEach(() => setSession(null));

  it('shares one request between concurrent callers', async () => {
    const fetchMock = vi.fn(async () => tokenResponse('t1'));
    const results = await Promise.all([refreshSession(fetchMock), refreshSession(fetchMock), refreshSession(fetchMock)]);
    expect(results).toEqual([true, true, true]);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('retries once on 409 (another tab just rotated the cookie)', async () => {
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(null, { status: 409 }))
      .mockResolvedValueOnce(tokenResponse('t2'));
    expect(await refreshSession(fetchMock)).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('signs out when the refresh token is rejected', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 401 }));
    expect(await refreshSession(fetchMock)).toBe(false);
  });
});

describe('createAuthFetch', () => {
  beforeEach(() => setSession(null));

  it('on 401 refreshes and replays the request with the new token', async () => {
    const seen: (string | null)[] = [];
    const fetchMock = vi.fn<typeof fetch>(async (input) => {
      if (urlOf(input) === '/auth/refresh') return tokenResponse('fresh');
      const auth = (input as Request).headers.get('Authorization');
      seen.push(auth);
      return new Response(auth === 'Bearer fresh' ? '[]' : null, { status: auth === 'Bearer fresh' ? 200 : 401 });
    });

    const res = await createAuthFetch(fetchMock)(new Request('http://localhost/api/lists'));

    expect(res.status).toBe(200);
    expect(seen).toEqual([null, 'Bearer fresh']);
  });

  it('does not try to refresh for auth endpoints themselves', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 401 }));
    const res = await createAuthFetch(fetchMock)(new Request('http://localhost/auth/login', { method: 'POST' }));
    expect(res.status).toBe(401);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
