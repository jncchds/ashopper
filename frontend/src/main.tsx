import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router';
import '@fontsource-variable/inter';
import './i18n';
import './index.css';
import { router } from './App';
import { AuthProvider } from './auth/AuthProvider';
import { applyTheme, storedTheme } from './hooks/useTheme';

// Before the first paint, so there's no light flash in dark mode.
applyTheme(storedTheme());

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 } },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
