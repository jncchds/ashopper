import { createContext, useContext } from 'react';
import type { Schemas } from '../api/client';

export interface AuthState {
  user: Schemas['UserResponse'] | null;
  /** False until the initial session restore finished. */
  ready: boolean;
  /** Returns null on success, or the problem details. */
  login(email: string, password: string): Promise<unknown>;
  register(email: string, password: string): Promise<unknown>;
  logout(): Promise<void>;
}

export const AuthContext = createContext<AuthState | null>(null);

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
