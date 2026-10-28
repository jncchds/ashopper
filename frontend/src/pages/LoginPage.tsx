import { useState, type FormEvent } from 'react';
import { Navigate } from 'react-router';
import { errorMessage } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { Logo } from '../components/Logo';
import { useI18n } from '../i18n';

export function LoginPage() {
  const { t } = useI18n();
  const auth = useAuth();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<{ kind: 'error' | 'ok'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  if (!auth.ready) return null;
  if (auth.user) return <Navigate to="/" replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setMessage(null);
    if (mode === 'register') {
      const error = await auth.register(email, password);
      if (error) setMessage({ kind: 'error', text: errorMessage(error) });
      else {
        setMessage({ kind: 'ok', text: t('auth.registered') });
        setMode('login');
      }
    } else {
      const error = await auth.login(email, password);
      if (error) setMessage({ kind: 'error', text: errorMessage(error) });
    }
    setBusy(false);
  }

  return (
    <div className="flex min-h-dvh items-center justify-center px-4">
      <form className="card w-full max-w-sm space-y-4 p-6" onSubmit={submit}>
        <div className="flex flex-col items-center gap-2 text-center">
          <Logo className="h-12 w-12" />
          <h1 className="text-2xl font-semibold">{t('app.name')}</h1>
          <p className="text-sm muted">{mode === 'login' ? t('auth.signInPrompt') : t('auth.signUpPrompt')}</p>
        </div>
        <label className="block">
          <span className="label">{t('auth.email')}</span>
          <input className="input" type="email" autoComplete="email" autoFocus required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label className="block">
          <span className="label">{t('auth.password')}</span>
          <input
            className="input"
            type="password"
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>
        {message && (
          <p className={message.kind === 'error' ? 'text-sm text-danger' : 'text-sm text-success'} role={message.kind === 'error' ? 'alert' : 'status'}>
            {message.text}
          </p>
        )}
        <button type="submit" className="btn-primary w-full" disabled={busy}>
          {mode === 'login' ? t('auth.signIn') : t('auth.signUp')}
        </button>
        <p className="text-center text-sm muted">
          {mode === 'login' ? t('auth.noAccount') : t('auth.haveAccount')}{' '}
          <button type="button" className="link cursor-pointer" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
            {mode === 'login' ? t('auth.signUp') : t('auth.signIn')}
          </button>
        </p>
      </form>
    </div>
  );
}
