import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';

/**
 * Minimal i18n: a flat dictionary per language. Adding Ukrainian = adding a `uk` entry.
 * (Swap for i18next/FormatJS once plurals and lazy-loaded bundles are needed.)
 */
const en = {
  appName: 'ashopper',
  tagline: 'Know when to buy.',
  signIn: 'Sign in',
  signUp: 'Create account',
  signOut: 'Sign out',
  email: 'Email',
  password: 'Password',
  noAccount: 'No account yet?',
  haveAccount: 'Already have an account?',
  registered: 'Account created. You can sign in now.',
  lists: 'Shopping lists',
  newList: 'New list',
  listName: 'Name',
  market: 'Market',
  create: 'Create',
  cancel: 'Cancel',
  save: 'Save',
  delete: 'Delete',
  edit: 'Edit',
  items: 'items',
  noLists: 'No lists yet. Create one for a PC build, a flat, anything.',
  totalNow: 'Total now',
  incomplete: 'some items have no offer',
  priceHistory: 'Total price over time',
  days: 'days',
  lowest: 'Lowest',
  highest: 'Highest',
  item: 'Item',
  requirements: 'Requirements',
  matches: 'matches',
  bestOffer: 'Best offer',
  noOffer: 'No offer',
  addItem: 'Add item',
  editItem: 'Edit item',
  title: 'Title',
  category: 'Category',
  quantity: 'Quantity',
  any: 'Any',
  yes: 'Yes',
  no: 'No',
  min: 'min',
  max: 'max',
  contains: 'contains…',
  matchingProducts: 'Matching products',
  onlyThis: 'Only this one',
  pinned: 'Pinned to one product',
  unpin: 'Any matching product',
  shops: 'Shops',
  allShops: 'All shops',
  shopsHint: 'Only offers from selected shops count. None selected = all.',
  discounts: 'My shop discounts',
  discountsHint: 'Personal discounts are applied to every price from that shop.',
  settings: 'Settings',
  showTable: 'Show as table',
  hideTable: 'Hide table',
  day: 'Day',
  total: 'Total',
  back: 'Back',
  confirmDelete: 'Delete this?',
  emailConfirmed: 'Email confirmed.',
  resetPassword: 'Set a new password',
  newPassword: 'New password',
  passwordChanged: 'Password changed. Sign in with the new one.',
} as const;

type Dict = Record<keyof typeof en, string>;

const pl: Dict = {
  appName: 'ashopper',
  tagline: 'Wiedz, kiedy kupić.',
  signIn: 'Zaloguj się',
  signUp: 'Załóż konto',
  signOut: 'Wyloguj',
  email: 'E-mail',
  password: 'Hasło',
  noAccount: 'Nie masz konta?',
  haveAccount: 'Masz już konto?',
  registered: 'Konto utworzone. Możesz się zalogować.',
  lists: 'Listy zakupów',
  newList: 'Nowa lista',
  listName: 'Nazwa',
  market: 'Rynek',
  create: 'Utwórz',
  cancel: 'Anuluj',
  save: 'Zapisz',
  delete: 'Usuń',
  edit: 'Edytuj',
  items: 'pozycji',
  noLists: 'Brak list. Utwórz listę na zestaw PC, mieszkanie, cokolwiek.',
  totalNow: 'Razem teraz',
  incomplete: 'brak ofert dla niektórych pozycji',
  priceHistory: 'Cena całości w czasie',
  days: 'dni',
  lowest: 'Najniższa',
  highest: 'Najwyższa',
  item: 'Pozycja',
  requirements: 'Wymagania',
  matches: 'pasuje',
  bestOffer: 'Najlepsza oferta',
  noOffer: 'Brak oferty',
  addItem: 'Dodaj pozycję',
  editItem: 'Edytuj pozycję',
  title: 'Tytuł',
  category: 'Kategoria',
  quantity: 'Ilość',
  any: 'Dowolne',
  yes: 'Tak',
  no: 'Nie',
  min: 'min',
  max: 'maks',
  contains: 'zawiera…',
  matchingProducts: 'Pasujące produkty',
  onlyThis: 'Tylko ten',
  pinned: 'Przypięte do jednego produktu',
  unpin: 'Dowolny pasujący produkt',
  shops: 'Sklepy',
  allShops: 'Wszystkie sklepy',
  shopsHint: 'Liczą się tylko oferty z wybranych sklepów. Brak wyboru = wszystkie.',
  discounts: 'Moje rabaty w sklepach',
  discountsHint: 'Osobisty rabat jest stosowany do każdej ceny z danego sklepu.',
  settings: 'Ustawienia',
  showTable: 'Pokaż jako tabelę',
  hideTable: 'Ukryj tabelę',
  day: 'Dzień',
  total: 'Razem',
  back: 'Wstecz',
  confirmDelete: 'Usunąć?',
  emailConfirmed: 'E-mail potwierdzony.',
  resetPassword: 'Ustaw nowe hasło',
  newPassword: 'Nowe hasło',
  passwordChanged: 'Hasło zmienione. Zaloguj się nowym hasłem.',
};

const dictionaries = { en, pl } satisfies Record<string, Dict>;
export type Lang = keyof typeof dictionaries;
export type MessageKey = keyof typeof en;

interface I18n {
  lang: Lang;
  locale: string;
  setLang(lang: Lang): void;
  t(key: MessageKey): string;
}

const I18nContext = createContext<I18n | null>(null);
const STORAGE_KEY = 'ashopper.lang';

function initialLang(): Lang {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved && saved in dictionaries) return saved as Lang;
  } catch {
    /* storage unavailable */
  }
  return navigator.language.startsWith('pl') ? 'pl' : 'en';
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(initialLang);
  const value = useMemo<I18n>(
    () => ({
      lang,
      locale: lang === 'pl' ? 'pl-PL' : 'en-GB',
      setLang(next) {
        setLangState(next);
        try {
          localStorage.setItem(STORAGE_KEY, next);
        } catch {
          /* ignore */
        }
      },
      t: (key) => dictionaries[lang][key],
    }),
    [lang],
  );
  return <I18nContext value={value}>{children}</I18nContext>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error('useI18n must be used inside <I18nProvider>');
  return ctx;
}
