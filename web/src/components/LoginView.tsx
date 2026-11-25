import { useState } from 'react';
import { api, setSession, type Session } from '../api/client';

interface Props {
  onSignedIn: (session: Session) => void;
}

// Register is open only for the first account on a new install, or when the server allows it.
export function LoginView({ onSignedIn }: Props) {
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (mode === 'register') {
        await api<void>('/api/auth/register', { method: 'POST', body: JSON.stringify({ userName, password }) });
      }
      const session = await api<Session>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ userName, password }),
      });
      setSession(session);
      onSignedIn(session);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login">
      <form className="login-card" onSubmit={submit}>
        <h1>DbExplorer</h1>
        <label>
          User name
          <input value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="username" required />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
            required
          />
        </label>
        {error && <div className="error">{error}</div>}
        <button type="submit" className="primary" disabled={busy}>
          {mode === 'register' ? 'Create account and sign in' : 'Sign in'}
        </button>
        <button type="button" className="link" onClick={() => setMode(mode === 'login' ? 'register' : 'login')}>
          {mode === 'login' ? 'First time? Create an account' : 'Back to sign in'}
        </button>
      </form>
    </div>
  );
}
