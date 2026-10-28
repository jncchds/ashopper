import { NavLink, Navigate, Outlet, createBrowserRouter } from 'react-router';
import { useAuth } from './auth/useAuth';
import { useI18n, type Lang } from './i18n';
import { ConfirmEmailPage, ResetPasswordPage } from './pages/AccountPages';
import { ListsPage } from './pages/ListsPage';
import { LoginPage } from './pages/LoginPage';
import { SettingsPage } from './pages/SettingsPage';

function Shell() {
  const { user, ready, logout } = useAuth();
  const { t, lang, setLang } = useI18n();

  if (!ready) return null;
  if (!user) return <Navigate to="/login" replace />;

  return (
    <>
      <header className="topbar">
        <NavLink to="/" className="brand">{t('appName')}</NavLink>
        <nav>
          <NavLink to="/" end>{t('lists')}</NavLink>
          <NavLink to="/settings">{t('settings')}</NavLink>
        </nav>
        <div className="topbar-end">
          <select value={lang} onChange={(e) => setLang(e.target.value as Lang)} aria-label="Language">
            <option value="en">EN</option>
            <option value="pl">PL</option>
          </select>
          <span className="muted small user">{user.email}</span>
          <button type="button" className="link" onClick={() => void logout()}>{t('signOut')}</button>
        </div>
      </header>
      <Outlet />
    </>
  );
}

function PublicOnly({ children }: { children: React.ReactNode }) {
  const { ready } = useAuth();
  return ready ? children : null;
}

export const router = createBrowserRouter([
  { path: '/login', element: <PublicOnly><LoginPage /></PublicOnly> },
  { path: '/confirm-email', element: <ConfirmEmailPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  {
    element: <Shell />,
    children: [
      { path: '/', element: <ListsPage /> },
      // Lazy: the chart library is the biggest dependency; the login and overview pages don't need it.
      { path: '/lists/:id', lazy: async () => ({ Component: (await import('./pages/ListPage')).ListPage }) },
      { path: '/settings', element: <SettingsPage /> },
    ],
  },
  { path: '*', element: <Navigate to="/" replace /> },
]);
