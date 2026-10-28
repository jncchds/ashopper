import { useEffect, useSyncExternalStore } from 'react';

export type Theme = 'system' | 'light' | 'dark';
export const THEMES: Theme[] = ['system', 'light', 'dark'];
const KEY = 'ashopper.theme';

export function storedTheme(): Theme {
  try {
    const v = localStorage.getItem(KEY);
    return v === 'light' || v === 'dark' ? v : 'system';
  } catch {
    return 'system';
  }
}

/** Toggles the `dark` class on <html> (Tailwind's dark variant) and the browser chrome colour. */
export function applyTheme(theme: Theme) {
  const dark = theme === 'dark' || (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('dark', dark);
  document.querySelector('meta[name="theme-color"]:not([media])')?.setAttribute('content', dark ? '#18202a' : '#ffffff');
}

// One store for the whole app, so the sidebar button and the settings page never disagree.
let current: Theme = storedTheme();
const listeners = new Set<() => void>();

function setTheme(theme: Theme) {
  current = theme;
  applyTheme(theme);
  try {
    if (theme === 'system') localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, theme);
  } catch {
    /* storage unavailable */
  }
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** System / light / dark, as in the sibling apps; stored only when not "system". */
export function useTheme() {
  const theme = useSyncExternalStore(subscribe, () => current);
  // In "system" mode, follow the OS setting live.
  useEffect(() => {
    if (theme !== 'system') return;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => applyTheme('system');
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, [theme]);
  return { theme, setTheme };
}
