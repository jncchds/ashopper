import i18n from 'i18next';
import { initReactI18next, useTranslation } from 'react-i18next';
import en from '../locales/en.json';
import pl from '../locales/pl.json';
import uk from '../locales/uk.json';

/** UI languages. Adding one = a locale file + an entry here (and in scripts/check-locales.mjs). */
export const LANGUAGES = [
  { code: 'en', name: 'English', locale: 'en-GB' },
  { code: 'pl', name: 'Polski', locale: 'pl-PL' },
  { code: 'uk', name: 'Українська', locale: 'uk-UA' },
] as const;

const STORAGE_KEY = 'ashopper.lang';

/** Maps a BCP-47 tag to a supported UI language (ru/be → uk), or null. */
export function matchLanguage(tag?: string | null): string | null {
  if (!tag) return null;
  const primary = tag.toLowerCase().split(/[-_]/)[0];
  if (LANGUAGES.some((l) => l.code === primary)) return primary;
  if (primary === 'ru' || primary === 'be' || primary === 'ua') return 'uk';
  return null;
}

/** Saved choice first, then the browser's languages, then English. */
export function detectLanguage(): string {
  let stored: string | null = null;
  try {
    stored = localStorage.getItem(STORAGE_KEY);
  } catch {
    /* storage unavailable */
  }
  for (const candidate of [stored, ...(navigator.languages ?? []), navigator.language]) {
    const match = matchLanguage(candidate);
    if (match) return match;
  }
  return 'en';
}

export function rememberLanguage(lang: string) {
  try {
    localStorage.setItem(STORAGE_KEY, lang);
  } catch {
    /* storage unavailable */
  }
}

/** Intl locale for number/date formatting in the current UI language. */
export function localeOf(lang: string): string {
  return LANGUAGES.find((l) => l.code === lang)?.locale ?? 'en-GB';
}

/** t() plus the formatting locale, the two things every page needs. */
export function useI18n() {
  const { t, i18n: instance } = useTranslation();
  return { t, locale: localeOf(instance.language), lang: instance.language };
}

const initial = detectLanguage();
document.documentElement.lang = initial;

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, pl: { translation: pl }, uk: { translation: uk } },
  lng: initial,
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
});

i18n.on('languageChanged', (lng) => {
  document.documentElement.lang = lng;
});

export default i18n;
