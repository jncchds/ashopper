import createClient from 'openapi-fetch';
import type { components, paths } from './schema';

export type Schemas = components['schemas'];

/**
 * Auth state lives in memory only: the access token is never written to storage, so XSS can't
 * lift a long-lived credential. The refresh token is an HttpOnly cookie the browser sends to /auth.
 */
let accessToken: string | null = null;
type Listener = (user: Schemas['UserResponse'] | null) => void;
const listeners = new Set<Listener>();

export function onAuthChange(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function setSession(token: Schemas['TokenResponse'] | null) {
  accessToken = token?.accessToken ?? null;
  listeners.forEach((l) => l(token?.user ?? null));
}

let refreshing: Promise<boolean> | null = null;

/**
 * Exchanges the refresh cookie for a new access token. Concurrent callers share one request.
 * A 409 means another tab just rotated the token; the browser now has the new cookie, so retry once.
 */
export function refreshSession(fetchImpl: typeof fetch = fetch): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      for (let attempt = 0; attempt < 2; attempt++) {
        const res = await fetchImpl('/auth/refresh', { method: 'POST' });
        if (res.status === 409) continue;
        if (!res.ok) {
          setSession(null);
          return false;
        }
        setSession((await res.json()) as Schemas['TokenResponse']);
        return true;
      }
      setSession(null);
      return false;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

/** fetch that adds the bearer token and, on 401, refreshes once and replays the request. */
export function createAuthFetch(fetchImpl: typeof fetch = fetch) {
  return async (input: Request): Promise<Response> => {
    const retry = input.clone();
    const send = (req: Request) => {
      if (accessToken) req.headers.set('Authorization', `Bearer ${accessToken}`);
      return fetchImpl(req);
    };

    const res = await send(input);
    if (res.status !== 401 || new URL(input.url).pathname.startsWith('/auth/')) return res;
    return (await refreshSession(fetchImpl)) ? send(retry) : res;
  };
}

export const api = createClient<paths>({ baseUrl: window.location.origin, fetch: createAuthFetch() });

/** Problem-details (RFC 9457) → one readable message. */
export function errorMessage(error: unknown): string {
  if (!error || typeof error !== 'object') return 'Something went wrong.';
  const e = error as { title?: string; detail?: string; errors?: Record<string, string[]> };
  const fieldErrors = e.errors ? Object.values(e.errors).flat() : [];
  return fieldErrors.length > 0 ? fieldErrors.join(' ') : (e.detail ?? e.title ?? 'Something went wrong.');
}

/** Unwraps an openapi-fetch result for TanStack Query: data or a thrown, readable error. */
export async function unwrap<T>(promise: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await promise;
  if (error !== undefined || !response.ok) throw new Error(errorMessage(error));
  return data as T;
}
