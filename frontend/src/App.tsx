import { Navigate, createBrowserRouter } from 'react-router';
import { Layout } from './components/Layout';
import { ConfirmEmailPage, ResetPasswordPage } from './pages/AccountPages';
import { ListsPage } from './pages/ListsPage';
import { LoginPage } from './pages/LoginPage';
import { SettingsPage } from './pages/SettingsPage';

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/confirm-email', element: <ConfirmEmailPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  {
    element: <Layout />,
    children: [
      { path: '/', element: <ListsPage /> },
      // Lazy: the chart library is the biggest dependency; the login and overview pages don't need it.
      { path: '/lists/:id', lazy: async () => ({ Component: (await import('./pages/ListPage')).ListPage }) },
      { path: '/settings', element: <SettingsPage /> },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
]);
