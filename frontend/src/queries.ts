import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from './api/client';

/** Shared, rarely-changing catalog data. Cached for the session; the server adds ETags on top. */
const catalog = { staleTime: 10 * 60_000 };

export const useMarkets = () =>
  useQuery({ queryKey: ['markets'], queryFn: () => unwrap(api.GET('/api/markets')), ...catalog });

export const useCategories = () =>
  useQuery({ queryKey: ['categories'], queryFn: () => unwrap(api.GET('/api/categories')), ...catalog });

export const useShops = (market: string | undefined) =>
  useQuery({
    queryKey: ['shops', market],
    queryFn: () => unwrap(api.GET('/api/shops', { params: { query: { market: market! } } })),
    enabled: !!market,
    ...catalog,
  });

export const useList = (id: string) =>
  useQuery({ queryKey: ['list', id], queryFn: () => unwrap(api.GET('/api/lists/{id}', { params: { path: { id } } })) });

export const useCurrent = (id: string) =>
  useQuery({ queryKey: ['list', id, 'current'], queryFn: () => unwrap(api.GET('/api/lists/{id}/current', { params: { path: { id } } })) });

export const useHistory = (id: string, from: string, to: string) =>
  useQuery({
    queryKey: ['list', id, 'history', from, to],
    queryFn: () => unwrap(api.GET('/api/lists/{id}/history', { params: { path: { id }, query: { from, to } } })),
  });
