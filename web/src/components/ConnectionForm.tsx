import { useState } from 'react';
import type { ConnectionDto, ConnectionInput, DbAuthMode } from '../api/types';

interface Props {
  initial: ConnectionDto | null;
  onSave: (input: ConnectionInput) => Promise<void>;
  onCancel: () => void;
}

export function ConnectionForm({ initial, onSave, onCancel }: Props) {
  const [form, setForm] = useState<ConnectionInput>({
    name: initial?.name ?? '',
    host: initial?.host ?? '',
    port: initial?.port ?? 1433,
    database: initial?.database ?? '',
    authMode: initial?.authMode ?? 'SqlAuth',
    userName: initial?.userName ?? '',
    password: null,
    readOnly: initial?.readOnly ?? false,
    trustServerCertificate: initial?.trustServerCertificate ?? false,
    timeoutSec: initial?.timeoutSec ?? 30,
  });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const set = <K extends keyof ConnectionInput>(key: K, value: ConnectionInput[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await onSave({
        ...form,
        database: form.database || null,
        userName: form.authMode === 'SqlAuth' ? form.userName || null : null,
        password: form.authMode === 'SqlAuth' ? form.password || null : null,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the connection.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="panel-form" onSubmit={submit}>
      <h3>{initial ? 'Edit connection' : 'New connection'}</h3>
      <label>
        Name
        <input value={form.name} onChange={(e) => set('name', e.target.value)} required />
      </label>
      <div className="row">
        <label className="grow">
          Host
          <input value={form.host} onChange={(e) => set('host', e.target.value)} required />
        </label>
        <label className="port">
          Port
          <input type="number" value={form.port ?? ''} onChange={(e) => set('port', e.target.value ? Number(e.target.value) : null)} />
        </label>
      </div>
      <label>
        Database
        <input value={form.database ?? ''} onChange={(e) => set('database', e.target.value)} />
      </label>
      <label>
        Authentication
        <select value={form.authMode} onChange={(e) => set('authMode', e.target.value as DbAuthMode)}>
          <option value="SqlAuth">SQL Server login</option>
          <option value="WindowsAuth">Windows integrated</option>
        </select>
      </label>
      {form.authMode === 'SqlAuth' && (
        <>
          <label>
            User name
            <input value={form.userName ?? ''} onChange={(e) => set('userName', e.target.value)} required />
          </label>
          <label>
            Password {initial?.hasPassword && <span className="hint">(leave blank to keep)</span>}
            <input
              type="password"
              value={form.password ?? ''}
              onChange={(e) => set('password', e.target.value)}
              autoComplete="new-password"
              required={!initial?.hasPassword}
            />
          </label>
        </>
      )}
      <label className="check">
        <input type="checkbox" checked={form.readOnly} onChange={(e) => set('readOnly', e.target.checked)} />
        Read-only (blocks edits and non-SELECT SQL)
      </label>
      <label className="check">
        <input
          type="checkbox"
          checked={form.trustServerCertificate}
          onChange={(e) => set('trustServerCertificate', e.target.checked)}
        />
        Trust server certificate (development only)
      </label>
      {error && <div className="error">{error}</div>}
      <div className="actions">
        <button type="submit" className="primary" disabled={busy}>
          Save
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}
