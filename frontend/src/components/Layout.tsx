import { useEffect, useState, type ReactNode } from 'react';
import { NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { api, unwrap } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { THEMES, useTheme } from '../hooks/useTheme';
import { LANGUAGES, rememberLanguage } from '../i18n';

const COLLAPSE_KEY = 'ashopper.sidebar';
const PHONE = '(max-width: 768px)';

function initialCollapsed() {
  try {
    const v = localStorage.getItem(COLLAPSE_KEY);
    if (v !== null) return v === '1';
  } catch {
    /* storage unavailable */
  }
  return window.matchMedia(PHONE).matches;
}

/** App shell shared with ABook/nopds: collapsible sidebar with navigation, the user's lists and account controls. */
export function Layout() {
  const { t, i18n } = useTranslation();
  const { user, ready, logout } = useAuth();
  const { theme, setTheme } = useTheme();
  const [collapsed, setCollapsed] = useState(initialCollapsed);
  const navigate = useNavigate();
  const location = useLocation();
  const lists = useQuery({ queryKey: ['lists'], queryFn: () => unwrap(api.GET('/api/lists')), enabled: !!user });

  useEffect(() => {
    try {
      localStorage.setItem(COLLAPSE_KEY, collapsed ? '1' : '0');
    } catch {
      /* storage unavailable */
    }
  }, [collapsed]);

  // On phones the expanded sidebar overlays content; close it when a link is followed.
  const closeOnPhone = () => {
    if (window.matchMedia(PHONE).matches) setCollapsed(true);
  };

  if (!ready) return null;
  if (!user) return <Navigate to="/login" replace />;

  const nav = [
    { to: '/', icon: '🛒', label: t('nav.lists'), end: true },
    { to: '/settings', icon: '⚙️', label: t('nav.settings') },
  ];
  const nextTheme = THEMES[(THEMES.indexOf(theme) + 1) % THEMES.length];
  const changeLanguage = async (lang: string) => {
    await i18n.changeLanguage(lang);
    rememberLanguage(lang);
  };

  return (
    <div className="flex h-dvh overflow-hidden">
      <a href="#main" className="sr-only btn-primary focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50">
        {t('nav.skip')}
      </a>
      <aside
        className={clsx(
          'z-30 flex shrink-0 flex-col overflow-hidden border-r border-line bg-surface transition-[width] duration-200',
          collapsed ? 'w-12' : 'w-[min(75vw,15rem)] max-md:absolute max-md:inset-y-0 max-md:left-0 max-md:shadow-2xl',
        )}
      >
        <div className="shrink-0 px-1 pt-1.5 pb-1">
          <button type="button" className="sidebar-btn" onClick={() => setCollapsed((v) => !v)} title={t('nav.menu')} aria-expanded={!collapsed}>
            <span className="w-[22px] shrink-0 text-center">☰</span>
            <Label collapsed={collapsed}>
              <span className="font-semibold">{t('app.name')}</span>
            </Label>
            {!collapsed && (
              <span className="shrink-0 rounded-full border border-accent/25 bg-accent-faint px-1.5 text-[0.65rem] leading-relaxed text-muted">
                v{__APP_VERSION__}
              </span>
            )}
          </button>
        </div>

        <nav className="flex-1 overflow-x-hidden overflow-y-auto px-1 py-0.5" aria-label={t('nav.main')}>
          {nav.map((n) => (
            <NavLink
              key={n.to}
              to={n.to}
              end={n.end}
              title={n.label}
              onClick={closeOnPhone}
              className={({ isActive }) => clsx('sidebar-btn', isActive && 'bg-accent-faint text-accent')}
            >
              <span className="w-[22px] shrink-0 text-center">{n.icon}</span>
              <Label collapsed={collapsed}>{n.label}</Label>
            </NavLink>
          ))}

          {!collapsed && (lists.data?.length ?? 0) > 0 && (
            <>
              <Divider />
              <div className="truncate px-2 pt-1.5 pb-0.5 caps">{t('nav.myLists')}</div>
              {lists.data!.map((l) => (
                <SidebarEntry
                  key={l.id}
                  icon="📋"
                  title={l.name}
                  subtitle={`${l.marketCode} · ${t('lists.items', { count: l.requirementCount })}`}
                  active={location.pathname === `/lists/${l.id}`}
                  onClick={() => {
                    closeOnPhone();
                    navigate(`/lists/${l.id}`);
                  }}
                />
              ))}
            </>
          )}
        </nav>

        <div className="shrink-0 border-t border-line px-1 pt-1 pb-2">
          {!collapsed && (
            <label className="block px-1 pb-1">
              <span className="sr-only">{t('settings.language')}</span>
              <select className="input py-1" value={i18n.language} onChange={(e) => void changeLanguage(e.target.value)}>
                {LANGUAGES.map((l) => (
                  <option key={l.code} value={l.code}>
                    🌐 {l.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          <button type="button" className="sidebar-btn" onClick={() => void logout()} title={`${t('auth.signOut')} (${user.email})`}>
            <span className="w-[22px] shrink-0 text-center">🚪</span>
            <Label collapsed={collapsed}>
              {t('auth.signOut')} <span className="muted">({user.email})</span>
            </Label>
          </button>
          <button type="button" className="sidebar-btn" onClick={() => setTheme(nextTheme)} title={`${t('theme.label')}: ${t(`theme.${theme}`)}`}>
            <span className="w-[22px] shrink-0 text-center">{theme === 'dark' ? '🌙' : theme === 'light' ? '☀️' : '◐'}</span>
            <Label collapsed={collapsed}>
              {t('theme.label')}: {t(`theme.${theme}`)}
            </Label>
          </button>
        </div>
      </aside>

      {!collapsed && <div className="fixed inset-0 z-20 bg-black/40 md:hidden" onClick={() => setCollapsed(true)} />}

      <main id="main" className="min-w-0 flex-1 overflow-y-auto bg-bg px-4 py-6 md:px-8">
        <div className="mx-auto max-w-6xl">
          <Outlet />
        </div>
      </main>
    </div>
  );
}

function Label({ collapsed, children }: { collapsed: boolean; children: ReactNode }) {
  return <span className={clsx('min-w-0 flex-1 truncate transition-opacity', collapsed && 'w-0 opacity-0')}>{children}</span>;
}

function Divider() {
  return <div className="mx-0.5 my-1 h-px bg-line" />;
}

function SidebarEntry({ icon, title, subtitle, active, onClick }: {
  icon: string;
  title: string;
  subtitle?: string;
  active?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      className={clsx(
        'flex w-full cursor-pointer items-start gap-1.5 overflow-hidden rounded-md px-2 py-1.5 text-left text-[0.82rem] leading-snug hover:bg-accent-faint',
        active && 'bg-accent-faint text-accent',
      )}
    >
      <span className="shrink-0 text-[0.85rem]">{icon}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate font-semibold">{title}</span>
        {subtitle && <span className="mt-px block truncate text-[0.76rem] text-muted">{subtitle}</span>}
      </span>
    </button>
  );
}
