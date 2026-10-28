import { useState, type FormEvent } from 'react';
import { Navigate } from 'react-router';
import { errorMessage } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { useI18n } from '../i18n';

export function LoginPage() {
  const { t } = useI18n();
  const auth = useAuth();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<{ kind: 'error' | 'ok'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  if (auth.user) return <Navigate to="/" replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setMessage(null);
    if (mode === 'register') {
      const error = await auth.register(email, password);
      if (error) setMessage({ kind: 'error', text: errorMessage(error) });
      else {
        setMessage({ kind: 'ok', text: t('registered') });
        setMode('login');
      }
    } else {
      const error = await auth.login(email, password);
      if (error) setMessage({ kind: 'error', text: errorMessage(error) });
    }
    setBusy(false);
  }

  return (
    <main className="auth-page">
      <form className="card auth-card" onSubmit={submit}>
        <h1 className="brand">{t('appName')}</h1>
        <p className="muted">{t('tagline')}</p>
        <label>
          {t('email')}
          <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          {t('password')}
          <input
            type="password"
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>
        {message && <p className={message.kind === 'error' ? 'error' : 'ok'}>{message.text}</p>}
        <button type="submit" className="primary" disabled={busy}>
          {mode === 'login' ? t('signIn') : t('signUp')}
        </button>
        <p className="muted small">
          {mode === 'login' ? t('noAccount') : t('haveAccount')}{' '}
          <button type="button" className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
            {mode === 'login' ? t('signUp') : t('signIn')}
          </button>
        </p>
      </form>
    </main>
  );
}
