import { useEffect, useState, type ReactNode } from 'react';
import { api, onAuthChange, refreshSession, setSession, type Schemas } from '../api/client';
import { AuthContext } from './useAuth';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<Schemas['UserResponse'] | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const unsubscribe = onAuthChange(setUser);
    // Restore the session after a reload: the refresh cookie survives, the in-memory token doesn't.
    refreshSession().finally(() => setReady(true));
    return () => {
      unsubscribe();
    };
  }, []);

  const value = {
    user,
    ready,
    async login(email: string, password: string) {
      const { data, error } = await api.POST('/auth/login', { body: { email, password } });
      if (error || !data) return error ?? { title: 'Login failed' };
      setSession(data);
      return null;
    },
    async register(email: string, password: string) {
      const { error } = await api.POST('/auth/register', { body: { email, password } });
      return error ?? null;
    },
    async logout() {
      await api.POST('/auth/logout');
      setSession(null);
    },
  };

  return <AuthContext value={value}>{children}</AuthContext>;
}
