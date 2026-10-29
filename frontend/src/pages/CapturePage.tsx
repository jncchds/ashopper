import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import { useMutation } from '@tanstack/react-query';
import { api, unwrap } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { readCapture } from '../bookmarklet';
import { Spinner } from '../components/ui';
import { formatMoney } from '../format';
import { useI18n } from '../i18n';

/**
 * Opened by the "Send to ashopper" bookmark on a store page. Saves the price the user's browser saw.
 * Outside the app layout, so a missing session shows a hint here instead of redirecting and losing the data.
 */
export function CapturePage() {
  const { t, locale } = useI18n();
  const { user, ready } = useAuth();
  // Read once, then drop it from the address bar so a reload doesn't send it again.
  const [payload] = useState(() => readCapture(window.location.hash));
  useEffect(() => {
    if (window.location.hash) history.replaceState(null, '', window.location.pathname);
  }, []);

  const save = useMutation({
    mutationFn: () => unwrap(api.POST('/api/tracked/capture', { body: payload! })),
  });
  const started = useRef(false);
  useEffect(() => {
    if (ready && user && payload && !started.current) {
      started.current = true; // StrictMode runs effects twice in development
      save.mutate();
    }
  }, [ready, user, payload, save]);

  let content;
  if (!payload) content = <p>{t('capture.noData')}</p>;
  else if (!ready || save.isPending || (user && save.isIdle)) content = <p className="flex items-center gap-2"><Spinner className="h-5 w-5" /> {t('capture.saving')}</p>;
  else if (!user) content = <p>{t('capture.signIn')} <Link className="link" to="/login">{t('auth.signIn')}</Link></p>;
  else if (save.error) content = <p className="text-danger" role="alert">{save.error.message}</p>;
  else if (save.data)
    content = (
      <div className="space-y-1">
        <p className="font-semibold">{save.data.title}</p>
        <p>
          {t('capture.saved', {
            price: save.data.price != null && save.data.currency ? formatMoney(save.data.price, save.data.currency, locale) : '—',
            store: save.data.storeName,
          })}
        </p>
        <p className="text-sm muted">{t('capture.closeHint')}</p>
      </div>
    );

  return (
    <main className="flex min-h-dvh items-center justify-center bg-bg p-4">
      <section className="card w-full max-w-md space-y-4 p-6">
        <h1 className="text-lg font-bold">{t('capture.title')}</h1>
        {content}
        <div className="flex justify-end gap-2">
          <Link className="btn-secondary" to="/tracked">{t('capture.toTracked')}</Link>
          <button type="button" className="btn-primary" onClick={() => window.close()}>{t('common.close')}</button>
        </div>
      </section>
    </main>
  );
}
