import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errorMessage } from '../api/client';
import { Logo } from '../components/Logo';
import { useI18n } from '../i18n';

function AccountCard({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <div className="card w-full max-w-sm space-y-4 p-6">
        <div className="flex justify-center">
          <Logo className="h-10 w-10" />
        </div>
        {children}
      </div>
    </div>
  );
}

function Result({ result }: { result: { ok: boolean; text: string } | null }) {
  if (!result) return null;
  return (
    <p className={result.ok ? 'text-sm text-success' : 'text-sm text-danger'} role={result.ok ? 'status' : 'alert'}>
      {result.text}
    </p>
  );
}

/** Target of the link in the confirmation email. */
export function ConfirmEmailPage() {
  const { t } = useI18n();
  const [params] = useSearchParams();
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    const userId = params.get('userId') ?? '';
    const code = params.get('code') ?? '';
    void api.GET('/auth/confirm-email', { params: { query: { userId, code } } }).then(({ error, response }) =>
      setResult(response.ok ? { ok: true, text: t('auth.emailConfirmed') } : { ok: false, text: errorMessage(error) }),
    );
  }, [params, t]);

  return (
    <AccountCard>
      <Result result={result} />
      <Link to="/login" className="btn-primary w-full">{t('auth.signIn')}</Link>
    </AccountCard>
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
    const { error, response } = await api.POST('/auth/reset-password', {
      body: { email: params.get('email') ?? '', code: params.get('code') ?? '', newPassword: password },
    });
    setResult(response.ok ? { ok: true, text: t('auth.passwordChanged') } : { ok: false, text: errorMessage(error) });
  }

  return (
    <AccountCard>
      <form className="space-y-4" onSubmit={submit}>
        <h1 className="text-center text-xl font-semibold">{t('auth.resetPassword')}</h1>
        <label className="block">
          <span className="label">{t('auth.newPassword')}</span>
          <input className="input" type="password" autoComplete="new-password" required minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <Result result={result} />
        <button type="submit" className="btn-primary w-full">{t('common.save')}</button>
        <Link to="/login" className="block text-center text-sm link">{t('auth.signIn')}</Link>
      </form>
    </AccountCard>
  );
}
