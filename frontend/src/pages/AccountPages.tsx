import { useEffect, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errorMessage } from '../api/client';
import { useI18n } from '../i18n';

/** Target of the link in the confirmation email. */
export function ConfirmEmailPage() {
  const { t } = useI18n();
  const [params] = useSearchParams();
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    const userId = params.get('userId') ?? '';
    const code = params.get('code') ?? '';
    void api.GET('/auth/confirm-email', { params: { query: { userId, code } } }).then(({ error }) =>
      setResult(error ? { ok: false, text: errorMessage(error) } : { ok: true, text: t('emailConfirmed') }),
    );
  }, [params, t]);

  return (
    <main className="auth-page">
      <div className="card auth-card">
        {result && <p className={result.ok ? 'ok' : 'error'}>{result.text}</p>}
        <Link to="/login">{t('signIn')}</Link>
      </div>
    </main>
  );
}

/** Target of the link in the password-reset email. */
export function ResetPasswordPage() {
  const { t } = useI18n();
  const [params] = useSearchParams();
  const [password, setPassword] = useState('');
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const { error } = await api.POST('/auth/reset-password', {
      body: { email: params.get('email') ?? '', code: params.get('code') ?? '', newPassword: password },
    });
    setResult(error ? { ok: false, text: errorMessage(error) } : { ok: true, text: t('passwordChanged') });
  }

  return (
    <main className="auth-page">
      <form className="card auth-card" onSubmit={submit}>
        <h1>{t('resetPassword')}</h1>
        <label>
          {t('newPassword')}
          <input type="password" autoComplete="new-password" required minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        {result && <p className={result.ok ? 'ok' : 'error'}>{result.text}</p>}
        <button type="submit" className="primary">{t('save')}</button>
        <Link to="/login">{t('signIn')}</Link>
      </form>
    </main>
  );
}
