import { useEffect, useRef, useState } from 'react';
import Editor from '@monaco-editor/react';
import { api, postJson } from '../api/client';
import type { QueryResponse, SavedQuery, SavedQueryInput } from '../api/types';

const MAX_DISPLAY_ROWS = 1000;

interface Props {
  connectionId: string;
  title: string;
  initialText: string;
  readOnly: boolean;
  active: boolean;
  canSave: boolean;
}

// Raw SQL editor (Monaco) with result sets and saved queries. Also used read-only for routine definitions.
export function SqlTab({ connectionId, title, initialText, readOnly, active, canSave }: Props) {
  const [text, setText] = useState(initialText);
  const [result, setResult] = useState<QueryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [saved, setSaved] = useState<SavedQuery[]>([]);
  const [savedId, setSavedId] = useState<string | null>(null);
  const [saveName, setSaveName] = useState('');
  const [saveFolder, setSaveFolder] = useState('');
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!canSave) return;
    api<SavedQuery[]>(`/api/saved-queries?connectionId=${connectionId}`)
      .then(setSaved)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load saved queries.'));
  }, [connectionId, canSave]);

  // Capture phase, so Monaco's own key bindings do not swallow Ctrl+Enter.
  useEffect(() => {
    const root = rootRef.current;
    if (!root) return;
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter' && !readOnly) {
        e.preventDefault();
        e.stopPropagation();
        void run();
      }
    };
    root.addEventListener('keydown', onKey, true);
    return () => root.removeEventListener('keydown', onKey, true);
  });

  async function run() {
    setRunning(true);
    setError(null);
    try {
      const response = await postJson<QueryResponse>(`/api/connections/${connectionId}/query`, { sql: text, timeoutSec: null, maxRows: null });
      setResult(response);
    } catch (err) {
      setResult(null);
      setError(err instanceof Error ? err.message : 'Query failed.');
    } finally {
      setRunning(false);
    }
  }

  function load(query: SavedQuery) {
    setSavedId(query.id);
    setText(query.sqlText);
    setSaveName(query.name);
    setSaveFolder(query.folder ?? '');
  }

  async function save() {
    const input: SavedQueryInput = { name: saveName, folder: saveFolder || null, sqlText: text, connectionId };
    try {
      const response = savedId
        ? await api<SavedQuery>(`/api/saved-queries/${savedId}`, { method: 'PUT', body: JSON.stringify(input) })
        : await postJson<SavedQuery>('/api/saved-queries', input);
      setSavedId(response.id);
      setSaved(await api<SavedQuery[]>(`/api/saved-queries?connectionId=${connectionId}`));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the query.');
    }
  }

  async function remove() {
    if (!savedId || !window.confirm('Delete this saved query?')) return;
    await api<void>(`/api/saved-queries/${savedId}`, { method: 'DELETE' });
    setSavedId(null);
    setSaved(await api<SavedQuery[]>(`/api/saved-queries?connectionId=${connectionId}`));
  }

  return (
    <div ref={rootRef} className="sql-tab" hidden={!active}>
      <div className="toolbar">
        <strong>{title}</strong>
        {readOnly && <span className="badge">read-only</span>}
        <span className="grow" />
        {!readOnly && (
          <button className="small primary" disabled={running || !text.trim()} onClick={run}>
            {running ? 'Running…' : 'Run (Ctrl+Enter)'}
          </button>
        )}
        <button className="small" onClick={() => navigator.clipboard.writeText(text)}>
          Copy
        </button>
      </div>

      <div className="sql-layout">
        {canSave && (
          <aside className="saved-list">
            <div className="section-title">
              Saved queries
              <button
                className="small"
                onClick={() => {
                  setSavedId(null);
                  setSaveName('');
                  setSaveFolder('');
                }}
              >
                New
              </button>
            </div>
            <ul>
              {saved.map((q) => (
                <li key={q.id} className={q.id === savedId ? 'selected' : ''}>
                  <button className="link-button" onClick={() => load(q)}>
                    {q.folder ? `${q.folder} / ` : ''}
                    {q.name}
                  </button>
                </li>
              ))}
            </ul>
            <input placeholder="Name" value={saveName} onChange={(e) => setSaveName(e.target.value)} />
            <input placeholder="Folder (optional)" value={saveFolder} onChange={(e) => setSaveFolder(e.target.value)} />
            <div className="actions">
              <button className="small primary" disabled={!saveName.trim() || !text.trim()} onClick={save}>
                {savedId ? 'Update' : 'Save'}
              </button>
              {savedId && (
                <button className="small danger" onClick={remove}>
                  Delete
                </button>
              )}
            </div>
          </aside>
        )}

        <div className="sql-main">
          <div className="editor-host">
            <Editor
              height="100%"
              defaultLanguage="sql"
              value={text}
              onChange={(value) => setText(value ?? '')}
              options={{ readOnly, minimap: { enabled: false }, fontSize: 13, scrollBeyondLastLine: false }}
              theme="vs"
            />
          </div>

          {error && <div className="error">{error}</div>}
          {result && (
            <div className="results">
              <div className="hint">
                {result.resultSets.length} result set(s) · {result.rowsAffected} row(s) affected · {result.durationMs} ms
              </div>
              {result.resultSets.map((rs, i) => (
                <div key={i} className="result-set">
                  <div className="hint">
                    Result {i + 1} · {rs.rows.length.toLocaleString()} row(s){rs.truncated ? ' (truncated)' : ''}
                  </div>
                  <div className="result-scroll">
                    <table className="result-table">
                      <thead>
                        <tr>
                          {rs.columns.map((c, j) => (
                            <th key={j} title={c.sqlType}>
                              {c.name}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {rs.rows.slice(0, MAX_DISPLAY_ROWS).map((row, r) => (
                          <tr key={r}>
                            {row.map((v, c) => (
                              <td key={c}>{v === null ? <i>NULL</i> : String(v)}</td>
                            ))}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {rs.rows.length > MAX_DISPLAY_ROWS && (
                    <div className="hint">Showing the first {MAX_DISPLAY_ROWS.toLocaleString()} rows.</div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
